using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

[assembly: MelonInfo(typeof(CreatorToolkit.ToolkitMod), "SlimeFPS", "1.1.0", "slitvin12", "https://github.com/slitvin12/SR2-CreatorToolkit")]
[assembly: MelonGame("MonomiPark", "SlimeRancher2")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyDescription("Standalone graphics optimization: textures, LOD, draw distance, decor, post-effects, runtime FPS and an FPS counter. Settings open with F8 by default; the key is rebindable in the settings window.\nОтдельная оптимизация графики: текстуры, LOD, дальность, декор, постэффекты, runtime FPS и счётчик FPS. Настройки — клавиша F8 по умолчанию, переназначается в окне настроек.")]
[assembly: AssemblyMetadata("display_version", "1.1.0")]
[assembly: AssemblyMetadata("source_code", "https://github.com/slitvin12/SR2-CreatorToolkit")]
[assembly: AssemblyMetadata("SR2ModManager.CompatibleWith", "SR2MP;Lycoreia;Starlight;CreatorToolkit")]
[assembly: AssemblyMetadata("SR2ModManager.HookPolicy", "cooperative")]

namespace CreatorToolkit;

// SlimeFPS host: an optimization-only melon built from the unchanged Creator Toolkit source tree.
// Only the optimization tool and the FPS counter run; the single smooth-loading Harmony hook is
// installed. No gameplay cheats, no save guard. Settings live in UserData\SlimeFPS.
public sealed class ToolkitMod : MelonMod
{
    internal static ToolkitMod I;
    internal const string Version = "1.1.0";
    internal readonly InputCapture Input = new();
    internal PlayerTools Player;
    internal MultiplayerBridge Coop;
    internal ActorTools Actors;
    internal WorldTools World;
    internal UnlockTools Unlocks;
    internal CinemaTools Cinema;
    internal RefineryTools Refinery;
    internal CinematicRateTools CinematicRate;
    internal SpawnPlacementTools Placement;
    internal MapTools Map;
    internal ConvenienceTools Convenience;
    internal CustomWaypointTools Custom;
    internal RadarTools Radar;
    internal FarmTools Farm;
    internal SlimeCareTools Care;
    internal GadgetTools Gadgets;
    internal PhenomenaTools Phenomena;
    internal OptimizationTools Optimization;
    internal DronePanelLayoutTools DronePanels;
    internal WorldTrackingTools Tracking;
    internal CorralAutoFeedTools AutoFeed;
    internal MiniMapTerrainTools MiniMapTerrain;
    internal CompassMarkerTools Compass;
    internal DroneToggleTools DroneToggle;
    internal PlayerMarkerTools PlayerMarker;
    internal MenuHotkey Hotkey;
    internal ActionHotkeys Shortcuts;
    internal NavigationTools Navigation;
    internal CaptureTools Capture;
    internal FreePlacementTools FreePlacement;
    internal SaveNameTools SaveNames;
    internal GameIconExport IconExport;
    internal PassiveFrameBenchmark Benchmark;
    internal M20ModProfiler ModProfiler;
    internal bool BenchmarkPendingWork => false;
    private ToolkitUi _ui;
    private DiagnosticIo _diagnosticIo;
    internal string SelftestFileRequest => null;
    internal void ForgetSelftestFileRequest() { }
    internal SceneContext Context;
    internal IntPtr Session;
    internal bool Ready
    {
        get
        {
            if (Context == null || Session == IntPtr.Zero) return false;
            try
            {
                var loader = SystemContext.Instance?.SceneLoader;
                return loader != null && !loader.WasCollected && !loader.IsSceneLoadInProgress && loader.IsCurrentSceneGroupGameplay();
            }
            catch { return false; }
        }
    }
    internal bool MenuOpen => Input.Active;
    internal string Status = "F8 — настройки графики";
    internal string LastError = "";
    internal Confirmation Pending;
    private readonly Queue<Action> _jobs = new();
    private readonly Dictionary<string, float> _errors = new();
    internal string DataDirectory;
    internal int InstalledHooks;
    internal SmokeTests Tests;
    internal FeatureSuite Audit;
    internal NativeUiProbe UiProbe;
    internal M15SliderProbe SliderProbe;
    internal NativeAtlasProbe MapProbe;
    internal StressSuite Stress;
    internal int FailureSerial { get; private set; }
    internal int UiOwnedTextures => _ui?.OwnedTextureCount ?? 0;
    internal ToolkitUi UiForTests => _ui;
    internal bool UiPointerCaptured => _ui != null && _ui.PointerCaptured;
    internal int UiDynamicLabels => _ui?.DynamicLabelCount ?? 0;
    internal int UiFilterCaches => _ui?.FilterCacheCount ?? 0;
    private (string Label, Action Run)[] _readyTicks, _alwaysTicks, _lateTicks;
    internal void UiForTest(int tab, int category, int quantity) { }
    internal int UiQuantityForTest => 0;
    internal float UiScrollForTest => 0f;
    internal IReadOnlyList<Rect> UiMapLabelsForTest => Array.Empty<Rect>();
    internal void ShowTabForTest(int tab) { }

