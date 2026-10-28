using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;

namespace CreatorToolkitAgentDirector;

/// <summary>Expected refusal with an explicit HTTP status (gates, bad state, not found).</summary>
internal sealed class GateException : Exception
{
    internal readonly int Code;
    internal GateException(int code, string message) : base(message) { Code = code; }
}

internal static class Clock
{
    private static readonly Stopwatch Watch = Stopwatch.StartNew();
    internal static double Now => Watch.Elapsed.TotalSeconds;
}

/// <summary>Small streaming JSON writer (no reflection, invariant culture, comma bookkeeping).</summary>
internal sealed class JsonWriter
{
    private readonly StringBuilder _sb;
    private readonly Stack<bool> _first = new();
    private bool _afterName;
    internal JsonWriter(int capacity = 2048) { _sb = new StringBuilder(capacity); }

    private void Prefix()
    {
        if (_afterName) { _afterName = false; return; }
        if (_first.Count == 0) return;
        if (_first.Peek()) { _first.Pop(); _first.Push(false); }
        else _sb.Append(',');
    }
    internal JsonWriter Obj() { Prefix(); _sb.Append('{'); _first.Push(true); return this; }
    internal JsonWriter EndObj() { _first.Pop(); _sb.Append('}'); return this; }
    internal JsonWriter Arr() { Prefix(); _sb.Append('['); _first.Push(true); return this; }
    internal JsonWriter EndArr() { _first.Pop(); _sb.Append(']'); return this; }
    internal JsonWriter Obj(string name) => Name(name).Obj();
    internal JsonWriter Arr(string name) => Name(name).Arr();
    internal JsonWriter Name(string name) { Prefix(); Quote(name); _sb.Append(':'); _afterName = true; return this; }
    internal JsonWriter Null() { Prefix(); _sb.Append("null"); return this; }
    internal JsonWriter Val(string v) { Prefix(); if (v == null) _sb.Append("null"); else Quote(v); return this; }
    internal JsonWriter Val(bool v) { Prefix(); _sb.Append(v ? "true" : "false"); return this; }
    internal JsonWriter Val(long v) { Prefix(); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
    internal JsonWriter Val(double v, int digits)
    {
        Prefix();
        if (double.IsNaN(v) || double.IsInfinity(v)) _sb.Append("null");
        else _sb.Append(Math.Round(v, Math.Clamp(digits, 0, 10)).ToString("0.##########", CultureInfo.InvariantCulture));
        return this;
    }
    /// <summary>Appends an already serialized JSON value.</summary>
    internal JsonWriter Raw(string json) { Prefix(); _sb.Append(string.IsNullOrEmpty(json) ? "null" : json); return this; }
    internal JsonWriter P(string n, string v) => Name(n).Val(v);
    internal JsonWriter P(string n, bool v) => Name(n).Val(v);
    internal JsonWriter P(string n, long v) => Name(n).Val(v);
    internal JsonWriter P(string n, int v) => Name(n).Val((long)v);
    internal JsonWriter P(string n, double v, int digits = 3) => Name(n).Val(v, digits);
    internal JsonWriter P(string n, float v, int digits = 3) => Name(n).Val((double)v, digits);
    internal JsonWriter Strings(string n, IEnumerable<string> values)
    {
        Arr(n);
        if (values != null) foreach (var v in values) Val(v);
        return EndArr();
    }

    private void Quote(string s)
    {
        _sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': _sb.Append("\\\""); break;
                case '\\': _sb.Append("\\\\"); break;
                case '\n': _sb.Append("\\n"); break;
                case '\r': _sb.Append("\\r"); break;
                case '\t': _sb.Append("\\t"); break;
                default:
                    if (c < (char)0x20 || c == '\u2028' || c == '\u2029')
                        _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else _sb.Append(c);
                    break;
            }
        }
        _sb.Append('"');
    }
    public override string ToString() => _sb.ToString();
    internal static string Quoted(string s) { var w = new JsonWriter(s == null ? 8 : s.Length + 8); w.Val(s); return w.ToString(); }
}

