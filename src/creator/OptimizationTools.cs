using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CreatorToolkit;

// Opt-in graphics plus an isolated conservative runtime sleep tool. No saves,
// PlayerPrefs, window resolution, time, colliders, AI or VFX Graph are changed.
// Object discovery is bounded and event-driven, never performed by OnGUI.
internal sealed class OptimizationTools
{
    private sealed class TextureState
    {
        internal Texture2D Asset; // Borrowed UI ASSET, not a world/actor wrapper.
        internal bool Original;
    }
    private sealed class ParticleState
    {
        internal int Id,Before,Expected;
        internal bool External;
    }
    private readonly ToolkitMod _app;
    private readonly GeometryLodPolicy _geometry;
    private readonly M18RenderTools _maximum=new();
    internal M25RuntimeTools Runtime {get;}
    private M25ScalarLease<int> _benchmarkCap;
    internal bool BenchmarkCapActive=>_benchmarkCap?.Owned==true;
    internal void UncapBenchmark()
    {RequireReady();if(BenchmarkCapActive)return;_benchmarkCap=new M25ScalarLease<int>(()=>Application.targetFrameRate,v=>Application.targetFrameRate=v,-1);_benchmarkCap.Apply();UiRevision++;}
    internal void RestoreBenchmarkCap(){_benchmarkCap?.Restore();UiRevision++;}
    internal void ApplyRuntime(M25RuntimeRequest request){RequireReady();Runtime.Apply(request);UiRevision++;}
    internal bool MaximumActive=>_maximum.Active;
    internal string MaximumStatus=>_maximum.Status;
    internal bool MaximumPresetActive=>_maximum.MaximumPreset;
    internal M19GraphicsRequest DetailedSettings=>_maximum.Active?_maximum.Request:new(GeometryActive?75:100,DistanceActive?180:0,false,false,M19Effects.None);
    internal M19Effects SeenEffects=>_maximum.SeenEffects;
    internal void ApplyDetailed(M19GraphicsRequest request)
    {
        RequireReady();Runtime.ReleaseDecor();_maximum.Restore();RestoreDistance();RestoreGeometry(false);
        try{_maximum.Apply(request);UiRevision++;Status=MaximumStatus;_app.Notify(Status);}
        catch{_maximum.Restore();throw;}
    }
    internal string MaximumDiagnostics()=>"M18_RENDER "+System.Text.Json.JsonSerializer.Serialize(new{_maximum.Active,_maximum.DecorCount,_maximum.EffectCount,_maximum.ShadowCount,_maximum.Conflicts,_maximum.Status,_maximum.Request,_maximum.SeenEffects,_maximum.MaximumPreset,_maximum.DecorScans,_maximum.LightScans,_maximum.VolumeScans,_maximum.CameraChecks,_maximum.ProfileInstantiations,_maximum.PeriodicPhases});
    private readonly Dictionary<int,TextureState> _uiTextures=new();
    private readonly Dictionary<int,ParticleState> _particles=new();
    private bool _snapshot,_mipOwned,_smoothOwned;
    private bool _distanceOn;private int _distanceCamera;private float _distanceBefore,_distanceExpected;
    internal bool DistanceActive=>_distanceOn;
    internal string DistanceStatus {get;private set;}="Полная дальность 3D";
    private int _mipBefore,_mipExpected,_uploadBefore,_renderersBefore,_ioBefore;
    private int _sceneCount=-1,_activeScene=-1,_conflicts;
    private ulong _sceneStamp;
    private UnityEngine.ThreadPriority _priorityBefore;
    private int _uploadExpected,_renderersExpected,_ioExpected;
    private float _nextCensus;
    internal OptimizationRequest Current {get;private set;}=OptimizationRequest.Full;
    internal bool Active=>_snapshot||GeometryActive||DistanceActive||MaximumActive||Runtime.Active||BenchmarkCapActive;
    internal int UiRevision {get;private set;}
    internal bool SmoothActive=>_snapshot&&Current.Smooth;
    internal bool RenderSupported=>OptimizationPolicy.NativeRenderScalingSupported;
    internal bool FoliageSupported=>OptimizationPolicy.NativeFoliageReductionSupported;
    internal bool MipSupported {get;private set;}=true;
    internal string MipReason {get;private set;}="Проверяется перед изменением";
    internal string RenderReason=>"Недоступно: запись HDRP-структур через IL2CPP отключена после аварии 0xc0000005.";
    internal string FoliageReason=>"Недоступно: detour DrawDecoMeshes отключён после NaN в отрисовке.";
    internal string Status {get;private set;}="Выключено · настройки игры не изменены";
    internal int UiTextureRoots=>_uiTextures.Count;
    internal int NativeActorRoots=>0;
    internal int CameraSnapshots=>0;
    internal int ParticleSnapshots=>_particles.Count;
    internal int FoliageManagers=>0;
    internal int EligibleParticles {get;private set;}
    internal bool ParticlesSupported=>NativeParticleSafety.Supported;
    internal int DiscoveryScans {get;private set;}
    internal int FoliageDraws {get;private set;}
    internal int StreamingBudgetApplications {get;private set;}
    internal float LastFoliageBefore {get;private set;}
    internal float LastFoliageAfter {get;private set;}
    internal int LastRestoreConflicts {get;private set;}
    internal int RenderCameraId=>0;
    internal bool SupportsNativeBatches=>OptimizationPolicy.NativeModelBatchWritesSupported;
    internal float ActualRenderPercent {get;private set;}=100;
    internal Vector2Int ActualRenderSize {get;private set;}
    internal string Census {get;private set;}="Настройки не менялись";
    internal OptimizationTools(ToolkitMod app)
    {
        _app=app;Runtime=new M25RuntimeTools(app);
        _geometry=new GeometryLodPolicy(()=>QualitySettings.lodBias,v=>QualitySettings.lodBias=v);
    }
    internal bool GeometryActive=>_geometry.Active;
    internal bool GeometryPending=>_geometry.Pending;
    internal bool GeometrySupported=>_geometry.Supported;
    // Cached readback only: OnGUI must never probe or mutate native quality.
    internal float GeometryEffectiveBias=>_geometry.EffectiveBias;
    internal string GeometryStatus=>_geometry.Status;
    internal string GeometryDiagnostics()=>_geometry.Diagnostics();
    internal void ApplyGeometryLow()
    {
        RequireReady();
        _geometry.ApplyLow();
        UiRevision++;
        _app.Notify(GeometryStatus);
    }
    internal void RestoreGeometry(bool notify=true)
    {
        _geometry.Restore();
        UiRevision++;
        if(notify)_app.Notify(GeometryStatus);
    }
    private static bool Live(UnityEngine.Object value)=>value!=null&&!value.WasCollected;
    private static bool LiveWorld(Component value)=>Live(value)&&value.gameObject.activeInHierarchy&&
        value.gameObject.scene.IsValid()&&value.gameObject.scene.isLoaded;

