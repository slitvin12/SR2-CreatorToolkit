using System;
using System.IO;
using CreatorToolkit;
internal static class M19GraphicsRegression
{
    internal static void Run(Action<bool,string> report,string root)
    {
        void Check(bool ok,string name)=>report(ok,"m15 / followup / m19 / "+name);
        var full=M19GraphicsRequest.Full;Check(!full.Any,"default draft does not claim a native graphics lease");
        var clamped=new M19GraphicsRequest(-99,99999,true,true,(M19Effects)int.MaxValue).Clamped();
        Check(clamped.LodPercent==10&&clamped.Distance==1000&&clamped.DisabledEffects==M19Effects.All,"request bounds and unknown flag bits clamped");
        Check((new M19GraphicsRequest(999,-9,false,false,0)).Clamped()==full,"disabled distance and full geometry canonical defaults");
        string[] names={"ScreenSpaceAmbientOcclusion","ScreenSpaceReflection","ScreenSpaceGlobalIllumination","VolumetricClouds","ContactShadows","MicroShadowing","MotionBlur","DepthOfField","Bloom"};
        for(int i=0;i<names.Length;i++)
        {
            var bit=(M19Effects)(1<<i);Check(M19GraphicsPolicy.Effect(names[i])==bit,"exact native effect identity "+names[i]);
            bool independent=true;for(int j=0;j<names.Length;j++)independent&=M19GraphicsPolicy.Disable(names[j],bit)==(i==j);
            Check(independent,"one effect cannot disable a different component "+names[i]);
        }
        foreach(string name in new[]{"Fog","Sky","Exposure","VisualEnvironment","ColorAdjustments","Tonemapping","BloomFake","",null})Check(!M19GraphicsPolicy.Disable(name,M19Effects.All),"non-whitelisted native state retained: "+name);
        foreach(float before in new[]{float.Epsilon,.00001f,.1f,.75f,1f,2f,100f,float.MaxValue})
        {
            foreach(int percent in new[]{-5,10,25,50,75,100,999}){float v=M19GraphicsPolicy.Lod(before,percent);Check(float.IsFinite(v)&&v>0&&v<=before,"custom LOD scalar finite/positive/no increase "+before+" / "+percent);}
            Check(M19GraphicsPolicy.Lod(before,100)==before,"100% keeps exact original LOD "+before);
        }
        foreach(float bad in new[]{0f,-1f,float.NaN,float.PositiveInfinity}){bool fail=false;try{M19GraphicsPolicy.Lod(bad,50);}catch(ArgumentOutOfRangeException){fail=true;}Check(fail,"invalid baseline LOD rejected "+bad);}
        foreach(int distance in new[]{0,40,90,180,1000,99999})
        {float v=M19GraphicsPolicy.FarClip(4000,.3f,distance);Check(v>.3f&&v<=4000,"bounded camera range "+distance);Check(M19GraphicsPolicy.FarClip(20,.3f,distance)==20,"custom distance never extends short native camera "+distance);}
        bool masks=true;for(int mask=0;mask<=511;mask++)for(int i=0;i<9;i++)masks&=M19GraphicsPolicy.Disable(names[i],(M19Effects)mask)==((mask&(1<<i))!=0);Check(masks,"all 512 effect-mask combinations keep exact component isolation");
        string Read(string n)=>File.ReadAllText(Path.Combine(root,"src","creator",n));
        string render=Read("M18RenderTools.cs"),tools=Read("OptimizationTools.cs"),ui=Read("OptimizationUi.cs"),detail=Read("M19GraphicsDetailsUi.cs");
        Check(render.Contains("request=request.Clamped()")&&render.Contains("Restore();_request=request")&&render.Contains("if(!request.Any)return"),"custom reapply restores previous lease before capture; full retires it");
        Check(render.Contains("M20DiscoveryPolicy.PhaseNeeded(_request,phase)")&&Read("M20DiscoveryPolicy.cs").Contains("0=>r.HideDecor")&&Read("M20DiscoveryPolicy.cs").Contains("1=>r.DisableShadows")&&render.Contains("(_request.DisabledEffects&bit)==0")&&Read("M20DiscoveryPolicy.cs").Contains("3=>r.Distance>0"),"every renderer category has an independent request gate");
        Check(tools.Contains("_maximum.Restore();RestoreDistance();RestoreGeometry(false)")&&tools.Contains("_maximum.Apply(request)"),"custom controls do not compound legacy LOD/camera leases");
        Check(ui.Contains("_app.Queue(\"detailed graphics\"")&&ui.Contains("_app.Optimization.ApplyDetailed(details)")&&!ui.Contains("QualitySettings.lodBias"),"explicit custom Apply queues native writes off OnGUI");
        Check(detail.Contains("Slider(74")&&detail.Contains("Slider(75")&&detail.Contains("GraphicsEffectBits.Length")&&detail.Contains("GUI.enabled=geometryEnabled&&geometrySupported"),"detailed LOD/distance and nine independent effects exposed with support gate");
        Check(!render.Contains("sharedProfile=")&&!render.Contains("SetResolution(")&&!render.Contains("currentPlatformRenderPipelineSettings")&&!detail.Contains("Slider(71")&&!detail.Contains("Slider(72"),"custom UI does not revive unsafe render-scale/foliage hooks");
    }
}
