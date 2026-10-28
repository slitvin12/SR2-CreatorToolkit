using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using SceneContext = Il2Cpp.SceneContext;
using SystemContext = Il2Cpp.SystemContext;
using SRCharacterController = Il2CppMonomiPark.SlimeRancher.Player.CharacterController.SRCharacterController;
using ZoneDefinition = Il2CppMonomiPark.SlimeRancher.World.ZoneDefinition;
using MapUIUtilities = Il2CppMonomiPark.SlimeRancher.UI.Map.MapUIUtilities;
using UiButton = UnityEngine.UI.Button;
using UiSlider = UnityEngine.UI.Slider;
using UiToggle = UnityEngine.UI.Toggle;

namespace CreatorToolkitAgentDirector;

/// <summary>A multi-frame command stepped on the main thread until it completes or times out.</summary>
internal sealed class Job
{
    internal AgentRequest Request;
    internal string Name;
    internal double Started, Deadline;
    internal int StartFrame, Stage, StageFrame;
    internal bool SceneSensitive, Detached;
    internal Func<Job, bool> Step;          // true = finished (request completed or detached)
    internal Action<Job> Cleanup;           // always runs exactly once at the end
    internal Func<Job, string> TimeoutJson; // optional custom 504 body
    internal object State;
    internal int Frames => FrameStats.Frame - StartFrame;
    internal double Elapsed => Clock.Now - Started;
    internal void Next(int stage) { Stage = stage; StageFrame = FrameStats.Frame; }
}

internal static partial class Commands
{
    private static readonly HashSet<string> OffThread = new(StringComparer.Ordinal)
    { "/", "/ping", "/help", "/log", "/errors", "/errors/reset", "/screenshot/status", "/screenshot.png", "/bridge" };

    internal static bool IsOffThread(string path) => OffThread.Contains(path);

    internal static double DefaultTimeout(string path) => path switch
    {
        "/wait" => 30, "/screenshot" => 15, "/toolkit" => 30, "/ui/click" => 10, "/teleport" => 10, "/reset" => 10, _ => 10
    };

    /// <summary>How long the transport waits for the main thread (job budget + grace).</summary>
    internal static int TransportBudgetMs(AgentRequest r)
    {
        if (IsOffThread(r.Path)) return 15000;
        double t;
        try { t = r.TimeoutSeconds(DefaultTimeout(r.Path), 300); } catch (ArgumentException) { t = DefaultTimeout(r.Path); }
        return (int)(t * 1000) + 10000;
    }

    // ---- response helpers --------------------------------------------------------------------
    internal static JsonWriter Begin(AgentRequest r, bool ok = true)
    {
        var w = new JsonWriter();
        w.Obj().P("ok", ok).P("id", r.Id).P("path", r.Path).P("frame", FrameStats.Frame);
        return w;
    }
    internal static void Done(AgentRequest r, JsonWriter w, int status = 200)
    {
        w.EndObj();
        r.Json(status, w.ToString());
    }
    internal static int StatusFor(Exception e) => e switch
    {
        GateException g => g.Code,
        ArgumentException => 400,
        MissingMemberException => 501,
        NotSupportedException => 501,
        TimeoutException => 504,
        InvalidOperationException => 409,
        _ => 500
    };
    internal static void Fail(AgentRequest r, Exception e)
    {
        e = Reflect.Unwrap(e);
        int code = StatusFor(e);
        var w = new JsonWriter(512);
        w.Obj().P("ok", false).P("id", r.Id).P("path", r.Path).P("status", code).P("error", e.Message).P("type", e.GetType().Name);
        if (code >= 500 && code != 503 && code != 504 && e.StackTrace != null)
        {
            string stack = e.StackTrace;
            w.P("stack", stack.Length > 1500 ? stack.Substring(0, 1500) : stack);
            AgentDirectorMod.Log.Warning("[" + r.Path + "] " + e.GetType().Name + ": " + e.Message);
        }
        w.EndObj();
        r.Json(code, w.ToString());
    }

    /// <summary>Writes one JSON value; a failing section becomes {"error":...} instead of breaking the reply.</summary>
    internal static void Section(JsonWriter w, string name, Action<JsonWriter> body)
    {
        string json;
        try { var sub = new JsonWriter(512); body(sub); json = sub.ToString(); }
        catch (Exception e) { e = Reflect.Unwrap(e); json = "{\"error\":" + JsonWriter.Quoted(e.GetType().Name + ": " + e.Message) + "}"; }
        w.Name(name).Raw(json);
    }

