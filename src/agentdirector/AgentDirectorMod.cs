using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(CreatorToolkitAgentDirector.AgentDirectorMod), "Creator Toolkit Agent Director", CreatorToolkitAgentDirector.AgentDirectorMod.Version, "slitvin12")]
[assembly: MelonGame("MonomiPark", "SlimeRancher2")]
[assembly: AssemblyTitle("CreatorToolkitAgentDirector")]
[assembly: AssemblyDescription("Local HTTP/file bridge for automated AI-agent testing of Creator Toolkit (SR2).")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1.0")]

namespace CreatorToolkitAgentDirector;

/// <summary>
/// Companion mod for Creator Toolkit: a loopback HTTP bridge (127.0.0.1:9898) plus a file IPC
/// fallback that lets an AI agent observe (state JSON, UI tree, screenshots, error counters) and
/// drive (UI clicks, tabs, test spawns, player reset) the game. All Unity/Il2Cpp work happens on
/// the main thread; transport threads only parse requests and wait for completion.
/// </summary>
public sealed class AgentDirectorMod : MelonMod
{
    internal const string Version = "1.1.0";
    internal static AgentDirectorMod I;
    internal static MelonLogger.Instance Log;

    private MelonPreferences_Category _prefs;
    private MelonPreferences_Entry<int> _port;
    private MelonPreferences_Entry<bool> _http, _file, _optIn, _testSave;
    internal bool RequireOptIn => _optIn == null || _optIn.Value;
    internal bool RequireTestSave => _testSave == null || _testSave.Value;

    internal string DataDir, IpcDir;
    private HttpBridge _httpBridge;
    private FileBridge _fileBridge;
    internal int HttpPort => _httpBridge?.Port ?? 0;

    private readonly ConcurrentQueue<AgentRequest> _inbox = new();
    private readonly List<Job> _jobs = new();
    private volatile int _jobCount;
    internal int InboxCount => _inbox.Count;
    internal int JobCount => _jobCount;

    private volatile bool _shutdown;
    private int _shutdownOnce;
    internal bool ShuttingDown => _shutdown;
    internal volatile int SceneSerial;
    internal volatile string LastSceneEvent = "";

    private bool _recorderInstalled, _wasReady;
    private double _nextBind;
    private int _bindAttempts;

    public override void OnInitializeMelon()
    {
        I = this;
        Log = LoggerInstance;
        _prefs = MelonPreferences.CreateCategory("CreatorToolkitAgent", "Creator Toolkit Agent Director");
        _port = _prefs.CreateEntry("Port", 9898, "HTTP port", "Loopback port (127.0.0.1 only). If busy, the next free port up to +7 is used; see UserData/CreatorToolkitAgent/bridge.json.");
        _http = _prefs.CreateEntry("HttpEnabled", true, "HTTP bridge", "Serve the agent API on http://127.0.0.1:<Port>.");
        _file = _prefs.CreateEntry("FileIpcEnabled", true, "File IPC", "Poll UserData/CreatorToolkitAgent/ipc/cmd.txt as a fallback transport.");
        _optIn = _prefs.CreateEntry("RequireOptIn", true, "Require opt-in file", "Clicks, spawns and teleports need UserData/CreatorToolkit/ct-test.enabled.");
        _testSave = _prefs.CreateEntry("RequireTestSave", true, "Require test save", "Spawns and teleports only on Creator Toolkit's dedicated test save.");

        DataDir = Path.Combine(MelonEnvironment.UserDataDirectory, "CreatorToolkitAgent");
        IpcDir = Path.Combine(DataDir, "ipc");
        Shots.Dir = Path.Combine(DataDir, "screenshots");
        TestPoints.FilePath = Path.Combine(DataDir, "testpoints.txt");
        Scenario.Dir = Path.Combine(DataDir, "scenarios");
        try
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(IpcDir);
            Directory.CreateDirectory(Shots.Dir);
            Shots.CleanTemp();
            Scenario.WriteDefaults();
            File.WriteAllText(Path.Combine(DataDir, "AGENT-COMMANDS.md"), Help.Text, new UTF8Encoding(false));
        }
        catch (Exception e) { Log.Warning("data directory: " + e.Message); }

        ErrorWatch.Install(FindLatestLog());
        StartBridges();
    }

    public override void OnLateInitializeMelon()
    {
        ErrorWatch.InstallUnity();
        TryBindToolkit();
    }

    private static string FindLatestLog()
    {
        var candidates = new List<string>();
        try { candidates.Add(Path.Combine(MelonEnvironment.MelonLoaderDirectory, "Latest.log")); } catch (Exception) { }
        try { candidates.Add(Path.Combine(MelonEnvironment.MelonLoaderLogsDirectory, "Latest.log")); } catch (Exception) { }
        try { candidates.Add(Path.Combine(MelonEnvironment.GameRootDirectory, "MelonLoader", "Latest.log")); } catch (Exception) { }
        foreach (var c in candidates) if (File.Exists(c)) return c;
        return candidates.Count > 0 ? candidates[0] : null;
    }

    private void StartBridges()
    {
        if (_http.Value)
        {
            _httpBridge = new HttpBridge(Execute);
            if (_httpBridge.Start(Math.Clamp(_port.Value, 1024, 65000), 8))
                Log.Msg("HTTP bridge: http://127.0.0.1:" + _httpBridge.Port + "/help");
            else
            {
                Log.Warning("HTTP bridge could not bind 127.0.0.1:" + _port.Value + "..+7 (" + _httpBridge.LastError + "); file IPC only.");
                _httpBridge = null;
            }
        }
        if (_file.Value)
        {
            try { _fileBridge = new FileBridge(IpcDir, Execute); _fileBridge.Start(); Log.Msg("File IPC: " + Path.Combine(IpcDir, "cmd.txt")); }
            catch (Exception e) { Log.Warning("file IPC: " + e.Message); _fileBridge = null; }
        }
        WriteBridgeInfo();
    }

    private void WriteBridgeInfo()
    {
        try
        {
            var w = new JsonWriter(512);
            w.Obj().P("mod", "CreatorToolkitAgentDirector").P("version", Version).P("pid", Environment.ProcessId)
             .P("started", DateTime.UtcNow.ToString("O")).P("http", _httpBridge != null)
             .P("port", HttpPort).P("url", _httpBridge == null ? null : "http://127.0.0.1:" + HttpPort)
             .P("fileIpc", _fileBridge != null).P("ipcDir", IpcDir).P("screenshots", Shots.Dir).P("scenarios", Scenario.Dir)
             .P("help", Path.Combine(DataDir, "AGENT-COMMANDS.md")).EndObj();
            string path = Path.Combine(DataDir, "bridge.json"), temp = path + ".tmp";
            File.WriteAllText(temp, w.ToString(), new UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        catch (Exception e) { Log.Warning("bridge.json: " + e.Message); }
    }

    private void TryBindToolkit()
    {
        if (_recorderInstalled || !Toolkit.TryBind()) return;
        _recorderInstalled = true;
        try
        {
            UiRecorder.Install(HarmonyInstance, Toolkit.ModType, Toolkit.UiType);
            Log.Msg("Creator Toolkit " + (Toolkit.Version ?? "?") + " bound; IMGUI hooks: " + string.Join(", ", UiRecorder.Patched) +
                    (UiRecorder.Missing.Count > 0 ? "; unavailable: " + string.Join(", ", UiRecorder.Missing) : ""));
        }
        catch (Exception e)
        {
            UiRecorder.InstallError = e.GetType().Name + ": " + e.Message;
            Log.Warning("IMGUI hooks: " + UiRecorder.InstallError);
        }
    }

    /// <summary>Transport threads: /batch, then the command itself, then after-hooks (shot=, errors during).</summary>
    internal void Execute(AgentRequest r)
    {
        if (Scenario.Paths.Contains(r.Path)) { Scenario.Run(r, Execute); return; }
        bool offThread = Commands.IsOffThread(r.Path);
        long logSerial = ErrorWatch.Serial;
        ExecuteCore(r);
        if (!offThread && !_shutdown) After.Apply(r, logSerial, ExecuteCore);
    }

    /// <summary>Answers off-thread commands directly and queues the rest for OnUpdate.</summary>
    private void ExecuteCore(AgentRequest r)
    {
        if (_shutdown) { r.Json(503, "{\"ok\":false,\"status\":503,\"error\":\"shutting down\"}"); return; }
        if (Commands.IsOffThread(r.Path))
        {
            try { Commands.HandleOffThread(r); }
            catch (Exception e) { Commands.Fail(r, e); }
            return;
        }
        if (_inbox.Count >= 256) { r.Json(503, "{\"ok\":false,\"status\":503,\"error\":\"inbox full (256)\"}"); return; }
        _inbox.Enqueue(r);
        if (r.Await(Commands.TransportBudgetMs(r))) return;
        double age = FrameStats.MainThreadAgeMs;
        string body = "{\"ok\":false,\"status\":504,\"id\":" + r.Id + ",\"path\":" + JsonWriter.Quoted(r.Path) +
                      ",\"error\":" + JsonWriter.Quoted(age > 2000 ? "главный поток игры не отвечает (загрузка/фриз)" : "тайм-аут ожидания ответа") +
                      ",\"mainThreadAgeMs\":" + ((long)age) + "}";
        if (!r.Json(504, body)) r.Await(5000);
    }

    public override void OnUpdate()
    {
        if (_shutdown) return;
        int frame;
        bool publish;
        try { frame = Time.frameCount; publish = FrameStats.Tick(frame, Time.unscaledDeltaTime); }
        catch (Exception) { return; }
        if (!_recorderInstalled && _bindAttempts < 120 && Clock.Now >= _nextBind)
        {
            _bindAttempts++; _nextBind = Clock.Now + 1.0;
            TryBindToolkit();
        }
        if (publish) PublishSnapshot(frame);
        Pump();
        StepJobs();
    }

    private void PublishSnapshot(int frame)
    {
        var s = new FrameSnapshot { Frame = frame, Fps = FrameStats.Fps, FpsAvg = FrameStats.FpsAvg, WorstMs = FrameStats.WorstMs };
        try
        {
            s.Scene = SceneManager.GetActiveScene().name;
            s.Width = Screen.width; s.Height = Screen.height;
            s.Focused = Application.isFocused; s.TimeScale = Time.timeScale;
            s.Realtime = Time.realtimeSinceStartup;
        }
        catch (Exception) { }
        s.SceneGroup = Commands.SceneGroupName() ?? "";
        s.Loading = Commands.Loading();
        s.ToolkitBound = Toolkit.Bound && Toolkit.App != null;
        if (s.ToolkitBound) { s.Ready = Toolkit.Ready; s.MenuOpen = Toolkit.MenuOpen; }
        else s.Ready = Commands.Gameplay();
        FrameStats.Publish(s);
        if (s.Ready && !_wasReady) TestPoints.CaptureStart();
        _wasReady = s.Ready;
    }

    private void Pump()
    {
        int handled = 0;
        while (handled < 32 && _inbox.TryDequeue(out var r))
        {
            handled++;
            if (r.IsCompleted) continue; // transport already timed out
            try
            {
                var job = Commands.Handle(r);
                if (job != null) _jobs.Add(job);
            }
            catch (Exception e) { Commands.Fail(r, e); }
        }
        _jobCount = _jobs.Count;
    }

    private void StepJobs()
    {
        for (int i = 0; i < _jobs.Count; i++)
        {
            var j = _jobs[i];
            bool done;
            try
            {
                if (!j.Detached && j.Request.IsCompleted) done = true; // answered or abandoned by transport
                else if (Clock.Now > j.Deadline) { TimeOut(j); done = true; }
                else done = j.Step(j);
            }
            catch (Exception e)
            {
                if (!j.Request.IsCompleted) Commands.Fail(j.Request, e);
                else Log.Warning("job " + j.Name + ": " + Reflect.Unwrap(e).Message);
                done = true;
            }
            if (!done) continue;
            Finish(j);
            _jobs.RemoveAt(i);
            i--;
        }
        _jobCount = _jobs.Count;
    }

    private static void TimeOut(Job j)
    {
        string body = null;
        try { body = j.TimeoutJson?.Invoke(j); } catch (Exception) { body = null; }
        body ??= "{\"ok\":false,\"status\":504,\"id\":" + j.Request.Id + ",\"path\":" + JsonWriter.Quoted(j.Request.Path) +
                 ",\"error\":" + JsonWriter.Quoted("тайм-аут команды '" + j.Name + "' (stage " + j.Stage + ")") + ",\"frames\":" + j.Frames + "}";
        j.Request.Json(504, body);
    }

    private static void Finish(Job j)
    {
        try { j.Cleanup?.Invoke(j); }
        catch (Exception e) { Log?.Warning("cleanup " + j.Name + ": " + e.Message); }
    }

    private void AbortJobs(string reason, bool sceneSensitiveOnly)
    {
        for (int i = _jobs.Count - 1; i >= 0; i--)
        {
            var j = _jobs[i];
            if (sceneSensitiveOnly && !j.SceneSensitive) continue;
            j.Request.Json(503, "{\"ok\":false,\"status\":503,\"id\":" + j.Request.Id + ",\"path\":" + JsonWriter.Quoted(j.Request.Path) +
                                ",\"error\":" + JsonWriter.Quoted("прервано: " + reason) + "}");
            Finish(j);
            _jobs.RemoveAt(i);
        }
        _jobCount = _jobs.Count;
    }

    // ---- scene changes: never keep references or synthetic input across them ---------------
    public override void OnSceneWasLoaded(int buildIndex, string sceneName) => SceneEvent("loaded", sceneName, false);
    public override void OnSceneWasInitialized(int buildIndex, string sceneName) => SceneEvent("initialized", sceneName, false);
    public override void OnSceneWasUnloaded(int buildIndex, string sceneName) => SceneEvent("unloaded", sceneName, true);

    private void SceneEvent(string kind, string sceneName, bool abort)
    {
        if (_shutdown) return;
        try
        {
            SceneSerial++;
            LastSceneEvent = kind + ":" + sceneName + "@" + FrameStats.Frame;
            if (!abort) return;
            UiRecorder.Reset();
            InputSim.ReleaseIfHeld();
            AbortJobs("выгрузка сцены " + sceneName, true);
        }
        catch (Exception e) { Log.Warning("scene " + kind + " " + sceneName + ": " + e.Message); }
    }

    public override void OnApplicationQuit() => Shutdown();
    public override void OnDeinitializeMelon() => Shutdown();

    /// <summary>Idempotent: stops transports, answers pending requests, removes hooks.</summary>
    private void Shutdown()
    {
        if (Interlocked.Exchange(ref _shutdownOnce, 1) != 0) return;
        _shutdown = true;
        try { _httpBridge?.Dispose(); } catch (Exception) { }
        try { _fileBridge?.Dispose(); } catch (Exception) { }
        try { AbortJobs("завершение игры", false); } catch (Exception) { }
        while (_inbox.TryDequeue(out var r)) r.Json(503, "{\"ok\":false,\"status\":503,\"error\":\"shutting down\"}");
        try { InputSim.ReleaseIfHeld(); } catch (Exception) { }
        try { ErrorWatch.Uninstall(); } catch (Exception) { }
        try { HarmonyInstance?.UnpatchSelf(); } catch (Exception) { }
        UiRecorder.Reset();
    }
}
