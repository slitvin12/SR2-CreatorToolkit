using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CreatorToolkitAgentDirector;

/// <summary>A UI element drawn on annotated screenshots (screen pixels, top-left origin).</summary>
internal struct Mark
{
    internal string Tag;   // "12" for Creator Toolkit IMGUI ids, "c3" for native uGUI selectables
    internal string Kind, Label;
    internal float X, Y, W, H;
}

internal sealed class ViewInfo
{
    internal string Path;
    internal int Width, Height, Drawn;
    internal float Factor; // view pixels per screen pixel
}

/// <summary>
/// Minimal managed PNG codec (8-bit RGB/RGBA, non-interlaced: what ScreenCapture writes) plus overlay
/// drawing. Pure .NET, no Unity calls, so it runs on a worker thread without touching the frame.
/// </summary>
internal sealed class RgbImage
{
    internal readonly int Width, Height;
    internal readonly byte[] Px; // RGB, 3 bytes per pixel, rows top to bottom

    internal RgbImage(int width, int height)
    {
        Width = width; Height = height;
        Px = new byte[checked(width * height * 3)];
    }

    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly uint[] CrcTable = MakeCrcTable();

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc(byte[] type, byte[] data, int count)
    {
        uint c = 0xFFFFFFFFu;
        for (int i = 0; i < type.Length; i++) c = CrcTable[(c ^ type[i]) & 0xFF] ^ (c >> 8);
        for (int i = 0; i < count; i++) c = CrcTable[(c ^ data[i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static int ReadBE(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];

    private static void WriteBE(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    internal static RgbImage Decode(byte[] png)
    {
        if (png == null || png.Length < 45) throw new InvalidDataException("not a PNG");
        for (int i = 0; i < 8; i++) if (png[i] != Signature[i]) throw new InvalidDataException("not a PNG");
        int pos = 8, width = 0, height = 0, depth = 0, color = -1, interlace = 0;
        var idat = new MemoryStream();
        while (pos + 12 <= png.Length)
        {
            int length = ReadBE(png, pos);
            if (length < 0 || pos + 12L + length > png.Length) throw new InvalidDataException("truncated PNG chunk");
            string type = Encoding.ASCII.GetString(png, pos + 4, 4);
            int data = pos + 8;
            if (type == "IHDR" && length >= 13)
            {
                width = ReadBE(png, data); height = ReadBE(png, data + 4);
                depth = png[data + 8]; color = png[data + 9]; interlace = png[data + 12];
            }
            else if (type == "IDAT") idat.Write(png, data, length);
            else if (type == "IEND") break;
            pos += 12 + length;
        }
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384) throw new InvalidDataException("bad PNG size " + width + "x" + height);
        if (depth != 8 || (color != 2 && color != 6) || interlace != 0)
            throw new NotSupportedException("PNG depth=" + depth + " color=" + color + " interlace=" + interlace + " (нужен 8-bit RGB/RGBA)");
        int bpp = color == 6 ? 4 : 3, stride = width * bpp;
        var raw = new byte[(long)(stride + 1) * height];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            int read = 0;
            while (read < raw.Length)
            {
                int n = z.Read(raw, read, raw.Length - read);
                if (n <= 0) break;
                read += n;
            }
            if (read < raw.Length) throw new InvalidDataException("short PNG image data");
        }
        var image = new RgbImage(width, height);
        var prev = new byte[stride];
        var cur = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * (stride + 1);
            int filter = raw[rowStart];
            Buffer.BlockCopy(raw, rowStart + 1, cur, 0, stride);
            switch (filter)
            {
                case 0: break;
                case 1: for (int i = bpp; i < stride; i++) cur[i] = (byte)(cur[i] + cur[i - bpp]); break;
                case 2: for (int i = 0; i < stride; i++) cur[i] = (byte)(cur[i] + prev[i]); break;
                case 3:
                    for (int i = 0; i < stride; i++)
                        cur[i] = (byte)(cur[i] + (((i >= bpp ? cur[i - bpp] : 0) + prev[i]) >> 1));
                    break;
                case 4:
                    for (int i = 0; i < stride; i++)
                    {
                        int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                        cur[i] = (byte)(cur[i] + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c));
                    }
                    break;
                default: throw new InvalidDataException("bad PNG filter " + filter);
            }
            int o = y * width * 3;
            if (bpp == 3) Buffer.BlockCopy(cur, 0, image.Px, o, stride);
            else for (int s = 0; s < stride; s += 4) { image.Px[o++] = cur[s]; image.Px[o++] = cur[s + 1]; image.Px[o++] = cur[s + 2]; }
            var t = prev; prev = cur; cur = t;
        }
        return image;
    }

    internal byte[] Encode()
    {
        int stride = Width * 3;
        var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            var row = new byte[stride + 1];
            row[0] = 1; // Sub filter: cheap and compresses UI screenshots well
            for (int y = 0; y < Height; y++)
            {
                int o = y * stride;
                for (int i = 0; i < 3; i++) row[i + 1] = Px[o + i];
                for (int i = 3; i < stride; i++) row[i + 1] = (byte)(Px[o + i] - Px[o + i - 3]);
                z.Write(row, 0, row.Length);
            }
        }
        var output = new MemoryStream((int)compressed.Length + 64);
        output.Write(Signature, 0, 8);
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)Width); WriteBE(ihdr, 4, (uint)Height);
        ihdr[8] = 8; ihdr[9] = 2; // 8-bit truecolor, deflate, no interlace
        Chunk(output, "IHDR", ihdr, 13);
        Chunk(output, "IDAT", compressed.GetBuffer(), (int)compressed.Length);
        Chunk(output, "IEND", Array.Empty<byte>(), 0);
        return output.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data, int count)
    {
        var head = new byte[4];
        WriteBE(head, 0, (uint)count);
        s.Write(head, 0, 4);
        var t = Encoding.ASCII.GetBytes(type);
        s.Write(t, 0, 4);
        if (count > 0) s.Write(data, 0, count);
        WriteBE(head, 0, Crc(t, data, count));
        s.Write(head, 0, 4);
    }

    /// <summary>Box-filter downscale (factor in (0,1]).</summary>
    internal RgbImage Scaled(float factor)
    {
        if (factor >= 0.999f) return this;
        int w = Math.Max(1, (int)Math.Round(Width * factor)), h = Math.Max(1, (int)Math.Round(Height * factor));
        var dst = new RgbImage(w, h);
        double sx = (double)Width / w, sy = (double)Height / h;
        for (int y = 0; y < h; y++)
        {
            int y0 = (int)(y * sy), y1 = Math.Min(Height, Math.Max(y0 + 1, (int)((y + 1) * sy)));
            for (int x = 0; x < w; x++)
            {
                int x0 = (int)(x * sx), x1 = Math.Min(Width, Math.Max(x0 + 1, (int)((x + 1) * sx)));
                int r = 0, g = 0, b = 0, n = 0;
                for (int yy = y0; yy < y1; yy++)
                {
                    int o = (yy * Width + x0) * 3;
                    for (int xx = x0; xx < x1; xx++, o += 3) { r += Px[o]; g += Px[o + 1]; b += Px[o + 2]; n++; }
                }
                int d = (y * w + x) * 3;
                dst.Px[d] = (byte)(r / n); dst.Px[d + 1] = (byte)(g / n); dst.Px[d + 2] = (byte)(b / n);
            }
        }
        return dst;
    }

    // ---- drawing --------------------------------------------------------------------------------
    internal void Fill(int x, int y, int w, int h, int rgb, int alpha = 256)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(Width, x + w), y1 = Math.Min(Height, y + h);
        byte r = (byte)(rgb >> 16), g = (byte)(rgb >> 8), b = (byte)rgb;
        for (int yy = y0; yy < y1; yy++)
        {
            int o = (yy * Width + x0) * 3;
            for (int xx = x0; xx < x1; xx++, o += 3)
            {
                if (alpha >= 256) { Px[o] = r; Px[o + 1] = g; Px[o + 2] = b; }
                else
                {
                    int ia = 256 - alpha;
                    Px[o] = (byte)((Px[o] * ia + r * alpha) >> 8);
                    Px[o + 1] = (byte)((Px[o + 1] * ia + g * alpha) >> 8);
                    Px[o + 2] = (byte)((Px[o + 2] * ia + b * alpha) >> 8);
                }
            }
        }
    }

    internal void Stroke(int x, int y, int w, int h, int thickness, int rgb)
    {
        Fill(x, y, w, thickness, rgb);
        Fill(x, y + h - thickness, w, thickness, rgb);
        Fill(x, y, thickness, h, rgb);
        Fill(x + w - thickness, y, thickness, h, rgb);
    }

    // 3x5 glyphs, one row per 3 bits (MSB = left column).
    private static readonly Dictionary<char, byte[]> Glyphs = new()
    {
        ['0'] = new byte[] { 7, 5, 5, 5, 7 }, ['1'] = new byte[] { 2, 6, 2, 2, 7 }, ['2'] = new byte[] { 7, 1, 7, 4, 7 },
        ['3'] = new byte[] { 7, 1, 7, 1, 7 }, ['4'] = new byte[] { 5, 5, 7, 1, 1 }, ['5'] = new byte[] { 7, 4, 7, 1, 7 },
        ['6'] = new byte[] { 7, 4, 7, 5, 7 }, ['7'] = new byte[] { 7, 1, 1, 1, 1 }, ['8'] = new byte[] { 7, 5, 7, 5, 7 },
        ['9'] = new byte[] { 7, 5, 7, 1, 7 }, ['c'] = new byte[] { 0, 7, 4, 4, 7 }, ['x'] = new byte[] { 0, 5, 2, 5, 5 },
        ['y'] = new byte[] { 5, 5, 7, 1, 7 }, ['-'] = new byte[] { 0, 0, 7, 0, 0 }, [','] = new byte[] { 0, 0, 0, 2, 4 }
    };

    internal static int TextWidth(string text, int scale) => text.Length == 0 ? 0 : text.Length * 4 * scale - scale;
    internal static int TextHeight(int scale) => 5 * scale;

    internal void Text(int x, int y, string text, int scale, int rgb)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (!Glyphs.TryGetValue(text[i], out var rows)) continue;
            int gx = x + i * 4 * scale;
            for (int row = 0; row < 5; row++)
                for (int col = 0; col < 3; col++)
                    if ((rows[row] & (4 >> col)) != 0) Fill(gx + col * scale, y + row * scale, scale, scale, rgb);
        }
    }

    /// <summary>Filled label box with a 1px dark border; text color picked for contrast.</summary>
    internal void Tag(int x, int y, string text, int scale, int background)
    {
        int pad = Math.Max(2, scale), w = TextWidth(text, scale) + pad * 2, h = TextHeight(scale) + pad * 2;
        x = Math.Clamp(x, 0, Math.Max(0, Width - w));
        y = Math.Clamp(y, 0, Math.Max(0, Height - h));
        Fill(x - 1, y - 1, w + 2, h + 2, 0x000000);
        Fill(x, y, w, h, background);
        int r = (background >> 16) & 255, g = (background >> 8) & 255, b = background & 255;
        int fg = r * 299 + g * 587 + b * 114 > 150000 ? 0x000000 : 0xFFFFFF;
        Text(x + pad, y + pad, text, scale, fg);
    }
}

