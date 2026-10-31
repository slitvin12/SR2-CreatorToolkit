using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace CreatorToolkit;
// M27 offline regression: pure policies (drone panel, unlocks, world tracking,
// corral autofeed, minimap terrain, compass markers) and the source wiring.
// No Unity, no game process, no saves and no installed Mods\ files are touched.
internal static class M27Regression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool b,string s)=>check(b,"m27 / "+s);
        Drone(C);Unlock(C);Tracking(C);AutoFeed(C);MiniMap(C);Compass(C);Sources(C,root);LiveFixes(C,root);
    }

    // Findings of the in-game acceptance run (0.6.0, 1920x1440) and their fixes.
    private static void LiveFixes(Action<bool,string> C,string root)
    {
        C(M27DronePanelPolicy.UsableRoot(982,630,1920,1440)&&!M27DronePanelPolicy.UsableRoot(1920,1440,1920,1440)&&
          !M27DronePanelPolicy.UsableRoot(float.NaN,630,1920,1440)&&!M27DronePanelPolicy.UsableRoot(0,630,1920,1440)&&
          !M27DronePanelPolicy.UsableRoot(982,1400,1920,1440),"live: the drone card root counts unless it is a stretched or degenerate container");
        float sh=1440,m=M27DronePanelPolicy.Margin;
        var expanded=M27DronePanelPolicy.AtlasPanel(1920,sh,true,14);
        var collapsed=M27DronePanelPolicy.AtlasPanel(1920,sh,false,14);
        var card=new MapShieldPolicy.Box(1920-982-60,120,982,630);
        var d=M27DronePanelPolicy.Decide(card,expanded,collapsed,true,true,sh);
        C(d.Collapse&&card.Bottom+d.Shift<=sh-m+1e-3&&d.Shift<sh*.5f,"live: a full 982x630 card collapses the atlas instead of leaving the screen");
        var row=new M27TrackingPolicy.Row{Kind=M27TrackingPolicy.Pods,Id="pod1",Name="Капсула",Zone="Радужные поля",Group="RainbowFields"};
        C(M27TrackingPolicy.Matches(row,"rainbow")&&M27TrackingPolicy.Matches(row,"радуж")&&!M27TrackingPolicy.Matches(row,"ember"),
          "live: tracking shows zone titles while the raw scene group stays searchable");
        string dir=Path.Combine(root,"src","creator");
        string R(string name){string p=Path.Combine(dir,name);return File.Exists(p)?File.ReadAllText(p):"";}
        string drone=R("M27DronePanelLayout.cs"),terrain=R("MiniMapTerrain.cs"),ui=Compact(R("ToolkitUi.cs")),explorer=R("ExplorerUi.cs"),
            tracking=R("WorldTrackingTools.cs"),relay=R("M27AcceptanceIpc.cs"),maptools=R("MapTools.cs"),atlasProbe=R("NativeAtlasProbe.cs");
        // Live zone probe: MapUI.TryFindZoneAt takes the projected UI point. Raw X/Z put the
        // Conservatory and Rainbow Fields into Starlight Strand and changed with the zoom.
        C(maptools.Contains("Vector3 ui=rect.TransformPoint(map.GetMapPosition(position,rect));")&&maptools.Contains("return atlas.TryFindZoneAt(new Vector2(ui.x,ui.y));")&&
          !maptools.Contains("TryFindZoneAt(new Vector2(position.x,position.z))")&&!maptools.Contains("TryFindZoneAt(new Vector2(point.Position.x")&&
          !atlasProbe.Contains("TryFindZoneAt(new Vector2(ground.x,ground.z))")&&atlasProbe.Contains("MapTools.ZoneAreaAt(ui,ground)")&&
          maptools.Contains("var area=ZoneAreaAt(Ui,position);")&&maptools.Contains("var zone=ZoneAreaAt(atlas,position)?.ZoneMapData?.PrimaryZone;"),
          "live: atlas zone polygons get the projected UI point, not raw X/Z (zoom-invariant)");
        C(maptools.Contains("if(MapForPoint(point)!=page)continue;")&&maptools.Contains("MapForPoint(point)!=Ui.CurrentMapDefinition)return null;")&&
          maptools.Contains("_maps.TryGetValue(current.name,out var currentPage)&&currentPage!=page)return null;"),
          "live: zone polygons resolve only points and flights of the shown atlas page");
        C(terrain.Contains("MapUIUtilities.GetPlayerCurrentZone()")&&terrain.Contains("page=_app.Context.MapDirector?.GetMapForZone(zone);")&&
          terrain.Contains("откройте карту игры один раз, чтобы определить страницу")&&terrain.Contains("_atlasSeen=true;"),
          "live: after a restart the minimap page comes from the player's native zone, not only the atlas-only mapping");
        C(drone.Contains("M27DronePanelPolicy.UsableRoot(")&&drone.Contains("menu._contentBorder")&&drone.Contains("Include(_target,offset,cam,"),
          "live: the drone box unites the card root, border, tabs and content");
        C(ui.Contains("_confirmTextHeight=_wrap.CalcHeight(newGUIContent(Loc.T(description)),652);")&&
          ui.Contains("floatdialogHeight=Mathf.Clamp(_confirmTextHeight+200,360,Mathf.Max(360,height-40));")&&
          ui.Contains("newRect(dialog.x+24,dialog.y+90,dialog.width-48,dialogHeight-190),description,_wrap"),"live: confirmation dialogs grow with their description");
        C(explorer.Contains("if(DetailedHelp)L.Label(new Rect(x,rect.yMax-176,w,34),tools.Status,_small);")&&
          explorer.Contains("float listBottom=rect.yMax-(DetailedHelp?184:148);"),"live: with «?» the radar status sits above the favourite-food button, the list makes room");
        C(terrain.Contains("_layerTextures[art.Key]=texture;")&&terrain.Contains("Note(\"committed\"")&&terrain.Contains("Note(\"sample\"")&&
          !terrain.Contains("_calibrations.Remove(_pageName)")&&terrain.Contains("FineSpan=20"),"live: minimap calibration adopts the displayed atlas art and reports why it waits");
        // MainWorldMap: MapBackground is empty, the island is ten zone Images (MapZone material).
        C(terrain.Contains("mapRect.GetComponentsInChildren<UiImage>(false)")&&terrain.Contains("StartsWith(\"MapZone\"")&&
          terrain.Contains("GetComponentInParent<FogMapElement>()==null")&&terrain.Contains("M27MiniMapPolicy.LayerKey(page,order++,layer)")&&
          terrain.Contains("M27MiniMapPolicy.LayerKey(page,0,BackgroundLayer)")&&terrain.Contains("originX=trimmed.x-offset.x")&&
          terrain.Contains("M27MiniMapPolicy.ComposeLayers(_sources,")&&terrain.Contains("M27MiniMapPolicy.Overlaps(")&&
          terrain.Contains("if(readBack){_dirty=true;")&&terrain.Contains("Resources.FindObjectsOfTypeAll<Texture2D>()"),
          "live: the minimap calibrates every zone picture of the atlas page and paints the ones under the disc");
        // Live 13:16: relief enabled first, atlas opened and closed, player standing: no layer was ever read.
        C(terrain.Contains("if(!_mapOpen){_mapOpen=true;_settledPage=\"\";_settledUi=IntPtr.Zero;_searchedPages.Clear();}")&&
          terrain.Contains("if(_mapOpen){_mapOpen=false;_captureRetry.Clear();_dirty=true;}")&&
          terrain.IndexOf("if(_mapOpen){_mapOpen=false;",StringComparison.Ordinal)<terrain.IndexOf("if(!map.ShowMiniMap||map.Busy)return;",StringComparison.Ordinal),
          "live: closing the atlas reads the missing minimap layers at once, also for a standing player");
        C(relay.Contains("case \"m27-minimapdump\"")&&relay.Contains(".Append(\" layers=\").Append(m.Layers)"),"live: acceptance can dump the composite and reports painted layers");
        C(tracking.Contains("ZoneSceneGroupMapping")&&tracking.Contains("Zone=ZoneTitle(map,point),Group=point.GroupName")&&
          tracking.Contains("Zone=ZoneTitle(map,point),Group=point?.GroupName")&&tracking.Contains("map.ZoneAt(point)")&&
          tracking.Contains("_zoneMisses[key]=now+M27TrackingPolicy.ZoneRetrySeconds")&&tracking.Contains("_zoneTitles.Clear();_zoneMisses.Clear();")&&
          tracking.Contains("\"Tracking zone title: \""),"live: tracking zone titles come from the zone mapping or the atlas polygon; misses retry");
        // Live: ZoneSceneGroupMapping and the polygons exist only while the atlas is open (0 mappings closed).
        C(maptools.Contains("else if(Time.unscaledTime>=_nextZoneHarvest){_nextZoneHarvest=Time.unscaledTime+2f;HarvestZoneTitles();}")&&
          maptools.Contains("if(zone==null||director.GetMapForZone(zone)!=page)continue;")&&maptools.Contains("private static readonly Dictionary<string,string> ZoneTitleByPoint")&&
          tracking.Contains("string harvested=map.ZoneTitleFor(point);")&&tracking.Contains("названия зон появятся после открытия карты игры"),
          "live: zone titles are harvested while the atlas is open and kept for the game process");
        C(M27TrackingPolicy.UsableZoneTitle("Радужные поля")&&!M27TrackingPolicy.UsableZoneTitle("!!zone.fields")&&
          !M27TrackingPolicy.UsableZoneTitle(" ")&&!M27TrackingPolicy.UsableZoneTitle(null)&&M27TrackingPolicy.ZoneRetrySeconds>=5f,
          "live: only localized zone names replace the raw scene group");
        // Live: after a restart the rows showed raw groups (ConservatoryFields) until the atlas opened.
        string saved=M27TrackingPolicy.SerializeZoneTitle(false,"pod1545015581","Радужные поля");
        C(saved=="p\tpod1545015581\tРадужные поля"&&M27TrackingPolicy.TryParseZoneTitle(saved,out bool g1,out string k1,out string t1)&&!g1&&k1=="pod1545015581"&&t1=="Радужные поля"&&
          M27TrackingPolicy.TryParseZoneTitle(M27TrackingPolicy.SerializeZoneTitle(true,"ConservatoryFields","Оранжерея"),out bool g2,out string k2,out _)&&g2&&k2=="ConservatoryFields"&&
          !M27TrackingPolicy.TryParseZoneTitle("p\tpod1\t!!zone.key",out _,out _,out _)&&!M27TrackingPolicy.TryParseZoneTitle("x\tpod1\tПоле",out _,out _,out _)&&
          !M27TrackingPolicy.TryParseZoneTitle("p\t \tПоле",out _,out _,out _)&&!M27TrackingPolicy.TryParseZoneTitle("p\tpod1",out _,out _,out _)&&
          !M27TrackingPolicy.TryParseZoneTitle(null,out _,out _,out _)&&M27TrackingPolicy.SerializeZoneTitle(false,"a\tb","c\nd")=="p\ta b\tc d",
          "live: saved zone titles round-trip; placeholders, unknown tags and broken lines are ignored");
        C(maptools.Contains("\"zone-titles.txt\"")&&maptools.Contains("if(!_zoneTitlesLoaded)LoadZoneTitles();")&&
          maptools.Contains("(ZoneTitleByPoint.TryGetValue(point.Id,out title)||SavedZoneByPoint.TryGetValue(point.Id,out title))")&&
          maptools.Contains("if(M27TrackingPolicy.KindOf(point.Category)>=0)TrackedZoneByPoint[point.Id]=title;")&&maptools.Contains("SaveZoneTitles();")&&
          maptools.Contains("File.Copy(temp,ZoneTitlesPath,true);"),"live: tracked zone titles persist between sessions; live atlas titles take precedence");
        C(relay.Contains("case \"m27-mapprobe\"")&&relay.Contains("note=\").Append(m.CalibrationNote)")&&relay.Contains("parts=\").Append(d.DescribeParts())"),
          "live: acceptance diagnostics expose drone parts and minimap calibration notes");
    }
    private static bool Near(double a,double b,double eps=1e-6)=>Math.Abs(a-b)<=eps;
    private static string Compact(string s)=>new string((s??"").Where(c=>!char.IsWhiteSpace(c)).ToArray());

    private static void Drone(Action<bool,string> C)
    {
        C(M27DronePanelPolicy.Scale(1080)==1f&&M27DronePanelPolicy.Scale(540)==.6f&&M27DronePanelPolicy.Scale(2160)==1.3f&&
          M27DronePanelPolicy.Scale(float.NaN)==1f&&M27DronePanelPolicy.Scale(0)==1f,"drone: GUI scale mirrors ToolkitUi clamp(h/1080,.6,1.3)");
        C(M27DronePanelPolicy.AtlasInset==620&&M27DronePanelPolicy.AtlasTop==20&&M27DronePanelPolicy.AtlasWidth==600&&M27DronePanelPolicy.AtlasCollapsedHeight==108,
          "drone: atlas constants equal the 0.8.1 toolbar (sw-620,20,600) with its line down to y=128");
        const int layers=14;
        var expanded=M27DronePanelPolicy.AtlasPanel(1920,1080,true,layers);
        var collapsed=M27DronePanelPolicy.AtlasPanel(1920,1080,false,layers);
        C(expanded.X==1920-620&&expanded.Y==20&&expanded.Width==600&&expanded.Bottom==1080-MapChromePolicy.BottomReserve&&
          collapsed.Height==108&&collapsed.X==expanded.X&&Near(expanded.Height,MapChromePolicy.Bounds(1920,1080,true).Height),
          "drone: atlas box at 1080p equals the toolbar plus the «Слои карты» drop-down");
        var wide=M27DronePanelPolicy.AtlasPanel(2560,1440,true,layers);
        var wideLogical=MapChromePolicy.Bounds(2560/1.3f,1440/1.3f,true);
        C(Near(wide.X,2560-620*1.3f,1e-2)&&Near(wide.Y,26,1e-3)&&Near(wide.Height,wideLogical.Height*1.3f,1e-2),
          "drone: atlas box follows the GUI matrix at 1440p");
        float sh=1080,m=M27DronePanelPolicy.Margin;
        // The drop-down reaches the bottom reserve: a panel under it can only be cleared by closing it.
        var under=new MapShieldPolicy.Box(expanded.X+40,200,300,300);
        var real=M27DronePanelPolicy.Decide(under,expanded,collapsed,true,true,sh);
        C(real.Collapse&&real.Clear&&real.Shift==0,"drone: a panel under the open «Слои карты» closes the drop-down instead of leaving the screen");
        // Decide itself is geometry-agnostic: a short fixture panel leaves room below it.
        expanded=new MapShieldPolicy.Box(1920-344,20,324,700);
        collapsed=new MapShieldPolicy.Box(1920-344,20,324,56);
        var left=new MapShieldPolicy.Box(200,300,400,300);
        var d=M27DronePanelPolicy.Decide(left,expanded,collapsed,true,true,sh);
        C(d.Shift==0&&!d.Collapse&&d.Clear,"drone: a panel clear of the atlas is never moved");
        var drone=new MapShieldPolicy.Box(expanded.X-120,expanded.Y+10,300,160);
        d=M27DronePanelPolicy.Decide(drone,expanded,collapsed,true,true,sh);
        var moved=new MapShieldPolicy.Box(drone.X,drone.Y+d.Shift,drone.Width,drone.Height);
        C(Near(d.Shift,expanded.Bottom+m-drone.Y,1e-3)&&!d.Collapse&&d.Clear&&!M27DronePanelPolicy.Overlaps(moved,expanded)&&moved.Bottom<=sh-m+1e-3,
          "drone: an overlapping panel is shifted just below the atlas and stays on screen");
        float need=expanded.Bottom+m-drone.Y;
        var tall=new MapShieldPolicy.Box(drone.X,drone.Y,300,sh-m-drone.Y-(need-50));
        d=M27DronePanelPolicy.Decide(tall,expanded,collapsed,true,true,sh);
        C(tall.Height>0&&d.Collapse&&d.Clear&&Near(d.Shift,collapsed.Bottom+m-tall.Y,1e-3),
          "drone: without room below the expanded atlas the atlas collapses and the panel clears its header");
        d=M27DronePanelPolicy.Decide(tall,expanded,collapsed,true,false,sh);
        C(!d.Collapse&&!d.Clear&&Near(d.Shift,sh-m-tall.Bottom,1e-3),"drone: a user-expanded atlas is respected; the panel moves only as far as the screen allows");
        var lower=new MapShieldPolicy.Box(drone.X,collapsed.Bottom+40,300,sh-m-(collapsed.Bottom+40)-10);
        d=M27DronePanelPolicy.Decide(lower,expanded,collapsed,true,true,sh);
        C(d.Collapse&&d.Clear&&d.Shift==0,"drone: collapsing alone clears a panel below the atlas header");
        d=M27DronePanelPolicy.Decide(drone,expanded,collapsed,false,true,sh);
        C(!d.Collapse&&d.Clear&&Near(d.Shift,collapsed.Bottom+m-drone.Y,1e-3),"drone: with a collapsed atlas only the header is cleared");
        d=M27DronePanelPolicy.Decide(new MapShieldPolicy.Box(0,0,0,10),expanded,collapsed,true,true,sh);
        var bad=M27DronePanelPolicy.Decide(drone,expanded,collapsed,true,true,float.NaN);
        C(d.Shift==0&&!d.Collapse&&d.Clear&&bad.Shift==0&&!bad.Collapse,"drone: invalid measurements leave the panel alone");
        C(M27DronePanelPolicy.Describe(new M27DronePanelPolicy.Decision(0,false,true))=="Панель дронов не пересекает атлас"&&
          M27DronePanelPolicy.Describe(new M27DronePanelPolicy.Decision(40.4f,false,true)).Contains("сдвинута вниз на 40 px")&&
          M27DronePanelPolicy.Describe(new M27DronePanelPolicy.Decision(10,true,false)).Contains("атлас свёрнут")&&
          M27DronePanelPolicy.Describe(new M27DronePanelPolicy.Decision(10,true,false)).Contains("экран слишком низкий"),
          "drone: status text describes shift, collapse and residual overlap");
        C(M27DronePanelPolicy.WriteTolerance>0&&M27DronePanelPolicy.MaxOverwrites>0,"drone: native rewrites are adopted and bounded");
    }

    private static void Unlock(Action<bool,string> C)
    {
        C(!M27UnlockPolicy.UnlockableStyle(M27UnlockPolicy.SaveNone,false)&&M27UnlockPolicy.UnlockableStyle(M27UnlockPolicy.SaveClassic,false)&&
          M27UnlockPolicy.UnlockableStyle(M27UnlockPolicy.SaveSecret,false)&&!M27UnlockPolicy.UnlockableStyle(M27UnlockPolicy.SaveSecret,true)&&
          !M27UnlockPolicy.UnlockableStyle(7,false),"unlock: only persisted CLASSIC/SECRET_STYLE appearances that are still locked");
        C(M27UnlockPolicy.DiscoverSecret(M27UnlockPolicy.SaveSecret,false)&&!M27UnlockPolicy.DiscoverSecret(M27UnlockPolicy.SaveSecret,true)&&
          !M27UnlockPolicy.DiscoverSecret(M27UnlockPolicy.SaveClassic,false),"unlock: only secret styles are marked discovered");
        C(M27UnlockPolicy.UnlockableBlueprint(true,false,false)&&!M27UnlockPolicy.UnlockableBlueprint(false,false,false)&&
          !M27UnlockPolicy.UnlockableBlueprint(true,true,false)&&!M27UnlockPolicy.UnlockableBlueprint(true,false,true),
          "unlock: blueprints need a prefab and skip secondary halves and owned ones");
        string styles=M27UnlockPolicy.StyleReport(40,12,20,3,0),stylesFailed=M27UnlockPolicy.StyleReport(40,12,20,3,2);
        C(styles.Contains("открыто 12")&&styles.Contains("уже было 20")&&styles.Contains("обнаружено 3")&&styles.Contains("видов 40")&&
          !styles.Contains("ошибок")&&stylesFailed.Contains("ошибок 2"),"unlock: style report counts and failures");
        string blueprints=M27UnlockPolicy.BlueprintReport(90,30,50,10,0),clean=M27UnlockPolicy.BlueprintReport(90,30,60,0,0);
        C(blueprints.Contains("открыто 30")&&blueprints.Contains("проверено 90")&&blueprints.Contains("недоступно для чертежа 10")&&
          !clean.Contains("недоступно")&&!clean.Contains("ошибок")&&clean.EndsWith("Сохраняется в сейве."),"unlock: blueprint report counts unavailable records");
    }

    private static M27TrackingPolicy.Row Row(int kind,string id,string name,string zone,bool? done,float distance)=>
        new(){Kind=kind,Id=id,Name=name,Zone=zone,State="",Done=done,Distance=distance};
    private static void Tracking(Action<bool,string> C)
    {
        C(M27TrackingPolicy.Kinds.Length==4&&M27TrackingPolicy.Filters(M27TrackingPolicy.Pods).Length==3&&M27TrackingPolicy.Filters(M27TrackingPolicy.Teleports).Length==1&&
          M27TrackingPolicy.ClampKind(-3)==0&&M27TrackingPolicy.ClampKind(9)==3&&M27TrackingPolicy.ClampFilter(3,2)==0&&M27TrackingPolicy.ClampFilter(0,7)==2,
          "tracking: kinds and filters are clamped");
        bool kinds=true;for(int i=0;i<M27TrackingPolicy.Kinds.Length;i++)kinds&=M27TrackingPolicy.KindOf(M27TrackingPolicy.Kinds[i])==i;
        C(kinds&&M27TrackingPolicy.KindOf("Ресурсы")==-1&&M27TrackingPolicy.KindOf(null)==-1,"tracking: atlas categories map to their list");
        C(M27TrackingPolicy.DoorIsOpen(1)&&!M27TrackingPolicy.DoorIsOpen(0)&&!M27TrackingPolicy.DoorIsOpen(2)&&
          M27TrackingPolicy.DoorState(0)=="Заперта"&&M27TrackingPolicy.DoorState(1)=="Открыта"&&M27TrackingPolicy.DoorState(2)=="Закрыта"&&
          M27TrackingPolicy.DoorState(9)=="Состояние неизвестно","tracking: AccessDoor.State LOCKED/OPEN/CLOSED");
        var near=Row(0,"pod2","Капсула Б","Rainbow",false,10);
        var far=Row(0,"pod1","Капсула А","Rainbow",true,50);
        var alpha=Row(0,"pod4","Alpha","Ember",false,-1);
        var unknown=Row(0,"pod3","Капсула В","Ember",null,float.NaN);
        var gordo=Row(1,"g1","Pink Gordo","Rainbow",false,5);
        var rows=new List<M27TrackingPolicy.Row>{unknown,gordo,far,alpha,near,null};
        var all=M27TrackingPolicy.Select(rows,0,0,"");
        C(all.Count==4&&all[0]==near&&all[1]==far&&all[2]==alpha&&all[3]==unknown,"tracking: known distance first, then zone/name; other kinds excluded");
        var done=M27TrackingPolicy.Select(rows,0,1,"");var pending=M27TrackingPolicy.Select(rows,0,2,"");
        C(done.Count==1&&done[0]==far&&pending.Count==2&&pending[0]==near&&pending[1]==alpha,"tracking: opened / not opened filters; unknown state only under the all filter");
        C(M27TrackingPolicy.Select(rows,0,0,"  КАПСУЛА б ").Count==1&&M27TrackingPolicy.Select(rows,0,0,"ember").Count==2&&
          M27TrackingPolicy.Select(rows,0,0,"POD1")[0]==far&&M27TrackingPolicy.Select(rows,0,0,"",2).Count==2&&
          M27TrackingPolicy.Select(null,0,0,"").Count==0,"tracking: case-insensitive search over name/id/zone and bounded rows");
        var tally=M27TrackingPolicy.Count(rows,0);
        string summary=M27TrackingPolicy.Summary(0,tally);
        C(tally.Total==4&&tally.Done==1&&tally.Pending==2&&tally.Unknown==1&&summary.Contains("всего 4")&&summary.Contains("открыто 1")&&
          summary.Contains("не открыто 2")&&summary.Contains("состояние неизвестно 1"),"tracking: pod tally and summary");
        C(M27TrackingPolicy.Summary(1,new M27TrackingPolicy.Tally(3,1,2,0)).Contains("лопнуло 1")&&
          M27TrackingPolicy.Summary(3,new M27TrackingPolicy.Tally(7,0,0,7))=="Точки телепортации атласа: 7"&&far.Key=="0|pod1",
          "tracking: gordo/teleport summaries and stable row keys");
    }

    private static void AutoFeed(Action<bool,string> C)
    {
        C(M27AutoFeedPolicy.Hungry(.6f,.5f)&&M27AutoFeedPolicy.Hungry(.5f,.5f)&&!M27AutoFeedPolicy.Hungry(.4f,.5f)&&
          !M27AutoFeedPolicy.Hungry(float.NaN,.5f)&&!M27AutoFeedPolicy.Hungry(.9f,float.NaN),"autofeed: hunger threshold; NaN never feeds");
        C(M27AutoFeedPolicy.Ready(10,5)&&!M27AutoFeedPolicy.Ready(4,5)&&M27AutoFeedPolicy.Ready(4,float.NaN),"autofeed: per-slime cooldown");
        C(M27AutoFeedPolicy.Within(0)&&M27AutoFeedPolicy.Within(300f*300f)&&!M27AutoFeedPolicy.Within(300f*300f+1)&&
          !M27AutoFeedPolicy.Within(-1)&&!M27AutoFeedPolicy.Within(float.NaN),"autofeed: 300 m corral radius");
        var M=(Func<bool,bool,bool,M27AutoFeedPolicy.Meal>)((f,t,s)=>new M27AutoFeedPolicy.Meal(f,t,s));
        C(M27AutoFeedPolicy.ChooseMeal(new[]{M(false,false,true),M(true,false,true)})==1&&
          M27AutoFeedPolicy.ChooseMeal(new[]{M(true,true,true),M(false,false,true)})==1&&
          M27AutoFeedPolicy.ChooseMeal(new[]{M(true,false,false),M(false,false,true)})==1&&
          M27AutoFeedPolicy.ChooseMeal(new[]{M(false,false,true),M(false,false,true)})==0&&
          M27AutoFeedPolicy.ChooseMeal(new[]{M(true,true,true)})==-1&&
          M27AutoFeedPolicy.ChooseMeal(Array.Empty<M27AutoFeedPolicy.Meal>())==-1&&M27AutoFeedPolicy.ChooseMeal(null)==-1,
          "autofeed: favourite spawnable food first, never transforming food, ordinary fallback");
        string on=M27AutoFeedPolicy.Status(true,2,10,3,1,7);
        C(M27AutoFeedPolicy.Status(false,3,3,3,3,3)=="Автокормление выключено"&&M27AutoFeedPolicy.Status(true,0,0,0,0,0).Contains("загонов в прогруженной зоне нет")&&
          on.Contains("Загонов 2")&&on.Contains("всего порций 7"),"autofeed: status text");
        C(M27AutoFeedPolicy.PortionsPerTick/M27AutoFeedPolicy.TickSeconds<=4f&&M27AutoFeedPolicy.MaxExamined>0&&M27AutoFeedPolicy.MaxCooldowns>=M27AutoFeedPolicy.MaxExamined&&
          M27AutoFeedPolicy.SlimeCooldown>M27AutoFeedPolicy.TickSeconds,"autofeed: at most 4 portions per second and bounded scans");
    }

    private static int Red(uint c)=>(int)(c&0xFF);
    private static int Green(uint c)=>(int)(c>>8&0xFF);
    private static int Alpha(uint c)=>(int)(c>>24);
    private static void MiniMap(Action<bool,string> C)
    {
        var truth=new M27MiniMapPolicy.Affine(.0004,.00001,.5,-.00002,.00039,.45);
        double x0=-310.5,z0=127.25,s=100;
        bool solved=M27MiniMapPolicy.TrySolve(x0,z0,truth.U(x0,z0),truth.V(x0,z0),x0+s,z0,truth.U(x0+s,z0),truth.V(x0+s,z0),
            x0,z0+s,truth.U(x0,z0+s),truth.V(x0,z0+s),out var affine);
        C(solved&&Near(affine.A,truth.A,1e-10)&&Near(affine.B,truth.B,1e-10)&&Near(affine.C,truth.C,1e-9)&&Near(affine.D,truth.D,1e-10)&&
          Near(affine.E,truth.E,1e-10)&&Near(affine.F,truth.F,1e-9),"minimap: three native samples recover the world->page affine map");
        C(M27MiniMapPolicy.Residual(affine,x0+s,z0+s,truth.U(x0+s,z0+s),truth.V(x0+s,z0+s))<1e-9&&
          M27MiniMapPolicy.Residual(affine,x0+s,z0+s,truth.U(x0+s,z0+s)+.01,truth.V(x0+s,z0+s))>M27MiniMapPolicy.MaxResidual,
          "minimap: a fourth sample rejects a non-affine or unsettled layout");
        C(!M27MiniMapPolicy.TrySolve(0,0,0,0,1,1,1,1,2,2,2,2,out _),"minimap: collinear samples are rejected");
        var shifted=new M27MiniMapPolicy.Affine(truth.A,truth.B,truth.C+.01,truth.D,truth.E,truth.F);
        C(M27MiniMapPolicy.Consistent(truth,affine,x0,z0,500)&&!M27MiniMapPolicy.Consistent(truth,shifted,x0,z0,500),
          "minimap: calibration commits only after two agreeing layouts");
        C(Near(M27MiniMapPolicy.TexelsPerMetre(new M27MiniMapPolicy.Affine(.001,0,0,0,.001,0),1000,1000),1,1e-9),"minimap: texel density from the affine determinant");
        M27MiniMapPolicy.FitRect(100,100,200,100,true,.5,.5,out double fx,out double fy,out double fw,out double fh);
        M27MiniMapPolicy.FitRect(100,100,100,200,true,0,1,out double gx,out double gy,out double gw,out double gh);
        M27MiniMapPolicy.FitRect(100,50,100,100,false,.5,.5,out double hx,out double hy,out double hw,out double hh);
        C(Near(fx,0)&&Near(fw,1)&&Near(fh,.5)&&Near(fy,.25)&&Near(gw,.5)&&Near(gx,0)&&Near(gy,0)&&Near(gh,1)&&hx==0&&hy==0&&hw==1&&hh==1,
          "minimap: Image preserveAspect letterboxing follows the RectTransform pivot");
        M27MiniMapPolicy.CaptureSize(4096,2048,2048,out int cw,out int ch);
        M27MiniMapPolicy.CaptureSize(1000,500,2048,out int dw,out int dh);
        M27MiniMapPolicy.CaptureSize(0,10,2048,out int ew,out int eh);
        C(cw==2048&&ch==1024&&dw==1000&&dh==500&&ew==0&&eh==0,"minimap: page readback is capped at 2048 px and never upscaled");
        var calibration=new M27MiniMapPolicy.Calibration{Page="Rainbow|Fields",Texture="mapRainbow",Width=2048,Height=1024,Map=affine};
        bool parsed=M27MiniMapPolicy.TryParse(M27MiniMapPolicy.Serialize(calibration),out var back);
        C(parsed&&back.Page=="Rainbow/Fields"&&back.Texture=="mapRainbow"&&back.Width==2048&&back.Height==1024&&back.Map.A==affine.A&&back.Map.B==affine.B&&
          back.Map.C==affine.C&&back.Map.D==affine.D&&back.Map.E==affine.E&&back.Map.F==affine.F,"minimap: calibration file round-trips exactly");
        C(!M27MiniMapPolicy.TryParse("",out _)&&!M27MiniMapPolicy.TryParse("v2|a|b|1|1|1|0|0|0|1|0",out _)&&!M27MiniMapPolicy.TryParse("v1|a|b|1|1|0|0|0|0|0|0",out _)&&
          !M27MiniMapPolicy.TryParse("v1|a|b|0|1|1|0|0|0|1|0",out _)&&M27MiniMapPolicy.TryParse("v1|a|b|1|1|1|0|0|0|1|0",out _),
          "minimap: malformed or singular calibration lines are ignored");
        bool rotation=true;
        foreach(double yaw in new[]{0,.3,1.2,Math.PI/2,-2.5,Math.PI})
        foreach(var (dx,dz) in new[]{(10.0,0.0),(0.0,10.0),(-7.5,3.25),(40.0,-60.0)})
        {
            M27MiniMapPolicy.Project(dx,dz,yaw,1.7,out double px,out double py);
            M27MiniMapPolicy.RotateScreen(dx*1.7,-dz*1.7,M27MiniMapPolicy.ScreenDegrees(yaw),out double rx,out double ry);
            // ScreenDegrees is float (GUI matrix precision): agree within a thousandth of a pixel.
            rotation&=Near(px,rx,1e-3)&&Near(py,ry,1e-3);
        }
        C(rotation,"minimap: the north-up page rotated by ScreenDegrees(yaw) matches the heading-up marker projection");
        C(M27MiniMapPolicy.ScreenDegrees(Math.PI/2)==-90f,"minimap: yaw to GUI degrees");
        bool pivot=true;
        foreach(double degrees in new[]{0.0,37,-90,180,271.5})
        {
            double cx=1700,cy=250;
            M27MiniMapPolicy.PivotTranslation(cx,cy,degrees,out double tx,out double ty);
            M27MiniMapPolicy.RotateScreen(cx,cy,degrees,out double rcx,out double rcy);
            M27MiniMapPolicy.RotateScreen(1750,180,degrees,out double rpx,out double rpy);
            M27MiniMapPolicy.RotateScreen(50,-70,degrees,out double ox,out double oy);
            pivot&=Near(rcx+tx,cx,1e-6)&&Near(rcy+ty,cy,1e-6)&&Near(rpx+tx,cx+ox,1e-6)&&Near(rpy+ty,cy+oy,1e-6);
        }
        C(pivot,"minimap: the GUI.matrix TRS translation pivots the rotation on the minimap centre");
        float diameter=Math.Clamp(1080*.22f,170f,260f),radius=diameter/2,terrainRadius=radius-6,markerRadius=radius-14;
        int size=M27MiniMapPolicy.CompositeSize(terrainRadius);
        double mpt=M27MiniMapPolicy.MetresPerTexel(90,markerRadius,terrainRadius,size);
        C(size==200&&M27MiniMapPolicy.CompositeSize(10)==96&&M27MiniMapPolicy.CompositeSize(60)==120&&M27MiniMapPolicy.CompositeSize(float.NaN)==96,
          "minimap: composite texture is 96..200 px");
        C(mpt>0&&Near((2.0*terrainRadius/size)/(markerRadius/90.0),mpt,1e-9)&&M27MiniMapPolicy.MetresPerTexel(0,1,1,1)==0,
          "minimap: terrain and markers share one metres-per-pixel scale");
        C(M27MiniMapPolicy.NeedsCompose(false,0,0,0,0,mpt,0,0)&&!M27MiniMapPolicy.NeedsCompose(true,0,0,50,50,mpt,1f,.99f)&&
          !M27MiniMapPolicy.NeedsCompose(true,0,0,.1*mpt,0,mpt,10,0)&&M27MiniMapPolicy.NeedsCompose(true,0,0,mpt,0,mpt,10,0),
          "minimap: recomposed at most 15 Hz and only after sub-texel movement");
        C(M27MiniMapPolicy.Pack(1,2,3,4)==0x04030201u&&M27MiniMapPolicy.Pack(300,-5,0,255)==0xFF0000FFu,"minimap: RGBA32 packing order and clamping");
        const int sw=64,sh=64,n=32;
        var source=new uint[sw*sh];
        for(int y=0;y<sh;y++)for(int x=0;x<sw;x++)source[y*sw+x]=M27MiniMapPolicy.Pack(x*4,y*4,0,255);
        var page=new M27MiniMapPolicy.Affine(1/640.0,0,.5,0,1/640.0,.5);
        uint background=M27MiniMapPolicy.Pack(13,31,36,255);
        int opaque=(int)Math.Round(M27MiniMapPolicy.Opacity*255.0);
        var target=new uint[n*n];
        M27MiniMapPolicy.Compose(source,sw,sh,page,0,0,1,target,n,background);
        bool inside=true,outside=true;
        for(int j=0;j<n;j++)for(int i=0;i<n;i++)
        {
            double dx=i+.5-n/2.0,dy=j+.5-n/2.0,dist=Math.Sqrt(dx*dx+dy*dy);uint c=target[j*n+i];
            if(dist>=n/2.0){outside&=c==0;continue;}
            if(dist>n/2.0-1)continue;
            inside&=Math.Abs(Red(c)-Math.Round(.4*dx+126))<=1&&Math.Abs(Green(c)-Math.Round(.4*dy+126))<=1&&Alpha(c)==opaque;
        }
        C(inside&&outside,"minimap: circular north-up crop samples the page bilinearly (east right, north up) with a transparent outside");
        C(Green(target[28*n+16])>Green(target[4*n+16])&&Red(target[16*n+28])>Red(target[16*n+4]),"minimap: composite row 0 is south (Unity texture order)");
        var offPage=new uint[n*n];
        M27MiniMapPolicy.Compose(source,sw,sh,page,100000,0,1,offPage,n,background);
        var blank=new uint[n*n];
        M27MiniMapPolicy.Compose(new uint[sw*sh],sw,sh,page,0,0,1,blank,n,background);
        uint centre=offPage[16*n+16];
        C((centre&0xFFFFFF)==(background&0xFFFFFF)&&Alpha(centre)==opaque&&(blank[16*n+16]&0xFFFFFF)==(background&0xFFFFFF),
          "minimap: off-page and transparent page art show the opaque background");
        bool small=false,singular=false;
        try{M27MiniMapPolicy.Compose(source,sw,sh,page,0,0,1,new uint[10],n,background);}catch(ArgumentException){small=true;}
        try{M27MiniMapPolicy.Compose(source,sw,sh,new M27MiniMapPolicy.Affine(0,0,0,0,0,0),0,0,1,target,n,background);}catch(ArgumentException){singular=true;}
        C(small&&singular,"minimap: invalid buffers or calibration are rejected before any write");
        string key=M27MiniMapPolicy.LayerKey("MainWorldMap",4,"Fields");
        bool layered=M27MiniMapPolicy.TryLayerKey(key,out string kp,out int ko,out string kl);
        bool legacy=M27MiniMapPolicy.TryLayerKey("MainWorldMap",out string lp,out int lo,out string ll);
        bool slashed=M27MiniMapPolicy.TryLayerKey("Rainbow/Fields",out string sp,out int so,out string sl);
        var keyed=new M27MiniMapPolicy.Calibration{Page=M27MiniMapPolicy.LayerKey("Main",12,"a/b|c"),Texture="Map_Fields",Width=4096,Height=4096,Map=page};
        bool keyedParsedOk=M27MiniMapPolicy.TryParse(M27MiniMapPolicy.Serialize(keyed),out var keyedParsed);
        bool keyedBack=M27MiniMapPolicy.TryLayerKey(keyedParsedOk?keyedParsed.Page:"",out string bp,out int bo,out string bl)&&keyedParsedOk;
        C(key=="MainWorldMap/04/Fields"&&layered&&kp=="MainWorldMap"&&ko==4&&kl=="Fields"&&legacy&&lp=="MainWorldMap"&&lo==0&&ll==""&&
          slashed&&sp=="Rainbow/Fields"&&so==0&&sl==""&&keyedBack&&bp=="Main"&&bo==12&&bl=="a_b_c"&&!M27MiniMapPolicy.TryLayerKey("",out _,out _,out _),
          "minimap: per-layer calibration keys page/NN/layer round-trip, whole-page keys stay pages");
        bool bounded=M27MiniMapPolicy.WorldBounds(page,out double bx0,out double bz0,out double bx1,out double bz1);
        C(bounded&&Near(bx0,-320,1e-9)&&Near(bz0,-320,1e-9)&&Near(bx1,320,1e-9)&&Near(bz1,320,1e-9)&&
          !M27MiniMapPolicy.WorldBounds(new M27MiniMapPolicy.Affine(0,0,0,0,0,0),out _,out _,out _,out _)&&
          M27MiniMapPolicy.Overlaps(bx0,bz0,bx1,bz1,350,0,40)&&!M27MiniMapPolicy.Overlaps(bx0,bz0,bx1,bz1,370,0,40)&&
          !M27MiniMapPolicy.Overlaps(bx0,bz0,bx1,bz1,0,-400,40),"minimap: a layer's world box culls layers away from the disc");
        // Two layers: an opaque red base and a green top that is opaque only west of x=0.
        var red=new uint[sw*sh];var green=new uint[sw*sh];
        for(int y=0;y<sh;y++)for(int x=0;x<sw;x++){red[y*sw+x]=M27MiniMapPolicy.Pack(255,0,0,255);green[y*sw+x]=M27MiniMapPolicy.Pack(0,255,0,x<sw/2?255:0);}
        var stacked=new uint[n*n];var swapped=new uint[n*n];var none=new uint[n*n];
        M27MiniMapPolicy.ComposeLayers(new[]{new M27MiniMapPolicy.Source(red,sw,sh,page),new M27MiniMapPolicy.Source(green,sw,sh,page)},0,0,8,stacked,n,background);
        M27MiniMapPolicy.ComposeLayers(new[]{new M27MiniMapPolicy.Source(green,sw,sh,page),new M27MiniMapPolicy.Source(red,sw,sh,page)},0,0,8,swapped,n,background);
        M27MiniMapPolicy.ComposeLayers(new M27MiniMapPolicy.Source[0],0,0,8,none,n,background);
        uint west=stacked[16*n+6],east=stacked[16*n+26];
        bool rejected=false;
        try{M27MiniMapPolicy.ComposeLayers(new[]{new M27MiniMapPolicy.Source(red,sw,sh,page),new M27MiniMapPolicy.Source(new uint[3],sw,sh,page)},0,0,8,none,n,background);}
        catch(ArgumentException){rejected=true;}
        C(Green(west)==255&&Red(west)==0&&Red(east)==255&&Green(east)==0&&Red(swapped[16*n+6])==255&&Green(swapped[16*n+6])==0&&
          (none[16*n+16]&0xFFFFFF)==(background&0xFFFFFF)&&Alpha(none[16*n+16])==opaque&&none[0]==0&&rejected,
          "minimap: zone layers paint in atlas order over the background; one bad layer rejects the composite");
    }

    private static void Compass(Action<bool,string> C)
    {
        var K=(Func<string,int,float,M27CompassPolicy.Candidate>)((k,p,d)=>new M27CompassPolicy.Candidate(k,p,d));
        var picked=M27CompassPolicy.Pick(new[]{K("c",2,5),K("b",1,300),K("sel",0,1000),K("a",1,10),K("d",1,float.NaN),K("e",1,20),K("a",1,15),K("",1,1)});
        C(string.Join(",",picked.Select(c=>c.Key))=="sel,a,e,c","compass: selection first regardless of range, then priority/distance; unknown, far, duplicate and empty keys skipped");
        var many=new List<M27CompassPolicy.Candidate>();for(int i=0;i<10;i++)many.Add(K("p"+i,1,100-i));
        var six=M27CompassPolicy.Pick(many);
        C(six.Count==M27CompassPolicy.MaxMarkers&&six[0].Key=="p9"&&six[5].Key=="p4"&&M27CompassPolicy.Pick(many,2).Count==2&&
          M27CompassPolicy.Pick(many,0).Count==0&&M27CompassPolicy.Pick(null).Count==0,"compass: never more than six markers, nearest first");
        C(M27CompassPolicy.SameSet(new[]{"a","b"},new[]{"b","a"})&&!M27CompassPolicy.SameSet(new[]{"a","b"},new[]{"a"})&&
          !M27CompassPolicy.SameSet(new[]{"a","b"},new[]{"a","c"})&&M27CompassPolicy.SameSet(null,null)&&!M27CompassPolicy.SameSet(new[]{"a"},null),
          "compass: an unchanged target set does not re-register");
    }

    private static void Sources(Action<bool,string> C,string root)
    {
        string dir=Path.Combine(root,"src","creator");
        string[] added={"M27Policy.cs","M27DronePanelLayout.cs","UnlockStyleTools.cs","UnlockUi.cs","WorldTrackingTools.cs","CorralAutoFeedTools.cs","MiniMapTerrain.cs","M27CompassMarkers.cs"};
        bool exist=added.All(f=>File.Exists(Path.Combine(dir,f)));
        C(exist,"sources: all M27 modules exist in src\\creator");
        if(!exist)return;
        string R(string name)=>File.ReadAllText(Path.Combine(dir,name));
        var text=added.ToDictionary(f=>f,R);
        string policy=text["M27Policy.cs"],drone=text["M27DronePanelLayout.cs"],styles=text["UnlockStyleTools.cs"],unlockUi=text["UnlockUi.cs"],
            tracking=text["WorldTrackingTools.cs"],feed=text["CorralAutoFeedTools.cs"],terrain=text["MiniMapTerrain.cs"],compass=text["M27CompassMarkers.cs"];
        string host=R("ToolkitMod.cs"),ui=R("ToolkitUi.cs"),explorer=R("ExplorerUi.cs"),mini=R("MiniMapUi.cs"),unlocks=R("UnlockTools.cs"),map=R("MapTools.cs");
        C(!policy.Contains("using UnityEngine")&&!policy.Contains("using Il2Cpp"),"sources: M27 policies stay free of Unity/Il2Cpp");
        C(added.All(f=>!text[f].Contains("\"Mods")&&!text[f].Contains("Mods\\\\")&&!text[f].Contains("SaveAllNow")&&!text[f].Contains("Application.Quit")),
          "sources: M27 modules never write the Mods folder, force saves or quit");
        C(host.Contains("DronePanels = new DronePanelLayoutTools(this);")&&host.Contains("Tracking = new WorldTrackingTools(this);")&&
          host.Contains("AutoFeed = new CorralAutoFeedTools(this);")&&host.Contains("MiniMapTerrain = new MiniMapTerrainTools(this);")&&
          host.Contains("Compass = new CompassMarkerTools(this);"),"wiring: every M27 tool is constructed by the host");
        C(host.Contains("(\"corral autofeed\",AutoFeed.Tick),(\"compass markers\",Compass.Tick)")&&
          host.Contains("(\"atlas\",Map.Tick),(\"minimap terrain\",MiniMapTerrain.Tick)")&&
          host.Contains("_lateTicks=new (string,Action)[]{(\"drone panel layout\",DronePanels.LateTick)}"),"wiring: cached tick delegates (ready, always, late)");
        string late=host.Split("public override void OnLateUpdate()")[1].Split("public override void OnGUI()")[0];
        C(late.Contains("foreach(var tick in _lateTicks)SafeTick(tick.Label,tick.Run)")&&late.Contains("Input.TickNativeHandoff()"),
          "wiring: the drone panel is placed in LateUpdate after the game's layout, isolated by SafeTick");
        C(host.Contains("\"restore drone panel layout\"")&&host.Contains("\"reset world tracking\"")&&host.Contains("\"restore corral autofeed\"")&&
          host.Contains("\"reset minimap terrain\"")&&host.Contains("\"restore compass markers\"")&&host.Contains("AutoFeed?.Reset();Compass?.Reset();DronePanels?.Reset();"),
          "wiring: scene change and quit restore every M27 tool");
        C(ui.Contains("ResetCreationUi();_headerSlime=null;ResetM27Ui();")&&ui.Contains("case 4:UnlockPage(body)")&&!ui.Contains("private void UnlockPage(")&&
          unlockUi.Contains("private void UnlockPage(Rect area)")&&unlockUi.Contains("private void ResetM27Ui()"),"unlock ui: the unlock tab lives in UnlockUi.cs and resets with the session");
        C(unlockUi.Contains("_app.Unlocks.Pedia")&&unlockUi.Contains("_app.Unlocks.Doors")&&unlockUi.Contains("_app.Unlocks.Styles")&&unlockUi.Contains("_app.Unlocks.Blueprints")&&
          unlockUi.Contains("_app.Confirm(title,"),"unlock ui: pedia/map/upgrades/doors plus styles and blueprints, each confirmed");
        C(unlocks.Contains("internal sealed partial class UnlockTools")&&styles.Contains("M27UnlockPolicy.UnlockableStyle(")&&
          styles.Contains("M27UnlockPolicy.UnlockableBlueprint(")&&styles.Contains("AddBlueprint(definition,false)")&&styles.Contains("RefreshCatalog()"),
          "unlock: styles and blueprints use the policy and the native blueprint path");
        C(unlockUi.Contains("M27TrackingPolicy.Select(")&&unlockUi.Contains("_app.Map.RequestTeleport(chosen)")&&unlockUi.Contains("_app.Map.OpenAt(chosen)")&&
          unlockUi.Contains("()=>tools.OpenDoor(id)")&&tracking.Contains("AllDoors()")&&tracking.Contains("NotifyParticipants()"),
          "tracking: lists use the policy, atlas/teleport actions and a confirmed single-door change");
        C(M27TrackingPolicy.Kinds.All(k=>map.Contains("\""+k+"\"")),"tracking: every list is an existing atlas category");
        C(feed.Contains("MaybeSpinAndChomp")&&feed.Contains("M27AutoFeedPolicy.ChooseMeal(")&&feed.Contains("M27AutoFeedPolicy.Within(")&&feed.Contains("_app.Confirm(")&&
          explorer.Contains("NativeRequiredIcon(new Rect(18,370+o,42,42),\"Farm\")")&&explorer.Contains("feed.Request(!feeding)"),
          "autofeed: native chomp path, policy meal choice, confirmed opt-in from the farm page");
        C(explorer.Contains("NativeRequiredIcon(new Rect(18,162+o,42,42),\"PinkPlort\")")&&explorer.Contains("NativeRequiredIcon(new Rect(x+18,162+o,42,42),\"Feeder\")")&&
          explorer.Contains("var target=tools.Tracked(entry.Type.ReferenceId);"),"explorer: existing farm/radar contracts kept");
        // M29: the disc diameter is M29MiniMapLayout.Diameter (size from navigation.cfg) in both; the zoom is the live range.
        C(mini.Contains("M29MiniMapLayout.Diameter(look.Size,Screen.height)")&&terrain.Contains("M29MiniMapLayout.Diameter(nav?.Settings.Size??1,Screen.height)")&&
          terrain.Contains("terrainRadius=radius-6f,markerRadius=radius-14f")&&mini.Contains("terrainRadius=radius-6f")&&mini.Contains("scale=(radius-14)/range"),
          "minimap: terrain composite and UI share the disc diameter and marker scale");
        C(mini.Contains("M27MiniMapPolicy.ScreenDegrees(yaw)")&&mini.Contains("M27MiniMapPolicy.PivotTranslation(center.x+clip.x,center.y+clip.y,degrees,out double tx,out double ty)")&&
          mini.Contains("GUI.matrix=Matrix4x4.identity;")&&mini.Contains("Event.current.type!=EventType.Repaint")&&mini.Contains("_nextMiniMap=Time.unscaledTime+.5f")&&
          mini.Contains("MiniMapPoints.Count>MiniMapMarkers")&&mini.Contains("CanonIcon(icon,CanonMapIconPolicy.Cell(point.Category,point.Id,point.Name))"),
          "minimap: rotated terrain under GUI.matrix, repaint-only, bounded markers kept");
        string compactTerrain=Compact(terrain);
        C(compactTerrain.Contains("internalvoidTick(){if(!Enabled)return;")&&terrain.Contains("M27MiniMapPolicy.NeedsCompose(")&&terrain.Contains("MaxCaptures=6,LayerCapture=1024")&&
          terrain.Contains("\"minimap-pages.txt\"")&&terrain.Contains("_app.DataDirectory")&&terrain.Contains("RenderTexture.ReleaseTemporary(target)"),
          "minimap: idle when off, page readback cached, composite throttled, calibration in UserData");
        // 0.8.0: the single minimap switch is the «Навигация → Режим» row (off / terrain / radar, saved).
        string navTools=R("M29NavigationTools.cs");
        C(explorer.Contains("Segmented(new Rect(cx,y,cw,40),M29NavigationSettings.ModeTitles,s.Mode)")&&explorer.Contains("nav.SetMode(mode)")&&
          navTools.Contains("map.ShowMiniMap=Settings.Mode!=0;")&&navTools.Contains("_app.MiniMapTerrain?.SetEnabled(Settings.Mode==1);"),"minimap: navigation mode row switches off / terrain / radar");
        C(compass.Contains("RegisterTrackedGameObjectAdvanced")&&compass.Contains("UnregisterTrackedGameObjectAdvanced")&&compass.Contains("M29CompassPolicy.Pick(")&&
          explorer.Contains("nav.SetCompass(on)"),"compass: native RadarRegistry markers, bounded by policy, toggled on the navigation page");
        C(drone.Contains("M27DronePanelPolicy.Decide(")&&drone.Contains("anchoredPosition")&&
          ui.Contains("Rect toolbar=R(MapChromePolicy.Toolbar(sw));")&&ui.Contains("layersOpen?R(MapChromePolicy.Layers(sw,sh)):default;")&&
          ui.Contains("_scale=Mathf.Clamp(Screen.height/1080f,.6f,1.3f)"),"drone: the policy box matches the live atlas overlay");
        string csproj=File.ReadAllText(Path.Combine(dir,"tests","CreatorToolkit.Tests.csproj"));
        C(csproj.Contains("..\\M27Policy.cs"),"tests: M27 policies are linked into the offline checker");
        // In-game acceptance relay: opt-in file, dedicated test save, exact confirmation titles.
        string relayPath=Path.Combine(dir,"M27AcceptanceIpc.cs");
        string relay=File.Exists(relayPath)?File.ReadAllText(relayPath):"";
        C(relay.Contains("OptInFile=\"m27-acceptance.enabled\"")&&relay.Contains("if(!Enabled(app)){Last=name+\" ignored: opt-in file missing\"")&&
          relay.Contains("_nextEnabledCheck=-1;")&&relay.Contains("if(!Enabled(app))return \"\";"),"acceptance: the M27 relay and its status lines are inert without the opt-in file");
        C(relay.Split("M26AcceptanceCore.RequireTestSave(app);").Length-1>=5&&relay.Contains("if(pending.Title!=title)return \"rejected: pending title differs\";")&&
          relay.Contains("[\"styles\"]=4,[\"blueprints\"]=5}"),"acceptance: M27 save changes only on the test save, via the UI confirmation with the exact title");
        C(relay.Length>0&&!relay.Contains("\"Mods")&&!relay.Contains("SaveAllNow")&&!relay.Contains("Application.Quit")&&
          host.Contains("if(M27AcceptanceIpc.Handle(this,_ui,command,NavigateMenu))break;")&&host.Contains("details.Append(M27AcceptanceIpc.Diagnostics(this,_ui));"),
          "acceptance: M27 relay wired into the diagnostic channel; no Mods, save-all or quit calls");
        string fixturePath=Path.Combine(dir,"M27FeedFixture.cs");
        string fixture=File.Exists(fixturePath)?File.ReadAllText(fixturePath):"";
        int fx=relay.IndexOf("case \"m27-feedfixture\"",StringComparison.Ordinal);
        int fxGuard=fx<0?-1:relay.IndexOf("M26AcceptanceCore.RequireTestSave(app);",fx,StringComparison.Ordinal);
        int fxSpawn=fx<0?-1:relay.IndexOf("M27FeedFixture.Spawn(",fx,StringComparison.Ordinal);
        int fxHungry=fx<0?-1:relay.IndexOf("M27FeedFixture.Hungry(",fx,StringComparison.Ordinal);
        C(fixture.Length>0&&fxGuard>fx&&fxSpawn>fxGuard&&fxHungry>fxGuard&&!fixture.Contains("SaveAllNow")&&!fixture.Contains("Destroy")&&
          !fixture.Contains("Application.Quit"),"acceptance: autofeed fixture is relay-only; spawn/hungry run only after the test-save guard");
    }
}
