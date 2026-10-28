using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2CppMonomiPark.SlimeRancher.UI.Framework.Displays;
using P=CreatorToolkit.PassiveFramePolicy;

namespace CreatorToolkit;

// Observe-only, explicit IPC opt-in. No settings/input/camera/world/save writes.
// All Unity/IL2CPP reads are on the main thread; writer receives immutable primitives only.
internal sealed class PassiveFrameBenchmark
{
    private readonly ToolkitMod _app;
    private P.Window _window;
    private P.Scope _scope;
    private P.Snapshot _baseline;
    private readonly int[] _sceneHandles=new int[P.MaxScenes];
    private Samples _samples;
    private Il2CppStructArray<FrameTiming> _timings;
    private bool _timingEnabled,_discardNativeBoundary;
    private ulong _nativeTimestamp;
    private int _graphicsRevision;
    private int _lastFrame,_nativeUnique,_nativeStale,_reportedCount;
    private string _runId="",_startedUtc="",_nativeReason="not-requested",_state="idle",_reason="";
    private string _lastRequest="none",_output="none";
    private double _lastStatusAt;
    private Task<WriteOutcome> _writer;
    internal bool Active=>_window!=null&&(_window.Phase==P.Phase.Warmup||_window.Phase==P.Phase.Sampling);
    internal string Status {get;private set;}="BenchmarkState: idle\nBenchmarkCapability: passive-observe-v1\n";
    internal string LastRun {get;private set;}="none";

