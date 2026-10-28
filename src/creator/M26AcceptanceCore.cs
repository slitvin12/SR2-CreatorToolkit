using System;
using System.Globalization;
using System.IO;
using Il2Cpp;
using UnityEngine;
namespace CreatorToolkit;
// Acceptance-only hooks for the local diagnostic relay. Every m26-* command is ignored unless
// the explicit opt-in file m26-acceptance.enabled exists in UserData\CreatorToolkit.
// UI state and window size only here: no pointer injection, saves or progression writes.
internal sealed partial class ToolkitUi
{
    partial void M26ResetSubpagesForTest();
    internal void ShowCareForTest(){ShowCreationForTest(1);M26ResetSubpagesForTest();}
    internal void ShowGadgetsForTest(){ShowCreationForTest(2);M26ResetSubpagesForTest();}
    internal bool CoreScrollForTest(string list,float y)
    {
        if(list=="care"){_careScroll=new Vector2(0,y);return true;}
        if(list=="gadget"){_gadgetScroll=new Vector2(0,y);return true;}
        return false;
    }
    internal string CoreUiStateForTest=>"tab="+_tab+" section="+_creationSection+
        " careScroll="+_careScroll.y.ToString("0.#",CultureInfo.InvariantCulture)+
        " gadgetScroll="+_gadgetScroll.y.ToString("0.#",CultureInfo.InvariantCulture);
}
internal static class M26AcceptanceCore
{
    internal const string OptInFile="m26-acceptance.enabled";
    internal const string TestGameName="20261020001829_6"; // native GameMetadata.GameName of the dedicated test world
    // Identical in every build under comparison, so before/after screenshots show the same last row.
    internal const string FixtureName="Контрольная строка M26 · очень длинное имя слайма для проверки переноса текста";
    internal const string FixtureFood="Контрольная еда: Морковь, Пряная груша, Хряцеребрый лук, Медовая тыква, Призрачная репа";
    internal static string Last="none";
    private static bool _enabled;
    private static float _nextEnabledCheck=-1;
    private static bool _screenSaved;
    private static int _screenW,_screenH;
    private static FullScreenMode _screenMode;
    internal static bool Enabled(ToolkitMod app)
    {
        if(Time.unscaledTime>=_nextEnabledCheck)
        {
            _nextEnabledCheck=Time.unscaledTime+5;
            try{_enabled=File.Exists(Path.Combine(app.DataDirectory,OptInFile));}catch{_enabled=false;}
        }
        return _enabled;
    }
    internal static string[] Args(string command)=>command.Split(' ',StringSplitOptions.RemoveEmptyEntries);
    internal static string GameName()
    {
        var metadata=NativeSaveMetadata.Read(GameContext.Instance?.AutoSaveDirector);
        return metadata!=null&&metadata.HasValue?metadata.Value.GameName:null;
    }
    // Mutating acceptance steps are limited to the dedicated test world.
    internal static void RequireTestSave(ToolkitMod app)
    {
        if(!app.Ready)throw new InvalidOperationException("world not ready");
        string name=GameName();
        if(name!=TestGameName)throw new InvalidOperationException("loaded save '"+name+"' is not the acceptance test save");
    }
    internal static bool Execute(ToolkitMod app,string name,Func<string> body)
    {
        _nextEnabledCheck=-1; // every explicit command re-reads the opt-in file
        if(!Enabled(app)){Last=name+" ignored: opt-in file missing";app.LoggerInstance.Warning("M26 acceptance: "+Last);return true;}
        try{Last=name+": "+body();}
        catch(Exception e){Last=name+" error: "+e.GetType().Name+": "+e.Message;}
        Last=DateTime.Now.ToString("HH:mm:ss",CultureInfo.InvariantCulture)+" "+Last;
        app.LoggerInstance.Msg("M26 acceptance: "+Last);
        return true;
    }
    internal static bool Handle(ToolkitMod app,ToolkitUi ui,string command)
    {
        if(!command.StartsWith("m26-",StringComparison.Ordinal))return false;
        var a=Args(command);
        switch(a[0])
        {
            case "m26-care-fixture": return Execute(app,a[0],()=>CareFixture(app,a.Length>1?int.Parse(a[1],CultureInfo.InvariantCulture):0));
            case "m26-care-live": return Execute(app,a[0],()=>{if(!app.Care.Inspect)app.Care.SetInspect(true);return "inspector on";});
            case "m26-scroll":
                if(a.Length<3||a[1] is not ("care" or "gadget"))return false;
                return Execute(app,a[0],()=>
                {
                    float y=a[2]=="end"?1e6f:float.Parse(a[2],NumberStyles.Float,CultureInfo.InvariantCulture);
                    ui.CoreScrollForTest(a[1],y);return a[1]+" target "+y.ToString(CultureInfo.InvariantCulture);
                });
            case "m26-screen": return Execute(app,a[0],()=>Resize(a));
            case "m26-screen-restore": return Execute(app,a[0],RestoreScreen);
            case "m26-cancel": return Execute(app,a[0],()=>{string title=app.Pending?.Title;app.Pending=null;return "cleared "+(title??"none");});
        }
        return false;
    }
    // total>0: pad with short UI-only rows up to 'total' so the list must scroll; the long fixture row stays last.
    private static string CareFixture(ToolkitMod app,int total)
    {
        var care=app.Care;
        if(care.Inspect)care.SetInspect(false); // a static list: the live inspector rescans every few seconds
        care.ScanNow();
        int real=care.Hits.Count;
        var last=real>0?care.Hits[real-1]:null;
        int pad=Math.Max(0,Math.Min(total,CarePolicy.MaxTargets)-real-1);
        for(int k=0;k<pad;k++)
            care.Hits.Add(new SlimeCareTools.Hit{Name="Контрольная строка M26 №"+(k+1).ToString(CultureInfo.InvariantCulture),Favorite="Контрольная еда",
                Reference=last?.Reference??"",FavoriteReference=last?.FavoriteReference??"",ActorToken=IntPtr.Zero,Distance=21f+k*.1f,Hunger=.5f,Agitation=.1f,Hungry=false});
        care.Hits.Add(new SlimeCareTools.Hit{Name=FixtureName,Favorite=FixtureFood,Reference=last?.Reference??"",
            FavoriteReference=last?.FavoriteReference??"",ActorToken=IntPtr.Zero,Distance=24.9f,Hunger=.62f,Agitation=.18f,Hungry=false});
        return "real rows "+real+" + "+(pad+1).ToString(CultureInfo.InvariantCulture)+" UI-only fixture rows (inspector off, list static)";
    }
    private static string Resize(string[] a)
    {
        if(a.Length<3)throw new ArgumentException("usage: m26-screen <w> <h>");
        int w=int.Parse(a[1],CultureInfo.InvariantCulture),h=int.Parse(a[2],CultureInfo.InvariantCulture);
        if(w<640||h<480||w>7680||h>4320)throw new ArgumentOutOfRangeException(nameof(a));
        if(!_screenSaved){_screenW=Screen.width;_screenH=Screen.height;_screenMode=Screen.fullScreenMode;_screenSaved=true;}
        Screen.SetResolution(w,h,FullScreenMode.Windowed);
        return "requested "+w+"x"+h+" windowed; original "+_screenW+"x"+_screenH+" "+_screenMode;
    }
    private static string RestoreScreen()
    {
        if(!_screenSaved)return "nothing to restore";
        Screen.SetResolution(_screenW,_screenH,_screenMode);_screenSaved=false;
        return "restored "+_screenW+"x"+_screenH+" "+_screenMode;
    }
    internal static string Diagnostics(ToolkitMod app,ToolkitUi ui)
    {
        if(!Enabled(app))return "";
        return "M26Acceptance: "+Last+"\nM26UiCore: "+ui.CoreUiStateForTest+"\nM26Screen: "+Screen.width+"x"+Screen.height+" "+Screen.fullScreenMode+
            (_screenSaved?" original "+_screenW+"x"+_screenH+" "+_screenMode:"")+"\nM26CareRows: "+app.Care.Hits.Count+" inspector="+app.Care.Inspect+"\n";
    }
}
