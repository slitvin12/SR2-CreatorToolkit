using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher.UI.Framework.CommonControls;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using CompassBarUI = Il2CppMonomiPark.SlimeRancher.UI.HUD.CompassBarUI;
using TmpText = Il2CppTMPro.TMP_Text;
using UiButton = UnityEngine.UI.Button;
using UiImage = UnityEngine.UI.Image;
using UiMask = UnityEngine.UI.Mask;
using UiRawImage = UnityEngine.UI.RawImage;
using UiSelectable = UnityEngine.UI.Selectable;
using UiText = UnityEngine.UI.Text;

namespace CreatorToolkit;

// M28 test relay on the local diagnostic channel (cmd.txt / status.txt): ct-* and m28-*
// commands, ignored unless UserData\CreatorToolkit\ct-test.enabled exists (re-read on
// every command). Read-only UI dumps, hit tests and state lines, UI navigation, and the
// same confirmations as the UI buttons. Anything that changes a save runs only on the
// dedicated test save (M26AcceptanceCore.RequireTestSave); a confirmation is accepted
// only when the pending title is the exact expected one. Reference: tools\ct-test\TESTING.md.
internal static class M28TestIpc
{
    internal static string Last="none";
    private static bool _enabled;
    private static float _nextCheck=-1;
    private static string _lastCustom="";
    private static readonly CultureInfo CI=CultureInfo.InvariantCulture;
    private static readonly Dictionary<string,int> UnlockIndex=new(StringComparer.Ordinal){["styles"]=4,["blueprints"]=5,["palettes"]=6};

    internal static bool Enabled(ToolkitMod app)
    {
        if(_nextCheck<0||Time.unscaledTime>=_nextCheck)
        {
            _nextCheck=Time.unscaledTime+5;
            try{_enabled=!string.IsNullOrEmpty(app.DataDirectory)&&File.Exists(Path.Combine(app.DataDirectory,M28TestPolicy.OptInFile));}
            catch{_enabled=false;}
        }
        return _enabled;
    }
    private static bool Run(ToolkitMod app,string name,Func<string> body)
    {
        _nextCheck=-1; // every explicit command re-reads the opt-in file
        if(!Enabled(app)){Last=name+" ignored: opt-in file "+M28TestPolicy.OptInFile+" missing";app.LoggerInstance.Warning("CT test: "+Last);return true;}
        string result;
        try{result=name+": "+body();}
        catch(Exception e){result=name+" error: "+e.GetType().Name+": "+e.Message;}
        Last=DateTime.Now.ToString("HH:mm:ss",CI)+" "+M28TestPolicy.OneLine(result,600);
        app.LoggerInstance.Msg("CT test: "+Last);
        return true;
    }
    private static int Int(string s,int min,int max)
    {
        if(!int.TryParse(s,NumberStyles.Integer,CI,out int v)||v<min||v>max)throw new ArgumentException("expected an integer "+min+".."+max+", got '"+s+"'");
        return v;
    }

