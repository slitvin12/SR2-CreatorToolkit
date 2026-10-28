using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.UI;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

[assembly: MelonInfo(typeof(CreatorToolkit.ToolkitMod), "Creator Toolkit", "0.6.0", "slitvin12", "https://github.com/slitvin12/SR2-CreatorToolkit")]
[assembly: MelonGame("MonomiPark", "SlimeRancher2")]
[assembly: AssemblyVersion("0.6.0.0")]
[assembly: AssemblyFileVersion("0.6.0.0")]

namespace CreatorToolkit;

public sealed class ToolkitMod : MelonMod
{
    internal static ToolkitMod I;
    internal const string Version = "0.6.0";
    internal readonly InputCapture Input = new();
    internal PlayerTools Player;
    internal ActorTools Actors;
    internal WorldTools World;
    internal UnlockTools Unlocks;
    internal CinemaTools Cinema;
    internal RefineryTools Refinery;
    internal CinematicRateTools CinematicRate;
    internal MapTools Map;
    internal ConvenienceTools Convenience;
    internal CustomWaypointTools Custom;
    internal RadarTools Radar;
    internal FarmTools Farm;
    internal SlimeCareTools Care;
    internal GadgetTools Gadgets;
    internal PhenomenaTools Phenomena;
    internal OptimizationTools Optimization;
    // M27: drone panel layout, world tracking lists, corral autofeed, minimap terrain, compass markers.
    internal DronePanelLayoutTools DronePanels;
    internal WorldTrackingTools Tracking;
    internal CorralAutoFeedTools AutoFeed;
    internal MiniMapTerrainTools MiniMapTerrain;
    internal CompassMarkerTools Compass;
    // M28: native drone-menu button kept clear of the atlas panel; minimap copy of the native player marker.
    internal DroneToggleTools DroneToggle;
    internal PlayerMarkerTools PlayerMarker;
    // M29 task 4: minimap / compass settings (navigation.cfg) and the smooth minimap zoom.
    internal NavigationTools Navigation;
    internal PassiveFrameBenchmark Benchmark;
    internal M20ModProfiler ModProfiler;
    internal bool BenchmarkPendingWork=>Pending!=null||_jobs.Count>0;
    private ToolkitUi _ui;
    private DiagnosticIo _diagnosticIo;
    internal string SelftestFileRequest=>_diagnosticIo.SelftestRequestOption;
    internal void ForgetSelftestFileRequest()=>_diagnosticIo.InvalidateSelftestRequest();
    internal SceneContext Context;
    internal IntPtr Session;
    internal bool Ready
    {
        get
        {
            if(Context == null || Session == IntPtr.Zero) return false;
            try
            {
                var loader=SystemContext.Instance?.SceneLoader;
                return loader != null && !loader.WasCollected && !loader.IsSceneLoadInProgress && loader.IsCurrentSceneGroupGameplay();
            }
            catch { return false; }
        }
    }
    internal bool MenuOpen => Input.Active;
    internal string Status = "Insert — открыть меню";
    internal string LastError = "";
    internal Confirmation Pending;
    private readonly Queue<Action> _jobs = new();
    private readonly Dictionary<string, float> _errors = new();
    private float _nextDiagnostics;
    private string _sprintReadOnlySnapshot="";
    private string _lastUiNavigation="not-requested";
    internal string DataDirectory;
    internal int InstalledHooks;
    internal SmokeTests Tests;
    internal FeatureSuite Audit;
    internal NativeUiProbe UiProbe;
    internal M15SliderProbe SliderProbe;
    internal NativeAtlasProbe MapProbe;
    internal StressSuite Stress;
    internal int FailureSerial {get;private set;}
    internal int UiOwnedTextures=>_ui.OwnedTextureCount;
    internal ToolkitUi UiForTests=>_ui;
    // An IMGUI slider/scroll drag owns the mouse (native map input gate).
    internal bool UiPointerCaptured=>_ui!=null&&_ui.PointerCaptured;
    internal int UiDynamicLabels=>_ui.DynamicLabelCount;
    internal int UiFilterCaches=>_ui.FilterCacheCount;
    private (string Label,Action Run)[] _readyTicks,_alwaysTicks,_lateTicks;
    private (string Label,Action Run)[] _m28LateTicks;
    internal void UiForTest(int tab,int category,int quantity)=>_ui.SetForTest(tab,category,quantity);
    internal int UiQuantityForTest=>_ui.QuantityForTest;
    internal float UiScrollForTest=>_ui.ScrollForTest;
    internal IReadOnlyList<Rect> UiMapLabelsForTest=>_ui.MapZoneLabelRects;
    internal void ShowTabForTest(int tab) => _ui.ShowTabForTest(tab);

    internal sealed class Confirmation
    {
        internal string Title, Description;
        internal Action Execute;
        internal IntPtr Session;
    }