    internal OptimizationRequest ForUi()=>_snapshot?Current:
        new OptimizationRequest(OptimizationPolicy.Mip(QualitySettings.globalTextureMipmapLimit),100,100,100,false);
    private void RequireReady()
    {
        if(!_app.Ready||_app.Map.Busy)throw new InvalidOperationException("Дождитесь завершения загрузки или телепортации.");
        if(_app.Audit.MainAutosaveHeld)
            throw new InvalidOperationException("Дождитесь завершения защитного возврата из проверочной копии.");
    }
    private void Snapshot()
    {
        if(_snapshot)return;
        _mipBefore=QualitySettings.globalTextureMipmapLimit;
        _uploadBefore=QualitySettings.asyncUploadTimeSlice;
        _renderersBefore=QualitySettings.streamingMipmapsRenderersPerFrame;
        _ioBefore=QualitySettings.streamingMipmapsMaxFileIORequests;
        _priorityBefore=Application.backgroundLoadingPriority;
        Current=new(_mipBefore,100,100,100,false);
        _snapshot=true;_conflicts=0;LastRestoreConflicts=0;
    }
    internal void ApplyPotato()
    {
        RequireReady();
        Runtime.ReleaseDecor();if(MaximumActive)_maximum.Restore();
        try
        {
            Runtime.Restore();
            Apply(OptimizationRequest.Potato with{ParticleMax=256});
            ApplyGeometryLow();
            ApplyDistanceLow();
            ApplyRuntime(M25RuntimeRequest.Balanced);
            Status="Картофельный режим включён · текстуры, LOD, загрузка и доступные частицы";
            if(!MipSupported)Status="Картофельный режим · LOD и загрузка; текстуры сохранены ради UI";
            _app.Notify(Status);
        }
        catch{Restore(false);throw;}
    }
    internal void ApplyMaximum()
    {
        RequireReady();
        try{ApplyPotato();_maximum.Apply();ApplyRuntime(M25RuntimeRequest.Maximum);Apply(Current with{ParticleMax=128});UiRevision++;Status=MaximumStatus;_app.Notify(Status);}
        catch{Restore(false);throw;}
    }
    internal void Apply(OptimizationRequest request)
    {
        RequireReady();Snapshot();UiRevision++;
        request=request.Clamped();
        try
        {
            // Collect before changing the global mip limit. Refuse, rather
            // than lower UI quality, if the bounded protection set overflows.
            int mip=request.Mip;
            if(mip>0&&!ProtectLoadedUi())mip=_mipBefore;
            if(QualitySettings.globalTextureMipmapLimit!=mip)
            {
                QualitySettings.globalTextureMipmapLimit=mip;
                _mipExpected=mip;_mipOwned=true;
            }
            else if(_mipOwned)_mipExpected=mip;
            bool renderApplied=ApplyRender(request.RenderPercent);
            int render=renderApplied?request.RenderPercent:100;
            int particles=ParticlesSupported?request.ParticlePercent:100;
            int foliage=FoliageSupported?request.FoliagePercent:100;
            Current=request with{Mip=mip,RenderPercent=render,FoliagePercent=foliage,ParticlePercent=particles,ParticleMax=ParticlesSupported?request.ParticleMax:0};
            ApplySmooth(request.Smooth);
            DiscoverVisuals();
            _sceneCount=SceneManager.sceneCount;_activeScene=SceneManager.GetActiveScene().handle;
            _sceneStamp=SceneStamp();
            _nextCensus=Time.unscaledTime+2f;
            Status="Текстуры: "+OptimizationPolicy.MipLabel(mip)+" · частицы: "+particles+"%"+
                (request.Smooth?" · фоновая загрузка":"")+(mip!=request.Mip?" · текстуры сохранены ради UI":"");
            _app.Notify(Status);
        }
        catch
        {
            Restore(false);
            throw;
        }
    }

