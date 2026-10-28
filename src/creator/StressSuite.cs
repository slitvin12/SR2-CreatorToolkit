using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2Cpp;
using Il2CppMonomiPark.KFC;
using Il2CppMonomiPark.SlimeRancher.Player;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace CreatorToolkit;

// Opt-in, bounded telemetry on the FeatureSuite's owned copy only. This is not
// a production background profiler, an OS input driver or a claim of zero leaks.
internal sealed class StressSuite
{
    private const int PlannedReloads=9;
    private const int PlannedComparisonReloads=StressComparisonPlan.TotalReloads;
    internal sealed class FrameResult
    {
        public string Name {get;set;}
        public int Frames {get;set;}
        public double Seconds {get;set;}
        public double AverageFps {get;set;}
        public double MedianMs {get;set;}
        public double P95Ms {get;set;}
        public double P99Ms {get;set;}
        public double MaxMs {get;set;}
        public int Over33Ms {get;set;}
        public int Over50Ms {get;set;}
        public double ModUpdateMeanMs {get;set;}
        public double ModUpdateMaxMs {get;set;}
        public double ModGuiMeanMs {get;set;}
        public double ModGuiMaxMs {get;set;}
        public double ModManagedBytesPerFrame {get;set;}
        public double MotorPathMeters {get;set;}
        public double ResetOffsetMeters {get;set;}
        public int Refills {get;set;}
        public int MapUiDiscoveryScans {get;set;}
        public int WeatherZoneDiscoveryScans {get;set;}
        public int Gen0Collections {get;set;}
        public bool ExplorerArmed {get;set;}
        public int RadarScans {get;set;}
        public int FarmTransfers {get;set;}
        public List<SectionResult> Sections {get;set;}
        public List<EngineFrameProbe.Summary> NativeEngineCounters {get;set;}
        public bool OptimizationActive {get;set;}
        public int TextureMipmapLimit {get;set;}
        public int RenderPercentRequested {get;set;}
        public float RenderPercentActual {get;set;}
        public int NativeRenderWidth {get;set;}
        public int NativeRenderHeight {get;set;}
        public int FoliagePercent {get;set;}
        public int ParticlePercent {get;set;}
        public int FoliageManagers {get;set;}
        public int ParticleSnapshots {get;set;}
        public int WorldActorBatch {get;set;}
        public int TargetFrameRate {get;set;}
        public int VSyncCount {get;set;}
    }
    internal sealed class SectionResult
    {
        public string Name {get;set;}
        public int Calls {get;set;}
        public double MeanMs {get;set;}
        public double MaxMs {get;set;}
    }
    private sealed class SectionCounter {internal long Ticks,Max;internal int Calls;}
    internal sealed class StallResult
    {
        public string Window {get;set;}
        public double ElapsedSeconds {get;set;}
        public double FrameMs {get;set;}
        public double UpdateMs {get;set;}
        public string HeaviestSection {get;set;}
        public double HeaviestSectionMs {get;set;}
    }
    internal sealed class MemoryResult
    {
        public string Name {get;set;}
        public double ElapsedSeconds {get;set;}
        public long ProcessPrivateBytes {get;set;}
        public long WorkingSetBytes {get;set;}
        public int ProcessHandles {get;set;}
        public long DotNetRetainedBytes {get;set;}
        public long Il2CppRetainedBytes {get;set;}
        public long Il2CppBeforeCollectionBytes {get;set;}
        public string UnityGcMode {get;set;}
        public bool NativeGcTemporarilyEnabled {get;set;}
        public bool NativeGcModeRestored {get;set;}
        public int SmokeControllerRoots {get;set;}
        public long UnityAllocatedBytes {get;set;}
        public long UnityReservedBytes {get;set;}
        public long UnityManagedHeapBytes {get;set;}
        public long UnityManagedUsedBytes {get;set;}
        public int OwnedTextures {get;set;}
        public int NativeOwnedTextures {get;set;}
        public int NativeParameterClones {get;set;}
        public int NativeShields {get;set;}
        public int ManagedShields {get;set;}
        public int NavigationSubscriptions {get;set;}
        public int CachedMapDisplays {get;set;}
        public int DynamicLabels {get;set;}
        public int FilterCaches {get;set;}
        public int FrozenSnapshots {get;set;}
        public int OldContextWrappersAlive {get;set;}
        public int CustomMarkers {get;set;}
        public int RadarTargets {get;set;}
        public int FarmCollectorRoots {get;set;}
        public int FarmFeederRoots {get;set;}
        public int CareTargets {get;set;}
        public int CareActorRoots {get;set;}
        public int GadgetDefinitions {get;set;}
        public int GadgetPreviewRoots {get;set;}
        public int NativeUiProbeDeviceRoots {get;set;}
        public int RemainingMatrixSteps {get;set;}
        public int NativeIconAssetRoots {get;set;}
        public int NativeIconScans {get;set;}
        public int PhenomenaRows {get;set;}
        public int PhenomenaActorRoots {get;set;}
        public bool AfterExplicitTestOnlyGc {get;set;}
        public bool OptimizationActive {get;set;}
        public int OptimizationUiAssets {get;set;}
        public int OptimizationCameras {get;set;}
        public int OptimizationParticles {get;set;}
        public int OptimizationFoliage {get;set;}
        public int JumpAssetRoots {get;set;}
        public int NativeJumpClones {get;set;}
        public bool HighJump {get;set;}
        public bool InfiniteItems {get;set;}
        public int TextListenerRoots {get;set;}
        public int TextProbeDeviceRoots {get;set;}
        public int NativeTabIcons {get;set;}
    }
    private readonly struct Window
    {
        internal readonly string Name;
        internal readonly bool Hud,Fast,Refill,Potato;
        internal readonly float Seconds;
        internal Window(string name,bool hud,bool fast,bool refill,float seconds,bool potato=false)
        {Name=name;Hud=hud;Fast=fast;Refill=refill;Seconds=seconds;Potato=potato;}
    }
    private readonly ToolkitMod _app;
    private readonly List<float> _frames=new(8192);
    private readonly List<FrameResult> _windows=new();
    private readonly List<MemoryResult> _memory=new();
    private readonly Dictionary<string,SectionCounter> _sections=new();
    private readonly List<StallResult> _stalls=new();
    private readonly List<EngineFrameProbe.Hitch> _engineHitches=new();
    private readonly List<string> _engineNotes=new();
    private EngineFrameProbe _engine;
    private bool _engineProfilerSupported,_frameTimingFeatureEnabled;
    private bool _comparison;
    private int _comparisonReloads;
    private string _comparisonCheckpointHash="";
    private long _frameSectionMax;
    private string _frameSectionName="";
    private readonly List<string> _checks=new(),_warnings=new();
    private readonly List<WeakReference<SceneContext>> _oldContexts=new();
    private readonly Window[] _plan={
        new("idle_hud_off",false,false,false,10),new("idle_hud_on",true,false,false,10),
        new("fast_hud_off_A",false,true,false,12),new("fast_hud_on_A",true,true,false,12),
        new("fast_hud_on_B",true,true,false,12),new("fast_hud_off_B",false,true,false,12),
        new("normal_refill_hud_on",true,false,true,12),
        new("graphics_original_A",false,false,false,12),new("graphics_potato_B",false,false,false,12,true)};
    private readonly MapTools.Point[] _route=new MapTools.Point[3];
    private int _stage=-1,_window=-1,_routeLeg,_reloads,_burst,_errorsAtStart,_textureBaseline,_frame=-1;
    private int _refillsBefore,_scanBefore,_weatherScanBefore,_gen0Before,_direction,_failed,_radarBefore,_farmBefore;
    private long _updateTicks,_updateMax,_guiTicks,_guiMax,_allocated;
    private int _updateCalls,_guiCalls;
    private bool _uncappedBenchmark;
    private int _benchmarkFpsBefore,_benchmarkVsyncBefore;
    private float _started,_next,_until,_deadline,_measureStarted,_nextDirection,_nextRefill;
    private Vector3 _origin,_lastMotor;
    private Keyboard _keyboard;
    internal bool Active=>_stage>=0;
    internal bool Measuring {get;private set;}
    internal string Status {get;private set;}="idle";
    private string JsonPath=>Path.Combine(_app.DataDirectory,"stress.json");
    private string TextPath=>Path.Combine(_app.DataDirectory,"stress.txt");
    internal StressSuite(ToolkitMod app)=>_app=app;

