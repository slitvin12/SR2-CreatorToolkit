using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEngine;
namespace CreatorToolkit;
// M27 in-game acceptance relay for the local diagnostic channel (cmd.txt / status.txt).
// Every m27-* command is ignored unless the explicit opt-in file m27-acceptance.enabled
// exists in UserData\CreatorToolkit. UI navigation and read-only dumps only, except the
// M27 save changes (styles, blueprints, one door) and the autofeed opt-in: those go through
// the same confirmation as the UI buttons, only on the dedicated test save, and a
// confirmation is accepted only when the pending title is the exact expected one.
internal sealed partial class ToolkitUi
{
    internal void M27ShowUnlockForTest(int section)
    {
        ShowPage(4);_unlockSection=Mathf.Clamp(section,0,UnlockSections.Length-1);_trackScroll=Vector2.zero;
    }
    internal void M27TrackingForTest(int kind,int filter)
    {
        M27ShowUnlockForTest(1);
        _trackKind=M27TrackingPolicy.ClampKind(kind);_trackFilter=M27TrackingPolicy.ClampFilter(_trackKind,filter);
        _trackSearch="";_trackSelected="";_trackScroll=Vector2.zero;
    }
    internal void M27TrackSearchForTest(string text){_trackSearch=text??"";_trackScroll=Vector2.zero;_trackSelected="";}
    internal List<M27TrackingPolicy.Row> M27TrackViewForTest()=>M27TrackingPolicy.Select(_app.Tracking.Rows,_trackKind,_trackFilter,_trackSearch);
    internal M27TrackingPolicy.Row M27TrackSelectedForTest=>
        string.IsNullOrEmpty(_trackSelected)?null:_app.Tracking.Rows.FirstOrDefault(r=>r.Key==_trackSelected);
    // token: first | last | <index in the current view> | <row id>
    internal M27TrackingPolicy.Row M27TrackSelectForTest(string token)
    {
        var view=M27TrackViewForTest();
        int index=token=="first"?0:token=="last"?view.Count-1:-1;
        if(index<0&&!int.TryParse(token,NumberStyles.None,CultureInfo.InvariantCulture,out index))
            index=view.FindIndex(r=>string.Equals(r.Id,token,StringComparison.OrdinalIgnoreCase));
        if(index<0||index>=view.Count)return null;
        var row=view[index];
        _trackSelected=row.Key;_trackScroll=new Vector2(0,Mathf.Max(0,index*48f-96f));
        return row;
    }
    // Same confirmation (title, text, action) as the «Разблокировать…» card button.
    internal string M27RequestUnlockForTest(int i)
    {
        var actions=UnlockActions();
        _app.Confirm(UnlockTitles[i],UnlockDetails[i]+"\nЭто постоянное изменение текущего сейва. Автоматического отката нет. Подтвердите намерение.",actions[i]);
        return UnlockTitles[i];
    }
    // Same confirmation as the «Открыть дверь…» button of the tracking list.
    internal string M27RequestDoorForTest(string id)
    {
        var tools=_app.Tracking;
        _app.Confirm("Открыть дверь?","Дверь "+id+" будет открыта в модели сейва (AccessDoor). "+
            "Это постоянное изменение текущего сейва; автоматического отката нет.",()=>tools.OpenDoor(id));
        return "Открыть дверь?";
    }
    internal static string M27UnlockTitleForTest(int i)=>UnlockTitles[i];
    internal static int M28UnlockCountForTest=>UnlockTitles.Length;
    internal string M27UiStateForTest=>"tab="+_tab+" unlockSection="+_unlockSection+" trackKind="+_trackKind+" trackFilter="+_trackFilter+
        " search='"+_trackSearch+"' selected="+(_trackSelected.Length>0?_trackSelected:"none")+" view="+_trackView.Count+
        " explorer="+_explorerSection+" menu="+_app.MenuOpen;
}
internal static class M27AcceptanceIpc
{
    internal const string OptInFile="m27-acceptance.enabled";
    internal static string Last="none";
    private static bool _enabled;
    private static float _nextEnabledCheck=-1;
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    // Save-changing unlocks reachable from the relay: the two new M27 cards only.
    private static readonly Dictionary<string,int> Unlocks=new(StringComparer.Ordinal){["styles"]=4,["blueprints"]=5};
    internal static bool Enabled(ToolkitMod app)
    {
        if(Time.unscaledTime>=_nextEnabledCheck)
        {
            _nextEnabledCheck=Time.unscaledTime+5;
            try{_enabled=File.Exists(Path.Combine(app.DataDirectory,OptInFile));}catch{_enabled=false;}
        }
        return _enabled;
    }
    private static bool Run(ToolkitMod app,string name,Func<string> body)
    {
        _nextEnabledCheck=-1; // every explicit command re-reads the opt-in file
        if(!Enabled(app)){Last=name+" ignored: opt-in file missing";app.LoggerInstance.Warning("M27 acceptance: "+Last);return true;}
        try{Last=name+": "+body();}
        catch(Exception e){Last=name+" error: "+e.GetType().Name+": "+e.Message;}
        Last=DateTime.Now.ToString("HH:mm:ss",CultureInfo.InvariantCulture)+" "+Last;
        app.LoggerInstance.Msg("M27 acceptance: "+Last);
        return true;
    }
    private static int Int(string s,int min,int max)
    {
        int v=int.Parse(s,NumberStyles.Integer,CultureInfo.InvariantCulture);
        if(v<min||v>max)throw new ArgumentOutOfRangeException(nameof(s));
        return v;
    }
    private static string RowText(M27TrackingPolicy.Row row)=>row==null?"none":
        row.Kind+"|"+row.Id+" '"+row.Name+"' zone='"+row.Zone+"' state='"+row.State+"' done="+(row.Done?.ToString()??"?")+
        " dist="+row.Distance.ToString("0.#",CultureInfo.InvariantCulture)+" point="+(row.Tag is MapTools.Point p?(p.Group!=null?"group":"no-group"):"none");
    internal static bool Handle(ToolkitMod app,ToolkitUi ui,string command,Action<string,Action> navigate)
    {
        if(!command.StartsWith("m27-",StringComparison.Ordinal))return false;
        var a=command.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        string Arg(int i)=>a.Length>i?a[i]:throw new ArgumentException("missing argument "+i);
        switch(a[0])
        {
            case "m27-unlocks": return Run(app,a[0],()=>{navigate(command,()=>ui.M27ShowUnlockForTest(0));return "navigation requested";});
            case "m27-tracking": return Run(app,a[0],()=>
            {
                int kind=Int(Arg(1),0,M27TrackingPolicy.Kinds.Length-1),filter=a.Length>2?Int(a[2],0,2):0;
                navigate(command,()=>ui.M27TrackingForTest(kind,filter));
                app.Queue("m27 acceptance tracking refresh",()=>app.Tracking.Refresh(true));
                return "kind "+M27TrackingPolicy.Kinds[kind]+" filter "+M27TrackingPolicy.Filters(kind)[M27TrackingPolicy.ClampFilter(kind,filter)];
            });
            case "m27-track-search": return Run(app,a[0],()=>{string text=string.Join(" ",a.Skip(1));ui.M27TrackSearchForTest(text);return "search '"+text+"' view="+ui.M27TrackViewForTest().Count;});
            case "m27-track-select": return Run(app,a[0],()=>"selected "+RowText(ui.M27TrackSelectForTest(Arg(1))));
            case "m27-track-dump": return Run(app,a[0],()=>Dump(app,a.Length>1?a[1]:"x"));
            case "m27-track-atlas": return Run(app,a[0],()=>
            {
                var point=ui.M27TrackSelectedForTest?.Tag as MapTools.Point??throw new InvalidOperationException("selected row has no atlas point");
                if(app.Map.Busy)throw new InvalidOperationException("transit in progress");
                app.Queue("m27 acceptance tracking atlas",()=>app.Map.OpenAt(point));
                return "queued OpenAt "+point.Id;
            });
            case "m27-track-teleport": return Run(app,a[0],()=>
            {
                M26AcceptanceCore.RequireTestSave(app);
                var point=ui.M27TrackSelectedForTest?.Tag as MapTools.Point??throw new InvalidOperationException("selected row has no atlas point");
                if(point.Group==null)throw new InvalidOperationException("scene group not confirmed");
                if(app.Map.Busy)throw new InvalidOperationException("transit in progress");
                app.Queue("m27 acceptance tracking teleport",()=>app.Map.RequestTeleport(point));
                return "queued RequestTeleport "+point.Id;
            });
            case "m27-explorer": return Run(app,a[0],()=>{int section=Int(Arg(1),0,4);navigate(command,()=>ui.ShowExplorerForTest(section));return "section "+section;});
            // Same three states as the atlas page button: off -> terrain -> radar.
            case "m27-minimap": return Run(app,a[0],()=>
            {
                string mode=Arg(1);var map=app.Map;var terrain=app.MiniMapTerrain;
                if(mode=="off"){map.ShowMiniMap=false;terrain.SetEnabled(false);}
                else if(mode=="terrain"){map.ShowMiniMap=true;terrain.SetEnabled(true);}
                else if(mode=="radar"){map.ShowMiniMap=true;terrain.SetEnabled(false);}
                else throw new ArgumentException("usage: m27-minimap off|terrain|radar");
                return "show="+map.ShowMiniMap+" terrain="+terrain.Enabled;
            });
            case "m27-compass": return Run(app,a[0],()=>
            {
                bool on=Arg(1)=="on";if(!on&&a[1]!="off")throw new ArgumentException("usage: m27-compass on|off");
                app.Queue("m27 acceptance compass markers",()=>app.Compass.SetEnabled(on));
                return "queued "+(on?"on":"off");
            });
            case "m27-autofeed": return Run(app,a[0],()=>
            {
                string mode=Arg(1);
                if(mode=="off"){app.Queue("m27 acceptance autofeed off",()=>app.AutoFeed.Request(false));return "queued off";}
                if(mode!="on")throw new ArgumentException("usage: m27-autofeed on|off");
                M26AcceptanceCore.RequireTestSave(app);
                app.AutoFeed.Request(true);
                return "pending "+(app.Pending?.Title??"none");
            });
            // Live autofeed fixture: spawn/hungry change the world (test save only); state is read-only.
            case "m27-feedfixture": return Run(app,a[0],()=>
            {
                string mode=Arg(1);
                if(mode=="state")return M27FeedFixture.State(app);
                M26AcceptanceCore.RequireTestSave(app);
                if(mode=="spawn")return M27FeedFixture.Spawn(app,a.Length>2?Int(a[2],1,6):3);
                if(mode=="hungry")return M27FeedFixture.Hungry(app);
                throw new ArgumentException("usage: m27-feedfixture spawn [1-6]|hungry|state");
            });
            case "m27-unlock": return Run(app,a[0],()=>
            {
                M26AcceptanceCore.RequireTestSave(app);
                if(!Unlocks.TryGetValue(Arg(1),out int i))throw new ArgumentException("usage: m27-unlock styles|blueprints");
                return "pending "+ui.M27RequestUnlockForTest(i);
            });
            case "m27-door": return Run(app,a[0],()=>
            {
                M26AcceptanceCore.RequireTestSave(app);
                var row=ui.M27TrackSelectedForTest;
                if(row==null||row.Kind!=M27TrackingPolicy.Doors||row.Done!=false)throw new InvalidOperationException("select a closed door first");
                return "pending "+ui.M27RequestDoorForTest(row.Id)+" "+row.Id;
            });
            case "m27-confirm": return Run(app,a[0],()=>Confirm(app,Arg(1)));
            case "m27-cancel": return Run(app,a[0],()=>{string title=app.Pending?.Title;app.Pending=null;return "cleared "+(title??"none");});
            // Native map drone panel (MapUI._droneMenu): UI-only show/hide while the atlas is open.
            case "m27-drone": return Run(app,a[0],()=>
            {
                string mode=Arg(1);var map=app.Map;
                if(!map.NativeMapOpen)throw new InvalidOperationException("native map is not open");
                var container=map.Ui._droneMenu??throw new InvalidOperationException("MapUI has no drone menu");
                if(mode=="show")container.Show(true);else if(mode=="hide")container.Hide(true);
                else if(mode!="state")throw new ArgumentException("usage: m27-drone show|hide|state");
                return DroneNative(app);
            });
            // Read-only: open atlas art/layout dump for the minimap terrain calibration.
            case "m27-mapprobe": return Run(app,a[0],()=>
            {
                if(!app.Map.NativeMapOpen)throw new InvalidOperationException("native map is not open");
                string text=app.MiniMapTerrain.Probe(app.Map);
                File.WriteAllText(Path.Combine(app.DataDirectory,"m27-mapprobe.txt"),text);
                int end=text.IndexOf('\n');
                return end>0?text.Substring(0,Math.Min(end,240)):text;
            });
            // Read-only: north-up PNG copies of the minimap composite and cached layer readbacks.
            case "m27-minimapdump": return Run(app,a[0],()=>app.MiniMapTerrain.Dump(Path.Combine(app.DataDirectory,"m27-minimap")));
            // Read-only: native zone polygons of the open atlas page vs indexed pods/gordos.
            case "m27-zoneprobe": return Run(app,a[0],()=>
            {
                string text=ZoneProbe(app.Map);
                File.WriteAllText(Path.Combine(app.DataDirectory,"m27-zoneprobe.txt"),text);
                int end=text.IndexOf('\n');
                return end>0?text.Substring(0,Math.Min(end,240)):text;
            });
        }
        return false;
    }
    private static string Confirm(ToolkitMod app,string kind)
    {
        string title=kind switch
        {
            "styles"=>ToolkitUi.M27UnlockTitleForTest(4),
            "blueprints"=>ToolkitUi.M27UnlockTitleForTest(5),
            "door"=>"Открыть дверь?",
            "autofeed"=>"Включить автокормление загонов?",
            _=>throw new ArgumentException("unknown confirmation "+kind)
        };
        M26AcceptanceCore.RequireTestSave(app);
        var pending=app.Pending;
        if(pending==null)return "no pending confirmation";
        if(pending.Title!=title)return "rejected: pending title differs";
        app.AcceptConfirmation();
        return "accepted "+kind;
    }
    private static string ZoneProbe(MapTools map)
    {
        var ui=map.Ui;
        if(ui==null||!map.NativeMapOpen)throw new InvalidOperationException("native map is not open");
        var sb=new StringBuilder();
        var areas=ui._zoneDetectionAreas;
        int n=areas?.Length??0;
        string Zone(Il2CppMonomiPark.SlimeRancher.World.ZoneDefinition z)=>z==null?"none":z.name+"/"+(MapTools.ZoneName(z)??"?");
        sb.Append("page=").Append(ui.CurrentMapDefinition?.name).Append(" areas=").Append(n).Append('\n');
        var rect=ui._map!=null?ui._map._rectTransform:null;
        if(rect!=null)
        {
            var corners=new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);rect.GetWorldCorners(corners);var canvas=rect.GetComponentInParent<Canvas>();
            sb.Append("maprect ").Append(corners[0].ToString("F0")).Append("..").Append(corners[2].ToString("F0")).Append(" canvas=").Append(canvas!=null?canvas.renderMode.ToString():"none").Append('\n');
        }
        for(int i=0;i<n;i++)
        {
            var area=areas[i];
            if(area==null){sb.Append("area ").Append(i).Append(" null\n");continue;}
            var b=area._bounds;
            sb.Append("area ").Append(i).Append(' ').Append(Zone(area.ZoneMapData?.PrimaryZone)).Append(" pts=").Append(area._points?.Count??0)
              .Append(" center=").Append(b.center.ToString("F0")).Append(" size=").Append(b.size.ToString("F0")).Append(" active=").Append(area.isActiveAndEnabled)
              .Append(" t=").Append(AreaTransform(area)).Append(" p0=").Append(area._points!=null&&area._points.Count>0?area._points[0].ToString("F0"):"-")
              .Append(" wc=").Append(WorldCenter(ui,area)).Append('\n');
        }
        Vector2 Raw(Vector3 p)=>new Vector2(p.x,p.z);
        Vector2 UiPos(Vector3 p){if(rect==null)return new Vector2(float.NaN,float.NaN);var w=rect.TransformPoint(ui._map.GetMapPosition(p,rect));return new Vector2(w.x,w.y);}
        string Hits(Vector2 v)
        {
            var parts=new List<string>();
            for(int i=0;i<n;i++){var area=areas[i];if(area!=null&&area.ContainsPoint(v))parts.Add(i+":"+(area.ZoneMapData?.PrimaryZone?.name??"none"));}
            return string.Join(",",parts);
        }
        string First(Vector2 v)=>ui.TryFindZoneAt(v)?.ZoneMapData?.PrimaryZone?.name??"none";
        if(map.HasPlayerPosition)
        {
            var pp=map.PlayerPosition;
            sb.Append("player ").Append(pp.ToString("F0")).Append(" raw=").Append(First(Raw(pp))).Append(" ui=").Append(First(UiPos(pp))).Append(" uiPos=").Append(UiPos(pp).ToString("F0"));
            try{sb.Append(" native=").Append(Zone(Il2CppMonomiPark.SlimeRancher.UI.Map.MapUIUtilities.GetPlayerCurrentZone()));}catch(Exception e){sb.Append(" native=err:").Append(e.Message);}
            sb.Append('\n');
        }
        string Tally(Dictionary<string,int> t)=>string.Join(", ",t.OrderByDescending(x=>x.Value).Select(x=>x.Key+"="+x.Value));
        foreach(var group in map.Points.Where(p=>p!=null).GroupBy(p=>(p.GroupName??"(null)")+" ["+p.Category+"]"))
        {
            var raw=new Dictionary<string,int>();var uit=new Dictionary<string,int>();int multi=0,total=0;var lines=new StringBuilder();
            foreach(var p in group)
            {
                total++;string r=First(Raw(p.Position)),u=First(UiPos(p.Position));
                raw[r]=raw.TryGetValue(r,out int c)?c+1:1;uit[u]=uit.TryGetValue(u,out c)?c+1:1;
                string hits=Hits(UiPos(p.Position));
                if(hits.Contains(','))multi++;
                if(total<=2||hits.Contains(',')&&multi<=2)lines.Append("  ").Append(p.Id).Append(' ').Append(p.Position.ToString("F0")).Append(" raw=").Append(r).Append(" ui=").Append(u).Append(" all=").Append(hits).Append('\n');
            }
            string key=group.First().GroupName;
            sb.Append("group ").Append(group.Key).Append(" n=").Append(total).Append(" title=").Append(key!=null&&MapTools.ZoneTitleByGroup.TryGetValue(key,out var gt)?gt:"-").Append(" multi=").Append(multi)
              .Append(" raw: ").Append(Tally(raw)).Append(" | ui: ").Append(Tally(uit)).Append('\n').Append(lines);
        }
        return sb.ToString();
    }
    private static string AreaTransform(Il2CppMonomiPark.SlimeRancher.UI.Map.ZoneDetectionArea area)
    {
        try{var t=area._transform;if(t==null)return "null";var c=t.GetComponentInParent<Canvas>();
            return t.name+"@"+t.position.ToString("F0")+" s="+t.lossyScale.ToString("F3")+" canvas="+(c!=null?c.renderMode.ToString():"none");}
        catch(Exception e){return "err:"+e.Message;}
    }
    private static string WorldCenter(Il2CppMonomiPark.SlimeRancher.UI.Map.MapUI ui,Il2CppMonomiPark.SlimeRancher.UI.Map.ZoneDetectionArea area)
    {
        try{var z=area.ZoneMapData?.PrimaryZone;return z==null?"-":ui.FindZoneWorldCenter(z).ToString("F0");}catch(Exception e){return "err:"+e.Message;}
    }
    private static string Dump(ToolkitMod app,string tag)
    {
        if(!app.Ready)throw new InvalidOperationException("world not ready");
        app.Tracking.Refresh(true);
        var rows=new List<Dictionary<string,object>>();
        foreach(var row in app.Tracking.Rows)
        {
            var point=row.Tag as MapTools.Point;
            rows.Add(new Dictionary<string,object>{["Kind"]=M27TrackingPolicy.Kinds[M27TrackingPolicy.ClampKind(row.Kind)],["Id"]=row.Id,["Name"]=row.Name,
                ["Zone"]=row.Zone,["State"]=row.State,["Done"]=row.Done,["Distance"]=Math.Round(row.Distance,1),
                ["Point"]=point!=null,["Group"]=point?.Group!=null?point.Group.name:null});
        }
        var doc=new Dictionary<string,object>{["Tag"]=tag,["Time"]=DateTime.Now.ToString("O"),["Game"]=M26AcceptanceCore.GameName(),
            ["Status"]=app.Tracking.Status,["Teleporters"]=app.Tracking.Teleporters,["TeleporterNodes"]=app.Tracking.TeleporterNodes,["Rows"]=rows};
        File.WriteAllText(Path.Combine(app.DataDirectory,"m27-tracking-"+tag+".json"),JsonSerializer.Serialize(doc,Json));
        return "rows="+rows.Count+" "+Tallies(app);
    }
    private static string Tallies(ToolkitMod app)
    {
        var sb=new StringBuilder(160);var rows=app.Tracking.Rows;
        for(int k=0;k<M27TrackingPolicy.Kinds.Length;k++)
        {
            var t=M27TrackingPolicy.Count(rows,k);
            sb.Append(k==0?"":" ").Append(M27TrackingPolicy.Kinds[k]).Append('=').Append(t.Done).Append('/').Append(t.Total)
              .Append("(pending ").Append(t.Pending).Append(" unknown ").Append(t.Unknown).Append(')');
        }
        return sb.ToString();
    }
    private static string DroneNative(ToolkitMod app)
    {
        var map=app.Map;
        if(!map.NativeMapOpen)return "map closed";
        var container=map.Ui._droneMenu;
        if(container==null)return "no container";
        var menu=container._droneMenu;
        var rect=menu!=null?menu.transform.TryCast<RectTransform>():null;
        return "showing="+container._isShowing+" active="+container._isActive+" enabled="+container.isActiveAndEnabled+
            (rect!=null?" anchored="+rect.anchoredPosition.ToString("F1")+" size="+rect.rect.size.ToString("F1"):" menu=none");
    }
    internal static string Diagnostics(ToolkitMod app,ToolkitUi ui)
    {
        if(!Enabled(app))return "";
        var sb=new StringBuilder(1536);
        sb.Append("M27Acceptance: ").Append(Last).Append('\n');
        try
        {
            sb.Append("M27Ui: ").Append(ui.M27UiStateForTest).Append('\n');
            var t=app.Tracking;
            sb.Append("M27Tracking: rows=").Append(t.Rows.Count).Append(" version=").Append(t.Version).Append(' ').Append(Tallies(app))
              .Append(" teleporters=").Append(t.Teleporters).Append(" nodes=").Append(t.TeleporterNodes)
              .Append(" zoneTitles=").Append(MapTools.ZoneTitleCount).Append(" saved=").Append(MapTools.SavedZoneTitleCount).Append(" status=").Append(t.Status).Append('\n');
            sb.Append("M27TrackSelected: ").Append(RowText(ui.M27TrackSelectedForTest)).Append('\n');
            sb.Append("M27Unlocks: styles=").Append(app.Unlocks.StylesResult).Append(" | blueprints=").Append(app.Unlocks.BlueprintsResult).Append('\n');
            var d=app.DronePanels;
            sb.Append("M27Drone: status=").Append(d.Status).Append(" shift=").Append(d.Shift.ToString("0.#",CultureInfo.InvariantCulture))
              .Append(" moves=").Append(d.Moves).Append(" collapsed=").Append(d.AtlasCollapsed).Append(" atlasPanelExpanded=").Append(app.Map.PanelExpanded)
              .Append(" userExpanded=").Append(d.UserExpanded).Append(" native=").Append(DroneNative(app)).Append(" parts=").Append(d.DescribeParts()).Append('\n');
            var m=app.MiniMapTerrain;
            sb.Append("M27MiniMap: show=").Append(app.Map.ShowMiniMap).Append(" terrain=").Append(m.Enabled).Append(" hasTerrain=").Append(m.HasTerrain)
              .Append(" page=").Append(m.PageName).Append(" calibrated=").Append(m.CalibratedPages).Append('/').Append(m.CalibratedLayers)
              .Append(" cached=").Append(m.CachedLayers).Append(" layers=").Append(m.Layers)
              .Append(" composes=").Append(m.Composes).Append(" lastMs=").Append(m.LastComposeMs.ToString("0.###",CultureInfo.InvariantCulture))
              .Append(" points=").Append(app.Map.MiniMapPoints.Count).Append(" status=").Append(m.Status).Append(" note=").Append(m.CalibrationNote).Append('\n');
            var c=app.Compass;
            sb.Append("M27Compass: enabled=").Append(c.Enabled).Append(" active=").Append(c.Active).Append(" registrations=").Append(c.Registrations)
              .Append(" status=").Append(c.Status).Append('\n');
            var f=app.AutoFeed;
            sb.Append("M27AutoFeed: enabled=").Append(f.Enabled).Append(" corrals=").Append(f.Corrals).Append(" foods=").Append(f.KnownFoods)
              .Append(" slimes=").Append(f.Slimes).Append(" hungry=").Append(f.HungrySlimes).Append(" fed=").Append(f.FedLastTick)
              .Append(" portions=").Append(f.Portions).Append(" status=").Append(f.Status).Append('\n');
        }
        catch(Exception e){sb.Append("M27DiagnosticsError: ").Append(e.GetType().Name).Append(' ').Append(e.Message).Append('\n');}
        sb.Append("M27Pending: ").Append(app.Pending?.Title??"none").Append('\n');
        return sb.ToString();
    }
}
