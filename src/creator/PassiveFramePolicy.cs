using System;

namespace CreatorToolkit;

// Pure policy: no Unity, IL2CPP, files, input, settings writes or save APIs.
internal static class PassiveFramePolicy
{
    internal const double WarmupSeconds=5, SampleSeconds=30, DeadlineSeconds=45;
    internal const int MaxSamples=32768, MaxScenes=256, MinimumSamples=60;
    internal const double PositionTolerance=.02, AngleToleranceDegrees=.1;
    internal enum Scope { ForegroundGameplay, BackgroundDiagnostic }
    internal enum Phase { Warmup, Sampling, Complete, Invalid, Cancelled }
    internal enum Step { Warmup, BeginSample, Sample, SampleAndComplete, InvalidClock, DeadlineExceeded, Terminal }

    internal readonly record struct Eligibility
    {
        internal bool Ready {get;init;}
        internal bool Gameplay {get;init;}
        internal bool Loading {get;init;}
        internal bool MapOpen {get;init;}
        internal bool Modal {get;init;}
        internal bool MenuOpen {get;init;}
        internal bool PendingWork {get;init;}
        internal bool OtherProbe {get;init;}
        internal bool AuditActive {get;init;}
        internal bool AuditHeld {get;init;}
        internal bool Focused {get;init;}
        internal bool RunInBackground {get;init;}
        internal bool UiReadError {get;init;}
        internal bool NonGameplayFeatures {get;init;}
        internal double TimeScale {get;init;}
    }
    internal static string Refusal(Eligibility e,Scope scope)
    {
        if(!e.Ready)return "not-ready";
        if(!e.Gameplay)return "not-gameplay";
        if(e.Loading)return "scene-loading";
        if(e.MapOpen)return "native-map-open";
        if(e.UiReadError)return "native-ui-read-error";
        if(e.Modal)return "native-modal";
        if(e.MenuOpen)return "mod-menu-open";
        if(e.PendingWork)return "pending-work";
        if(e.OtherProbe)return "another-probe";
        if(e.AuditActive||e.AuditHeld)return "audit-or-recovery-active";
        if(e.NonGameplayFeatures)return "freeze-or-mutable-automation-active";
        if(!double.IsFinite(e.TimeScale)||Math.Abs(e.TimeScale-1)>.000001)return "not-normal-timescale";
        if(scope==Scope.ForegroundGameplay)return e.Focused?"":"foreground-focus-required";
        if(scope!=Scope.BackgroundDiagnostic)return "unknown-scope";
        if(e.Focused)return "background-scope-requires-unfocused";
        return e.RunInBackground?"":"background-execution-disabled";
    }