    private static void Require(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
    private void Check(bool ok,string text)
    {
        _checks.Add((ok?"PASS ":"FAIL ")+text);if(!ok)_failed++;
        _app.LoggerInstance.Msg("Stress: "+_checks[_checks.Count-1]);
    }
    private void SetStatus(string text)
    {
        Status=text;_app.LoggerInstance.Msg("Stress: "+text);
        UnityEngine.Debug.Log("CreatorToolkit stress stage: "+text);Write(false);
    }
    internal void Start()
    {
        Require(!Active,"Stress suite is already active.");_app.Audit.GuardStressCopy();
        Require(!_app.Map.Busy&&!_app.UiProbe.Active&&!_app.MapProbe.Active,"Another native probe/transit is active.");
        _keyboard=Keyboard.current;
        Require(_keyboard!=null&&!_keyboard.wKey.isPressed&&!_keyboard.sKey.isPressed,"Stress refuses to override held W/S.");
        _windows.Clear();_memory.Clear();_checks.Clear();_warnings.Clear();_oldContexts.Clear();_sections.Clear();_stalls.Clear();_failed=0;
        _engineHitches.Clear();_engineNotes.Clear();_comparison=false;_comparisonReloads=0;_comparisonCheckpointHash="";
        _engine?.Dispose();_engine=null;
        _started=Time.realtimeSinceStartup;_errorsAtStart=_app.FailureSerial;_window=-1;_routeLeg=_reloads=_burst=0;
        _app.Optimization.Reset();_app.CloseMenu();_app.Map.CloseNative();_app.Convenience.Reset();SetExplorer(false);_app.World.Reset();_app.Player.Reset();_app.Player.Tick();
        _app.Player.GodMode=_app.Player.InfiniteEnergy=true;
        _origin=_app.Player.Controller.Motor.TransientPosition;
        var group=_app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true).SceneGroup;
        _route[2]=new MapTools.Point{Id="CT.Stress.Origin",Name="Stress origin",Group=group,GroupName=group.name,Position=_origin,ExactHeight=true,AllowNearbyLanding=true};
        _app.Map.RefreshIndex();
        string[] names={"LuminousStrand","RumblingGorge"};
        for(int n=0;n<names.Length;n++)
        {
            var target=_app.Map.Points.FirstOrDefault(p=>p.GroupName==names[n]&&p.Group!=null)?.Group;
            Require(target!=null,"Native route scene group is not resolvable: "+names[n]);
            // InitialPlayerPosition is a scene spawn transform, not an indexed
            // object's ground-level coordinate. Use the normal map surface
            // search; capsule/slope/hazard safety remains fully enforced.
            _route[n]=new MapTools.Point{Id="CT.Stress."+names[n],Name="Stress "+names[n],Group=target,GroupName=target.name,Position=target.InitialPlayerPosition,ExactHeight=false};
        }
        _textureBaseline=_app.UiOwnedTextures;
        _engine=new EngineFrameProbe(_engineNotes);_engineProfilerSupported=_engine.ProfilerSupported;_frameTimingFeatureEnabled=_engine.TimingFeatureEnabled;
        _stage=0;_until=Time.realtimeSinceStartup+12f;_deadline=Time.realtimeSinceStartup+840f;
        SetStatus("Стресс: прогрев мира и шейдеров (12 с)");
    }
    internal void RecordUpdate(long ticks,long bytes)
    {
        if(!Measuring)return;_updateTicks+=ticks;_updateMax=Math.Max(_updateMax,ticks);_allocated+=Math.Max(0,bytes);_updateCalls++;
        if(1000.0*ticks/Stopwatch.Frequency>50&&_stalls.Count<128)
            _stalls.Add(new StallResult{Window=_plan[_window].Name,ElapsedSeconds=Time.realtimeSinceStartup-_started,
                FrameMs=Time.unscaledDeltaTime*1000,UpdateMs=1000.0*ticks/Stopwatch.Frequency,
                HeaviestSection=_frameSectionName,HeaviestSectionMs=1000.0*_frameSectionMax/Stopwatch.Frequency});
        _frameSectionMax=0;_frameSectionName="";
    }
    internal void RecordSection(string name,long ticks)
    {
        if(!Measuring)return;
        if(!_sections.TryGetValue(name,out var counter))
        {
            if(_sections.Count>=32)return;
            _sections.Add(name,counter=new SectionCounter());
        }
        counter.Ticks+=ticks;counter.Max=Math.Max(counter.Max,ticks);counter.Calls++;
        if(ticks>_frameSectionMax){_frameSectionMax=ticks;_frameSectionName=name;}
    }
    internal void RecordGui(long ticks,long bytes)
    {
        if(!Measuring)return;_guiTicks+=ticks;_guiMax=Math.Max(_guiMax,ticks);_allocated+=Math.Max(0,bytes);_guiCalls++;
    }
    private unsafe void Move(int direction)
    {
        if(direction==_direction)return;
        var keyboard=_keyboard??Keyboard.current;
        Require(keyboard!=null&&!keyboard.WasCollected,"Native keyboard vanished during stress input.");
        var state=StateEvent.From(keyboard,out InputEventPtr ptr,Allocator.Temp);
        try
        {
            InputControlExtensions.WriteValueIntoEvent<float>(keyboard.wKey,direction>0?1:0,ptr);
            InputControlExtensions.WriteValueIntoEvent<float>(keyboard.sKey,direction<0?1:0,ptr);
            InputSystem.QueueEvent(ptr);
        }
        finally{state.Dispose();}
        _keyboard=keyboard;_direction=direction;
    }
    private void ArmRefill()
    {
        var ammo=_app.Context.PlayerState.Ammo;int slot=ammo._selectedAmmoIdx;
        Require(slot>=0&&slot<ammo.Slots.Length&&ammo.Slots[slot]?.Id!=null&&ammo.Slots[slot].Count>0,"Stress refill needs the audit's populated selected slot.");
        _app.Convenience.ArmRefillForTest();
    }
    private void SetAmmoCount(int count)
    {
        var ammo=_app.Context.PlayerState.Ammo;ammo.Slots[ammo._selectedAmmoIdx].Count=count;
    }
    private void SetExplorer(bool enabled)
    {
        _app.Audit.GuardStressCopy();
        if(!enabled){_app.Custom.HideGoal();_app.Radar.Reset();_app.Farm.Reset();_app.Care.Reset();_app.Gadgets.Reset();return;}
        _app.Care.SetInspect(true);_app.Gadgets.RefreshCatalog();
        _app.Custom.EnsureLoaded();
        Require(_app.Custom.Records.Count==1,"Owned persistent waypoint fixture is missing or duplicated.");
        _app.Custom.SetGoal(_app.Custom.Records[0].Id);
        var slime=_app.Actors.Catalog.First(e=>e.Category=="Слаймы"&&!e.IsPrototype&&
            e.Type.TryCast<SlimeDefinition>()?.Diet?.FavoriteIdents?.Length>0);
        _app.Radar.SetSlime(slime);_app.Radar.SetModes(true,true);
        _app.Farm.SetModes(true,true);
    }
    private void BeginWindow()
    {
        _window++;
        if(_window>=_plan.Length)
        {
            RestoreBenchmarkTiming();
            _app.Optimization.Reset();
            Move(0);_app.Player.SpeedMultiplier=1;_app.Player.Tick();_app.Convenience.SetHud(true);
            _app.Convenience.StopRefill("enter forced burst");SetAmmoCount(100);ArmRefill();
            _refillsBefore=_app.Convenience.RefillOperations;_stage=3;SetStatus("Стресс: 2000 последовательных native-пополнений на копии");return;
        }
        var w=_plan[_window];Move(0);_app.Convenience.StopRefill("stress window");
        if(w.Name.StartsWith("graphics_",StringComparison.Ordinal))
        {
            if(!_uncappedBenchmark)
            {
                _benchmarkFpsBefore=Application.targetFrameRate;_benchmarkVsyncBefore=QualitySettings.vSyncCount;
                _uncappedBenchmark=true;
            }
            Application.targetFrameRate=-1;QualitySettings.vSyncCount=0; // Copy-only benchmark, NOT the profile.
        }
        else RestoreBenchmarkTiming();
        _app.Audit.GuardStressCopy();
        if(w.Potato)_app.Optimization.ApplyPotato();else _app.Optimization.Reset();
        _app.Player.SpeedMultiplier=w.Fast?5:1;_app.Player.Tick();
        // Reset only outside measurement. The live world can occupy the anchor
        // between windows, so use the same bounded, collision-checked saved
        // position helper instead of bypassing safety or aborting a valid test.
        Vector3 reset=_origin;
        if(!_app.Player.IsClearPosition(reset))
        {
            Require(_app.Player.TryNearbyMapGround(_origin,out reset),"FPS reset has no safe nearby ground.");
            Check(true,"occupied FPS anchor safely offset before "+w.Name);
        }
        _resetOffset=Vector3.Distance(reset,_origin);
        _app.Player.PlaceForMap(reset);_app.Convenience.SetHud(w.Hud);_app.Convenience.HudPlacement=_window%3;
        SetExplorer(w.Hud);
        if(w.Refill)ArmRefill();
        _stage=1;_until=Time.realtimeSinceStartup+(w.Name.StartsWith("graphics_",StringComparison.Ordinal)?5f:2f);
        SetStatus("Стресс: подготовка "+w.Name);
    }
    private void BeginMeasure()
    {
        _frames.Clear();_updateTicks=_updateMax=_guiTicks=_guiMax=_allocated=0;_updateCalls=_guiCalls=0;
        _sections.Clear();_frameSectionMax=0;_frameSectionName="";
        _refillsBefore=_app.Convenience.RefillOperations;_scanBefore=_app.Map.UiDiscoveryScans;_weatherScanBefore=_app.World.WeatherZoneDiscoveryScans;_gen0Before=GC.CollectionCount(0);
        _radarBefore=_app.Radar.Scans;_farmBefore=_app.Farm.TransferredUnits;
        _lastMotor=_app.Player.Controller.Motor.TransientPosition;_path=0;_frame=-1;
        _engine?.BeginWindow();
        _measureStarted=Time.realtimeSinceStartup;_until=_measureStarted+_plan[_window].Seconds;_nextDirection=_nextRefill=0;
        _stage=2;SetStatus("Стресс: замер "+_plan[_window].Name);Measuring=true;
    }
    private double _path,_resetOffset;
    private static double Quantile(float[] sorted,double p)=>sorted.Length==0?0:sorted[(int)Math.Clamp(Math.Ceiling(sorted.Length*p)-1,0,sorted.Length-1)];
    private void EndMeasure()
    {
        Measuring=false;Move(0);var w=_plan[_window];float[] sorted=_frames.ToArray();Array.Sort(sorted);
        double elapsed=Time.realtimeSinceStartup-_measureStarted;
        _windows.Add(new FrameResult{Name=w.Name,Frames=sorted.Length,Seconds=elapsed,AverageFps=sorted.Length/Math.Max(.001,elapsed),
            MedianMs=Quantile(sorted,.5),P95Ms=Quantile(sorted,.95),P99Ms=Quantile(sorted,.99),MaxMs=sorted.LastOrDefault(),
            Over33Ms=sorted.Count(v=>v>33.333),Over50Ms=sorted.Count(v=>v>50),
            ModUpdateMeanMs=1000.0*_updateTicks/Stopwatch.Frequency/Math.Max(1,_updateCalls),ModUpdateMaxMs=1000.0*_updateMax/Stopwatch.Frequency,
            ModGuiMeanMs=1000.0*_guiTicks/Stopwatch.Frequency/Math.Max(1,_guiCalls),ModGuiMaxMs=1000.0*_guiMax/Stopwatch.Frequency,
            ModManagedBytesPerFrame=(double)_allocated/Math.Max(1,sorted.Length),MotorPathMeters=_path,ResetOffsetMeters=_resetOffset,
            Refills=_app.Convenience.RefillOperations-_refillsBefore,MapUiDiscoveryScans=_app.Map.UiDiscoveryScans-_scanBefore,
            WeatherZoneDiscoveryScans=_app.World.WeatherZoneDiscoveryScans-_weatherScanBefore,Gen0Collections=GC.CollectionCount(0)-_gen0Before,
            ExplorerArmed=w.Hud,RadarScans=_app.Radar.Scans-_radarBefore,FarmTransfers=_app.Farm.TransferredUnits-_farmBefore,
            Sections=_sections.Select(k=>new SectionResult{Name=k.Key,Calls=k.Value.Calls,MeanMs=1000.0*k.Value.Ticks/Stopwatch.Frequency/Math.Max(1,k.Value.Calls),MaxMs=1000.0*k.Value.Max/Stopwatch.Frequency}).ToList(),
            NativeEngineCounters=_engine?.Summaries()??new List<EngineFrameProbe.Summary>(),
            OptimizationActive=_app.Optimization.Active,TextureMipmapLimit=QualitySettings.globalTextureMipmapLimit,
            RenderPercentRequested=_app.Optimization.Current.RenderPercent,RenderPercentActual=_app.Optimization.ActualRenderPercent,
            NativeRenderWidth=_app.Optimization.ActualRenderSize.x,NativeRenderHeight=_app.Optimization.ActualRenderSize.y,
            FoliagePercent=_app.Optimization.Current.FoliagePercent,ParticlePercent=_app.Optimization.Current.ParticlePercent,
            FoliageManagers=_app.Optimization.FoliageManagers,ParticleSnapshots=_app.Optimization.ParticleSnapshots,
            WorldActorBatch=WorldPopulator.ACTORS_PER_FRAME,TargetFrameRate=Application.targetFrameRate,VSyncCount=QualitySettings.vSyncCount});
        Check(sorted.Length>=60,"frame samples collected: "+w.Name+" n="+sorted.Length);
        Check(_app.Map.UiDiscoveryScans==_scanBefore,"closed-map FPS window avoids global display scans: "+w.Name);
        if(w.Fast)Check(_path>5,"native fast movement actually travelled: "+w.Name+" meters="+_path.ToString("F2"));
        if(w.Refill)Check(_app.Convenience.RefillOperations-_refillsBefore>=20,"normal 4 Hz refill callbacks exercised");
        if(w.Hud)Check(_app.Radar.Scans-_radarBefore>=5,"native current-zone radar scans exercised within the armed FPS window");
        if(w.Potato)Check(_app.Optimization.Active&&_app.Optimization.Current.Mip==3&&
            Math.Abs(_app.Optimization.ActualRenderPercent-70)<1.5f,"actual lightweight graphics profile stayed applied during measurement");
        if(w.Name.StartsWith("graphics_",StringComparison.Ordinal))
            Check(Application.targetFrameRate==-1&&QualitySettings.vSyncCount==0,
                "copy-only matched graphics benchmark is uncapped: "+w.Name);
        BeginWindow();
    }
    private void RestoreBenchmarkTiming()
    {
        if(!_uncappedBenchmark)return;
        Application.targetFrameRate=_benchmarkFpsBefore;QualitySettings.vSyncCount=_benchmarkVsyncBefore;
        _uncappedBenchmark=false;
    }
    private void Memory(string name)
    {
        _app.Audit.GuardStressCopy();
        Require(!Measuring,"Retained-memory collections must be outside FPS windows.");
        // Explicit collectors are test-only and outside all FPS windows. Reserved
        // Unity pools and streamed assets are reported separately from live data.
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long il2cpp=-1,beforeCollection=-1;
        string gcMode="unavailable";bool temporaryEnable=false,modeRestored=false;
        var originalMode=UnityEngine.Scripting.GarbageCollector.GetMode();
        gcMode=originalMode.ToString();
        beforeCollection=Il2CppSystem.GC.GetTotalMemory(false);
        temporaryEnable=originalMode==UnityEngine.Scripting.GarbageCollector.Mode.Disabled;
        try
        {
            // Unity's property setter is stripped in these exact bindings.
            // Use the verified native GetMode/SetMode APIs, only for this
            // owned-copy snapshot; never change production GC/FPS behavior.
            if(temporaryEnable)UnityEngine.Scripting.GarbageCollector.SetMode(UnityEngine.Scripting.GarbageCollector.Mode.Enabled);
            Il2CppSystem.GC.Collect();
            il2cpp=Il2CppSystem.GC.GetTotalMemory(false);
        }
        finally
        {
            if(temporaryEnable)UnityEngine.Scripting.GarbageCollector.SetMode(originalMode);
            modeRestored=UnityEngine.Scripting.GarbageCollector.GetMode()==originalMode;
        }
        Require(modeRestored,"Native GC mode was not restored after the owned-copy snapshot.");
        using var process=Process.GetCurrentProcess();process.Refresh();
        var m=new MemoryResult{Name=name,ElapsedSeconds=Time.realtimeSinceStartup-_started,ProcessPrivateBytes=process.PrivateMemorySize64,
            WorkingSetBytes=process.WorkingSet64,ProcessHandles=process.HandleCount,DotNetRetainedBytes=GC.GetTotalMemory(false),Il2CppRetainedBytes=il2cpp,
            Il2CppBeforeCollectionBytes=beforeCollection,UnityGcMode=gcMode,NativeGcTemporarilyEnabled=temporaryEnable,NativeGcModeRestored=modeRestored,
            SmokeControllerRoots=_app.Tests.HoldsPlayerController?1:0,
            OwnedTextures=_app.UiOwnedTextures,ManagedShields=_app.Map.ShieldCount,NavigationSubscriptions=_app.Map.SubscriptionCount,CachedMapDisplays=_app.Map.CachedUiCount,
            DynamicLabels=_app.UiDynamicLabels,FilterCaches=_app.UiFilterCaches,FrozenSnapshots=_app.World.FrozenCount,AfterExplicitTestOnlyGc=true,
            CustomMarkers=_app.Custom.Records.Count,RadarTargets=_app.Radar.TargetCount,
            FarmCollectorRoots=_app.Farm.CollectorCount,FarmFeederRoots=_app.Farm.FeederCount,
            CareTargets=_app.Care.Hits.Count,CareActorRoots=_app.Care.NativeActorRoots,
            GadgetDefinitions=_app.Gadgets.Catalog.Count,GadgetPreviewRoots=_app.Gadgets.PreviewRoots,
            NativeUiProbeDeviceRoots=_app.UiProbe.HoldsDevice?1:0,RemainingMatrixSteps=_app.Audit.CachedStepCount,
            NativeIconAssetRoots=_app.Map.Icons.AssetRoots,NativeIconScans=_app.Map.Icons.DiscoveryScans,
            PhenomenaRows=_app.Phenomena.Rows.Count,PhenomenaActorRoots=_app.Phenomena.NativeActorRoots,
            OptimizationActive=_app.Optimization.Active,OptimizationUiAssets=_app.Optimization.UiTextureRoots,
            OptimizationCameras=_app.Optimization.CameraSnapshots,OptimizationParticles=_app.Optimization.ParticleSnapshots,
            OptimizationFoliage=_app.Optimization.FoliageManagers,
            JumpAssetRoots=_app.Player.JumpAssetRoots,HighJump=_app.Player.HighJump,
            InfiniteItems=_app.Player.InfiniteItems,TextListenerRoots=_app.UiForTests.TextListenerRootsForTest,
            TextProbeDeviceRoots=NativeTextProbe.HoldsDevice?1:0,NativeTabIcons=_app.Map.Icons.TabCount};
        try
        {
            m.UnityAllocatedBytes=Profiler.GetTotalAllocatedMemoryLong();m.UnityReservedBytes=Profiler.GetTotalReservedMemoryLong();
            m.UnityManagedHeapBytes=Profiler.GetMonoHeapSizeLong();m.UnityManagedUsedBytes=Profiler.GetMonoUsedSizeLong();
        }
        catch(Exception e){_warnings.Add("Unity allocator metric unavailable: "+e.Message);}
        foreach(var texture in Resources.FindObjectsOfTypeAll<Texture2D>())if(texture!=null&&!texture.WasCollected&&texture.name.StartsWith("CreatorToolkit_UI_",StringComparison.Ordinal))m.NativeOwnedTextures++;
        foreach(var c in Resources.FindObjectsOfTypeAll<CharacterControllerParameters>())if(c!=null&&!c.WasCollected&&c.name=="CreatorToolkit_PlayerParameters")m.NativeParameterClones++;
        foreach(var c in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Player.CharacterController.Abilities.JumpAbilityData>())
            if(c!=null&&!c.WasCollected&&c.name=="CreatorToolkit_PrivateJumpAbility")m.NativeJumpClones++;
        foreach(var image in Resources.FindObjectsOfTypeAll<Image>())if(image!=null&&!image.WasCollected&&image.name=="CreatorToolkit_MapInputShield")m.NativeShields++;
        foreach(var weak in _oldContexts)if(weak.TryGetTarget(out _))m.OldContextWrappersAlive++;
        _memory.Add(m);
        Check(m.SmokeControllerRoots==0,"completed smoke test does not retain a player controller after "+name);
        Check(m.NativeOwnedTextures==m.OwnedTextures&&m.OwnedTextures==_textureBaseline,"owned UI textures stable after "+name);
        Check(m.NativeParameterClones<=1,"no accumulated player-parameter clones after "+name);
        Check(m.NativeShields==0&&m.ManagedShields==0,"closed-map shields destroyed after "+name);
        Check(m.NavigationSubscriptions<=1&&m.CachedMapDisplays<=8,"bounded map subscriptions/displays after "+name);
        Check(m.DynamicLabels<=513&&m.FilterCaches<=41,"dynamic UI caches bounded after "+name);
        Check(m.CustomMarkers<=32&&m.RadarTargets<=32&&m.FarmCollectorRoots<=64&&m.FarmFeederRoots<=64,"new exploration caches bounded after "+name);
        Check(m.CareTargets<=CarePolicy.MaxTargets&&m.CareActorRoots==0&&m.GadgetDefinitions<=CarePolicy.MaxGadgets&&
            m.GadgetPreviewRoots==0&&m.NativeUiProbeDeviceRoots==0&&m.RemainingMatrixSteps<=2,
            "0.6 care/gadget/probe and retired matrix native roots bounded after "+name);
        Check(m.NativeIconAssetRoots<=NativeIconTools.AssetBudget&&m.NativeIconScans<=2&&
            m.PhenomenaRows<=PhenomenaTools.MaxRows&&m.PhenomenaActorRoots==0,
            "native icon assets and primitive phenomena snapshots bounded after "+name);
        Check(m.OptimizationUiAssets<=OptimizationPolicy.UiTextureBudget&&m.OptimizationCameras<=OptimizationPolicy.CameraBudget&&
            m.OptimizationParticles<=OptimizationPolicy.ParticleBudget&&m.OptimizationFoliage<=OptimizationPolicy.FoliageBudget&&
            _app.Optimization.NativeActorRoots==0,"bounded optimization asset/value snapshots after "+name);
        Check(!m.HighJump&&!m.InfiniteItems&&m.JumpAssetRoots==0&&m.NativeJumpClones==0&&
            m.TextListenerRoots==0&&m.TextProbeDeviceRoots==0,
            "LO cheats default off; no private jump/text/probe roots after "+name);
        Check(m.NativeTabIcons==ToolkitUi.TabCount,"all primary tabs use original game sprites after "+name);
        Write(false);
    }
    private void BeginRoute()
    {
        _app.Audit.GuardStressCopy();_app.Optimization.ApplyPotato();
        SetExplorer(false);
        _engine?.Dispose();_engine=null;
        _app.Convenience.StopRefill("stress route");_app.Player.SpeedMultiplier=1;_app.Player.Tick();
        VerifyOccupiedSavedPoint();
        _app.World.SetFreezeMobs(true);_routeLeg=0;_stage=4;_next=Time.realtimeSinceStartup+2f;
        SetStatus("Стресс: повторная загрузка трёх регионов, 9 безопасных переходов");
    }
    private void VerifyOccupiedSavedPoint()
    {
        // A transient collider is never serialized into the save. Deliberately
        // block the saved feet position to test actual native physics, rather
        // than relying on a random actor happening to occupy it this run.
        GameObject blocker=null;
        try
        {
            blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.name="CreatorToolkit_Stress_ReturnBlocker";
            int mask=_app.Player.Controller.Motor.CollidableLayers.value,layer=0;
            while(layer<31&&(mask&(1<<layer))==0)layer++;
            Require((mask&(1<<layer))!=0,"Native collidable layer mask is empty.");
            blocker.layer=layer;blocker.transform.localScale=new Vector3(2f,3f,2f);
            blocker.transform.position=_origin+Vector3.up*1.5f;
            Physics.SyncTransforms();
            Check(!_app.Player.IsClearPosition(_origin),"saved-position fixture blocks native capsule");
            Require(!_app.Player.IsClearPosition(_origin),"Occupied-position fixture is not collidable.");
            bool safe=_app.Player.TryNearbyMapGround(_origin,out var landing);
            Check(safe,"occupied saved position resolves to collision-checked nearby ground");
            Require(safe,"No safe nearby landing found in the native occupied-position fixture.");
            var offset=landing-_origin;
            Check(offset.sqrMagnitude>.01f&&SafetyPolicy.NearbyReturnOffset(offset.x,offset.y,offset.z)&&_app.Player.IsClearPosition(landing),
                "nearby saved landing stays within 3 m and 2 m height, with clear native capsule");
        }
        finally
        {
            if(blocker!=null){blocker.SetActive(false);UnityEngine.Object.Destroy(blocker);}
        }
    }
    private void BeginReload()
    {
        _app.Optimization.Reset();
        Move(0);_app.Convenience.Reset();_app.World.Reset();_app.Player.Reset();_app.Player.Tick();
        // These are deliberately armed before unload; BindSession must reset
        // them, rather than allowing a stale slot/session/controller to survive.
        _app.Convenience.SetProfile(1);ArmRefill();_app.Convenience.SetSafeTracking(true);
        _oldContexts.Add(new WeakReference<SceneContext>(_app.Context));
        _stage=8;_next=Time.realtimeSinceStartup+50f;
        SetStatus("Стресс: штатный выход в меню и загрузка копии, цикл "+(_reloads+1)+"/"+PlannedReloads);
        _app.Audit.BeginStressMenuReload(armOptimizationForUnload:true);
    }
    private static bool ComparisonArmed(int index)=>StressComparisonPlan.Armed(index);
    private static string ComparisonName(int index)=>StressComparisonPlan.Name(index);
    private void BeginComparison()
    {
        _comparison=true;_comparisonReloads=0;
        _comparisonCheckpointHash=_app.Audit.StressCheckpointFingerprint();
        Check(_comparisonCheckpointHash.Length==64,"immutable owned checkpoint fingerprint captured for ABBA comparison");
        BeginComparisonReload();
    }
    private void BeginComparisonReload()
    {
        Move(0);_app.Convenience.Reset();SetExplorer(false);_app.World.Reset();_app.Player.Reset();_app.Player.Tick();
        if(ComparisonArmed(_comparisonReloads))
        {_app.Convenience.SetProfile(1);ArmRefill();_app.Convenience.SetSafeTracking(true);SetExplorer(true);}
        Require(_app.Audit.StressCheckpointFingerprint()==_comparisonCheckpointHash,"Owned comparison checkpoint changed before reload.");
        _oldContexts.Add(new WeakReference<SceneContext>(_app.Context));
        _stage=8;_next=Time.realtimeSinceStartup+50f;
        SetStatus("Стресс: неизменный checkpoint, "+ComparisonName(_comparisonReloads)+" ("+(_comparisonReloads+1)+"/"+PlannedComparisonReloads+")");
        _app.Audit.BeginStressMenuReload(saveCheckpoint:false);
    }
    internal void Tick()
    {
        if(!Active)return;
        try
        {
            float now=Time.realtimeSinceStartup;
            Require(now<_deadline,"Stress global deadline reached; no repeated launch or save replacement attempted.");
            if(_stage==8)
            {
                Require(now<_next,"Native unload to main menu timed out.");
                var loader=SystemContext.Instance?.SceneLoader;
                if(_app.Ready||loader==null||loader.IsSceneLoadInProgress||loader.IsCurrentSceneGroupGameplay())return;
                Check(_app.Session==IntPtr.Zero,"old world session released before "+(_comparison?ComparisonName(_comparisonReloads):"reload "+(_reloads+1)));
                _keyboard=null;_app.Audit.LoadStressCopyFromMenu();_stage=9;_next=now+60f;return;
            }
            if(_stage==9)
            {
                Require(now<_next,"Native owned-world reload timed out.");if(!_app.Ready)return;
                _app.Audit.RebindStressCopy();_keyboard=Keyboard.current;
                string reloadName=_comparison?ComparisonName(_comparisonReloads):"world reload "+(_reloads+1);
                Check(!_app.Convenience.ShowHud&&!_app.Convenience.Refilling&&!_app.Convenience.TrackSafe&&_app.Convenience.Profile==0,
                    "HUD/refill/profile/SOS reset on "+reloadName);
                Check(!_app.Player.Flight&&Math.Abs(_app.Player.SpeedMultiplier-1)<.001f,"native player reset on "+reloadName);
                Check(!_app.Player.HighJump&&!_app.Player.InfiniteItems&&_app.Player.JumpAssetRoots==0&&
                    _app.UiForTests.TextListenerRootsForTest==0&&!NativeTextProbe.HoldsDevice,
                    "LO jump, infinite items and input listeners reset on "+reloadName);
                Check(!_app.Custom.ShowCompass&&!_app.Map.CustomPointMode&&!_app.Radar.Enabled&&!_app.Farm.Enabled&&
                    _app.Radar.TargetCount==0&&_app.Farm.CollectorCount==0&&_app.Farm.FeederCount==0,
                    "custom navigation/radar/farm native roots reset on "+reloadName);
                Check(_app.Custom.Records.Count==1,"named colored waypoint file persisted across "+reloadName);
                Check(!_app.Optimization.Active&&_app.Optimization.UiTextureRoots==0&&_app.Optimization.CameraSnapshots==0&&
                    _app.Optimization.ParticleSnapshots==0,"graphics profile and its roots reset on "+reloadName);
                if(_comparison)
                {
                    Require(_app.Audit.StressCheckpointFingerprint()==_comparisonCheckpointHash,"Owned comparison checkpoint was modified.");
                    Check(true,"same immutable checkpoint reloaded: "+reloadName);
                    if(ComparisonArmed(_comparisonReloads))
                    {_app.Convenience.SetProfile(1);ArmRefill();_app.Convenience.SetSafeTracking(true);SetExplorer(true);}
                    _stage=13;_next=now+6f;return;
                }
                _reloads++;_stage=10;_next=now+6f;_app.Convenience.SetHud(true);return;
            }
            if(!_app.Ready)return;
            _app.Audit.RebindStressCopyIfNeeded();
            _app.Audit.GuardStressCopy();
            if(Time.timeScale<=0){SystemContext.Instance.SceneLoader.UnpauseGame();return;}
            switch(_stage)
            {
                case 0:
                    if(now<_until)return;Memory("warm_baseline");BeginWindow();break;
                case 1:
                    if(now>=_until)BeginMeasure();break;
                case 2:
                    var w=_plan[_window];
                    if(w.Fast&&now>=_nextDirection){_nextDirection=now+1.25f;Move(_direction>0?-1:1);}
                    if(w.Refill&&now>=_nextRefill){_nextRefill=now+.25f;SetAmmoCount(25);}
                    if(_frame!=Time.frameCount)
                    {
                        _frame=Time.frameCount;if(_frames.Count<60000)_frames.Add(Time.unscaledDeltaTime*1000f);else throw new InvalidOperationException("Bounded frame buffer exhausted.");
                        _engine?.SamplePreviousFrame();
                        if(Time.unscaledDeltaTime>.05f&&_engineHitches.Count<128&&_engine!=null)
                            _engineHitches.Add(_engine.CaptureHitch(w.Name,now-_started,Time.unscaledDeltaTime*1000.0));
                        var position=_app.Player.Controller.Motor.TransientPosition;Require(float.IsFinite(position.x)&&float.IsFinite(position.y)&&float.IsFinite(position.z),"Non-finite native motor position.");
                        _path+=Vector3.Distance(_lastMotor,position);_lastMotor=position;
                    }
                    if(now>=_until)EndMeasure();break;
                case 3:
                    for(int i=0;i<4&&_burst<2000;i++)
                    {
                        SetAmmoCount(1+_burst%25);_app.Convenience.TickRefill();_burst++;
                        var ammo=_app.Context.PlayerState.Ammo;Require(ammo.Slots[ammo._selectedAmmoIdx].Count==100,"Burst refill lost native count/model binding.");
                    }
                    if(_burst<2000)return;
                    Check(_app.Convenience.RefillOperations-_refillsBefore==2000,"2000 native refill/depletion cycles completed");
                    _app.Convenience.StopRefill("stress burst complete");_stage=11;_next=now+3;break;
                case 11:
                    if(now<_next)return;Memory("after_2000_refills");BeginRoute();break;
                case 4:
                    if(now<_next)return;
                    _app.Convenience.SetHud(true);_app.Map.RequestTeleport(_route[_routeLeg%3]);_stage=5;_next=now+65f;break;
                case 5:
                    Require(now<_next,"Cross-region transit timed out.");if(_app.Map.Busy)return;
                    Check(_app.Map.LastTransitSucceeded,"safe transit "+(_routeLeg+1)+" to "+_route[_routeLeg%3].GroupName);
                    Require(_app.Map.LastTransitSucceeded,"Route landing failed: "+_app.Map.TransitDetails);
                    var player=_app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true);
                    Check(player.SceneGroup==_route[_routeLeg%3].Group,"correct native region after transit "+(_routeLeg+1));
                    Check(SystemContext.Instance.SceneLoader.CurrentSceneGroup==_route[_routeLeg%3].Group,"destination scenes actually loaded after transit "+(_routeLeg+1));
                    Check(!_app.Player.TransitHeld&&_app.Player.Controller.Motor.enabled&&_app.Map.LastSceneAssetsReady&&
                        _app.Map.LastStableGroundSamples>=4&&_app.Map.LastTransitHoldSeconds>=1f,
                        "stable collision warmup and native physics restoration after transit "+(_routeLeg+1));
                    Check(_app.Optimization.Active&&_app.Optimization.SmoothActive,
                        "graphics/loading profile preserved during streamed transit "+(_routeLeg+1));
                    int validated=0;
                    foreach(var pod in UnityEngine.Object.FindObjectsOfType<TreasurePod>())
                    {Require(_app.Map.Capsules.MatchesLoadedPod(pod.Id,pod),"Cross-region capsule contents mismatch: "+pod.Id);validated++;}
                    Check(validated>0,"native capsule reward IDs checked in destination "+(_routeLeg+1));
                    _routeLeg++;_stage=6;_next=now+3f;break;
                case 6:
                    if(now<_next)return;
                    if(_routeLeg%3==0)Memory("route_cycle_"+(_routeLeg/3));
                    if(_routeLeg<9){_stage=4;_next=now+.25f;}else{_app.World.SetFreezeMobs(false);BeginReload();}break;
                case 10:
                    if(now<_next)return;Memory("world_reload_"+_reloads);
                    if(_reloads==PlannedReloads)ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"stress-hud-after-reload.png"));
                    if(_reloads<PlannedReloads)BeginReload();else{_stage=12;_next=now+10f;SetStatus("Стресс: финальная выдержка и проверка удерживаемой памяти");}break;
                case 12:
                    if(now<_next)return;Memory("final_settled");BeginComparison();break;
                case 13:
                    if(now<_next)return;
                    Memory(ComparisonName(_comparisonReloads));_comparisonReloads++;
                    if(_comparisonReloads<PlannedComparisonReloads)BeginComparisonReload();
                    else{_stage=14;_next=now+10f;_app.Convenience.Reset();SetExplorer(false);_app.Player.Reset();_app.Player.Tick();}
                    break;
                case 14:
                    if(now<_next)return;
                    Require(_app.Audit.StressCheckpointFingerprint()==_comparisonCheckpointHash,"Owned checkpoint changed after ABBA comparison.");
                    Memory("control_final_settled");Finish();break;
            }
        }
        catch(Exception e)
        {
            RestoreBenchmarkTiming();
            _app.Optimization.Reset();
            Check(false,e.ToString());Measuring=false;
            try{Move(0);}catch(Exception release){_warnings.Add("Input release failed: "+release.Message);}
            _app.Convenience.StopRefill("stress failed");_stage=-1;Status="FAILED";Write(true);
            UnityEngine.Debug.Log("CreatorToolkit stress end: FAILED");
            ReleaseTestRoots();_app.Fail("stress",e);
        }
    }
    private void Finish()
    {
        RestoreBenchmarkTiming();
        Measuring=false;Move(0);_app.Optimization.Reset();_app.Convenience.Reset();SetExplorer(false);_app.World.Reset();_app.Player.Reset();_app.Player.Tick();
        Check(_app.FailureSerial==_errorsAtStart,"no Toolkit native/runtime errors during stress");
        Check(_reloads==PlannedReloads&&_comparisonReloads==PlannedComparisonReloads&&_routeLeg==9&&_burst==2000,"all planned movement/refill/transit/reload/comparison stages completed");
        var reloads=_memory.Where(x=>x.Name.StartsWith("world_reload_")).ToArray();
        if(reloads.Length==PlannedReloads)
        {
            long managedGrowth=reloads[^1].DotNetRetainedBytes-reloads[0].DotNetRetainedBytes;
            long nativeGrowth=reloads[^1].UnityAllocatedBytes-reloads[0].UnityAllocatedBytes;
            long il2cppGrowth=reloads[^1].Il2CppRetainedBytes-reloads[0].Il2CppRetainedBytes;
            if(managedGrowth>8*1024*1024)_warnings.Add("Retained .NET heap grew over 8 MiB across repeated reloads; investigate, not a zero-leak certificate.");
            if(nativeGrowth>128*1024*1024)_warnings.Add("Live Unity allocations grew over 128 MiB across reloads; distinguish streamed caches from leaks.");
            if(reloads[0].Il2CppRetainedBytes>=0&&il2cppGrowth>8*1024*1024)
                _warnings.Add("Retained IL2CPP managed metric grew over 8 MiB across reloads; owned-object bounds alone cannot distinguish engine caches from all possible leaks.");
        }
        _stage=-1;Status=_failed==0?"PASS":"FAILED";Write(true);
        UnityEngine.Debug.Log("CreatorToolkit stress end: "+Status);
        _app.LoggerInstance.Msg("Stress complete: "+Status+" windows="+_windows.Count+" reloads="+_reloads+" transitions="+_routeLeg);
        ReleaseTestRoots();
    }
    private void ReleaseTestRoots()
    {
        _engine?.Dispose();_engine=null;
        _keyboard=null;for(int i=0;i<_route.Length;i++)_route[i]=null;_oldContexts.Clear();
    }
    internal void Cancel()
    {
        RestoreBenchmarkTiming();
        _app.Optimization.Reset();
        if(!Active){ReleaseTestRoots();return;}
        Measuring=false;
        try{Move(0);}
        finally
        {
            _stage=-1;Status="CANCELLED";Write(true);
            UnityEngine.Debug.Log("CreatorToolkit stress end: CANCELLED");ReleaseTestRoots();
        }
    }
    private void Write(bool final)
    {
        var value=new {Version=ToolkitMod.Version,Status=Status,Final=final,ElapsedSeconds=Time.realtimeSinceStartup-_started,
            Resolution=Screen.width+"x"+Screen.height,VSync=QualitySettings.vSyncCount,TargetFrameRate=Application.targetFrameRate,
            Scope="Owned audit copy only; frame intervals, native W/S at x5, forced-count native refill callbacks, three-region cycles, same-process menu/world reloads. Not OS input, GPU frame-time certification or proof of absence of every leak.",
            FailedChecks=_failed,WorldReloads=_reloads,ComparisonReloads=_comparisonReloads,ComparisonCheckpointSha256=_comparisonCheckpointHash,
            SafeTransitions=_routeLeg,ForcedRefillCycles=_burst,Windows=_windows,Memory=_memory,Stalls=_stalls,
            EngineProfilerSupported=_engineProfilerSupported,FrameTimingFeatureEnabled=_frameTimingFeatureEnabled,EngineRecorderNotes=_engineNotes,EngineHitches=_engineHitches,
            EngineSampleLimit="Last completed native samples may lag the frame interval; recorder availability is reported, not fabricated. Probes are test-only and disposed before memory/reload stages.",
            GraphicsComparisonScope="Two fixed-order 12-second windows on the owned copy: original graphics A, lightweight profile B; both with HUD/automation off, same collision-checked reset anchor, 5-second warmup, temporary targetFrameRate=-1/VSync=0 outside the production module. Original timing is restored before later stages. Single-machine/single-order result, not a 2-4 GiB VRAM or Slime Rancher 1 equivalence claim. Each window records its actual cap, mip limit, render size and profile state.",
            ComparisonScope="ABBA: 3 all-off, 6 HUD/profile/refill/SOS/waypoint/radar/farm-on, 3 all-off; identical owned checkpoint hash, no explicit resave; six-second settle per load. Mod remains loaded in both arms, so this is not a vanilla-only heap attribution.",
            Checks=_checks,Warnings=_warnings};
        File.WriteAllText(JsonPath,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(TextPath,"CreatorToolkit "+ToolkitMod.Version+" stress suite\n"+Status+"\n"+
            "FailedChecks="+_failed+" Windows="+_windows.Count+" Reloads="+_reloads+" ComparisonReloads="+_comparisonReloads+" Transitions="+_routeLeg+" ForcedRefills="+_burst+"\n"+string.Join("\n",_checks)+"\nWarnings: "+string.Join(" | ",_warnings)+"\n");
    }
}
