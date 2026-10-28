using CreatorToolkit;
using P = CreatorToolkit.GeometryLodPolicy;

internal static class GeometryLodRegression
{
    // Faults are injected at the production policy's IO boundary, never modeled
    // by a test-only lease algorithm. This does not load Unity or touch a save.
    private sealed class Scalar
    {
        internal float Value=2;
        internal int Reads,Writes;
        internal bool FailRead,FailBeforeWrite,FailAfterWrite,IgnoreWrite;
        internal float Read(){Reads++;if(FailRead)throw new IOException("synthetic getter failure");return Value;}
        internal void Write(float value)
        {
            Writes++;
            if(FailBeforeWrite)throw new IOException("synthetic pre-write failure");
            if(!IgnoreWrite)Value=value;
            if(FailAfterWrite)throw new IOException("synthetic post-write failure");
        }
        internal P Policy()=>new P(Read,Write);
    }
    internal static void Run(Action<bool,string> check,string root)
    {
        void Check(bool value,string name)=>check(value,"geometry LOD / "+name);
        void Fails(Action action,string name)
        {
            bool threw=false;try{action();}catch(Exception e) when(e is InvalidOperationException||e is IOException||e is ArgumentOutOfRangeException){threw=true;}
            Check(threw,name);
        }
        Check(P.LowFactor==.75f,"explicit low preset is three quarters");
        foreach(float bad in new[]{0f,-0f,-1f,-float.Epsilon,float.MinValue,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            Fails(()=>P.LowBias(bad),"nonpositive or nonfinite baseline rejected "+bad);
        float previous=0;
        foreach(int bits in new[]{1,2,3,4,7,16,255,65535,0x7fffff,0x800000,0x800001,0x1000000,0x20000000,0x3d000000,0x3e800000,0x3f000000,0x3f800000,0x40000000,0x41000000,0x7f000000,0x7f7fffff})
        {
            float baseline=BitConverter.Int32BitsToSingle(bits),low=P.LowBias(baseline);
            Check(float.IsFinite(low)&&low>0&&low<=baseline,"positive finite no-increase bounds at float bits "+bits);
            Check(low>=previous,"monotone mapping at float bits "+bits);previous=low;
        }
        Check(P.LowBias(float.Epsilon)==float.Epsilon,"positive floor never raises smallest native bias");
        Check(P.LowBias(2)==1.5f&&P.LowBias(1)==.75f,"ordinary scalar mapping exact");
        // Deterministic positive IEEE-754 sample sweep; expected state is always
        // the production object's state rather than a parallel lease model.
        uint seed=0x31415926;
        bool bounded=true;
        for(int i=0;i<4096;i++)
        {
            seed=unchecked(seed*1664525+1013904223);
            int bits=(int)(seed%0x7f7fffff)+1;
            float value=BitConverter.Int32BitsToSingle(bits),low=P.LowBias(value);
            bounded&=float.IsFinite(low)&&low>0&&low<=value;
        }
        Check(bounded,"4096 deterministic finite positive inputs never increase or overflow");
        var s=new Scalar();var p=s.Policy();
        Check(!p.Active&&!p.Pending&&p.Supported&&!p.ReadbackValid&&float.IsNaN(p.EffectiveBias)&&s.Reads==0&&s.Writes==0,
            "constructor is unprobed and never reads or writes");
        p.Observe();p.Restore();p.Diagnostics();
        Check(s.Reads==0&&s.Writes==0&&!p.Active,"inactive observe restore and cached diagnostics are inert");
        p.ApplyLow();
        Check(p.Active&&!p.Pending&&p.ReadbackValid&&p.Supported&&p.EffectiveBias==1.5f&&s.Value==1.5f&&s.Writes==1,
            "explicit apply owns confirmed lowered scalar");
        string status=p.Status;string diagnostic=p.Diagnostics();int reads=s.Reads;
        Check(diagnostic.Contains("GeometryBaseline: 2")&&diagnostic.Contains("GeometryExpected: 1.5")&&
            diagnostic.Contains("GeometryFactor: 0.75")&&s.Reads==reads,"diagnostics expose cached owned values without native reads");
        for(int i=0;i<8;i++)p.ApplyLow();
        Check(s.Value==1.5f&&s.Writes==1&&p.Active&&!p.Pending,"repeated apply never compounds or rewrites");
        p.Observe();Check(s.Writes==1&&!p.Pending,"observe matching expected scalar is read-only");
        p.Restore();Check(s.Value==2&&s.Writes==2&&!p.Active&&!p.Pending,"restore returns exact original not inverse scaling");
        reads=s.Reads;p.Restore();Check(s.Reads==reads&&s.Writes==2,"released restore is idempotent");
        s.Value=4;p.ApplyLow();Check(s.Value==3&&p.Active,"new lease snapshots fresh scalar after prior release");p.Restore();
        Check(s.Value==4&&!p.Active,"new lease restores its own fresh original");
        s=new Scalar{Value=float.Epsilon};p=s.Policy();p.ApplyLow();
        Check(s.Value==float.Epsilon&&s.Writes==0&&!p.Active&&!p.Pending,"floor no-op never claims ownership");
        foreach(float external in new[]{.25f,1.75f,5f})
        {
            s=new Scalar();p=s.Policy();p.ApplyLow();s.Value=external;
            p.Observe();p.Observe();
            Check(p.Active&&p.Pending&&p.Conflicts==1&&s.Value==external&&s.Writes==1,"external edit observed once without mutation "+external);
            Fails(()=>p.ApplyLow(),"conflicted reapply refused "+external);
            Check(s.Value==external&&s.Writes==1,"refusal never overwrites external edit "+external);
            p.Restore();Check(!p.Active&&!p.Pending&&s.Value==external&&s.Writes==1,"restore skips and releases external value "+external);
        }
        s=new Scalar();p=s.Policy();p.ApplyLow();s.Value=3;
        Fails(()=>p.ApplyLow(),"reapply itself detects unobserved external edit");
        Check(p.Active&&p.Pending&&p.Conflicts==1&&s.Writes==1&&s.Value==3,"unobserved conflict is not adopted or overwritten");p.Restore();
        s=new Scalar();p=s.Policy();p.ApplyLow();s.Value=2;p.Restore();
        Check(!p.Active&&s.Writes==1&&s.Value==2,"externally restored original releases without writing");
        foreach(float bad in new[]{0f,-1f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
        {
            s=new Scalar{Value=bad};p=s.Policy();
            Fails(()=>p.ApplyLow(),"invalid initial native read refused "+bad);
            Check(!p.Active&&!p.Pending&&!p.Supported&&!p.ReadbackValid&&s.Writes==0,"invalid initial read does not claim or mutate "+bad);
            s.Value=2;p.ApplyLow();Check(p.Active&&p.Supported&&p.ReadbackValid&&s.Value==1.5f,"valid explicit retry recovers read capability "+bad);p.Restore();
        }
        s=new Scalar{FailRead=true};p=s.Policy();Fails(()=>p.ApplyLow(),"initial getter exception surfaces");
        Check(!p.Active&&!p.Pending&&!p.Supported&&s.Writes==0,"initial getter failure creates no fictional lease");
        foreach(bool afterWrite in new[]{false,true})
        {
            s=new Scalar{FailBeforeWrite=!afterWrite,FailAfterWrite=afterWrite};p=s.Policy();
            Fails(()=>p.ApplyLow(),"apply setter failure surfaces "+afterWrite);
            Check(p.Active&&p.Pending&&!p.ReadbackValid&&s.Writes==1,"ownership preclaimed before uncertain setter "+afterWrite);
            Check(s.Value==(afterWrite?1.5f:2f),"fault injection distinguishes pre/post mutation "+afterWrite);
            Fails(()=>p.ApplyLow(),"uncertain reapply blocked "+afterWrite);
            s.FailBeforeWrite=s.FailAfterWrite=false;p.Restore();
            Check(!p.Active&&!p.Pending&&s.Value==2&&s.Writes==(afterWrite?2:1),"explicit conditional cleanup after apply setter failure "+afterWrite);
        }
        s=new Scalar{IgnoreWrite=true};p=s.Policy();Fails(()=>p.ApplyLow(),"ignored apply setter fails exact readback");
        Check(p.Active&&p.Pending&&p.Conflicts==1&&s.Value==2,"unconfirmed apply retains lease");p.Restore();
        Check(!p.Active&&!p.Pending&&s.Writes==1,"baseline already present releases failed apply without extra write");
        // Setter mutates then sabotages just the readback; use the production
        // policy directly with delegates to hit precisely this transaction edge.
        s=new Scalar();var captured=s;p=new P(s.Read,v=>{captured.Write(v);captured.FailRead=true;});
        Fails(()=>p.ApplyLow(),"post-apply getter failure surfaces");
        Check(p.Active&&p.Pending&&!p.ReadbackValid&&s.Value==1.5f,"post-apply getter failure retains original and expected");
        s.FailRead=false; // restore write will deliberately make readback fail again
        Fails(()=>p.Restore(),"restore post-write getter failure surfaces");
        Check(p.Active&&p.Pending&&s.Value==2,"restore getter failure retains lease despite successful write");
        s.FailRead=false;p.Restore();Check(!p.Active&&!p.Pending&&s.Writes==2,"retry sees baseline and releases without duplicate write");
        foreach(bool afterWrite in new[]{false,true})
        {
            s=new Scalar();p=s.Policy();p.ApplyLow();s.FailBeforeWrite=!afterWrite;s.FailAfterWrite=afterWrite;
            Fails(()=>p.Restore(),"restore setter failure surfaces "+afterWrite);
            Check(p.Active&&p.Pending&&!p.ReadbackValid,"restore setter failure retains retry lease "+afterWrite);
            s.FailBeforeWrite=s.FailAfterWrite=false;p.Restore();
            Check(!p.Active&&!p.Pending&&s.Value==2&&s.Writes==(afterWrite?2:3),"restore failure retry conditionally resolves "+afterWrite);
        }
        s=new Scalar();p=s.Policy();p.ApplyLow();s.FailRead=true;p.Observe();
        Check(p.Active&&p.Pending&&!p.Supported&&!p.ReadbackValid&&s.Writes==1,"observe getter failure retains ownership and never writes");
        Fails(()=>p.Restore(),"restore getter failure surfaces");Check(p.Active&&p.Pending&&s.Writes==1,"restore cannot write with unreadable current scalar");
        s.FailRead=false;p.Restore();Check(!p.Active&&!p.Pending&&s.Value==2,"explicit cleanup retries after getter recovery");
        s=new Scalar();p=s.Policy();p.ApplyLow();s.IgnoreWrite=true;Fails(()=>p.Restore(),"ignored restore setter fails exact readback");
        Check(p.Active&&p.Pending&&p.Conflicts==1&&s.Value==1.5f,"unconfirmed restore preserves retry state");
        s.IgnoreWrite=false;p.Restore();Check(!p.Active&&!p.Pending&&s.Value==2,"unconfirmed restore can be retried explicitly");
        foreach(float bad in new[]{0f,float.NaN,float.PositiveInfinity})
        {
            s=new Scalar();p=s.Policy();p.ApplyLow();s.Value=bad;p.Observe();
            Check(p.Active&&p.Pending&&!p.ReadbackValid&&s.Writes==1,"invalid observe retains lease without write "+bad);
            Fails(()=>p.Restore(),"invalid current native scalar blocks restore "+bad);
            s.Value=1.5f;p.Restore();Check(!p.Active&&s.Value==2,"restore recovers after invalid scalar becomes readable "+bad);
        }
        s=new Scalar();p=s.Policy();p.ApplyLow();s.Value=3;p.Observe();p.Restore();s.Value=4;p.ApplyLow();s.Value=5;p.Observe();
        Check(p.Conflicts==2&&p.Pending&&s.Writes==2,"conflict counter increments once per independent lease");p.Restore();
        // These source guards supplement the executable pure-policy checks;
        // they do not simulate the Unity adapter or establish runtime safety.
        var tools=File.ReadAllText(Path.Combine(root,"src","creator","OptimizationTools.cs"));
        Check(tools.Contains("new GeometryLodPolicy(()=>QualitySettings.lodBias,v=>QualitySettings.lodBias=v)")&&
            tools.Contains("_geometry.ApplyLow();")&&tools.Contains("_geometry.Restore();"),
            "actual runtime adapter consumes tested production scalar lease");
        Check(tools.Contains("internal bool Active=>_snapshot||GeometryActive||DistanceActive||MaximumActive||Runtime.Active||BenchmarkCapActive;")&&
            tools.Contains("internal bool GeometryPending=>_geometry.Pending;"),
            "independent geometry lease is visible even without ordinary graphics snapshot");
        int restore=tools.IndexOf("internal void Restore(bool notify=true)",StringComparison.Ordinal);
        string cleanup=restore>=0?tools.Substring(restore):"";
        Check(cleanup.Contains("if(!_snapshot&&!GeometryActive&&!DistanceActive&&!MaximumActive&&!Runtime.Active&&!BenchmarkCapActive)return;")&&
            cleanup.Contains("Attempt(\"geometry\",()=>RestoreGeometry(false));")&&
            cleanup.IndexOf("Attempt(\"geometry\"",StringComparison.Ordinal)<cleanup.IndexOf("if(!_snapshot)",StringComparison.Ordinal)&&
            !cleanup.Contains("_geometry=new")&&!cleanup.Contains("_geometry=null"),
            "ordinary cleanup attempts geometry first without discarding failed independent lease");
        Check(tools.Contains("_geometry.Observe();")&&
            tools.Contains("Apply(OptimizationRequest.Potato with{ParticleMax=256});\n            ApplyGeometryLow();")&&tools.Contains("catch{Restore(false);throw;}"),
            "callback only observes geometry; explicit Potato apply includes LOD and rollback");
        var ui=File.ReadAllText(Path.Combine(root,"src","creator","OptimizationUi.cs"));
        Check(ui.Contains("_app.Queue(\"detailed graphics\"")&&ui.Contains("_app.Optimization.ApplyDetailed(details);")&&
            !ui.Contains("QualitySettings.lodBias"),"UI queues explicit geometry opt-in and reads no native LOD scalar");
        var policy=File.ReadAllText(Path.Combine(root,"src","creator","GeometryLodPolicy.cs"));
        Check(!policy.Contains("using UnityEngine")&&!policy.Contains("Il2Cpp")&&!policy.Contains("System.IO")&&
            !policy.Contains("Task.")&&!policy.Contains("Process.")&&!policy.Contains("File."),"tested production policy remains scalar-only with no engine filesystem or process dependency");
    }
}