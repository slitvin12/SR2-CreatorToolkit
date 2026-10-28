using System;
using System.Collections.Generic;
using System.Globalization;

namespace CreatorToolkit;

// M27 rules. Deliberately free of Unity and Il2Cpp types: the offline checker
// links this file directly and unit-tests every decision (tests\M27Regression.cs).

// 1. Native map drone panel (MapUI._droneMenu) placement. Boxes are top-left
//    screen pixels. The creator atlas panel is drawn by ToolkitUi.DrawMapOverlay
//    at logical new(sw-344,20,324,PanelExpanded?MapLayerOrder.PanelHeight(n):56)
//    under the uniform GUI scale clamp(Screen.height/1080,.6,1.3).
internal static class M27DronePanelPolicy
{
    internal const float Margin=12f,AtlasInset=344f,AtlasTop=20f,AtlasWidth=324f,AtlasCollapsedHeight=56f;
    // Drift beyond this means the game itself rewrote the panel position.
    internal const float WriteTolerance=.5f;
    internal const int MaxOverwrites=30;
    // The DroneMenuUI root frames the visible card (live: 982x630 at 1920x1440); its
    // children alone can be laid out smaller than the card. A stretched root
    // (full-screen container) is not a panel box.
    internal static bool UsableRoot(float width,float height,float screenWidth,float screenHeight)=>
        float.IsFinite(width)&&float.IsFinite(height)&&width>=1f&&height>=1f&&
        width<screenWidth*.7f&&height<screenHeight*.9f;
    internal readonly struct Decision
    {
        internal readonly float Shift;
        internal readonly bool Collapse,Clear;
        internal Decision(float shift,bool collapse,bool clear){Shift=shift;Collapse=collapse;Clear=clear;}
    }
    internal static float Scale(float screenHeight)=>float.IsFinite(screenHeight)&&screenHeight>0?Math.Clamp(screenHeight/1080f,.6f,1.3f):1f;
    internal static MapShieldPolicy.Box AtlasPanel(float screenWidth,float screenHeight,bool expanded,int layers)
    {
        float scale=Scale(screenHeight);
        float height=expanded?MapLayerOrder.PanelHeight(layers):AtlasCollapsedHeight;
        return new MapShieldPolicy.Box(screenWidth-AtlasInset*scale,AtlasTop*scale,AtlasWidth*scale,height*scale);
    }
    internal static bool Overlaps(MapShieldPolicy.Box a,MapShieldPolicy.Box b)=>
        a.Valid&&b.Valid&&a.X<b.Right&&b.X<a.Right&&a.Y<b.Bottom&&b.Y<a.Bottom;
    // drone is the panel exactly where the game placed it (any shift of ours removed).
    // Shift moves it down. Collapse asks for the compact atlas header when even the
    // lowest on-screen position would still overlap the expanded atlas panel.
    internal static Decision Decide(MapShieldPolicy.Box drone,MapShieldPolicy.Box expandedAtlas,MapShieldPolicy.Box collapsedAtlas,
        bool expanded,bool allowCollapse,float screenHeight,float margin=Margin)
    {
        if(!drone.Valid||!float.IsFinite(screenHeight)||screenHeight<=0||!float.IsFinite(margin)||margin<0)
            return new Decision(0,false,true);
        var atlas=expanded?expandedAtlas:collapsedAtlas;
        if(!Overlaps(drone,atlas))return new Decision(0,false,true);
        float room=Math.Max(0f,screenHeight-margin-drone.Bottom);
        float need=atlas.Bottom+margin-drone.Y;
        if(need<=room)return new Decision(need,false,true);
        if(expanded&&allowCollapse)
        {
            if(!Overlaps(drone,collapsedAtlas))return new Decision(0,true,true);
            float compact=collapsedAtlas.Bottom+margin-drone.Y;
            return compact<=room?new Decision(compact,true,true):new Decision(Math.Min(compact,room),true,false);
        }
        return new Decision(Math.Min(need,room),false,false);
    }
    internal static bool Same(float a,float b,float tolerance=.01f)=>Math.Abs(a-b)<=tolerance;
    internal static string Describe(Decision decision)=>
        decision.Shift<=.01f&&!decision.Collapse?"Панель дронов не пересекает атлас":
        (decision.Shift>.01f?"Панель дронов сдвинута вниз на "+Math.Round(decision.Shift).ToString(CultureInfo.InvariantCulture)+" px":"Панель дронов на месте")+
        (decision.Collapse?" · атлас свёрнут, пока открыта панель дронов":"")+
        (decision.Clear?"":" · экран слишком низкий: перекрытие уменьшено, но не устранено");
}