    internal static bool Handle(ToolkitMod app,ToolkitUi ui,string command,Action<string,Action> navigate)
    {
        if(!command.StartsWith("ct-",StringComparison.Ordinal)&&!command.StartsWith("m28-",StringComparison.Ordinal))return false;
        var a=command.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        string Arg(int i)=>a.Length>i?a[i]:throw new ArgumentException("missing argument "+i+"; see ct-help");
        string Rest(int i)=>string.Join(" ",a.Skip(i));
        switch(a[0])
        {
            case "ct-help": return Run(app,a[0],()=>Write(app,"ct-help.txt",string.Join("\n",M28TestPolicy.Help)+"\n"));
            case "ct-state": return Run(app,a[0],()=>State(app));
            case "ct-ui-dump": return Run(app,a[0],()=>UiDump(app,Arg(1),a.Length>2?a[2]:""));
            case "ct-ui-hit": return Run(app,a[0],()=>Hit(app,Arg(1),Arg(2)));
            case "ct-ui-find": return Run(app,a[0],()=>Find(app,Rest(1)));
            case "ct-continue-test": return Run(app,a[0],()=>ContinueTest(app));
            case "ct-atlas-panel": return Run(app,a[0],()=>AtlasPanel(app,Arg(1)));
            // M29 atlas: search text (or clear) exactly as the atlas page field sets it, a state line, focus.
            case "ct-atlas-search": return Run(app,a[0],()=>AtlasSearch(app,ui,Rest(1)));
            case "ct-atlas-state": return Run(app,a[0],()=>AtlasState(app,ui));
            case "ct-atlas-focus": return Run(app,a[0],()=>AtlasFocus(app,Arg(1),a.Length>2?Int(a[2],0,9999):0));
            // 0.8.1 native map: fog mask (pieces, hidden markers), layer presets, the fog toggle.
            case "ct-map-fog": return Run(app,a[0],()=>MapFogState(app));
            case "ct-map-preset": return Run(app,a[0],()=>MapPreset(app,Int(Arg(1),0,AtlasGroupPolicy.Presets.Length-1)));
            case "ct-map-fog-hide": return Run(app,a[0],()=>MapFogHide(app,Arg(1)));
            // M29 phantom drones: zone binding of the native routes (source, atlas page, compass group).
            case "ct-ghost-state": return Run(app,a[0],()=>GhostState(app));
            // M29 audits (read only): where the game grants the vacpack styles; layer -> list/compass coverage.
            case "ct-palette-sources": return Run(app,a[0],()=>Write(app,"ct-palettes-src.txt",M29PaletteAudit.Report(app)));
            case "ct-layer-audit": return Run(app,a[0],()=>Write(app,"ct-layers.txt",M29LayerAudit.Report(app)));
            // Same three states as the atlas page button (off -> terrain -> radar); M29: kept in navigation.cfg.
            case "ct-minimap": return Run(app,a[0],()=>MiniMap(app,Arg(1)));
            // M29 task 4: minimap / compass settings (navigation.cfg), same keys as the file.
            case "ct-nav": return Run(app,a[0],()=>Nav(app,a));
            case "ct-menu": return Run(app,a[0],()=>
            {
                int tab=Int(Arg(1),0,ToolkitUi.TabCount-1),section=a.Length>2?Int(a[2],0,8):0;
                navigate(command,()=>{if(tab==4)ui.M27ShowUnlockForTest(section);else ui.ShowPage(tab);});
                return "navigation requested tab "+tab+(tab==4?" section "+section:"");
            });
            case "m28-drone": return Run(app,a[0],()=>Drone(app,a));
            case "m28-compass-dump": return Run(app,a[0],()=>Write(app,"m28-compass.txt",app.Compass.Dump())+" | "+app.Compass.Status+" | "+app.Compass.StyleStatus);
            case "m28-compass-style": return Run(app,a[0],()=>
            {
                if(!M28CompassIconPolicy.KnownStyle(Arg(1)))throw new ArgumentException("overlay|replace|off");
                app.Compass.SetStyle(Arg(1));return "style "+app.Compass.Style+" ("+app.Compass.StyleStatus+")";
            });
            case "m28-palettes": return Run(app,a[0],()=>Write(app,"m28-palettes.txt",app.Unlocks.PaletteReport()));
            case "m28-unlock": return Run(app,a[0],()=>
            {
                if(!UnlockIndex.TryGetValue(Arg(1),out int index))throw new ArgumentException("palettes|styles|blueprints");
                M26AcceptanceCore.RequireTestSave(app);
                if(app.Pending!=null)throw new InvalidOperationException("another confirmation is pending: "+app.Pending.Title);
                return "pending '"+ui.M27RequestUnlockForTest(index)+"'";
            });
            case "m28-confirm": return Run(app,a[0],()=>Confirm(app,Arg(1)));
            case "m28-player-marker": return Run(app,a[0],()=>PlayerMarker(app,a.Length>1?a[1]:"state"));
            case "m28-custom-add": return Run(app,a[0],()=>CustomAdd(app,Arg(1),a.Length>2?Rest(2):""));
            case "m28-custom-goal": return Run(app,a[0],()=>{string id=CustomId(Arg(1));app.Custom.SetGoal(id);return "goal "+id+" compass="+app.Custom.ShowCompass;});
            case "m28-custom-del": return Run(app,a[0],()=>
            {
                M26AcceptanceCore.RequireTestSave(app);
                string id=CustomId(Arg(1));
                if(app.Custom.Find(id)==null)throw new InvalidOperationException("no marker "+id);
                app.Custom.Delete(id);if(id==_lastCustom)_lastCustom="";
                return "deleted "+id+" ("+app.Custom.Records.Count+" left)";
            });
            case "m28-tracking": return Run(app,a[0],()=>Tracking(app));
        }
        return Run(app,a[0],()=>throw new ArgumentException("unknown command; see ct-help"));
    }

    private static string Write(ToolkitMod app,string file,string text)
    {
        text??="";
        File.WriteAllText(Path.Combine(app.DataDirectory,file),text,new UTF8Encoding(false));
        int lines=0;foreach(char c in text)if(c=='\n')lines++;
        return "written "+file+" ("+lines+" lines)";
    }

    private static string State(ToolkitMod app)
    {
        var map=app.Map;
        var sb=new StringBuilder(512);
        sb.Append("ready=").Append(app.Ready).Append(" save=").Append(app.Ready?M26AcceptanceCore.GameName()??"?":"-")
          .Append(" test=").Append(app.Ready&&M26AcceptanceCore.GameName()==M26AcceptanceCore.TestGameName)
          .Append(" menu=").Append(app.MenuOpen).Append(" pending=").Append(app.Pending?.Title??"-")
          .Append(" map=").Append(map!=null&&map.NativeMapOpen).Append(" atlasPanel=").Append(map!=null&&map.PanelExpanded?"expanded":"collapsed")
          .Append(" screen=").Append(Screen.width).Append('x').Append(Screen.height).Append(" timeScale=").Append(Time.timeScale.ToString("0.##",CI))
          .Append(" | drone: ").Append(app.DroneToggle?.Status).Append(" | compass: ").Append(app.Compass?.Status).Append(" · ").Append(app.Compass?.StyleStatus)
          .Append(" | marker: ").Append(app.PlayerMarker?.Status).Append(" | palettes: ").Append(app.Unlocks?.PalettesResult);
        return sb.ToString();
    }

