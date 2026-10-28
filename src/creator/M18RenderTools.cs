using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
namespace CreatorToolkit;

// Explicit, reversible visual-only profile. No pipeline settings structs,
// DrawDecoMeshes detour, native model batches, resolution, physics or saves.
internal sealed class M18RenderTools
{
    private readonly Dictionary<int,bool> _decor=new(),_effects=new();
    private readonly Dictionary<int,bool> _lights=new();
    private readonly Dictionary<int,(float Before,float Expected)> _cameras=new();
    private readonly HashSet<int> _alive=new();
    private readonly List<int> _dead=new();
    private M19GraphicsRequest _request=M19GraphicsRequest.Full;
    private bool _maximumPreset;
    internal M19GraphicsRequest Request=>Active?_request:M19GraphicsRequest.Full;
    internal bool MaximumPreset=>Active&&_maximumPreset;
    internal M19Effects SeenEffects {get;private set;}
    private ulong _sceneStamp;
    private int _phase=-1;
    private float _nextSceneCheck;
    private bool _lodOwned;private float _lodBefore,_lodExpected,_next;
    internal bool Active {get;private set;}
    internal int Conflicts {get;private set;}
    internal int DecorScans {get;private set;}
    internal int LightScans {get;private set;}
    internal int VolumeScans {get;private set;}
    internal int CameraChecks {get;private set;}
    internal int ProfileInstantiations {get;private set;}
    internal int PeriodicPhases {get;private set;}
    internal string Status {get;private set;}="Максимум FPS выключен";
    internal int DecorCount=>_decor.Count;
    internal int EffectCount=>_effects.Count;
    internal int ShadowCount=>_lights.Count;
    private static bool Loaded(Component c)=>c!=null&&!c.WasCollected&&c.gameObject.scene.IsValid()&&c.gameObject.scene.isLoaded;
    internal static bool HeavyEffect(string name)=>M18Policy.HeavyEffect(name);
    internal void Apply()=>Apply(M19GraphicsRequest.Maximum,true);
    internal void Apply(M19GraphicsRequest request,bool maximum=false)
    {
        request=request.Clamped();
        if(Active&&_request==request&&_maximumPreset==maximum)return;
        Restore();_request=request;_maximumPreset=maximum;SeenEffects=M19Effects.None;
        if(!request.Any)return;
        Conflicts=0;Active=true;
        try
        {
            _lodBefore=QualitySettings.lodBias;
            _lodExpected=maximum?M18Policy.LowLod(_lodBefore):M19GraphicsPolicy.Lod(_lodBefore,request.LodPercent);
            if(request.LodPercent<100&&float.IsFinite(_lodBefore)&&_lodBefore>0){_lodOwned=true;QualitySettings.lodBias=_lodExpected;}
            // Explicit Apply remains synchronous. Only background rediscovery
            // is spread over frames; no half-applied button result is reported.
            for(int p=0;p<M20DiscoveryPolicy.PhaseCount;p++)DiscoverPhase(p);
            _sceneStamp=SceneStamp();_nextSceneCheck=Time.unscaledTime+1f;
            _next=Time.unscaledTime+30f;_phase=-1;UpdateStatus();
        }
        catch{Restore();throw;}
    }
    internal void Tick()
    {
        if(!Active||!M20DiscoveryPolicy.NeedsPeriodic(_request))return;
        float now=Time.unscaledTime;
        if(now>=_nextSceneCheck)
        {
            _nextSceneCheck=now+1f;ulong stamp=SceneStamp();
            if(stamp!=_sceneStamp)
            {
                _sceneStamp=stamp;
                // Do not restart an in-flight batch on every streaming event.
                // A trailing rescan covers objects created after their phase.
                _next=now+1f;
            }
        }
        if(_phase<0)
        {
            if(now<_next)return;
            _phase=0;_next=now+30f;
        }
        while(_phase<M20DiscoveryPolicy.PhaseCount&&!M20DiscoveryPolicy.PhaseNeeded(_request,_phase))_phase++;
        if(_phase<M20DiscoveryPolicy.PhaseCount){DiscoverPhase(_phase++);PeriodicPhases++;}
        if(_phase>=M20DiscoveryPolicy.PhaseCount){_phase=-1;UpdateStatus();}
    }
    private static ulong SceneStamp()
    {
        ulong hash=14695981039346656037UL;
        for(int i=0;i<SceneManager.sceneCount;i++)hash=M20DiscoveryPolicy.AppendScene(hash,SceneManager.GetSceneAt(i).handle);
        return hash;
    }
    private void DiscoverPhase(int phase)
    {
        if(!M20DiscoveryPolicy.PhaseNeeded(_request,phase))return;
        if(phase==0)DiscoverDecor();
        else if(phase==1)DiscoverLights();
        else if(phase==2)DiscoverEffects();
        else if(phase==3)DiscoverCamera();
    }
    private void DiscoverDecor()
    {
        DecorScans++;_alive.Clear();
        foreach(var deco in UnityEngine.Object.FindObjectsOfType<DecoInstanceManager>(true))
        {
            if(!Loaded(deco))continue;int id=deco.GetInstanceID();_alive.Add(id);
            if(_decor.ContainsKey(id)||_decor.Count>=512||!deco.enabled||!deco.initialized||!deco.useGPUInstancing)continue;
            _decor.Add(id,true);deco.enabled=false;if(deco.enabled)throw new InvalidOperationException("Decor renderer rejected disable");
        }
        Retire(_decor);
    }
    private void DiscoverLights()
    {
        LightScans++;_alive.Clear();
        foreach(var light in UnityEngine.Object.FindObjectsOfType<HDAdditionalLightData>(true))
        {
            if(!Loaded(light))continue;int id=light.GetInstanceID();_alive.Add(id);
            if(_lights.ContainsKey(id)||_lights.Count>=256||!light.isActiveAndEnabled||!light.ShadowsEnabled())continue;
            _lights.Add(id,true);light.EnableShadows(false);if(light.ShadowsEnabled())throw new InvalidOperationException("HDRP light rejected shadow disable");
        }
        Retire(_lights);
    }
    private void DiscoverEffects()
    {
        VolumeScans++;
        foreach(var volume in UnityEngine.Object.FindObjectsOfType<Volume>(true))
        {
            if(!Loaded(volume)||!volume.isActiveAndEnabled||volume.weight<=0)continue;
            bool instantiated=volume.HasInstantiatedProfile();
            // Inspect the existing instance or shared asset without creating a
            // runtime copy merely for discovery. Shared assets are read-only.
            var profile=instantiated?volume.profile:volume.sharedProfile;
            if(profile==null||profile.WasCollected||profile.components==null)continue;
            bool requestedActive=false;
            foreach(var effect in profile.components)
            {
                if(effect==null||effect.WasCollected)continue;
                var bit=M19GraphicsPolicy.Effect(effect.GetIl2CppType().Name);SeenEffects|=bit;
                if((_request.DisabledEffects&bit)!=0&&effect.active)requestedActive=true;
            }
            if(!requestedActive)continue;
            if(M20DiscoveryPolicy.CloneNeeded(instantiated,requestedActive))
            {
                profile=volume.profile;ProfileInstantiations++;
                if(profile==null||profile.WasCollected||profile.components==null)continue;
            }
            foreach(var effect in profile.components)
            {
                if(effect==null||effect.WasCollected)continue;int id=effect.GetInstanceID();
                string name=effect.GetIl2CppType().Name;var bit=M19GraphicsPolicy.Effect(name);
                if((_request.DisabledEffects&bit)==0||_effects.ContainsKey(id)||_effects.Count>=256||!effect.active||!HeavyEffect(name))continue;
                _effects.Add(id,true);effect.active=false;if(effect.active)throw new InvalidOperationException("Volume component rejected disable");
            }
        }
        // Retain inactive volumes for conditional restore. Do not destroy any
        // runtime profile: another native system may already hold its pointer.
    }
    private void DiscoverCamera()
    {
        CameraChecks++;var camera=Camera.main;
        if(camera!=null&&!camera.WasCollected&&_cameras.Count<16)
        {
            int id=camera.GetInstanceID();float before=camera.farClipPlane;
            if(!_cameras.ContainsKey(id)&&float.IsFinite(before)&&before>camera.nearClipPlane+1)
            {
                float expected=_maximumPreset?M18Policy.FarClip(before,camera.nearClipPlane):M19GraphicsPolicy.FarClip(before,camera.nearClipPlane,_request.Distance);
                _cameras.Add(id,(before,expected));camera.farClipPlane=expected;if(Mathf.Abs(camera.farClipPlane-expected)>.1f)throw new InvalidOperationException("Camera rejected clipping range");
            }
        }
    }
    private void UpdateStatus()=>Status=(_maximumPreset?"Максимум FPS":"Своя графика")+" · тени: "+ShadowCount+" · декор: "+DecorCount+" · эффекты: "+EffectCount+" · LOD "+_request.LodPercent+"% · дальность "+(_request.Distance>0?_request.Distance+" м":"игры");
    private void Retire(Dictionary<int,bool> states)
    {
        _dead.Clear();foreach(int id in states.Keys)if(!_alive.Contains(id))_dead.Add(id);
        foreach(int id in _dead)states.Remove(id);
    }
    internal void Restore()
    {
        if(!Active)return;
        // Each restore only writes a value that is still ours. A later game
        // change wins; no per-frame reapplication fights another system.
        var failures=new List<Exception>();
        void Attempt(Action action){try{action();}catch(Exception e){failures.Add(e);}}
        // No global native census is needed for an empty ownership ledger.
        if(_decor.Count>0)Attempt(()=>{foreach(var deco in Resources.FindObjectsOfTypeAll<DecoInstanceManager>())
            if(deco!=null&&!deco.WasCollected&&_decor.TryGetValue(deco.GetInstanceID(),out bool before))
            {if(!deco.enabled)deco.enabled=before;else if(deco.enabled!=before)Conflicts++;}
        });
        if(_lights.Count>0)Attempt(()=>{foreach(var light in Resources.FindObjectsOfTypeAll<HDAdditionalLightData>())
            if(light!=null&&!light.WasCollected&&_lights.TryGetValue(light.GetInstanceID(),out bool before))
            {if(!light.ShadowsEnabled())light.EnableShadows(before);}
        });
        if(_effects.Count>0)Attempt(()=>{foreach(var effect in Resources.FindObjectsOfTypeAll<VolumeComponent>())
            if(effect!=null&&!effect.WasCollected&&_effects.TryGetValue(effect.GetInstanceID(),out bool before))
            {if(!effect.active)effect.active=before;}
        });
        if(_cameras.Count>0)Attempt(()=>{foreach(var camera in Resources.FindObjectsOfTypeAll<Camera>())
            if(camera!=null&&!camera.WasCollected&&_cameras.TryGetValue(camera.GetInstanceID(),out var state))
            {if(Mathf.Abs(camera.farClipPlane-state.Expected)<.1f)camera.farClipPlane=state.Before;else Conflicts++;}
        });
        Attempt(()=>{if(_lodOwned){if(Mathf.Abs(QualitySettings.lodBias-_lodExpected)<.0001f)QualitySettings.lodBias=_lodBefore;else Conflicts++;}
        });
        if(failures.Count>0)throw new AggregateException("Maximum profile restore incomplete; leases retained for retry",failures);
        _decor.Clear();_lights.Clear();_effects.Clear();_cameras.Clear();_alive.Clear();_dead.Clear();_lodOwned=false;Active=false;_phase=-1;
        Status="Максимум FPS выключен · конфликты восстановления: "+Conflicts;
    }
}