    internal readonly record struct Pose(double X,double Y,double Z,double Qx,double Qy,double Qz,double Qw);
    internal readonly record struct Settings
    {
        internal int Width {get;init;}
        internal int Height {get;init;}
        internal int TargetFrameRate {get;init;}
        internal int VSyncCount {get;init;}
        internal bool Focused {get;init;}
        internal bool RunInBackground {get;init;}
        internal int Mip {get;init;}
        internal bool OptimizationActive {get;init;}
        internal int RequestedMip {get;init;}
        internal int RenderPercent {get;init;}
        internal int FoliagePercent {get;init;}
        internal int ParticlePercent {get;init;}
        internal bool Smooth {get;init;}
        internal double ActualRenderPercent {get;init;}
        internal int RenderWidth {get;init;}
        internal int RenderHeight {get;init;}
        internal bool FreezeTime {get;init;}
        internal bool FreezeMobs {get;init;}
        internal bool HiddenHud {get;init;}
        internal bool CustomHud {get;init;}
        internal bool Automation {get;init;}
        internal double TimeScale {get;init;}
        internal double GeometryLodBias {get;init;}
    }
    internal readonly record struct Snapshot
    {
        internal long Session {get;init;}
        internal int ActiveScene {get;init;}
        internal int SceneCount {get;init;}
        internal ulong SceneFingerprint {get;init;}
        internal int CameraId {get;init;}
        internal int PlayerId {get;init;}
        internal int MotorId {get;init;}
        internal Pose Camera {get;init;}
        internal Pose Player {get;init;}
        internal Pose Motor {get;init;}
        internal double FieldOfView {get;init;}
        internal double Aspect {get;init;}
        internal bool Orthographic {get;init;}
        internal double OrthographicSize {get;init;}
        internal double ViewX {get;init;}
        internal double ViewY {get;init;}
        internal double ViewWidth {get;init;}
        internal double ViewHeight {get;init;}
        internal Settings Graphics {get;init;}
    }
    private static bool Valid(Pose p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)&&
        double.IsFinite(p.Qx)&&double.IsFinite(p.Qy)&&double.IsFinite(p.Qz)&&double.IsFinite(p.Qw)&&
        p.Qx*p.Qx+p.Qy*p.Qy+p.Qz*p.Qz+p.Qw*p.Qw>.5;
    private static bool PositionStable(Pose a,Pose b)
    {
        double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;
        return x*x+y*y+z*z<=PositionTolerance*PositionTolerance;
    }
    private static bool RotationStable(Pose a,Pose b)
    {
        double dot=a.Qx*b.Qx+a.Qy*b.Qy+a.Qz*b.Qz+a.Qw*b.Qw;
        double aa=a.Qx*a.Qx+a.Qy*a.Qy+a.Qz*a.Qz+a.Qw*a.Qw;
        double bb=b.Qx*b.Qx+b.Qy*b.Qy+b.Qz*b.Qz+b.Qw*b.Qw;
        double cosine=Math.Min(1,Math.Abs(dot)/Math.Sqrt(aa*bb));
        return double.IsFinite(cosine)&&2*Math.Acos(cosine)*180/Math.PI<=AngleToleranceDegrees;
    }
    internal static string StableReason(Snapshot a,Snapshot b)
    {
        if(a.Session==0||b.Session==0||a.Session!=b.Session)return "session-changed";
        if(a.SceneCount<1||a.SceneCount>MaxScenes||b.SceneCount!=a.SceneCount||
            a.ActiveScene!=b.ActiveScene||a.SceneFingerprint!=b.SceneFingerprint)return "scene-handles-changed";
        if(a.CameraId==0||a.CameraId!=b.CameraId)return "camera-changed";
        if(a.PlayerId==0||a.PlayerId!=b.PlayerId||a.MotorId==0||a.MotorId!=b.MotorId)return "player-identity-changed";
        if(!Valid(a.Camera)||!Valid(b.Camera)||!Valid(a.Player)||!Valid(b.Player)||!Valid(a.Motor)||!Valid(b.Motor))
            return "invalid-pose";
        if(!PositionStable(a.Camera,b.Camera)||!RotationStable(a.Camera,b.Camera))return "camera-pose-changed";
        if(!PositionStable(a.Player,b.Player)||!RotationStable(a.Player,b.Player)||!PositionStable(a.Motor,b.Motor))
            return "player-pose-changed";
        if(!double.IsFinite(a.FieldOfView)||!double.IsFinite(b.FieldOfView)||a.FieldOfView<=0||
            !double.IsFinite(a.Aspect)||!double.IsFinite(b.Aspect)||a.Aspect<=0||
            !double.IsFinite(a.OrthographicSize)||!double.IsFinite(b.OrthographicSize)||
            !double.IsFinite(a.ViewX)||!double.IsFinite(b.ViewX)||!double.IsFinite(a.ViewY)||!double.IsFinite(b.ViewY)||
            !double.IsFinite(a.ViewWidth)||!double.IsFinite(b.ViewWidth)||a.ViewWidth<=0||
            !double.IsFinite(a.ViewHeight)||!double.IsFinite(b.ViewHeight)||a.ViewHeight<=0||
            a.FieldOfView!=b.FieldOfView||a.Aspect!=b.Aspect||a.Orthographic!=b.Orthographic||
            a.OrthographicSize!=b.OrthographicSize||a.ViewX!=b.ViewX||a.ViewY!=b.ViewY||
            a.ViewWidth!=b.ViewWidth||a.ViewHeight!=b.ViewHeight)return "camera-projection-changed";
        if(!double.IsFinite(a.Graphics.GeometryLodBias)||a.Graphics.GeometryLodBias<=0||
            !double.IsFinite(b.Graphics.GeometryLodBias)||b.Graphics.GeometryLodBias<=0)return "invalid-lod-bias";
        if(a.Graphics.Width<=0||a.Graphics.Height<=0||!double.IsFinite(a.Graphics.ActualRenderPercent)||
            !double.IsFinite(b.Graphics.ActualRenderPercent)||a.Graphics!=b.Graphics)return "settings-changed";
        return "";
    }

