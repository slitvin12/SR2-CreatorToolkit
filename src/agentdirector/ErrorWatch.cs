using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;

namespace CreatorToolkitAgentDirector;

internal sealed class LogEntry
{
    internal long Serial;
    internal DateTime Utc;
    internal string Level, Source, Text;
}

/// <summary>
/// Error/warning counters from three independent sources: MelonLogger callbacks (all mods and
/// MelonLoader itself), Unity's main-thread log callback, and an incremental scan of Latest.log
/// (which also covers lines written before this mod loaded). Thread-safe.
/// </summary>
internal static class ErrorWatch
{
    private const int Capacity = 600, MaxText = 4000;
    private static readonly object Gate = new(), ScanGate = new();
    private static readonly LogEntry[] Ring = new LogEntry[Capacity];
    private static long _serial;
    internal static long MelonErrors, MelonWarnings, MelonMessages, UnityErrors, UnityExceptions, UnityAsserts, UnityWarnings;
    internal static long LogErrors, LogWarnings, LogLines;
    private static long _bMelonErrors, _bMelonWarnings, _bUnityErrors, _bUnityExceptions, _bUnityWarnings, _bLogErrors, _bLogWarnings;
    internal static DateTime ResetUtc = DateTime.UtcNow;
    internal static long ResetSerial;
    private static string _logPath;
    private static long _logOffset;
    internal static string UnityHookError = "not installed", LogScanError = "";
    private static bool _melonHooked, _unityHooked;
    private static Action<MelonLoader.Logging.ColorARGB, MelonLoader.Logging.ColorARGB, string, string> _onMsg;
    private static Action<string, string> _onWarning, _onError;
    private static Action<string, string, LogType> _unityManaged;
    private static Application.LogCallback _unityCallback;
    [ThreadStatic] private static bool _inCallback;

    internal static string LogPath => _logPath;
    internal static long Serial { get { lock (Gate) return _serial; } }

    internal static void Install(string latestLog)
    {
        _logPath = latestLog;
        if (_melonHooked) return;
        _onMsg = OnMsg; _onWarning = OnWarning; _onError = OnError;
        MelonLogger.MsgDrawingCallbackHandler += _onMsg;
        MelonLogger.WarningCallbackHandler += _onWarning;
        MelonLogger.ErrorCallbackHandler += _onError;
        _melonHooked = true;
    }

    /// <summary>Main thread, after the Unity engine is initialized.</summary>
    internal static void InstallUnity()
    {
        if (_unityHooked) return;
        try
        {
            _unityManaged = OnUnity;
            _unityCallback = DelegateSupport.ConvertDelegate<Application.LogCallback>(_unityManaged);
            Application.add_logMessageReceived(_unityCallback);
            _unityHooked = true;
            UnityHookError = "";
        }
        catch (Exception e) { UnityHookError = e.GetType().Name + ": " + e.Message; }
    }

    internal static void Uninstall()
    {
        if (_melonHooked)
        {
            try
            {
                MelonLogger.MsgDrawingCallbackHandler -= _onMsg;
                MelonLogger.WarningCallbackHandler -= _onWarning;
                MelonLogger.ErrorCallbackHandler -= _onError;
            }
            catch (Exception) { }
            _melonHooked = false;
        }
        if (_unityHooked)
        {
            try { Application.remove_logMessageReceived(_unityCallback); } catch (Exception) { }
            _unityHooked = false;
        }
    }