/// <summary>Renders "set-of-marks" views: numbered boxes on every clickable element, optional coordinate grid.</summary>
internal static class Annotator
{
    internal static int ColorFor(string kind) => kind switch
    {
        "button" => 0xFF4040, "tab" => 0xFFA000, "icon" => 0x20A0FF, "toggle" => 0x20D060,
        "category" => 0xB060FF, "role" => 0xFF40C0, "canvas" => 0x00E0E0, _ => 0xFFE000
    };

    /// <param name="screenW">Screen width the marks were measured in (screenshot may be supersampled).</param>
    internal static ViewInfo Render(string source, string target, List<Mark> marks, int screenW, int screenH, float scale, int grid)
    {
        var image = RgbImage.Decode(File.ReadAllBytes(source));
        if (scale < 0.999f) image = image.Scaled(scale);
        float fx = screenW > 0 ? (float)image.Width / screenW : 1f, fy = screenH > 0 ? (float)image.Height / screenH : fx;
        int fontScale = Math.Max(2, (int)Math.Round(image.Width / 960f));
        if (grid > 0)
        {
            int step = Math.Max(20, grid);
            for (int gx = step; gx < screenW; gx += step)
            {
                int px = (int)Math.Round(gx * fx);
                image.Fill(px, 0, 1, image.Height, 0xFFFF00, 110);
            }
            for (int gy = step; gy < screenH; gy += step)
            {
                int py = (int)Math.Round(gy * fy);
                image.Fill(0, py, image.Width, 1, 0xFFFF00, 110);
            }
            for (int gx = step; gx < screenW; gx += step)
                image.Tag((int)Math.Round(gx * fx) + 2, 0, gx.ToString(), fontScale, 0x202020);
            for (int gy = step; gy < screenH; gy += step)
                image.Tag(0, (int)Math.Round(gy * fy) + 2, gy.ToString(), fontScale, 0x202020);
        }
        int drawn = 0;
        var placed = new List<(int x, int y, string tag, int color)>();
        if (marks != null)
        {
            foreach (var m in marks)
            {
                if (drawn >= 600) break;
                int x = (int)Math.Round(m.X * fx), y = (int)Math.Round(m.Y * fy);
                int w = (int)Math.Round(m.W * fx), h = (int)Math.Round(m.H * fy);
                if (w < 3 || h < 3 || x >= image.Width || y >= image.Height || x + w <= 0 || y + h <= 0) continue;
                int color = ColorFor(m.Kind);
                image.Stroke(x, y, w, h, w > 12 && h > 12 ? 2 : 1, color);
                int tagH = RgbImage.TextHeight(fontScale) + Math.Max(2, fontScale) * 2 + 2;
                placed.Add((x, y - tagH >= 0 ? y - tagH : y, m.Tag, color));
                drawn++;
            }
        }
        foreach (var p in placed) image.Tag(p.x, p.y, p.tag, fontScale, p.color); // tags last: always on top
        byte[] bytes = image.Encode();
        string temp = target + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, target, true);
        return new ViewInfo { Path = target, Width = image.Width, Height = image.Height, Drawn = drawn, Factor = fx };
    }
}
