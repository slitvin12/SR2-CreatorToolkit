using System;
using System.IO;
using CreatorToolkit;
internal static class M20Regression
{
    internal static void Run(Action<bool,string> report,string root)
    {
        void Check(bool value,string label)=>report(value,"m15 / followup / m20 / "+label);
        for(int mask=0;mask<32;mask++)
        {
            bool decor=(mask&1)!=0,shadows=(mask&2)!=0,effects=(mask&4)!=0,distance=(mask&8)!=0,lod=(mask&16)!=0;
            var r=new M19GraphicsRequest(lod?50:100,distance?180:0,decor,shadows,effects?M19Effects.MotionBlur:M19Effects.None);
            Check(M20DiscoveryPolicy.PhaseNeeded(r,0)==decor&&M20DiscoveryPolicy.PhaseNeeded(r,1)==shadows&&
                M20DiscoveryPolicy.PhaseNeeded(r,2)==effects&&M20DiscoveryPolicy.PhaseNeeded(r,3)==distance&&
                M20DiscoveryPolicy.NeedsPeriodic(r)==(decor||shadows||effects||distance),"independent discovery plan "+mask);
        }
        Check(!M20DiscoveryPolicy.PhaseNeeded(M19GraphicsRequest.Maximum,-1)&&!M20DiscoveryPolicy.PhaseNeeded(M19GraphicsRequest.Maximum,4),"invalid phases never enumerate");
        bool allMasks=true;
        for(int mask=0;mask<512;mask++)
        {
            var r=M19GraphicsRequest.Full with{DisabledEffects=(M19Effects)mask};
            allMasks&=M20DiscoveryPolicy.PhaseNeeded(r,2)==(mask!=0)&&!M20DiscoveryPolicy.PhaseNeeded(r,0)&&!M20DiscoveryPolicy.PhaseNeeded(r,1)&&!M20DiscoveryPolicy.PhaseNeeded(r,3);
        }
        Check(allMasks,"all 512 effect masks route only to volume census");
        foreach(bool instantiated in new[]{false,true})foreach(bool requested in new[]{false,true})
            Check(M20DiscoveryPolicy.CloneNeeded(instantiated,requested)==(!instantiated&&requested),"runtime profile creation gate "+instantiated+" / "+requested);
        ulong a=M20DiscoveryPolicy.AppendScene(M20DiscoveryPolicy.AppendScene(14695981039346656037UL,7),9);
        ulong b=M20DiscoveryPolicy.AppendScene(M20DiscoveryPolicy.AppendScene(14695981039346656037UL,7),10);
        Check(a!=b,"same-count streamed scene swap changes fingerprint");
        Check(a==M20DiscoveryPolicy.AppendScene(M20DiscoveryPolicy.AppendScene(14695981039346656037UL,7),9),"stable scene fingerprint is deterministic");
        string Read(string n)=>File.ReadAllText(Path.Combine(root,"src","creator",n));
        string render=Read("M18RenderTools.cs"),prof=Read("M20ModProfiler.cs"),mod=Read("ToolkitMod.cs");
        Check(mod.Contains("new System.Text.StringBuilder(16384)")&&!mod.Contains("details +=")&&!mod.Contains("details+="),"status builder avoids repeated growing-string copies");
        Check(mod.Contains("details.Clear().Append(\"DiagnosticError: \"")&&mod.Contains(" + details.ToString();"),"status error reset and final string publication preserved");
        Check(Read("WorldTools.cs").Contains("weather-zone-resolve")&&Read("WorldTools.cs").Contains("weather-active-read"),"native weather resolve and active-state reads measured separately");
        Check(render.Contains("if(!Active||!M20DiscoveryPolicy.NeedsPeriodic(_request))return")&&render.Contains("if(!M20DiscoveryPolicy.PhaseNeeded(_request,phase))return"),"LOD-only bypasses periodic census and every apply phase");
        Check(render.Contains("if(_decor.Count>0)Attempt")&&render.Contains("if(_lights.Count>0)Attempt")&&render.Contains("if(_effects.Count>0)Attempt")&&render.Contains("if(_cameras.Count>0)Attempt"),"empty ledgers never trigger global restore scans");
        Check(render.Contains("instantiated?volume.profile:volume.sharedProfile")&&render.Contains("if(!requestedActive)continue")&&render.Contains("M20DiscoveryPolicy.CloneNeeded(instantiated,requestedActive)")&&!render.Contains("sharedProfile="),"shared assets inspected read-only; copies only for requested active effects");
        Check(render.Contains("private readonly HashSet<int> _alive=new()")&&render.Contains("private readonly List<int> _dead=new()")&&render.Contains("_alive.Clear()")&&render.Contains("_dead.Clear()"),"periodic decor/light scratch buffers reused");
        Check(render.Contains("DiscoverPhase(_phase++)")&&render.Contains("_nextSceneCheck=now+1f")&&render.Contains("_next=now+30f"),"one native category per background tick with bounded census frequency");
        Check(render.Contains("for(int p=0;p<M20DiscoveryPolicy.PhaseCount;p++)DiscoverPhase(p)")&&render.Contains("catch{Restore();throw;}"),"explicit apply synchronous and exceptions restore leases");
        Check(prof.Contains(">=20")&&prof.Contains("_sections.Count>=48")&&prof.Contains("Task.Run")&&prof.Contains("File.WriteAllText(path,json)"),"passive profiler duration and state bounded; filesystem off main thread");
        Check(!prof.Contains("GC.Collect(")&&!prof.Contains("QueueEvent(")&&!prof.Contains("targetFrameRate=")&&!prof.Contains("SaveGame(")&&!prof.Contains("QualitySettings."),"profiler never changes input, caps, GC, graphics or saves");
        Check(prof.Contains("invalid-session-or-loading")&&prof.Contains("invalid-mod-error")&&mod.Contains("ModProfiler?.Cancel(\"scene-unloaded\")"),"profile invalidated across session/load/error boundaries");
        Check(mod.Contains("case \"m20-profile\"")&&mod.Contains("ModProfiler?.BeginUpdate()")&&mod.Contains("ModProfiler.RecordSection(label"),"explicit production IPC and section instrumentation wired");
    }
}