    private static void OnMsg(MelonLoader.Logging.ColorARGB melonColor, MelonLoader.Logging.ColorARGB textColor, string section, string message)
    {
        Interlocked.Increment(ref MelonMessages);
        Add("msg", section, message);
    }
    private static void OnWarning(string section, string message)
    {
        Interlocked.Increment(ref MelonWarnings);
        Add("warning", section, message);
    }
    private static void OnError(string section, string message)
    {
        Interlocked.Increment(ref MelonErrors);
        Add("error", section, message);
    }
    private static void OnUnity(string condition, string stackTrace, LogType type)
    {
        if (_inCallback) return;
        _inCallback = true;
        try
        {
            switch (type)
            {
                case LogType.Exception: Interlocked.Increment(ref UnityExceptions); Add("unity-exception", "Unity", condition + (string.IsNullOrEmpty(stackTrace) ? "" : "\n" + stackTrace)); break;
                case LogType.Error: Interlocked.Increment(ref UnityErrors); Add("unity-error", "Unity", condition); break;
                case LogType.Assert: Interlocked.Increment(ref UnityAsserts); Add("unity-assert", "Unity", condition); break;
                case LogType.Warning: Interlocked.Increment(ref UnityWarnings); Add("unity-warning", "Unity", condition); break;
            }
        }
        catch (Exception) { }
        finally { _inCallback = false; }
    }

    private static void Add(string level, string source, string text)
    {
        if (text != null && text.Length > MaxText) text = text.Substring(0, MaxText) + "…";
        var entry = new LogEntry { Utc = DateTime.UtcNow, Level = level, Source = source ?? "", Text = text ?? "" };
        lock (Gate)
        {
            entry.Serial = ++_serial;
            Ring[entry.Serial % Capacity] = entry;
        }
    }

