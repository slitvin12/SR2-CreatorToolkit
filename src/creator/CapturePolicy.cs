using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CreatorToolkit;

// Owner request 2026-10-29: ideas seen on Nexus (Smooth Zoom, Fov Changer, Interpolate All Rigid Bodies,
// FPS Counter, Background FPS Limiter, Name Your Save, Placement Improvements), written from scratch.
// Pure rules only: no Unity or native types, covered by the offline checks.
internal static class CapturePolicy
{
    // ── Camera: own FOV and smooth zoom ─────────────────────────────────────────────
    internal const float MinFov = 30f, MaxFov = 120f, DefaultFov = 75f;
    internal const float MinZoom = 1.5f, MaxZoom = 8f, DefaultZoom = 4f;
    internal const float ZoomSeconds = .12f; // time constant of the smooth zoom approach
    internal static readonly string[] ZoomKeys = { "C", "Z", "V", "СКМ", "M4" };

    internal static float Fov(float value) => float.IsFinite(value) ? MathF.Round(Math.Clamp(value, MinFov, MaxFov)) : DefaultFov;
    internal static float Zoom(float value) => float.IsFinite(value) ? MathF.Round(Math.Clamp(value, MinZoom, MaxZoom) * 2f) / 2f : DefaultZoom;
    internal static int ZoomKey(int value) => value >= 0 && value < ZoomKeys.Length ? value : 0;

    // Exponential approach to the target: frame-rate independent and settles exactly.
    internal static float Step(float current, float target, float dt)
    {
        if (!float.IsFinite(current)) return target;
        if (!float.IsFinite(dt) || dt <= 0) return current;
        float next = current + (target - current) * (1f - MathF.Exp(-dt / ZoomSeconds));
        return MathF.Abs(next - target) < .002f ? target : next;
    }

    // Optical zoom: the tangent of the half-angle shrinks by the factor (not a linear FOV cut).
    // A game FOV outside the slider range is followed as is, only invalid values fall back.
    internal static float ZoomedFov(float baseFov, float factor, float t)
    {
        if (!float.IsFinite(baseFov) || baseFov <= 1f || baseFov >= 179f) baseFov = DefaultFov;
        t = Math.Clamp(float.IsFinite(t) ? t : 0f, 0f, 1f);
        if (t <= 0f) return baseFov;
        double z = 1.0 + (Zoom(factor) - 1.0) * t;
        double half = Math.Tan(baseFov * Math.PI / 360.0) / z;
        return (float)(Math.Atan(half) * 360.0 / Math.PI);
    }

    // Someone else (game settings, sprint or shake effects, another mod) wrote the camera FOV.
    internal static bool ExternalChange(float observed, float written) =>
        float.IsFinite(written) && float.IsFinite(observed) && MathF.Abs(observed - written) > .05f;

    // ── FPS counter ─────────────────────────────────────────────────────────────────
    internal const float FpsWindow = .5f;
    internal static readonly string[] FpsCorners = { "Слева сверху", "Справа сверху", "Слева снизу", "Справа снизу" };
    internal static int Corner(int value) => value >= 0 && value < FpsCorners.Length ? value : 1;
    internal static string FpsLabel(int frames, float seconds, float worst)
    {
        if (frames <= 0 || !(seconds > 0f) || !float.IsFinite(seconds)) return "FPS —";
        double fps = frames / (double)seconds, ms = seconds * 1000.0 / frames;
        string text = "FPS " + Math.Round(fps).ToString(CultureInfo.InvariantCulture) + " · " + ms.ToString("0.0", CultureInfo.InvariantCulture) + " мс";
        if (float.IsFinite(worst) && worst * 1000f > ms * 1.5 && worst > 0f)
            text += " · пик " + (worst * 1000f).ToString("0", CultureInfo.InvariantCulture) + " мс";
        return text;
    }

    // ── Background FPS limit ────────────────────────────────────────────────────────
    internal static readonly int[] BackgroundRates = { 10, 15, 30, 60 };
    internal const int DefaultBackgroundFps = 30;
    internal static int BackgroundFps(int value) => BackgroundRates.Contains(value) ? value : DefaultBackgroundFps;
    // Only while the window has lost focus; a running FPS measurement keeps its own cap.
    internal static bool LimitBackground(bool enabled, bool focused, bool measuring) => enabled && !focused && !measuring;

    // ── Settings file: UserData\CreatorToolkit\capture.cfg (key=value) ────────────
    internal static readonly string[] SettingKeys =
        { "fov.enabled", "fov.value", "zoom.enabled", "zoom.key", "zoom.factor", "physics.smooth", "fps.show", "fps.corner", "background.limit", "background.fps" };
    internal static Dictionary<string, string> ParseSettings(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in (text ?? "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
            if (Array.IndexOf(SettingKeys, key) >= 0) result[key] = value;
        }
        return result;
    }
    internal static bool Flag(IReadOnlyDictionary<string, string> s, string key) => s.TryGetValue(key, out var v) && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
    internal static float Number(IReadOnlyDictionary<string, string> s, string key, float fallback) =>
        s.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && float.IsFinite(f) ? f : fallback;
    internal static string Line(string key, bool value) => key + "=" + (value ? "1" : "0");
    internal static string Line(string key, float value) => key + "=" + value.ToString("0.##", CultureInfo.InvariantCulture);