    internal sealed class Confirmation
    {
        internal string Title, Description;
        internal Action Execute;
        internal IntPtr Session;
    }

    public override void OnInitializeMelon()
    {
        I = this;
        DataDirectory = Path.Combine(MelonEnvironment.UserDataDirectory, "SlimeFPS");
        Directory.CreateDirectory(DataDirectory);
        SeedHotkey("F8");
        _diagnosticIo = new DiagnosticIo(DataDirectory);
        HookCoexistence.Configure(HarmonyInstance, this);
        ModInterop.Configure(this);
        Coop = new MultiplayerBridge(this);
        Player = new PlayerTools(this);
        Actors = new ActorTools(this);
        World = new WorldTools(this);
        Unlocks = new UnlockTools(this);
        Cinema = new CinemaTools(this);
        Refinery = new RefineryTools(this);
        CinematicRate = new CinematicRateTools(this);
        Placement = new SpawnPlacementTools(this);
        Map = new MapTools(this);
        Convenience = new ConvenienceTools(this);
        Custom = new CustomWaypointTools(this);
        Radar = new RadarTools(this);
        Farm = new FarmTools(this);
        Care = new SlimeCareTools(this);
        Gadgets = new GadgetTools(this);
        Phenomena = new PhenomenaTools(this);
        Optimization = new OptimizationTools(this);
        DronePanels = new DronePanelLayoutTools(this);
        Tracking = new WorldTrackingTools(this);
        AutoFeed = new CorralAutoFeedTools(this);
        MiniMapTerrain = new MiniMapTerrainTools(this);
        Compass = new CompassMarkerTools(this);
        DroneToggle = new DroneToggleTools(this);
        PlayerMarker = new PlayerMarkerTools(this);
        Hotkey = new MenuHotkey(this, "F8");
        Hotkey.EnsureLoaded();
        Status = Hotkey.Display + " — настройки графики";
        Shortcuts = new ActionHotkeys(this);
        Navigation = new NavigationTools(this);
        Capture = new CaptureTools(this);
        FreePlacement = new FreePlacementTools(this);
        SaveNames = new SaveNameTools(this);
        IconExport = new GameIconExport(this);
        _ui = new ToolkitUi(this);
        Tests = new SmokeTests(this);
        Audit = new FeatureSuite(this);
        UiProbe = new NativeUiProbe(this);
        SliderProbe = new M15SliderProbe(this);
        MapProbe = new NativeAtlasProbe(this);
        Stress = new StressSuite(this);
        Benchmark = new PassiveFrameBenchmark(this);
        ModProfiler = new M20ModProfiler(this);
        Capture.Load(); // Restores the FPS-counter choice; smooth physics stays off (no actor hook here).
        _readyTicks = new (string, Action)[] { ("graphics optimization", Optimization.Tick) };
        _alwaysTicks = new (string, Action)[]
        {
            ("capture", Capture.Tick), ("hook coexistence", HookCoexistence.Tick), ("mod interop", ModInterop.Tick),
        };
        _lateTicks = new (string, Action)[] { ("camera fov", Capture.LateTick) };
        InstalledHooks = InstallLoadingHook();
        LoggerInstance.Msg("SlimeFPS " + Version + " initialized; optimization only, settings key " + Hotkey.Key + ", hooks=" + InstalledHooks + ".");
    }