/// <summary>Minimal JSON reader for request bodies (objects, arrays, scalars).</summary>
internal static class MiniJson
{
    internal static object Parse(string text)
    {
        int i = 0;
        var value = Value(text, ref i, 0);
        Ws(text, ref i);
        if (i != text.Length) throw new ArgumentException("JSON: лишние символы с позиции " + i);
        return value;
    }
    private static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
    private static void Expect(string s, ref int i, char c)
    {
        Ws(s, ref i);
        if (i >= s.Length || s[i] != c) throw new ArgumentException("JSON: ожидался '" + c + "' в позиции " + i);
        i++;
    }
    private static bool Literal(string s, ref int i, string word)
    {
        if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
        i += word.Length; return true;
    }
    private static object Value(string s, ref int i, int depth)
    {
        if (depth > 32) throw new ArgumentException("JSON: слишком глубокая вложенность");
        Ws(s, ref i);
        if (i >= s.Length) throw new ArgumentException("JSON: неожиданный конец");
        char c = s[i];
        if (c == '{')
        {
            i++;
            var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            Ws(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }
            while (true)
            {
                Ws(s, ref i);
                string key = String(s, ref i);
                Expect(s, ref i, ':');
                map[key] = Value(s, ref i, depth + 1);
                Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                Expect(s, ref i, '}');
                return map;
            }
        }
        if (c == '[')
        {
            i++;
            var list = new List<object>();
            Ws(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(Value(s, ref i, depth + 1));
                Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                Expect(s, ref i, ']');
                return list;
            }
        }
        if (c == '"') return String(s, ref i);
        if (Literal(s, ref i, "true")) return true;
        if (Literal(s, ref i, "false")) return false;
        if (Literal(s, ref i, "null")) return null;
        int start = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
        if (start == i) throw new ArgumentException("JSON: неожиданный символ '" + c + "' в позиции " + i);
        return double.Parse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
    private static string String(string s, ref int i)
    {
        if (i >= s.Length || s[i] != '"') throw new ArgumentException("JSON: ожидалась строка в позиции " + i);
        i++;
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            if (i >= s.Length) break;
            char e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    if (i + 4 > s.Length) throw new ArgumentException("JSON: неполный \\u");
                    sb.Append((char)int.Parse(s.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4; break;
                default: sb.Append(e); break;
            }
        }
        throw new ArgumentException("JSON: незакрытая строка");
    }
    internal static string Scalar(object v)
    {
        switch (v)
        {
            case null: return "";
            case string s: return s;
            case bool b: return b ? "true" : "false";
            case double d: return d.ToString("R", CultureInfo.InvariantCulture);
            case List<object> list:
                var parts = new List<string>();
                foreach (var item in list) parts.Add(Scalar(item));
                return string.Join("\n", parts);
            default: return Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>Case-insensitive request arguments (query, form or JSON body).</summary>
internal sealed class Args
{
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    internal void Set(string key, string value) { if (!string.IsNullOrEmpty(key)) _map[key.Trim()] = value ?? ""; }
    internal bool Has(string key) => _map.ContainsKey(key);
    internal IEnumerable<KeyValuePair<string, string>> All => _map;
    internal string Str(string key, string fallback = null) => _map.TryGetValue(key, out var v) ? v : fallback;
    internal string First(params string[] keys)
    {
        foreach (var k in keys) if (_map.TryGetValue(k, out var v) && v.Length > 0) return v;
        return null;
    }
    internal int Int(string key, int fallback)
    {
        if (!_map.TryGetValue(key, out var v) || v.Length == 0) return fallback;
        if (int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return n;
        if (double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d >= int.MinValue && d <= int.MaxValue) return (int)d;
        throw new ArgumentException("Параметр '" + key + "' должен быть целым числом, получено '" + v + "'.");
    }
    internal float Float(string key, float fallback)
    {
        if (!_map.TryGetValue(key, out var v) || v.Length == 0) return fallback;
        if (float.TryParse(v.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && !float.IsNaN(f) && !float.IsInfinity(f)) return f;
        throw new ArgumentException("Параметр '" + key + "' должен быть числом, получено '" + v + "'.");
    }
    internal bool Bool(string key, bool fallback)
    {
        if (!_map.TryGetValue(key, out var v)) return fallback;
        switch (v.Trim().ToLowerInvariant())
        {
            case "": case "1": case "true": case "yes": case "on": case "да": return true;
            case "0": case "false": case "no": case "off": case "нет": return false;
            default: throw new ArgumentException("Параметр '" + key + "' должен быть 0/1, получено '" + v + "'.");
        }
    }
    internal static void ParseQuery(string query, Args into)
    {
        if (string.IsNullOrEmpty(query)) return;
        foreach (var part in query.Split('&'))
        {
            if (part.Length == 0) continue;
            int eq = part.IndexOf('=');
            string key = eq < 0 ? part : part.Substring(0, eq);
            string value = eq < 0 ? "" : part.Substring(eq + 1);
            into.Set(Unescape(key), Unescape(value));
        }
    }
    internal static string Unescape(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        s = s.Replace("+", " ");
        if (s.IndexOf(EscapeMark) < 0) return s;
        try
        {
            // Decode escapes to raw bytes first, so both UTF-8 and CP1251 escapes work.
            var bytes = new List<byte>(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                int hi = s[i] == EscapeMark && i + 2 < s.Length ? HexValue(s[i + 1]) : -1;
                int lo = hi >= 0 ? HexValue(s[i + 2]) : -1;
                if (lo >= 0) { bytes.Add((byte)(hi * 16 + lo)); i += 2; continue; }
                int len = char.IsHighSurrogate(s[i]) && i + 1 < s.Length ? 2 : 1;
                bytes.AddRange(Encoding.UTF8.GetBytes(s.Substring(i, len)));
                i += len - 1;
            }
            var arr = bytes.ToArray();
            return HttpBridge.Decode(arr, arr.Length);
        }
        catch (Exception) { return s; }
    }

    private const char EscapeMark = (char)0x25;
    private const string HexDigits = "0123456789abcdef";
    private static int HexValue(char c) => HexDigits.IndexOf(char.ToLowerInvariant(c));
}

/// <summary>One agent command. Completed exactly once (first writer wins), from any thread.</summary>
internal sealed class AgentRequest
{
    private static long _ids;
    internal readonly long Id = Interlocked.Increment(ref _ids);
    internal readonly string Method, Path, Source, Line;
    internal readonly Args Args;
    internal string RawBody; // original request body (used by /batch)
    internal readonly DateTime ReceivedUtc = DateTime.UtcNow;
    internal readonly double Received = Clock.Now;
    private readonly ManualResetEventSlim _done = new(false);
    private int _completed;
    internal int Status { get; private set; }
    internal string ContentType { get; private set; }
    internal byte[] Body { get; private set; }
    internal bool IsCompleted => Volatile.Read(ref _completed) != 0;
    internal double AgeMs => (Clock.Now - Received) * 1000.0;

    internal AgentRequest(string method, string path, Args args, string source, string line)
    {
        Method = method; Path = path; Args = args ?? new Args(); Source = source; Line = line;
    }
    internal bool Complete(int status, string contentType, byte[] body)
    {
        if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0) return false;
        Status = status; ContentType = contentType; Body = body ?? Array.Empty<byte>();
        _done.Set();
        return true;
    }
    /// <summary>Transport thread only, after completion: replace the reply body (after-hook fields).</summary>
    internal void Amend(byte[] body) { if (IsCompleted) Body = body ?? Array.Empty<byte>(); }
    internal bool Json(int status, string json) => Complete(status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
    internal bool Text(int status, string text, string type = "text/plain; charset=utf-8") => Complete(status, type, Encoding.UTF8.GetBytes(text));
    internal bool Await(int timeoutMs) { try { return _done.Wait(timeoutMs); } catch (ObjectDisposedException) { return true; } }
    internal void Release() { try { _done.Dispose(); } catch (Exception) { } }
    /// <summary>Job/HTTP budget from ?timeout=seconds.</summary>
    internal double TimeoutSeconds(double fallback, double max)
    {
        double t = Args.Float("timeout", (float)fallback);
        return Math.Clamp(t, 0.05, max);
    }
}

/// <summary>Reflection helpers with member caching and TargetInvocationException unwrapping.</summary>
internal static class Reflect
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly ConcurrentDictionary<(Type, string), MemberInfo> Members = new();

    internal static MemberInfo Member(Type type, string name)
    {
        if (type == null) return null;
        return Members.GetOrAdd((type, name), key =>
        {
            for (var t = key.Item1; t != null; t = t.BaseType)
            {
                var f = t.GetField(key.Item2, All | BindingFlags.DeclaredOnly);
                if (f != null) return f;
                var p = t.GetProperty(key.Item2, All | BindingFlags.DeclaredOnly);
                if (p != null && p.GetIndexParameters().Length == 0) return p;
            }
            return null;
        });
    }
    internal static bool Has(Type type, string name) => Member(type, name) != null;
    internal static object Get(object target, string name) => target == null ? null : GetFrom(target.GetType(), target, name);
    internal static object GetStatic(Type type, string name) => GetFrom(type, null, name);
    private static object GetFrom(Type type, object target, string name)
    {
        var m = Member(type, name) ?? throw new MissingMemberException(type?.FullName, name);
        try
        {
            return m is FieldInfo f ? f.GetValue(target) : ((PropertyInfo)m).GetValue(target);
        }
        catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    internal static T Get<T>(object target, string name, T fallback = default)
    {
        try { return Get(target, name) is T t ? t : fallback; } catch (Exception) { return fallback; }
    }
    internal static T GetStatic<T>(Type type, string name, T fallback = default)
    {
        try { return GetStatic(type, name) is T t ? t : fallback; } catch (Exception) { return fallback; }
    }
    internal static void Set(object target, string name, object value)
    {
        var m = Member(target.GetType(), name) ?? throw new MissingMemberException(target.GetType().FullName, name);
        if (m is FieldInfo f) f.SetValue(target, value);
        else ((PropertyInfo)m).SetValue(target, value);
    }
    internal static MethodInfo Method(Type type, string name, int argCount)
    {
        for (var t = type; t != null; t = t.BaseType)
            foreach (var m in t.GetMethods(All | BindingFlags.DeclaredOnly))
                if (m.Name == name && m.GetParameters().Length == argCount && !m.IsGenericMethodDefinition) return m;
        return null;
    }
    internal static object Call(object target, string name, params object[] args)
        => Invoke(target?.GetType(), target, name, args);
    internal static object CallStatic(Type type, string name, params object[] args)
        => Invoke(type, null, name, args);
    private static object Invoke(Type type, object target, string name, object[] args)
    {
        if (type == null) throw new GateException(503, "Creator Toolkit не загружен.");
        var m = Method(type, name, args.Length) ?? throw new MissingMethodException(type.FullName, name);
        try { return m.Invoke(m.IsStatic ? null : target, args); }
        catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    internal static Exception Unwrap(Exception e)
    {
        while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
        if (e is AggregateException a && a.InnerExceptions.Count == 1) return Unwrap(a.InnerExceptions[0]);
        return e;
    }
}

/// <summary>Per-frame FPS statistics, published lock-free for off-thread /ping.</summary>
internal sealed class FrameSnapshot
{
    internal int Frame;
    internal double Fps, FpsAvg, WorstMs, Realtime, TimeScale;
    internal string Scene = "", SceneGroup = "";
    internal bool Ready, MenuOpen, Loading, Focused, ToolkitBound;
    internal int Width, Height;
}

internal static class FrameStats
{
    private static FrameSnapshot _snap = new();
    private static long _lastUpdateTicks = Stopwatch.GetTimestamp();
    private static double _ema = 60, _window, _worst, _publishedWorst, _publishedAvg, _nextPublish;
    private static int _windowFrames;
    internal static volatile int Frame;
    internal static FrameSnapshot Snap => Volatile.Read(ref _snap);
    internal static double Fps => _ema;
    internal static double FpsAvg => _publishedAvg;
    internal static double WorstMs => _publishedWorst;
    internal static double MainThreadAgeMs => (Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastUpdateTicks)) * 1000.0 / Stopwatch.Frequency;

    /// <summary>Main thread only. Returns true when a new snapshot should be published.</summary>
    internal static bool Tick(int frame, float unscaledDelta)
    {
        Frame = frame;
        Interlocked.Exchange(ref _lastUpdateTicks, Stopwatch.GetTimestamp());
        double dt = unscaledDelta > 1e-5f ? unscaledDelta : 1e-5;
        _ema = _ema * 0.9 + (1.0 / dt) * 0.1;
        _window += dt; _windowFrames++;
        if (dt * 1000.0 > _worst) _worst = dt * 1000.0;
        if (_window >= 1.0)
        {
            _publishedAvg = _windowFrames / _window;
            _publishedWorst = _worst;
            _window = 0; _windowFrames = 0; _worst = 0;
        }
        double now = Clock.Now;
        if (now < _nextPublish) return false;
        _nextPublish = now + 0.25;
        return true;
    }
    internal static void Publish(FrameSnapshot snapshot) => Volatile.Write(ref _snap, snapshot);
}