    private static string ContinueTest(ToolkitMod app)
    {
        if(app.Ready)
        {
            string name=M26AcceptanceCore.GameName();
            if(name!=M26AcceptanceCore.TestGameName)throw new InvalidOperationException("loaded save '"+name+"' is not the test save");
            return "already in test save "+name;
        }
        var loader=Il2Cpp.SystemContext.Instance?.SceneLoader;
        if(loader==null||loader.IsSceneLoadInProgress)return "scene loader busy; retry";
        // Same steps as ToolkitMod.TryContinueGame, except that the final load happens
        // only when the continue save is the dedicated test world.
        var engagement=UnityEngine.Object.FindObjectOfType<Il2CppMonomiPark.SlimeRancher.UI.MainMenu.PlatformEngagementPrompt>();
        if(engagement!=null&&engagement.SceneGroupToLoad!=null&&engagement.ShouldShowInteractionPrompt())
        {engagement.LoadSceneGroup();return "advanced past the engagement prompt; repeat";}
        var start=UnityEngine.Object.FindObjectOfType<Il2CppMonomiPark.SlimeRancher.UI.StandaloneStartScreen>();
        if(start!=null&&start.pastFirstFrame&&!start.isLoading&&start.defaultSceneGroup!=null)
        {
            start.isLoading=true;
            try{loader.LoadSceneGroup(start.defaultSceneGroup);}
            catch{if(!start.WasCollected)start.isLoading=false;throw;}
            return "advanced past the start screen; repeat";
        }
        var save=Il2Cpp.GameContext.Instance?.AutoSaveDirector;
        if(save==null)return "main menu not ready (no AutoSaveDirector yet); retry";
        var summary=save.HasContinue()?save.GetSaveToContinue():null;
        if(summary==null||summary.IsInvalid||summary.GameOver||summary.Name!=M26AcceptanceCore.TestGameName)
            throw new InvalidOperationException("continue save is '"+(summary?.Name??"none")+"', not the test save "+M26AcceptanceCore.TestGameName+"; nothing loaded");
        save.BeginLoad(summary.SaveIdentifier);
        return "BeginLoad "+summary.Name;
    }

    private static string AtlasPanel(ToolkitMod app,string mode)
    {
        var map=app.Map;bool before=map.PanelExpanded;
        map.PanelExpanded=mode switch{"expanded"=>true,"collapsed"=>false,"toggle"=>!before,_=>throw new ArgumentException("expanded|collapsed|toggle")};
        return "PanelExpanded "+before+" -> "+map.PanelExpanded+" (map open="+map.NativeMapOpen+")";
    }

    // Session-only, like typing: the blur-safe UI path; "clear" = the × buttons. The relay
    // lower-cases lines, so Cyrillic may also come percent-encoded (UTF-8).
    private static string AtlasSearch(ToolkitMod app,ToolkitUi ui,string text)
    {
        text=(text??"").Trim();
        if(text.IndexOf('%')>=0)text=Uri.UnescapeDataString(text);
        if(text.Length==0)throw new ArgumentException("ct-atlas-search <text>|clear");
        ui.SetAtlasSearchForTest(text=="clear"?"":text);
        return AtlasState(app,ui);
    }
    private static string MapFogState(ToolkitMod app)
    {
        var map=app.Map??throw new InvalidOperationException("no map tools");
        var page=map.Filter(true);
        return Write(app,"ct-map-fog.txt",map.Fog.Describe())+" | "+map.Fog.Status+" | hideFogged="+(map.HideFogged?1:0)+
            " hiddenOnPage="+map.FogHiddenOnPage+" page="+page.Count+" map="+map.NativeMapOpen;
    }
    private static string MapPreset(ToolkitMod app,int preset)
    {
        var map=app.Map??throw new InvalidOperationException("no map tools");
        map.ApplyPreset(preset);
        return "preset "+AtlasGroupPolicy.Presets[preset].Title+" matching="+map.MatchingPreset()+" page="+map.Filter(true).Count;
    }
    private static string MapFogHide(ToolkitMod app,string mode)
    {
        var map=app.Map??throw new InvalidOperationException("no map tools");
        map.HideFogged=mode switch{"on"=>true,"off"=>false,"toggle"=>!map.HideFogged,_=>throw new ArgumentException("expected on|off|toggle")};
        return "hideFogged="+(map.HideFogged?1:0)+" hiddenOnPage="+map.FogHiddenOnPage;
    }
    private static string AtlasState(ToolkitMod app,ToolkitUi ui)
    {
        var map=app.Map??throw new InvalidOperationException("no map tools");
        var page=map.Filter(true);
        int own=0,pinned=0;
        foreach(var p in page)if(p.Category==CustomWaypointTools.Category){own++;if(map.PinnedOnly(p))pinned++;}
        var sel=map.Selected;
        return "search='"+map.Search+"' active="+(M29AtlasSearchPolicy.Active(map.Search)?1:0)+" map="+map.NativeMapOpen+
            " page="+page.Count+" hits="+map.SearchHitsOnPage()+" own="+own+" pinnedOnly="+pinned+" list="+map.Filter().Count+
            " | drawn: markers="+ui.MapMarkersForTest+" own="+ui.MapOwnDrawnForTest+" shownCovered="+ui.MapShownCoveredForTest+
            " | card "+ui.MapCardForTest+" | selected="+(sel==null?"none":sel.Category+"|"+sel.Id+"|"+map.DisplayName(sel))+
            " | screen="+Screen.width+"x"+Screen.height+" clears="+ui.AtlasSearchClearsForTest;
    }
    // Selects and centres the n-th point of a layer like the atlas list does. Focus also sets the
    // native navigation marker, hence test save only. own = «Мои метки» whatever the filters say.
    private static string AtlasFocus(ToolkitMod app,string kind,int index)
    {
        M26AcceptanceCore.RequireTestSave(app);
        var map=app.Map??throw new InvalidOperationException("no map tools");
        if(!map.NativeMapOpen)throw new InvalidOperationException("open the native map first");
        string category=kind switch{"pod"=>"Капсулы","own"=>CustomWaypointTools.Category,"drone"=>"Дроны","gordo"=>"Гордо","teleport"=>"Телепорты",_=>kind};
        bool own=category==CustomWaypointTools.Category;
        var list=(own?map.Points:map.Filter()).Where(p=>string.Equals(p.Category,category,StringComparison.OrdinalIgnoreCase)&&map.MapForPoint(p)!=null).ToList();
        if(list.Count==0)throw new InvalidOperationException("no '"+category+"' point"+(own?"":" in the current atlas filter"));
        int at=Math.Min(index,list.Count-1);var point=list[at];
        map.Focus(point);
        return "focused "+at+"/"+list.Count+" "+point.Category+"|"+point.Id+"|"+map.DisplayName(point)+" at "+
            point.Position.x.ToString("0",CI)+","+point.Position.z.ToString("0",CI)+" (card placed on the next repaint)";
    }