    // First run: write the mod's default so MenuHotkey loads F8, not the shared Insert default.
    private void SeedHotkey(string key)
    {
        try
        {
            string file = Path.Combine(DataDirectory, MenuHotkeyPolicy.FileName);
            if (!File.Exists(file)) File.WriteAllText(file, "# SlimeFPS: settings key (UnityEngine.InputSystem.Key name)\n" + MenuHotkeyPolicy.Prefix + key + "\n");
        }
        catch (Exception e) { LoggerInstance.Warning("[hotkey] seed: " + e.Message); }
    }

    // The one hook optimization needs: keep the smooth-loading budget after a turbo load.
    private int InstallLoadingHook()
    {
        try
        {
            var loader = AccessTools.Method(typeof(Il2CppMonomiPark.SlimeRancher.SceneManagement.SceneLoader), "SetTurboLoadEnabled");
            var budget = AccessTools.Method(typeof(NativeHooks), "LoadingBudget");
            if (loader == null || budget == null) return 0;
            HarmonyInstance.Patch(loader, postfix: new HarmonyMethod(budget, HarmonyLib.Priority.Last));
            HookCoexistence.Track(loader, budget);
            return 1;
        }
        catch (Exception e) { Fail("patch smooth loading budget", e); return 0; }
    }

    public override void OnUpdate()
    {
        try { UpdateFrame(); }
        catch (Exception e) { Fail("update", e); CloseMenu(); }
    }