    internal sealed class Window
    {
        private readonly double _started;
        private double _previous;
        internal Phase Phase {get;private set;}=Phase.Warmup;
        internal double SampleStarted {get;private set;}=double.NaN;
        internal double Elapsed {get;private set;}
        internal double SampleElapsed=>double.IsFinite(SampleStarted)?_previous-SampleStarted:0;
        internal Window(double now)
        {
            _started=_previous=now;
            if(!double.IsFinite(now))Phase=Phase.Invalid;
        }
        internal Step Advance(double now)
        {
            if(Phase is Phase.Complete or Phase.Invalid or Phase.Cancelled)return Step.Terminal;
            if(!double.IsFinite(now)||now<_previous){Phase=Phase.Invalid;return Step.InvalidClock;}
            Elapsed=now-_started;_previous=now;
            if(Elapsed>=DeadlineSeconds){Phase=Phase.Invalid;return Step.DeadlineExceeded;}
            if(Phase==Phase.Warmup)
            {
                if(Elapsed<WarmupSeconds)return Step.Warmup;
                SampleStarted=now;Phase=Phase.Sampling;return Step.BeginSample;
            }
            if(now-SampleStarted>=SampleSeconds){Phase=Phase.Complete;return Step.SampleAndComplete;}
            return Step.Sample;
        }
        internal void Cancel(){if(Phase is Phase.Warmup or Phase.Sampling)Phase=Phase.Cancelled;}
    }
    internal static bool AcceptNativeTimestamp(ulong candidate,ref ulong last)
    {
        if(candidate==0||candidate<=last)return false;
        last=candidate;return true;
    }
    internal static bool ValidDuration(double milliseconds)=>double.IsFinite(milliseconds)&&milliseconds>0;
    internal sealed class DurationMetrics
    {
        public int Count {get;}
        public double MeanMs {get;}
        public double MedianMs {get;}
        public double P95Ms {get;}
        public double P99Ms {get;}
        public double MaxMs {get;}
        public DurationMetrics(int Count, double MeanMs, double MedianMs, double P95Ms, double P99Ms, double MaxMs)
        {
            this.Count=Count;
            this.MeanMs=MeanMs;
            this.MedianMs=MedianMs;
            this.P95Ms=P95Ms;
            this.P99Ms=P99Ms;
            this.MaxMs=MaxMs;
        }
    }
    // CPU/GPU zeros are unavailable; a finite present-wait zero is a valid observed zero.
    internal static DurationMetrics SummarizeDurations(double[] values,int count,bool allowZero)
    {
        if(values==null||count<1||count>values.Length||count>MaxSamples)throw new ArgumentOutOfRangeException(nameof(count));
        var sorted=new double[count];double sum=0;
        for(int i=0;i<count;i++)
        {
            double v=values[i];
            if(!double.IsFinite(v)||(allowZero?v<0:v<=0))throw new ArgumentOutOfRangeException(nameof(values));
            sorted[i]=v;sum+=v;
            if(!double.IsFinite(sum))throw new ArgumentOutOfRangeException(nameof(values));
        }
        Array.Sort(sorted);
        double Quantile(double p)=>sorted[Math.Clamp((int)Math.Ceiling(p*count)-1,0,count-1)];
        return new(count,sum/count,Quantile(.5),Quantile(.95),Quantile(.99),sorted[count-1]);
    }
    internal sealed class MetricResult
    {
        public int Count {get;}
        public double ElapsedSeconds {get;}
        public double IntervalSeconds {get;}
        public double AverageFps {get;}
        public double IntervalFps {get;}
        public double AlignmentErrorMs {get;}
        public double MedianMs {get;}
        public double P95Ms {get;}
        public double P99Ms {get;}
        public double MaxMs {get;}
        public int Over33Ms {get;}
        public int Over50Ms {get;}
        public MetricResult(int Count, double ElapsedSeconds, double IntervalSeconds, double AverageFps, double IntervalFps, double AlignmentErrorMs, double MedianMs, double P95Ms, double P99Ms, double MaxMs, int Over33Ms, int Over50Ms)
        {
            this.Count=Count;
            this.ElapsedSeconds=ElapsedSeconds;
            this.IntervalSeconds=IntervalSeconds;
            this.AverageFps=AverageFps;
            this.IntervalFps=IntervalFps;
            this.AlignmentErrorMs=AlignmentErrorMs;
            this.MedianMs=MedianMs;
            this.P95Ms=P95Ms;
            this.P99Ms=P99Ms;
            this.MaxMs=MaxMs;
            this.Over33Ms=Over33Ms;
            this.Over50Ms=Over50Ms;
        }
    }
    // Non-mutating nearest-rank quantiles: sorted[ceil(p*n)-1]. Empty input is unavailable to the caller.
    internal static MetricResult Summarize(double[] values,int count,double wallSeconds)
    {
        if(values==null||count<1||count>values.Length||count>MaxSamples||!double.IsFinite(wallSeconds)||wallSeconds<=0)
            throw new ArgumentOutOfRangeException(nameof(count),"Positive bounded samples and elapsed time required.");
        var sorted=new double[count];double sum=0;int over33=0,over50=0;
        for(int i=0;i<count;i++)
        {
            double v=values[i];if(!ValidDuration(v))throw new ArgumentOutOfRangeException(nameof(values));
            sorted[i]=v;sum+=v;
            if(!double.IsFinite(sum))throw new ArgumentOutOfRangeException(nameof(values),"Interval sum overflow.");
            if(v>33.3333333333333)over33++;if(v>50)over50++;
        }
        if(sum/1000<=0||!double.IsFinite(count/wallSeconds)||!double.IsFinite(count/(sum/1000))||
            !double.IsFinite(wallSeconds*1000)||!double.IsFinite(sum-wallSeconds*1000))
            throw new ArgumentOutOfRangeException(nameof(wallSeconds),"Non-finite metric result.");
        Array.Sort(sorted);
        double Quantile(double p)=>sorted[Math.Clamp((int)Math.Ceiling(p*count)-1,0,count-1)];
        return new(count,wallSeconds,sum/1000,count/wallSeconds,count/(sum/1000),sum-wallSeconds*1000,
            Quantile(.5),Quantile(.95),Quantile(.99),sorted[count-1],over33,over50);
    }
}