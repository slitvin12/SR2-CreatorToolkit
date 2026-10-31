using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace CreatorToolkit;
// M29 offline regression, task 4 (minimap and compass settings in navigation.cfg: size, corner,
// zoom 30-300 m, background transparency, north-up / heading-up, compass range, marker limit,
// phantom-drone priority) and task 5 (every enabled layer reaches the list, the minimap and the
// compass). Pure policies + source wiring; no Unity, no game process, no saves.
internal static class M29NavigationRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool b,string s)=>check(b,"m29 nav / "+s);
        Settings(C);Layout(C);Zoom(C);Compass(C);Layers(C);Png(C);Wiring(C,root);
    }
    private static bool Near(double a,double b,double eps=1e-3)=>Math.Abs(a-b)<=eps;

    private static void Settings(Action<bool,string> C)
    {
        var d=new M29NavigationSettings();
        C(d.Mode==0&&d.Size==1&&d.Range==90f&&d.Corner==0&&d.Transparency==0&&!d.NorthUp&&d.Compass&&d.CompassRange==250&&d.CompassMarkers==6&&d.Ghosts==0&&
          d.CompassRange==(int)M27CompassPolicy.Range&&d.CompassMarkers==M27CompassPolicy.MaxMarkers&&d.BackgroundAlpha==1f,
          "settings: defaults are the M27/M28 behaviour (minimap off, medium, 90 m, top right, opaque, heading-up; compass 250 m, 6 markers)");
        var s=new M29NavigationSettings{Mode=1,Size=2,Range=137f,Corner=3,Transparency=35,NorthUp=true,Compass=false,CompassRange=575,CompassMarkers=11,Ghosts=2};
        var back=M29NavigationSettings.Parse(s.Serialize(),out int applied,out int rejected);
        C(back.Serialize()==s.Serialize()&&applied==11&&rejected==0&&back.NorthUp&&!back.Compass&&back.Corner==3&&back.Ghosts==2,
          "settings: serialize/parse round trip keeps every key");
        var bad=M29NavigationSettings.Parse("# c\n\nminimap=terrain\r\nminimap.range=9999\nminimap.size=huge\ncompass.markers=0\ncompass.range=1013\n"+
            "minimap.transparency=37%\nunknown=1\nnoequals\nminimap.corner= bottom-left \n",out applied,out rejected);
        C(bad.Mode==1&&bad.Range==300f&&bad.Size==1&&bad.CompassMarkers==1&&bad.CompassRange==1000&&bad.Transparency==35&&bad.Corner==3&&applied==6&&rejected==3,
          "settings: numbers clamped, bad values keep the default, comments/CRLF/unknown keys skipped");
        C(M29NavigationSettings.Parse(null,out applied,out rejected).Serialize()==d.Serialize()&&applied==0&&rejected==0,"settings: a missing file is the defaults");
        var t=d.Copy();
        C(!t.Set("minimap.range","abc")&&!t.Set("nope","1")&&!t.Set("minimap.orientation","up")&&t.Serialize()==d.Serialize()&&t.Set("compass","OFF")&&!t.Compass&&
          t.Set("minimap.orientation","north")&&t.NorthUp&&d.Compass&&!d.NorthUp,"settings: rejected keys change nothing; Copy is independent");
        var r=s.Copy();r.ResetLook();
        C(r.Mode==1&&!r.Compass&&r.Size==1&&r.Range==90f&&r.Corner==0&&r.Transparency==0&&!r.NorthUp&&r.CompassRange==250&&r.CompassMarkers==6&&r.Ghosts==0,
          "settings: reset restores look and limits, keeps the minimap mode and the compass toggle");
        C(M29NavigationSettings.ZoomIn(90)==72&&M29NavigationSettings.ZoomOut(90)==112&&M29NavigationSettings.ZoomIn(31)==30&&M29NavigationSettings.ZoomOut(290)==300&&
          M29NavigationSettings.ZoomIn(float.NaN)==72,"settings: zoom buttons step x1.25 inside 30..300 m, whole metres");
        C(Near(M29NavigationSettings.RangeToSlider(30),0)&&Near(M29NavigationSettings.RangeToSlider(300),1)&&M29NavigationSettings.SliderToRange(0)==30&&
          M29NavigationSettings.SliderToRange(1)==300&&M29NavigationSettings.SliderToRange(.5f)==95&&M29NavigationSettings.SliderToRange(float.NaN)==30&&
          Enumerable.Range(30,271).All(m=>M29NavigationSettings.SliderToRange(M29NavigationSettings.RangeToSlider(m))==m),
          "settings: logarithmic zoom slider with an exact round trip for whole metres");
        C(M29NavigationSettings.ClampCompassRange(10)==50&&M29NavigationSettings.ClampCompassRange(262)==250&&M29NavigationSettings.ClampCompassRange(5000)==1000&&
          M29NavigationSettings.ClampMarkers(0)==1&&M29NavigationSettings.ClampMarkers(99)==16&&M29NavigationSettings.ClampTransparency(-3)==0&&
          M29NavigationSettings.ClampTransparency(103)==100&&Near(new M29NavigationSettings{Transparency=40}.BackgroundAlpha,.6),
          "settings: compass range 50..1000 m in 25 m steps, 1..16 markers, transparency 0..100 % in 5 % steps");
    }

    private static readonly (float W,float H)[] Screens={(1920,1080),(2560,1440),(1366,768),(1920,1440),(1600,1200),(3840,2160),(3440,1440)};
    private static void Layout(Action<bool,string> C)
    {
        foreach(var (w,h) in Screens)
            C(Near(M29MiniMapLayout.Diameter(1,h),Math.Clamp(h*.22f,170f,260f))&&M29MiniMapLayout.Diameter(0,h)<M29MiniMapLayout.Diameter(1,h)&&
              M29MiniMapLayout.Diameter(1,h)<M29MiniMapLayout.Diameter(2,h),"layout: medium = the M27 disc, small < medium < large at "+w+"x"+h);
        bool inside=true,sides=true,hud=true,info=true,m27=true;
        foreach(var (w,h) in Screens)
        for(int size=0;size<3;size++)
        {
            float d=M29MiniMapLayout.Diameter(size,h);
            for(int corner=0;corner<4;corner++)
            foreach(bool infoHud in new[]{false,true})
            for(int placement=0;placement<3;placement++)
            {
                var b=M29MiniMapLayout.Box(corner,w,h,d,infoHud,placement);
                inside&=b.X>=0&&b.Y>=0&&b.X+b.Width<=w&&b.Y+b.Height+M29MiniMapLayout.Label<=h&&Near(b.Width,d)&&Near(b.Height,d);
                sides&=M29MiniMapLayout.Right(corner)?Near(b.X+d,w-M29MiniMapLayout.Margin):Near(b.X,M29MiniMapLayout.Margin);
                if(M29MiniMapLayout.Bottom(corner))hud&=b.Y+d+M29MiniMapLayout.Label<=h*(1-M29MiniMapLayout.BottomHud)+.01f;
                else
                {
                    bool shifted=infoHud&&placement==(M29MiniMapLayout.Right(corner)?0:1);
                    info&=Near(b.Y,shifted?72+144+12:24);
                }
                // M27: top right, below the info HUD when it sits there.
                if(corner==0&&size==1)m27&=Near(b.X,w-d-24)&&Near(b.Y,infoHud&&placement==0?72+144+12:24);
            }
        }
        C(inside,"layout: every size and corner stays on screen with its label");
        C(sides,"layout: right corners keep the right margin, left corners the left one");
        C(hud,"layout: bottom corners stay above the native health / newbucks HUD");
        C(info,"layout: a top corner moves below the info HUD only when the HUD is on that side");
        C(m27,"layout: the default box is the M27 one");
        C(M29MiniMapLayout.MapYaw(false,1.2)==1.2&&M29MiniMapLayout.MapYaw(true,1.2)==0&&M29MiniMapLayout.MapYaw(false,double.NaN)==0&&
          Near(M29MiniMapLayout.PlayerTurn(true,Math.PI/2),90)&&M29MiniMapLayout.PlayerTurn(false,1.2)==0f,
          "orientation: heading-up turns the map, north-up turns the player marker instead");
        M28PlayerMarkerPolicy.Rotate(0,-1,M29MiniMapLayout.PlayerTurn(true,Math.PI/2),out float ex,out float ey);
        C(Near(ex,1)&&Near(ey,0),"orientation: facing east points right on a north-up minimap");
    }

    private static void Zoom(Action<bool,string> C)
    {
        float r=90f;int frames=0;
        while(r!=300f&&frames<600){r=M29MiniMapZoom.Step(r,300f,1f/60f);frames++;}
        C(r==300f&&frames>5&&frames<60,"zoom: smooth (several frames) and settled within a second ("+frames+" frames)");
        float a=M29MiniMapZoom.Step(90f,300f,1f/60f);
        C(a>90f&&a<300f&&M29MiniMapZoom.Step(90f,300f,0f)==90f&&M29MiniMapZoom.Step(float.NaN,120f,.1f)==120f&&M29MiniMapZoom.Step(299.9f,1000f,.1f)==300f,
          "zoom: one frame moves part of the way; dt 0 holds; NaN jumps; the target is clamped");
        C(!M29MiniMapZoom.Stale(90f,90.1f)&&M29MiniMapZoom.Stale(90f,100f)&&M29MiniMapZoom.Stale(0f,90f)&&Near(M29MiniMapZoom.TextureScale(90f,180f),.5)&&
          M29MiniMapZoom.TextureScale(180f,90f)==1f&&M29MiniMapZoom.TextureScale(0f,90f)==1f,
          "zoom: the terrain composite is rebuilt once the zoom left it and drawn scaled meanwhile, never beyond the disc");
    }

    private static M29CompassPolicy.Candidate K(string key,string layer,int priority,float distance)=>new(key,layer,priority,distance);
    private static void Compass(Action<bool,string> C)
    {
        // Eight capsules nearer than a gordo, a teleport and a resource: the M27 pick (six nearest) showed capsules only.
        var list=new List<M29CompassPolicy.Candidate>();
        for(int i=0;i<8;i++)list.Add(K("pt|Капсулы|"+i,"Капсулы",2,10+i));
        list.Add(K("pt|Гордо|g","Гордо",2,120));list.Add(K("pt|Телепорты|t","Телепорты",3,60));list.Add(K("pt|Ресурсы|r","Ресурсы",3,30));
        var picked=M29CompassPolicy.Pick(list,6,250);
        var layers=picked.Select(p=>p.Layer).Distinct().ToList();
        C(picked.Count==6&&layers.Count==4&&picked.Count(p=>p.Layer=="Капсулы")==3&&picked[0].Key=="pt|Капсулы|0",
          "compass: every layer in range gets a marker before a busy layer gets more");
        var near=M29CompassPolicy.Pick(new[]{K("sel","Капсулы",0,5000),K("a","Дроны",2,10),K("b","Дроны",2,400)},6,250);
        C(near.Count==2&&near[0].Key=="sel"&&near.All(p=>p.Key!="b"),"compass: priority 0 ignores the range, the others need it");
        C(M29CompassPolicy.Pick(list,0,250).Count==0&&M29CompassPolicy.Pick(null,6,250).Count==0&&M29CompassPolicy.Pick(list,100,250).Count==list.Count&&
          M29CompassPolicy.Pick(list,6,5).Count==0,"compass: limit 0, null, a limit above the candidates, a range below all");
        var dup=M29CompassPolicy.Pick(new[]{K("x","A",3,10),K("x","A",1,50),K("","B",1,1),K("y","B",2,float.NaN)},6,250);
        C(dup.Count==1&&dup[0].Priority==1,"compass: one marker per key (best priority wins); blank keys and unknown distances dropped");
        C(M29CompassPolicy.Layers(list,250)==4&&M29CompassPolicy.Starved(list,picked,250)==0&&M29CompassPolicy.Starved(list,M29CompassPolicy.Pick(list,2,250),250)==2,
          "compass: layers in range and layers left without a marker are counted");
        C(M29CompassPolicy.GhostPriority(0,true)==2&&M29CompassPolicy.GhostPriority(1,false)==1&&M29CompassPolicy.GhostPriority(2,true)==0&&
          M29CompassPolicy.GhostPriority(2,false)==1,"compass: phantom drones normal = drones, high = own markers, always = nearest pinned");
        var ghost=M29CompassPolicy.Pick(new[]{K("g1",M29CompassPolicy.GhostLayer,M29CompassPolicy.GhostPriority(2,true),900),K("c","Капсулы",2,5)},1,250);
        C(ghost.Count==1&&ghost[0].Key=="g1","compass: a pinned phantom drone keeps its slot beyond the range and before nearer targets");
    }

    private static void Layers(Action<bool,string> C)
    {
        C(M29LayerPolicy.HudNeedsSources(true,false,true)&&M29LayerPolicy.HudNeedsSources(false,true,true)&&!M29LayerPolicy.HudNeedsSources(true,true,false)&&
          !M29LayerPolicy.HudNeedsSources(false,false,true),"layers: the source census runs for the minimap or the compass while a source layer is on");
        C(M29LayerPolicy.SourceLayer("Горшочки")&&M29LayerPolicy.SourceLayer("Спавнеры")&&!M29LayerPolicy.SourceLayer("Капсулы"),"layers: the two source layers");
        C(M29LayerPolicy.SourceGroup(" zoneFields ","zoneGorge",false)=="zoneFields"&&M29LayerPolicy.SourceGroup("","zoneGorge",true)=="zoneGorge"&&
          M29LayerPolicy.SourceGroup(null,"zoneGorge",false)==""&&M29LayerPolicy.SourceGroup("","",true)=="",
          "layers: a source keeps its native owner; a potential source in a loaded scene gets the current group, otherwise none");
        C(M29LayerPolicy.CensusBudget(true)==32&&M29LayerPolicy.CensusBudget(false)<32,"layers: the HUD census uses a smaller per-frame budget than the atlas");
        // 100 capsules nearer than 3 pots and 2 gordos: the M27 cut (80 nearest) dropped both layers.
        var names=new List<string>();var distances=new List<float>();
        for(int i=0;i<100;i++){names.Add("Капсулы");distances.Add(i);}
        for(int i=0;i<3;i++){names.Add("Горшочки");distances.Add(200+i);}
        for(int i=0;i<2;i++){names.Add("Гордо");distances.Add(300+i);}
        var keep=M29LayerPolicy.RoundRobin(names,distances,80);
        C(keep.Count==80&&keep.Distinct().Count()==80&&keep.Count(i=>names[i]=="Горшочки")==3&&keep.Count(i=>names[i]=="Гордо")==2&&keep.Contains(0)&&!keep.Contains(99),
          "layers: the minimap cap keeps every enabled layer (round robin, nearest first)");
        C(M29LayerPolicy.RoundRobin(names,distances,0).Count==0&&M29LayerPolicy.RoundRobin(null,distances,5).Count==0&&
          M29LayerPolicy.RoundRobin(new[]{"a","b"},new[]{1f},5).Count==1,"layers: round robin bounds");
    }

    // The saved copy of the native player marker (player-marker-N.png) is read without
    // ImageConversion.LoadImage, which throws MissingMethodException in this game build.
    // Vectors: Python zlib/filters, cross-checked with Pillow.
    private static void Png(Action<bool,string> C)
    {
        const string Rgba="iVBORw0KGgoAAAANSUhEUgAAAAMAAAAFCAYAAACAcVaiAAAAC3RFWHRDb21tZW50AG0yOXbwR8QAAAAmSURBVHjaAUEAvv8AJrGTLLDJ1AkQNeNzAbIr/qgkO+avmnjsDQImbZDIpE/iiwAAACZJREFU/ItlRikT+84DUPtAFOeHY98xUCNOBJNd4kK4ZwFGPgVfwL/jHFMjFy23AAAAAElFTkSuQmCC";
        const string Rgb="iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR42mNkkRCZqfqeRfL3hw0yjwAdjgWVqsdOzAAAAABJRU5ErkJggg==";
        const string GreyAlpha="iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAQAAADYv8WvAAAAEklEQVR42mPeW3b4MtNS3nt/ARpCBVwtGCr4AAAAAElFTkSuQmCC";
        uint[] rgba={0x8E69A44Fu,0xD4CC0B07u,0x944DDF45u,0x4C8747BCu,0x53CB23AEu,0x90EEDAD4u,0x708E98D8u,0x9D49F1D2u,0x32CBF199u,0xA8FE2BB2u,0x57E466D6u,0x64D0DE70u,0x2C93B126u,0x09D4C9B0u,0x73E33510u};
        uint[] rgb={0xFF04131Du,0xFFE5594Du,0xFF141804u,0xFF033D9Du};
        uint[] greyAlpha={0x83626262u,0x0BFFFFFFu,0x76BDBDBDu,0x0E212121u};
        var good=Convert.FromBase64String(Rgba);
        var a=M29PngDecoder.Decode(good,out int w,out int h);
        C(w==3&&h==5&&a.SequenceEqual(rgba),"png: RGBA rows with all five filters, a split IDAT and a text chunk decode bottom row first");
        var b=M29PngDecoder.Decode(Convert.FromBase64String(Rgb),out w,out h);
        C(w==2&&h==2&&b.SequenceEqual(rgb),"png: RGB decodes opaque");
        var g=M29PngDecoder.Decode(Convert.FromBase64String(GreyAlpha),out w,out h);
        C(w==2&&h==2&&g.SequenceEqual(greyAlpha),"png: grey+alpha spreads the grey to r, g and b");
        bool Refused(byte[] bytes){try{M29PngDecoder.Decode(bytes,out _,out _);return false;}catch(InvalidDataException){return true;}}
        byte[] Patch(int at,byte value){var copy=(byte[])good.Clone();copy[at]=value;return copy;}
        C(Refused(null)&&Refused(new byte[10])&&Refused(Patch(1,0))&&Refused(good.Take(good.Length-30).ToArray())&&Refused(Patch(24,16))&&Refused(Patch(28,1))&&
          Refused(Patch(25,3)),"png: empty, foreign, truncated, 16-bit, interlaced and palette files are refused, not guessed");
    }

    private static void Wiring(Action<bool,string> C,string root)
    {
        string dir=Path.Combine(root,"src","creator");
        string R(string name){string p=Path.Combine(dir,name);return File.Exists(p)?File.ReadAllText(p):"";}
        string host=R("ToolkitMod.cs"),nav=R("M29NavigationTools.cs"),mini=R("MiniMapUi.cs"),terrain=R("MiniMapTerrain.cs"),compass=R("M27CompassMarkers.cs"),
            map=R("MapTools.cs"),explorer=R("ExplorerUi.cs"),relay=R("M28TestIpc.cs"),tests=R(Path.Combine("tests","CreatorToolkit.Tests.csproj"));
        C(host.Contains("(\"navigation settings\",Navigation.Tick),(\"atlas\",Map.Tick)")&&host.Contains("Navigation.Load();")&&host.Contains("Navigation?.Flush();"),
          "wiring: settings load at start, tick before the atlas, flush on quit");
        C(nav.Contains("M29NavigationSettings.FileName")&&nav.Contains("File.Replace(temp,path,null)")&&nav.Contains("M29MiniMapZoom.Step(")&&nav.Contains("_app.DataDirectory")&&
          nav.Contains("_app.Compass?.Wanted"),"wiring: navigation.cfg in UserData, atomic save, smooth zoom, live toggles adopted");
        C(mini.Contains("M29MiniMapLayout.Box(look.Corner,")&&mini.Contains("M29MiniMapLayout.Diameter(look.Size,")&&mini.Contains("M29MiniMapLayout.MapYaw(look.NorthUp,")&&
          mini.Contains("look.BackgroundAlpha")&&mini.Contains("M29MiniMapZoom.TextureScale(terrain.ComposedRange,range)")&&mini.Contains("M29LayerPolicy.RoundRobin(")&&
          mini.Contains("SourcesActualOnly&&point.SourceObservation&&!point.LiveSource")&&mini.Contains("playerMarker?.Draw(center,radius,true,turn,clip);"),
          "wiring: minimap size, corner, zoom, transparency, orientation and the per-layer cap");
        C(terrain.Contains("M29MiniMapLayout.Diameter(nav?.Settings.Size??1,Screen.height)")&&terrain.Contains("ComposedRange=range;")&&
          terrain.Contains("M29MiniMapZoom.Stale(ComposedRange,range)"),"wiring: the terrain composite follows the disc size and the zoom");
        C(compass.Contains("M29CompassPolicy.Pick(_candidates,max,range)")&&compass.Contains("M29CompassPolicy.GhostPriority(ghosts,point==pinned)")&&
          compass.Contains("nav.CompassRange")&&compass.Contains("internal bool Wanted")&&compass.Contains("map.SourcesActualOnly&&point.SourceObservation&&!point.LiveSource"),
          "wiring: compass range, limit, layer share, phantom priority and the list's source rule");
        C(map.Contains("M29LayerPolicy.HudNeedsSources(ShowMiniMap,")&&map.Contains("M29LayerPolicy.CensusBudget(atlas)")&&map.Contains("GroupName=groupName")&&
          map.Contains("M29LayerPolicy.SourceGroup(observation.SceneGroup,currentGroup,"),"wiring: sources are observed for the HUD and carry the scene group");
        C(explorer.Contains("\"Навигация\"")&&explorer.Contains("NavigationPage(width,height)")&&explorer.Contains("Slider(60,")&&explorer.Contains("nav.ResetLook"),
          "wiring: the explorer has the navigation page");
        C(relay.Contains("case \"ct-nav\"")&&relay.Contains("private static string Nav(")&&M28TestPolicy.Help.Any(h=>h.StartsWith("ct-nav ",StringComparison.Ordinal)),
          "relay: ct-nav handled and documented");
        C(tests.Contains("..\\M29NavigationPolicy.cs"),"tests: the navigation policy is linked");
        string marker=R("M28PlayerMarker.cs");
        C(marker.Contains("M29PngDecoder.Decode(bytes,out int pngWidth,out int pngHeight)")&&marker.Contains("Upload(pixels,pngWidth,pngHeight)")&&
          !marker.Contains("ImageConversion.LoadImage(")&&tests.Contains("..\\M29PngDecoder.cs"),"wiring: the saved player marker loads through the managed PNG reader");
        C(mini.Contains("DrawRoundedSurface(new Rect(chipX,y,chipWidth,26f),_hudFrame.normal.background,12f)")&&mini.Contains("Caption(\"рельеф: откройте карту игры в этой зоне\"")&&
          mini.Contains("terrainOn&&(terrainTexture==null||terrain.Layers==0)"),"wiring: minimap captions sit on a chip; a dark terrain disc says to open the atlas");
    }
}
