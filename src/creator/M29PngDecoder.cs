using System;
using System.IO;
using System.IO.Compression;
using System.Text;
namespace CreatorToolkit;

// M29: managed PNG reader for the saved copy of the native player marker (player-marker-N.png, M28).
// In this game build ImageConversion.LoadImage fails before it runs (Il2CppInterop has no
// ReadOnlySpan.GetPinnableReference), so the copy never loaded and every start showed the bare
// arrow on the minimap until the atlas was opened. Reads 8-bit non-interlaced grey, grey+alpha,
// RGB and RGBA (what EncodeToPNG writes) into the GetPixels32 order the uploader expects:
// bottom row first, each pixel packed r | g<<8 | b<<16 | a<<24.
internal static class M29PngDecoder
{
    internal const int MaxSide=4096;
    private static readonly byte[] Signature={137,80,78,71,13,10,26,10};

    internal static uint[] Decode(byte[] png,out int width,out int height)
    {
        width=height=0;
        if(png==null||png.Length<8+25)throw new InvalidDataException("PNG: файл слишком короткий");
        for(int i=0;i<Signature.Length;i++)if(png[i]!=Signature[i])throw new InvalidDataException("PNG: нет сигнатуры");
        int at=8,channels=0;bool header=false;
        using var idat=new MemoryStream();
        while(at+12<=png.Length)
        {
            int length=ReadInt(png,at);
            if(length<0||at+12L+length>png.Length)throw new InvalidDataException("PNG: обрезанный блок");
            string type=Encoding.ASCII.GetString(png,at+4,4);
            int data=at+8;
            if(type=="IHDR")
            {
                if(length<13)throw new InvalidDataException("PNG: короткий IHDR");
                width=ReadInt(png,data);height=ReadInt(png,data+4);
                int depth=png[data+8],color=png[data+9];
                if(width<=0||height<=0||width>MaxSide||height>MaxSide)throw new InvalidDataException("PNG: размер "+width+"x"+height);
                if(depth!=8||png[data+10]!=0||png[data+11]!=0||png[data+12]!=0)throw new InvalidDataException("PNG: нужен 8-битный без чересстрочности");
                channels=color switch{0=>1,2=>3,4=>2,6=>4,_=>0};
                if(channels==0)throw new InvalidDataException("PNG: тип цвета "+color);
                header=true;
            }
            else if(type=="IDAT")
            {
                if(!header)throw new InvalidDataException("PNG: IDAT до IHDR");
                idat.Write(png,data,length);
            }
            else if(type=="IEND")break;
            at=data+length+4;
        }
        if(!header||idat.Length<2)throw new InvalidDataException("PNG: нет данных изображения");
        int stride=width*channels;
        var raw=new byte[(long)height*(stride+1)];
        idat.Position=0;
        using(var inflate=new ZLibStream(idat,CompressionMode.Decompress))
        {
            int read=0;
            while(read<raw.Length){int n=inflate.Read(raw,read,raw.Length-read);if(n<=0)break;read+=n;}
            if(read<raw.Length)throw new InvalidDataException("PNG: данных меньше, чем строк");
        }
        var pixels=new uint[width*height];
        var previous=new byte[stride];var line=new byte[stride];
        for(int row=0;row<height;row++)
        {
            int offset=row*(stride+1);
            Buffer.BlockCopy(raw,offset+1,line,0,stride);
            Unfilter(raw[offset],line,previous,channels);
            // PNG rows run top to bottom; Unity textures start at the bottom row.
            int target=(height-1-row)*width;
            for(int x=0;x<width;x++)
            {
                int p=x*channels;byte r,g,b,a;
                switch(channels)
                {
                    case 1:r=g=b=line[p];a=255;break;
                    case 2:r=g=b=line[p];a=line[p+1];break;
                    case 3:r=line[p];g=line[p+1];b=line[p+2];a=255;break;
                    default:r=line[p];g=line[p+1];b=line[p+2];a=line[p+3];break;
                }
                pixels[target+x]=(uint)(r|g<<8|b<<16|a<<24);
            }
            (previous,line)=(line,previous);
        }
        return pixels;
    }

    // The five PNG row filters (none, sub, up, average, Paeth); the row above the first is zeros.
    private static void Unfilter(int kind,byte[] line,byte[] previous,int bpp)
    {
        int n=line.Length;
        switch(kind)
        {
            case 0:return;
            case 1:for(int i=bpp;i<n;i++)line[i]=(byte)(line[i]+line[i-bpp]);return;
            case 2:for(int i=0;i<n;i++)line[i]=(byte)(line[i]+previous[i]);return;
            case 3:for(int i=0;i<n;i++)line[i]=(byte)(line[i]+((i>=bpp?line[i-bpp]:0)+previous[i])/2);return;
            case 4:
                for(int i=0;i<n;i++)
                {
                    int a=i>=bpp?line[i-bpp]:0,b=previous[i],c=i>=bpp?previous[i-bpp]:0;
                    line[i]=(byte)(line[i]+Paeth(a,b,c));
                }
                return;
            default:throw new InvalidDataException("PNG: фильтр строки "+kind);
        }
    }
    private static int Paeth(int a,int b,int c)
    {
        int p=a+b-c,pa=Math.Abs(p-a),pb=Math.Abs(p-b),pc=Math.Abs(p-c);
        return pa<=pb&&pa<=pc?a:pb<=pc?b:c;
    }
    private static int ReadInt(byte[] bytes,int at)=>bytes[at]<<24|bytes[at+1]<<16|bytes[at+2]<<8|bytes[at+3];
}