    private void UpdateFrame()
    {
        Loc.Tick();
        BindSession();
        Coop.Poll();
        Input.TickNativeHandoff();
        Input.TickDeferredOpen();
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (Hotkey.Capturing)
            {
                if (MenuOpen) Hotkey.TickCapture(kb); else Hotkey.CancelCapture();
            }
            else if (Hotkey.Pressed(kb) && !(MenuOpen && Hotkey.Typing && (_ui?.TextFieldFocused ?? false)))
            {
                if (MenuOpen) CloseMenu(); else Input.Open();
            }
            else if (MenuOpen && kb.escapeKey.wasPressedThisFrame) CloseMenu();
        }
        if (MenuOpen) Input.Enforce();
        if (Ready)
        {
            for (int i = 0; i < 4 && _jobs.Count > 0; i++)
            {
                try { _jobs.Dequeue()(); } catch (Exception e) { Fail("action", e); }
            }
            foreach (var tick in _readyTicks) SafeTick(tick.Label, tick.Run);
        }
        foreach (var tick in _alwaysTicks) SafeTick(tick.Label, tick.Run);
    }

    public override void OnLateUpdate()
    {
        try { Input.TickNativeHandoff(); if (MenuOpen) Input.Enforce(); }
        catch (Exception e) { Fail("input", e); CloseMenu(); }
        if (_lateTicks != null) foreach (var tick in _lateTicks) SafeTick(tick.Label, tick.Run);
    }

    public override void OnGUI()
    {
        if (_ui == null) return;
        try { DrawGui(); }
        catch (Exception e) { Fail("GUI", e); CloseMenu(); }
    }

    private void DrawGui()
    {
        _ui.SyncLanguage();
        if (Ready && _ui.OwnedTextureCount == 0 && Event.current?.type == EventType.Repaint)
        {
            try { _ui.InitializeReadyGui(); } catch (Exception e) { Fail("UI initialization", e); return; }
        }
        if (MenuOpen)
        {
            try { _ui.DrawSlimeFpsSettings(); }
            catch (Exception e) { Fail("settings GUI", e); CloseMenu(); }
        }
        try { _ui.DrawFpsCounter(); } catch (Exception e) { Fail("FPS counter", e); }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName) => Loc.SceneLoaded(sceneName);

    public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
    {
        Map?.InvalidateScene();
    }

    public override void OnApplicationQuit()
    {
        SafeTick("restore graphics optimization", () => Optimization?.Reset());
        SafeTick("capture shutdown", () => Capture?.Shutdown());
        RestoreTransient();
        _ui?.Dispose();
        _diagnosticIo?.Dispose();
        Context = null;
        Session = IntPtr.Zero;
        I = null;
    }

    private void BindSession()
    {
        SceneContext ctx = null;
        IntPtr session = IntPtr.Zero;
        try
        {
            ctx = SceneContext.Instance;
            var loader = SystemContext.Instance?.SceneLoader;
            bool stableMenu = loader != null && !loader.WasCollected && !loader.IsSceneLoadInProgress && !loader.IsCurrentSceneGroupGameplay();
            if (!stableMenu && ctx != null && !ctx.WasCollected && ctx.Player != null && ctx.GameModel != null && ctx.PlayerState != null)
                session = ctx.GameModel.Pointer;
            else ctx = null;
        }
        catch { ctx = null; }
        if (session == IntPtr.Zero && Session != IntPtr.Zero && SystemContext.Instance?.SceneLoader?.IsSceneLoadInProgress == true)
            return;
        if (session != Session)
        {
            RestoreTransient();
            Pending = null;
            _jobs.Clear();
            _ui?.ResetSession();
            Session = session;
        }
        Context = ctx;
    }

    private void RestoreTransient()
    {
        SafeTick("restore graphics optimization", () => Optimization?.Reset());
        SafeTick("reset camera and capture", () => Capture?.ResetSession());
        CloseMenu();
    }

    private void SafeTick(string label, Action action)
    {
        try { action(); } catch (Exception e) { Fail(label, e); }
    }

    internal void Queue(string label, Action action)
    {
        string denied = Coop?.Deny(label);
        if (denied != null) { Notify(denied); return; }
        IntPtr expected = Session;
        _jobs.Enqueue(() =>
        {
            if (!Ready || !SafetyPolicy.SameSession(expected, Session)) throw new InvalidOperationException("Сейв изменился: операция отменена.");
            action();
        });
    }

    internal void Confirm(string title, string description, Action action)
    {
        if (!Ready) { Notify("Сначала загрузите сейв."); return; }
        Pending = new Confirmation { Title = title, Description = description, Execute = action, Session = Session };
    }

    internal void AcceptConfirmation()
    {
        Confirmation confirmed = Pending;
        Pending = null;
        if (confirmed == null || !SafetyPolicy.SameSession(confirmed.Session, Session)) { Notify("Сейв изменился: подтверждение отменено."); return; }
        Queue(confirmed.Title, confirmed.Execute);
    }

    internal void Notify(string text)
    {
        Status = text;
        LastError = "";
        LoggerInstance.Msg(text);
    }

    internal void Fail(string scope, Exception e)
    {
        FailureSerial++;
        LastError = scope + ": " + e.Message;
        Status = "Ошибка: " + e.Message;
        if (!_errors.TryGetValue(scope, out float last) || Time.unscaledTime - last > 5f)
        {
            _errors[scope] = Time.unscaledTime;
            LoggerInstance.Warning(scope + ": " + e);
        }
    }

    internal bool EnsureInfiniteBuilderHooks() => false;
    internal bool EnsureCaptureHooks() => false;

    internal void CloseMenu()
    {
        Pending = null;
        _ui?.ClearInteraction();
        try { Input.Close(); } catch (Exception e) { Fail("restore input", e); }
    }

    internal void YieldToNativeUi()
    {
        Pending = null;
        _ui?.ClearInteraction();
        Input.YieldToNative();
    }

    internal bool TryContinueGame() => false;
    internal void SaveAndQuit() { }
}