    // M29 phantom drones: how each native route got its zone and whether the atlas page and the
    // compass (same scene group as the player, compass range from navigation.cfg) can show it. Read-only.
    private static string GhostState(ToolkitMod app)
    {
        var map=app.Map??throw new InvalidOperationException("no map tools");
        var current=Il2Cpp.SystemContext.Instance?.SceneLoader?.CurrentSceneGroup;
        string here=current!=null&&!current.WasCollected?current.name:"";
        var sb=new StringBuilder(2048);
        int paged=0,onPage=0,same=0,near=0;
        foreach(var p in map.GhostRoutes)
        {
            var page=map.MapForPoint(p);if(page!=null)paged++;
            bool on=map.OnCurrentMap(p);if(on)onPage++;
            bool group=here.Length>0&&string.Equals(p.GroupName,here,StringComparison.Ordinal);if(group)same++;
            float d=map.Distance(p);if(group&&d>=0&&d<=(app.Navigation?.Settings.CompassRange??M27CompassPolicy.Range))near++;
            sb.Append(p.Id.Substring(16)).Append('\t').Append(Path.GetFileNameWithoutExtension(p.SourceScene??"")).Append('\t')
                .Append(p.GroupName??"").Append('\t').Append(p.ZoneSource??M29GhostZonePolicy.None).Append('\t')
                .Append(page!=null?page.name:"-").Append('\t').Append(on?1:0).Append('\t').Append(group?1:0).Append('\t')
                .Append(d<0?"-":d.ToString("0",CI)).Append('\t').Append(p.Position.x.ToString("0",CI)).Append(',')
                .Append(p.Position.y.ToString("0",CI)).Append(',').Append(p.Position.z.ToString("0",CI)).Append('\t')
                .Append(p.ExactHeight?"live":"anchor").Append('\t').Append(p.Name).Append('\t').Append(M29GhostZonePolicy.Label(p.ZoneSource)).Append('\n');
        }
        string head=map.GhostZoneSummary+" paged="+paged+" onPage="+onPage+" sameGroup="+same+" inRange="+near+
            " current="+(here.Length>0?here:"-")+" atlas="+map.NativeMapOpen+" mapResolved="+map.GhostMapResolutions+
            " liveBound="+map.GhostLiveBindings+" droneLayer="+(map.Visible.Contains("Дроны")?1:0);
        Write(app,"ct-ghost.txt",head+"\nid\tscene\tgroup\tsource\tpage\tonPage\tsameGroup\tdistance\tposition\tstate\tname\tlabel\n"+sb);
        return head;
    }

    // M29 task 4: state | set <key> <value> | reset | zoom in|out | reload. Writes only navigation.cfg (no save data).
    private static string Nav(ToolkitMod app,string[] a)
    {
        var nav=app.Navigation??throw new InvalidOperationException("no navigation tools");
        string verb=a.Length>1?a[1].Trim().ToLowerInvariant():"state";
        switch(verb)
        {
            case "state": break;
            case "set":
                if(a.Length<4)throw new ArgumentException("set <key> <value>; keys as in "+M29NavigationSettings.FileName);
                if(!nav.Set(a[2],a[3]))throw new ArgumentException("rejected "+a[2]+"="+a[3]);
                break;
            case "reset": nav.ResetLook();break;
            case "zoom":
                string direction=a.Length>2?a[2].Trim().ToLowerInvariant():"";
                if(direction=="in")nav.ZoomIn();else if(direction=="out")nav.ZoomOut();else throw new ArgumentException("zoom in|out");
                break;
            case "reload": nav.Load();break;
            default: throw new ArgumentException("state|set <key> <value>|reset|zoom in|out|reload");
        }
        return nav.State();
    }

    private static string MiniMap(ToolkitMod app,string mode)
    {
        var map=app.Map??throw new InvalidOperationException("no map tools");
        var terrain=app.MiniMapTerrain;
        switch(mode)
        {
            case "off": map.ShowMiniMap=false;terrain?.SetEnabled(false);break;
            case "terrain": map.ShowMiniMap=true;terrain?.SetEnabled(true);break;
            case "radar": map.ShowMiniMap=true;terrain?.SetEnabled(false);break;
            default: throw new ArgumentException("off|radar|terrain");
        }
        return "show="+map.ShowMiniMap+" terrain="+(terrain!=null&&terrain.Enabled);
    }