    internal void VisitLoadedUiTextures(Action<Texture2D> observe)
    {
        // A Sprite resource is not evidence of UI use: a blanket Sprite
        // census includes the unreadable Unity splash logo and world art.
        // Only actual loaded UI components, fonts and our native catalog art
        // are candidates. No GPU copies or private engine bypasses are used.
        foreach(var image in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Image>())
            if(Live(image)&&image.gameObject.scene.IsValid())
            {
                if(Live(image.sprite))observe(image.sprite.texture);
                if(Live(image.overrideSprite))observe(image.overrideSprite.texture);
            }
        foreach(var image in Resources.FindObjectsOfTypeAll<UnityEngine.UI.RawImage>())
            if(Live(image)&&image.gameObject.scene.IsValid())observe(image.texture?.TryCast<Texture2D>());
        foreach(var font in Resources.FindObjectsOfTypeAll<Font>())
            if(Live(font)&&Live(font.material))observe(font.material.mainTexture?.TryCast<Texture2D>());
        foreach(var font in Resources.FindObjectsOfTypeAll<Il2CppTMPro.TMP_FontAsset>())
            if(Live(font)&&font.atlasTextures!=null)
                foreach(var texture in font.atlasTextures)observe(texture);
        foreach(var entry in _app.Actors.Catalog)
        {
            var sprite=entry.Type?.Icon??entry.Type?.icon;
            if(Live(sprite))observe(sprite.texture);
        }
        _app.Map.Icons.VisitUiTextures(observe);
    }
    private bool ProtectLoadedUi()
    {
        var dead=new List<int>();
        foreach(var pair in _uiTextures)if(!Live(pair.Value.Asset))dead.Add(pair.Key);
        foreach(int id in dead)_uiTextures.Remove(id);
        var candidates=new Dictionary<int,Texture2D>();
        void Consider(Texture2D texture)
        {
            if(!Live(texture)||texture.mipmapCount<=1||texture.ignoreMipmapLimit)return;
            int id=texture.GetInstanceID();
            if(_uiTextures.ContainsKey(id))return;
            if(candidates.Count+_uiTextures.Count>=OptimizationPolicy.UiTextureBudget&&!candidates.ContainsKey(id))
                throw new InvalidOperationException("Лимит защиты UI достигнут; снижение текстур отменено.");
            candidates[id]=texture;
        }
        VisitLoadedUiTextures(Consider);
        // Validate the entire set before the first setter. Imported
        // non-readable UI cannot be opted out at runtime in this player.
        // Keep the original global quality, rather than blur it or fail
        // halfway through a profile with a UnityException.
        foreach(var texture in candidates.Values)if(!texture.isReadable)
        {
            MipSupported=false;MipReason="Нечитаемая UI-текстура: "+texture.name+
                ". Исходный mip-уровень сохранён.";
            return false;
        }
        foreach(var pair in candidates)
        {
            bool original=pair.Value.ignoreMipmapLimit;
            pair.Value.ignoreMipmapLimit=true;
            if(!pair.Value.ignoreMipmapLimit)
                throw new InvalidOperationException("Нативное исключение UI-текстуры не подтвердилось.");
            _uiTextures.Add(pair.Key,new TextureState{Asset=pair.Value,Original=original});
        }
        MipSupported=true;MipReason="Используемые UI-текстуры защищены; без чтения пикселей и GPU-копий.";
        return true;
    }
    // Crash containment: no boxed HDRP settings setters, camera mutation,
    // dynamic scaler callback or replacement rendering hook. A managed catch
    // cannot recover from the observed native/coreclr access violation.
    private bool ApplyRender(int percent)
    {
        RestoreRender();
        return percent==100;
    }
    private void ApplySmooth(bool enabled)
    {
        if(!enabled){RestoreSmooth();return;}
        _uploadExpected=Math.Min(Math.Max(1,_uploadBefore),OptimizationPolicy.UploadMs);
        _renderersExpected=OptimizationPolicy.NativeBatch(_renderersBefore,OptimizationPolicy.RenderersPerFrame);
        _ioExpected=OptimizationPolicy.NativeBatch(_ioBefore,OptimizationPolicy.IoRequests);
        // Both original and reproduced crash dumps end in WorldPopulator
        // set_ACTORS_PER_FRAME -> il2cpp_field_static_set_value. Never write
        // either model-batch field, including during restore. Use streaming APIs only.
        _smoothOwned=true;
        WriteLoadingBudgets();
        if(_renderersExpected>0)QualitySettings.streamingMipmapsRenderersPerFrame=_renderersExpected;
        if(_ioExpected>0)QualitySettings.streamingMipmapsMaxFileIORequests=_ioExpected;
    }
    private void WriteLoadingBudgets()
    {
        QualitySettings.asyncUploadTimeSlice=_uploadExpected;
        Application.backgroundLoadingPriority=UnityEngine.ThreadPriority.BelowNormal;
        StreamingBudgetApplications++;
    }
    internal void AfterTurboLoad(Il2CppMonomiPark.SlimeRancher.SceneManagement.SceneLoader loader,bool enabled)
    {
        if(!SmoothActive||!_smoothOwned||loader==null)return;
        // Turbo loading can cache the module's active budget. Ensure its
        // eventual native restore cannot resurrect it after this module stops.
        // Never read/write the loader's physics/continuation/scene queues.
        if(enabled)
        {
            if(loader._cachedAsyncUploadTimeSlice==_uploadExpected)loader._cachedAsyncUploadTimeSlice=_uploadBefore;
            if(loader._cachedBackgroundLoadingPriority==UnityEngine.ThreadPriority.BelowNormal)
                loader._cachedBackgroundLoadingPriority=_priorityBefore;
        }
        WriteLoadingBudgets();
    }
    private void DiscoverVisuals()
    {
        DiscoveryScans++;EligibleParticles=0;
        // Store only primitive IDs/values across streaming. Retire snapshots
        // of destroyed native objects so an unloaded zone cannot consume the
        // bounded budget forever. Inactive-but-live objects remain restorable.
        var liveParticles=new HashSet<int>();
        foreach(var system in Resources.FindObjectsOfTypeAll<ParticleSystem>())
            if(Live(system))liveParticles.Add(system.GetInstanceID());
        var dead=new List<int>();
        foreach(int id in _particles.Keys)if(!liveParticles.Contains(id))dead.Add(id);
        foreach(int id in dead)_particles.Remove(id);
        if(Current.ParticlePercent==100&&Current.ParticleMax==0)RestoreParticles();
        foreach(var system in UnityEngine.Object.FindObjectsOfType<ParticleSystem>())
        {
            if(!LiveWorld(system))continue;
            string name=system.name+" "+system.transform.parent?.name;
            if(!NativeParticleSafety.TryRead(system,out bool collision,out bool trigger)||
                !OptimizationPolicy.AmbientParticle(name,collision,trigger))continue;
            EligibleParticles++;
            if(Current.ParticlePercent==100&&Current.ParticleMax==0)continue;
            int id=system.GetInstanceID();int actual=system.main.maxParticles;
            if(!_particles.TryGetValue(id,out var state))
            {
                if(_particles.Count>=OptimizationPolicy.ParticleBudget)continue;
                state=new ParticleState{Id=id,Before=actual,Expected=actual};_particles.Add(id,state);
            }
            if(state.External)continue;
            if(actual!=state.Expected){state.External=true;_conflicts++;continue;}
            int cap=OptimizationPolicy.ParticleCap(state.Before,Current.ParticlePercent);
            if(Current.ParticleMax>0)cap=Math.Min(cap,Current.ParticleMax);
            var module=system.main;module.maxParticles=cap;state.Expected=cap;
        }
        Census="Растительность: без изменений · частицы декора: "+EligibleParticles+
            " (ограничено "+_particles.Count+"). "+(NativeParticleSafety.Supported?
            "VFX Graph и опасные эффекты не меняются.":"Проверка частиц недоступна: они не меняются.");
    }
    internal void Tick()
    {
        if(!Active||!_app.Ready)return;
        _maximum.Tick();Runtime.Tick();
        if(Time.unscaledTime<_nextCensus)return;
        _nextCensus=Time.unscaledTime+2f;
        if(!MaximumActive)_geometry.Observe(); // Read-only; never fights external quality edits.
        if(!_snapshot)return;
        int scenes=SceneManager.sceneCount,active=SceneManager.GetActiveScene().handle;
        ulong stamp=SceneStamp();
        if(scenes!=_sceneCount||active!=_activeScene||stamp!=_sceneStamp)
        {
            if(Current.Mip>0&&!ProtectLoadedUi())
            {
                if(_mipOwned&&QualitySettings.globalTextureMipmapLimit==_mipExpected)
                {QualitySettings.globalTextureMipmapLimit=_mipBefore;_mipOwned=false;}
                Current=Current with{Mip=QualitySettings.globalTextureMipmapLimit};
                Status="Mip-уровень восстановлен ради нового UI. "+MipReason;
            }
            DiscoverVisuals();_sceneCount=scenes;_activeScene=active;_sceneStamp=stamp;
        }
    }
    private static ulong SceneStamp()
    {
        // Equal scene counts do not imply equal streamed zones. A small
        // allocation-free census of actual native handles catches swaps.
        ulong stamp=14695981039346656037UL;
        for(int i=0;i<SceneManager.sceneCount;i++)
            stamp=unchecked((stamp^(uint)SceneManager.GetSceneAt(i).handle)*1099511628211UL);
        return stamp;
    }
    private void RestoreParticles()
    {
        if(_particles.Count==0)return;
        foreach(var system in Resources.FindObjectsOfTypeAll<ParticleSystem>())
        {
            if(!Live(system)||!_particles.TryGetValue(system.GetInstanceID(),out var state)||state.External)continue;
            var module=system.main;
            if(module.maxParticles==state.Expected)module.maxParticles=state.Before;
            else _conflicts++;
        }
        _particles.Clear();
    }
    private void RestoreSmooth()
    {
        if(!_smoothOwned)return;
        void RestoreInt(int actual,int expected,int original,Action<int> write)
        {if(actual==expected)write(original);else _conflicts++;}
        RestoreInt(QualitySettings.asyncUploadTimeSlice,_uploadExpected,_uploadBefore,v=>QualitySettings.asyncUploadTimeSlice=v);
        if(Application.backgroundLoadingPriority==UnityEngine.ThreadPriority.BelowNormal)
            Application.backgroundLoadingPriority=_priorityBefore;
        else _conflicts++;
        RestoreInt(QualitySettings.streamingMipmapsRenderersPerFrame,_renderersExpected,_renderersBefore,
            v=>QualitySettings.streamingMipmapsRenderersPerFrame=v);
        RestoreInt(QualitySettings.streamingMipmapsMaxFileIORequests,_ioExpected,_ioBefore,
            v=>QualitySettings.streamingMipmapsMaxFileIORequests=v);
        _smoothOwned=false;
    }
    private void RestoreRender()
    {
        // This version never owns HDRP/camera settings. Do not attempt to
        // restore values from a previous DLL by making the same unsafe calls.
        ActualRenderPercent=100;ActualRenderSize=Vector2Int.zero;
    }
    // Scalar Camera property, not an HDRP pipeline/camera settings struct.
    // Explicit temporary lease; do not fight an external camera override.
    internal void ApplyDistanceLow()
    {
        RequireReady();var camera=Camera.main;if(camera==null)throw new InvalidOperationException("Игровая камера недоступна");
        if(_distanceOn)return;
        _distanceBefore=camera.farClipPlane;_distanceExpected=M17Policy.FarClip(_distanceBefore,camera.nearClipPlane);
        if(!float.IsFinite(_distanceExpected)||_distanceExpected<=camera.nearClipPlane)throw new InvalidOperationException("Некорректная дальность камеры");
        _distanceCamera=camera.GetInstanceID();_distanceOn=true;camera.farClipPlane=_distanceExpected;
        if(Mathf.Abs(camera.farClipPlane-_distanceExpected)>.1f)throw new InvalidOperationException("Камера не приняла дальность");
        _distanceOn=true;DistanceStatus="3D до "+_distanceExpected.ToString("0")+" м · дальние объекты обрезаются";UiRevision++;
    }
    internal void RestoreDistance()
    {
        if(!_distanceOn)return;
        foreach(var camera in UnityEngine.Object.FindObjectsOfType<Camera>())
            if(camera!=null&&camera.GetInstanceID()==_distanceCamera)
            {if(Mathf.Abs(camera.farClipPlane-_distanceExpected)<.1f)camera.farClipPlane=_distanceBefore;else _conflicts++;break;}
        _distanceOn=false;_distanceCamera=0;DistanceStatus="Полная дальность 3D";UiRevision++;
    }
    internal void Reset()=>Restore(false);
    internal void Restore(bool notify=true)
    {
        if(!_snapshot&&!GeometryActive&&!DistanceActive&&!MaximumActive&&!Runtime.Active&&!BenchmarkCapActive)return;
        UiRevision++;
        var errors=new List<string>();
        void Attempt(string label,Action action)
        {try{action();}catch(Exception e){errors.Add(label+": "+e.Message);_app.Fail("optimization restore "+label,e);}}
        // Geometry owns an independent lease; always clean it even if ordinary
        // graphics were never applied. Failure must survive the reset below.
        Attempt("FPS cap",RestoreBenchmarkCap);
        Attempt("runtime",Runtime.Restore);
        Attempt("maximum",_maximum.Restore);
        Attempt("distance",RestoreDistance);
        Attempt("geometry",()=>RestoreGeometry(false));
        if(!_snapshot)
        {
            LastRestoreConflicts=_conflicts+_geometry.Conflicts;
            Status=GeometryStatus;
            if(notify&&errors.Count==0)_app.Notify(Status);
            return;
        }
        Current=Current with{FoliagePercent=100};
        Attempt("particles",RestoreParticles);Attempt("render",RestoreRender);Attempt("streaming",RestoreSmooth);
        Attempt("mip",()=>{
            if(_mipOwned)
            {
                if(QualitySettings.globalTextureMipmapLimit==_mipExpected)
                    QualitySettings.globalTextureMipmapLimit=_mipBefore;
                else _conflicts++;
            }
            foreach(var state in _uiTextures.Values)
                if(Live(state.Asset))
                {if(state.Asset.ignoreMipmapLimit)state.Asset.ignoreMipmapLimit=state.Original;else _conflicts++;}
        });
        _uiTextures.Clear();_particles.Clear();
        _snapshot=_mipOwned=_smoothOwned=false;
        Current=OptimizationRequest.Full;_sceneCount=_activeScene=-1;_sceneStamp=0;
        LastRestoreConflicts=_conflicts+_geometry.Conflicts;
        Status=errors.Count>0?"Не все настройки восстановлены: "+string.Join("; ",errors):
            _conflicts>0?"Восстановлено; сторонние изменения не перезаписаны: "+_conflicts:
            "Исходные настройки восстановлены · модуль выключен";
        Status+=" · "+GeometryStatus;
        if(notify&&errors.Count==0)_app.Notify(Status);
    }
    internal string Diagnostics()=>
        "OptimizationActive: "+Active+"\nOptimizationRenderSupported: "+RenderSupported+
        "\nOptimizationRenderReason: "+RenderReason+"\nOptimizationFoliageSupported: "+FoliageSupported+
        "\nOptimizationFoliageReason: "+FoliageReason+"\nOptimizationMip: "+QualitySettings.globalTextureMipmapLimit+
        "\nOptimizationRenderRequested: "+Current.RenderPercent+"\nOptimizationRenderActual: "+ActualRenderPercent+
        "\nOptimizationRenderSize: "+ActualRenderSize+"\nOptimizationSmooth: "+SmoothActive+
        "\nOptimizationActorBatch: "+WorldPopulator.ACTORS_PER_FRAME+
        "\nOptimizationOtherBatch: "+WorldPopulator.NON_ACTORS_PER_FRAME+
        "\nOptimizationNativeModelBatchesSupported: "+SupportsNativeBatches+
        "\nOptimizationUiTextureRoots: "+UiTextureRoots+"\nOptimizationCameraSnapshots: "+CameraSnapshots+
        "\nOptimizationParticleSnapshots: "+ParticleSnapshots+"\nOptimizationFoliageManagers: "+FoliageManagers+
        "\nOptimizationFoliageDraws: "+FoliageDraws+"\nOptimizationDiscoveryScans: "+DiscoveryScans+
        "\nOptimizationRestoreConflicts: "+LastRestoreConflicts+"\n"+GeometryDiagnostics()+"\n"+MaximumDiagnostics()+"\n"+Runtime.Diagnostics();
}