    internal static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var chars = new System.Text.StringBuilder(s.Length);
        bool space = false;
        foreach (char raw in s.Trim())
        {
            char c = char.ToLowerInvariant(raw == 'ё' || raw == 'Ё' ? 'е' : raw);
            if (char.IsWhiteSpace(c)) { if (!space) chars.Append(' '); space = true; continue; }
            space = false; chars.Append(c);
        }
        return chars.ToString();
    }

    // ---- dispatch ----------------------------------------------------------------------------
    internal static void HandleOffThread(AgentRequest r)
    {
        switch (r.Path)
        {
            case "/":
            case "/help":
                if (r.Args.Str("format") == "json") { var w = Begin(r); w.Strings("endpoints", Help.Endpoints); Done(r, w); }
                else r.Text(200, Help.Text, "text/markdown; charset=utf-8");
                return;
            case "/ping":
            case "/bridge": Ping(r); return;
            case "/log": LogTail(r); return;
            case "/errors/reset": ErrorWatch.Reset(); Errors(r); return;
            case "/errors": Errors(r); return;
            case "/screenshot/status": Shots.Status(r); return;
            case "/screenshot.png": Shots.Png(r); return;
        }
        throw new GateException(404, "Неизвестная команда " + r.Path);
    }

    /// <summary>Main thread. Returns a Job for multi-frame commands, or null when already answered.</summary>
    internal static Job Handle(AgentRequest r)
    {
        switch (r.Path)
        {
            case "/state": State(r); return null;
            case "/ui":
            case "/ui/imgui": UiList(r); return null;
            case "/ui/canvas": UiCanvas(r); return null;
            case "/ui/click":
            case "/click": return Click(r);
            case "/tab": return Tab(r);
            case "/menu": return Menu(r);
            case "/screenshot": return Shots.Capture(r);
            case "/spawn/list": Spawner.List(r); return null;
            case "/spawn": Spawner.Spawn(r); return null;
            case "/testpoint": TestPoints.Handle(r); return null;
            case "/testpoints": TestPoints.List(r); return null;
            case "/teleport": return Teleporter.Teleport(r, null);
            case "/reset": return Teleporter.Teleport(r, "test");
            case "/toolkit": return Relay(r);
            case "/wait": return Wait(r);
            case "/assert": Assert(r); return null;
        }
        throw new GateException(404, "Неизвестная команда " + r.Path + ". Список команд: /help");
    }

    internal static Job NewJob(AgentRequest r, string name, Func<Job, bool> step, bool sceneSensitive)
    {
        return new Job
        {
            Request = r, Name = name, Step = step, SceneSensitive = sceneSensitive,
            Started = Clock.Now, StartFrame = FrameStats.Frame, StageFrame = FrameStats.Frame,
            Deadline = Clock.Now + r.TimeoutSeconds(DefaultTimeout(r.Path), 300)
        };
    }

    /// <summary>Answers after N frames so the next /ui or /screenshot sees the new IMGUI layout.</summary>
    internal static Job Settle(AgentRequest r, string name, int frames, Action<JsonWriter> write, Func<int> status)
    {
        return NewJob(r, name, j =>
        {
            if (j.Frames < frames) return false;
            int code = status == null ? 200 : status();
            var w = Begin(j.Request, code < 300);
            write(w);
            Done(j.Request, w, code);
            return true;
        }, true);
    }

    // ---- off-thread endpoints ----------------------------------------------------------------
    private static void Ping(AgentRequest r)
    {
        var s = FrameStats.Snap;
        var mod = AgentDirectorMod.I;
        double age = FrameStats.MainThreadAgeMs;
        var w = Begin(r);
        w.P("mod", "CreatorToolkitAgentDirector").P("version", AgentDirectorMod.Version)
         .P("port", mod?.HttpPort ?? 0).P("pid", Environment.ProcessId)
         .P("mainThreadAgeMs", age, 0).P("mainThreadAlive", age < 3000)
         .P("fps", s.Fps, 1).P("fpsAvg1s", s.FpsAvg, 1).P("worstFrameMs1s", s.WorstMs, 1)
         .P("scene", s.Scene).P("sceneGroup", s.SceneGroup).P("loading", s.Loading)
         .P("ready", s.Ready).P("menuOpen", s.MenuOpen).P("focused", s.Focused).P("toolkit", s.ToolkitBound)
         .P("screen", s.Width + "x" + s.Height)
         .P("errors", ErrorWatch.ErrorsSinceReset).P("screenshot", Shots.StateName)
         .P("inbox", mod?.InboxCount ?? 0).P("jobs", mod?.JobCount ?? 0)
         .P("dataDir", mod?.DataDir).P("ipcDir", mod?.IpcDir);
        Done(r, w);
    }

    private static void Errors(AgentRequest r)
    {
        var w = Begin(r);
        w.Name("counters"); ErrorWatch.Write(w, true);
        int limit = Math.Clamp(r.Args.Int("limit", 50), 0, 600);
        w.Name("recent"); ErrorWatch.WriteEntries(w, ErrorWatch.Since(r.Args.Has("since") ? r.Args.Int("since", 0) : ErrorWatch.ResetSerial, r.Args.Str("level", "problems"), r.Args.Str("contains"), limit));
        Done(r, w);
    }

    private static void LogTail(AgentRequest r)
    {
        long since = r.Args.Int("since", 0);
        int limit = Math.Clamp(r.Args.Int("limit", 100), 1, 600);
        var entries = ErrorWatch.Since(since, r.Args.Str("level", "all"), r.Args.First("contains", "source"), limit);
        var w = Begin(r);
        w.P("serial", ErrorWatch.Serial).P("count", entries.Count);
        w.Name("entries"); ErrorWatch.WriteEntries(w, entries);
        Done(r, w);
    }

    // ---- /state ------------------------------------------------------------------------------
    private static void State(AgentRequest r)
    {
        bool ui = r.Args.Bool("ui", true), tree = r.Args.Bool("tree", false), hidden = r.Args.Bool("hidden", false);
        int maxNodes = Math.Clamp(r.Args.Int("max", 1500), 1, 20000), maxDepth = Math.Clamp(r.Args.Int("depth", 24), 1, 64);
        var w = Begin(r);
        w.P("time", DateTime.UtcNow.ToString("O")).P("version", AgentDirectorMod.Version);
        Section(w, "fps", s => s.Obj()
            .P("fps", FrameStats.Fps, 1).P("fpsAvg1s", FrameStats.FpsAvg, 1).P("worstFrameMs1s", FrameStats.WorstMs, 1)
            .P("frameMs", Time.unscaledDeltaTime * 1000f, 2).P("timeScale", Time.timeScale, 2)
            .P("targetFrameRate", Application.targetFrameRate).P("vSync", QualitySettings.vSyncCount)
            .P("realtime", Time.realtimeSinceStartup, 2).EndObj());
        Section(w, "screen", s => s.Obj().P("width", Screen.width).P("height", Screen.height)
            .P("fullScreen", Screen.fullScreen).P("focused", Application.isFocused).EndObj());
        Section(w, "scene", WriteScene);
        Section(w, "zone", WriteZone);
        Section(w, "player", WritePlayer);
        Section(w, "camera", WriteCamera);
        Section(w, "toolkit", WriteToolkit);
        Section(w, "errors", s => ErrorWatch.Write(s, true));
        Section(w, "screenshot", s => { s.Obj(); Shots.WriteFields(s); s.EndObj(); });
        Section(w, "gates", Gates.Write);
        Section(w, "testPoints", TestPoints.WriteNames);
        if (ui) Section(w, "ui", s => WriteUi(s, tree, hidden, maxNodes, maxDepth, r.Args.Str("root")));
        Done(r, w);
    }

    internal static string SceneGroupName()
    {
        try
        {
            var loader = SystemContext.Instance?.SceneLoader;
            if (loader == null || loader.WasCollected) return null;
            var group = loader.CurrentSceneGroup;
            return group == null ? null : group.name;
        }
        catch (Exception) { return null; }
    }
    internal static bool Loading()
    {
        try { var loader = SystemContext.Instance?.SceneLoader; return loader != null && !loader.WasCollected && loader.IsSceneLoadInProgress; }
        catch (Exception) { return false; }
    }
    internal static bool Gameplay()
    {
        try
        {
            var loader = SystemContext.Instance?.SceneLoader;
            return loader != null && !loader.WasCollected && !loader.IsSceneLoadInProgress && loader.IsCurrentSceneGroupGameplay();
        }
        catch (Exception) { return false; }
    }

    private static void WriteScene(JsonWriter s)
    {
        s.Obj();
        var active = SceneManager.GetActiveScene();
        s.P("active", active.name);
        s.Arr("loaded");
        int count = SceneManager.sceneCount;
        for (int i = 0; i < count && i < 64; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            s.Val(scene.isLoaded ? scene.name : scene.name + " (loading)");
        }
        s.EndArr();
        s.P("group", SceneGroupName()).P("loading", Loading()).P("gameplay", Gameplay());
        var mod = AgentDirectorMod.I;
        s.P("serial", mod?.SceneSerial ?? 0).P("lastEvent", mod?.LastSceneEvent);
        s.EndObj();
    }

    private static void WriteZone(JsonWriter s)
    {
        ZoneDefinition zone = null;
        string source = null;
        var ctx = SceneContext.Instance;
        var tracker = ctx == null ? null : ctx.PlayerZoneTracker;
        if (tracker != null)
        {
            zone = tracker.GetCurrentZone(); source = "current";
            if (zone == null) { zone = tracker.GetLatestZone(); source = "latest"; }
        }
        if (zone == null)
        {
            try { zone = MapUIUtilities.GetPlayerCurrentZone(); source = "map"; } catch (Exception) { zone = null; }
        }
        s.Obj();
        if (zone == null || zone.WasCollected) s.P("id", (string)null).P("title", (string)null);
        else s.P("id", zone.name).P("title", Toolkit.ZoneTitle(zone)).P("source", source);
        s.EndObj();
    }

    internal static GameObject PlayerObject()
    {
        var ctx = SceneContext.Instance;
        var player = ctx == null ? null : ctx.Player;
        return player == null || player.WasCollected ? null : player;
    }

    internal static SRCharacterController Controller(GameObject player)
    {
        if (player == null) return null;
        var c = player.GetComponentInChildren<SRCharacterController>(true);
        return c == null || c.WasCollected ? null : c;
    }

    /// <summary>Character-motor position (capsule base) when available, else the player transform.</summary>
    internal static Vector3 PlayerPosition(GameObject player)
    {
        try
        {
            var motor = Controller(player)?.Motor;
            if (motor != null) return motor.TransientPosition;
        }
        catch (Exception) { }
        return player.transform.position;
    }

    private static void WritePlayer(JsonWriter s)
    {
        var player = PlayerObject();
        s.Obj();
        if (player == null) { s.P("present", false).EndObj(); return; }
        var p = PlayerPosition(player);
        var t = player.transform;
        s.P("present", true).P("x", p.x, 3).P("y", p.y, 3).P("z", p.z, 3).P("yaw", t.eulerAngles.y, 1);
        var tp = t.position;
        s.Obj("transform").P("x", tp.x, 3).P("y", tp.y, 3).P("z", tp.z, 3).EndObj();
        try
        {
            var state = SceneContext.Instance.PlayerState;
            if (state != null)
                s.P("health", Convert.ToDouble(state.GetCurrHealth()), 1).P("maxHealth", Convert.ToDouble(state.GetMaxHealth()), 1)
                 .P("energy", Convert.ToDouble(state.GetCurrEnergy()), 1).P("maxEnergy", Convert.ToDouble(state.GetMaxEnergy()), 1);
        }
        catch (Exception e) { s.P("stateError", e.Message); }
        try
        {
            var motor = Controller(player)?.Motor;
            if (motor != null) s.P("speed", motor.BaseVelocity.magnitude, 2).P("grounded", motor.GroundingStatus.IsStableOnGround);
        }
        catch (Exception) { }
        s.EndObj();
    }

    private static void WriteCamera(JsonWriter s)
    {
        var cam = Camera.main;
        s.Obj();
        if (cam == null) { s.P("present", false).EndObj(); return; }
        var t = cam.transform;
        var p = t.position; var e = t.eulerAngles;
        s.P("present", true).P("x", p.x, 3).P("y", p.y, 3).P("z", p.z, 3)
         .P("pitch", e.x > 180 ? e.x - 360 : e.x, 1).P("yaw", e.y, 1).P("fov", cam.fieldOfView, 1);
        s.EndObj();
    }

    internal static void WriteToolkitBrief(JsonWriter s)
    {
        s.Obj();
        if (!Toolkit.TryBind() || Toolkit.App == null) { s.P("loaded", false).P("error", Toolkit.BindError).EndObj(); return; }
        int tab = Toolkit.Tab;
        var tabs = Toolkit.Tabs;
        s.P("ready", Toolkit.Ready).P("menuOpen", Toolkit.MenuOpen).P("tab", tab)
         .P("tabName", tab >= 0 && tab < tabs.Length ? tabs[tab] : null)
         .P("status", Toolkit.Status).P("lastError", Toolkit.LastError).P("pending", Toolkit.PendingTitle)
         .P("lastUiNavigation", Reflect.Get<string>(Toolkit.App, "_lastUiNavigation"));
        s.EndObj();
    }

    private static void WriteToolkit(JsonWriter s)
    {
        s.Obj();
        bool bound = Toolkit.TryBind();
        s.P("loaded", bound);
        var app = bound ? Toolkit.App : null;
        if (app == null) { s.P("initialized", false).P("error", bound ? "ToolkitMod.I == null" : Toolkit.BindError).EndObj(); return; }
        int tab = Toolkit.Tab;
        var tabs = Toolkit.Tabs;
        s.P("initialized", true).P("version", Toolkit.Version)
         .P("ready", Toolkit.Ready).P("menuOpen", Toolkit.MenuOpen)
         .P("tab", tab).P("tabName", tab >= 0 && tab < tabs.Length ? tabs[tab] : null).Strings("tabs", tabs)
         .P("status", Toolkit.Status).P("lastError", Toolkit.LastError).P("pending", Toolkit.PendingTitle)
         .P("failureSerial", Reflect.Get(app, "FailureSerial", 0))
         .P("lastUiNavigation", Reflect.Get<string>(app, "_lastUiNavigation"));
        var map = Reflect.Get<object>(app, "Map");
        if (map != null)
            s.Obj("map").P("nativeMapOpen", Reflect.Get(map, "NativeMapOpen", false)).P("panelExpanded", Reflect.Get(map, "PanelExpanded", false))
             .P("busy", Reflect.Get(map, "Busy", false)).EndObj();
        s.Obj("last");
        foreach (var kv in Toolkit.Lasts()) if (kv.Key.StartsWith("m2", StringComparison.Ordinal)) s.P(kv.Key, kv.Value);
        s.EndObj();
        s.Obj("recorder").P("patched", string.Join(",", UiRecorder.Patched)).P("missing", string.Join(",", UiRecorder.Missing))
         .P("error", UiRecorder.InstallError).P("passes", UiRecorder.Passes).P("pressSerial", UiRecorder.PressSerial).EndObj();
        s.EndObj();
    }

    private static void WriteUi(JsonWriter s, bool tree, bool hidden, int maxNodes, int maxDepth, string root)
    {
        s.Obj();
        var items = UiRecorder.Snapshot();
        int visible = 0;
        foreach (var it in items) if (it.Visible) visible++;
        s.Obj("imgui").P("frame", UiRecorder.PublishedFrame).P("ageFrames", UiRecorder.PublishedFrame < 0 ? -1 : FrameStats.Frame - UiRecorder.PublishedFrame)
         .P("count", items.Count).P("visible", visible).P("overflow", UiRecorder.Overflow);
        s.Arr("elements");
        foreach (var it in items) if (hidden || it.Visible) UiRecorder.WriteItem(s, it);
        s.EndArr().EndObj();
        s.Name("canvasRoots"); CanvasTree.WriteRoots(s);
        if (tree)
        {
            s.Name("canvasTree");
            int left = CanvasTree.WriteTree(s, maxNodes, maxDepth, true, root);
            s.P("canvasTreeTruncated", left <= 0);
        }
        s.EndObj();
    }

    // ---- /ui ---------------------------------------------------------------------------------
    private static bool ItemMatches(ImguiView v, string kind, string label, bool exact)
    {
        if (kind != null && !string.Equals(v.Kind, kind, StringComparison.OrdinalIgnoreCase)) return false;
        if (label == null) return true;
        string n = Norm(v.Label);
        return exact ? n == label : n.Contains(label, StringComparison.Ordinal);
    }

    private static void UiList(AgentRequest r)
    {
        bool hidden = r.Args.Bool("hidden", false);
        string kind = r.Args.Str("kind");
        string label = r.Args.Has("label") ? Norm(r.Args.Str("label")) : null;
        var items = UiRecorder.Snapshot();
        var w = Begin(r);
        w.P("imguiFrame", UiRecorder.PublishedFrame).P("ageFrames", UiRecorder.PublishedFrame < 0 ? -1 : FrameStats.Frame - UiRecorder.PublishedFrame);
        Section(w, "toolkit", WriteToolkitBrief);
        w.Arr("imgui");
        foreach (var it in items) if ((hidden || it.Visible) && ItemMatches(it, kind, label, false)) UiRecorder.WriteItem(w, it);
        w.EndArr();
        if (r.Args.Bool("canvas", false))
        {
            w.Arr("canvas");
            foreach (var h in CanvasTree.Selectables(hidden))
                if (label == null || Norm(h.Text).Contains(label, StringComparison.Ordinal) || Norm(h.Name).Contains(label, StringComparison.Ordinal))
                    CanvasTree.WriteHit(w, h);
            w.EndArr();
        }
        Done(r, w);
    }

    private static void UiCanvas(AgentRequest r)
    {
        bool hidden = r.Args.Bool("hidden", false);
        var w = Begin(r);
        w.Arr("selectables");
        foreach (var h in CanvasTree.Selectables(hidden)) CanvasTree.WriteHit(w, h);
        w.EndArr();
        if (r.Args.Bool("tree", true))
        {
            w.Name("tree");
            int left = CanvasTree.WriteTree(w, Math.Clamp(r.Args.Int("max", 1500), 1, 20000), Math.Clamp(r.Args.Int("depth", 24), 1, 64), r.Args.Bool("rects", true), r.Args.Str("root"));
            w.P("truncated", left <= 0);
        }
        Done(r, w);
    }

    // ---- /ui/click ---------------------------------------------------------------------------
    private sealed class ClickState
    {
        internal string Mode, Via = "";
        internal ImguiView Item;
        internal CanvasHit Hit;
        internal Vector2 Screen;
        internal long PressBefore;
        internal bool Registered, Armed;
    }
    private static bool _clickBusy;

    private static ImguiView FindImgui(Args a)
    {
        if (UiRecorder.Patched.Count == 0)
            throw new GateException(503, "Запись IMGUI Creator Toolkit не установлена: " + (UiRecorder.InstallError.Length > 0 ? UiRecorder.InstallError : Toolkit.BindError));
        if (UiRecorder.PublishedFrame < 0 || FrameStats.Frame - UiRecorder.PublishedFrame > 30)
            throw new GateException(409, "Интерфейс Creator Toolkit не отрисовывался недавно.");
        var items = UiRecorder.Snapshot();
        bool hidden = a.Bool("hidden", false);
        if (a.Has("id"))
        {
            int id = a.Int("id", -1);
            var byId = items.Find(i => i.Id == id) ?? throw new GateException(404, "Нет элемента id=" + id + " в текущем кадре (всего " + items.Count + "). Обновите /ui.");
            if (!byId.Visible && !hidden) throw new GateException(409, "Элемент id=" + id + " скрыт (вне области прокрутки). hidden=1 + mode=force для принудительного нажатия.");
            return byId;
        }
        string kind = a.Str("kind");
        int index = a.Has("index") ? a.Int("index", -1) : int.MinValue;
        string label = a.Has("label") ? Norm(a.Str("label")) : null;
        if (label == null && index == int.MinValue)
            throw new ArgumentException("Укажите label=, id=, index= (+kind=), либо x=&y=, либо path=/name=/text= для нативного uGUI.");
        var pool = items.FindAll(i => (hidden || i.Visible) && (index == int.MinValue || i.Index == index));
        var matches = pool.FindAll(i => ItemMatches(i, kind, label, true));
        if (matches.Count == 0) matches = pool.FindAll(i => ItemMatches(i, kind, label, false));
        int nth = a.Int("nth", 0);
        if (matches.Count == 0 || nth < 0 || nth >= matches.Count)
        {
            var names = new List<string>();
            foreach (var i in items) if (i.Visible && i.Label.Length > 0 && names.Count < 40) names.Add(i.Kind + ":" + i.Label);
            throw new GateException(404, "Элемент не найден (совпадений " + matches.Count + ", nth=" + nth + "). Видимые: " + string.Join(" | ", names));
        }
        return matches[nth];
    }

    private static CanvasHit FindCanvas(Args a)
    {
        bool hidden = a.Bool("hidden", false);
        string path = a.Str("path"), name = a.Has("name") ? Norm(a.Str("name")) : null, text = a.Has("text") ? Norm(a.Str("text")) : null;
        var all = CanvasTree.Selectables(hidden);
        if (a.Has("cid"))
        {
            int cid = a.Int("cid", -1);
            return all.Find(h => h.Id == cid)
                ?? throw new GateException(404, "uGUI-элемент cid=" + cid + " не найден (всего " + all.Count + "). Свежий список: /ui/canvas");
        }
        var matches = all.FindAll(h =>
            (path == null || h.Path.Equals(path, StringComparison.OrdinalIgnoreCase) || h.Path.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase)) &&
            (name == null || Norm(h.Name) == name) &&
            (text == null || Norm(h.Text).Contains(text, StringComparison.Ordinal)));
        int nth = a.Int("nth", 0);
        if (matches.Count == 0 || nth < 0 || nth >= matches.Count)
        {
            var names = new List<string>();
            foreach (var h in all) if (names.Count < 30) names.Add(h.Name + (string.IsNullOrEmpty(h.Text) ? "" : "('" + h.Text + "')"));
            throw new GateException(404, "uGUI-элемент не найден. Активные: " + string.Join(" | ", names));
        }
        return matches[nth];
    }

    private static Job Click(AgentRequest r)
    {
        Gates.RequireOptIn("ui/click");
        if (_clickBusy) throw new GateException(409, "Предыдущий клик ещё выполняется.");
        string mode = (r.Args.Str("mode", "auto") ?? "auto").Trim().ToLowerInvariant();
        if (mode != "auto" && mode != "pointer" && mode != "force" && mode != "invoke")
            throw new ArgumentException("mode = auto | pointer | force | invoke");
        var st = new ClickState();
        if (r.Args.Has("x") && r.Args.Has("y"))
        {
            if (mode == "force" || mode == "invoke") throw new ArgumentException("Клик по координатам только mode=pointer.");
            st.Screen = new Vector2(r.Args.Float("x", 0), r.Args.Float("y", 0));
            mode = "pointer";
        }
        else if (r.Args.First("cid", "path", "name", "text") != null)
        {
            st.Hit = FindCanvas(r.Args);
            st.Screen = st.Hit.Center;
            if (mode == "auto") mode = "invoke";
            if (mode == "force") throw new ArgumentException("mode=force только для элементов Creator Toolkit (IMGUI).");
        }
        else
        {
            st.Item = FindImgui(r.Args);
            st.Screen = st.Item.Center;
            if (mode == "invoke") mode = "force";
            if (!st.Item.Visible && mode != "force") throw new GateException(409, "Скрытый элемент можно нажать только mode=force.");
        }
        st.Mode = mode;
        st.PressBefore = UiRecorder.PressSerial;
        var job = NewJob(r, "click", ClickStep, true);
        job.State = st;
        job.Cleanup = j =>
        {
            var s = (ClickState)j.State;
            if (s.Armed && UiRecorder.ForceArmed) UiRecorder.ForceArmed = false;
            InputSim.ReleaseIfHeld();
            _clickBusy = false;
        };
        if (mode == "invoke") { Invoke(st, r.Args); job.Next(20); }
        else if (mode == "force") { Arm(st); job.Next(10); }
        else
        {
            if (InputSim.RealButtonHeld()) throw new GateException(409, "Реальная левая кнопка мыши зажата — клик отменён.");
            InputSim.Send(st.Screen, false);
            job.Next(1);
        }
        _clickBusy = true;
        return job;
    }

    private static void Arm(ClickState st)
    {
        UiRecorder.ForceGlobal = st.Item.Global;
        UiRecorder.ForceApplied = false;
        UiRecorder.ForceArmed = true;
        st.Armed = true;
    }

    private static void Invoke(ClickState st, Args a)
    {
        var sel = st.Hit.Selectable;
        if (sel == null || sel.WasCollected || !sel.isActiveAndEnabled) throw new GateException(409, "uGUI-элемент больше не активен.");
        if (!sel.IsInteractable()) throw new GateException(409, "uGUI-элемент неинтерактивен (interactable=false).");
        var button = sel.TryCast<UiButton>();
        if (button != null) { button.onClick.Invoke(); st.Via = "invoke:onClick"; st.Registered = true; return; }
        var toggle = sel.TryCast<UiToggle>();
        if (toggle != null) { toggle.isOn = a.Has("on") ? a.Bool("on", true) : !toggle.isOn; st.Via = "invoke:isOn"; st.Registered = true; return; }
        var slider = sel.TryCast<UiSlider>();
        if (slider != null && a.Has("value")) { slider.value = a.Float("value", slider.value); st.Via = "invoke:value"; st.Registered = true; return; }
        throw new GateException(409, "Для этого типа (" + st.Hit.Kind + ") нет invoke; используйте mode=pointer.");
    }

    private static bool Overlaps(Rect a, Rect b) =>
        a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height;

    private static bool ClickStep(Job j)
    {
        var st = (ClickState)j.State;
        int frame = FrameStats.Frame;
        switch (j.Stage)
        {
            case 1: // pointer moved (button up); press on the next frame
                if (frame <= j.StageFrame) return false;
                InputSim.Send(st.Screen, true);
                j.Next(2);
                return false;
            case 2: // wait for the toolkit to register the press, then release
            {
                bool registered = UiRecorder.PressSerial != st.PressBefore && (st.Item == null || Overlaps(UiRecorder.LastPressGlobal, st.Item.Global));
                if (!registered && frame - j.StageFrame < 4) return false;
                InputSim.Send(st.Screen, false);
                st.Registered = registered;
                st.Via = "pointer";
                if (!registered && st.Mode == "auto" && st.Item != null) { Arm(st); st.Via = "force"; j.Next(10); return false; }
                j.Next(20);
                return false;
            }
            case 10: // forced press armed; applied when the control is drawn
                if (UiRecorder.ForceApplied && st.Armed)
                {
                    st.Registered = true; st.Via = st.Mode == "force" ? "force" : "pointer→force";
                    j.Next(20);
                    return false;
                }
                if (frame - j.StageFrame > 20)
                {
                    UiRecorder.ForceArmed = false;
                    throw new GateException(409, "Элемент не отрисован после взведения клика (меню закрыто или раскладка изменилась).");
                }
                return false;
            case 20: // settle two frames so the reply reflects the result
            {
                if (frame - j.StageFrame < 2) return false;
                var w = Begin(j.Request);
                w.Name("target");
                if (st.Item != null) UiRecorder.WriteItem(w, st.Item);
                else if (st.Hit != null) CanvasTree.WriteHit(w, st.Hit);
                else w.Obj().P("x", st.Screen.x, 0).P("y", st.Screen.y, 0).EndObj();
                w.P("mode", st.Mode).P("via", st.Via).P("registered", st.Registered)
                 .P("frames", j.Frames).P("ms", j.Elapsed * 1000, 0);
                Section(w, "toolkit", WriteToolkitBrief);
                Done(j.Request, w);
                return true;
            }
        }
        return false;
    }

    // ---- /tab, /menu -------------------------------------------------------------------------
    private static int ResolveTab(Args a, string[] tabs)
    {
        string raw = a.First("index", "name", "tab", "body");
        if (raw == null) throw new ArgumentException("Укажите index=0.." + (tabs.Length - 1) + " или name=" + string.Join("|", tabs));
        raw = raw.Trim();
        if (int.TryParse(raw, out int n))
        {
            if (n < 0 || n >= tabs.Length) throw new ArgumentException("Вкладка " + n + " вне диапазона 0.." + (tabs.Length - 1));
            return n;
        }
        string norm = Norm(raw);
        for (int i = 0; i < tabs.Length; i++) if (Norm(tabs[i]) == norm) return i;
        for (int i = 0; i < tabs.Length; i++) if (Norm(tabs[i]).StartsWith(norm, StringComparison.Ordinal)) return i;
        throw new GateException(404, "Нет вкладки '" + raw + "'. Доступны: " + string.Join(", ", tabs));
    }

    private static void Navigate(object app, string command, Action navigate)
    {
        if (Reflect.Method(Toolkit.ModType, "NavigateMenu", 2) != null) Reflect.Call(app, "NavigateMenu", command, navigate);
        else
        {
            navigate();
            Reflect.Call(Reflect.Get<object>(app, "Input"), "Open");
        }
    }

    private static Job Tab(AgentRequest r)
    {
        var app = Toolkit.RequireReady();
        var tabs = Toolkit.Tabs;
        int index = ResolveTab(r.Args, tabs);
        Navigate(app, "agent-tab " + index, () => Reflect.Call(app, "ShowTabForTest", index));
        return Settle(r, "tab", 2, w =>
        {
            w.P("requested", index).P("requestedName", tabs[index]);
            Section(w, "toolkit", WriteToolkitBrief);
        }, () => Toolkit.Tab == index && Toolkit.MenuOpen ? 200 : 409);
    }

    private static Job Menu(AgentRequest r)
    {
        var app = Toolkit.RequireApp();
        string action = (r.Args.First("action", "do", "body") ?? (r.Args.Has("open") ? (r.Args.Bool("open", true) ? "open" : "close") : "toggle")).Trim().ToLowerInvariant();
        bool open = Toolkit.MenuOpen;
        bool want = action switch
        {
            "open" or "show" or "1" => true,
            "close" or "hide" or "0" => false,
            "toggle" => !open,
            _ => throw new ArgumentException("action = open | close | toggle")
        };
        if (want && !open)
        {
            Toolkit.RequireReady();
            Navigate(app, "agent-menu-open", () => { });
        }
        else if (!want && open) Reflect.Call(app, "CloseMenu");
        return Settle(r, "menu", 2, w =>
        {
            w.P("action", action).P("wanted", want);
            Section(w, "toolkit", WriteToolkitBrief);
        }, () => Toolkit.MenuOpen == want ? 200 : 409);
    }

    // ---- /wait -------------------------------------------------------------------------------
    private static Job Wait(AgentRequest r)
    {
        int frames = Math.Max(0, r.Args.Int("frames", 0));
        float seconds = Math.Max(0f, r.Args.Float("seconds", 0f));
        string until = (r.Args.Str("until") ?? "").Trim().ToLowerInvariant();
        int tab = r.Args.Int("tab", -1);
        Func<bool> condition = until switch
        {
            "" => null,
            "ready" => () => Toolkit.Ready,
            "menu" => () => Toolkit.MenuOpen,
            "nomenu" => () => !Toolkit.MenuOpen,
            "gameplay" => Gameplay,
            "loaded" => () => !Loading(),
            "player" => () => PlayerObject() != null,
            "screenshot" or "capture" => () => !Shots.Pending,
            "tab" => () => Toolkit.MenuOpen && Toolkit.Tab == tab,
            "focus" => () => Application.isFocused,
            "noerrors" => () => ErrorWatch.ErrorsSinceReset == 0,
            "ui" => () => UiPresent(r.Args.Str("label"), r.Args.Str("kind"), r.Args.Bool("canvas", false)),
            "noui" => () => !UiPresent(r.Args.Str("label"), r.Args.Str("kind"), r.Args.Bool("canvas", false)),
            "stable" => StableCondition(Math.Clamp(r.Args.Int("quiet", 10), 2, 600)),
            "near" => NearCondition(r.Args),
            _ => throw new ArgumentException("until = ready | menu | nomenu | gameplay | loaded | player | screenshot | tab (+tab=N) | focus | noerrors | ui (+label=, kind=, canvas=1) | noui (+label=) | stable (+quiet=N) | near (+point=, radius=)")
        };
        if (frames == 0 && seconds <= 0 && condition == null) frames = 1;
        var job = NewJob(r, "wait", j =>
        {
            if (j.Frames < frames || j.Elapsed < seconds) return false;
            bool met;
            try { met = condition == null || condition(); } catch (Exception) { met = false; }
            if (!met) return false;
            var w = Begin(j.Request);
            w.P("until", until).P("waitedFrames", j.Frames).P("waitedMs", j.Elapsed * 1000, 0)
             .P("ready", Toolkit.Ready).P("menuOpen", Toolkit.MenuOpen).P("scene", SceneManager.GetActiveScene().name).P("group", SceneGroupName());
            Done(j.Request, w);
            return true;
        }, false);
        job.TimeoutJson = j => "{\"ok\":false,\"status\":504,\"error\":" + JsonWriter.Quoted("Условие '" + until + "' не выполнено за " + j.Elapsed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " с") + ",\"waitedFrames\":" + j.Frames + "}";
        return job;
    }

    // ---- /toolkit relay ----------------------------------------------------------------------
    private sealed class RelayState
    {
        internal List<string> Commands;
        internal Dictionary<string, string> Before;
        internal long LogSerial;
        internal DateTime StartUtc;
        internal string Via, Until;
        internal double Wait;
        internal bool ReadStatus;
    }

    private static Job Relay(AgentRequest r)
    {
        Toolkit.RequireApp();
        var commands = new List<string>();
        foreach (var source in new[] { r.Args.Str("cmd"), r.Args.Str("body") })
        {
            if (string.IsNullOrEmpty(source)) continue;
            foreach (var part in source.Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string c = part.Trim();
                if (c.Length == 0) continue;
                if (c.Length > 512) throw new ArgumentException("Команда длиннее 512 символов.");
                foreach (char ch in c) if (char.IsControl(ch)) throw new ArgumentException("Управляющие символы в команде запрещены.");
                commands.Add(c);
            }
        }
        bool readStatus = r.Args.Bool("status", false) || r.Args.Bool("read", false);
        if (commands.Count == 0 && !readStatus) throw new ArgumentException("Укажите cmd=<команда Creator Toolkit> (несколько — через ';') или status=1.");
        if (commands.Count > 16) throw new ArgumentException("Не более 16 команд за запрос.");
        var st = new RelayState
        {
            Commands = commands, Before = Toolkit.Lasts(), LogSerial = ErrorWatch.Serial, StartUtc = DateTime.UtcNow,
            Until = r.Args.Str("until"), Wait = Math.Clamp(r.Args.Float("wait", commands.Count == 0 ? 0f : 1f), 0f, 290f), ReadStatus = readStatus, Via = "none"
        };
        if (commands.Count > 0)
        {
            try { Toolkit.Inject(commands); st.Via = "diagnostic-queue"; }
            catch (GateException) { throw; }
            catch (Exception e) { Toolkit.WriteCommandFile(commands); st.Via = "cmd.txt (" + Reflect.Unwrap(e).GetType().Name + ")"; }
        }
        var job = NewJob(r, "toolkit", j =>
        {
            var s = (RelayState)j.State;
            if (j.Frames < (s.Commands.Count == 0 ? 0 : 3)) return false;
            if (!string.IsNullOrEmpty(s.Until))
            {
                if (!Changed(s, s.Until)) return false;
            }
            else if (j.Elapsed < s.Wait) return false;
            j.Request.Json(200, RelayReply(j, true));
            return true;
        }, false);
        job.State = st;
        job.TimeoutJson = j => RelayReply(j, false);
        return job;
    }

    private static bool Changed(RelayState s, string key)
    {
        var now = Toolkit.Lasts();
        foreach (var kv in now)
        {
            if (key != "any" && !kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            s.Before.TryGetValue(kv.Key, out string old);
            if (old != kv.Value) return true;
        }
        return false;
    }

    private static string RelayReply(Job j, bool ok)
    {
        var s = (RelayState)j.State;
        var w = Begin(j.Request, ok);
        if (!ok) w.P("status", 504).P("error", "Изменение '" + s.Until + "' не наступило до тайм-аута.");
        w.Strings("commands", s.Commands).P("via", s.Via).P("waitedMs", j.Elapsed * 1000, 0).P("frames", j.Frames);
        var after = Toolkit.Lasts();
        w.Obj("last");
        foreach (var kv in after) w.P(kv.Key, kv.Value);
        w.EndObj();
        w.Arr("changed");
        foreach (var kv in after) { s.Before.TryGetValue(kv.Key, out string old); if (old != kv.Value) w.Val(kv.Key); }
        w.EndArr();
        w.Name("log"); ErrorWatch.WriteEntries(w, ErrorWatch.Since(s.LogSerial, "all", null, 200));
        w.Arr("files");
        try
        {
            var dir = new DirectoryInfo(Toolkit.DataDirectory);
            if (dir.Exists)
                foreach (var f in dir.GetFiles())
                    if (f.LastWriteTimeUtc >= s.StartUtc.AddSeconds(-0.5))
                        w.Obj().P("name", f.Name).P("bytes", f.Length).P("path", f.FullName).EndObj();
        }
        catch (Exception) { }
        w.EndArr();
        if (s.ReadStatus)
        {
            string path = Path.Combine(Toolkit.DataDirectory, "status.txt");
            string text = null;
            try
            {
                if (File.Exists(path))
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(fs);
                    text = reader.ReadToEnd();
                    if (text.Length > 65536) text = text.Substring(0, 65536);
                }
            }
            catch (Exception e) { text = "read error: " + e.Message; }
            w.P("statusTxt", text);
        }
        w.EndObj();
        return w.ToString();
    }
}
