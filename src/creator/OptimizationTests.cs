using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2Cpp;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace CreatorToolkit;

internal sealed partial class FeatureSuite
{
    private sealed class GraphicsBaseline
    {
        internal int Mip,Upload,Renderers,Io,Actors,Other,Width,Height,Fps,Vsync,Particle;
        internal UnityEngine.ThreadPriority Priority;
        internal float TimeScale,FixedDelta;
        internal bool SolveMovement,SolveGrounding,CameraDr,HdDr;
        internal bool DrEnabled,DrForced;
        internal float DrPercentage,DrMin,DrMax;
        internal DynamicResolutionType DrType;
        internal int NativeAmbientCandidates,NativeFoliageCandidates,WorldMipCandidates;
    }
    private GraphicsBaseline _graphicsBefore;
    private GameObject _ambientFixture;
    private readonly List<object> _graphicsProof=new();
    private void CaptureGraphics(string name)=>ScreenCapture.CaptureScreenshot(
        Path.Combine(_app.DataDirectory,"v060-optimization-"+name+".png"));
    private static string ColliderFingerprint()=>string.Join(";",UnityEngine.Object.FindObjectsOfType<Collider>()
        .OrderBy(c=>c.GetInstanceID()).Select(c=>c.GetInstanceID()+":"+c.enabled+":"+c.isTrigger));
    private void WriteGraphicsProof()=>File.WriteAllText(Path.Combine(_app.DataDirectory,"optimization-proof.json"),
        JsonSerializer.Serialize(new{Version=ToolkitMod.Version,CopyOnly=true,Save=_recovery?.TestName,
            Scope="Native API and rendered-size readback on owned copy. Ambient ParticleSystem fixture is explicitly synthetic; native candidates are counted separately. No dedicated VRAM or weak-GPU performance certificate.",
            Rows=_graphicsProof},new JsonSerializerOptions{WriteIndented=true}));
    private void GraphicsRow(string label)
    {
        var opt=_app.Optimization;var camera=Camera.main;
        var handler=camera!=null?DynamicResolutionHandler.GetOrCreateDrsInstanceHandler(camera):null;
        var size=handler?.GetLastScaledSize()??Vector2Int.zero;
        _graphicsProof.Add(new{Label=label,Active=opt.Active,Mip=QualitySettings.globalTextureMipmapLimit,
            ScreenWidth=Screen.width,ScreenHeight=Screen.height,Requested3D=opt.Current.RenderPercent,
            NativeResolvedScale=handler?.GetCurrentScale(),NativeResolvedVector=handler?.GetResolvedScale().ToString("F4"),
            NativeScaledWidth=size.x,NativeScaledHeight=size.y,
            CameraDynamic=camera!=null&&camera.allowDynamicResolution,
            HdCameraDynamic=camera?.GetComponent<HDAdditionalCameraData>()?.allowDynamicResolution,
            UploadMs=QualitySettings.asyncUploadTimeSlice,BackgroundPriority=Application.backgroundLoadingPriority.ToString(),
            ActorsPerFrame=WorldPopulator.ACTORS_PER_FRAME,OtherPerFrame=WorldPopulator.NON_ACTORS_PER_FRAME,
            opt.FoliageManagers,opt.FoliageDraws,opt.LastFoliageBefore,opt.LastFoliageAfter,
            opt.EligibleParticles,opt.ParticleSnapshots,opt.UiTextureRoots,opt.CameraSnapshots,opt.DiscoveryScans,
            opt.RenderSupported,opt.RenderReason,opt.LastRestoreConflicts,opt.StreamingBudgetApplications,
            FixtureMaxParticles=_ambientFixture?.GetComponent<ParticleSystem>()?.main.maxParticles,
            NativeAmbientCandidates=_graphicsBefore?.NativeAmbientCandidates,
            NativeFoliageCandidates=_graphicsBefore?.NativeFoliageCandidates,
            NativeWorldMipCandidates=_graphicsBefore?.WorldMipCandidates});
        WriteGraphicsProof();
    }
    private void BuildOptimizationSteps()
    {
        Add("Optimization / copy guard and exact native baseline",()=>{
            GuardCopy();_app.Optimization.Reset();_graphicsProof.Clear();
            var camera=Camera.main;var asset=GraphicsSettings.currentRenderPipeline?.TryCast<HDRenderPipelineAsset>();
            Require(camera!=null&&asset!=null&&camera.GetComponent<HDAdditionalCameraData>()!=null,
                "The actual native HDRP camera/asset is missing.");
            var dr=asset.currentPlatformRenderPipelineSettings.dynamicResolutionSettings;
            _graphicsBefore=new GraphicsBaseline{
                Mip=QualitySettings.globalTextureMipmapLimit,Upload=QualitySettings.asyncUploadTimeSlice,
                Renderers=QualitySettings.streamingMipmapsRenderersPerFrame,Io=QualitySettings.streamingMipmapsMaxFileIORequests,
                Actors=WorldPopulator.ACTORS_PER_FRAME,Other=WorldPopulator.NON_ACTORS_PER_FRAME,
                Width=Screen.width,Height=Screen.height,Fps=Application.targetFrameRate,Vsync=QualitySettings.vSyncCount,
                Priority=Application.backgroundLoadingPriority,TimeScale=Time.timeScale,FixedDelta=Time.fixedDeltaTime,
                SolveMovement=Controller.Motor._solveMovementCollisions,SolveGrounding=Controller.Motor._solveGrounding,
                CameraDr=camera.allowDynamicResolution,HdDr=camera.GetComponent<HDAdditionalCameraData>().allowDynamicResolution,
                DrEnabled=dr.enabled,DrForced=dr.forceResolution,DrPercentage=dr.forcedPercentage,
                DrMin=dr.minPercentage,DrMax=dr.maxPercentage,DrType=dr.dynResType,Particle=200};
            var ambientNames=new List<string>();var foliageNames=new List<string>();
            foreach(var ps in UnityEngine.Object.FindObjectsOfType<ParticleSystem>())
                if(NativeParticleSafety.TryRead(ps,out bool collision,out bool trigger)&&
                    OptimizationPolicy.AmbientParticle(ps.name+" "+ps.transform.parent?.name,collision,trigger))
                {_graphicsBefore.NativeAmbientCandidates++;if(ambientNames.Count<32)ambientNames.Add(ps.name);}
            foreach(var manager in UnityEngine.Object.FindObjectsOfType<DecoInstanceManager>())
            {
                string label=manager.name+" "+manager.decoMesh?.name+" "+manager.decoMaterial?.name;
                if(OptimizationPolicy.Foliage(label,manager.useGPUInstancing,manager.initialized))
                {_graphicsBefore.NativeFoliageCandidates++;if(foliageNames.Count<32)foliageNames.Add(label);}
            }
            foreach(var texture in Resources.FindObjectsOfTypeAll<Texture2D>())
                if(texture!=null&&!texture.WasCollected&&texture.mipmapCount>4&&!texture.ignoreMipmapLimit)
                    _graphicsBefore.WorldMipCandidates++;
            var uiTextures=new Dictionary<int,object>();
            _app.Optimization.VisitLoadedUiTextures(texture=>{
                if(texture==null||texture.WasCollected)return;
                uiTextures.TryAdd(texture.GetInstanceID(),new{texture.name,texture.width,texture.height,
                    Mips=texture.mipmapCount,Readable=texture.isReadable,Ignores=texture.ignoreMipmapLimit,
                    ActiveMip=texture.activeMipmapLimit,Group=texture.mipmapLimitGroup});
            });
            File.WriteAllText(Path.Combine(_app.DataDirectory,"optimization-ui-textures.json"),
                JsonSerializer.Serialize(new{CopyOnly=true,Rows=uiTextures.Values},new JsonSerializerOptions{WriteIndented=true}));
            _app.LoggerInstance.Msg("Native optimization census: ambient="+_graphicsBefore.NativeAmbientCandidates+
                " foliage="+_graphicsBefore.NativeFoliageCandidates+" mipmappedTextures="+_graphicsBefore.WorldMipCandidates+
                " ambientNames="+string.Join("|",ambientNames)+" foliageNames="+string.Join("|",foliageNames));
            Require(_graphicsBefore.Actors>0&&_graphicsBefore.Other>0,"Native population budgets are not positive.");
            _ambientFixture=new GameObject("CreatorToolkitAmbientDustAudit");
            var fixture=_ambientFixture.AddComponent<ParticleSystem>();var main=fixture.main;
            main.maxParticles=_graphicsBefore.Particle;main.playOnAwake=false;fixture.Stop();
            _app.UiForTests.ShowOptimizationForTest();_app.Input.Open();GraphicsRow("baseline");
        },.4f);
        Add("Optimization / native particle collision and trigger safety gates",()=>{
            GuardCopy();var system=_ambientFixture.GetComponent<ParticleSystem>();
            Require(NativeParticleSafety.Supported,"Actual engine particle safety bindings are unavailable; native particles must remain untouched.");
            try
            {
                NativeParticleSafety.SetOwnedFixture(system,true,false);
                Require(NativeParticleSafety.TryRead(system,out bool collision,out bool trigger)&&collision&&!trigger&&
                    !OptimizationPolicy.AmbientParticle(system.name,collision,trigger),"Collision-enabled particles were not rejected.");
                NativeParticleSafety.SetOwnedFixture(system,false,true);
                Require(NativeParticleSafety.TryRead(system,out collision,out trigger)&&!collision&&trigger&&
                    !OptimizationPolicy.AmbientParticle(system.name,collision,trigger),"Trigger-enabled particles were not rejected.");
            }
            finally{NativeParticleSafety.SetOwnedFixture(system,false,false);}
            Require(NativeParticleSafety.TryRead(system,out bool c,out bool t)&&!c&&!t,
                "Owned fixture particle modules did not return to disabled.");
        });
        Add("Optimization / default-off native page capture",()=>{
            GuardCopy();Require(!_app.Optimization.Active&&_app.Optimization.NativeActorRoots==0,
                "Optimization must start off without world-object roots.");
            Require(_app.UiForTests.OptimizationVisibleForTest,"Optimization page did not render.");
            Require(_app.UiForTests.CareHeadersFitForTest,"Long care table header still clips.");
            CaptureGraphics("default");
        },.25f);
        for(int mip=0;mip<=4;mip++)
        {
            int level=mip;
            Add("Optimization / actual mip level "+level,()=>{
                GuardCopy();string colliders=ColliderFingerprint();
                _app.Optimization.Apply(new OptimizationRequest(level,100,100,100,false));
                Require(QualitySettings.globalTextureMipmapLimit==level,"Native global mip setter/readback differs.");
                Require(ColliderFingerprint()==colliders,"Graphics apply changed native colliders or their enabled/trigger state.");
                Require(Screen.width==_graphicsBefore.Width&&Screen.height==_graphicsBefore.Height,
                    "Mipmap setting changed output resolution.");
                GraphicsRow("mip_"+level);
            },.25f);
        }
        Add("Optimization / genuine texture exemptions and no blanket 64x64",()=>{
            GuardCopy();Require(_graphicsBefore.WorldMipCandidates>0,"No genuine native mipmapped textures in the copy.");
            int checkedWorld=0,checkedUi=0;
            foreach(var texture in Resources.FindObjectsOfTypeAll<Texture2D>())
                if(texture!=null&&!texture.WasCollected&&texture.mipmapCount>4&&!texture.ignoreMipmapLimit)
                {Require(texture.activeMipmapLimit>=0,"Native mip readback is invalid.");checkedWorld++;}
            _app.Optimization.VisitLoadedUiTextures(texture=>{
                if(texture!=null&&!texture.WasCollected&&texture.mipmapCount>1)
                {Require(texture.ignoreMipmapLimit,"An actually used native UI texture was not protected: "+texture.name);checkedUi++;}
            });
            Require(checkedWorld>0&&_app.Optimization.UiTextureRoots<=OptimizationPolicy.UiTextureBudget,
                "Native world mip coverage or bounded UI protection failed.");
            _app.LoggerInstance.Msg("Native mip proof: world="+checkedWorld+" mipmapped UI sprite refs="+checkedUi);
        });
        Add("Optimization / one-click profile and native population caps",()=>{
            GuardCopy();string colliders=ColliderFingerprint();_app.Optimization.ApplyPotato();
            var opt=_app.Optimization;
            Require(opt.Active&&opt.Current.Mip==3&&opt.SmoothActive,"Lightweight profile did not apply.");
            Require(WorldPopulator.ACTORS_PER_FRAME==Math.Min(_graphicsBefore.Actors,OptimizationPolicy.ActorBatch)&&
                WorldPopulator.NON_ACTORS_PER_FRAME==Math.Min(_graphicsBefore.Other,OptimizationPolicy.OtherBatch),
                "Actual native model population budgets were not lowered without increasing originals.");
            Require(QualitySettings.asyncUploadTimeSlice<=OptimizationPolicy.UploadMs,
                "Actual async upload time budget was not applied.");
            Require(_ambientFixture.GetComponent<ParticleSystem>().main.maxParticles==
                OptimizationPolicy.ParticleCap(_graphicsBefore.Particle,50),"Explicit native ambient fixture was not capped.");
            Require(ColliderFingerprint()==colliders&&Controller.Motor._solveMovementCollisions==_graphicsBefore.SolveMovement&&
                Controller.Motor._solveGrounding==_graphicsBefore.SolveGrounding&&Time.timeScale==_graphicsBefore.TimeScale&&
                Time.fixedDeltaTime==_graphicsBefore.FixedDelta,"Optimization changed physics or collision behavior.");
        },2.5f);
        Add("Optimization / actual 70 percent HDRP and native-size UI",()=>{
            GuardCopy();var opt=_app.Optimization;var main=Camera.main;
            Require(opt.RenderSupported&&opt.Current.RenderPercent==70,"Native HDRP scale unavailable: "+opt.RenderReason);
            var handler=DynamicResolutionHandler.GetOrCreateDrsInstanceHandler(main);
            float scale=handler.GetCurrentScale();
            var size=handler.GetLastScaledSize();
            Require(Math.Abs(scale-.70f)<.015f&&size.x>0&&size.y>0&&
                size.x<Screen.width&&size.y<Screen.height,
                "Requested 70% is not actual native reduced 3D resolution: "+scale+" / "+size);
            Require(Screen.width==_graphicsBefore.Width&&Screen.height==_graphicsBefore.Height&&
                Application.targetFrameRate==_graphicsBefore.Fps&&QualitySettings.vSyncCount==_graphicsBefore.Vsync,
                "Render scaling changed screen size, cap or VSync.");
            foreach(var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
                if(canvas.enabled&&canvas.renderMode==RenderMode.ScreenSpaceCamera&&canvas.worldCamera!=null)
                    Require(canvas.worldCamera!=main&&!canvas.worldCamera.allowDynamicResolution,
                        "A screen-space UI camera is being downscaled.");
            Require(opt.ParticleSnapshots<=OptimizationPolicy.ParticleBudget&&opt.CameraSnapshots<=OptimizationPolicy.CameraBudget&&
                opt.FoliageManagers<=OptimizationPolicy.FoliageBudget&&opt.NativeActorRoots==0,
                "Optimization caches are not bounded value snapshots.");
            if(_graphicsBefore.NativeFoliageCandidates>0)
                Require(opt.FoliageDraws>0&&opt.LastFoliageAfter<=opt.LastFoliageBefore*.401f,
                    "Actual native instanced foliage draw arguments were not reduced.");
            _app.UiForTests.ShowOptimizationForTest();GraphicsRow("potato_70");CaptureGraphics("potato");
        },.25f);
        Add("Optimization / actual render slider minimum",()=>{
            GuardCopy();var current=_app.Optimization.Current;
            _app.Optimization.Apply(current with{RenderPercent=50});
        },2.5f);
        Add("Optimization / actual 50 percent readback and capture",()=>{
            GuardCopy();var handler=DynamicResolutionHandler.GetOrCreateDrsInstanceHandler(Camera.main);
            Require(Math.Abs(handler.GetCurrentScale()-.50f)<.015f,"Native HDRP minimum is not 50%.");
            _app.UiForTests.ShowOptimizationForTest();GraphicsRow("render_50");CaptureGraphics("50");
        },.25f);
        Add("Optimization / exact full restoration, including native fixture",()=>{
            GuardCopy();_app.Optimization.Restore(false);
            Require(!_app.Optimization.Active&&_app.Optimization.UiTextureRoots==0&&
                _app.Optimization.ParticleSnapshots==0&&_app.Optimization.CameraSnapshots==0&&
                _app.Optimization.FoliageManagers==0,"Module retained native roots after restoration.");
            Require(_app.Optimization.LastRestoreConflicts==0,"Restoration encountered unexpected external settings.");
            Require(QualitySettings.globalTextureMipmapLimit==_graphicsBefore.Mip&&
                QualitySettings.asyncUploadTimeSlice==_graphicsBefore.Upload&&
                QualitySettings.streamingMipmapsRenderersPerFrame==_graphicsBefore.Renderers&&
                QualitySettings.streamingMipmapsMaxFileIORequests==_graphicsBefore.Io&&
                Application.backgroundLoadingPriority==_graphicsBefore.Priority&&
                WorldPopulator.ACTORS_PER_FRAME==_graphicsBefore.Actors&&
                WorldPopulator.NON_ACTORS_PER_FRAME==_graphicsBefore.Other,"Native quality/loading values did not exactly restore.");
            var asset=GraphicsSettings.currentRenderPipeline.TryCast<HDRenderPipelineAsset>();
            var dr=asset.currentPlatformRenderPipelineSettings.dynamicResolutionSettings;
            Require(dr.enabled==_graphicsBefore.DrEnabled&&dr.forceResolution==_graphicsBefore.DrForced&&
                dr.forcedPercentage==_graphicsBefore.DrPercentage&&dr.minPercentage==_graphicsBefore.DrMin&&
                dr.maxPercentage==_graphicsBefore.DrMax&&dr.dynResType==_graphicsBefore.DrType,
                "Owned HDRP asset fields did not exactly restore.");
            Require(Camera.main.allowDynamicResolution==_graphicsBefore.CameraDr&&
                Camera.main.GetComponent<HDAdditionalCameraData>().allowDynamicResolution==_graphicsBefore.HdDr,
                "Native camera flags did not restore.");
            Require(_ambientFixture.GetComponent<ParticleSystem>().main.maxParticles==_graphicsBefore.Particle,
                "Explicit native ambient fixture capacity did not restore.");
            _app.UiForTests.ShowOptimizationForTest();GraphicsRow("restored");
        },.4f);
        Add("Optimization / restored page and fixture cleanup",()=>{
            GuardCopy();CaptureGraphics("restored");RetireOptimizationFixtures();_app.CloseMenu();
        },.25f);
        Add("Optimization / repeated reversible profile cycles",()=>{
            GuardCopy();
            for(int i=0;i<3;i++)
            {
                _app.Optimization.ApplyPotato();Require(_app.Optimization.Active,"Repeated profile failed to arm.");
                _app.Optimization.Reset();
                Require(!_app.Optimization.Active&&_app.Optimization.UiTextureRoots==0&&
                    _app.Optimization.LastRestoreConflicts==0&&QualitySettings.globalTextureMipmapLimit==_graphicsBefore.Mip,
                    "Repeated profile failed exact restoration.");
            }
            GraphicsRow("repeated_restore");
        });
    }
    private void RetireOptimizationFixtures()
    {
        _app.Optimization.Reset();
        if(_ambientFixture!=null&&!_ambientFixture.WasCollected)UnityEngine.Object.Destroy(_ambientFixture);
        _ambientFixture=null;
    }
}