    private static string Drone(ToolkitMod app,string[] a)
    {
        var d=app.DroneToggle;
        string verb=a.Length>1?a[1]:"state";
        switch(verb)
        {
            case "state": return d.Describe();
            case "find": return Write(app,"m28-drone.txt",d.FindReport())+" | "+d.Status;
            case "mode":
                if(a.Length<3||!M28DroneButtonPolicy.KnownMode(a[2]))throw new ArgumentException("mode auto|below|left|off");
                d.SetMode(a[2]);return "mode "+d.Mode;
            case "climb": d.SetClimb(a.Length>2&&a[2]!="auto"?Int(a[2],0,3):-1);return "climb set; "+d.Describe();
            case "pick": d.Pick(a.Length>2&&a[2]!="auto"?Int(a[2],0,63):-1);return "pick set; "+d.Describe();
        }
        throw new ArgumentException("m28-drone state|find|mode|climb|pick");
    }

    private static string Confirm(ToolkitMod app,string kind)
    {
        if(!UnlockIndex.TryGetValue(kind,out int index))throw new ArgumentException("palettes|styles|blueprints");
        string title=ToolkitUi.M27UnlockTitleForTest(index);
        M26AcceptanceCore.RequireTestSave(app);
        var pending=app.Pending;
        if(pending==null)return "no pending confirmation";
        if(pending.Title!=title)return "rejected: pending title '"+pending.Title+"' differs";
        app.AcceptConfirmation();
        return "accepted "+kind+(kind=="palettes"?" | "+app.Unlocks.PalettesResult:"");
    }

    private static string PlayerMarker(ToolkitMod app,string verb)
    {
        var m=app.PlayerMarker;
        switch(verb)
        {
            case "state": return "parts="+m.Parts+" captures="+m.Captures+" failures="+m.Failures+" | "+m.Status+" | last: "+m.LastNote;
            case "capture": m.RequestCapture();return "capture requested (map open="+app.Map.NativeMapOpen+")";
            case "dump": return Write(app,"m28-player-marker.txt",m.Dump());
            case "forget": m.Forget();return m.Status;
        }
        throw new ArgumentException("m28-player-marker state|capture|dump|forget");
    }

    private static string CustomAdd(ToolkitMod app,string color,string name)
    {
        M26AcceptanceCore.RequireTestSave(app);
        var c=app.Custom;
        int index=Int(color,0,ExplorerPolicy.ColorCount-1);
        var before=new HashSet<string>(c.Records.Select(r=>r.Id),StringComparer.Ordinal);
        string oldName=c.DraftName;int oldColor=c.DraftColor;
        try
        {
            c.DraftName=string.IsNullOrWhiteSpace(name)?"CT "+DateTime.Now.ToString("HHmmss",CI):name.Trim();
            c.DraftColor=index;
            c.SaveCurrent();
        }
        finally{c.DraftName=oldName;c.DraftColor=oldColor;}
        var record=c.Records.FirstOrDefault(r=>!before.Contains(r.Id))??throw new InvalidOperationException("marker not added");
        _lastCustom=record.Id;
        return "added "+record.Id+" '"+record.Name+"' color "+record.Color+" at "+record.X.ToString("0.#",CI)+","+record.Y.ToString("0.#",CI)+","+record.Z.ToString("0.#",CI);
    }
    private static string CustomId(string token)
    {
        if(token!="last")return token;
        if(_lastCustom.Length==0)throw new InvalidOperationException("no marker was added by m28-custom-add in this session");
        return _lastCustom;
    }

    private static string Tracking(ToolkitMod app)
    {
        if(!app.Ready)throw new InvalidOperationException("world not ready");
        // The list is otherwise rebuilt only while the tracking page is visible.
        app.Tracking.Refresh(true);
        var rows=app.Tracking.Rows;
        var sb=new StringBuilder();
        var summary=new StringBuilder();
        sb.Append("tracking rows=").Append(rows.Count).Append(" status=").AppendLine(app.Tracking.Status);
        for(int k=0;k<M27TrackingPolicy.Kinds.Length;k++)
        {
            var t=M27TrackingPolicy.Count(rows,k);
            string line=M27TrackingPolicy.Kinds[k]+"="+t.Done+"/"+t.Total+" pending "+t.Pending+" unknown "+t.Unknown+" unvisited "+t.Unvisited;
            sb.AppendLine(line);summary.Append(k==0?"":"; ").Append(line);
        }
        foreach(var g in rows.Where(r=>r.Unvisited).GroupBy(r=>r.Kind+" | "+r.Zone).OrderBy(g=>g.Key,StringComparer.Ordinal))
            sb.Append("unvisited ").Append(g.Key).Append(": ").Append(g.Count()).AppendLine();
        foreach(var g in rows.GroupBy(r=>r.Kind+" | "+r.State).OrderBy(g=>g.Key,StringComparer.Ordinal))
            sb.Append("state ").Append(g.Key).Append(": ").Append(g.Count()).AppendLine();
        return Write(app,"m28-tracking.txt",sb.ToString())+" | "+summary;
    }

