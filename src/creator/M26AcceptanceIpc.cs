using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppMonomiPark.SlimeRancher.UI.Framework.Displays;
using Il2CppMonomiPark.SlimeRancher.UI.Refinery;
using UnityEngine;
namespace CreatorToolkit;
// M26 acceptance relay (opt-in file + dedicated test save for every mutating step).
// Confirmations are accepted only when the pending dialog title is the exact expected one;
// native refinery checks open the real terminal display and read its bound widgets.
internal sealed partial class ToolkitUi
{
    partial void M26ResetSubpagesForTest(){_refineryPage=false;_refineryDropdown=false;}
    internal void ShowRefineryForTest(){ShowCreationForTest(2);_refineryPage=true;_refineryDropdown=false;}
    internal void ShowTimeRateForTest(){ShowPage(5);_phenomenaPage=false;_timePage=true;}
    internal void RefineryScrollForTest(float y)=>_refineryScroll=new Vector2(0,y);
    internal void RefineryDropdownForTest(bool open)=>_refineryDropdown=open;
    internal string RefinerySelectionForTest=>_refinerySelection;
    internal string RefinerySelectForTest(string token)
    {
        var rows=_app.Refinery.Rows;
        RefineryTools.Row row=token=="first"?rows.FirstOrDefault():token=="last"?rows.LastOrDefault():
            rows.FirstOrDefault(r=>string.Equals(r.Reference,token,StringComparison.OrdinalIgnoreCase));
        if(row==null&&int.TryParse(token,NumberStyles.None,CultureInfo.InvariantCulture,out int i)&&i>=0&&i<rows.Count)row=rows[i];
        if(row==null)return null;
        _refinerySelection=row.Reference;_refineryDropdown=false;
        return row.Reference;
    }
    internal string M26UiStateForTest=>"refinery="+_refineryPage+" dropdown="+_refineryDropdown+" selection="+_refinerySelection+
        " refineryScroll="+_refineryScroll.y.ToString("0.#",CultureInfo.InvariantCulture)+" time="+_timePage;
}
internal static class M26AcceptanceIpc
{
    private static readonly Dictionary<string,string> Titles=new()
    {
        ["rate5"]="Ускорить симуляцию в 5 раз?",
        ["refinery-add"]="Добавить ресурсы Преобразователя?",
        ["refinery-fill"]="Заполнить Преобразователь?",
    };
    private static UIDisplayInteractable[] _interactables=Array.Empty<UIDisplayInteractable>();
    private static string _openedDisplay;
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    internal static bool Handle(ToolkitMod app,ToolkitUi ui,string command)
    {
        if(!command.StartsWith("m26-",StringComparison.Ordinal))return false;
        var a=M26AcceptanceCore.Args(command);
        string Arg(int i)=>a.Length>i?a[i]:throw new ArgumentException("missing argument "+i);
        switch(a[0])
        {
            case "m26-rate": return M26AcceptanceCore.Execute(app,a[0],()=>
            {
                float rate=float.Parse(Arg(1),NumberStyles.Float,CultureInfo.InvariantCulture);
                if(!M26RatePolicy.Allowed(rate))throw new ArgumentOutOfRangeException(nameof(rate));
                app.Queue("m26 acceptance time rate",()=>app.CinematicRate.Request(rate));
                return "queued x"+rate.ToString(CultureInfo.InvariantCulture);
            });
            case "m26-confirm": return M26AcceptanceCore.Execute(app,a[0],()=>Confirm(app,Arg(1)));
            case "m26-pause-native": return M26AcceptanceCore.Execute(app,a[0],()=>"TryPauseGame="+SystemContext.Instance.SceneLoader.TryPauseGame()+" "+TimeState(app));
            case "m26-unpause-native": return M26AcceptanceCore.Execute(app,a[0],()=>{SystemContext.Instance.SceneLoader.UnpauseGame();return "UnpauseGame "+TimeState(app);});
            // Native in-game load of the dedicated test world (same path as the pause menu's Load); refused for any other save.
            case "m26-load-test-save": return M26AcceptanceCore.Execute(app,a[0],()=>
            {
                M26AcceptanceCore.RequireTestSave(app);
                var save=GameContext.Instance?.AutoSaveDirector??throw new InvalidOperationException("AutoSaveDirector unavailable");
                var summary=save.HasContinue()?save.GetSaveToContinue():null;
                if(summary==null||summary.IsInvalid||summary.GameOver||summary.Name!=M26AcceptanceCore.TestGameName)
                    throw new InvalidOperationException("latest save is not the test world: "+(summary?.Name??"none"));
                string before=TimeState(app);
                save.BeginLoad(summary.SaveIdentifier);
                return "BeginLoad "+summary.Name+" (before: "+before+")";
            });
            case "m26-pause-direct": return M26AcceptanceCore.Execute(app,a[0],()=>{app.Context.TimeDirector.Pause();return "TimeDirector.Pause "+TimeState(app);});
            case "m26-unpause-direct": return M26AcceptanceCore.Execute(app,a[0],()=>{app.Context.TimeDirector.Unpause();return "TimeDirector.Unpause "+TimeState(app);});
            case "m26-scroll":
                if(a.Length<3||a[1]!="refinery")return false;
                return M26AcceptanceCore.Execute(app,a[0],()=>
                {
                    float y=a[2]=="end"?1e6f:float.Parse(a[2],NumberStyles.Float,CultureInfo.InvariantCulture);
                    ui.RefineryScrollForTest(y);return "refinery target "+y.ToString(CultureInfo.InvariantCulture);
                });
            case "m26-refinery-dropdown": return M26AcceptanceCore.Execute(app,a[0],()=>{ui.RefineryDropdownForTest(Arg(1)=="on");return "dropdown "+Arg(1);});
            case "m26-refinery-select": return M26AcceptanceCore.Execute(app,a[0],()=>"selected "+(ui.RefinerySelectForTest(Arg(1))??"nothing (no such row)"));
            case "m26-refinery-request": return M26AcceptanceCore.Execute(app,a[0],()=>
            {
                M26AcceptanceCore.RequireTestSave(app);
                string kind=Arg(1);
                if(kind is not ("add-all" or "fill-all" or "add-selected" or "fill-selected"))throw new ArgumentException(kind);
                bool fill=kind.StartsWith("fill",StringComparison.Ordinal);
                string reference=kind.EndsWith("selected",StringComparison.Ordinal)?ui.RefinerySelectionForTest:null;
                if(kind.EndsWith("selected",StringComparison.Ordinal)&&string.IsNullOrEmpty(reference))throw new InvalidOperationException("no refinery selection");
                app.Queue("m26 acceptance refinery request",()=>app.Refinery.Request(fill,reference));
                return "queued "+kind+" "+(reference??"all");
            });
            case "m26-refinery-dump": return M26AcceptanceCore.Execute(app,a[0],()=>Dump(app,a.Length>1?a[1]:"x"));
            case "m26-native-list": return M26AcceptanceCore.Execute(app,a[0],()=>ListInteractables(app));
            case "m26-native-open": return M26AcceptanceCore.Execute(app,a[0],()=>OpenNative(app,Arg(1)));
            case "m26-native-read": return M26AcceptanceCore.Execute(app,a[0],()=>ReadNative(app,a.Length>1?a[1]:"x"));
            case "m26-native-close": return M26AcceptanceCore.Execute(app,a[0],CloseNative);
        }
        return false;
    }
    private static string Confirm(ToolkitMod app,string kind)
    {
        if(!Titles.TryGetValue(kind,out var title))throw new ArgumentException("unknown confirmation "+kind);
        M26AcceptanceCore.RequireTestSave(app);
        var pending=app.Pending;
        if(pending==null)return "no pending confirmation";
        if(pending.Title!=title)return "rejected: pending title differs";
        app.AcceptConfirmation();
        return "accepted "+kind;
    }
    private static string TimeState(ToolkitMod app)
    {
        var d=app.Context?.TimeDirector;
        return d==null?"no director":"scale="+Time.timeScale.ToString(CultureInfo.InvariantCulture)+" paused="+d.isPaused()+" pausers="+d._pauserCount+" saved="+d._savedTimeScale.ToString(CultureInfo.InvariantCulture);
    }
    private static string NameOf(ToolkitMod app,IdentifiableType type)=>
        app.Actors.Catalog.FirstOrDefault(e=>!e.IsPrototype&&!e.IsRadiant&&e.Reference==type.ReferenceId)?.Name??type.ReferenceId;
    private static GadgetDirector Director(ToolkitMod app)
    {
        if(!app.Ready)throw new InvalidOperationException("world not ready");
        var director=app.Context.GadgetDirector;
        if(director==null||director.WasCollected||director.RefineryTypeGroup==null)throw new InvalidOperationException("GadgetDirector unavailable");
        return director;
    }
    private static string Dump(ToolkitMod app,string tag)
    {
        var director=Director(app);
        var rows=new List<Dictionary<string,object>>();long total=0;
        using(InfiniteBuildHooks.RealCounts())
        foreach(var type in NativeEnumerable.Items(director.RefineryTypeGroup.GetAllMembers()))
        {
            if(type==null||type.WasCollected||string.IsNullOrEmpty(type.ReferenceId))continue;
            int count=director.GetItemCount(type);total+=count;
            rows.Add(new Dictionary<string,object>{["Ref"]=type.ReferenceId,["Name"]=NameOf(app,type),["Count"]=count,["Space"]=director.GetRefinerySpaceAvailable(type)});
        }
        var doc=new Dictionary<string,object>{["Tag"]=tag,["Time"]=DateTime.Now.ToString("O"),["Game"]=M26AcceptanceCore.GameName(),
            ["RefineryMax"]=GadgetDirector.REFINERY_MAX,["ToolLimit"]=app.Refinery.Limit,["Total"]=total,["Rows"]=rows};
        File.WriteAllText(Path.Combine(app.DataDirectory,"m26-refinery-"+tag+".json"),JsonSerializer.Serialize(doc,Json));
        return "rows="+rows.Count+" total="+total+" max="+GadgetDirector.REFINERY_MAX;
    }
    private static string PathOf(Transform t,int depth)
    {
        var parts=new List<string>();
        for(int i=0;t!=null&&i<depth;i++,t=t.parent)parts.Insert(0,t.name);
        return string.Join("/",parts);
    }
    private static string DisplayName(UIDisplayInteractable x)
    {
        try{return x._display?.Name;}catch{return null;}
    }
    private static string ListInteractables(ToolkitMod app)
    {
        if(!app.Ready)throw new InvalidOperationException("world not ready");
        var player=app.Context.Player.transform.position;
        _interactables=UnityEngine.Object.FindObjectsOfType<UIDisplayInteractable>(true).ToArray();
        var rows=new List<Dictionary<string,object>>();
        for(int i=0;i<_interactables.Length;i++)
        {
            var x=_interactables[i];if(x==null||x.WasCollected)continue;
            var science=x.TryCast<ScienceUIDisplayActivator>();
            bool condition=false;try{condition=x.IsEnableConditionSatisfied();}catch{}
            rows.Add(new Dictionary<string,object>{["Index"]=i,["Path"]=PathOf(x.transform,6),["Type"]=x.GetIl2CppType().Name,["Display"]=DisplayName(x),
                ["Active"]=x.gameObject.activeInHierarchy,["Enabled"]=x.enabled,["Condition"]=condition,["Gadget"]=science!=null&&science._isGadget,
                ["Distance"]=Math.Round(Vector3.Distance(player,x.transform.position),1),["Position"]=x.transform.position.ToString("F1")});
        }
        File.WriteAllText(Path.Combine(app.DataDirectory,"m26-native-list.json"),JsonSerializer.Serialize(rows,Json));
        int refinery=rows.Count(r=>(r["Display"] as string)?.IndexOf("refinery",StringComparison.OrdinalIgnoreCase)>=0);
        return "interactables="+rows.Count+" refineryDisplays="+refinery;
    }
    private static string OpenNative(ToolkitMod app,string token)
    {
        if(!app.Ready)throw new InvalidOperationException("world not ready");
        if(NativeUiPriority.HasNativeFocus)throw new InvalidOperationException("a native display already owns focus");
        if(_interactables.Length==0)ListInteractables(app);
        var player=app.Context.Player.transform.position;
        UIDisplayInteractable target=null;
        if(token=="refinery")
            target=_interactables.Where(x=>x!=null&&!x.WasCollected&&x.gameObject.activeInHierarchy&&
                (DisplayName(x)?.IndexOf("refinery",StringComparison.OrdinalIgnoreCase)??-1)>=0)
                .OrderBy(x=>Vector3.Distance(player,x.transform.position)).FirstOrDefault();
        else if(int.TryParse(token,NumberStyles.None,CultureInfo.InvariantCulture,out int i)&&i>=0&&i<_interactables.Length)target=_interactables[i];
        if(target==null||target.WasCollected)throw new InvalidOperationException("no live interactable for "+token);
        string display=DisplayName(target);
        if(!target.IsEnableConditionSatisfied())return "enable condition not satisfied for "+display+"; not forced";
        if(app.MenuOpen)app.CloseMenu();
        _openedDisplay=display;
        target.OnInteract();
        return "OnInteract "+display+" path="+PathOf(target.transform,6)+" distance="+Vector3.Distance(player,target.transform.position).ToString("F1",CultureInfo.InvariantCulture);
    }
    private static string ReadNative(ToolkitMod app,string tag)
    {
        var director=Director(app);
        var uis=UnityEngine.Object.FindObjectsOfType<RefineryUI>(true);
        var report=new List<Dictionary<string,object>>();int items=0,matched=0,agree=0,activeUis=0;
        using(InfiniteBuildHooks.RealCounts())
        foreach(var ui in uis)
        {
            if(ui==null||ui.WasCollected)continue;
            bool active=ui.gameObject.activeInHierarchy;if(active)activeUis++;
            var types=new List<IdentifiableType>();
            var list=ui._itemTypes;
            if(list!=null)for(int i=0;i<list.Count;i++)if(list[i]!=null)types.Add(list[i]);
            var widgets=new List<Dictionary<string,object>>();
            foreach(var item in ui.GetComponentsInChildren<RefineryItem>(true))
            {
                if(item==null||!item.gameObject.activeInHierarchy)continue;
                items++;
                var sprite=item._icon?.sprite;string text=item._counter?.text;
                var type=sprite==null?null:types.FirstOrDefault(t=>t.Icon!=null&&t.Icon.Pointer==sprite.Pointer);
                int? real=type==null?null:director.GetItemCount(type);
                string digits=new string((text??"").Where(char.IsDigit).ToArray());
                bool same=real!=null&&digits==real.Value.ToString(CultureInfo.InvariantCulture);
                if(type!=null)matched++;if(same)agree++;
                widgets.Add(new Dictionary<string,object>{["Sprite"]=sprite?.name,["Counter"]=text,["Ref"]=type?.ReferenceId,
                    ["Name"]=type==null?null:NameOf(app,type),["Real"]=real,["Agree"]=same});
            }
            report.Add(new Dictionary<string,object>{["Path"]=PathOf(ui.transform,6),["Active"]=active,["Types"]=types.Count,["Widgets"]=widgets});
        }
        var top=UIDisplayManager._instance?._lastState?.TopFocus;
        var doc=new Dictionary<string,object>{["Tag"]=tag,["Time"]=DateTime.Now.ToString("O"),["TopFocus"]=top?.Name,["Opened"]=_openedDisplay,["Uis"]=report};
        File.WriteAllText(Path.Combine(app.DataDirectory,"m26-native-"+tag+".json"),JsonSerializer.Serialize(doc,Json));
        return "refineryUis="+report.Count+" active="+activeUis+" widgets="+items+" matched="+matched+" agree="+agree+" top="+top?.Name;
    }
    private static string CloseNative()
    {
        var top=UIDisplayManager._instance?._lastState?.TopFocus;
        if(top==null)return "no focused display";
        string name=top.Name;
        bool ours=name!=null&&(name==_openedDisplay||name.IndexOf("refinery",StringComparison.OrdinalIgnoreCase)>=0);
        if(!ours)return "top display "+name+" was not opened by acceptance; left open";
        if(!top.IsClosing)top.Close();
        return "closed "+name;
    }
    internal static string Diagnostics(ToolkitMod app,ToolkitUi ui)
    {
        if(!M26AcceptanceCore.Enabled(app))return "";
        var sb=new StringBuilder(768);var rate=app.CinematicRate;
        sb.Append("M26Rate: active=").Append(rate.Active).Append(" rate=").Append(rate.Rate.ToString(CultureInfo.InvariantCulture))
          .Append(" repairs=").Append(rate.SavedScaleRepairs).Append(" pause=").Append(rate.PauseDiagnostics).Append(" status=").Append(rate.Status).Append('\n');
        try{sb.Append("M26Time: ").Append(app.Ready?TimeState(app):"not ready").Append('\n');}
        catch(Exception e){sb.Append("M26Time: error ").Append(e.GetType().Name).Append('\n');}
        sb.Append("M26Pending: ").Append(app.Pending?.Title??"none").Append('\n');
        sb.Append("M26Refinery: rows=").Append(app.Refinery.Rows.Count).Append(" limit=").Append(app.Refinery.Limit).Append(" status=").Append(app.Refinery.Status).Append('\n');
        sb.Append("M26Ui: ").Append(ui.M26UiStateForTest).Append('\n');
        return sb.ToString();
    }
}