// 2. «Разблокировка»: slime styles and gadget blueprints.
internal static class M27UnlockPolicy
{
    // SlimeAppearance.AppearanceSaveSet: NONE=0 (base look, never persisted), CLASSIC=1, SECRET_STYLE=2.
    internal const int SaveNone=0,SaveClassic=1,SaveSecret=2;
    internal static bool UnlockableStyle(int saveSet,bool unlocked)=>!unlocked&&(saveSet==SaveClassic||saveSet==SaveSecret);
    internal static bool DiscoverSecret(int saveSet,bool discovered)=>saveSet==SaveSecret&&!discovered;
    // Secondary gadget halves share their base blueprint; a prefab-less record is not placeable.
    internal static bool UnlockableBlueprint(bool hasPrefab,bool secondary,bool owned)=>hasPrefab&&!secondary&&!owned;
    internal static string StyleReport(int slimes,int unlocked,int already,int discovered,int failed)=>
        "Стили слаймов: открыто "+unlocked+" · уже было "+already+" · секретных стилей обнаружено "+discovered+
        " · видов "+slimes+(failed>0?" · ошибок "+failed+" (подробности в логе)":"")+". Сохраняется в сейве.";
    internal static string BlueprintReport(int examined,int unlocked,int already,int unavailable,int failed)=>
        "Чертежи гаджетов: открыто "+unlocked+" · уже было "+already+" · проверено "+examined+
        (unavailable>0?" · недоступно для чертежа "+unavailable:"")+
        (failed>0?" · ошибок "+failed+" (подробности в логе)":"")+". Сохраняется в сейве.";
}