    // ── Save names (main menu «Файл N» → own name) ─────────────────────────────────
    internal const int MaxSaveName = 28;
    internal static string CleanName(string value)
    {
        var sb = new StringBuilder();
        bool space = false;
        foreach (char c in value ?? "")
        {
            if (char.IsControl(c) || c == '<' || c == '>') { space = sb.Length > 0; continue; } // no TMP rich-text tags
            if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
            if (sb.Length >= MaxSaveName) break;
        }
        return sb.ToString().Trim();
    }
    // Native game names look like "20261013155400_5": creation time + "_" + slot number (1-based).
    private static readonly Regex GameNamePattern = new(@"^\d{14}_(\d{1,2})$", RegexOptions.CultureInvariant);
    internal static int SlotOfGame(string gameName)
    {
        var m = GameNamePattern.Match(gameName ?? "");
        return m.Success && int.TryParse(m.Groups[1].Value, out int slot) && slot > 0 ? slot : 0;
    }
    // Save files: "<GameName>_<save number>.sav" → GameName.
    private static readonly Regex SaveFilePattern = new(@"^(\d{14}_\d{1,2})_\d+$", RegexOptions.CultureInvariant);
    internal static string GameOfSaveFile(string leaf)
    {
        var m = SaveFilePattern.Match(leaf ?? "");
        return m.Success ? m.Groups[1].Value : null;
    }
    // Slot captions of the native list: one word ("Файл", "File", "Slot", …) and a number.
    private static readonly Regex CaptionPattern = new(@"^([^\d<>:/]{2,20}?)\s+(\d{1,2})$", RegexOptions.CultureInvariant);
    internal static int SlotOfCaption(string text)
    {
        var m = CaptionPattern.Match((text ?? "").Trim());
        return m.Success && int.TryParse(m.Groups[2].Value, out int slot) && slot > 0 ? slot : 0;
    }
    // The slot title object of a list item ("<object>/<parent>" names); counters such as the day,
    // play time or money share the "word number" shape and are never replaced.
    private static readonly string[] NotCaption = { "day", "time", "date", "currency", "newbuck", "money", "count", "version", "button" };
    // insideSlotButton: the path runs from the label up to (not including) a slot button of the list,
    // so the word "button" of a generic button label no longer disqualifies it.
    internal static bool CaptionObject(string path, bool insideSlotButton = false)
    {
        string p = (path ?? "").ToLowerInvariant();
        foreach (string word in NotCaption) if ((!insideSlotButton || word != "button") && p.Contains(word)) return false;
        return true;
    }
    // Slot buttons of the native save list ("SaveGameSlotButton(Clone)", game 1.x).
    internal static bool SlotButtonObject(string name) => (name ?? "").StartsWith("SaveGameSlotButton", StringComparison.Ordinal);
    // Newest save file of every slot → its GameName (a slot keeps one game; an older leftover loses).
    internal static Dictionary<int, string> SlotGames(IEnumerable<(string Leaf, DateTime Time)> files)
    {
        var result = new Dictionary<int, string>(); var newest = new Dictionary<int, DateTime>();
        foreach (var (leaf, time) in files ?? Array.Empty<(string, DateTime)>())
        {
            string game = GameOfSaveFile(leaf); int slot = SlotOfGame(game);
            if (slot <= 0) continue;
            if (!newest.TryGetValue(slot, out var best) || time > best) { newest[slot] = time; result[slot] = game; }
        }
        return result;
    }
    internal static bool ValidGameName(string gameName) =>
        !string.IsNullOrEmpty(gameName) && gameName.Length <= 64 && gameName.IndexOfAny(new[] { '\t', '\r', '\n', '=' }) < 0;
    internal static Dictionary<string, string> ParseNames(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in (text ?? "").Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            int tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            string game = line.Substring(0, tab), name = CleanName(line.Substring(tab + 1));
            if (ValidGameName(game) && name.Length > 0) result[game] = name;
        }
        return result;
    }
    internal static string SerializeNames(IReadOnlyDictionary<string, string> names)
    {
        var sb = new StringBuilder("# Creator Toolkit: own save names (GameName<TAB>name). The save files are not changed.\n");
        foreach (var pair in names.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            string name = CleanName(pair.Value);
            if (ValidGameName(pair.Key) && name.Length > 0) sb.Append(pair.Key).Append('\t').Append(name).Append('\n');
        }
        return sb.ToString();
    }

    // ── Free placement («Свободная установка», with or without the infinite builder) ─
    internal const float FreeRangeFactor = 4f, FreeRangeMax = 60f, FreeSlope = 180f, FreeGroundedDistance = 500f;
    internal static float FreeRange(float vanilla) =>
        float.IsFinite(vanilla) && vanilla > 0f ? Math.Max(vanilla, Math.Min(vanilla * FreeRangeFactor, FreeRangeMax)) : FreeRangeMax;
    internal static string FreePlacementLabel(bool enabled) => enabled ? "Свободная установка: ВКЛ" : "Свободная установка: ВЫКЛ";
}