    // ---- UI inspection ----------------------------------------------------------
    private static string UiDump(ToolkitMod app,string target,string depthText)
    {
        int depth=M28TestPolicy.Depth(depthText);
        var roots=new List<Transform>();
        var map=app.Map;
        string t=target.ToLowerInvariant();
        if(t=="map"||t=="drone"||t=="marker")
        {
            if(map==null||!map.NativeMapOpen||map.Ui==null)throw new InvalidOperationException("native map is not open");
            if(t=="map")roots.Add(map.Ui.transform);
            else if(t=="drone")roots.Add((map.Ui._droneMenu??throw new InvalidOperationException("no drone menu container")).transform);
            else
            {
                var marker=map.Ui._playerMarker??throw new InvalidOperationException("no player marker");
                roots.Add(marker.transform);
                var below=marker._moveBelowMarkersTransform;
                if(below!=null&&!IsUnder(below,marker.transform))roots.Add(below);
            }
        }
        else if(t=="compass")roots.Add((UnityEngine.Object.FindObjectOfType<CompassBarUI>()??throw new InvalidOperationException("no CompassBarUI")).transform);
        else if(t=="canvases")
        {
            foreach(var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())if(canvas!=null&&canvas.isRootCanvas)roots.Add(canvas.transform);
            if(depthText.Length==0)depth=2;
        }
        else if(t.StartsWith("name:",StringComparison.Ordinal)&&target.Length>5)
        {
            string part=target.Substring(5);
            foreach(var rt in UnityEngine.Object.FindObjectsOfType<RectTransform>())
                if(rt!=null&&DroneToggleTools.PathOf(rt).IndexOf(part,StringComparison.OrdinalIgnoreCase)>=0){roots.Add(rt);if(roots.Count>=5)break;}
        }
        else throw new ArgumentException("target map|drone|marker|compass|canvases|name:<path part>");
        if(roots.Count==0)throw new InvalidOperationException("nothing found for "+target);
        var lines=new List<string>{"ct-ui-dump "+target+" depth="+depth+" screen="+Screen.width+"x"+Screen.height+" (box: top-left screen px x,y wxh)"};
        foreach(var root in roots)
        {
            if(lines.Count>=M28TestPolicy.MaxDumpLines)break;
            lines.Add("== "+DroneToggleTools.PathOf(root));
            Node(app,root,0,depth,lines);
        }
        if(lines.Count>=M28TestPolicy.MaxDumpLines)lines.Add("... truncated at "+M28TestPolicy.MaxDumpLines+" lines");
        return Write(app,"ct-ui-"+M28TestPolicy.SafeTag(target)+".txt",string.Join("\n",lines)+"\n")+" roots="+roots.Count;
    }
    private static void Node(ToolkitMod app,Transform t,int level,int depth,List<string> lines)
    {
        if(t==null||lines.Count>=M28TestPolicy.MaxDumpLines)return;
        var sb=new StringBuilder(160);
        sb.Append(' ',level*2).Append(t.gameObject.name);
        if(!t.gameObject.activeSelf)sb.Append(" [off]");else if(!t.gameObject.activeInHierarchy)sb.Append(" [hidden]");
        var rt=t.TryCast<RectTransform>();
        if(rt!=null)
        {
            if(TryBox(rt,out var box))sb.Append(" box=").Append(M28TestPolicy.Box(box.x,box.y,box.width,box.height));
            sb.Append(" pos=").Append(V(rt.anchoredPosition)).Append(" size=").Append(V(rt.rect.size)).Append(" pivot=").Append(V(rt.pivot))
              .Append(" anchors=").Append(V(rt.anchorMin)).Append('-').Append(V(rt.anchorMax));
        }
        var euler=t.localEulerAngles;
        if(Math.Abs(Mathf.DeltaAngle(0,euler.z))>.05f||Math.Abs(Mathf.DeltaAngle(0,euler.x))>.05f||Math.Abs(Mathf.DeltaAngle(0,euler.y))>.05f)
            sb.Append(" rot=").Append(F(euler.x)).Append(',').Append(F(euler.y)).Append(',').Append(F(euler.z));
        var scale=t.localScale;
        if(Math.Abs(scale.x-1)>.001f||Math.Abs(scale.y-1)>.001f||Math.Abs(scale.z-1)>.001f)sb.Append(" scale=").Append(F(scale.x)).Append(',').Append(F(scale.y)).Append(',').Append(F(scale.z));
        foreach(var component in t.GetComponents<Component>())
        {
            string text;
            try{text=Describe(app,component);}catch(Exception e){text="?"+e.GetType().Name;}
            if(text.Length>0)sb.Append(" | ").Append(text);
        }
        lines.Add(sb.ToString());
        if(level+1>=depth)
        {
            if(t.childCount>0)lines.Add(new string(' ',(level+1)*2)+"... "+t.childCount+" children");
            return;
        }
        for(int i=0;i<t.childCount;i++)Node(app,t.GetChild(i),level+1,depth,lines);
    }
    private static string Describe(ToolkitMod app,Component c)
    {
        if(c==null)return "";
        var type=c.GetIl2CppType();string name=type.Name;
        if(name=="RectTransform"||name=="Transform"||name=="CanvasRenderer")return "";
        var sb=new StringBuilder(name);
        var behaviour=c.TryCast<Behaviour>();
        if(behaviour!=null&&!behaviour.enabled)sb.Append("(disabled)");
        var image=c.TryCast<UiImage>();
        if(image!=null)
        {
            sb.Append(" sprite=").Append(image.sprite!=null?image.sprite.name:"-").Append(" type=").Append(image.type).Append(" color=").Append(C(image.color));
            if((int)image.type==3)sb.Append(" fill=").Append(F(image.fillAmount)).Append(" method=").Append(image.fillMethod).Append(" origin=").Append(image.fillOrigin).Append(image.fillClockwise?" cw":" ccw");
            if(image.preserveAspect)sb.Append(" aspect");
            if(!image.raycastTarget)sb.Append(" noRaycast");
            var material=image.material;
            if(material!=null&&material.name!="Default UI Material")sb.Append(" mat=").Append(material.name);
            return sb.ToString();
        }
        var raw=c.TryCast<UiRawImage>();
        if(raw!=null){sb.Append(" tex=").Append(raw.texture!=null?raw.texture.name:"-").Append(" uv=").Append(raw.uvRect).Append(" color=").Append(C(raw.color));return sb.ToString();}
        var tmp=c.TryCast<TmpText>();
        if(tmp!=null){sb.Append(" '").Append(M28TestPolicy.OneLine(tmp.text,60)).Append("' size=").Append(F(tmp.fontSize));return sb.ToString();}
        var text=c.TryCast<UiText>();
        if(text!=null){sb.Append(" '").Append(M28TestPolicy.OneLine(text.text,60)).Append('\'');return sb.ToString();}
        var canvas=c.TryCast<Canvas>();
        if(canvas!=null){sb.Append(" mode=").Append(canvas.renderMode).Append(" order=").Append(canvas.sortingOrder).Append(canvas.overrideSorting?" override":"").Append(canvas.isRootCanvas?" root":"");return sb.ToString();}
        var group=c.TryCast<CanvasGroup>();
        if(group!=null){sb.Append(" alpha=").Append(F(group.alpha)).Append(" interactable=").Append(group.interactable).Append(" blocks=").Append(group.blocksRaycasts);return sb.ToString();}
        var mask=c.TryCast<UiMask>();
        if(mask!=null){sb.Append(" show=").Append(mask.showMaskGraphic);return sb.ToString();}
        var selectable=c.TryCast<UiSelectable>();
        if(selectable!=null)
        {
            sb.Append(" interactable=").Append(selectable.interactable);
            var button=c.TryCast<UiButton>();
            if(button!=null)Persistent(sb,button.onClick);
            return sb.ToString();
        }
        var trigger=c.TryCast<EventTrigger>();
        if(trigger!=null)
        {
            var entries=trigger.triggers;
            for(int i=0;entries!=null&&i<entries.Count&&i<8;i++){var e=entries[i];sb.Append(' ').Append(e.eventID).Append(':');Persistent(sb,e.callback);}
            return sb.ToString();
        }
        var invoke=c.TryCast<InvokeInputEventOnPointerClick>();
        if(invoke!=null){sb.Append(" event=").Append(invoke._event!=null?invoke._event.name:"-");return sb.ToString();}
        var perform=c.TryCast<PerformInputEventOnPointerClick>();
        if(perform!=null){sb.Append(" event=").Append(perform._event!=null?perform._event.name:"-");return sb.ToString();}
        if(c.TryCast<MonoBehaviour>()!=null&&app.DroneToggle!=null)
        {
            int shown=0;
            try{foreach(var ev in app.DroneToggle.EventFields(c)){if(shown++>=4)break;Persistent(sb,ev);}}catch{}
        }
        return sb.ToString();
    }
    private static void Persistent(StringBuilder sb,UnityEventBase ev)
    {
        if(ev==null)return;
        int n=ev.GetPersistentEventCount();
        for(int i=0;i<n&&i<4;i++)
        {
            var target=ev.GetPersistentTarget(i);
            sb.Append(" on=").Append(target!=null?target.name:"-").Append('.').Append(ev.GetPersistentMethodName(i));
        }
    }