    private sealed class Samples
    {
        internal readonly double[] Frames=new double[P.MaxSamples],Cpu=new double[P.MaxSamples],
            Main=new double[P.MaxSamples],Render=new double[P.MaxSamples],Wait=new double[P.MaxSamples],Gpu=new double[P.MaxSamples];
        internal int Count,CpuCount,MainCount,RenderCount,WaitCount,GpuCount;
    }
    // Immutable classes expose public getters for System.Text.Json; no Unity wrappers or mutable arrays.
    private sealed class DurationResult
    {
        public int Count {get;}
        public int Missing {get;}
        public double MeanMs {get;}
        public double MedianMs {get;}
        public double P95Ms {get;}
        public double P99Ms {get;}
        public double MaxMs {get;}
        public DurationResult(int Count, int Missing, double MeanMs, double MedianMs, double P95Ms, double P99Ms, double MaxMs)
        {
            this.Count=Count;
            this.Missing=Missing;
            this.MeanMs=MeanMs;
            this.MedianMs=MedianMs;
            this.P95Ms=P95Ms;
            this.P99Ms=P99Ms;
            this.MaxMs=MaxMs;
        }
    }
    private sealed class NativeResult
    {
        public string Availability {get;}
        public int UniqueSamples {get;}
        public int StaleOrRepeated {get;}
        public DurationResult Cpu {get;}
        public DurationResult Main {get;}
        public DurationResult Render {get;}
        public DurationResult PresentWait {get;}
        public DurationResult Gpu {get;}
        public NativeResult(string Availability, int UniqueSamples, int StaleOrRepeated, DurationResult Cpu, DurationResult Main, DurationResult Render, DurationResult PresentWait, DurationResult Gpu)
        {
            this.Availability=Availability;
            this.UniqueSamples=UniqueSamples;
            this.StaleOrRepeated=StaleOrRepeated;
            this.Cpu=Cpu;
            this.Main=Main;
            this.Render=Render;
            this.PresentWait=PresentWait;
            this.Gpu=Gpu;
        }
    }
    private sealed class ContextResult
    {
        public string RuntimeGraphics {get;set;}
        public long Session {get;}
        public int ActiveScene {get;}
        public int SceneCount {get;}
        public ulong SceneFingerprint {get;}
        public int CameraId {get;}
        public int PlayerId {get;}
        public int MotorId {get;}
        public double CameraX {get;}
        public double CameraY {get;}
        public double CameraZ {get;}
        public double FieldOfView {get;}
        public int Width {get;}
        public int Height {get;}
        public int TargetFrameRate {get;}
        public int VSyncCount {get;}
        public bool Focused {get;}
        public bool RunInBackground {get;}
        public int Mip {get;}
        public bool OptimizationActive {get;}
        public int RequestedMip {get;}
        public int RenderPercent {get;}
        public int FoliagePercent {get;}
        public int ParticlePercent {get;}
        public bool Smooth {get;}
        public double ActualRenderPercent {get;}
        public int ActualRenderWidth {get;}
        public int ActualRenderHeight {get;}
        public bool FreezeTime {get;}
        public bool FreezeMobs {get;}
        public bool HiddenHud {get;}
        public bool CustomHud {get;}
        public bool Automation {get;}
        public double TimeScale {get;}
        public double GeometryLodBias {get;}
        public ContextResult(long Session, int ActiveScene, int SceneCount, ulong SceneFingerprint, int CameraId, int PlayerId, int MotorId, double CameraX, double CameraY, double CameraZ, double FieldOfView, int Width, int Height, int TargetFrameRate, int VSyncCount, bool Focused, bool RunInBackground, int Mip, bool OptimizationActive, int RequestedMip, int RenderPercent, int FoliagePercent, int ParticlePercent, bool Smooth, double ActualRenderPercent, int ActualRenderWidth, int ActualRenderHeight, bool FreezeTime, bool FreezeMobs, bool HiddenHud, bool CustomHud, bool Automation, double TimeScale, double GeometryLodBias)
        {
            this.Session=Session;
            this.ActiveScene=ActiveScene;
            this.SceneCount=SceneCount;
            this.SceneFingerprint=SceneFingerprint;
            this.CameraId=CameraId;
            this.PlayerId=PlayerId;
            this.MotorId=MotorId;
            this.CameraX=CameraX;
            this.CameraY=CameraY;
            this.CameraZ=CameraZ;
            this.FieldOfView=FieldOfView;
            this.Width=Width;
            this.Height=Height;
            this.TargetFrameRate=TargetFrameRate;
            this.VSyncCount=VSyncCount;
            this.Focused=Focused;
            this.RunInBackground=RunInBackground;
            this.Mip=Mip;
            this.OptimizationActive=OptimizationActive;
            this.RequestedMip=RequestedMip;
            this.RenderPercent=RenderPercent;
            this.FoliagePercent=FoliagePercent;
            this.ParticlePercent=ParticlePercent;
            this.Smooth=Smooth;
            this.ActualRenderPercent=ActualRenderPercent;
            this.ActualRenderWidth=ActualRenderWidth;
            this.ActualRenderHeight=ActualRenderHeight;
            this.FreezeTime=FreezeTime;
            this.FreezeMobs=FreezeMobs;
            this.HiddenHud=HiddenHud;
            this.CustomHud=CustomHud;
            this.Automation=Automation;
            this.TimeScale=TimeScale;
            this.GeometryLodBias=GeometryLodBias;
        }
    }
    private sealed class Result
    {
        public string Schema {get;}
        public string RunId {get;}
        public string Scope {get;}
        public string State {get;}
        public string Reason {get;}
        public string StartedUtc {get;}
        public string FinishedUtc {get;}
        public double ElapsedSeconds {get;}
        public double SampleElapsedSeconds {get;}
        public ContextResult Context {get;}
        public P.MetricResult Frames {get;}
        public NativeResult Native {get;}
        public string Quantiles {get;}
        public string Limitation {get;}
        public Result(string Schema, string RunId, string Scope, string State, string Reason, string StartedUtc, string FinishedUtc, double ElapsedSeconds, double SampleElapsedSeconds, ContextResult Context, P.MetricResult Frames, NativeResult Native, string Quantiles, string Limitation)
        {
            this.Schema=Schema;
            this.RunId=RunId;
            this.Scope=Scope;
            this.State=State;
            this.Reason=Reason;
            this.StartedUtc=StartedUtc;
            this.FinishedUtc=FinishedUtc;
            this.ElapsedSeconds=ElapsedSeconds;
            this.SampleElapsedSeconds=SampleElapsedSeconds;
            this.Context=Context;
            this.Frames=Frames;
            this.Native=Native;
            this.Quantiles=Quantiles;
            this.Limitation=Limitation;
        }
    }
    private sealed class WriteOutcome
    {
        public bool Success {get;}
        public string Error {get;}
        public WriteOutcome(bool Success, string Error)
        {
            this.Success=Success;
            this.Error=Error;
        }
    }

    internal PassiveFrameBenchmark(ToolkitMod app)=>_app=app;
    private static double Now()=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
    private static string Short(string text)=>string.IsNullOrEmpty(text)?"unspecified":text.Length<=192?text:text.Substring(0,192);
    private string ScopeName=>_scope==P.Scope.BackgroundDiagnostic?"background-diagnostic-not-gaming":"foreground-gameplay";

