using CreatorToolkit;
using P = CreatorToolkit.PassiveFramePolicy;

internal static class PassiveFrameBenchmarkRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void Check(bool value,string name)=>check(value,"passive benchmark / "+name);
        void Reject(Action action,string name)
        {
            bool rejected=false;
            try{action();}catch(ArgumentOutOfRangeException){rejected=true;}
            Check(rejected,name);
        }
        static bool Near(double a,double b)=>Math.Abs(a-b)<1e-9;
        Check(P.WarmupSeconds==5&&P.SampleSeconds==30&&P.DeadlineSeconds==45,"fixed observe-only timing contract");
        Check(P.MaxSamples==32768&&P.MaxScenes==256&&P.MinimumSamples==60,"bounded sample and scene contracts");
        var eligible=new P.Eligibility{Ready=true,Gameplay=true,Focused=true,TimeScale=1};
        Check(P.Refusal(eligible,P.Scope.ForegroundGameplay)=="","known running foreground gameplay eligible");
        var refusals=new (string Name,P.Eligibility Value)[]{
            ("not ready",eligible with{Ready=false}),
            ("not gameplay",eligible with{Gameplay=false}),
            ("loading",eligible with{Loading=true}),
            ("native map",eligible with{MapOpen=true}),
            ("native modal",eligible with{Modal=true}),
            ("mod menu",eligible with{MenuOpen=true}),
            ("pending work",eligible with{PendingWork=true}),
            ("other probe",eligible with{OtherProbe=true}),
            ("audit active",eligible with{AuditActive=true}),
            ("audit recovery held",eligible with{AuditHeld=true}),
            ("frozen simulation or automation",eligible with{NonGameplayFeatures=true}),
            ("unknown native UI",eligible with{UiReadError=true}),
            ("paused",eligible with{TimeScale=0}),
            ("accelerated simulation",eligible with{TimeScale=2}),
            ("slow simulation",eligible with{TimeScale=.5}),
            ("NaN simulation",eligible with{TimeScale=double.NaN}),
            ("infinite simulation",eligible with{TimeScale=double.PositiveInfinity})};
        foreach(var row in refusals)
        {
            Check(P.Refusal(row.Value,P.Scope.ForegroundGameplay)!="",row.Name+" refuses foreground");
            Check(P.Refusal(row.Value with{Focused=false,RunInBackground=true},P.Scope.BackgroundDiagnostic)!="",
                row.Name+" also refuses explicit background diagnostic");
        }
        Check(P.Refusal(default,P.Scope.ForegroundGameplay)!="","default unknown eligibility fails closed");
        Check(P.Refusal(eligible with{Focused=false,RunInBackground=true},P.Scope.ForegroundGameplay)!="",
            "background execution does not silently downgrade foreground scope");
        var background=eligible with{Focused=false,RunInBackground=true};
        Check(P.Refusal(background,P.Scope.BackgroundDiagnostic)=="","explicit unfocused background diagnostic eligible");
        Check(P.Refusal(background with{RunInBackground=false},P.Scope.BackgroundDiagnostic)!="",
            "unfocused process without background execution refused");
        Check(P.Refusal(eligible,P.Scope.BackgroundDiagnostic)!="","background scope cannot be mislabeled foreground");
        Check(P.Refusal(eligible,(P.Scope)99)!="","unknown scope rejected");

        var clock=new P.Window(100);
        Check(clock.Phase==P.Phase.Warmup&&double.IsNaN(clock.SampleStarted),"clock begins without fabricated sample boundary");
        Check(clock.Advance(104.999)==P.Step.Warmup,"frame below five seconds remains warmup");
        Check(clock.Advance(105)==P.Step.BeginSample&&clock.SampleStarted==105,"five seconds primes but does not sample transition interval");
        Check(clock.Advance(105.01)==P.Step.Sample,"first following frame is measured");
        Check(clock.Advance(134.999)==P.Step.Sample,"below thirty measured seconds remains sampling");
        Check(clock.Advance(135)==P.Step.SampleAndComplete&&clock.Phase==P.Phase.Complete&&Near(clock.SampleElapsed,30),
            "thirty measured seconds completes once");
        Check(clock.Advance(136)==P.Step.Terminal,"complete window does not accumulate more samples");
        clock.Cancel();Check(clock.Phase==P.Phase.Complete,"cancel does not rewrite a completed result");
        var delayed=new P.Window(0);
        Check(delayed.Advance(14)==P.Step.BeginSample&&delayed.SampleStarted==14,"delayed priming uses actual boundary not theoretical five seconds");
        Check(delayed.Advance(43.999)==P.Step.Sample&&delayed.Advance(44)==P.Step.SampleAndComplete,
            "sample duration measured from actual delayed prime");
        var deadline=new P.Window(0);
        Check(deadline.Advance(20)==P.Step.BeginSample&&deadline.Advance(44.999)==P.Step.Sample,"late prime cannot extend deadline");
        Check(deadline.Advance(45)==P.Step.DeadlineExceeded&&deadline.Phase==P.Phase.Invalid,
            "forty-five-second deadline invalidates unfinished sample");
        Check(deadline.Advance(46)==P.Step.Terminal,"deadline failure is terminal");
        var tied=new P.Window(0);tied.Advance(15);
        Check(tied.Advance(45)==P.Step.DeadlineExceeded,"deadline wins over coincident thirty-second completion");
        var emptyDeadline=new P.Window(0);
        Check(emptyDeadline.Advance(45)==P.Step.DeadlineExceeded,"deadline wins before any sample was primed");
        foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,-.01})
        {
            var bad=new P.Window(0);
            Check(bad.Advance(invalid)==P.Step.InvalidClock&&bad.Phase==P.Phase.Invalid,
                "invalid/backward warmup clock rejected "+invalid);
        }
        var backwards=new P.Window(0);backwards.Advance(5);
        Check(backwards.Advance(4.999)==P.Step.InvalidClock,"backward sampling clock rejected");
        Check(new P.Window(double.NaN).Advance(1)==P.Step.Terminal,"nonfinite constructor cannot start a valid window");
        foreach(bool afterPrime in new[]{false,true})
        {
            var cancelled=new P.Window(0);if(afterPrime)cancelled.Advance(5);cancelled.Cancel();cancelled.Cancel();
            Check(cancelled.Phase==P.Phase.Cancelled&&cancelled.Advance(6)==P.Step.Terminal,
                "cancel is idempotent and terminal "+(afterPrime?"sampling":"warmup"));
        }

        var pose=new P.Pose(1,2,3,0,0,0,1);
        var settings=new P.Settings{Width=1920,Height=1080,TargetFrameRate=-1,VSyncCount=0,Focused=true,
            Mip=2,RequestedMip=2,RenderPercent=100,FoliagePercent=100,ParticlePercent=100,ActualRenderPercent=100,TimeScale=1,GeometryLodBias=1};
        var baseline=new P.Snapshot{Session=101,ActiveScene=2,SceneCount=3,SceneFingerprint=123,CameraId=4,PlayerId=5,MotorId=6,
            Camera=pose,Player=pose,Motor=pose,FieldOfView=60,Aspect=16.0/9,OrthographicSize=5,
            ViewWidth=1,ViewHeight=1,Graphics=settings};
        Check(P.StableReason(baseline,baseline)=="","fully known identical snapshot stable");
        var changes=new (string Name,P.Snapshot Value)[]{
            ("missing session",baseline with{Session=0}),
            ("different session",baseline with{Session=102}),
            ("active scene",baseline with{ActiveScene=3}),
            ("scene count",baseline with{SceneCount=4}),
            ("same-count scene replacement",baseline with{SceneFingerprint=124}),
            ("missing camera",baseline with{CameraId=0}),
            ("camera identity",baseline with{CameraId=40}),
            ("player identity",baseline with{PlayerId=50}),
            ("motor identity",baseline with{MotorId=60}),
            ("camera movement",baseline with{Camera=pose with{X=pose.X+.03}}),
            ("player movement",baseline with{Player=pose with{Y=pose.Y+.03}}),
            ("motor movement",baseline with{Motor=pose with{Z=pose.Z+.03}}),
            ("camera rotation",baseline with{Camera=new P.Pose(1,2,3,0,Math.Sin(Math.PI/180),0,Math.Cos(Math.PI/180))}),
            ("player rotation",baseline with{Player=new P.Pose(1,2,3,0,Math.Sin(Math.PI/180),0,Math.Cos(Math.PI/180))}),
            ("invalid camera position",baseline with{Camera=pose with{X=double.NaN}}),
            ("invalid player position",baseline with{Player=pose with{Y=double.PositiveInfinity}}),
            ("invalid motor orientation",baseline with{Motor=pose with{Qw=0}}),
            ("FOV",baseline with{FieldOfView=61}),
            ("aspect",baseline with{Aspect=1}),
            ("projection mode",baseline with{Orthographic=true}),
            ("orthographic size",baseline with{OrthographicSize=6}),
            ("viewport X",baseline with{ViewX=.1}),
            ("viewport Y",baseline with{ViewY=.1}),
            ("viewport width",baseline with{ViewWidth=.9}),
            ("viewport height",baseline with{ViewHeight=.9}),
            ("unknown FOV",baseline with{FieldOfView=double.NaN}),
            ("unknown actual render",baseline with{Graphics=settings with{ActualRenderPercent=double.NaN}})};
        foreach(var row in changes)Check(P.StableReason(baseline,row.Value)!="",row.Name+" invalidates fixed-view sample");
        Check(P.StableReason(baseline with{SceneCount=0},baseline with{SceneCount=0})!="","empty scene baseline rejected");
        Check(P.StableReason(baseline with{SceneCount=P.MaxScenes+1},baseline with{SceneCount=P.MaxScenes+1})!="",
            "oversized scene baseline rejected");
        Check(P.StableReason(baseline,baseline with{Camera=pose with{X=pose.X+.01}})=="","sub-tolerance camera jitter allowed");
        Check(P.StableReason(baseline,baseline with{Camera=pose with{Qw=-1}})=="","equivalent quaternion sign does not invalidate view");
        Check(P.StableReason(baseline with{Camera=default},baseline with{Camera=default})!="","matching invalid poses never imply stable view");
        Check(P.StableReason(baseline with{Graphics=settings with{Width=0}},baseline with{Graphics=settings with{Width=0}})!="",
            "matching unknown resolution rejected");
        var graphicsChanges=new (string Name,P.Settings Value)[]{
            ("width",settings with{Width=1280}),("height",settings with{Height=720}),
            ("cap",settings with{TargetFrameRate=60}),("VSync",settings with{VSyncCount=1}),
            ("focus",settings with{Focused=false}),("background mode",settings with{RunInBackground=true}),
            ("mip readback",settings with{Mip=3}),("profile active",settings with{OptimizationActive=true}),
            ("mip request",settings with{RequestedMip=3}),("render request",settings with{RenderPercent=70}),
            ("foliage",settings with{FoliagePercent=40}),("particles",settings with{ParticlePercent=50}),
            ("streaming profile",settings with{Smooth=true}),("render readback",settings with{ActualRenderPercent=70}),
            ("render width",settings with{RenderWidth=1280}),("render height",settings with{RenderHeight=720}),
            ("frozen time",settings with{FreezeTime=true}),("frozen mobs",settings with{FreezeMobs=true}),
            ("hidden HUD",settings with{HiddenHud=true}),("custom HUD",settings with{CustomHud=true}),
            ("automation",settings with{Automation=true}),("timescale",settings with{TimeScale=.9999999})};
        foreach(var row in graphicsChanges)
            Check(P.StableReason(baseline,baseline with{Graphics=row.Value})=="settings-changed",row.Name+" setting invalidates window");

        Check(P.StableReason(baseline,baseline with{Graphics=settings with{GeometryLodBias=.75}})=="settings-changed",
            "geometry LOD changed scalar invalidates fixed-view window");
        foreach(double invalid in new[]{0.0,-1.0,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        {
            var unknown=baseline with{Graphics=settings with{GeometryLodBias=invalid}};
            Check(P.StableReason(baseline,unknown)=="invalid-lod-bias",
                "geometry LOD invalid current scalar refuses window "+invalid);
            Check(P.StableReason(unknown,unknown)=="invalid-lod-bias",
                "geometry LOD identical invalid baseline never becomes stable "+invalid);
        }
        Check(P.StableReason(baseline,baseline with{Graphics=settings with{GeometryLodBias=double.Epsilon}})=="settings-changed",
            "geometry LOD tiny positive scalar stays known but changed");

        ulong last=0;
        Check(!P.AcceptNativeTimestamp(0,ref last)&&last==0,"zero GPU timestamp unavailable");
        Check(P.AcceptNativeTimestamp(10,ref last)&&last==10,"first genuine GPU timestamp accepted");
        Check(!P.AcceptNativeTimestamp(10,ref last)&&last==10,"repeated GPU sample not counted twice");
        Check(!P.AcceptNativeTimestamp(9,ref last)&&last==10,"out-of-order GPU sample rejected without rolling cursor back");
        Check(P.AcceptNativeTimestamp(11,ref last)&&last==11,"fresh GPU sample accepted after rejected stale sample");
        foreach(double invalid in new[]{0,-1,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
            Check(!P.ValidDuration(invalid),"invalid frame/GPU duration rejected "+invalid);
        Check(P.ValidDuration(.001)&&P.ValidDuration(2000),"positive tiny and genuine slow frames remain valid");
        double[] values={100,10,30,20,40};
        var original=(double[])values.Clone();var result=P.Summarize(values,5,1);
        Check(result.Count==5&&result.AverageFps==5&&result.ElapsedSeconds==1,"FPS uses count divided by measured wall time");
        Check(result.MedianMs==30&&result.P95Ms==100&&result.P99Ms==100&&result.MaxMs==100,"nearest-rank quantiles retain slow outlier");
        Check(Near(result.IntervalSeconds,.2)&&Near(result.IntervalFps,25)&&Near(result.AlignmentErrorMs,-800),
            "interval throughput and signed alignment error do not masquerade as wall FPS");
        Check(result.Over33Ms==2&&result.Over50Ms==1,"hitch counters include genuine outliers");
        Check(values.SequenceEqual(original),"summary never mutates caller frame buffer");
        var singleton=P.Summarize(new[]{16.0},1,.5);
        Check(singleton.AverageFps==2&&singleton.MedianMs==16&&singleton.P99Ms==16,"singleton quantiles and elapsed FPS valid");
        Check(P.Summarize(new[]{10.0,double.NaN},1,1).Count==1,"unused buffer tail is not a fabricated frame");
        Reject(()=>P.Summarize(null,1,1),"null sample buffer rejected");
        Reject(()=>P.Summarize(Array.Empty<double>(),0,1),"zero frames unavailable rather than zero-cost result");
        Reject(()=>P.Summarize(new[]{10.0},2,1),"count beyond buffer rejected");
        Reject(()=>P.Summarize(new double[P.MaxSamples+1],P.MaxSamples+1,1),"sample bound enforced before summary");
        foreach(double wall in new[]{0,-1,double.NaN,double.PositiveInfinity})
            Reject(()=>P.Summarize(new[]{10.0},1,wall),"invalid elapsed interval rejected "+wall);
        foreach(double duration in new[]{0,-1,double.NaN,double.PositiveInfinity})
            Reject(()=>P.Summarize(new[]{duration},1,1),"invalid observed duration rejected "+duration);

        Reject(()=>P.Summarize(new[]{double.MaxValue,double.MaxValue},2,1),"finite interval sum overflow rejected");
        Reject(()=>P.Summarize(new[]{1.0},1,double.Epsilon),"FPS overflow rejected");
        Reject(()=>P.Summarize(new[]{1.0},1,double.MaxValue),"elapsed-millisecond overflow rejected");
        Reject(()=>P.Summarize(new[]{double.Epsilon},1,1),"underflowed interval seconds rejected");
        Check(P.AcceptNativeTimestamp(ulong.MaxValue,ref last)&&last==ulong.MaxValue,
            "maximum native timestamp accepted without arithmetic overflow");
        Check(!P.AcceptNativeTimestamp(1,ref last)&&last==ulong.MaxValue,
            "native timestamp wrap or reset cannot silently become fresh timing");
        double[] waits={0,0,0,100};var savedWaits=(double[])waits.Clone();
        var wait=P.SummarizeDurations(waits,4,true);
        Check(wait.Count==4&&wait.MeanMs==25&&wait.MedianMs==0&&wait.P95Ms==100&&wait.P99Ms==100&&wait.MaxMs==100,
            "present-wait zeros retained in mean and nearest-rank quantiles");
        Check(waits.SequenceEqual(savedWaits),"native duration summary preserves input buffer");
        var zeroWait=P.SummarizeDurations(new[]{0.0,0.0},2,true);
        Check(zeroWait.Count==2&&zeroWait.MeanMs==0&&zeroWait.MaxMs==0,"all-zero observed wait remains a real zero not unavailable");
        var cpu=P.SummarizeDurations(new[]{5.0,1.0,3.0},3,false);
        Check(cpu.Count==3&&cpu.MeanMs==3&&cpu.MedianMs==3&&cpu.P95Ms==5&&cpu.P99Ms==5,
            "positive CPU/GPU samples use duration statistics not FPS");
        Reject(()=>P.SummarizeDurations(new[]{0.0},1,false),"CPU/GPU zero unavailable rather than a free frame");
        foreach(bool allowZero in new[]{false,true})
        {
            Reject(()=>P.SummarizeDurations(null,1,allowZero),"null native durations rejected "+allowZero);
            Reject(()=>P.SummarizeDurations(Array.Empty<double>(),0,allowZero),"empty native durations unavailable "+allowZero);
            Reject(()=>P.SummarizeDurations(new[]{1.0},2,allowZero),"native duration count beyond buffer rejected "+allowZero);
            Reject(()=>P.SummarizeDurations(new double[P.MaxSamples+1],P.MaxSamples+1,allowZero),"native duration bound enforced "+allowZero);
            foreach(double invalid in new[]{-1,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
                Reject(()=>P.SummarizeDurations(new[]{invalid},1,allowZero),"invalid native duration rejected "+allowZero+" "+invalid);
            Reject(()=>P.SummarizeDurations(new[]{double.MaxValue,double.MaxValue},2,allowZero),"native duration sum overflow rejected "+allowZero);
        }
        // Static adapter guards supplement, not replace, the production-policy executions above.
        AdapterGuards(Check,root);
    }

    private static void AdapterGuards(Action<bool,string> check,string root)
    {
        var policy=File.ReadAllText(Path.Combine(root,"src","creator","PassiveFramePolicy.cs"));
        check(!policy.Contains("using UnityEngine")&&!policy.Contains("using Il2Cpp")&&
            !policy.Contains("System.IO")&&!policy.Contains("File.Write")&&!policy.Contains("QualitySettings"),
            "pure policy has no engine, filesystem or quality dependency");
        var adapter=File.ReadAllText(Path.Combine(root,"src","creator","PassiveFrameBenchmark.cs"));
        bool Has(string pattern)=>System.Text.RegularExpressions.Regex.IsMatch(adapter,pattern);
        check(!Has(@"\b(?:QualitySettings|Application|Screen|Time|Physics|RenderSettings|WorldPopulator)\s*\.\s*\w+\s*(?:=(?!=|>)|[+\-*/]=|\+\+|--)"),
            "observer never assigns quality, caps, background execution, time, physics or world settings");
        check(!Has(@"\.\s*(?:position|rotation|enabled|allowDynamicResolution|maxParticles)\s*=(?!=|>)")&&
            !Has(@"\b(?:UnpauseGame|PauseGame|SetResolution|SetQualityLevel|SetDynamicResScaler|ResizeBuffers|SetActive|PlaceForMap|RequestTeleport|QueueEvent|WriteValueIntoEvent|SendInput|SetForegroundWindow|SetCursorPos|SaveAllNow|SaveGameAndFlush|SaveGame|il2cpp_field_static_set_value)\s*\("),
            "observer has no camera, input, focus, physics, save or world-mutation call sites");
        check(!Has(@"Optimization\s*\.\s*(?:Apply\w*|Reset|Restore)\s*\(")&&
            !Has(@"\b(?:Process\s*\.\s*Start|Hook|BeforeFoliage|UpdateVelocity|set_ACTORS_PER_FRAME|set_NON_ACTORS_PER_FRAME)\s*\(")&&
            !adapter.Contains("NativeAuditExecutionEnabled=>true"),
            "observer cannot optimize, restore, launch processes, add unsafe hooks or bypass audit gate");
        check(Has(@"\bP\.Refusal\s*\(")&&Has(@"\bP\.StableReason\s*\(")&&Has(@"\.Advance\s*\(")&&
            Has(@"\bP\.Summarize\s*\(")&&Has(@"\bP\.SummarizeDurations\s*\("),
            "actual adapter consumes tested eligibility, stability, timing and metric policies");
        check(adapter.Contains("Application.isFocused")&&adapter.Contains("Application.runInBackground")&&
            adapter.Contains("Time.timeScale")&&adapter.Contains("NativeUiPriority.LastReadError")&&
            adapter.Contains("NonGameplayFeatures=")&&adapter.Contains("AuditSaveGuard.Required"),
            "adapter populates focus, simulation, unknown-UI, mutable-feature and audit refusal signals");
        check(adapter.Contains("SceneManager.GetSceneAt(i)")&&adapter.Contains("_sceneHandles[i]!=handle")&&
            adapter.Contains("Camera=Pose(")&&adapter.Contains("Player=Pose(")&&adapter.Contains("Motor=Pose("),
            "adapter validates actual scene handles and camera/player/motor poses rather than only counts");
        int tickStart=adapter.IndexOf("internal void Tick()",StringComparison.Ordinal);
        int lateStart=adapter.IndexOf("internal void ValidateLateState()",StringComparison.Ordinal);
        string tick=tickStart>=0&&lateStart>tickStart?adapter.Substring(tickStart,lateStart-tickStart):"";
        check(tick.Contains("Validate()")&&tick.IndexOf("Validate()",StringComparison.Ordinal)<tick.IndexOf("_window.Advance",StringComparison.Ordinal)&&
            tick.Contains("frame==_lastFrame")&&tick.Contains("frame-sequence-gap")&&tick.Contains("P.ValidDuration(interval)"),
            "each unique frame is eligibility-checked before clock/sample and gaps or invalid intervals invalidate");
        check(tick.Contains("P.Step.BeginSample")&&tick.Contains("_discardNativeBoundary=true")&&
            tick.Contains("P.MaxSamples")&&tick.Contains("P.MinimumSamples"),
            "adapter excludes boundary interval and enforces bounded minimum-count sampling");
        check(adapter.Contains("P.AcceptNativeTimestamp(timing.frameStartTimestamp,ref _nativeTimestamp)")&&
            adapter.Contains("if(collect&&_discardNativeBoundary)")&&
            adapter.Contains("timing.cpuMainThreadPresentWaitTime,true")&&adapter.Contains("timing.gpuFrameTime"),
            "native samples deduplicate and discard warmup boundary while preserving zero wait separately from GPU");
        check(adapter.Contains("if(!_timingEnabled)return")&&adapter.Contains("capture-unavailable:")&&
            adapter.Contains("if(count==0)return null")&&adapter.Contains("null fields mean unavailable, not zero cost"),
            "missing native capability is optional and absent durations remain explicitly unavailable");
        check(adapter.Contains("background-diagnostic-not-gaming")&&adapter.Contains("no optimization improvement claimed")&&
            adapter.Contains("getter overhead")&&adapter.Contains("normal gameplay/autosave may progress independently"),
            "diagnostics distinguish background scope, observer overhead and non-isolated limitations");
        int finishStart=adapter.IndexOf("private void Finish(",StringComparison.Ordinal);
        int releaseStart=adapter.IndexOf("private void Release()",StringComparison.Ordinal);
        string finish=finishStart>=0&&releaseStart>finishStart?adapter.Substring(finishStart,releaseStart-finishStart):"";
        check(finish.Contains("_window?.Cancel()")&&finish.Contains("_timingEnabled=false;_timings=null")&&
            finish.IndexOf("_window?.Cancel()",StringComparison.Ordinal)<finish.IndexOf("Task.Run",StringComparison.Ordinal)&&
            adapter.Contains("internal void Cancel(string reason)")&&adapter.Contains("internal void Invalidate(string reason)"),
            "completion/cancel/error detach native observation before asynchronous report writing");
        int writeStart=adapter.IndexOf("private static WriteOutcome WriteResult",StringComparison.Ordinal);
        int harvestStart=adapter.IndexOf("private void HarvestWriter",StringComparison.Ordinal);
        string writer=writeStart>=0&&harvestStart>writeStart?adapter.Substring(writeStart,harvestStart-writeStart):"";
        check(writer.Length>0&&!writer.Contains("_app")&&!writer.Contains("UnityEngine")&&
            writer.Contains("json.Length>65536")&&adapter.Contains("Task.Run(()=>WriteResult(path,result))")&&
            adapter.Contains("\"passive-benchmark-\"+_runId+\".json\""),
            "report worker receives bounded diagnostic primitives only, not engine wrappers");
        var toolkit=File.ReadAllText(Path.Combine(root,"src","creator","ToolkitMod.cs"));
        check(toolkit.Contains("internal bool BenchmarkPendingWork=>Pending!=null||_jobs.Count>0")&&
            toolkit.Contains("Benchmark = new PassiveFrameBenchmark(this)"),
            "observer is initialized with queued host work exposed to eligibility");
        int updateStart=toolkit.IndexOf("public override void OnUpdate()",StringComparison.Ordinal);
        int lateUpdate=toolkit.IndexOf("public override void OnLateUpdate()",StringComparison.Ordinal);
        string update=updateStart>=0&&lateUpdate>updateStart?toolkit.Substring(updateStart,lateUpdate-updateStart):"";
        int observerTick=update.IndexOf("Benchmark?.Tick()",StringComparison.Ordinal);
        int readyGate=update.IndexOf("if (Ready)",StringComparison.Ordinal);
        check(observerTick>=0&&readyGate>observerTick&&update.Contains("Benchmark?.Invalidate(\"host-update-error\")"),
            "observer ticks outside Ready gate and host update errors invalidate instead of leaving a stale active run");
        check(toolkit.Contains("Input.Enforce(); Benchmark?.ValidateLateState();")&&
            toolkit.Contains("Benchmark?.Invalidate(\"host-late-update-error\")"),
            "late-state validation runs independently of menu visibility and late errors invalidate");
        check(toolkit.Contains("Benchmark?.Cancel(\"scene-unloaded\")")&&
            toolkit.Contains("Benchmark?.Cancel(\"application-quit\")")&&
            toolkit.Contains("Benchmark?.Cancel(\"session-restore-or-unload\")")&&
            toolkit.Contains("Benchmark?.Cancel(\"host-save-and-quit-request\")"),
            "observer lifecycle hooks cancel observation without initiating unload or save themselves");
        check(toolkit.Contains("case \"bench-observe\": Benchmark.Start(false); break;")&&
            toolkit.Contains("case \"bench-background\": Benchmark.Start(true); break;")&&
            toolkit.Contains("case \"bench-cancel\": Benchmark.Cancel(\"explicit-ipc-cancel\"); break;"),
            "only explicit observation/background/cancel commands select the corresponding observer action");
        check(toolkit.Contains("Benchmark.Invalidate(\"other-diagnostic-command\")")&&
            toolkit.Contains("details.Append(Benchmark.Status)"),
            "other diagnostic commands invalidate measurement and status reads cached observer strings");
    }
}
