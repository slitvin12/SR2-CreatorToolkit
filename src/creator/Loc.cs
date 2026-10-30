using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace CreatorToolkit;

// 0.8.0: the mod UI follows the game language. Russian is the source language (identity, zero cost);
// every other game locale falls back to English from the embedded LocEnglish.tsv. Text is translated at
// draw time (L.Label/L.Box, ButtonContent), so a language switch in the game options applies live.
internal static class Loc
{
    internal static bool English { get; private set; }
    internal static string LocaleCode { get; private set; } = "";
    internal static int Generation { get; private set; }
    // Diagnostics only (cmd.txt «lang-en» / «lang-ru» / «lang-auto»): forces a language over the game locale.
    internal static string Override { get; private set; }
    internal static int TableSize => _table?.Count ?? 0;
    internal static int MissCount => _misses.Count;

    private readonly struct Fragment
    {
        internal readonly string Key, Value; internal readonly bool LetterStart, LetterEnd;
        internal Fragment(string key, string value)
        { Key = key; Value = value; LetterStart = char.IsLetter(key[0]); LetterEnd = char.IsLetter(key[key.Length - 1]); }
    }
    private static Dictionary<string, string> _table;
    private static Dictionary<int, Fragment[]> _fragments;
    private static bool _loaded;
    private static readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _misses = new(StringComparer.Ordinal);
    private static float _nextPoll;
    private static bool _initialized;
    // The boot scene reports the default locale (en) for ~20 s before the saved option is applied;
    // the game locale is trusted only after the first other scene and when two polls agree.
    private static bool _sceneReady;
    private static string _candidate;

    internal static void Tick()
    {
        if (!_initialized)
        {
            _initialized = true;
            bool russian = Application.systemLanguage is SystemLanguage.Russian or SystemLanguage.Ukrainian or SystemLanguage.Belarusian;
            Apply(russian ? "ru" : "en", "system");
        }
        float now = Time.unscaledTime;
        if (now < _nextPoll) return;
        _nextPoll = now + .5f;
        if (Override != null) { Apply(Override, "override"); return; }
        string code = _sceneReady ? ReadGameLocale() : null;
        if (string.IsNullOrEmpty(code)) { _candidate = null; return; }
        if (code != _candidate) { _candidate = code; return; }
        Apply(code, "game");
    }

    internal static void SceneLoaded(string sceneName)
    {
        if (!_sceneReady && !string.IsNullOrEmpty(sceneName) && !string.Equals(sceneName, "SystemCore", StringComparison.OrdinalIgnoreCase))
        { _sceneReady = true; _nextPoll = 0; }
    }

    internal static void SetOverride(string code)
    {
        Override = string.IsNullOrEmpty(code) ? null : code;
        _nextPoll = 0;
        if (Override != null) Apply(Override, "override");
    }

    private static string ReadGameLocale()
    {
        try
        {
            var context = Il2Cpp.SystemContext.Instance;
            var loader = context?.SceneLoader;
            if (loader != null && !loader.WasCollected && loader.IsSceneLoadInProgress) return null;
            var director = context?.LocalizationDirector;
            if (director == null || director.WasCollected) return null;
            return director.GetCurrentLocaleCode();
        }
        catch { return null; }
    }

    internal static bool IsRussianCode(string code)
        => code != null && code.Trim().StartsWith("ru", StringComparison.OrdinalIgnoreCase);

    private static void Apply(string code, string source)
    {
        if (string.Equals(code, LocaleCode, StringComparison.Ordinal)) return;
        LocaleCode = code;
        bool english = !IsRussianCode(code);
        if (english == English && Generation > 0) return;
        English = english;
        if (english) EnsureLoaded();
        _cache.Clear();
        Generation++;
        try { ToolkitMod.I?.LoggerInstance.Msg("UI language: " + (english ? "English" : "Русский") + " (" + source + " locale «" + code + "», " + TableSize + " strings)."); }
        catch { }
    }

    internal static string T(string text)
    {
        if (!English || string.IsNullOrEmpty(text) || !HasCyrillic(text)) return text;
        if (_cache.TryGetValue(text, out var result)) return result;
        result = Translate(text);
        if (_cache.Count >= 8192) _cache.Clear();
        _cache[text] = result;
        return result;
    }

    internal static bool HasCyrillic(string text)
    {
        for (int i = 0; i < text.Length; i++) { char c = text[i]; if (c >= '\u0400' && c <= '\u04FF') return true; }
        return false;
    }