    internal static List<LogEntry> Since(long serial, string level, string contains, int limit)
    {
        var result = new List<LogEntry>();
        lock (Gate)
        {
            long first = Math.Max(serial + 1, _serial - Capacity + 1);
            for (long s = Math.Max(1, first); s <= _serial; s++)
            {
                var e = Ring[s % Capacity];
                if (e == null || e.Serial != s) continue;
                if (!LevelMatches(e.Level, level)) continue;
                if (!string.IsNullOrEmpty(contains) && e.Text.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0
                    && e.Source.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                result.Add(e);
            }
        }
        if (limit > 0 && result.Count > limit) result.RemoveRange(0, result.Count - limit);
        return result;
    }

    private static bool LevelMatches(string entry, string filter)
    {
        if (string.IsNullOrEmpty(filter) || filter == "all") return true;
        switch (filter)
        {
            case "error": case "errors": return entry == "error" || entry == "unity-error" || entry == "unity-exception" || entry == "unity-assert";
            case "warning": case "warnings": return entry == "warning" || entry == "unity-warning";
            case "problems": return entry != "msg";
            case "msg": return entry == "msg";
            case "unity": return entry.StartsWith("unity", StringComparison.Ordinal);
            default: return entry == filter;
        }
    }

    /// <summary>Incrementally counts [ERROR]/[WARNING] lines appended to MelonLoader's Latest.log.</summary>
    internal static void ScanLog()
    {
        if (string.IsNullOrEmpty(_logPath)) return;
        lock (ScanGate)
        {
            try
            {
                if (!File.Exists(_logPath)) { LogScanError = "Latest.log not found"; return; }
                using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long length = fs.Length;
                if (length < _logOffset) { _logOffset = 0; LogErrors = LogWarnings = LogLines = 0; _bLogErrors = _bLogWarnings = 0; }
                if (length == _logOffset) return;
                fs.Seek(_logOffset, SeekOrigin.Begin);
                int toRead = (int)Math.Min(length - _logOffset, 8L * 1024 * 1024);
                var buffer = new byte[toRead];
                int read = 0;
                while (read < toRead)
                {
                    int n = fs.Read(buffer, read, toRead - read);
                    if (n <= 0) break;
                    read += n;
                }
                int lastNewline = Array.LastIndexOf(buffer, (byte)'\n', Math.Max(0, read - 1));
                if (lastNewline < 0) { if (read == toRead && toRead >= 8 * 1024 * 1024) _logOffset += read; return; }
                int start = 0;
                for (int i = 0; i <= lastNewline; i++)
                {
                    if (buffer[i] != (byte)'\n') continue;
                    CountLine(buffer, start, i - start);
                    start = i + 1;
                }
                _logOffset += lastNewline + 1;
                LogScanError = "";
            }
            catch (Exception e) { LogScanError = e.GetType().Name + ": " + e.Message; }
        }
    }

    private static readonly byte[] ErrorTag = Encoding.ASCII.GetBytes("[ERROR]"), WarningTag = Encoding.ASCII.GetBytes("[WARNING]");
    private static void CountLine(byte[] data, int start, int count)
    {
        LogLines++;
        int window = Math.Min(count, 160); // the level tag is in the line prefix
        if (Contains(data, start, window, ErrorTag)) LogErrors++;
        else if (Contains(data, start, window, WarningTag)) LogWarnings++;
    }
    private static bool Contains(byte[] data, int start, int count, byte[] tag)
    {
        for (int i = start; i + tag.Length <= start + count; i++)
        {
            int j = 0;
            while (j < tag.Length && data[i + j] == tag[j]) j++;
            if (j == tag.Length) return true;
        }
        return false;
    }

    internal static void Reset()
    {
        ScanLog();
        lock (ScanGate)
        {
            _bLogErrors = LogErrors; _bLogWarnings = LogWarnings;
        }
        _bMelonErrors = Interlocked.Read(ref MelonErrors); _bMelonWarnings = Interlocked.Read(ref MelonWarnings);
        _bUnityErrors = Interlocked.Read(ref UnityErrors) + Interlocked.Read(ref UnityAsserts);
        _bUnityExceptions = Interlocked.Read(ref UnityExceptions);
        _bUnityWarnings = Interlocked.Read(ref UnityWarnings);
        ResetUtc = DateTime.UtcNow;
        ResetSerial = Serial;
    }

    /// <summary>Errors since the last /errors/reset (MelonLoader + Unity).</summary>
    internal static long ErrorsSinceReset =>
        Interlocked.Read(ref MelonErrors) - _bMelonErrors +
        Interlocked.Read(ref UnityErrors) + Interlocked.Read(ref UnityAsserts) - _bUnityErrors +
        Interlocked.Read(ref UnityExceptions) - _bUnityExceptions;

    internal static void Write(JsonWriter w, bool scan)
    {
        if (scan) ScanLog();
        long melonErrors = Interlocked.Read(ref MelonErrors), melonWarnings = Interlocked.Read(ref MelonWarnings);
        long unityErrors = Interlocked.Read(ref UnityErrors) + Interlocked.Read(ref UnityAsserts);
        long unityExceptions = Interlocked.Read(ref UnityExceptions), unityWarnings = Interlocked.Read(ref UnityWarnings);
        long logErrors, logWarnings, logLines;
        lock (ScanGate) { logErrors = LogErrors; logWarnings = LogWarnings; logLines = LogLines; }
        w.Obj()
         .P("errors", ErrorsSinceReset)
         .P("since", ResetUtc.ToString("O"))
         .Obj("melon").P("errors", melonErrors - _bMelonErrors).P("warnings", melonWarnings - _bMelonWarnings)
            .P("errorsTotal", melonErrors).P("warningsTotal", melonWarnings).P("messagesTotal", Interlocked.Read(ref MelonMessages)).EndObj()
         .Obj("unity").P("errors", unityErrors - _bUnityErrors).P("exceptions", unityExceptions - _bUnityExceptions)
            .P("warnings", unityWarnings - _bUnityWarnings).P("hooked", _unityHooked).P("hookError", UnityHookError).EndObj()
         .Obj("latestLog").P("errors", logErrors - _bLogErrors).P("warnings", logWarnings - _bLogWarnings)
            .P("errorsTotal", logErrors).P("warningsTotal", logWarnings).P("lines", logLines)
            .P("path", _logPath).P("scanError", LogScanError).EndObj()
         .P("logSerial", Serial)
         .EndObj();
    }

    internal static void WriteEntries(JsonWriter w, List<LogEntry> entries)
    {
        w.Arr();
        foreach (var e in entries)
            w.Obj().P("serial", e.Serial).P("time", e.Utc.ToString("HH:mm:ss.fff")).P("level", e.Level).P("source", e.Source).P("text", e.Text).EndObj();
        w.EndArr();
    }
}