    public override void OnInitializeMelon()
    {
        I = this;
        DataDirectory = Path.Combine(MelonEnvironment.UserDataDirectory, "CreatorToolkit");
        Directory.CreateDirectory(DataDirectory);
        _diagnosticIo=new DiagnosticIo(DataDirectory);
        AuditSaveGuard.Configure(DataDirectory);
        int saveHooks=AuditSaveGuard.Install(HarmonyInstance); // Before audit recovery or any opt-in boot.
        Player = new PlayerTools(this);
        Actors = new ActorTools(this);
        World = new WorldTools(this);
        Unlocks = new UnlockTools(this);
        Cinema = new CinemaTools(this);
        Refinery = new RefineryTools(this);
        CinematicRate = new CinematicRateTools(this);
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
        Navigation = new NavigationTools(this);
        Navigation.Load();
        _ui = new ToolkitUi(this);
        Tests = new SmokeTests(this);
        Audit = new FeatureSuite(this);
        UiProbe = new NativeUiProbe(this);
        SliderProbe = new M15SliderProbe(this);
        MapProbe = new NativeAtlasProbe(this);
        Stress = new StressSuite(this);
        Benchmark = new PassiveFrameBenchmark(this);
        ModProfiler = new M20ModProfiler(this);
        _readyTicks=new (string,Action)[]{("player",Player.Tick),("world",World.Tick),("weather",World.TickWeather),
            ("map unlock",Unlocks.Tick),("cinema",Cinema.Tick),("conveniences",Convenience.Tick),("actors",Actors.Tick),
            ("custom waypoints",Custom.Tick),("zone radar",Radar.Tick),("corral assistant",Farm.Tick),("slime care",Care.Tick),
            ("gadget placement",Gadgets.Tick),("native phenomena",Phenomena.Tick),("graphics optimization",Optimization.Tick),
            ("corral autofeed",AutoFeed.Tick),("compass markers",Compass.Tick)};
        _alwaysTicks=new (string,Action)[]{("cinematic rate",CinematicRate.Tick),("navigation settings",Navigation.Tick),("atlas",Map.Tick),("minimap terrain",MiniMapTerrain.Tick),("selftest",Tests.Tick),("native atlas probe",MapProbe.Tick),
            ("native UI probe",UiProbe.Tick),("M15 slider probe",SliderProbe.Tick),("functional suite",Audit.Tick)};
        _lateTicks=new (string,Action)[]{("drone panel layout",DronePanels.LateTick)};
        _m28LateTicks=new (string,Action)[]{("drone toggle",DroneToggle.LateTick),("compass icons",Compass.LateTick),("player marker",PlayerMarker.LateTick)};
        InstalledHooks = saveHooks+NativeHooks.Install(HarmonyInstance);
        LoggerInstance.Msg("CreatorToolkit " + Version + " initialized; standalone MelonLoader/IL2CPP; Insert only.");
    }

    public override void OnUpdate()
    {
        bool measure=Stress?.Measuring==true,observe=ModProfiler?.BeginUpdate()==true,timed=measure||observe;
        long start=timed?Stopwatch.GetTimestamp():0,bytes=timed?GC.GetAllocatedBytesForCurrentThread():0;
        try { UpdateFrame(); }
        finally
        {
            if(timed){long elapsed=Stopwatch.GetTimestamp()-start,allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
                if(measure)Stress.RecordUpdate(elapsed,allocated);if(observe)ModProfiler.RecordUpdate(elapsed,allocated);}
        }
    }

