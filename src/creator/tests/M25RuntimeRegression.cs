using System;
using System.IO;
namespace CreatorToolkit;
internal static class M25RuntimeRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool ok,string name)=>check(ok,"m25 / runtime FPS / "+name);
        C(!M25RuntimeRequest.Full.Any,"full has no lease");
        C(!M25RuntimeRequest.Balanced.Sleep&&!M25RuntimeRequest.Maximum.Sleep,"physics always opt-in, not enabled by graphics presets");
        var r=new M25RuntimeRequest(1,(M25ShadowMode)99,9999,1,true).Clamped();
        C(r.ShadowDistance==10&&r.Shadows==M25ShadowMode.Game&&r.DetailDistance==500&&r.Raycasts==16,"bounds and invalid enum canonicalized");
        foreach(int n in new[]{-5,0,1,16,256,99999})
        {
            int cap=M25RuntimePolicy.ParticleMax(n);C(cap==0||cap>=16&&cap<=4096,"particle absolute cap bounds "+n);
            var a=(OptimizationRequest.Full with{ParticleMax=n}).Clamped();C(a.ParticleMax==cap,"absolute cap survives clamp "+n);
            foreach(float b in new[]{0f,1f,20f,500f,5000f})C(M25RuntimePolicy.Distance(b,n)<=b,"detail range never raises original "+n+" / "+b);
        }
        foreach(float bad in new[]{float.NaN,float.PositiveInfinity,-1}){bool fail=false;try{M25RuntimePolicy.Distance(bad,40);}catch(ArgumentOutOfRangeException){fail=true;}C(fail,"invalid baseline rejected");}
        C(M25RuntimePolicy.MaySleep(false,2500,0,0),"distant offscreen resting slime eligible");
        C(!M25RuntimePolicy.MaySleep(true,2500,0,0)&&!M25RuntimePolicy.MaySleep(false,1600,0,0)&&!M25RuntimePolicy.MaySleep(false,2500,1,0)&&!M25RuntimePolicy.MaySleep(false,2500,0,1)&&!M25RuntimePolicy.MaySleep(false,float.NaN,0,0),"visible nearby moving rotating invalid bodies excluded");
        int value=100,writes=0;var lease=new M25ScalarLease<int>(()=>value,v=>{value=v;writes++;},25);lease.Apply();lease.Restore();
        C(value==100&&writes==2&&!lease.Owned,"ordinary apply/readback/restore");
        lease=new M25ScalarLease<int>(()=>value,v=>value=v,25);lease.Apply();value=77;lease.Restore();C(value==77&&lease.Conflict,"external edits win");
        bool throws=true;value=100;lease=new M25ScalarLease<int>(()=>value,v=>{value=v;if(v==100&&throws)throw new Exception();},25);lease.Apply();try{lease.Restore();}catch{}C(lease.Owned,"failed restore retains lease");throws=false;lease.Restore();C(!lease.Owned&&value==100,"restore retry completes");
        value=100;bool reject=true;lease=new M25ScalarLease<int>(()=>value,v=>{if(v!=100||!reject)value=v;},25);lease.Apply();try{lease.Restore();}catch{}C(lease.Owned&&value==25,"silent restore rejection retains lease");reject=false;lease.Restore();C(!lease.Owned&&value==100,"restore readback confirmed");
        C(M25RuntimePolicy.BoundsDistanceSquared(0,0,0,10,0,0,2,2,2)==64,"AABB nearest distance, not batch-center culling");
        C(M25RuntimePolicy.BoundsDistanceSquared(10,0,0,10,0,0,2,2,2)==0,"camera inside batch always retained");
        C(float.IsNaN(M25RuntimePolicy.BoundsDistanceSquared(0,0,0,0,0,0,-1,2,2))&&float.IsNaN(M25RuntimePolicy.BoundsDistanceSquared(float.NaN,0,0,0,0,0,2,2,2)),"invalid batch bounds fail open");
        C(!M25RuntimePolicy.CullDecor(3600,60,false)&&!M25RuntimePolicy.CullDecor(4900,60,false)&&M25RuntimePolicy.CullDecor(5000,60,false),"10 metre hide hysteresis");
        C(M25RuntimePolicy.CullDecor(3700,60,true)&&!M25RuntimePolicy.CullDecor(3600,60,true)&&!M25RuntimePolicy.CullDecor(float.NaN,60,true),"restore at requested distance or invalid bounds");
        string Read(string f)=>File.ReadAllText(Path.Combine(root,"src","creator",f));
        var tools=Read("M25RuntimeTools.cs");
        C(!tools.Contains("isKinematic=")&&!tools.Contains("constraints=")&&!tools.Contains("Physics.autoSimulation")&&!tools.Contains("gameObject.SetActive(false)")&&!tools.Contains("currentPlatformRenderPipelineSettings"),"no invasive physics or boxed pipeline writes");
        C(tools.Contains("volume.profile")&&tools.Contains("HasInstantiatedProfile")&&!tools.Contains("sharedProfile="),"shared HDRP assets never mutated");
        C(tools.Contains("WakeAll();_bodies.Clear()")&&tools.Contains("budget=Math.Min(64")&&tools.Contains("_bodies.Count>=1024"),"sleep releases and has bounded actor work");
        C(Read("OptimizationTools.cs").Contains("Attempt(\"runtime\",Runtime.Restore)")&&Read("PassiveFrameBenchmark.cs").Contains("graphics-profile-changed"),"reset/quit restoration and benchmark profile guard");
        C(Read("OptimizationUi.cs").Contains("GraphicsRuntime(area)")&&Read("M25RuntimeUi.cs").Contains("Slider(79"),"fourth tab and all runtime controls exposed");
        C(tools.Contains("lease.Restore();_leases.Remove(key);_unsupported[key]")&&Read("OptimizationUi.cs").Contains("tools.Runtime.Status"),"unconfirmed setters roll back and show unavailable, never count as applied");
        C(!tools.Contains("v=>DecoInstanceManager.cullDistanceMax=")&&tools.Contains("_decor.Count>=512")&&Read("OptimizationTools.cs").Contains("Runtime.ReleaseDecor();_maximum.Restore()"),"bounded visual culling does not write native constants and releases before maximum-render ownership changes");
    }
}