// 3. World tracking lists: pods, gordos, AccessDoors and teleport points.
internal static class M27TrackingPolicy
{
    internal const int Pods=0,Gordos=1,Doors=2,Teleports=3,MaxRows=600;
    internal const float RefreshSeconds=1f,DoorScanSeconds=5f,ZoneRetrySeconds=30f;
    // A zone name the game actually localized (not empty, not a "!!key" placeholder).
    internal static bool UsableZoneTitle(string title)=>!string.IsNullOrWhiteSpace(title)&&!title.StartsWith("!!",StringComparison.Ordinal);
    // Zone titles kept between game sessions (UserData/CreatorToolkit/zone-titles.txt):
    // "g<TAB>scene group<TAB>title" or "p<TAB>atlas point id<TAB>title". Live atlas titles win.
    internal const int MaxCachedZoneTitles=4096;
    internal static string SerializeZoneTitle(bool group,string key,string title)=>(group?"g\t":"p\t")+CleanField(key)+"\t"+CleanField(title);
    internal static bool TryParseZoneTitle(string line,out bool group,out string key,out string title)
    {
        group=false;key=title=null;
        if(string.IsNullOrEmpty(line))return false;
        var parts=line.Split('\t');
        if(parts.Length!=3||(parts[0]!="g"&&parts[0]!="p"))return false;
        string k=parts[1].Trim(),t=parts[2].Trim();
        if(k.Length==0||k.Length>256||t.Length>128||!UsableZoneTitle(t))return false;
        group=parts[0]=="g";key=k;title=t;
        return true;
    }
    private static string CleanField(string s)=>(s??"").Replace('\t',' ').Replace('\r',' ').Replace('\n',' ').Trim();
    internal static readonly string[] Kinds={"Капсулы","Гордо","Двери","Телепорты"};
    private static readonly string[][] FilterSets={
        new[]{"Все","Открытые","Неоткрытые"},
        new[]{"Все","Лопнувшие","Живые"},
        new[]{"Все","Открытые","Закрытые"},
        new[]{"Все"}};
    internal static int ClampKind(int kind)=>Math.Clamp(kind,0,Kinds.Length-1);
    internal static string[] Filters(int kind)=>FilterSets[ClampKind(kind)];
    internal static int ClampFilter(int kind,int filter)=>Math.Clamp(filter,0,Filters(kind).Length-1);
    // Atlas category of each list; doors also come from the save model itself.
    internal static int KindOf(string category)=>category switch{"Капсулы"=>Pods,"Гордо"=>Gordos,"Двери"=>Doors,"Телепорты"=>Teleports,_=>-1};
    // AccessDoor.State: LOCKED=0, OPEN=1, CLOSED=2.
    internal static bool DoorIsOpen(int state)=>state==1;
    internal static string DoorState(int state)=>state switch{0=>"Заперта",1=>"Открыта",2=>"Закрыта",_=>"Состояние неизвестно"};
    internal sealed class Row
    {
        internal int Kind;
        internal string Id="",Name="",Zone="",State="";
        // Raw scene group name behind the zone title (searchable, not displayed).
        internal string Group="";
        internal bool? Done;
        // M28: not done because its area has never been loaded in this save (no save model yet).
        internal bool Unvisited;
        internal float Distance=-1f;
        // Atlas point (MapTools.Point) when the location is known.
        internal object Tag{get;set;}
        internal string Key=>Kind.ToString(CultureInfo.InvariantCulture)+"|"+(Id??"");
    }
    // Filter 1 = done (opened/burst/open), 2 = not done; unknown state only under «Все».
    internal static bool Include(Row row,int filter)=>row!=null&&(filter<=0||row.Done is bool done&&(filter==1?done:!done));
    internal static bool Matches(Row row,string needle)
    {
        if(string.IsNullOrEmpty(needle))return true;
        string text=((row.Name??"")+" "+(row.Id??"")+" "+(row.Zone??"")+" "+(row.State??"")+" "+(row.Group??"")).ToLowerInvariant();
        return text.Contains(needle,StringComparison.Ordinal);
    }
    internal static int Compare(Row a,Row b)
    {
        bool ka=float.IsFinite(a.Distance)&&a.Distance>=0,kb=float.IsFinite(b.Distance)&&b.Distance>=0;
        if(ka!=kb)return ka?-1:1;
        if(ka){int d=a.Distance.CompareTo(b.Distance);if(d!=0)return d;}
        int c=string.CompareOrdinal(a.Zone,b.Zone);if(c!=0)return c;
        c=string.CompareOrdinal(a.Name,b.Name);if(c!=0)return c;
        return string.CompareOrdinal(a.Id,b.Id);
    }
    internal static List<Row> Select(IEnumerable<Row> rows,int kind,int filter,string search,int max=MaxRows)
    {
        var result=new List<Row>();
        if(rows==null)return result;
        kind=ClampKind(kind);filter=ClampFilter(kind,filter);
        string needle=(search??"").Trim().ToLowerInvariant();
        foreach(var row in rows)if(row!=null&&row.Kind==kind&&Include(row,filter)&&Matches(row,needle))result.Add(row);
        result.Sort(Compare);
        if(max>=0&&result.Count>max)result.RemoveRange(max,result.Count-max);
        return result;
    }
    internal readonly struct Tally
    {
        internal readonly int Total,Done,Pending,Unknown,Unvisited;
        internal Tally(int total,int done,int pending,int unknown):this(total,done,pending,unknown,0){}
        internal Tally(int total,int done,int pending,int unknown,int unvisited){Total=total;Done=done;Pending=pending;Unknown=unknown;Unvisited=unvisited;}
    }
    internal static Tally Count(IEnumerable<Row> rows,int kind)
    {
        int total=0,done=0,pending=0,unknown=0,unvisited=0;
        if(rows!=null)foreach(var row in rows)
        {
            if(row==null||row.Kind!=kind)continue;
            total++;
            if(row.Done==true)done++;else if(row.Done==false){pending++;if(row.Unvisited)unvisited++;}else unknown++;
        }
        return new Tally(total,done,pending,unknown,unvisited);
    }
    internal static string Summary(int kind,Tally tally)
    {
        string unknown=tally.Unknown>0?" · состояние неизвестно "+tally.Unknown:"";
        string unvisited=M28TrackingStatePolicy.UnvisitedSuffix(tally.Unvisited);
        return ClampKind(kind) switch
        {
            Pods=>"Капсулы: всего "+tally.Total+" · открыто "+tally.Done+" · не открыто "+tally.Pending+unvisited+unknown,
            Gordos=>"Гордо: всего "+tally.Total+" · лопнуло "+tally.Done+" · живых "+tally.Pending+unvisited+unknown,
            Doors=>"Двери (AccessDoor сейва): всего "+tally.Total+" · открыто "+tally.Done+" · закрыто "+tally.Pending+unknown,
            _=>"Точки телепортации атласа: "+tally.Total
        };
    }
}