    private void UpdateFrame()
    {
        try
        {
            bool profileSession=Stress?.Measuring==true,observeSession=ModProfiler?.Capturing==true;long sessionStart=profileSession||observeSession?Stopwatch.GetTimestamp():0,sessionBytes=observeSession?GC.GetAllocatedBytesForCurrentThread():0;
            AuditSaveGuard.Tick();
            BindSession();
            InfiniteBuildHooks.Frame(this); // Resets per-frame scopes; disables the view on save change.
            if(MenuOpen&&NativeUiPriority.HasNativeFocus)YieldToNativeUi();
            Input.TickNativeHandoff();
            Input.TickDeferredOpen();
            if(profileSession)Stress.RecordSection("session",Stopwatch.GetTimestamp()-sessionStart);
            if(observeSession)ModProfiler.RecordSection("session",Stopwatch.GetTimestamp()-sessionStart,GC.GetAllocatedBytesForCurrentThread()-sessionBytes);
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.insertKey.wasPressedThisFrame)
                {
                    if (MenuOpen) CloseMenu(); else Input.Open();
                }
                else if (MenuOpen && kb.escapeKey.wasPressedThisFrame) CloseMenu();
            }
            if (MenuOpen) Input.Enforce();
            M17UiHoverProbe.Tick();
            M18GordoProbe.Tick();
            M19GraphicsUiProbe.Tick();
            _ui?.TickTextInput();
            _ui?.TickPlayerReadouts();
            Benchmark?.Tick(); // Unconditional: invalidation/cleanup must run before the Ready gate.
            if (Ready)
            {
                for (int i = 0; i < 4 && _jobs.Count > 0; i++)
                {
                    try { _jobs.Dequeue()(); } catch (Exception e) { Fail("action", e); }
                }
                foreach(var tick in _readyTicks)SafeTick(tick.Label,tick.Run);
            }
            foreach(var tick in _alwaysTicks)SafeTick(tick.Label,tick.Run);
            bool profileDiagnostics=Stress?.Measuring==true,observeDiagnostics=ModProfiler?.Capturing==true;long diagnosticStart=profileDiagnostics||observeDiagnostics?Stopwatch.GetTimestamp():0,diagnosticBytes=observeDiagnostics?GC.GetAllocatedBytesForCurrentThread():0;
            try{PollDiagnostics();}
            finally{if(profileDiagnostics)Stress.RecordSection("diagnostics",Stopwatch.GetTimestamp()-diagnosticStart);
                if(observeDiagnostics)ModProfiler.RecordSection("diagnostics",Stopwatch.GetTimestamp()-diagnosticStart,GC.GetAllocatedBytesForCurrentThread()-diagnosticBytes);}
        }
        catch (Exception e)
        {
            Benchmark?.Invalidate("host-update-error");
            Fail("update", e);
            CloseMenu();
        }
    }

    public override void OnLateUpdate()
    {
        // Native displays may acquire focus after Update, even when our menu
        // has never been opened. Do not gate native handoff on MenuOpen.
        try { Input.TickNativeHandoff(); if (MenuOpen) Input.Enforce(); Benchmark?.ValidateLateState(); }
        catch (Exception e) { Benchmark?.Invalidate("host-late-update-error"); Fail("input", e); CloseMenu(); }
        // M27: after the game's own UI layout for this frame (native drone panel placement).
        if(_lateTicks!=null)foreach(var tick in _lateTicks)SafeTick(tick.Label,tick.Run);
        // M28: drone-menu button placement, compass icons on the bar's own instances, player marker capture.
        if(_m28LateTicks!=null)foreach(var tick in _m28LateTicks)SafeTick(tick.Label,tick.Run);
    }

    public override void OnGUI()
    {
        bool measure=Stress?.Measuring==true,observe=ModProfiler?.Capturing==true,timed=measure||observe;
        long start=timed?Stopwatch.GetTimestamp():0,bytes=timed?GC.GetAllocatedBytesForCurrentThread():0;
        try { DrawGui(); }
        finally
        {
            if(timed){long elapsed=Stopwatch.GetTimestamp()-start,allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
                if(measure)Stress.RecordGui(elapsed,allocated);if(observe)ModProfiler.RecordGui(elapsed,allocated);}
        }
    }

    private void DrawGui()
    {
        if (_ui == null) return;
        if(MenuOpen&&NativeUiPriority.HasNativeFocus)YieldToNativeUi();
        if(Ready&&_ui.OwnedTextureCount==0&&Event.current?.type==EventType.Repaint)
        {
            try{_ui.InitializeReadyGui();}catch(Exception e){Fail("UI initialization",e);return;}
        }
        if (!MenuOpen)
        {
            try { _ui.DrawTransitStatus();_ui.DrawSessionHud();_ui.DrawExplorerHud();_ui.DrawMiniMap();_ui.DrawMapOverlay();Audit?.DrawNativeFontSwatch(); } catch(Exception e) { Fail("HUD / map overlay",e); }
            return;
        }
        try { _ui.Draw(); Tests?.Rendered(); }
        catch (Exception e) { Tests?.GuiFailed(e); Fail("GUI", e); CloseMenu(); }
    }

    public override void OnSceneWasUnloaded(int buildIndex,string sceneName)
    {
        Benchmark?.Cancel("scene-unloaded");
        ModProfiler?.Cancel("scene-unloaded");
        Map?.InvalidateScene();
        World?.InvalidateWeatherResolution();
        _sprintReadOnlySnapshot="";
    }

    public override void OnApplicationQuit()
    {
        M17UiHoverProbe.Cancel();
        Benchmark?.Cancel("application-quit");
        // Native callbacks may follow Melon cleanup. Never disarm the static
        // save guard or restore native autosaving during a protected audit exit.
        AuditSaveGuard.OnHostQuit();
        Stress?.Cancel();
        Navigation?.Flush();
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
            var loader=SystemContext.Instance?.SceneLoader;
            // The native singleton can outlive its gameplay scene. A settled
            // main menu is an unload boundary even if that old wrapper remains.
            bool stableMenu=loader!=null&&!loader.WasCollected&&!loader.IsSceneLoadInProgress&&!loader.IsCurrentSceneGroupGameplay();
            if(!stableMenu && ctx != null && !ctx.WasCollected && ctx.Player != null && ctx.GameModel != null && ctx.PlayerState != null)
                session = ctx.GameModel.Pointer;
            else ctx = null;
        }
        catch { ctx = null; }
        // A streamed location temporarily has no SceneContext. Do not interpret
        // this as a new save and tear down the in-flight safe transit mid-load.
        if(session==IntPtr.Zero&&Session!=IntPtr.Zero&&SystemContext.Instance?.SceneLoader?.IsSceneLoadInProgress==true)
            return;
        if (session != Session)
        {
            RestoreTransient();
            Pending = null;
            _sprintReadOnlySnapshot="";
            _jobs.Clear();
            Actors?.ResetTracking();
            Cinema?.ResetWaypoints();
            _ui?.ResetSession();
            Session = session;
            if (session != IntPtr.Zero) Notify("Сейв загружен. Временные эффекты сброшены.");
        }
        Context = ctx;
    }

    private void RestoreTransient()
    {
        Benchmark?.Cancel("session-restore-or-unload");
        ModProfiler?.Cancel("session-restore-or-unload");
        Tests?.Cancel();
        UiProbe?.Cancel();SliderProbe?.Cancel();M19GraphicsUiProbe.Cancel();
        MapProbe?.Cancel();
        SafeTick("restore cinematic rate",()=>CinematicRate?.Reset());
        SafeTick("reset refinery catalog",()=>Refinery?.Reset());
        SafeTick("restore graphics optimization",()=>Optimization?.Reset());
        SafeTick("restore corral assistant",()=>Farm?.Reset());
        SafeTick("restore drone panel layout",()=>DronePanels?.Reset());
        SafeTick("restore drone toggle",()=>DroneToggle?.Reset());
        SafeTick("reset player marker",()=>PlayerMarker?.Reset());
        SafeTick("reset world tracking",()=>Tracking?.Reset());
        SafeTick("restore corral autofeed",()=>AutoFeed?.Reset());
        SafeTick("reset minimap terrain",()=>MiniMapTerrain?.Reset());
        SafeTick("restore compass markers",()=>Compass?.Reset());
        SafeTick("restore slime care",()=>Care?.Reset());
        SafeTick("restore gadget catalog",()=>Gadgets?.Reset());
        SafeTick("restore native phenomena",()=>Phenomena?.Reset());
        SafeTick("restore radar",()=>Radar?.Reset());
        SafeTick("restore custom waypoints",()=>Custom?.Reset());
        SafeTick("restore atlas", () => Map?.Reset());
        SafeTick("restore conveniences", () => Convenience?.Reset());
        CloseMenu();
        SafeTick("restore player", () => Player?.Reset());
        SafeTick("restore world", () => World?.Reset());
        SafeTick("restore HUD", () => Cinema?.Reset());
    }

    private void SafeTick(string label, Action action)
    {
        bool profile=Stress?.Measuring==true,observe=ModProfiler?.Capturing==true;long start=profile||observe?Stopwatch.GetTimestamp():0,bytes=observe?GC.GetAllocatedBytesForCurrentThread():0;
        try { action(); } catch (Exception e) { Fail(label, e); }
        finally{if(profile)Stress.RecordSection(label,Stopwatch.GetTimestamp()-start);
            if(observe)ModProfiler.RecordSection(label,Stopwatch.GetTimestamp()-start,GC.GetAllocatedBytesForCurrentThread()-bytes);}
    }

    internal bool TryContinueGame()
    {
        if (Ready)
        {
            LoggerInstance.Msg("Continue: game is already in gameplay scene.");
            return true;
        }

        var loader = SystemContext.Instance?.SceneLoader;
        if (loader == null || loader.IsSceneLoadInProgress)
        {
            LoggerInstance.Msg("Continue: SceneLoader is unavailable or load is in progress.");
            return false;
        }

        var engagement = UnityEngine.Object.FindObjectOfType<Il2CppMonomiPark.SlimeRancher.UI.MainMenu.PlatformEngagementPrompt>();
        if (engagement != null && engagement.SceneGroupToLoad != null && engagement.ShouldShowInteractionPrompt())
        {
            LoggerInstance.Msg("Continue: advancing past PlatformEngagementPrompt into menu.");
            engagement.LoadSceneGroup();
            return true;
        }

        var start = UnityEngine.Object.FindObjectOfType<StandaloneStartScreen>();
        if (start != null && start.pastFirstFrame && !start.isLoading && start.defaultSceneGroup != null)
        {
            LoggerInstance.Msg("Continue: advancing past StandaloneStartScreen into menu.");
            start.isLoading = true;
            try { loader.LoadSceneGroup(start.defaultSceneGroup); }
            catch { if (!start.WasCollected) start.isLoading = false; throw; }
            return true;
        }

        var save = GameContext.Instance?.AutoSaveDirector;
        if (save != null)
        {
            var summary = save.HasContinue() ? save.GetSaveToContinue() : null;
            if (summary != null && !summary.IsInvalid && !summary.GameOver)
            {
                LoggerInstance.Msg("Continue: triggering native BeginLoad for save '" + summary.SaveIdentifier + "' (" + summary.Name + ").");
                save.BeginLoad(summary.SaveIdentifier);
                return true;
            }
            else
            {
                LoggerInstance.Warning("Continue: AutoSaveDirector has no valid save to continue.");
            }
        }
        else
        {
            LoggerInstance.Warning("Continue: AutoSaveDirector is not available.");
        }

        return false;
    }

    internal void SaveAndQuit()
    {
        Benchmark?.Cancel("host-save-and-quit-request");
        if(Map?.Busy == true){Notify("Дождитесь завершения безопасной телепортации перед выходом.");return;}
        if(AuditSaveGuard.Required)
        {
            Audit.RequestProtectedExit("explicit quit in protected audit process");
            return; // No SaveAllNow/SaveGameAndFlush or early autosave release.
        }
        if(Audit?.Active == true){Audit.CancelForQuit();if(Audit.Active)return;}
        Tests?.CancelRequest();
        CloseMenu();
        CinematicRate?.Reset();
        Refinery?.Reset();
        Optimization?.Reset(); // Before native save/profile persistence, not only Application.Quit.
        Convenience?.Reset();
        Farm?.Reset();Radar?.Reset();Custom?.HideGoal();Care?.Reset();Gadgets?.Reset();
        AutoFeed?.Reset();Compass?.Reset();DronePanels?.Reset();
        DroneToggle?.Reset();
        var save=GameContext.Instance?.AutoSaveDirector;
        if(Ready)
        {
            if(save == null || !save.SaveAllNow(false,null))
                throw new InvalidOperationException("Сохранение не подтверждено: выход из игры отменён.");
            save.SaveGameAndFlush();
        }
        else if(save != null) save.SaveProfile();
        LoggerInstance.Msg("Save confirmed; requesting normal Unity Application.Quit.");
        Application.Quit();
    }

    internal void CloseMenu()
    {
        Pending = null;
        _ui?.ClearInteraction();
        try { Input.Close(); } catch (Exception e) { Fail("restore input", e); }
    }

    internal void YieldToNativeUi()
    {
        Pending=null;
        _ui?.ClearInteraction(); // Native text/IME listeners, drag and focus too.
        Input.YieldToNative();
    }

    internal void Queue(string label, Action action)
    {
        IntPtr expected = Session;
        _jobs.Enqueue(() =>
        {
            if (!Ready || !SafetyPolicy.SameSession(expected, Session)) throw new InvalidOperationException("Сейв изменился: операция отменена.");
            action();
            if (string.IsNullOrEmpty(LastError)) LoggerInstance.Msg("Action: " + label);
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

    // «Бесконечный режим строителя» hooks: installed on the first enable only.
    internal bool EnsureInfiniteBuilderHooks()
    {
        if(!InfiniteBuildHooks.Installed)
        {
            int added=InfiniteBuildHooks.Install(HarmonyInstance);
            InstalledHooks+=added;
            LoggerInstance.Msg("Infinite builder hooks installed="+added+" available="+InfiniteBuildHooks.Available);
        }
        return InfiniteBuildHooks.Available;
    }

    // UI-only navigation, no pointer injection or game/progression action.
    private void NavigateMenu(string command,Action navigate)
    {
        string reason=!Ready?"not-ready":NativeUiPriority.HasNativeFocus?"native-focus":Map.Busy?"transit":Pending!=null?"confirmation-active":null;
        try { if(reason==null){navigate();Input.Open();reason=MenuOpen?"accepted":"open-rejected";} }
        catch(Exception e){reason="error:"+e.GetType().Name;throw;}
        finally
        {
            _lastUiNavigation=DateTime.UtcNow.ToString("O")+" "+command+" "+reason+" tab="+_ui.CurrentTabForTest;
            LoggerInstance.Msg("UI navigation: "+_lastUiNavigation);
        }
    }

    // Safe local diagnostic relay, not a game/network dependency. No destructive commands.
    private void PollDiagnostics()
    {
        if (Time.unscaledTime < _nextDiagnostics) return;
        _nextDiagnostics = Time.unscaledTime + 0.5f;
        var ioError=_diagnosticIo.TakeError();
        if(ioError!=null)Fail("diagnostic IO",ioError);
        var lines=_diagnosticIo.TakeCommands();
        if (lines.Length>0)
        {
            foreach (string line in lines)
            {
                string command=line.Trim().ToLowerInvariant();
                if(Benchmark?.Active==true&&command is not ("status" or "bench-observe" or "bench-background" or "bench-cancel"))
                    Benchmark.Invalidate("other-diagnostic-command");
                switch (command)
                {
                    case "menu": if (!MenuOpen) Input.Open(); break;
                    case "close": CloseMenu(); break;
                    case "catalog": if (Ready) Actors.RefreshCatalog(); break;
                    case "status": break;
                    case "builder-infinite-on": if(Ready)Queue("infinite builder",()=>Gadgets.SetInfiniteBuilder(true)); break;
                    case "builder-infinite-off": if(Ready)Queue("infinite builder",()=>Gadgets.SetInfiniteBuilder(false)); break;
                    case "builder-audit": _sprintReadOnlySnapshot=InfiniteBuildHooks.Audit(this); break;
                    case "builder-mode": if(Ready)Queue("native build mode",Gadgets.ToggleNativeModeForTest); break;
                    case "builder-open-menu": if(Ready)Queue("native gadget menu",Gadgets.OpenNativeMenuForTest); break;
                    case "menu-gadgets": NavigateMenu(command,()=>{_ui.ShowCreationForTest(2);Gadgets.RefreshCatalog();}); break;
                    case "menu-care": NavigateMenu(command,()=>{_ui.ShowCareForTest();if(!Care.Inspect)Care.SetInspect(true);}); break;
                    case "menu-care-static": NavigateMenu(command,_ui.ShowCareForTest); break;
                    case "menu-gadgets-page": NavigateMenu(command,()=>{_ui.ShowGadgetsForTest();Gadgets.RefreshCatalog();}); break;
                    case "menu-refinery": NavigateMenu(command,()=>{_ui.ShowRefineryForTest();Queue("refinery catalog",Refinery.Refresh);}); break;
                    case "menu-time": NavigateMenu(command,_ui.ShowTimeRateForTest); break;
                    case "m14-audit": _sprintReadOnlySnapshot=ReadOnlySprintDiagnostics.Snapshot(this); break;
                    case "m15-audit": _sprintReadOnlySnapshot=M15RuntimeDiagnostics.Snapshot(this); break;
                    case "menu-radiant": NavigateMenu(command,_ui.ShowRadiantCatalog); break;
                    case "menu-player": NavigateMenu(command,()=>_ui.ShowPage(0)); break;
                    case "menu-potato": NavigateMenu(command,_ui.ShowOptimizationForTest); break;
                    case "m17-ui-hover": M17UiHoverProbe.Start();break;
                    case "menu-graphics-runtime": NavigateMenu(command,()=>_ui.ShowGraphicsTabForTest(3));break;
                    case "fps-runtime-balanced": if(Ready)Queue("runtime FPS",()=>{Optimization.ApplyDetailed(M19GraphicsRequest.Full with{LodPercent=50});Optimization.Apply(Optimization.ForUi() with{ParticleMax=256});Optimization.ApplyRuntime(M25RuntimeRequest.Balanced);});break;
                    case "fps-runtime-sleep-on": if(Ready)Queue("runtime sleep",()=>Optimization.ApplyRuntime(Optimization.Runtime.Request with{Sleep=true}));break;
                    case "fps-runtime-sleep-off": if(Ready)Queue("runtime sleep",()=>Optimization.ApplyRuntime(Optimization.Runtime.Request with{Sleep=false}));break;
                    case "fps-bench-uncap": if(Ready)Queue("temporary FPS cap removal",Optimization.UncapBenchmark);break;
                    case "fps-bench-recap": if(Ready)Queue("restore FPS cap",Optimization.RestoreBenchmarkCap);break;
                    case "fps-runtime-audit": _sprintReadOnlySnapshot=Optimization.Runtime.Audit();break;
                    case "graphics-maximum": if(Ready)Queue("maximum FPS",()=>Optimization.ApplyMaximum());break;
                    case "m18-gordo-test": M18GordoProbe.Start(this);break;
                    case "menu-graphics-geometry": NavigateMenu(command,()=>_ui.ShowGraphicsTabForTest(1));break;
                    case "menu-graphics-effects": NavigateMenu(command,()=>_ui.ShowGraphicsTabForTest(2));break;
                    case "m19-graphics-custom": if(Ready)Queue("m19-graphics-custom",()=>Optimization.ApplyDetailed(new M19GraphicsRequest(50,180,false,false,M19Effects.MotionBlur|M19Effects.Bloom)));break;
                    case "m19-graphics-decor": if(Ready)Queue("m19-graphics-decor",()=>Optimization.ApplyDetailed(M19GraphicsRequest.Full with{HideDecor=true}));break;
                    case "m19-graphics-shadows": if(Ready)Queue("m19-graphics-shadows",()=>Optimization.ApplyDetailed(M19GraphicsRequest.Full with{DisableShadows=true}));break;
                    case "m19-graphics-effects": if(Ready)Queue("m19-graphics-effects",()=>Optimization.ApplyDetailed(M19GraphicsRequest.Full with{DisabledEffects=M19Effects.All}));break;
                    case "m19-graphics-full": if(Ready)Queue("m19-graphics-full",()=>Optimization.ApplyDetailed(M19GraphicsRequest.Full));break;
                    case "m19-graphics-ui-test": M19GraphicsUiProbe.Start(this);break;
                    case "m22-capsule-audit": if(Ready)_sprintReadOnlySnapshot=Map.CapsuleProvenanceAudit();break;
                    case "m21-weather-audit": _sprintReadOnlySnapshot=M21WeatherAudit.Snapshot(this);break;
                    case "m21-weather-invalidate": World.InvalidateWeatherResolution();break;
                    case "m21-weather-refresh": if(Ready)World.RefreshWeather();break;
                    case "m20-profile": ModProfiler.Start();break;
                    case "m20-profile-cancel": ModProfiler.Cancel("explicit");break;
                    case "m20-lod-only": if(Ready)Queue("LOD only",()=>Optimization.ApplyDetailed(M19GraphicsRequest.Full with{LodPercent=50}));break;
                    case "m18-audit": _sprintReadOnlySnapshot=M18Diagnostics.Snapshot(this);break;
                    case "m17-audit": _sprintReadOnlySnapshot=M17RuntimeDiagnostics.Snapshot(this); break;
                    case "menu-gordo": NavigateMenu(command,()=>_ui.SetForTest(1,7,1)); break;
                    case "m17-distance-low": if(Ready)Queue("diagnostic camera distance",()=>Optimization.ApplyDistanceLow()); break;
                    case "m17-distance-restore": if(Ready)Queue("diagnostic camera restore",()=>Optimization.RestoreDistance()); break;
                    case "m17-map-resources": Map.Visible.Clear();Map.Visible.Add("Ресурсы");Map.Search="";break;
                    case "m16-audit": _sprintReadOnlySnapshot=M16RuntimeDiagnostics.Snapshot(this); break;
                    case "m16-source-select": Map.Selected=Map.Points.Find(p=>p.SourceObservation&&p.ExactHeight)??Map.Points.Find(p=>p.SourceObservation); break;
                    case "atlas-close": Map.CloseNative(); break;
                    case "m16-map-focus": if(Map.NativeMapOpen) {var targets=Map.Filter(true);if(targets.Count>0){var p=targets[0];Map.Ui._viewModel.CenterOnWorldPosition(new Vector2(p.Position.x,p.Position.z));}} break;
                    case "m16-map-max": if(Map.NativeMapOpen) {for(int i=0;i<12;i++)Map.Ui._viewModel.ZoomMapBy(.35f);} break;
                    case "m16-map-far": if(Map.NativeMapOpen) {for(int i=0;i<12;i++)Map.Ui._viewModel.ZoomMapBy(-.35f);} break;
                    case "menu-weather": NavigateMenu(command,()=>_ui.ShowPage(3)); break;
                    case "menu-capsules": NavigateMenu(command,_ui.ShowCapsules); break;
                    case "menu-sources": NavigateMenu(command,_ui.ShowSources); break;
                    case "continue": case "game-continue": TryContinueGame(); break;
                    case "bench-observe": Benchmark.Start(false); break;
                    case "bench-background": Benchmark.Start(true); break;
                    case "bench-cancel": Benchmark.Cancel("explicit-ipc-cancel"); break;
                    // Explicit local requests, on the main thread, same path as UI.
                    case "graphics-potato": if(Ready) Queue("diagnostic potato",()=>Optimization.ApplyPotato()); break;
                    case "graphics-restore": if(Ready) Queue("diagnostic graphics restore",()=>Optimization.Restore()); break;
                    case "graphics-lod-low": if(Ready) Queue("diagnostic geometry low",()=>Optimization.ApplyGeometryLow()); break;
                    case "graphics-lod-restore": if(Ready) Queue("diagnostic geometry restore",()=>Optimization.RestoreGeometry()); break;
                    case "recover": Input.RecoverGameplay(); break;
                    case "selftest": Tests.Request(); break;
                    case "testplayer": Tests.RequestMovement(); break;
                    case "testcancel": Tests.CancelRequest(); break;
                    case "functional": Audit.Request(); break;
                    case "functionalstop": Audit.Cancel(); break;
                    case "auditrelease": Audit.ReleaseMainAutosaveHold(); break;
                    case "auditexit": Audit.RequestProtectedExit("explicit protected audit exit"); break;
                    case "quit": SaveAndQuit(); break;
                    case "atlastest": if(Ready) MapProbe.Start(); break;
                    case "uitest": if(Ready) UiProbe.Start(); break;
                    case "m15-slider-test": if(Ready) SliderProbe.Start(); break;
                    case "m15-slider-cancel": SliderProbe.Cancel(); break;
                    case "atlas": if(Ready) Map.Open(); break;
                    case "atlasrefresh": if(Ready) Map.RefreshIndex(); break;
                    case "atlasreturn": if(Ready) Map.Return(); break;
                    case "capture": ScreenCapture.CaptureScreenshot(Path.Combine(DataDirectory,"capture.png")); break;
                    default:
                        if(command.StartsWith("menu-tab ",StringComparison.Ordinal)&&int.TryParse(command.Substring(9),out int menuTab)&&menuTab>=0&&menuTab<ToolkitUi.TabCount)
                        {NavigateMenu(command,()=>_ui.ShowPage(menuTab));break;}
                        if(M28TestIpc.Handle(this,_ui,command,NavigateMenu))break; // ct-/m28- relay, ct-test.enabled gated
                        if(M27AcceptanceIpc.Handle(this,_ui,command,NavigateMenu))break; // M27 opt-in file gated
                        if(M26AcceptanceIpc.Handle(this,_ui,command)||M26AcceptanceCore.Handle(this,_ui,command))break; // opt-in file gated
                        LoggerInstance.Warning("Unknown diagnostic command; auditrelease acknowledges evidence only, auditexit never saves the protected main."); break;
                }
            }
        }
        bool profile=Stress?.Measuring==true;long snapshotStart=profile?Stopwatch.GetTimestamp():0;
        var details = new System.Text.StringBuilder(16384);
        try
        {
            var input = Ready ? Context.Player.GetComponentInChildren<Il2CppMonomiPark.SlimeRancher.Player.CharacterController.SRCharacterInput>(true) : null;
            var loader=SystemContext.Instance?.SceneLoader;
            var controller=Ready?Context.Player.GetComponentInChildren<Il2CppMonomiPark.SlimeRancher.Player.CharacterController.SRCharacterController>(true):null;
            var motor=controller?.Motor;
            details.Clear().Append($"GameplayScene: {loader != null && loader.IsCurrentSceneGroupGameplay()}\nSceneLoading: {loader != null && loader.IsSceneLoadInProgress}\nRunInBackground: {Application.runInBackground}\nFocused: {Application.isFocused}\nCursorLock: {Cursor.lockState}\nCursorVisible: {Cursor.visible}\nTimeScale: {Time.timeScale}\nEnabledActions: {InputSystem.ListEnabledActions().Count}\nMissingAfterRestore: {Input.MissingAfterRestore}\nPlayerInputEnabled: {input != null && input._inputEnabled}\nPlayerMove: {(input != null ? input.MovementInput.ToString() : "none")}\nPosition: {(Ready ? Context.Player.transform.position.ToString("F3") : "none")}\nTest: {Tests.Status}\n");
            details.Append($"MotorPosition: {(motor != null ? motor.TransientPosition.ToString("F4") : "none")}\nMotorVelocity: {(motor != null ? motor.BaseVelocity.ToString("F4") : "none")}\nMotorEnabled: {motor != null && motor.enabled}\nControllerMove: {(controller != null ? controller.InputVector.ToString() : "none")}\nKeyboardEnabled: {Keyboard.current != null && Keyboard.current.enabled}\nW: {Keyboard.current != null && Keyboard.current.wKey.isPressed}\nInputBackground: {InputSystem.settings.backgroundBehavior}\n");
            details.Append($"MouseAvailable: {Mouse.current!=null}\nMouseLeftHeld: {Mouse.current!=null&&Mouse.current.leftButton.isPressed}\nMouseRightHeld: {Mouse.current!=null&&Mouse.current.rightButton.isPressed}\n");
        }
        catch (Exception e) { details.Clear().Append("DiagnosticError: " + e.Message + "\n"); }
        details.Append($"UiProbe: {UiProbe.Status}\nAtlasProbe: {MapProbe.Status}\nAtlasClicks: {Map.ClickRequests}\nAtlasPoints: {Map.Points.Count}\nAtlasIndex: {Map.IndexStatus}\nAtlasOpen: {Map.NativeMapOpen}\nAtlasProjectionError: {Map.ProjectionError:F3}\nAtlasTransit: {Map.Busy}\nAtlasStatus: {Map.TeleportStatus}\nRevealedZones: {Unlocks.RevealedZones}\nRevealedFog: {Unlocks.RevealedFogEvents}\n");
        details.Append("AtlasDisplay: "+Map.DisplayStatus+"\n");
        details.Append("MapRevealResult: "+Unlocks.MapResult+"\n");
        details.Append("AtlasTransitDetails: "+Map.TransitDetails+"\n");
        details.Append($"UiPage: {_ui.CurrentTabForTest}\nUiNavigation: {_lastUiNavigation}\nTransitPhysicsHeld: {Player.TransitHeld}\nTransitStableSamples: {Map.LastStableGroundSamples}\nTransitHoldSeconds: {Map.LastTransitHoldSeconds:F3}\nTransitSceneAssetsReady: {Map.LastSceneAssetsReady}\nCapsuleIds: {Map.Capsules.Database.Pods.Count}\nCapsuleRewardAssets: {Map.Capsules.Database.Assets.Count}\nCapsuleNativeTitles: {Map.Capsules.NativeTitleCount}\nCatalogCanonicalTypes: {Actors.CanonicalCount}\n");
        details.Append($"WeatherEntries: {World.Weather.Count}\nWeatherZone: {World.ZoneName}\nAuditActive: {Audit.Active}\nAuditStatus: {Audit.Status}\n");
        details.Append($"ConvenienceProfile: {Convenience.ProfileName}\nCustomHUD: {Convenience.ShowHud}\nSafeTracking: {Convenience.TrackSafe}\nSafePoint: {Convenience.HasSafe}\nAutoRefill: {Convenience.Refilling}\nRefillOperations: {Convenience.RefillOperations}\nRefillStatus: {Convenience.RefillStatus}\n");
        details.Append($"CustomWaypoints: {Custom.Records.Count}\nCustomCompass: {Custom.ShowCompass}\nCustomMapPlacement: {Map.CustomPointMode}\nRadarFood: {Radar.FavoriteFood}\nRadarRare: {Radar.RareResources}\nRadarTargets: {Radar.TargetCount}\nRadarQueryDefinitions: {Radar.QueryDefinitionCount}\nRadarExaminedModels: {Radar.ExaminedModelCount}\nFarmCollect: {Farm.Collect}\nFarmFeed: {Farm.Feed}\nFarmCollectorRoots: {Farm.CollectorCount}\nFarmFeederRoots: {Farm.FeederCount}\nFarmScannedPlots: {Farm.ScannedPlotCount}\nFarmTransfers: {Farm.TransferredUnits}\n");
        details.Append($"Stress: {Stress.Status}\nStressActive: {Stress.Active}\nOwnedUiTextures: {UiOwnedTextures}\nMapUiDiscoveries: {Map.UiDiscoveryScans}\nMapShields: {Map.ShieldCount}\n");
        details.Append($"CareInspector: {Care.Inspect}\nCareTargets: {Care.Hits.Count}\nCareActorRoots: {Care.NativeActorRoots}\nCareMeals: {Care.Meals}\nGadgetCatalog: {Gadgets.Catalog.Count}\nGadgetPreviewRoots: {Gadgets.PreviewRoots}\nGadgetPlacementPending: {Gadgets.PlacementPending}\nGadgetPlaced: {Gadgets.Placed}\nWeatherActive: {World.ActiveWeather.Count}\nWeatherDirectorStates: {World.NativeDirectorStates}\n");
        details.Append($"AuditAutosaveHeld: {Audit.MainAutosaveHeld}\nNativeAutosaveDisabled: {GameContext.Instance?.AutoSaveDirector?._autosaveDisabled}\n");
        if(AuditSaveGuard.Required)details.Append("AuditSaveProtection: "+System.Text.Json.JsonSerializer.Serialize(AuditSaveGuard.Evidence())+"\n");
        details.Append($"UiTheme: {UiThemePolicy.Theme}\nUiThemeTextureBudget: {ToolkitUi.ThemeTextureBudget}\nUiFont: {_ui.FontNameForTest}\nUiFontSource: {_ui.FontSourceForTest}\nUiFontCyrillic: {_ui.FontCyrillicForTest}\nUiFontNative: {_ui.FontNativeForTest}\nUiFontRounded: {_ui.FontRoundedForTest}\nUiFontResourceScans: {_ui.FontResourceScansForTest}\nUiJellyHeldRepaints: {_ui.JellyHeldRepaintsForTest}\nUiJellyHoverRepaints: {_ui.JellyHoverRepaintsForTest}\n");
        details.Append($"NativeIconCategories: {Map.Icons.CategoryCount}\nNativeRewardIcons: {Map.Icons.RewardCount}\nNativeIconAssetRoots: {Map.Icons.AssetRoots}\nNativeIconScans: {Map.Icons.DiscoveryScans}\nPhenomenaInspector: {Phenomena.Inspect}\nPhenomenaRows: {Phenomena.Rows.Count}\nPhenomenaActorRoots: {Phenomena.NativeActorRoots}\n");
        details.Append("WorldSources: "+Map.SourceStatus+"\n");
        details.Append(_sprintReadOnlySnapshot); // Explicit command snapshot only; never query native collections per status tick.
        details.Append("AtlasDroneScans: "+Map.DroneObservationScans+"\n");
        details.Append(World.WeatherResolutionDiagnostics());
        details.Append(ModProfiler.Status);
        details.Append(Benchmark.Status); // Cached strings only; no per-status timing reads.
        details.Append(Optimization.Diagnostics());
        details.Append(NativeUiPriority.Diagnostics());
        details.Append(InfiniteBuildHooks.Diagnostics());
        details.Append(M26AcceptanceCore.Diagnostics(this,_ui));details.Append(M26AcceptanceIpc.Diagnostics(this,_ui));
        details.Append(M27AcceptanceIpc.Diagnostics(this,_ui));
        details.Append(M28TestIpc.Diagnostics(this));
        details.Append($"OpenMenuKey: Insert\nHighJump: {Player.HighJump}\nJumpHeight: {Player.JumpHeightMultiplier:F2}\nJumpPrivateAssetRoots: {Player.JumpAssetRoots}\nInfiniteVacpackItems: {Player.InfiniteItems}\nPreservedVacpackSpends: {Player.PreservedSpends}\nNativeTabIcons: {Map.Icons.TabCount}\nTextListenerRoots: {_ui.TextListenerRootsForTest}\nTextCommits: {_ui.TextCommitsForTest}\nImePreviews: {_ui.ImePreviewsForTest}\nRejectedImeEvents: {_ui.RejectedImeEventsForTest}\nLastTextCallbackMs: {_ui.LastTextCallbackMsForTest:F4}\n");
        try{details.Append("UnityGcMode: "+UnityEngine.Scripting.GarbageCollector.GetMode()+"\n");}
        catch(Exception e){details.Append("UnityGcModeError: "+e.Message+"\n");}
        if(Ready)try
        {
            var registry=Context.WeatherRegistry;var tracker=Context.PlayerZoneTracker;
            details.Append($"WeatherInitialized: {registry!=null&&registry.Initialized}\nWeatherNativeZones: {registry?._zones?.Count}\nPlayerZone: {tracker?.GetCurrentZone()?.DebugNameKey}\nPlayerLatestZone: {tracker?.GetLatestZone()?.DebugNameKey}\n");
        }catch(Exception e){details.Append("WeatherDiagnostic: "+e.Message+"\n");}
        try { var save=GameContext.Instance?.AutoSaveDirector; var metadata=NativeSaveMetadata.Read(save); if(metadata!=null&&metadata.HasValue) details.Append("SaveGameName: "+metadata.Value.GameName+"\n"); /* Native TryGetCurrentGameSummary incorrectly looks up an unsuffixed filename; do not poll it. */ } catch(Exception e) { details.Append("SaveDiagnostic: "+e.Message+"\n"); }
        string snapshot=$"CreatorToolkit {Version}\nStandalone: true\nReady: {Ready}\nMenu: {MenuOpen}\nDisabledActions: {Input.DisabledCount}\nHooks: {InstalledHooks}\nCatalog: {Actors.Catalog.Count}\nSpawnedOwned: {Actors.OwnedCount}\nGod: {Player.GodMode}\nEnergy: {Player.InfiniteEnergy}\nSpeed: {Player.SpeedMultiplier:F2}\nFlight: {Player.Flight}\nFreezeTime: {World.FreezeTime}\nFreezeMobs: {World.FreezeMobs}\nHiddenHUD: {Cinema.HideHud}\nWaypoints: {Cinema.Waypoints.Count}\nError: {LastError}\nStatus: {Status}\n" + details.ToString();
        if(profile)Stress.RecordSection("diagnostic-snapshot",Stopwatch.GetTimestamp()-snapshotStart);
        _diagnosticIo.QueueSnapshot(snapshot);
    }
}
