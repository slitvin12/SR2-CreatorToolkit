using System;
using System.IO;
using System.Linq;
using System.Globalization;
using CreatorToolkit;
internal static class M18Regression
{
    internal static void Run(Action<bool,string> report,string root)
    {
        void Check(bool ok,string name)=>report(ok,"m15 / followup / m18 / "+name);
        foreach(string name in new[]{"ScreenSpaceAmbientOcclusion","ScreenSpaceReflection","ScreenSpaceGlobalIllumination","VolumetricClouds","ContactShadows","MicroShadowing","MotionBlur","DepthOfField","Bloom"})Check(M18Policy.HeavyEffect(name),"explicit visual effect whitelist: "+name);
        foreach(string name in new[]{"Fog","VisualEnvironment","ColorAdjustments","Exposure","Tonemapping","Slime","Damage","",null})Check(!M18Policy.HeavyEffect(name),"lighting/gameplay/unidentified component retained: "+name);
        foreach(float original in new[]{float.Epsilon,.0001f,.1f,.75f,1f,2f,10f})Check(M18Policy.LowLod(original)>0&&M18Policy.LowLod(original)<=original,"aggressive LOD never increases original: "+original);
        foreach(float original in new[]{10f,90f,180f,4000f})Check(M18Policy.FarClip(original,.3f)<=original&&M18Policy.FarClip(original,.3f)>.3f,"90m far clip bounded to existing camera: "+original);
        foreach(float bad in new[]{float.NaN,float.PositiveInfinity,-1f,0f})
        {bool rejected=false;try{M18Policy.LowLod(bad);}catch(ArgumentOutOfRangeException){rejected=true;}Check(rejected,"invalid native LOD rejected: "+bad);}
        string Read(string file)=>File.ReadAllText(Path.Combine(root,"src","creator",file));
        var rows=Read("GhostDroneIndex.tsv").Split('\n',StringSplitOptions.RemoveEmptyEntries);
        Check(rows[0].StartsWith("#catalog="),"ghost routes version gated");
        var ghosts=rows.Skip(1).Select(row=>row.TrimEnd('\r').Split('\t')).ToArray();
        Check(ghosts.Length==12&&ghosts.All(c=>c.Length==6&&c[5].Length>0),"all twelve exact native spawners recorded (M29: with their authored zone)");
        Check(ghosts.Select(c=>c[0]).Distinct().Count()==12,"no duplicate phantom route IDs");
        Check(ghosts.All(c=>c[1].StartsWith("Assets/Scene/")&&c.Skip(2).Take(3).All(v=>double.IsFinite(double.Parse(v,CultureInfo.InvariantCulture)))),"finite world transforms and scene provenance for every route");
        string render=Read("M18RenderTools.cs"),map=Read("MapTools.cs"),player=Read("M18PlayerTools.cs"),ui=Read("ToolkitUi.cs"),opt=Read("OptimizationTools.cs");
        Check(render.Contains("deco.enabled=false")&&render.Contains("deco.useGPUInstancing")&&!render.Contains("DrawDecoMeshes("),"instanced decor stops exact native component without draw hook");
        Check(render.Contains("light.EnableShadows(false)")&&render.Contains("light.ShadowsEnabled()"),"shadow changes use dedicated HDRP native API and readback");
        Check(render.Contains("profile=volume.profile;ProfileInstantiations++")&&render.Contains("volume.sharedProfile")&&render.Contains("effect.active=false"),"post effects edit per-volume runtime profile, not shared asset");
        Check(!render.Contains("currentPlatformRenderPipelineSettings")&&!render.Contains("ACTORS_PER_FRAME")&&!render.Contains("SetResolution("),"maximum profile avoids known unsafe structures, batch fields and resolution");
        Check(opt.Contains("if(!MaximumActive)_geometry.Observe()")&&opt.Contains("if(MaximumActive)_maximum.Restore()"),"balanced and maximum LOD leases never fight");
        Check(opt.Contains("Attempt(\"maximum\",_maximum.Restore)")&&render.Contains("_cameras.Clear()"),"maximum profile participates in normal restore and retires state");
        Check(player.Contains("player.SceneGroup.Pointer!=_checkpointGroup")&&player.Contains("_checkpointSession==_app.Session")&&player.Contains("IsClearPosition(ground)"),"checkpoint return revalidates session, scene and capsule clearance");
        Check(!player.Contains("SaveGame(")&&!player.Contains("File.Write"),"temporary checkpoint never persists a save");
        Check(ui.Contains("private void SpawnTiles")&&ui.Contains("sidebar=168")&&ui.Contains("Разместить Гордо")&&ui.Contains("int count=gordo?1:_quantity"),"tile catalog and unambiguous one-Gordo action");
        Check(map.Contains("ResetGhostRouteStates()")&&map.Contains("drone.SpawnerId")&&map.Contains("if(route!=null){route.Position=go.transform.position"),"live drone reconciles to its exact static route without duplicate marker");
        Check(render.Contains("_next=Time.unscaledTime+30f;")&&render.Contains("if(stamp!=_sceneStamp)")&&render.Contains("DiscoverPhase(_phase++)"),"steady maximum avoids frequent full-scene census; scene changes rescan once settled");
        string actors=Read("ActorTools.cs");
        Check(actors.Contains("_app.Context.GadgetDirector.GetAllGadgets()")&&actors.Contains("GetComponentInChildren<GordoSnare>(true)"),"Gordo uses native gadget registry and exact prefab component, not actor lookup");
    }
}