// 4. Corral autofeed: the game's own chomp path, bounded work per second.
internal static class M27AutoFeedPolicy
{
    internal const float TickSeconds=1f,ScanSeconds=5f,SlimeCooldown=20f,DeclineCooldown=6f,MaxDistance=300f;
    internal const int PortionsPerTick=4,MaxExamined=160,MaxCorrals=64,MaxCooldowns=2048;
    internal static bool Hungry(float hunger,float cutoff)=>float.IsFinite(hunger)&&float.IsFinite(cutoff)&&hunger>=cutoff;
    internal static bool Ready(float now,float until)=>!float.IsFinite(until)||now>=until;
    internal static bool Within(float sqrDistance)=>float.IsFinite(sqrDistance)&&sqrDistance>=0&&sqrDistance<=MaxDistance*MaxDistance;
    internal readonly struct Meal
    {
        internal readonly bool Favorite,Transforms,Spawnable;
        internal Meal(bool favorite,bool transforms,bool spawnable){Favorite=favorite;Transforms=transforms;Spawnable=spawnable;}
    }
    // A favourite spawnable food first, otherwise the first ordinary spawnable food.
    // Diet entries that transform the slime (largo, tarr) are never chosen.
    internal static int ChooseMeal(IReadOnlyList<Meal> meals)
    {
        if(meals==null)return -1;
        int first=-1;
        for(int i=0;i<meals.Count;i++)
        {
            var meal=meals[i];
            if(!meal.Spawnable||meal.Transforms)continue;
            if(meal.Favorite)return i;
            if(first<0)first=i;
        }
        return first;
    }
    internal static string Status(bool enabled,int corrals,int slimes,int hungry,int fed,int total)=>
        !enabled?"Автокормление выключено":
        corrals==0?"Автокормление включено · загонов в прогруженной зоне нет":
        "Загонов "+corrals+" · слаймов "+slimes+" · голодных "+hungry+" · за такт "+fed+" · всего порций "+total;
}

