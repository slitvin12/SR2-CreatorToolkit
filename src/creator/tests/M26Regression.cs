using System;
using System.IO;
namespace CreatorToolkit;
internal static class M26Regression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool b,string s)=>check(b,"m26 / "+s);
        foreach(int native in new[]{1,50,999,9999})foreach(int before in new[]{0,1,998,999,1000,int.MaxValue})foreach(int room in new[]{0,1,100,int.MaxValue})foreach(bool fill in new[]{false,true})
        {
            int add=M26RefineryPolicy.Addition(before,native,room,fill);
            C(add>=0&&add<=room&&add<=Math.Max(0,M26RefineryPolicy.Cap(native)-before)&&(fill||add<=100),$"refinery bounded native={native} before={before} room={room} fill={fill}");
        }
        C(M26RefineryPolicy.Addition(0,999,999,false)==100&&M26RefineryPolicy.Addition(0,999,999,true)==999,"zero to 100 / 999 arithmetic");
        C(M26RefineryPolicy.Addition(1200,999,0,true)==0,"never reduce existing stock above cap");
        foreach(int bad in new[]{0,-1}){bool threw=false;try{M26RefineryPolicy.Cap(bad);}catch(ArgumentOutOfRangeException){threw=true;}C(threw,"invalid native capacity rejected "+bad);}
        foreach(float h in new[]{0f,82,246,280,10000})C(M26ListPolicy.Extent(h)==h+16,"all list extents retain bottom gutter "+h);
        C(M26ListPolicy.Extent(float.NaN)==16&&M26ListPolicy.Extent(-1)==16,"invalid extent does not leak NaN");
        foreach(float width in new[]{800,1000,1100,1280}){float w=M26ListPolicy.CareButtonWidth(width);C(Math.Abs(3*w+24-width)<.001f,"three equal buttons fit logical width "+width);}
        foreach(float rate in new[]{.25f,1,5})C(M26RatePolicy.Allowed(rate),"explicit time rate allowed "+rate);
        foreach(float bad in new[]{0f,-1,2,float.NaN,float.PositiveInfinity})C(!M26RatePolicy.Allowed(bad),"invalid time rate denied "+bad);
        float value=1;var lease=new M25ScalarLease<float>(()=>value,v=>value=v,.25f);lease.Apply();lease.Restore();C(value==1,"slow-motion restores ordinary tempo");
        lease=new M25ScalarLease<float>(()=>value,v=>value=v,5);lease.Apply();value=0;lease.Restore();C(value==0&&lease.Conflict,"native pause is never overridden on reset");
        C(M26RatePolicy.ShouldRepairSavedScale(0,.25f,true,.25f)&&M26RatePolicy.ShouldRepairSavedScale(0,5,true,5),"pause that remembered our rate is repaired");
        C(!M26RatePolicy.ShouldRepairSavedScale(0,.25f,false,.25f),"no repair without a native pause");
        C(!M26RatePolicy.ShouldRepairSavedScale(0,.25f,true,1)&&!M26RatePolicy.ShouldRepairSavedScale(0,5,true,.25f),"foreign remembered scale is never replaced");
        C(!M26RatePolicy.ShouldRepairSavedScale(.25f,.25f,true,.25f),"no repair while our rate is still current");
        C(!M26RatePolicy.ShouldRepairSavedScale(0,1,true,1)&&!M26RatePolicy.ShouldRepairSavedScale(0,2,true,2),"ordinary or invalid rate never repairs");
        C(!M26RatePolicy.ShouldRepairSavedScale(0,.25f,true,float.NaN)&&!M26RatePolicy.ShouldRepairSavedScale(0,.25f,true,float.PositiveInfinity),"non-finite remembered scale ignored");
        foreach(float rate in new[]{.25f,5f})foreach(bool repair in new[]{false,true})
        {
            // Simulated TimeDirector: Pause() remembers the current scale, Unpause() restores it.
            float scale=1,saved=1;bool paused=false;
            var timeLease=new M25ScalarLease<float>(()=>scale,v=>scale=v,rate);timeLease.Apply();
            saved=scale;scale=0;paused=true;
            if(repair&&M26RatePolicy.ShouldRepairSavedScale(scale,rate,paused,saved))saved=timeLease.Before;
            timeLease.Restore();bool stillPaused=scale==0;
            paused=false;scale=saved;
            C(stillPaused&&(repair?scale==1:scale==rate),$"native pause/resume during x{rate} {(repair?"returns to x1":"reproduces stuck rate without repair")}");
        }
        string R(string name)=>File.ReadAllText(Path.Combine(root,"src","creator",name));
        string refinery=R("RefineryTools.cs"),rateTool=R("CinematicRateTools.cs"),ui=R("ToolkitUi.cs"),care=R("CareUi.cs");
        C(refinery.Contains("GetAllMembers()")&&refinery.Contains("RefineryTypeGroup.IsMember")&&refinery.Contains("InfiniteBuildHooks.RealCounts()"),"native refinery membership and real counts, never catalog guesses");
        C(refinery.Contains("_app.Confirm(")&&refinery.Contains("expected!=_app.Session")&&refinery.Contains("item.Add,false")&&refinery.Contains("after!=item.Before+item.Add"),"refinery confirmation/session/overflow/readback wired");
        C(!refinery.Contains(".SetCount(")&&!refinery.Contains("WriteAllBytes")&&!refinery.Contains("SaveGame("),"refinery does not overwrite dictionaries or save files");
        C(rateTool.Contains("NativeUiPriority.HasNativeFocus")&&rateTool.Contains("_app.Benchmark.Active")&&!rateTool.Contains("Time.fixedDeltaTime="),"time tool rejects native focus and FPS measurement without changing physics timestep");
        C(rateTool.Contains("RepairNativePauseMemory();_lease?.Restore()")&&rateTool.Contains("director._savedTimeScale=_lease.Before")&&!rateTool.Contains("Unpause(")&&!rateTool.Contains("Time.timeScale=1"),"reset repairs remembered native pause scale before releasing, never unpauses");
        C(ui.Contains("contentHeight=M26ListPolicy.Extent(contentHeight)")&&care.Contains("float careButtonWidth=")&&care.Contains("careRow-16"),"global bottom padding, equal care header and measured rows wired");
        C(R("ToolkitMod.cs").Contains("_alwaysTicks=new (string,Action)[]{(\"cinematic rate\",CinematicRate.Tick)")&&rateTool.Contains("if(!_app.Ready||"),"time cleanup runs even when loading clears Ready");
        C(R("ToolkitMod.cs").Contains("restore cinematic rate")&&R("ToolkitMod.cs").Contains("Refinery?.Reset()"),"new tools released on world lifecycle");
        C(R("M25RuntimeTools.cs").Contains("if(Pipeline==\"Built-in\")")&&R("M25RuntimeTools.cs").Contains("shadow.pipeline"),"unknown URP pipeline never uses ineffective legacy shadow settings");
        string core=R("M26AcceptanceCore.cs"),relay=R("M26AcceptanceIpc.cs");
        C(core.Contains("if(!Enabled(app)){Last=name+\" ignored: opt-in file missing\"")&&core.Contains("OptInFile=\"m26-acceptance.enabled\"")&&core.Contains("_nextEnabledCheck=-1;"),"acceptance relay is inert without the explicit opt-in file");
        C(relay.Contains("M26AcceptanceCore.RequireTestSave(app);\r\n        var pending=app.Pending;")&&relay.Contains("if(pending.Title!=title)return")&&relay.Contains("M26AcceptanceCore.RequireTestSave(app);\r\n                string kind=Arg(1);"),"acceptance confirmations and refinery requests require the test save and exact dialog title");
        C(!relay.Contains("AddItem(")&&!relay.Contains("SaveGame")&&!relay.Contains("_savedTimeScale=")&&!relay.Contains("Time.timeScale=")&&!core.Contains("Time.timeScale="),"acceptance relay never writes refinery, saves or time directly");
        C(relay.Contains("if(!target.IsEnableConditionSatisfied())return")&&relay.Contains("if(!ours)return"),"native terminal is opened only through its own enable condition and only our display is closed");
    }
}