    private static string Translate(string text)
    {
        EnsureLoaded();
        if (_table == null) return text;
        if (_table.TryGetValue(text, out var exact)) return exact;
        int a = 0, b = text.Length;
        while (a < b && char.IsWhiteSpace(text[a])) a++;
        while (b > a && char.IsWhiteSpace(text[b - 1])) b--;
        if ((a > 0 || b < text.Length) && _table.TryGetValue(text.Substring(a, b - a), out exact))
            return text.Substring(0, a) + exact + text.Substring(b);
        var sb = new StringBuilder(text.Length + 16);
        int n = text.Length, i = 0;
        while (i < n)
        {
            char c = text[i];
            if (i + 1 < n && _fragments.TryGetValue(c << 16 | text[i + 1], out var list) && TryList(list, text, i, sb, out int used))
            { i += used; continue; }
            sb.Append(c); i++;
        }
        string translated = sb.ToString();
        if (HasCyrillic(translated) && _misses.Count < 4000) _misses.Add(text);
        return translated;
    }

    private static bool TryList(Fragment[] list, string text, int i, StringBuilder sb, out int used)
    {
        int n = text.Length;
        foreach (var f in list)
        {
            int k = f.Key.Length;
            if (i + k > n || string.CompareOrdinal(text, i, f.Key, 0, k) != 0) continue;
            if (f.LetterStart && i > 0 && char.IsLetter(text[i - 1])) continue;
            if (f.LetterEnd && i + k < n && char.IsLetter(text[i + k])) continue;
            sb.Append(f.Value); used = k; return true;
        }
        used = 0; return false;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CreatorToolkit.LocEnglish.tsv");
            if (stream == null) { try { ToolkitMod.I?.LoggerInstance.Warning("LocEnglish.tsv: embedded table missing"); } catch { } return; }
            using var reader = new StreamReader(stream, Encoding.UTF8);
            Load(reader.ReadToEnd());
        }
        catch (Exception e) { try { ToolkitMod.I?.LoggerInstance.Warning("LocEnglish.tsv: " + e.Message); } catch { } }
    }

    // Test seam and loader: «key<TAB>value» per line, \n \t \\ escaped, '#' comments.
    internal static void Load(string tsv)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in tsv.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            int tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            string key = Unescape(line.Substring(0, tab)), value = Unescape(line.Substring(tab + 1));
            if (key.Length > 0) table[key] = value;
        }
        var buckets = new Dictionary<int, List<Fragment>>();
        void Add(string key, string value)
        {
            if (key.Length < 2 || !HasCyrillic(key)) return;
            int bucket = key[0] << 16 | key[1];
            if (!buckets.TryGetValue(bucket, out var list)) buckets[bucket] = list = new List<Fragment>();
            foreach (var f in list) if (f.Key == key) return;
            list.Add(new Fragment(key, value));
        }
        foreach (var pair in table) Add(pair.Key, pair.Value); // exact keys win over trimmed variants
        foreach (var pair in table)
        {
            string trimmed = pair.Key.Trim();
            if (trimmed.Length != pair.Key.Length && trimmed.Length > 0)
            {
                string value = pair.Value.Trim();
                Add(trimmed, value.Length > 0 ? value : pair.Value);
            }
        }
        var fragments = new Dictionary<int, Fragment[]>();
        foreach (var pair in buckets)
        {
            pair.Value.Sort((x, y) => y.Key.Length.CompareTo(x.Key.Length));
            fragments[pair.Key] = pair.Value.ToArray();
        }
        _table = table; _fragments = fragments; _loaded = true;
        _cache.Clear(); _misses.Clear();
    }

    internal static void ForceForTest(bool english) { English = english; _cache.Clear(); Generation++; }

    private static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                char e = s[++i];
                sb.Append(e switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => e });
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    internal static string DumpMisses(string path)
    {
        var lines = new List<string>(_misses);
        lines.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var line in lines) sb.Append(line.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\t", "\\t")).Append('\n');
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }
}

// Translating stand-ins for GUI.Label / GUI.Box. String overloads go through Loc.T; GUIContent
// overloads pass through (contents come from ButtonContent, which is already translated).
internal static class L
{
    internal static void Label(Rect rect, string text) => GUI.Label(rect, Loc.T(text));
    internal static void Label(Rect rect, string text, GUIStyle style) => GUI.Label(rect, Loc.T(text), style);
    internal static void Label(Rect rect, GUIContent content) => GUI.Label(rect, content);
    internal static void Label(Rect rect, GUIContent content, GUIStyle style) => GUI.Label(rect, content, style);
    internal static void Label(Rect rect, Texture image) => GUI.Label(rect, image);
    internal static void Label(Rect rect, Texture image, GUIStyle style) => GUI.Label(rect, image, style);
    internal static void Box(Rect rect, string text) => GUI.Box(rect, Loc.T(text));
    internal static void Box(Rect rect, string text, GUIStyle style) => GUI.Box(rect, Loc.T(text), style);
    internal static void Box(Rect rect, GUIContent content) => GUI.Box(rect, content);
    internal static void Box(Rect rect, GUIContent content, GUIStyle style) => GUI.Box(rect, content, style);
    internal static void Box(Rect rect, Texture image) => GUI.Box(rect, image);
    internal static void Box(Rect rect, Texture image, GUIStyle style) => GUI.Box(rect, image, style);
}