// 5a. Round minimap with the real map page. World (x,z) -> texture UV of the
//     page background (v up) is an affine map, calibrated from the native map.
internal static class M27MiniMapPolicy
{
    internal const int MinComposite=96,MaxComposite=200,MaxCapture=2048;
    internal const double MaxResidual=.004,ConsistentTolerance=.002;
    internal const float MinMoveTexels=.6f,ComposeInterval=1f/15f,Opacity=.94f;
    internal readonly struct Affine
    {
        internal readonly double A,B,C,D,E,F;
        internal Affine(double a,double b,double c,double d,double e,double f){A=a;B=b;C=c;D=d;E=e;F=f;}
        internal double U(double x,double z)=>A*x+B*z+C;
        internal double V(double x,double z)=>D*x+E*z+F;
        internal double Determinant=>A*E-B*D;
        internal bool Valid=>Finite(A)&&Finite(B)&&Finite(C)&&Finite(D)&&Finite(E)&&Finite(F)&&Math.Abs(Determinant)>1e-14;
    }
    internal sealed class Calibration
    {
        internal string Page="",Texture="";
        internal int Width,Height;
        internal Affine Map;
    }
    private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    // Cramer's rule for [x z 1]·[a b c]ᵀ = u (and v) through three samples.
    internal static bool TrySolve(double x0,double z0,double u0,double v0,double x1,double z1,double u1,double v1,
        double x2,double z2,double u2,double v2,out Affine affine)
    {
        affine=default;
        double det=x0*(z1-z2)-z0*(x1-x2)+(x1*z2-x2*z1);
        if(!Finite(det)||Math.Abs(det)<1e-9)return false;
        double a=(u0*(z1-z2)-z0*(u1-u2)+(u1*z2-u2*z1))/det;
        double b=(x0*(u1-u2)-u0*(x1-x2)+(x1*u2-x2*u1))/det;
        double c=(x0*(z1*u2-z2*u1)-z0*(x1*u2-x2*u1)+u0*(x1*z2-x2*z1))/det;
        double d=(v0*(z1-z2)-z0*(v1-v2)+(v1*z2-v2*z1))/det;
        double e=(x0*(v1-v2)-v0*(x1-x2)+(x1*v2-x2*v1))/det;
        double f=(x0*(z1*v2-z2*v1)-z0*(x1*v2-x2*v1)+v0*(x1*z2-x2*z1))/det;
        affine=new Affine(a,b,c,d,e,f);
        return affine.Valid;
    }
    internal static double Residual(Affine affine,double x,double z,double u,double v)=>
        Math.Max(Math.Abs(affine.U(x,z)-u),Math.Abs(affine.V(x,z)-v));
    internal static bool Consistent(Affine a,Affine b,double centerX,double centerZ,double span,double tolerance=ConsistentTolerance)
    {
        if(!a.Valid||!b.Valid||!Finite(centerX)||!Finite(centerZ)||!Finite(span))return false;
        for(int i=0;i<5;i++)
        {
            double x=centerX+(i==1?span:i==2?-span:0),z=centerZ+(i==3?span:i==4?-span:0);
            if(Math.Abs(a.U(x,z)-b.U(x,z))>tolerance||Math.Abs(a.V(x,z)-b.V(x,z))>tolerance)return false;
        }
        return true;
    }
    internal static double TexelsPerMetre(Affine affine,int width,int height)=>
        affine.Valid&&width>0&&height>0?Math.Sqrt(Math.Abs(affine.Determinant)*width*height):0;
    // Unity Image (Simple): preserveAspect fits the sprite inside the rect and
    // offsets it by the RectTransform pivot. Output is normalised to the rect.
    internal static void FitRect(double rectWidth,double rectHeight,double spriteWidth,double spriteHeight,bool preserveAspect,
        double pivotX,double pivotY,out double x,out double y,out double width,out double height)
    {
        x=y=0;width=height=1;
        if(!preserveAspect||rectWidth<=0||rectHeight<=0||spriteWidth<=0||spriteHeight<=0)return;
        double spriteRatio=spriteWidth/spriteHeight,rectRatio=rectWidth/rectHeight;
        if(spriteRatio>rectRatio){height=rectRatio/spriteRatio;y=(1-height)*pivotY;}
        else{width=spriteRatio/rectRatio;x=(1-width)*pivotX;}
    }
    internal static void CaptureSize(int width,int height,int max,out int captureWidth,out int captureHeight)
    {
        captureWidth=captureHeight=0;
        if(width<=0||height<=0||max<=0)return;
        double scale=Math.Min(1.0,(double)max/Math.Max(width,height));
        captureWidth=Math.Max(1,(int)Math.Round(width*scale));captureHeight=Math.Max(1,(int)Math.Round(height*scale));
    }
    private static string Clean(string value)=>(value??"").Replace('|','/').Replace('\n',' ').Replace('\r',' ');
    private static string R(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
    internal static string Serialize(Calibration calibration)=>string.Join("|","v1",Clean(calibration.Page),Clean(calibration.Texture),
        calibration.Width.ToString(CultureInfo.InvariantCulture),calibration.Height.ToString(CultureInfo.InvariantCulture),
        R(calibration.Map.A),R(calibration.Map.B),R(calibration.Map.C),R(calibration.Map.D),R(calibration.Map.E),R(calibration.Map.F));
    internal static bool TryParse(string line,out Calibration calibration)
    {
        calibration=null;
        var parts=(line??"").Split('|');
        if(parts.Length!=11||parts[0]!="v1"||parts[1].Length==0)return false;
        if(!int.TryParse(parts[3],NumberStyles.Integer,CultureInfo.InvariantCulture,out int width)||
           !int.TryParse(parts[4],NumberStyles.Integer,CultureInfo.InvariantCulture,out int height)||width<=0||height<=0)return false;
        var values=new double[6];
        for(int i=0;i<6;i++)
            if(!double.TryParse(parts[5+i],NumberStyles.Float,CultureInfo.InvariantCulture,out values[i]))return false;
        var map=new Affine(values[0],values[1],values[2],values[3],values[4],values[5]);
        if(!map.Valid)return false;
        calibration=new Calibration{Page=parts[1],Texture=parts[2],Width=width,Height=height,Map=map};
        return true;
    }
    // Rainbow Island has no single page background: the atlas draws one Image per
    // zone. Every art layer is calibrated on its own, keyed page/NN/layer (NN = draw
    // order). A key without that suffix is a whole-page (legacy) calibration.
    internal static string LayerKey(string page,int order,string layer)=>
        (page??"")+"/"+Math.Clamp(order,0,99).ToString("00",CultureInfo.InvariantCulture)+"/"+(layer??"").Replace('/','_').Replace('|','_');
    internal static bool TryLayerKey(string key,out string page,out int order,out string layer)
    {
        page=key??"";order=0;layer="";
        if(page.Length==0)return false;
        int last=page.LastIndexOf('/');
        int previous=last>0?page.LastIndexOf('/',last-1):-1;
        if(previous<1||last-previous!=3||!char.IsDigit(page[previous+1])||!char.IsDigit(page[previous+2]))return true;
        order=(page[previous+1]-'0')*10+(page[previous+2]-'0');
        layer=page.Substring(last+1);
        page=page.Substring(0,previous);
        return true;
    }
    // World (x,z) box covered by a layer texture: the inverse affine of the UV unit square.
    internal static bool WorldBounds(Affine affine,out double minX,out double minZ,out double maxX,out double maxZ)
    {
        minX=minZ=double.MaxValue;maxX=maxZ=double.MinValue;
        if(!affine.Valid)return false;
        double det=affine.Determinant;
        for(int corner=0;corner<4;corner++)
        {
            double u=(corner&1)-affine.C,v=(corner>>1)-affine.F;
            double x=(affine.E*u-affine.B*v)/det,z=(affine.A*v-affine.D*u)/det;
            minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minZ=Math.Min(minZ,z);maxZ=Math.Max(maxZ,z);
        }
        return Finite(minX)&&Finite(minZ)&&Finite(maxX)&&Finite(maxZ);
    }
    internal static bool Overlaps(double minX,double minZ,double maxX,double maxZ,double centerX,double centerZ,double half)=>
        maxX>=centerX-half&&minX<=centerX+half&&maxZ>=centerZ-half&&minZ<=centerZ+half;
    // Heading-up minimap: world offset (dx,dz) -> screen offset (y down); mirrors
    // MiniMapUi.Project. A north-up image drawn under ScreenDegrees(yaw) agrees.
    internal static void Project(double dx,double dz,double yaw,double scale,out double x,out double y)
    {
        double cos=Math.Cos(yaw),sin=Math.Sin(yaw);
        x=(dx*cos-dz*sin)*scale;y=-(dx*sin+dz*cos)*scale;
    }
    internal static float ScreenDegrees(double yaw)=>(float)(-yaw*180.0/Math.PI);
    // GUI z-rotation (y down) by degrees: (x,y) -> (x cos - y sin, x sin + y cos).
    internal static void RotateScreen(double x,double y,double degrees,out double rx,out double ry)
    {
        double angle=degrees*Math.PI/180.0,cos=Math.Cos(angle),sin=Math.Sin(angle);
        rx=x*cos-y*sin;ry=x*sin+y*cos;
    }
    // Translation t of TRS(t, Rz(degrees)) so the rotation pivots on (cx,cy).
    internal static void PivotTranslation(double cx,double cy,double degrees,out double tx,out double ty)
    {
        RotateScreen(cx,cy,degrees,out double rx,out double ry);
        tx=cx-rx;ty=cy-ry;
    }
    internal static int CompositeSize(float terrainRadius)=>
        float.IsFinite(terrainRadius)?Math.Clamp((int)Math.Round(2*terrainRadius),MinComposite,MaxComposite):MinComposite;
    // Metres per composite texel so terrain and markers share one scale:
    // markers use (markerRadius px) per range metres; the disc spans 2·terrainRadius px.
    internal static double MetresPerTexel(double range,double markerRadius,double terrainRadius,int size)=>
        range>0&&markerRadius>0&&terrainRadius>0&&size>0?range/markerRadius*(2*terrainRadius/size):0;
    internal static bool NeedsCompose(bool composed,double lastX,double lastZ,double x,double z,double metresPerTexel,float now,float lastTime)
    {
        if(!composed)return true;
        if(now-lastTime<ComposeInterval)return false;
        double dx=x-lastX,dz=z-lastZ,limit=MinMoveTexels*metresPerTexel;
        return dx*dx+dz*dz>=limit*limit;
    }
    internal static uint Pack(int r,int g,int b,int a)=>
        (uint)Math.Clamp(r,0,255)|(uint)Math.Clamp(g,0,255)<<8|(uint)Math.Clamp(b,0,255)<<16|(uint)Math.Clamp(a,0,255)<<24;
    // North-up circular crop centred on (centerX,centerZ). Buffers are RGBA32
    // packed r|g<<8|b<<16|a<<24, row 0 = bottom (Unity texture order). The page
    // art is composited over an opaque background; the disc edge is 1 px antialiased.
    internal sealed class Source
    {
        internal readonly uint[] Pixels;
        internal readonly int Width,Height;
        internal readonly Affine Map;
        internal Source(uint[] pixels,int width,int height,Affine map){Pixels=pixels;Width=width;Height=height;Map=map;}
    }
    internal static void Compose(uint[] source,int sourceWidth,int sourceHeight,Affine affine,double centerX,double centerZ,
        double metresPerTexel,uint[] target,int size,uint background,float opacity=Opacity)=>
        ComposeLayers(new[]{new Source(source,sourceWidth,sourceHeight,affine)},centerX,centerZ,metresPerTexel,target,size,background,opacity);
    // Art layers in atlas draw order, each alpha-blended over the result so far.
    internal static void ComposeLayers(IReadOnlyList<Source> layers,double centerX,double centerZ,double metresPerTexel,
        uint[] target,int size,uint background,float opacity=Opacity)
    {
        if(layers==null||target==null||size<=0||target.Length<size*size||!(metresPerTexel>0)||!Finite(centerX)||!Finite(centerZ))
            throw new ArgumentException("Недопустимые буферы мини-карты.");
        int count=layers.Count;
        for(int k=0;k<count;k++)
        {
            var layer=layers[k];
            if(layer==null||layer.Pixels==null||layer.Width<=0||layer.Height<=0||layer.Pixels.Length<layer.Width*layer.Height||!layer.Map.Valid)
                throw new ArgumentException("Недопустимые буферы мини-карты.");
        }
        double alphaScale=Math.Clamp(opacity,0f,1f)*255.0,half=size*.5;
        double startX=centerX+(.5-half)*metresPerTexel,startZ=centerZ+(.5-half)*metresPerTexel;
        var stepUi=new double[count];var stepUj=new double[count];var stepVi=new double[count];var stepVj=new double[count];
        var u0=new double[count];var v0=new double[count];
        for(int k=0;k<count;k++)
        {
            var layer=layers[k];var affine=layer.Map;
            stepUi[k]=affine.A*metresPerTexel*layer.Width;stepUj[k]=affine.B*metresPerTexel*layer.Width;
            stepVi[k]=affine.D*metresPerTexel*layer.Height;stepVj[k]=affine.E*metresPerTexel*layer.Height;
            u0[k]=affine.U(startX,startZ)*layer.Width-.5;v0[k]=affine.V(startX,startZ)*layer.Height-.5;
        }
        int br=(int)(background&0xFF),bg=(int)(background>>8&0xFF),bb=(int)(background>>16&0xFF);
        for(int j=0;j<size;j++)
        {
            double dy=j+.5-half;
            for(int i=0;i<size;i++)
            {
                double dx=i+.5-half,mask=Math.Clamp(half-Math.Sqrt(dx*dx+dy*dy),0.0,1.0);
                int index=j*size+i;
                if(mask<=0){target[index]=0;continue;}
                double r=br,g=bg,b=bb;
                for(int k=0;k<count;k++)
                {
                    var layer=layers[k];int w=layer.Width,h=layer.Height;
                    double px=u0[k]+j*stepUj[k]+i*stepUi[k],py=v0[k]+j*stepVj[k]+i*stepVi[k];
                    if(!(px>=-.5&&py>=-.5&&px<=w-.5&&py<=h-.5))continue;
                    var source=layer.Pixels;
                    int x0=(int)Math.Floor(px),y0=(int)Math.Floor(py);
                    double fx=px-x0,fy=py-y0;
                    int x1=Math.Min(x0+1,w-1),y1=Math.Min(y0+1,h-1);
                    x0=Math.Max(x0,0);y0=Math.Max(y0,0);
                    uint c00=source[y0*w+x0],c10=source[y0*w+x1],c01=source[y1*w+x0],c11=source[y1*w+x1];
                    double sa=Lerp2(c00,c10,c01,c11,24,fx,fy)/255.0;
                    if(sa<=0)continue;
                    double sr=Lerp2(c00,c10,c01,c11,0,fx,fy),sg=Lerp2(c00,c10,c01,c11,8,fx,fy),sb=Lerp2(c00,c10,c01,c11,16,fx,fy);
                    r=sr*sa+r*(1-sa);g=sg*sa+g*(1-sa);b=sb*sa+b*(1-sa);
                }
                target[index]=Pack((int)Math.Round(r),(int)Math.Round(g),(int)Math.Round(b),(int)Math.Round(alphaScale*mask));
            }
        }
    }
    private static double Lerp2(uint c00,uint c10,uint c01,uint c11,int shift,double fx,double fy)
    {
        double a=(c00>>shift&0xFF)*(1-fx)+(c10>>shift&0xFF)*fx,b=(c01>>shift&0xFF)*(1-fx)+(c11>>shift&0xFF)*fx;
        return a*(1-fy)+b*fy;
    }
}

// 5b. Native compass markers: a few nearby targets, never a flood.
internal static class M27CompassPolicy
{
    internal const int MaxMarkers=6;
    internal const float Range=250f,TickSeconds=1f;
    internal readonly struct Candidate
    {
        internal readonly string Key;
        internal readonly int Priority;
        internal readonly float Distance;
        internal Candidate(string key,int priority,float distance){Key=key;Priority=priority;Distance=distance;}
    }
    private static float Known(float distance)=>float.IsFinite(distance)&&distance>=0?distance:float.MaxValue;
    // Priority 0 (the selected atlas target) ignores the range; others need a
    // known distance within it. Order: priority, distance, key; keys are unique.
    internal static List<Candidate> Pick(IEnumerable<Candidate> candidates,int max=MaxMarkers,float range=Range)
    {
        var result=new List<Candidate>();
        if(candidates==null||max<=0)return result;
        var all=new List<Candidate>();
        foreach(var candidate in candidates)
        {
            if(string.IsNullOrEmpty(candidate.Key))continue;
            if(candidate.Priority>0&&Known(candidate.Distance)>range)continue;
            all.Add(candidate);
        }
        all.Sort((a,b)=>
        {
            int p=a.Priority.CompareTo(b.Priority);if(p!=0)return p;
            int d=Known(a.Distance).CompareTo(Known(b.Distance));if(d!=0)return d;
            return string.CompareOrdinal(a.Key,b.Key);
        });
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var candidate in all)
        {
            if(result.Count>=max)break;
            if(seen.Add(candidate.Key))result.Add(candidate);
        }
        return result;
    }
    internal static bool SameSet(IReadOnlyCollection<string> a,IReadOnlyCollection<string> b)
    {
        if(a==null||b==null)return a==b;
        if(a.Count!=b.Count)return false;
        var set=new HashSet<string>(a,StringComparer.Ordinal);
        foreach(string key in b)if(!set.Contains(key))return false;
        return true;
    }
}