    private static string Hit(ToolkitMod app,string xs,string ys)
    {
        if(!M28TestPolicy.TryScreenPoint(xs,ys,Screen.width,Screen.height,out float x,out float y))
            throw new ArgumentException("x y: a top-left screen pixel inside "+Screen.width+"x"+Screen.height);
        var system=EventSystem.current??throw new InvalidOperationException("no EventSystem");
        var data=new PointerEventData(system){position=new Vector2(x,Screen.height-y)};
        var results=new Il2CppSystem.Collections.Generic.List<RaycastResult>();
        system.RaycastAll(data,results);
        var lines=new List<string>{"ct-ui-hit "+F(x)+","+F(y)+" top-left (unity "+F(x)+","+F(Screen.height-y)+") hits="+results.Count+
            " menu="+app.MenuOpen+" map="+(app.Map!=null&&app.Map.NativeMapOpen)+" atlasPanel="+(app.Map!=null&&app.Map.PanelExpanded?"expanded":"collapsed")};
        string top="-";
        for(int i=0;i<results.Count&&i<M28TestPolicy.MaxHits;i++)
        {
            var r=results[i];var go=r.gameObject;
            string path=go!=null?DroneToggleTools.PathOf(go.transform):"-";
            if(i==0)top=path;
            var module=r.module;
            lines.Add("#"+i+" "+path+" module="+(module!=null?module.GetIl2CppType().Name+":"+module.name:"-")+
                " depth="+r.depth+" order="+r.sortingOrder+" layer="+r.sortingLayer+" dist="+F(r.distance)+
                (go!=null&&app.DroneToggle!=null&&IsDroneTarget(app,go.transform)?" <- drone button":""));
        }
        Write(app,"ct-ui-hit.txt",string.Join("\n",lines)+"\n");
        return "hits="+results.Count+" top="+top;
    }
    private static bool IsDroneTarget(ToolkitMod app,Transform t)
    {
        try{var c=t.GetComponent<RectTransform>();return c!=null&&app.DroneToggle.Shielded(c);}catch{return false;}
    }

