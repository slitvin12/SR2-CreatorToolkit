using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MelonLoader.Utils;

namespace CreatorToolkitAgentDirector;

/// <summary>
/// Reflection bridge to Creator Toolkit internals (soft dependency: the agent bridge keeps
/// working for screenshots/state/errors when the toolkit is absent or fails to load).
/// </summary>
internal static class Toolkit
{
    internal static Assembly Asm;
    internal static Type ModType, UiType, M26Type, M27Type, M28Type, MapToolsType;
    internal static string BindError = "not bound yet";
    internal static bool Bound => ModType != null;
    internal static string DataDirectory => Path.Combine(MelonEnvironment.UserDataDirectory, "CreatorToolkit");
    internal static string OptInPath => Path.Combine(DataDirectory, "ct-test.enabled");

    internal static bool TryBind()
    {
        if (Bound) return true;
        try
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name == "CreatorToolkit") { Asm = a; break; }
            }
            if (Asm == null) { BindError = "CreatorToolkit.dll is not loaded"; return false; }
            var mod = Asm.GetType("CreatorToolkit.ToolkitMod");
            if (mod == null) { BindError = "type CreatorToolkit.ToolkitMod not found"; return false; }
            UiType = Asm.GetType("CreatorToolkit.ToolkitUi");
            M26Type = Asm.GetType("CreatorToolkit.M26AcceptanceCore");
            M27Type = Asm.GetType("CreatorToolkit.M27AcceptanceIpc");
            M28Type = Asm.GetType("CreatorToolkit.M28TestIpc");
            MapToolsType = Asm.GetType("CreatorToolkit.MapTools");
            ModType = mod;
            BindError = "";
            return true;
        }
        catch (Exception e) { BindError = e.GetType().Name + ": " + e.Message; return false; }
    }

    internal static object App => ModType == null ? null : Reflect.GetStatic<object>(ModType, "I");
    internal static object Ui => Reflect.Get<object>(App, "_ui");

    internal static object RequireApp()
    {
        if (!TryBind()) throw new GateException(503, "Creator Toolkit недоступен: " + BindError);
        return App ?? throw new GateException(503, "Creator Toolkit ещё не инициализирован (ToolkitMod.I == null).");
    }
    internal static object RequireUi() => Reflect.Get<object>(RequireApp(), "_ui") ?? throw new GateException(503, "Интерфейс Creator Toolkit ещё не создан.");
    internal static object RequireReady()
    {
        var app = RequireApp();
        if (!Reflect.Get<bool>(app, "Ready")) throw new GateException(409, "Мир не готов (загрузка/главное меню). Ждите /wait?until=ready.");
        return app;
    }

    internal static string Version => ModType == null ? null : Reflect.GetStatic<string>(ModType, "Version");
    internal static bool Ready => Reflect.Get<bool>(App, "Ready");
    internal static bool MenuOpen => Reflect.Get<bool>(App, "MenuOpen");
    internal static int Tab => Reflect.Get(Ui, "CurrentTabForTest", -1);
    internal static string[] Tabs => Reflect.Get<string[]>(Ui, "_tabs") ?? new[] { "Игрок", "Спавн", "Ресурсы", "Мир", "Открытия", "Съёмка", "Карта", "Удобство" };
    internal static string Status => Reflect.Get<string>(App, "Status");
    internal static string LastError => Reflect.Get<string>(App, "LastError");
    internal static string PendingTitle
    {
        get { var p = Reflect.Get<object>(App, "Pending"); return p == null ? null : (Reflect.Get<string>(p, "Title") ?? p.ToString()); }
    }

    internal static Dictionary<string, string> Lasts()
    {
        var map = new Dictionary<string, string>();
        if (M26Type != null) map["m26"] = Reflect.GetStatic<string>(M26Type, "Last");
        if (M27Type != null) map["m27"] = Reflect.GetStatic<string>(M27Type, "Last");
        if (M28Type != null) map["m28"] = Reflect.GetStatic<string>(M28Type, "Last");
        map["uiNavigation"] = Reflect.Get<string>(App, "_lastUiNavigation");
        map["status"] = Status;
        map["lastError"] = LastError;
        return map;
    }

    internal static string GameName()
    {
        if (M26Type == null) return null;
        try { return Reflect.CallStatic(M26Type, "GameName") as string; } catch (Exception) { return null; }
    }
    internal static string TestGameName => M26Type == null ? null : Reflect.GetStatic<string>(M26Type, "TestGameName");

    /// <summary>Puts lines straight into the toolkit's DiagnosticIo queue (same path as cmd.txt).</summary>
    internal static void Inject(IList<string> lines)
    {
        var app = RequireApp();
        var io = Reflect.Get<object>(app, "_diagnosticIo") ?? throw new MissingMemberException("DiagnosticIo", "_diagnosticIo");
        var gate = Reflect.Get<object>(io, "_gate") ?? throw new MissingMemberException("DiagnosticIo", "_gate");
        var queue = Reflect.Get<Queue<string>>(io, "_commands") ?? throw new MissingMemberException("DiagnosticIo", "_commands");
        lock (gate)
        {
            if (queue.Count + lines.Count > 64) throw new GateException(429, "Очередь команд Creator Toolkit заполнена (64).");
            foreach (var line in lines) queue.Enqueue(line);
        }
        // PollDiagnostics runs at most every 0.5 s; make the next OnUpdate consume the batch.
        try { Reflect.Set(app, "_nextDiagnostics", 0f); } catch (Exception) { }
    }

    /// <summary>Fallback when reflection injection is unavailable: the documented cmd.txt file.</summary>
    internal static void WriteCommandFile(IList<string> lines)
    {
        Directory.CreateDirectory(DataDirectory);
        string path = Path.Combine(DataDirectory, "cmd.txt");
        File.AppendAllLines(path, lines);
    }

    internal static string ZoneTitle(object zone)
    {
        if (zone == null || MapToolsType == null) return null;
        try { return Reflect.CallStatic(MapToolsType, "ZoneName", zone) as string; } catch (Exception) { return null; }
    }
}
