using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace CreatorToolkit;
// M28 offline regression: pure policies (drone-menu button, compass icons, never-loaded
// pods/gordos, vacpack styles, unlock cards, minimap player marker, test relay) and the
// source wiring. No Unity, no game process, no saves and no installed Mods\ files.
internal static class M28Regression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool b,string s)=>check(b,"m28 / "+s);
        Drone(C);Compass(C);Tracking(C);Palettes(C);Layout(C);Marker(C);Relay(C);Sources(C,root);
    }

    private static bool Near(float a,float b,float eps=1e-3f)=>Math.Abs(a-b)<=eps;

    private static void Drone(Action<bool,string> C)
    {
        C(M28DroneButtonPolicy.Mode(" BELOW ")==M28DroneButtonPolicy.Below&&M28DroneButtonPolicy.Mode("bogus")==M28DroneButtonPolicy.Auto&&
          M28DroneButtonPolicy.Mode(null)==M28DroneButtonPolicy.Auto&&M28DroneButtonPolicy.KnownMode("off")&&!M28DroneButtonPolicy.KnownMode("up"),
          "drone: placement modes are normalised and validated");
        float sw=1920,sh=1440;
        var collapsed=M27DronePanelPolicy.AtlasPanel(sw,sh,false,14);
        var expanded=M27DronePanelPolicy.AtlasPanel(sw,sh,true,14);
        var button=new MapShieldPolicy.Box(1840,30,60,60);
        C(M27DronePanelPolicy.Overlaps(button,collapsed),"drone: the live case — the native button sits under the atlas header");
        var below=M28DroneButtonPolicy.Place(button,collapsed,sw,sh,M28DroneButtonPolicy.Auto);
        var movedBelow=M28DroneButtonPolicy.Offset(button,below.Dx,below.Dy);
        C(below.Side==M28DroneButtonPolicy.Move.Below&&below.Dx==0&&below.Dy>0&&below.Clear&&!M27DronePanelPolicy.Overlaps(movedBelow,collapsed)&&
          movedBelow.Y>=collapsed.Bottom&&movedBelow.Bottom<=sh,"drone: collapsed header — auto drops the button just below the panel");
        var side=M28DroneButtonPolicy.Place(button,expanded,sw,sh,M28DroneButtonPolicy.Auto);
        var movedSide=M28DroneButtonPolicy.Offset(button,side.Dx,side.Dy);
        bool shallow=expanded.Bottom+M28DroneButtonPolicy.Gap*M27DronePanelPolicy.Scale(sh)-button.Y<=M28DroneButtonPolicy.MaxDropFraction*sh;
        C(side.Moved&&side.Clear&&!M27DronePanelPolicy.Overlaps(movedSide,expanded)&&
          (shallow?side.Side==M28DroneButtonPolicy.Move.Below:side.Side==M28DroneButtonPolicy.Move.Left&&side.Dy==0&&movedSide.Right<=expanded.X),
          "drone: expanded list — auto moves the button left of the panel instead of far down");
        var forcedBelow=M28DroneButtonPolicy.Place(button,expanded,sw,sh,M28DroneButtonPolicy.Below);
        C(forcedBelow.Side==M28DroneButtonPolicy.Move.Below&&!M27DronePanelPolicy.Overlaps(M28DroneButtonPolicy.Offset(button,0,forcedBelow.Dy),expanded),
          "drone: mode below always goes under the panel");
        var forcedLeft=M28DroneButtonPolicy.Place(button,collapsed,sw,sh,M28DroneButtonPolicy.Left);
        C(forcedLeft.Side==M28DroneButtonPolicy.Move.Left&&forcedLeft.Dx<0&&forcedLeft.Dy==0,"drone: mode left keeps the height");
        var off=M28DroneButtonPolicy.Place(button,collapsed,sw,sh,M28DroneButtonPolicy.Off);
        var clear=M28DroneButtonPolicy.Place(new MapShieldPolicy.Box(20,20,60,60),collapsed,sw,sh,M28DroneButtonPolicy.Auto);
        var invalid=M28DroneButtonPolicy.Place(default,collapsed,sw,sh,M28DroneButtonPolicy.Auto);
        var nan=M28DroneButtonPolicy.Place(button,collapsed,float.NaN,sh,M28DroneButtonPolicy.Auto);
        C(!off.Moved&&!clear.Moved&&clear.Clear&&!invalid.Moved&&!nan.Moved,"drone: off, a clear button and invalid input never move anything");
        var tiny=M28DroneButtonPolicy.Place(new MapShieldPolicy.Box(250,10,60,200),M27DronePanelPolicy.AtlasPanel(320,240,true,14),320,240,M28DroneButtonPolicy.Below);
        C(float.IsFinite(tiny.Dy)&&tiny.Dy>=0&&(tiny.Clear||tiny.Side==M28DroneButtonPolicy.Move.Below),"drone: a too small screen reduces the overlap without leaving the screen");
        var obstacle=M28DroneButtonPolicy.Obstacle(collapsed,true,button,sw,sh,M28DroneButtonPolicy.Auto);
        var unknown=M28DroneButtonPolicy.Obstacle(collapsed,false,button,sw,sh,M28DroneButtonPolicy.Auto);
        C(Near(unknown.X,collapsed.X)&&Near(unknown.Bottom,collapsed.Bottom)&&obstacle.X<=collapsed.X+1e-3f&&obstacle.Bottom>=movedBelow.Bottom-1e-3f&&
          obstacle.Right>=collapsed.Right-1e-3f,"drone: the drone panel keeps clear of the atlas and of the moved button");
        var union=M28DroneButtonPolicy.Union(new MapShieldPolicy.Box(0,0,10,10),new MapShieldPolicy.Box(20,30,5,5));
        C(Near(union.X,0)&&Near(union.Y,0)&&Near(union.Right,25)&&Near(union.Bottom,35)&&M28DroneButtonPolicy.Union(default,button).Valid,
          "drone: union and inflate helpers");
        C(M28DroneButtonPolicy.Describe(false,default).Contains("не найдена")&&M28DroneButtonPolicy.Describe(true,below).Contains("опущена")&&
          M28DroneButtonPolicy.Describe(true,forcedLeft).Contains("левее")&&M28DroneButtonPolicy.Describe(true,clear).Contains("не пересекает")&&
          M28DroneButtonPolicy.ModeTitle("left")=="левее атласа","drone: Russian status texts");
    }

    private static void Compass(Action<bool,string> C)
    {
        C(M28CompassIconPolicy.Style("REPLACE")==M28CompassIconPolicy.Replace&&M28CompassIconPolicy.Style("star")==M28CompassIconPolicy.Overlay&&
          M28CompassIconPolicy.KnownStyle("off")&&!M28CompassIconPolicy.KnownStyle("star")&&M28CompassIconPolicy.StyleTitle("off")=="звёзды игры",
          "compass: icon styles are normalised");
        C(M28CompassIconPolicy.CustomKey("abc")=="cw|abc"&&M28CompassIconPolicy.CustomKey(null)=="cw|"&&M28CompassIconPolicy.GoalPriority<M28CompassIconPolicy.OwnPriority,
          "compass: own markers have their own key space and the goal ranks first");
        C(M28CompassIconPolicy.CellRect(0,6,4,64,out float x0,out float y0,out float w0,out float h0)&&x0==0&&y0==192&&w0==64&&h0==64&&
          M28CompassIconPolicy.CellRect(7,6,4,64,out float x7,out float y7,out _,out _)&&x7==64&&y7==128&&
          !M28CompassIconPolicy.CellRect(24,6,4,64,out _,out _,out _,out _)&&!M28CompassIconPolicy.CellRect(-1,6,4,64,out _,out _,out _,out _),
          "compass: wiki atlas cells map to bottom-up sprite rects");
        C(M28CompassIconPolicy.PrimaryImage(new[]{"bg","Marker_Star","glow"},new[]{100f,10f,5f})==1&&
          M28CompassIconPolicy.PrimaryImage(new[]{"a","b"},new[]{10f,40f})==1&&
          M28CompassIconPolicy.PrimaryImage(new[]{"a"},new[]{float.NaN})==-1&&M28CompassIconPolicy.PrimaryImage(null,null)==-1,
          "compass: the star image of the native marker prefab is found");
        C(Near(M28CompassIconPolicy.IconSize(0,0),M28CompassIconPolicy.DefaultIcon*M28CompassIconPolicy.IconScale)&&
          M28CompassIconPolicy.IconSize(1000,10)==M28CompassIconPolicy.MaxIcon&&M28CompassIconPolicy.IconSize(5,5)==M28CompassIconPolicy.MinIcon,
          "compass: icon size follows the star, clamped");
    }

    private static void Tracking(Action<bool,string> C)
    {
        C(M28TrackingStatePolicy.Classify(true,false,false,true)==M28TrackingStatePolicy.Missing.Known&&
          M28TrackingStatePolicy.Classify(false,true,false,false)==M28TrackingStatePolicy.Missing.NeverLoaded&&
          M28TrackingStatePolicy.Classify(false,true,true,false)==M28TrackingStatePolicy.Missing.Unknown&&
          M28TrackingStatePolicy.Classify(false,false,false,false)==M28TrackingStatePolicy.Missing.Unknown&&
          M28TrackingStatePolicy.Classify(false,true,false,true)==M28TrackingStatePolicy.Missing.Conflict,
          "tracking: no save model + complete census = area never loaded (not «unknown»)");
        var rows=new List<M27TrackingPolicy.Row>{
            new(){Kind=M27TrackingPolicy.Pods,Id="a",Done=true},
            new(){Kind=M27TrackingPolicy.Pods,Id="b",Done=false},
            new(){Kind=M27TrackingPolicy.Pods,Id="c",Done=false,Unvisited=true,State=M28TrackingStatePolicy.PodNeverLoaded},
            new(){Kind=M27TrackingPolicy.Pods,Id="d"},
            new(){Kind=M27TrackingPolicy.Gordos,Id="g",Done=false,Unvisited=true,State=M28TrackingStatePolicy.GordoNeverLoaded}};
        var pods=M27TrackingPolicy.Count(rows,M27TrackingPolicy.Pods);
        var gordos=M27TrackingPolicy.Count(rows,M27TrackingPolicy.Gordos);
        C(pods.Total==4&&pods.Done==1&&pods.Pending==2&&pods.Unvisited==1&&pods.Unknown==1&&gordos.Unvisited==1,"tracking: tallies count never-loaded pods/gordos as not done");
        string summary=M27TrackingPolicy.Summary(M27TrackingPolicy.Pods,pods);
        C(summary.Contains("не открыто 2")&&summary.Contains("из них 1")&&M28TrackingStatePolicy.UnvisitedSuffix(0)==""&&
          M27TrackingPolicy.Include(rows[2],2)&&!M27TrackingPolicy.Include(rows[2],1),"tracking: the summary explains them and «не открытые» lists them");
        C(M27TrackingPolicy.Matches(rows[2],"загружал"),"tracking: the never-loaded state is searchable");
    }

    private static void Palettes(Action<bool,string> C)
    {
        C(M28PalettePolicy.Unlockable(true,false)&&!M28PalettePolicy.Unlockable(true,true)&&!M28PalettePolicy.Unlockable(false,false),
          "palettes: only vacpack palettes not owned yet are opened");
        string ok=M28PalettePolicy.Report(10,3,7,3,0),failed=M28PalettePolicy.Report(10,1,7,0,2);
        C(ok.Contains("открыто 3")&&ok.Contains("уже было 7")&&ok.Contains("всего 10")&&ok.Contains("Слаймопедии 3")&&!ok.Contains("ошибок")&&
          failed.Contains("ошибок 2")&&!failed.Contains("Слаймопедии"),"palettes: report text");
    }

    private static void Layout(Action<bool,string> C)
    {
        float width=1100,height=594,col=M28UnlockLayout.Column(width);
        C(M28UnlockLayout.Fits(7,width,height)&&!M28UnlockLayout.Fits(9,width,height)&&!M28UnlockLayout.Fits(0,width,height)&&
          M28UnlockLayout.X(1,width)==col+M28UnlockLayout.Gap&&M28UnlockLayout.X(6,width)==0&&M28UnlockLayout.Y(6)==M28UnlockLayout.Top+3*M28UnlockLayout.RowStep,
          "unlock cards: seven cards fit the 1100x594 page in two columns");
        C(col-218>=0&&41+M28UnlockLayout.ButtonHeight<=M28UnlockLayout.CardHeight&&col-254>0&&90+30<=M28UnlockLayout.CardHeight,
          "unlock cards: button, blurb and result stay inside a card");
    }

    private static void Marker(Action<bool,string> C)
    {
        M28PlayerMarkerPolicy.Rotate(1,0,90,out float rx,out float ry);
        M28PlayerMarkerPolicy.Rotate(0,-1,-90,out float lx,out float ly);
        C(Near(rx,0)&&Near(ry,1)&&Near(lx,-1)&&Near(ly,0),"marker: positive degrees turn clockwise on screen");
        C(Near(M28PlayerMarkerPolicy.Heading(0,-1),0)&&Near(M28PlayerMarkerPolicy.Heading(1,0),90)&&Near(Math.Abs(M28PlayerMarkerPolicy.Heading(0,1)),180)&&
          Near(M28PlayerMarkerPolicy.Heading(-1,0),-90)&&M28PlayerMarkerPolicy.Heading(0,0)==0&&M28PlayerMarkerPolicy.Heading(float.NaN,1)==0,
          "marker: heading is measured clockwise from screen up");
        float h=M28PlayerMarkerPolicy.Heading(.6f,.8f);
        M28PlayerMarkerPolicy.Rotate(.6f,.8f,-h,out float ux,out float uy);
        C(Near(ux,0)&&Near(uy,-1),"marker: turning a part by -heading makes the facing point up (heading-up minimap)");
        C(Near(M28PlayerMarkerPolicy.Normalize(270),-90)&&Near(M28PlayerMarkerPolicy.Normalize(-190),170)&&M28PlayerMarkerPolicy.Normalize(float.NaN)==0&&
          Near(M28PlayerMarkerPolicy.Normalize(180),180),"marker: angles are normalised to (-180,180]");
        C(Near(M28PlayerMarkerPolicy.FitScale(88,88,44),.5f)&&M28PlayerMarkerPolicy.FitScale(0,0,44)==1&&
          Near(M28PlayerMarkerPolicy.FitScale(10,10,1000),M28PlayerMarkerPolicy.MaxSize/10f)&&Near(M28PlayerMarkerPolicy.FitScale(10,10,1),M28PlayerMarkerPolicy.MinSize/10f),
          "marker: the copy is scaled to the minimap size, clamped");
        M28PlayerMarkerPolicy.FitAspect(100,50,10,10,out float aw,out float ah);
        M28PlayerMarkerPolicy.FitAspect(50,100,20,10,out float bw,out float bh);
        M28PlayerMarkerPolicy.FitAspect(50,100,0,10,out float cw,out float ch);
        C(Near(aw,50)&&Near(ah,50)&&Near(bw,50)&&Near(bh,25)&&Near(cw,50)&&Near(ch,100),"marker: Image.preserveAspect fitting");
        C(M28PlayerMarkerPolicy.RadialVisible(.1f,.1f,1f,0,true)&&!M28PlayerMarkerPolicy.RadialVisible(.1f,.1f,0f,0,true)&&
          M28PlayerMarkerPolicy.RadialVisible(.25f,.25f,.25f,0,true)&&!M28PlayerMarkerPolicy.RadialVisible(.75f,.25f,.25f,0,true)&&
          M28PlayerMarkerPolicy.RadialVisible(.75f,.25f,.25f,0,false)&&!M28PlayerMarkerPolicy.RadialVisible(.25f,.25f,.25f,0,false)&&
          M28PlayerMarkerPolicy.RadialVisible(.75f,.75f,.25f,2,true)&&!M28PlayerMarkerPolicy.RadialVisible(.25f,.75f,.25f,2,true)&&
          M28PlayerMarkerPolicy.RadialVisible(.5f,.5f,.1f,1,true),"marker: Radial360 fill (origin bottom/top, clockwise or not)");
        C(M28PlayerMarkerPolicy.LinearVisible(.2f,.9f,.5f,0,0)&&!M28PlayerMarkerPolicy.LinearVisible(.8f,.9f,.5f,0,0)&&
          M28PlayerMarkerPolicy.LinearVisible(.8f,.1f,.5f,0,1)&&M28PlayerMarkerPolicy.LinearVisible(.9f,.2f,.5f,1,0)&&
          !M28PlayerMarkerPolicy.LinearVisible(.9f,.8f,.5f,1,0)&&M28PlayerMarkerPolicy.LinearVisible(.9f,.8f,.5f,1,1)&&
          M28PlayerMarkerPolicy.LinearVisible(0,0,1,0,0)&&!M28PlayerMarkerPolicy.LinearVisible(0,0,0,0,0),"marker: horizontal/vertical fill");
    }

    private static void Relay(Action<bool,string> C)
    {
        C(M28TestPolicy.SafeTag("name:Drone Menu/x")=="name_Drone_Menu_x"&&M28TestPolicy.SafeTag("")=="x"&&M28TestPolicy.SafeTag("!!!")=="x"&&
          M28TestPolicy.SafeTag(new string('a',100)).Length==40&&M28TestPolicy.SafeTag("..\\..\\evil")=="evil","relay: dump file tags cannot leave the data folder");
        C(M28TestPolicy.Depth("3")==3&&M28TestPolicy.Depth("99")==M28TestPolicy.MaxDepth&&M28TestPolicy.Depth("0")==1&&M28TestPolicy.Depth("x")==M28TestPolicy.DefaultDepth,
          "relay: dump depth is bounded");
        C(M28TestPolicy.TryScreenPoint("10","20",1920,1440,out float px,out float py)&&px==10&&py==20&&
          !M28TestPolicy.TryScreenPoint("-1","5",1920,1440,out _,out _)&&!M28TestPolicy.TryScreenPoint("1920","5",1920,1440,out _,out _)&&
          !M28TestPolicy.TryScreenPoint("a","5",1920,1440,out _,out _)&&!M28TestPolicy.TryScreenPoint("NaN","5",1920,1440,out _,out _)&&
          M28TestPolicy.TryScreenPoint("1e3","1",1920,1440,out float ex,out _)&&ex==1000,"relay: hit-test points are validated");
        C(M28TestPolicy.Box(1.4f,2.6f,10,20)=="1,3 10x20"&&M28TestPolicy.Box(float.NaN,0,1,1)=="nan,0 1x1"&&
          M28TestPolicy.OneLine("a\r\nb")=="a b"&&M28TestPolicy.OneLine(new string('z',300),10).Length==11,"relay: one-line formatting");
        C(M28TestPolicy.OptInFile=="ct-test.enabled"&&M28TestPolicy.Help.Length>=18&&M28TestPolicy.Help[0].Contains(M28TestPolicy.OptInFile),
          "relay: opt-in file and help");
    }

    internal static readonly string[] Commands={"ct-help","ct-state","ct-ui-dump","ct-ui-hit","ct-ui-find","ct-continue-test","ct-atlas-panel","ct-minimap","ct-menu",
        "m28-drone","m28-compass-dump","m28-compass-style","m28-palettes","m28-unlock","m28-confirm","m28-player-marker","m28-custom-add",
        "m28-custom-goal","m28-custom-del","m28-tracking"};

    private static void Sources(Action<bool,string> C,string root)
    {
        string dir=Path.Combine(root,"src","creator");
        string R(string name){string p=Path.Combine(dir,name);return File.Exists(p)?File.ReadAllText(p):"";}
        string host=R("ToolkitMod.cs"),relay=R("M28TestIpc.cs"),toggle=R("M28DroneToggle.cs"),panel=R("M27DronePanelLayout.cs"),map=R("MapTools.cs"),
            mini=R("MiniMapUi.cs"),marker=R("M28PlayerMarker.cs"),compass=R("M27CompassMarkers.cs"),styles=R("UnlockStyleTools.cs"),unlockUi=R("UnlockUi.cs"),
            tracking=R("WorldTrackingTools.cs"),gordos=R("MapGordoTools.cs"),tests=R(Path.Combine("tests","CreatorToolkit.Tests.csproj"));
        C(host.Contains("DroneToggle = new DroneToggleTools(this);")&&host.Contains("PlayerMarker = new PlayerMarkerTools(this);")&&
          host.Contains("(\"drone toggle\",DroneToggle.LateTick)")&&host.Contains("(\"compass icons\",Compass.LateTick)")&&
          host.Contains("(\"player marker\",PlayerMarker.LateTick)")&&host.Contains("foreach(var tick in _m28LateTicks)SafeTick(tick.Label,tick.Run);")&&
          host.Contains("SafeTick(\"restore drone toggle\",()=>DroneToggle?.Reset());")&&(host.Contains("DroneToggle?.Reset();\r\n")||host.Contains("DroneToggle?.Reset();\n")),
          "wiring: M28 tools are created, late-ticked after the native layout and restored");
        int m28=host.IndexOf("M28TestIpc.Handle(this,_ui,command,NavigateMenu)",StringComparison.Ordinal),m27=host.IndexOf("M27AcceptanceIpc.Handle(this,_ui,command,NavigateMenu)",StringComparison.Ordinal);
        C(m28>0&&m27>m28&&host.Contains("details.Append(M28TestIpc.Diagnostics(this));"),"wiring: ct-/m28- relay and its diagnostics lines");
        C(relay.Contains("M28TestPolicy.OptInFile")&&relay.Contains("_nextCheck=-1;")&&relay.Contains("pending.Title!=title")&&
          relay.Contains("summary.Name!=M26AcceptanceCore.TestGameName")&&!relay.Contains("TryContinueGame()")&&
          relay.Split("M26AcceptanceCore.RequireTestSave(app);").Length-1>=4&&Commands.All(c=>relay.Contains("case \""+c+"\"")),
          "relay: opt-in gated, test-save only for changes, never continues another save, every command handled");
        C(Commands.All(c=>M28TestPolicy.Help.Any(h=>h.StartsWith(c+" ",StringComparison.Ordinal)||h.Contains(c))),"relay: every command is documented in ct-help");
        C(toggle.Contains("_toggleDroneMenuInput")&&toggle.Contains("M28DroneButtonPolicy.Place(")&&toggle.Contains("anchoredPosition")&&
          toggle.Contains("M27DronePanelPolicy.MaxOverwrites")&&toggle.Contains("_droneMenu")&&toggle.Contains("internal bool Shielded(Component control)"),
          "drone button: found through the native toggle input, moved through anchoredPosition only, restored");
        C(panel.Contains("M28DroneButtonPolicy.Obstacle(M27DronePanelPolicy.AtlasPanel(sw,sh,true,_layers)")&&panel.Contains("toggle.TryBaseBox(out button)")&&
          map.Contains("!(_app.DroneToggle?.Shielded(control)??false)"),"drone button: no shield hole under it, the drone panel avoids it");
        C(compass.Contains("_radarInstances")&&compass.Contains("TryCast<ManualRadarEntry>")&&compass.Contains("M28CompassIconPolicy.GoalPriority")&&
          compass.Contains("CustomWaypointTools.Category")&&compass.Contains("MarkerColor(")&&compass.Contains("CreatorToolkit.CompassIcon")&&
          compass.Contains("RegisterTrackedGameObjectAdvanced")&&compass.Contains("UnregisterTrackedGameObjectAdvanced"),
          "compass: real icons on the bar's own instances, own markers with the native flag in their colour");
        C(marker.Contains("_playerMarker")&&marker.Contains("_moveBelowMarkersTransform")&&marker.Contains("_arrowRect")&&marker.Contains("Graphics.Blit(")&&
          marker.Contains("player-marker.txt")&&marker.Contains("HideFlags.HideAndDontSave")&&marker.Contains("GUI.DrawTexture(")&&
          !marker.Contains("DrawTextureWithTexCoords")&&!marker.Contains("BeginGroup")&&marker.Contains("RenderTexture.ReleaseTemporary(target)"),
          "player marker: copied once per map opening, drawn with the tested DrawTexture path only");
        C(mini.Contains("playerMarker?.Draw(center,radius,true,turn);")&&mini.Contains("!playerMarker.Draw(center,radius,false,turn)")&&mini.Contains("\"↑\",_badge"),
          "player marker: cone under the icons, portrait on top, arrow until the first copy");
        C(styles.Contains("_palettes")&&styles.Contains("SetOwned(")&&styles.Contains("VacPalette")&&styles.Contains("M28PalettePolicy.")&&
          unlockUi.Contains("Открыть все стили вакпака")&&unlockUi.Contains("_app.Unlocks.Palettes")&&unlockUi.Contains("M28UnlockLayout."),
          "palettes: the seventh unlock card opens the vacpack styles of the save");
        C(tracking.Contains("M28TrackingStatePolicy.PodNeverLoaded")&&map.Contains("PodNeverLoaded(")&&gordos.Contains("GordoNeverLoaded("),
          "tracking: never-loaded pods and gordos are labelled");
        C(tests.Contains("..\\M28Policy.cs"),"tests: the policy file is linked into the offline checker");
        string doc=Path.Combine(root,"tools","ct-test","TESTING.md");
        C(File.Exists(doc)&&File.ReadAllText(doc).Contains(M28TestPolicy.OptInFile),"tools: tools\\ct-test\\TESTING.md documents the opt-in test relay");
    }
}
