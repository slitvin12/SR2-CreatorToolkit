using System;
using System.Collections.Generic;
using System.Text.Json;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
namespace CreatorToolkit;
// Scalar engine APIs only. No pipeline-settings structs, detours, save writes,
// layer changes, collider/AI disabling, isKinematic, constraints or time changes.
internal sealed class M25RuntimeTools
{
    private sealed class Entry {internal Func<bool> Alive;internal Action Restore;}
    private readonly Dictionary<string,Entry> _leases=new();
    private readonly List<DecoInstanceManager> _decor=new();
    private readonly Dictionary<int,DecoInstanceManager> _hiddenDecor=new();
    private readonly HashSet<int> _decorExternal=new();private float _decorAt;
    internal int HiddenDecor=>_hiddenDecor.Count;
    private readonly List<Rigidbody> _bodies=new();private readonly Dictionary<int,Rigidbody> _slept=new();
    private readonly Dictionary<string,string> _unsupported=new();
    private readonly List<int> _dead=new();private readonly List<string> _retired=new();
    private readonly ToolkitMod _app;private float _discoverAt,_sleepAt;private int _bodyCursor;private ulong _sceneStamp;private float _sceneCheckAt;
    internal M25RuntimeRequest Request {get;private set;}=M25RuntimeRequest.Full;
    internal bool Active=>Request.Any||_leases.Count>0||_slept.Count>0;
    internal int Conflicts {get;private set;} internal int Discoveries {get;private set;}
    internal int Sleeps {get;private set;} internal int Wakes {get;private set;}
    internal int DetailManagers {get;private set;} internal int ShadowVolumes {get;private set;}
    internal int ShadowLights {get;private set;} internal int TerrainCount {get;private set;}
    internal float HdShadowDistanceActual {get;private set;}
    internal string Pipeline {get;private set;}="не проверен";
    internal string Status {get;private set;}="Runtime FPS: настройки игры";
    internal M25RuntimeTools(ToolkitMod app)=>_app=app;
    private static bool Live(UnityEngine.Object o)=>o!=null&&!o.WasCollected;
    private static bool Loaded(Component c)=>Live(c)&&c.gameObject.scene.IsValid()&&c.gameObject.scene.isLoaded;
    private void Set<T>(string key,Func<T> read,Action<T> write,T value,Func<bool> alive=null) where T:IEquatable<T>
    {
        if(_leases.ContainsKey(key)||_unsupported.ContainsKey(key)||_leases.Count>=1024)return;
        var lease=new M25ScalarLease<T>(read,write,value);if(!lease.Owned)return;
        _leases.Add(key,new Entry{Alive=alive??(()=>true),Restore=()=>{lease.Restore();if(lease.Conflict)Conflicts++;}});
        try{lease.Apply();}
        catch(Exception e)
        {
            // Refused/no-op bindings are not reported as applied. Roll back first;
            // a failed rollback throws and retains the ledger for a later retry.
            lease.Restore();_leases.Remove(key);_unsupported[key]=e.Message;
        }
    }
    internal void Apply(M25RuntimeRequest request)
    {
        request=request.Clamped();if(request==Request)return;
        Restore();Request=request;if(!request.Any)return;
        try
        {
            Pipeline=GraphicsSettings.currentRenderPipeline?.GetIl2CppType().FullName??"Built-in";
            // Unity's legacy shadow QualitySettings are not the HDRP control path.
            // Use volume/light scalars in HDRP; do not claim an ineffective legacy write.
            if(Pipeline=="Built-in")
            {
                if(request.ShadowDistance>0)Set("quality.distance",()=>QualitySettings.shadowDistance,v=>QualitySettings.shadowDistance=v,M25RuntimePolicy.Distance(QualitySettings.shadowDistance,request.ShadowDistance));
                if(request.Shadows!=M25ShadowMode.Game)Set("quality.resolution",()=> (int)QualitySettings.shadowResolution,v=>QualitySettings.shadowResolution=(ShadowResolution)v,(int)ShadowResolution.Low);
                if(request.Shadows==M25ShadowMode.Off)Set("quality.shadows",()=> (int)QualitySettings.shadows,v=>QualitySettings.shadows=(ShadowQuality)v,(int)ShadowQuality.Disable);
            }
            if(Pipeline!="Built-in"&&!Pipeline.Contains("HighDefinition")&&(request.ShadowDistance>0||request.Shadows!=M25ShadowMode.Game))
                _unsupported["shadow.pipeline"]="Unsupported pipeline: "+Pipeline; // URP requires its own validated asset API.
            if(request.Raycasts>0)Set("quality.raycasts",()=>QualitySettings.particleRaycastBudget,v=>QualitySettings.particleRaycastBudget=v,Math.Min(QualitySettings.particleRaycastBudget,request.Raycasts));
            // cullDistanceMax is a native constant in this player; no writes.
            // Use bounded, reversible visual-only batch culling instead.
            Discover();_sceneStamp=SceneStamp();_discoverAt=Time.unscaledTime+30;_sceneCheckAt=Time.unscaledTime+1;_sleepAt=Time.unscaledTime;
            UpdateStatus();
        }
        catch{Restore();throw;}
    }
    private void Discover()
    {
        Discoveries++;_retired.Clear();foreach(var pair in _leases)if(!pair.Value.Alive())_retired.Add(pair.Key);
        foreach(string key in _retired)_leases.Remove(key);
        if(Request.DetailDistance>0)
        {
            _dead.Clear();foreach(var pair in _hiddenDecor)if(!Loaded(pair.Value))_dead.Add(pair.Key);
            foreach(int id in _dead)_hiddenDecor.Remove(id);
            _decor.Clear();DetailManagers=0;
            if(!_app.Optimization.DetailedSettings.HideDecor)
            foreach(var d in UnityEngine.Object.FindObjectsOfType<DecoInstanceManager>())
            {if(!Loaded(d)||!d.initialized||!d.useGPUInstancing)continue;_decor.Add(d);DetailManagers++;if(_decor.Count>=512)break;}
            TerrainCount=0;foreach(var t in UnityEngine.Object.FindObjectsOfType<Terrain>())
            {if(!Loaded(t))continue;TerrainCount++;var terrain=t;Set("terrain."+t.GetInstanceID(),()=>terrain.detailObjectDistance,v=>terrain.detailObjectDistance=v,M25RuntimePolicy.Distance(t.detailObjectDistance,Request.DetailDistance),()=>Loaded(terrain));}
        }
        if(Pipeline.Contains("HighDefinition"))
        {
            if(Request.ShadowDistance>0)
            {
                ShadowVolumes=0;
                foreach(var volume in UnityEngine.Object.FindObjectsOfType<Volume>())
                {
                    if(!Loaded(volume)||!volume.isActiveAndEnabled||volume.weight<=0)continue;
                    var view=volume.HasInstantiatedProfile()?volume.profile:volume.sharedProfile;
                    if(!Live(view)||view.components==null)continue;
                    bool has=false;foreach(var c in view.components)if(Live(c)&&c.TryCast<HDShadowSettings>()!=null){has=true;break;}
                    if(!has)continue;
                    // Clone only a requested volume, never mutate the shared asset.
                    var profile=volume.profile;if(!Live(profile)||profile.components==null)continue;
                    foreach(var c in profile.components)
                    {
                        var hd=c?.TryCast<HDShadowSettings>();if(!Live(hd)||hd.maxShadowDistance==null)continue;
                        var parameter=hd.maxShadowDistance;var owner=volume;ShadowVolumes++;
                        string key="hd.distance."+hd.GetInstanceID();
                        Set(key,()=>parameter.value,v=>parameter.value=v,M25RuntimePolicy.Distance(parameter.value,Request.ShadowDistance),()=>Loaded(owner)&&Live(hd));
                        Set(key+".override",()=>parameter.overrideState,v=>parameter.overrideState=v,true,()=>Loaded(owner)&&Live(hd));
                        HdShadowDistanceActual=parameter.value;
                    }
                }
            }
            if(Request.Shadows!=M25ShadowMode.Game)
            {
                ShadowLights=0;
                foreach(var light in UnityEngine.Object.FindObjectsOfType<HDAdditionalLightData>())
                {
                    if(!Loaded(light)||!light.isActiveAndEnabled||!light.ShadowsEnabled())continue;var l=light;string key="hd.light."+light.GetInstanceID();ShadowLights++;
                    if(Request.Shadows==M25ShadowMode.Off)Set(key,()=>l.ShadowsEnabled(),v=>l.EnableShadows(v),false,()=>Loaded(l));
                    else
                    {
                        var r=l.shadowResolution;if(r==null)continue;
                        // Reference-type scalable settings, not boxed HDRP settings structs.
                        if(r.useOverride)
                        {if(r.@override>0)Set(key+".resolution",()=>r.@override,v=>r.@override=v,Math.Min(r.@override,256),()=>Loaded(l));}
                        else Set(key+".level",()=>r.level,v=>r.level=v,0,()=>Loaded(l));
                    }
                }
            }
        }
        if(Request.Sleep)
        {
            // Bounded cached census, not a full-world Find every frame.
            _bodies.Clear();_bodyCursor=0;var ids=new HashSet<int>();
            foreach(var p in UnityEngine.Object.FindObjectsOfType<SubbehaviourPlexer>())
            {
                if(!Loaded(p)||p._emotions==null)continue;var body=p._body??p.GetComponentInParent<Rigidbody>();
                if(Loaded(body)&&!body.isKinematic&&ids.Add(body.GetInstanceID()))_bodies.Add(body);
                if(_bodies.Count>=1024)break;
            }
        }
    }
    // Uses the game's own precomputed batch bounds; no DrawDecoMeshes detour,
    // density mutation, native arrays or pipeline settings struct writes.
    private void DecorTick()
    {
        if(_app.Optimization.DetailedSettings.HideDecor)return;
        var camera=Camera.main;if(!Loaded(camera)){ReleaseDecor();return;}
        var p=camera.transform.position;
        foreach(var d in _decor)
        {
            if(!Loaded(d))continue;int id=d.GetInstanceID();bool ours=_hiddenDecor.ContainsKey(id);
            if(_decorExternal.Contains(id))continue;
            if(ours&&d.enabled){_hiddenDecor.Remove(id);_decorExternal.Add(id);Conflicts++;continue;}
            if(!ours&&!d.enabled)continue;
            var bounds=d.thisBounds;var c=bounds.center;var e=bounds.extents;
            float sq=M25RuntimePolicy.BoundsDistanceSquared(p.x,p.y,p.z,c.x,c.y,c.z,e.x,e.y,e.z);
            bool hide=M25RuntimePolicy.CullDecor(sq,Request.DetailDistance,ours);
            if(hide&&!ours)
            {_hiddenDecor.Add(id,d);d.enabled=false;if(d.enabled)throw new InvalidOperationException("Decoration culling rejected disable");}
            else if(!hide&&ours)
            {d.enabled=true;if(!d.enabled)throw new InvalidOperationException("Decoration culling rejected restore");_hiddenDecor.Remove(id);}
        }
    }
    internal void ReleaseDecor()
    {
        _dead.Clear();var errors=new List<Exception>();
        foreach(var pair in _hiddenDecor)
        {
            try{var d=pair.Value;if(Loaded(d)&&!d.enabled){d.enabled=true;if(!d.enabled)throw new InvalidOperationException("Decoration restore readback mismatch");}_dead.Add(pair.Key);}
            catch(Exception e){errors.Add(e);}
        }
        foreach(int id in _dead)_hiddenDecor.Remove(id);
        _decor.Clear();_decorExternal.Clear();
        if(errors.Count>0)throw new AggregateException("Decoration restore incomplete",errors);
    }
    private void SleepTick()
    {
        var camera=Camera.main;if(!Loaded(camera)||!camera.isActiveAndEnabled||_app.Map.NativeMapOpen||NativeUiPriority.HasNativeFocus||_app.MenuOpen||_app.World.FreezeMobs){WakeAll();return;}
        var position=camera.transform.position;
        bool Visible(Rigidbody body)
        {
            var v=camera.WorldToViewportPoint(body.position); // generous margin, camera-front depth
            return v.z>0&&v.x>=-.15f&&v.x<=1.15f&&v.y>=-.15f&&v.y<=1.15f;
        }
        _dead.Clear();foreach(var pair in _slept)
        {
            var b=pair.Value;
            if(!Loaded(b)){_dead.Add(pair.Key);continue;}
            if(!b.IsSleeping()){_dead.Add(pair.Key);continue;} // native wake always wins
            if((b.position-position).sqrMagnitude<35*35||Visible(b)){b.WakeUp();Wakes++;_dead.Add(pair.Key);}
        }
        foreach(int id in _dead)_slept.Remove(id);
        int budget=Math.Min(64,_bodies.Count);
        for(int i=0;i<budget;i++)
        {
            if(_bodyCursor>=_bodies.Count)_bodyCursor=0;var b=_bodies[_bodyCursor++];
            if(_slept.Count>=1024||!Loaded(b)||!b.gameObject.activeInHierarchy||b.isKinematic||b.IsSleeping()||_slept.ContainsKey(b.GetInstanceID()))continue;
            if(!M25RuntimePolicy.MaySleep(Visible(b),(b.position-position).sqrMagnitude,b.velocity.sqrMagnitude,b.angularVelocity.sqrMagnitude))continue;
            _slept.Add(b.GetInstanceID(),b);b.Sleep();Sleeps++;
        }
    }
    private void WakeAll()
    {foreach(var b in _slept.Values)if(Loaded(b)&&!b.isKinematic&&b.IsSleeping()){b.WakeUp();Wakes++;}_slept.Clear();}
    internal void Tick()
    {
        if(!Request.Any)return;
        if(Time.unscaledTime>=_sceneCheckAt)
        {_sceneCheckAt=Time.unscaledTime+1;ulong stamp=SceneStamp();if(stamp!=_sceneStamp){_sceneStamp=stamp;_discoverAt=Time.unscaledTime+1;}}
        if(Time.unscaledTime>=_discoverAt){_discoverAt=Time.unscaledTime+30;Discover();UpdateStatus();}
        if(Request.DetailDistance>0&&Time.unscaledTime>=_decorAt){_decorAt=Time.unscaledTime+.25f;DecorTick();}
        if(Request.Sleep&&Time.unscaledTime>=_sleepAt){_sleepAt=Time.unscaledTime+.25f;SleepTick();}
    }
    private static ulong SceneStamp()
    {ulong h=14695981039346656037UL;for(int i=0;i<SceneManager.sceneCount;i++)h=M20DiscoveryPolicy.AppendScene(h,SceneManager.GetSceneAt(i).handle);return h;}
    private void UpdateStatus()=>Status=$"{Pipeline} · тени {Request.Shadows} / {Request.ShadowDistance} м · декор {Request.DetailDistance} м · raycasts ≤{Request.Raycasts} · сон { (Request.Sleep?"ВКЛ":"ВЫКЛ")}"+(_unsupported.Count>0?" · Недоступно: "+string.Join(", ",_unsupported.Keys):"");
    internal void Restore()
    {
        WakeAll();_bodies.Clear();ReleaseDecor();var failures=new List<Exception>();_retired.Clear();
        foreach(var pair in _leases)
        {try{if(pair.Value.Alive())pair.Value.Restore();_retired.Add(pair.Key);}catch(Exception e){failures.Add(e);}}
        foreach(string key in _retired)_leases.Remove(key);
        if(failures.Count>0)throw new AggregateException("Runtime FPS restore incomplete",failures);
        _unsupported.Clear();Request=M25RuntimeRequest.Full;Status="Runtime FPS: настройки игры восстановлены";
    }
    internal string Diagnostics()=>"RuntimeFPS: "+JsonSerializer.Serialize(new{Pipeline,Request.ShadowDistance,Shadows=Request.Shadows.ToString(),Request.DetailDistance,Request.Raycasts,Request.Sleep,Leases=_leases.Count,HdShadowDistanceActual,Unsupported=_unsupported,ShadowVolumes,ShadowLights,DetailManagers,HiddenDecor,TerrainCount,TrackedBodies=_bodies.Count,Sleeping=_slept.Count,Sleeps,Wakes,Conflicts,Discoveries,Status})+"\n";
    internal string Audit()=>Diagnostics()+"RuntimeFPSActual: "+JsonSerializer.Serialize(new{QualitySettings.shadowDistance,Resolution=QualitySettings.shadowResolution.ToString(),Shadows=QualitySettings.shadows.ToString(),QualitySettings.particleRaycastBudget,DecoInstanceManager.cullDistanceMax,QualitySettings.lodBias})+"\n";
}