    internal void Start(bool background)
    {
        HarvestWriter();
        if(Active)
        {
            _lastRequest="refused:already-active";RefreshStatus();return;
        }
        if(_writer!=null)
        {
            _lastRequest="refused:result-writer-busy";RefreshStatus();return;
        }
        _scope=background?P.Scope.BackgroundDiagnostic:P.Scope.ForegroundGameplay;
        _runId=Guid.NewGuid().ToString("N");_startedUtc=DateTime.UtcNow.ToString("O");
        _window=null;_samples=null;_timings=null;_timingEnabled=false;
        _nativeTimestamp=0;_nativeUnique=_nativeStale=_reportedCount=0;_nativeReason="not-requested";
        _reason="";_state="refused";_output="not-started";_lastRequest="checking";
        try
        {
            _graphicsRevision=_app.Optimization.UiRevision;
            string refusal=P.Refusal(ReadEligibility(),_scope);
            if(refusal.Length==0)refusal=ReadSnapshot(true,out _baseline);
            if(refusal.Length==0)refusal=P.StableReason(_baseline,_baseline);
            if(refusal.Length!=0){Refuse(refusal);return;}
            // Allocation precedes the timed warmup. Buffers are bounded and never grow.
            _samples=new Samples();
            _nativeReason="feature-disabled";
            try
            {
                _timingEnabled=FrameTimingManager.IsFeatureEnabled();
                if(_timingEnabled){_timings=new Il2CppStructArray<FrameTiming>(1);_nativeReason="available-if-valid-samples";}
            }
            catch(Exception e){_timingEnabled=false;_nativeReason="unavailable:"+Short(e.Message);}
            _lastFrame=Time.frameCount;
            _window=new P.Window(Now());_lastStatusAt=0;
            _state="warmup";_lastRequest="accepted";_output="pending-completion";
            RefreshStatus();
        }
        catch(Exception e){Refuse("read-error:"+Short(e.Message));}
    }
    private void Refuse(string reason)
    {
        _reason=Short(reason);_state="refused";_lastRequest="refused:"+_reason;
        _window=null;Release();LastRun=_runId+" refused "+_reason;RefreshStatus();
    }
    private static bool SmokeBusy(string status)=>status!="idle"&&
        !(status?.StartsWith("PASS",StringComparison.Ordinal)==true)&&
        !(status?.StartsWith("FAILED",StringComparison.Ordinal)==true);
    private P.Eligibility ReadEligibility()
    {
        var loader=SystemContext.Instance?.SceneLoader;
        var displayManager=UIDisplayManager._instance;
        bool modal=NativeUiPriority.HasNativeFocus;
        return new P.Eligibility{
            Ready=_app.Ready,Gameplay=loader!=null&&!loader.WasCollected&&loader.IsCurrentSceneGroupGameplay(),
            Loading=loader==null||loader.WasCollected||loader.IsSceneLoadInProgress,
            MapOpen=_app.Map.NativeMapOpen||_app.Map.Busy||_app.Player.TransitHeld,
            Modal=modal,UiReadError=!string.IsNullOrEmpty(NativeUiPriority.LastReadError),
            MenuOpen=_app.MenuOpen,PendingWork=_app.BenchmarkPendingWork||_app.Unlocks.MapPending||
                (displayManager?._pendingDisplays?.Count>0),
            OtherProbe=_app.Stress.Active||_app.UiProbe.Active||_app.MapProbe.Active||
                SmokeBusy(_app.Tests.Status)||_app.Tests.HoldsPlayerController||
                !string.IsNullOrWhiteSpace(_app.SelftestFileRequest),
            AuditActive=_app.Audit.Active,AuditHeld=_app.Audit.MainAutosaveHeld||AuditSaveGuard.Required,
            NonGameplayFeatures=_app.World.FreezeTime||_app.World.FreezeMobs||
                _app.Convenience.Refilling||_app.Farm.Collect||_app.Farm.Feed,
            Focused=Application.isFocused,RunInBackground=Application.runInBackground,TimeScale=Time.timeScale
        };
    }
    private static P.Pose Pose(Vector3 p,Quaternion q)=>new(p.x,p.y,p.z,q.x,q.y,q.z,q.w);
    private string ReadSnapshot(bool capture,out P.Snapshot snapshot)
    {
        snapshot=default;
        int sceneCount=SceneManager.sceneCount;
        if(sceneCount<1||sceneCount>P.MaxScenes)return "scene-budget";
        ulong hash=14695981039346656037UL;
        for(int i=0;i<sceneCount;i++)
        {
            var scene=SceneManager.GetSceneAt(i);
            if(!scene.IsValid()||!scene.isLoaded)return "scene-not-loaded";
            int handle=scene.handle;
            if(capture)_sceneHandles[i]=handle;
            else if(_sceneHandles[i]!=handle)return "scene-handles-changed";
            unchecked{hash=(hash^(uint)handle)*1099511628211UL;}
        }
        // Fresh short-lived wrappers only. No camera/controller/world object retained by this observer.
        var camera=Camera.main;var player=_app.Context?.Player;var motor=_app.Player.Controller?.Motor;
        if(camera==null||camera.WasCollected||!camera.isActiveAndEnabled||player==null||player.WasCollected||
            motor==null||motor.WasCollected)return "camera-player-unavailable";
        var cameraTransform=camera.transform;var playerTransform=player.transform;
        var request=_app.Optimization.Current;var viewport=camera.rect;var size=_app.Optimization.ActualRenderSize;
        snapshot=new P.Snapshot{
            Session=_app.Session.ToInt64(),ActiveScene=SceneManager.GetActiveScene().handle,SceneCount=sceneCount,
            SceneFingerprint=hash,CameraId=camera.GetInstanceID(),PlayerId=player.GetInstanceID(),MotorId=motor.GetInstanceID(),
            Camera=Pose(cameraTransform.position,cameraTransform.rotation),Player=Pose(playerTransform.position,playerTransform.rotation),
            Motor=Pose(motor.TransientPosition,Quaternion.identity),FieldOfView=camera.fieldOfView,Aspect=camera.aspect,
            Orthographic=camera.orthographic,OrthographicSize=camera.orthographicSize,
            ViewX=viewport.x,ViewY=viewport.y,ViewWidth=viewport.width,ViewHeight=viewport.height,
            Graphics=new P.Settings{
                Width=Screen.width,Height=Screen.height,TargetFrameRate=Application.targetFrameRate,
                VSyncCount=QualitySettings.vSyncCount,Focused=Application.isFocused,RunInBackground=Application.runInBackground,
                Mip=QualitySettings.globalTextureMipmapLimit,OptimizationActive=_app.Optimization.Active,
                RequestedMip=request.Mip,RenderPercent=request.RenderPercent,FoliagePercent=request.FoliagePercent,
                ParticlePercent=request.ParticlePercent,Smooth=request.Smooth,
                ActualRenderPercent=_app.Optimization.ActualRenderPercent,RenderWidth=size.x,RenderHeight=size.y,
                FreezeTime=_app.World.FreezeTime,FreezeMobs=_app.World.FreezeMobs,HiddenHud=_app.Cinema.HideHud,
                CustomHud=_app.Convenience.ShowHud,Automation=_app.Convenience.Refilling||_app.Farm.Collect||_app.Farm.Feed,
                TimeScale=Time.timeScale,GeometryLodBias=QualitySettings.lodBias
            }
        };
        return "";
    }
    private string Validate()
    {
        if(_graphicsRevision!=_app.Optimization.UiRevision)return "graphics-profile-changed";
        string reason=P.Refusal(ReadEligibility(),_scope);
        if(reason.Length!=0)return reason;
        reason=ReadSnapshot(false,out var snapshot);
        return reason.Length!=0?reason:P.StableReason(_baseline,snapshot);
    }
    internal void Tick()
    {
        HarvestWriter();
        if(!Active)return;
        try
        {
            string reason=Validate();
            if(reason.Length!=0){Finish("invalid",reason);return;}
            int frame=Time.frameCount;
            if(frame==_lastFrame)return;
            if(unchecked(frame-_lastFrame)!=1){Finish("invalid","frame-sequence-gap");return;}
            _lastFrame=frame;
            double now=Now();var step=_window.Advance(now);
            if(step==P.Step.InvalidClock||step==P.Step.DeadlineExceeded)
            {Finish("invalid",step==P.Step.InvalidClock?"invalid-clock":"deadline");return;}
            if(step==P.Step.Warmup){ReadNative(false);UpdateProgress(now);return;}
            if(step==P.Step.BeginSample)
            {
                // The interval ending on this frame straddles warmup: never count it.
                _state="sampling";ReadNative(false);_discardNativeBoundary=true;RefreshStatus();return;
            }
            if(step!=P.Step.Sample&&step!=P.Step.SampleAndComplete)return;
            double interval=Time.unscaledDeltaTime*1000.0;
            if(!P.ValidDuration(interval)){Finish("invalid","invalid-unscaled-interval");return;}
            if(_samples.Count>=P.MaxSamples){Finish("invalid","frame-buffer-full");return;}
            _samples.Frames[_samples.Count++]=interval;
            ReadNative(true);
            if(step==P.Step.SampleAndComplete)
            {Finish(_samples.Count>=P.MinimumSamples?"complete":"invalid",_samples.Count>=P.MinimumSamples?"":"insufficient-frames");return;}
            UpdateProgress(now);
        }
        catch(Exception e){Finish("invalid","read-error:"+Short(e.Message));}
    }
    // Covers native focus/camera/settings changes occurring after the unconditional pre-Ready Tick.
    internal void ValidateLateState()
    {
        if(!Active)return;
        try{string reason=Validate();if(reason.Length!=0)Finish("invalid",reason);}
        catch(Exception e){Finish("invalid","late-read-error:"+Short(e.Message));}
    }
    private void UpdateProgress(double now){if(now-_lastStatusAt>=.5){_lastStatusAt=now;RefreshStatus();}}
    private void ReadNative(bool collect)
    {
        if(!_timingEnabled)return;
        try
        {
            uint count=FrameTimingManager.GetLatestTimings(1,_timings);
            if(count>0)
            {
                var timing=_timings[0];
                if(P.AcceptNativeTimestamp(timing.frameStartTimestamp,ref _nativeTimestamp))
                {
                    if(collect&&_discardNativeBoundary)_discardNativeBoundary=false;
                    else if(collect)
                    {
                        if(_nativeUnique>=P.MaxSamples)throw new InvalidOperationException("native-buffer-full");
                        _nativeUnique++;
                        Add(_samples.Cpu,ref _samples.CpuCount,timing.cpuFrameTime);
                        Add(_samples.Main,ref _samples.MainCount,timing.cpuMainThreadFrameTime);
                        Add(_samples.Render,ref _samples.RenderCount,timing.cpuRenderThreadFrameTime);
                        Add(_samples.Wait,ref _samples.WaitCount,timing.cpuMainThreadPresentWaitTime,true);
                        Add(_samples.Gpu,ref _samples.GpuCount,timing.gpuFrameTime);
                    }
                }
                else if(collect)_nativeStale++;
            }
            else if(collect)_nativeStale++;
            FrameTimingManager.CaptureFrameTimings();
        }
        catch(Exception e){_timingEnabled=false;_timings=null;_nativeReason="capture-unavailable:"+Short(e.Message);}
    }
    private static void Add(double[] values,ref int count,double value,bool allowZero=false)
    {if(double.IsFinite(value)&&(allowZero?value>=0:value>0)&&count<values.Length)values[count++]=value;}
    internal void Cancel(string reason)
    {
        if(!Active)return;
        Finish("cancelled",Short(reason));
    }
    internal void Invalidate(string reason)
    {
        if(!Active)return;
        Finish("invalid",Short(reason));
    }
    private static DurationResult Duration(double[] values,int count,int unique,bool allowZero=false)
    {
        if(count==0)return null;
        var metric=P.SummarizeDurations(values,count,allowZero);
        return new(count,Math.Max(0,unique-count),metric.MeanMs,
            metric.MedianMs,metric.P95Ms,metric.P99Ms,metric.MaxMs);
    }
    private ContextResult Context()
    {
        var b=_baseline;var g=b.Graphics;
        return new(b.Session,b.ActiveScene,b.SceneCount,b.SceneFingerprint,b.CameraId,b.PlayerId,b.MotorId,
            b.Camera.X,b.Camera.Y,b.Camera.Z,b.FieldOfView,g.Width,g.Height,g.TargetFrameRate,g.VSyncCount,
            g.Focused,g.RunInBackground,g.Mip,g.OptimizationActive,g.RequestedMip,g.RenderPercent,g.FoliagePercent,
            g.ParticlePercent,g.Smooth,g.ActualRenderPercent,g.RenderWidth,g.RenderHeight,
            g.FreezeTime,g.FreezeMobs,g.HiddenHud,g.CustomHud,g.Automation,g.TimeScale,g.GeometryLodBias){RuntimeGraphics=_app.Optimization.Runtime.Diagnostics()+"ParticleMax: "+_app.Optimization.Current.ParticleMax};
    }
    private void Finish(string state,string reason)
    {
        // First detach timing ownership and mark inactive, even if report construction/I/O fails.
        _state=state;_reason=Short(reason.Length==0?"completed-observation-not-optimization-pass":reason);
        _window?.Cancel();_timingEnabled=false;_timings=null;
        LastRun=_runId+" "+_state+" "+ScopeName+" "+_reason;
        var samples=_samples;_samples=null;_reportedCount=samples?.Count??0;
        try
        {
            double seconds=_window?.SampleElapsed??0;
            P.MetricResult frames=samples!=null&&samples.Count>0&&seconds>0?
                P.Summarize(samples.Frames,samples.Count,seconds):null;
            NativeResult native=samples==null?null:new(_nativeUnique==0?"no-unique-samples:"+_nativeReason:_nativeReason,_nativeUnique,_nativeStale,
                Duration(samples.Cpu,samples.CpuCount,_nativeUnique),
                Duration(samples.Main,samples.MainCount,_nativeUnique),
                Duration(samples.Render,samples.RenderCount,_nativeUnique),
                Duration(samples.Wait,samples.WaitCount,_nativeUnique,true),
                Duration(samples.Gpu,samples.GpuCount,_nativeUnique));
            var result=new Result("creator-passive-frame-v1",_runId,ScopeName,_state,_reason,_startedUtc,
                DateTime.UtcNow.ToString("O"),_window?.Elapsed??0,seconds,Context(),frames,native,
                "nearest-rank ceil(p*n)-1; FPS=count/wall; IntervalFps=count/sum(intervals)",
                "Observe only; no optimization improvement claimed. Native timings lag Update; null fields mean unavailable, not zero cost. " +
                "First fresh native timing after sampling boundary discarded. Background diagnostics are not gaming baselines. " +
                "Present-wait accepts finite zero; CPU/GPU zero is unavailable. Per-frame observer getter overhead (twice per frame) is unmeasured; no small-effect gain claims. " +
                "No settings, input, world or save writes; normal gameplay/autosave may progress independently.");
            // Immutable result and local path only enter the static worker; never capture _app or this.
            string path=Path.Combine(_app.DataDirectory,"passive-benchmark-"+_runId+".json");
            _output="writing";
            _writer=Task.Run(()=>WriteResult(path,result));
            if(frames!=null)LastRun+=$" FPS={frames.AverageFps:F2} frames={frames.Count} wall={frames.ElapsedSeconds:F3}s intervalSum={frames.IntervalSeconds:F3}s alignmentMs={frames.AlignmentErrorMs:F3}";
        }
        catch(Exception e){_output="report-error:"+Short(e.Message);}
        finally{RefreshStatus();}
    }
    private void Release(){_timingEnabled=false;_timings=null;_samples=null;}
    private static WriteOutcome WriteResult(string path,Result result)
    {
        string temporary=path+".tmp";
        try
        {
            string json=JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true});
            if(json.Length>65536)return new(false,"result-exceeds-64KiB");
            File.WriteAllText(temporary,json);File.Move(temporary,path,true);
            return new(true,"");
        }
        catch(Exception e)
        {
            try{if(File.Exists(temporary))File.Delete(temporary);}catch{/* best-effort own diagnostic temporary only */}
            return new(false,Short(e.Message));
        }
    }
    private void HarvestWriter()
    {
        if(_writer==null||!_writer.IsCompleted)return;
        try{var result=_writer.GetAwaiter().GetResult();_output=result.Success?"written":"write-error:"+result.Error;}
        catch(Exception e){_output="write-error:"+Short(e.Message);}
        _writer=null;RefreshStatus();
    }
    private void RefreshStatus()
    {
        Status=$"BenchmarkCapability: passive-observe-v1\nBenchmarkRunId: {_runId}\nBenchmarkScope: {ScopeName}\n"+
            $"BenchmarkState: {_state}\nBenchmarkReason: {_reason}\nBenchmarkRequest: {_lastRequest}\n"+
            $"BenchmarkElapsed: {_window?.Elapsed??0:F3}\nBenchmarkSampleElapsed: {_window?.SampleElapsed??0:F3}\n"+
            $"BenchmarkSamples: {_samples?.Count??_reportedCount}\nBenchmarkNative: {_nativeReason}\nBenchmarkOutput: {_output}\n"+
            $"BenchmarkLastRun: {LastRun}\n";
    }
}