    private static string Find(ToolkitMod app,string text)
    {
        if(string.IsNullOrWhiteSpace(text))throw new ArgumentException("text to find");
        var lines=new List<string>{"ct-ui-find '"+text+"' (path or TMP text; box: top-left screen px)"};
        int count=0;
        foreach(var rt in UnityEngine.Object.FindObjectsOfType<RectTransform>())
        {
            if(rt==null)continue;
            string path=DroneToggleTools.PathOf(rt);
            if(path.IndexOf(text,StringComparison.OrdinalIgnoreCase)<0)continue;
            if(++count<=M28TestPolicy.MaxFind)lines.Add((TryBox(rt,out var b)?M28TestPolicy.Box(b.x,b.y,b.width,b.height):"-")+" "+path);
        }
        foreach(var label in UnityEngine.Object.FindObjectsOfType<TmpText>())
        {
            if(label==null||string.IsNullOrEmpty(label.text)||label.text.IndexOf(text,StringComparison.OrdinalIgnoreCase)<0)continue;
            var rt=label.rectTransform;
            if(++count<=M28TestPolicy.MaxFind)lines.Add((rt!=null&&TryBox(rt,out var b)?M28TestPolicy.Box(b.x,b.y,b.width,b.height):"-")+" text '"+M28TestPolicy.OneLine(label.text,60)+"' "+DroneToggleTools.PathOf(label.transform));
        }
        if(count>M28TestPolicy.MaxFind)lines.Add("... "+(count-M28TestPolicy.MaxFind)+" more");
        Write(app,"ct-ui-find.txt",string.Join("\n",lines)+"\n");
        return count+" matches";
    }

    private static bool TryBox(RectTransform rt,out Rect box)
    {
        box=default;
        var canvas=rt.GetComponentInParent<Canvas>();
        var root=canvas!=null?canvas.rootCanvas:null;
        var camera=root!=null&&root.renderMode!=RenderMode.ScreenSpaceOverlay?root.worldCamera:null;
        var corners=new Il2CppStructArray<Vector3>(4);
        rt.GetWorldCorners(corners);
        float minX=float.MaxValue,minY=float.MaxValue,maxX=float.MinValue,maxY=float.MinValue;
        for(int i=0;i<4;i++)
        {
            var s=RectTransformUtility.WorldToScreenPoint(camera,corners[i]);
            minX=Math.Min(minX,s.x);maxX=Math.Max(maxX,s.x);minY=Math.Min(minY,s.y);maxY=Math.Max(maxY,s.y);
        }
        if(!float.IsFinite(minX)||!float.IsFinite(maxY))return false;
        box=new Rect(minX,Screen.height-maxY,maxX-minX,maxY-minY);
        return true;
    }
    private static bool IsUnder(Transform transform,Transform ancestor)
    {
        for(var t=transform;t!=null;t=t.parent)if(t.Pointer==ancestor.Pointer)return true;
        return false;
    }
    private static string F(float v)=>float.IsFinite(v)?v.ToString("0.##",CI):"nan";
    private static string V(Vector2 v)=>F(v.x)+","+F(v.y);
    private static string C(Color c)=>F(c.r)+","+F(c.g)+","+F(c.b)+","+F(c.a);

    internal static string Diagnostics(ToolkitMod app)
    {
        if(!Enabled(app))return "";
        var sb=new StringBuilder(768);
        sb.Append("CtTest: ").Append(Last).Append('\n');
        try
        {
            if(app.DroneToggle!=null)sb.Append("M28Drone: ").Append(app.DroneToggle.Describe()).Append('\n');
            var c=app.Compass;
            if(c!=null)sb.Append("M28Compass: style=").Append(c.Style).Append(" styled=").Append(c.Styled).Append(" failures=").Append(c.StyleFailures)
              .Append(" anchors=").Append(c.Active).Append(" | ").Append(c.StyleStatus).Append(" | ").Append(c.Status).Append('\n');
            var m=app.PlayerMarker;
            if(m!=null)sb.Append("M28Marker: parts=").Append(m.Parts).Append(" captures=").Append(m.Captures).Append(" failures=").Append(m.Failures)
              .Append(" | ").Append(m.Status).Append(" | last: ").Append(m.LastNote).Append('\n');
            sb.Append("M28Unlocks: cards=").Append(ToolkitUi.M28UnlockCountForTest).Append(" palettes=").Append(app.Unlocks?.PalettesResult).Append('\n');
            sb.Append("M28Atlas: map=").Append(app.Map!=null&&app.Map.NativeMapOpen).Append(" panelExpanded=").Append(app.Map!=null&&app.Map.PanelExpanded).Append('\n');
        }
        catch(Exception e){sb.Append("M28DiagnosticsError: ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');}
        return sb.ToString();
    }
}
