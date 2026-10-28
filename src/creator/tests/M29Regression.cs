using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace CreatorToolkit;
// M29 offline regression, task 1 (atlas): own markers pinned on the native atlas page, the
// explicit search chip, and the selected-object card placement on every aspect ratio.
// Task 2 (phantom drones): every native route gets a world zone (M29GhostZonePolicy).
// Pure policies + source wiring; no Unity, no game process, no saves, no installed Mods\ files.
internal static class M29Regression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool b,string s)=>check(b,"m29 / "+s);
        Search(C);Card(C);Sweep(C);Sources(C,root);Ghost(C,root);DronePods(C,root);
        M29NavigationRegression.Run(check,root);
    }

    // Bug report after task 2: a capsule on the atlas where nothing is in the world. Every
    // component-drone spawner (ComponentAcqDroneSpawner) keeps its reward TreasurePod on the
    // spawner object, i.e. on the phantom route anchor; the phantom drone hands it out.
    private static double P(string s)=>double.Parse(s,System.Globalization.CultureInfo.InvariantCulture);
    private static void DronePods(Action<bool,string> C,string root)
    {
        var anchors=new[]{("r1",10.0,2.0,3.0),("r2",-5.0,0.0,7.25)};
        C(CapsuleMapPolicy.DroneRewardRoute(10.01,1.99,3.04,anchors)=="r1"&&CapsuleMapPolicy.DroneRewardRoute(-5,0,7.25,anchors)=="r2",
          "drone pods: a capsule on a route anchor is that route's reward");
        C(CapsuleMapPolicy.DroneRewardRoute(10.2,2,3,anchors)==null&&CapsuleMapPolicy.DroneRewardRoute(10,2,3,null)==null&&
          CapsuleMapPolicy.DroneRewardRoute(double.NaN,2,3,anchors)==null&&CapsuleMapPolicy.DroneRewardRoute(10,2,3,new[]{("",10.0,2.0,3.0)})==null,
          "drone pods: nearby capsules, missing anchors, NaN and blank routes are never taken");
        string dir=Path.Combine(root,"src","creator");
        string R(string name){string p=Path.Combine(dir,name);return File.Exists(p)?File.ReadAllText(p):"";}
        var routes=R("GhostDroneIndex.tsv").Split('\n').Skip(1).Select(l=>l.TrimEnd('\r').Split('\t')).Where(c=>c.Length>4)
            .Select(c=>(c[0],P(c[2]),P(c[3]),P(c[4]))).ToArray();
        var pods=R("MapIndex.tsv").Split('\n').Skip(1).Select(l=>l.TrimEnd('\r').Split('\t')).Where(c=>c.Length>6&&c[1]=="Капсулы").ToArray();
        var taken=pods.Select(c=>(Row:c,Route:CapsuleMapPolicy.DroneRewardRoute(P(c[4]),P(c[5]),P(c[6]),routes))).Where(t=>t.Route!=null).ToArray();
        C(routes.Length==12&&taken.Length==12&&taken.Select(t=>t.Route).Distinct().Count()==12,
          "drone pods: the 12 route anchors take exactly 12 index capsules, one per route");
        C(taken.All(t=>t.Row[2].Contains("nodeComponentAcqDrone"))&&pods.Count(c=>c[2].Contains("nodeComponentAcqDrone"))==12&&pods.Length-taken.Length==140,
          "drone pods: exactly the nodeComponentAcqDrone pods leave the capsule layer, 140 real capsules stay");
        string contents=R("CapsuleContents.json");
        C(taken.All(t=>contents.Contains("\""+t.Row[0]+"\"")),"drone pods: every phantom route gets baked reward contents");
        string map=R("MapTools.cs"),prov=R("MapCapsuleProvenance.cs"),ghost=R("M18GhostMap.cs");
        int routesAt=map.IndexOf("LoadGhostRoutes(installed);",StringComparison.Ordinal),indexAt=map.IndexOf("GetManifestResourceStream(\"CreatorToolkit.MapIndex.tsv\")",StringComparison.Ordinal);
        C(routesAt>0&&indexAt>routesAt&&map.IndexOf("LoadGhostRoutes(installed);",routesAt+1,StringComparison.Ordinal)<0,"wiring: route anchors load once, before the capsule rows");
        int takeAt=prov.IndexOf("if(TakeGhostReward(point.Id,null,point.Position) is string route)",StringComparison.Ordinal);
        C(takeAt>0&&takeAt<prov.IndexOf("bool allowed=CapsuleMapPolicy.StaticAllowed(",StringComparison.Ordinal)&&prov.IndexOf("Allowed=false,",takeAt,StringComparison.Ordinal)>takeAt,
          "wiring: static drone reward rows are audited as not-a-capsule and never added");
        int liveAt=map.IndexOf("if(TakeGhostReward(pod.Id,droneSpawner!=null?droneSpawner.Id:null,pod.transform.position)!=null)continue;",StringComparison.Ordinal);
        C(map.Contains("var droneSpawner=pod.GetComponent<Il2CppMonomiPark.SlimeRancher.Drone.ComponentAcqDroneSpawner>();")&&liveAt>0&&
          liveAt<map.IndexOf("var existingPod=Points.FirstOrDefault(",StringComparison.Ordinal),"wiring: a live pod on a drone spawner is never added as a capsule");
        C(map.Contains("RefreshOpenedPods();RefreshGordos();ResetGhostRouteStates();")&&ghost.Contains("+GhostRewardText(id);")&&
          map.Contains("string reward=GhostRewardText(drone.SpawnerId);")&&map.Contains("маршрут \"+drone.SpawnerId+reward")&&
          map.Contains("if(point.Category==\"Дроны\")return GhostRouteReward(point) is string reward?point.Name+\" · \"+reward:point.Name;"),
          "wiring: phantom routes and live drones name the reward of their spawner");
        C(map.Contains("(_readDrones.Contains(point.Id)||GhostPaid(point))")&&ghost.Contains("Capsules.GetState(pod)==CapsuleTruthPolicy.OpenState.Open")&&
          map.Contains("bool paid=drone.Awarded||GhostRewardTaken(drone.SpawnerId);"),"wiring: a paid-out phantom counts as opened (reward pod OPEN or model flag)");
    }

    private static bool Near(float a,float b,float eps=1e-2f)=>Math.Abs(a-b)<=eps;
    private readonly struct Display
    {
        internal readonly string Name;internal readonly float Sw,Sh;
        // Same logical space as DrawMapOverlay: scale = clamp(height/1080, .6, 1.3).
        internal Display(string name,float width,float height)
        {Name=name;float scale=Math.Clamp(height/1080f,.6f,1.3f);Sw=width/scale;Sh=height/scale;}
    }
    private static readonly Display[] Wide={new("1920x1080",1920,1080),new("2560x1440",2560,1440),new("1366x768",1366,768),
        new("1920x1200",1920,1200),new("3440x1440",3440,1440)};
    private static readonly Display[] Square={new("1920x1440",1920,1440),new("1600x1200",1600,1200),new("1024x768",1024,768),
        new("1280x1024",1280,1024)};
    private static MapShieldPolicy.Box Panel(float sw,bool expanded=true)=>new(sw-344,20,324,expanded?MapLayerOrder.PanelHeight(14):56);
    private static MapShieldPolicy.Box[] Chrome(float sh)=>new MapShieldPolicy.Box[]{new(0,0,720,112),new(0,120,260,100),new(0,sh-400,116,320)};
    private static MapShieldPolicy.Box Marker(float x,float y,float size=32)=>new(x-size*.5f,y-size*.5f,size,size);
    private static MapShieldPolicy.Box Old(float sw)=>new(Math.Max(16,Panel(sw).X-436),136,420,414);
    private static bool Hits(MapShieldPolicy.Box a,MapShieldPolicy.Box b)=>M29MapCardPolicy.Overlap(a,b)>0;
    private static bool Same(MapShieldPolicy.Box a,MapShieldPolicy.Box b)=>Near(a.X,b.X)&&Near(a.Y,b.Y)&&Near(a.Width,b.Width)&&Near(a.Height,b.Height);
    private static M29MapCardPolicy.Placement Place(Display d,MapShieldPolicy.Box? anchor,M29MapCardPolicy.Slot? previous=null,bool expanded=true)=>
        M29MapCardPolicy.Place(d.Sw,d.Sh,Panel(d.Sw,expanded),anchor,Chrome(d.Sh),previous);
    // The card must stay inside the usable map, off the panel and the native controls.
    private static bool Free(Display d,MapShieldPolicy.Box card,bool expanded=true)=>
        card.Valid&&card.X>=0&&card.Y>=0&&card.Right<=d.Sw+.01f&&card.Bottom<=d.Sh-M29MapCardPolicy.BottomReserve+.01f&&
        !Hits(card,M29MapCardPolicy.Inflate(Panel(d.Sw,expanded),M29MapCardPolicy.PanelMargin))&&!Chrome(d.Sh).Any(c=>Hits(card,c));

    private static void Search(Action<bool,string> C)
    {
        C(M29AtlasSearchPolicy.OwnCategory=="Мои метки"&&M29AtlasSearchPolicy.PinnedOnMap("Мои метки")&&!M29AtlasSearchPolicy.PinnedOnMap("Дроны")&&
          !M29AtlasSearchPolicy.PinnedOnMap("Капсулы")&&!M29AtlasSearchPolicy.PinnedOnMap(null),"search: only the own-marker layer is pinned on the atlas page");
        C(M29AtlasSearchPolicy.Needle("  ДрОн ")=="дрон"&&M29AtlasSearchPolicy.Needle(null)==""&&M29AtlasSearchPolicy.Active(" дрон ")&&
          !M29AtlasSearchPolicy.Active("   ")&&!M29AtlasSearchPolicy.Active(null),"search: the needle is the trimmed lower-case text; blanks are no search");
        C(M29AtlasSearchPolicy.ChipHead(3)=="ПОИСК · 3 НА КАРТЕ"&&M29AtlasSearchPolicy.ChipHead(0)=="ПОИСК · НЕТ НА КАРТЕ"&&
          M29AtlasSearchPolicy.ChipHead(-2)=="ПОИСК · НЕТ НА КАРТЕ","search: the chip head counts the hits on this map page");
        string chip=M29AtlasSearchPolicy.ChipQuery(new string('я',90));
        C(M29AtlasSearchPolicy.ChipQuery(" дрон ")=="«дрон»"&&M29AtlasSearchPolicy.ChipQuery(null)=="«»"&&
          chip.Length==M29AtlasSearchPolicy.ChipChars+2&&chip.EndsWith("…»",StringComparison.Ordinal),"search: the chip query is quoted and bounded");
        string hint=M29AtlasSearchPolicy.PageHint("дрон");
        C(hint.Contains("«дрон»")&&hint.Contains("свои метки"),"search: the atlas page says that own markers stay on the map");
    }

    private static void Card(Action<bool,string> C)
    {
        // 16:9 family: unchanged M28 rectangle, expanded or collapsed panel.
        bool kept=true;
        foreach(var d in Wide)
        {
            var p=Place(d,null);var collapsed=Place(d,null,null,false);
            kept&=p.Slot==M29MapCardPolicy.Slot.Beside&&p.Clear&&Same(p.Card,Old(d.Sw))&&Free(d,p.Card)&&
                collapsed.Slot==M29MapCardPolicy.Slot.Beside&&Same(collapsed.Card,Old(d.Sw));
        }
        C(kept,"card: wide screens keep the M28 rectangle beside the panel");
        var hd=Wide[0];
        C(Same(Place(hd,null).Card,new MapShieldPolicy.Box(1140,136,420,414)),"card: 1920x1080 is exactly new(Mathf.Max(16,panelBounds.x-436),136,420,414)");
        // The live bug: on 4:3 the M28 rectangle covered the centred (focused) marker.
        var qxga=Square[0];
        var centred=Marker(qxga.Sw*.5f,qxga.Sh*.5f);
        C(Hits(Old(qxga.Sw),centred),"card: reproduces the 4:3 bug (the M28 card covered the focused marker at the map centre)");
        bool moved=true;
        foreach(var d in Square)
        {
            var centre=Marker(d.Sw*.5f,d.Sh*.5f);
            var free=Place(d,null);var anchored=Place(d,centre);
            moved&=free.Clear&&free.Slot!=M29MapCardPolicy.Slot.Beside&&!Hits(free.Card,M29MapCardPolicy.Centre(d.Sw,d.Sh))&&Free(d,free.Card)&&
                anchored.Clear&&!Hits(anchored.Card,M29MapCardPolicy.Inflate(centre,M29MapCardPolicy.AnchorMargin))&&Free(d,anchored.Card)&&anchored.Anchored;
        }
        C(moved,"card: 4:3 and 5:4 screens move the card off the map centre and off the focused marker");
        C(Place(qxga,centred).Slot==M29MapCardPolicy.Slot.Left&&Same(Place(qxga,centred).Card,new MapShieldPolicy.Box(16,236,420,414)),
          "card: 1920x1440 uses the left slot below the native zone title");
        // A marker under the usual rectangle on 16:9: the card steps aside instead of hiding it.
        var under=Marker(1300,300);
        var aside=Place(hd,under);
        C(aside.Clear&&aside.Slot!=M29MapCardPolicy.Slot.Beside&&!Hits(aside.Card,M29MapCardPolicy.Inflate(under,M29MapCardPolicy.AnchorMargin))&&Free(hd,aside.Card),
          "card: a shown marker under the usual rectangle moves the card instead of being hidden");
        // Hysteresis: the same shown object keeps a clear slot; a blocked slot is left.
        var stay=Place(hd,null,M29MapCardPolicy.Slot.Left);
        var leftMarker=Marker(200,400);
        var leave=Place(hd,leftMarker,M29MapCardPolicy.Slot.Left);
        C(stay.Slot==M29MapCardPolicy.Slot.Left&&stay.Clear&&leave.Slot==M29MapCardPolicy.Slot.Beside&&leave.Clear,
          "card: keeps its slot while clear (no jumping while the map pans), leaves it when the marker moves under it");
        var lowLeft=Place(qxga,leftMarker);
        C(lowLeft.Clear&&!Hits(lowLeft.Card,M29MapCardPolicy.Inflate(leftMarker,M29MapCardPolicy.AnchorMargin))&&Free(qxga,lowLeft.Card),
          "card: on 4:3 a marker under the left slot sends the card to a low slot");
        // Tiny logical screens: no clear slot may exist; the least bad candidate wins.
        var tiny=new Display("800x600",800,600);
        bool least=true;
        for(float x=60;x<tiny.Sw-60;x+=90)for(float y=120;y<tiny.Sh-120;y+=90)
        {
            var anchor=Marker(x,y);var p=Place(tiny,anchor);
            if(p.Clear)continue;
            foreach(var slot in M29MapCardPolicy.Preference)
                least&=p.Penalty<=M29MapCardPolicy.Penalty(M29MapCardPolicy.Candidate(slot,tiny.Sw,tiny.Sh,Panel(tiny.Sw)),tiny.Sw,tiny.Sh,Panel(tiny.Sw),anchor,Chrome(tiny.Sh))+1e-4f;
            least&=p.Card.Valid&&float.IsFinite(p.Penalty);
        }
        C(least,"card: without a clear slot the least bad candidate is used");
        var names=M29MapCardPolicy.Preference.Select(M29MapCardPolicy.Name).ToArray();
        C(names.Distinct().Count()==4&&M29MapCardPolicy.Preference[0]==M29MapCardPolicy.Slot.Beside&&
          M29MapCardPolicy.Describe(Place(hd,null)).StartsWith("beside card=1140,136 420x414 clear=1",StringComparison.Ordinal),"card: slot names and the state line");
    }

    // Every marker position the overlay can draw (inside the projection margins, outside the
    // panel and native-control exclusions), on every common aspect ratio and in portrait.
    private static void Sweep(Action<bool,string> C)
    {
        var displays=Wide.Concat(Square).Append(new Display("1080x1920",1080,1920)).ToArray();
        int anchors=0,bad=0,clear=0;string first="";
        foreach(var d in displays)
        {
            var panel=Panel(d.Sw);
            var exclusion=M29MapCardPolicy.Inflate(panel,28);
            var chrome=Chrome(d.Sh).Select(c=>M29MapCardPolicy.Inflate(c,28)).ToArray();
            for(float x=28;x<=d.Sw-28;x+=37)for(float y=100;y<=d.Sh-100;y+=37)
            {
                var at=new MapShieldPolicy.Box(x,y,.01f,.01f);
                if(Hits(at,exclusion)||chrome.Any(c=>Hits(at,c)))continue;
                var marker=Marker(x,y);anchors++;
                var p=Place(d,marker);
                // The centre is a soft hint (5:4 may have no fully clear slot), the marker is not.
                bool ok=!Hits(p.Card,M29MapCardPolicy.Inflate(marker,M29MapCardPolicy.AnchorMargin))&&Free(d,p.Card);
                if(p.Clear)clear++;
                if(!ok){bad++;if(first.Length==0)first=" first: "+d.Name+" at "+x+","+y+" -> "+M29MapCardPolicy.Describe(p);}
            }
        }
        C(anchors>2000&&bad==0&&clear*100>=anchors*95,"card sweep: "+anchors+" marker positions on "+displays.Length+" screens ("+clear+" fully clear),"+
          " the card never covers the shown marker, the panel or native controls"+(bad>0?" (bad "+bad+first+")":""));
    }

    private static readonly string[] Commands={"ct-atlas-search","ct-atlas-state","ct-atlas-focus","ct-ghost-state","ct-palette-sources","ct-layer-audit","ct-nav"};

    private static void Sources(Action<bool,string> C,string root)
    {
        string dir=Path.Combine(root,"src","creator");
        string R(string name){string p=Path.Combine(dir,name);return File.Exists(p)?File.ReadAllText(p):"";}
        string map=R("MapTools.cs"),progress=R("MapProgress.cs"),ui=R("ToolkitUi.cs"),custom=R("CustomWaypointTools.cs"),relay=R("M28TestIpc.cs"),
            tests=R(Path.Combine("tests","CreatorToolkit.Tests.csproj")),program=R(Path.Combine("tests","Program.cs"));
        C(custom.Contains("internal const string Category=\"Мои метки\";"),"wiring: the pinned category is the own-marker layer");
        C(map.Contains("currentPage&&M29AtlasSearchPolicy.PinnedOnMap(p.Category)?OnCurrentMap(p):")&&map.Contains("MatchesSearch(p,needle)")&&
          map.Contains("internal bool PinnedOnly(Point p)")&&map.Contains("internal int SearchHitsOnPage()")&&
          progress.Contains("if(currentPage&&PinnedOnly(point))continue;"),
          "wiring: own markers bypass search and layers only on the atlas page; not hits, not «ближайшая» targets");
        int place=ui.IndexOf("var markers=MapOverlayLayout.Place(projected,map.MarkerSize);",StringComparison.Ordinal);
        int anchored=ui.IndexOf("capsuleBounds=MapCardRect(sw,sh,panelBounds,nativeChrome,cardShown,shownMarker==null?(Rect?)null:",StringComparison.Ordinal);
        int loop=ui.IndexOf("foreach(var marker in markers)",anchored<0?0:anchored,StringComparison.Ordinal);
        C(!ui.Contains("Rect capsuleBounds=new(Mathf.Max(16,panelBounds.x-436),136,420,414);")&&
          ui.Contains("Rect capsuleBounds=MapCardRect(sw,sh,panelBounds,nativeChrome,cardShown,null,repaint&&map.ProjectionError>=20f);")&&
          place>0&&anchored>place&&loop>anchored&&ui.Contains("M29MapCardPolicy.Place(")&&
          ui.Contains("if(covered){if(point==cardShown)shownCovered++;continue;}"),
          "wiring: the card slot is chosen after the marker layout, before the marker loop");
        C(ui.Contains("Rect panelBounds=new(sw-344,20,324,map.PanelExpanded?MapLayerOrder.PanelHeight(layers.Length):56);")&&
          ui.Contains("CapsuleCard(capsuleBounds,capsule,false)")&&ui.Contains("CustomMapCard(capsuleBounds,custom)"),
          "wiring: panel geometry and both cards unchanged");
        C(ui.Contains("if(M29AtlasSearchPolicy.Active(map.Search))AtlasSearchChip(new Rect(52,6,212,44),map);")&&
          ui.Contains("if(Button(new Rect(chip.xMax-42,chip.y+5,38,34),\"×\",_button))ClearAtlasSearch();")&&
          ui.Contains("M29AtlasSearchPolicy.ChipHead(map.SearchHitsOnPage())"),"wiring: the native-map header shows the active search with a clear button");
        C(ui.Contains("TextBox(\"atlas-search\",new Rect(0,160,area.width-52,40)")&&
          ui.Contains("if(Button(new Rect(area.width-44,160,44,40),\"×\",_button))ClearAtlasSearch();")&&
          ui.Contains("M29AtlasSearchPolicy.PageHint(map.Search)"),"wiring: the atlas page has a clear button and the pinned-markers hint");
        C(ui.Contains("if(_editingField==\"atlas-search\")BlurTextInput();")&&ui.Contains("_pendingTextWrites.Remove(\"atlas-search\");")&&
          ui.Contains("_mapCardShown=null;_mapCardSlot=null;"),"wiring: clearing survives a focused field; session reset forgets the card slot");
        int focus=relay.IndexOf("private static string AtlasFocus(",StringComparison.Ordinal);
        C(Commands.All(c=>relay.Contains("case \""+c+"\""))&&Commands.All(c=>M28TestPolicy.Help.Any(h=>h.StartsWith(c+" ",StringComparison.Ordinal)))&&
          focus>0&&relay.IndexOf("M26AcceptanceCore.RequireTestSave(app);",focus,StringComparison.Ordinal) is int guard&&guard>focus&&guard<focus+200,
          "relay: ct-atlas-* handled and documented; focus (native nav marker) only on the test save");
        C(tests.Contains("..\\M29Policy.cs")&&program.Contains("M29Regression.Run(Check,root);"),"tests: the policy file is linked and the regression runs");
    }

    private static string S(string name)=>"Assets/Scene/"+name+".unity";
    private static M29GhostZonePolicy.Zone Z(string name,bool complete,params string[] scenes)=>
        new(name,complete,new HashSet<string>(scenes.Select(S),StringComparer.OrdinalIgnoreCase));
    // Live M28 capture on 1.3.0 (m22-capsule-audit Membership), trimmed to the scenes that matter:
    // the real zones are complete, the test groups list the same zone scenes.
    private static readonly M29GhostZonePolicy.Zone[] Captured={
        Z("Labyrinth",true,"coreSceneLabyrinth","zoneLabyrinthHub","zoneLabyrinthDreamland","zoneLabyrinthTerrarium_FoyerGazebo","zoneLabyrinthCorePath"),
        Z("LuminousStrand",true,"zoneStrand","zoneStrand_Area1","zoneStrand_Area2","ProxyZones/ProxyZone_Fields"),
        Z("PowderfallBluffs",true,"zoneBluffs","zoneBluffs_Area2","zoneBluffs_Area4","ProxyZones/ProxyZone_Fields"),
        Z("RumblingGorge",true,"zoneGorge_Area1","zoneGorge_Area3","ProxyZones/ProxyZone_Fields"),
        Z("ConservatoryFields",true,"zoneConservatory","zoneConservatory_Den","zoneConservatory_Digsite","zoneFields","zoneFields_Area1","zoneFields_Vista"),
        Z("Sanctuary",true,"zoneConservatory_Sanctuary","zoneConservatory_Sanctuary_Hideaway"),
        Z("DenExpansion_Test",true,"zoneConservatory_Den","zoneConservatory_Digsite","zoneFields_Vista"),
        Z("AllZones",false,"zoneConservatory","zoneConservatory_Den","zoneConservatory_Sanctuary","zoneStrand_Area2","zoneGorge_Area3","zoneFields_Area1","zoneBluffs"),
        Z("SceneRestructuringTest",false,"zoneBluffs","zoneBluffs_Area2","ProxyZones/ProxyZone_Fields"),
        Z("SlimeShaderTest",false),Z("RainbowZoo",false)};

    private static void Ghost(Action<bool,string> C,string root)
    {
        const string M=M29GhostZonePolicy.Membership;
        bool Is(M29GhostZonePolicy.Binding b,string group,string source)=>b.Group==group&&b.Source==source&&b.Bound==(group.Length>0);
        string tsv=Path.Combine(root,"src","creator","GhostDroneIndex.tsv");
        var rows=(File.Exists(tsv)?File.ReadAllText(tsv):"").Split('\n',StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(r=>r.TrimEnd('\r').Split('\t')).Where(c=>c.Length>=6).ToArray();
        // The bug: "exactly one group of any kind lists the scene" binds only 5 of 12 routes.
        int old=rows.Count(c=>Captured.Count(z=>z.Owns(c[1]))==1);
        C(rows.Length==12&&old==5,"ghost: reproduces the M28 bug (the old unique-owner rule binds "+old+" of "+rows.Length+" routes)");
        int bound=rows.Count(c=>Is(M29GhostZonePolicy.Resolve(c[1],c[5],Captured),c[5],M)&&Is(M29GhostZonePolicy.Resolve(c[1],null,Captured),c[5],M));
        C(bound==12,"ghost: all twelve routes bind to their real zone by complete membership alone ("+bound+"/12)");
        var real=new[]{"ConservatoryFields","LuminousStrand","RumblingGorge","PowderfallBluffs","Labyrinth","Sanctuary"};
        C(rows.Length==12&&rows.All(c=>real.Contains(c[5]))&&real.All(g=>rows.Any(c=>c[5]==g)),"ghost: authored zones are the six real world zones, each used");
        // Several complete owners (DenExpansion_Test is a complete test group): the map reference decides.
        string den=S("zoneConservatory_Den");
        C(Is(M29GhostZonePolicy.Resolve(den,null,Captured),"",M29GhostZonePolicy.None)&&
          Is(M29GhostZonePolicy.Resolve(den,null,Captured,g=>g=="ConservatoryFields"),"ConservatoryFields",M29GhostZonePolicy.MapPage)&&
          Is(M29GhostZonePolicy.Resolve(den,"conservatoryfields",Captured),"ConservatoryFields",M29GhostZonePolicy.Authored)&&
          Is(M29GhostZonePolicy.Resolve(den,"DenExpansion_Test",Captured,g=>true),"DenExpansion_Test",M29GhostZonePolicy.Authored),
          "ghost: several complete owners -> the only one with an atlas page, else the authored zone, else unbound");
        string proxy=S("ProxyZones/ProxyZone_Fields");
        C(Is(M29GhostZonePolicy.Resolve(proxy,null,Captured,g=>true),"",M29GhostZonePolicy.None)&&
          Is(M29GhostZonePolicy.Resolve(proxy,"Sanctuary",Captured,g=>true),"",M29GhostZonePolicy.None)&&
          Is(M29GhostZonePolicy.Resolve(proxy,"RumblingGorge",Captured,g=>true),"RumblingGorge",M29GhostZonePolicy.Authored),
          "ghost: a scene shared by real zones is never guessed; an authored zone outside its owners is refused");
        // Membership unreadable (no scenes located): the authored zone of a known group is used.
        var blind=new[]{Z("Sanctuary",false),Z("Labyrinth",false)};
        C(Is(M29GhostZonePolicy.Resolve(S("zoneConservatory_Sanctuary"),"Sanctuary",blind),"Sanctuary",M29GhostZonePolicy.Authored)&&
          Is(M29GhostZonePolicy.Resolve(S("zoneConservatory_Sanctuary"),"Nowhere",blind),"",M29GhostZonePolicy.None)&&
          Is(M29GhostZonePolicy.Resolve(S("zoneConservatory_Sanctuary"),"Sanctuary",null),"",M29GhostZonePolicy.None),
          "ghost: unreadable membership falls back to the authored zone only when the index knows it");
        var partial=new[]{Z("A",false,"s1","s2"),Z("B",false,"s1"),Z("C",false)};
        C(Is(M29GhostZonePolicy.Resolve(S("s2"),null,partial),"A",M29GhostZonePolicy.Unique)&&
          Is(M29GhostZonePolicy.Resolve(S("s1"),null,partial),"",M29GhostZonePolicy.None)&&
          Is(M29GhostZonePolicy.Resolve(S("s1"),"B",partial),"B",M29GhostZonePolicy.Authored)&&
          Is(M29GhostZonePolicy.Resolve(S("s1"),"C",partial),"",M29GhostZonePolicy.None)&&
          Is(M29GhostZonePolicy.Resolve(S("s1"),null,partial,g=>g=="B"),"B",M29GhostZonePolicy.MapPage),
          "ghost: incomplete owners: unique owner, map page or authored owner; never a non-owner");
        C(Captured[4].Owns("Assets\\Scene\\ZONECONSERVATORY.unity ")&&!Captured[4].Owns(null)&&!Captured[4].Owns("")&&
          !Z("X",true).Owns(S("zoneConservatory"))&&new M29GhostZonePolicy.Zone("X",true,null).Owns(S("a"))==false&&
          M29GhostZonePolicy.Scene(null)==""&&M29GhostZonePolicy.Scene(" a\\b.unity ")=="a/b.unity",
          "ghost: scene paths compare with / separators, trimmed and case-insensitive");
        var membership=M29GhostZonePolicy.Resolve(rows.Length>0?rows[0][1]:"",null,Captured);
        C(Is(M29GhostZonePolicy.WithLive(membership,"Labyrinth",true),"Labyrinth",M29GhostZonePolicy.Live)&&
          Is(M29GhostZonePolicy.WithLive(membership,"Labyrinth",false),membership.Group,membership.Source)&&
          Is(M29GhostZonePolicy.WithLive(membership," ",true),membership.Group,membership.Source)&&
          Is(M29GhostZonePolicy.WithLive(default,"Sanctuary",true),"Sanctuary",M29GhostZonePolicy.Live),
          "ghost: the live drone model's scene group wins when the index knows it");
        C(!M29GhostZonePolicy.MapMayImprove(M)&&!M29GhostZonePolicy.MapMayImprove(M29GhostZonePolicy.MapPage)&&
          !M29GhostZonePolicy.MapMayImprove(M29GhostZonePolicy.Live)&&M29GhostZonePolicy.MapMayImprove(M29GhostZonePolicy.Authored)&&
          M29GhostZonePolicy.MapMayImprove(M29GhostZonePolicy.Unique)&&M29GhostZonePolicy.MapMayImprove(M29GhostZonePolicy.None)&&
          M29GhostZonePolicy.MapMayImprove(null),"ghost: only weaker static sources retry with the map reference");
        var empty=new M29GhostZonePolicy.Binding("  ",M);
        C(!empty.Bound&&empty.Source==M29GhostZonePolicy.None&&new M29GhostZonePolicy.Binding("G",null).Source==M29GhostZonePolicy.None&&
          M29GhostZonePolicy.Summary(new[]{M,M,M29GhostZonePolicy.Live,M29GhostZonePolicy.None,null,"odd"})==
          "routes=6 bound=3 membership=2 map=0 authored=0 unique=0 live=1 none=3"&&M29GhostZonePolicy.Summary(null)==
          "routes=0 bound=0 membership=0 map=0 authored=0 unique=0 live=0 none=0","ghost: binding invariants and the state summary");
        C(M29GhostZonePolicy.Sources.Select(M29GhostZonePolicy.Label).Distinct().Count()==M29GhostZonePolicy.Sources.Length&&
          M29GhostZonePolicy.Sources.All(s=>M29GhostZonePolicy.Label(s).StartsWith("зона ",StringComparison.Ordinal))&&
          M29GhostZonePolicy.Label("odd")==M29GhostZonePolicy.Label(M29GhostZonePolicy.None),"ghost: every source has its own Russian label");
        string dir=Path.Combine(root,"src","creator");
        string R(string name){string p=Path.Combine(dir,name);return File.Exists(p)?File.ReadAllText(p):"";}
        string ghost=R("M18GhostMap.cs"),map=R("MapTools.cs"),compass=R("M27CompassMarkers.cs"),relay=R("M28TestIpc.cs");
        C(!ghost.Contains("owners.Length==1?owners[0]")&&ghost.Contains("string authored=c.Length>5?c[5].Trim():\"\";")&&
          ghost.Contains("BindGhost(route,M29GhostZonePolicy.Resolve(ScenePath(c[1]),authored,zones));")&&
          ghost.Contains("zones.Add(new M29GhostZonePolicy.Zone(name,_gordoSceneComplete.Contains(name),scenes));")&&
          map.Contains("internal string ZoneSource;"),"wiring: routes load with the zone policy over the complete-membership index");
        C(map.Contains("DroneObservationScans++;ResolveGhostZonesWithMaps();")&&
          ghost.Contains("M29GhostZonePolicy.Resolve(ScenePath(route.SourceScene),authored,zones,HasPage)")&&
          ghost.Contains("if(pages>0)_ghostMapResolved=true;")&&ghost.Contains("if(_ghostMapResolved||!NativeMapOpen)return;"),
          "wiring: with the atlas open the map reference settles ambiguous routes once");
        C(map.Contains("if(route!=null){BindGhostLive(route,drone.SceneGroup);")&&map.Contains("if(route!=null){route.Position=go.transform.position")&&
          ghost.Contains("M29GhostZonePolicy.WithLive("),"wiring: a native drone model binds its route to the model's scene group");
        C(compass.Contains("!map.Visible.Contains(point.Category)||!SameGroup(point,group))continue;")&&
          map.Contains("MapForGroup(point?.Group)"),"wiring: compass and atlas pages read the route group the policy sets");
        int state=relay.IndexOf("private static string GhostState(",StringComparison.Ordinal);
        C(state>0&&relay.IndexOf("Write(app,\"ct-ghost.txt\"",state,StringComparison.Ordinal)>state&&
          relay.IndexOf("map.Focus(",state,StringComparison.Ordinal) is int focus&&(focus<0||focus>relay.IndexOf("private static string MiniMap(",StringComparison.Ordinal)),
          "relay: ct-ghost-state only reads and writes its report");
    }
}
