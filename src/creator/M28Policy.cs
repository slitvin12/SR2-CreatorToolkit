using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CreatorToolkit;

// M28 rules. Deliberately free of Unity and Il2Cpp types: the offline checker
// links this file directly and unit-tests every decision (tests\M28Regression.cs).

// 1. The native map button that opens the drone panel (MapUI._toggleDroneMenuInput)
//    vs the creator atlas panel at the top right. Boxes are top-left screen pixels.
//    A button left under the atlas header also received the click meant for
//    «свернуть атлас» and opened the drone panel.
internal static class M28DroneButtonPolicy
{
    internal const float Gap=8f,Margin=8f,MaxDropFraction=.25f;
    internal const string Auto="auto",Below="below",Left="left",Off="off";
    internal enum Move{None,Below,Left}
    internal readonly struct Placement
    {
        internal readonly float Dx,Dy;
        internal readonly Move Side;
        internal readonly bool Clear;
        internal Placement(float dx,float dy,Move side,bool clear){Dx=dx;Dy=dy;Side=side;Clear=clear;}
        internal bool Moved=>Side!=Move.None;
    }
    internal static bool KnownMode(string text)
    {
        string mode=(text??"").Trim().ToLowerInvariant();
        return mode==Auto||mode==Below||mode==Left||mode==Off;
    }
    internal static string Mode(string text)
    {
        string mode=(text??"").Trim().ToLowerInvariant();
        return mode==Below||mode==Left||mode==Off?mode:Auto;
    }
    internal static MapShieldPolicy.Box Inflate(MapShieldPolicy.Box box,float by)=>new(box.X-by,box.Y-by,box.Width+2*by,box.Height+2*by);
    internal static MapShieldPolicy.Box Offset(MapShieldPolicy.Box box,float dx,float dy)=>new(box.X+dx,box.Y+dy,box.Width,box.Height);
    internal static MapShieldPolicy.Box Union(MapShieldPolicy.Box a,MapShieldPolicy.Box b)
    {
        if(!a.Valid)return b;
        if(!b.Valid)return a;
        float x=Math.Min(a.X,b.X),y=Math.Min(a.Y,b.Y);
        return new(x,y,Math.Max(a.Right,b.Right)-x,Math.Max(a.Bottom,b.Bottom)-y);
    }
    // button: where the game put the button (any offset of ours removed);
    // atlas: the creator panel in its current state (collapsed header or expanded list).
    // auto: just below the panel when that is a short drop (collapsed header), else to
    // its left (expanded list), else below, else as far down as the screen allows.
    internal static Placement Place(MapShieldPolicy.Box button,MapShieldPolicy.Box atlas,float screenWidth,float screenHeight,string mode)
    {
        mode=Mode(mode);
        if(mode==Off||!button.Valid||!atlas.Valid||!float.IsFinite(screenWidth)||!float.IsFinite(screenHeight)||screenWidth<=0||screenHeight<=0)
            return new(0,0,Move.None,true);
        float scale=M27DronePanelPolicy.Scale(screenHeight),gap=Gap*scale,margin=Margin*scale;
        if(!M27DronePanelPolicy.Overlaps(button,Inflate(atlas,gap*.5f)))return new(0,0,Move.None,true);
        float dy=atlas.Bottom+gap-button.Y,dx=atlas.X-gap-button.Right;
        bool belowFits=button.Bottom+dy<=screenHeight-margin,leftFits=button.X+dx>=margin;
        bool shallow=dy<=MaxDropFraction*screenHeight;
        if(mode==Left&&leftFits)return new(dx,0,Move.Left,true);
        if(mode==Auto)
        {
            if(belowFits&&shallow)return new(0,dy,Move.Below,true);
            if(leftFits)return new(dx,0,Move.Left,true);
        }
        if(belowFits)return new(0,dy,Move.Below,true);
        return new(0,Math.Max(0f,Math.Min(dy,screenHeight-margin-button.Bottom)),Move.Below,false);
    }
    // What the drone PANEL has to keep clear of: the atlas plus the moved button.
    internal static MapShieldPolicy.Box Obstacle(MapShieldPolicy.Box atlas,bool known,MapShieldPolicy.Box button,float screenWidth,float screenHeight,string mode)
    {
        if(!known||!button.Valid)return atlas;
        var placement=Place(button,atlas,screenWidth,screenHeight,mode);
        return placement.Moved?Union(atlas,Offset(button,placement.Dx,placement.Dy)):atlas;
    }
    internal static string Describe(bool found,Placement placement)=>
        !found?"Кнопка дронов на карте не найдена — положение не меняется":
        !placement.Moved?"Кнопка дронов не пересекает атлас":
        (placement.Side==Move.Below?"Кнопка дронов опущена под атлас на "+Px(placement.Dy)+" px":
            "Кнопка дронов сдвинута левее атласа на "+Px(-placement.Dx)+" px")+
        (placement.Clear?"":" · экран слишком мал: перекрытие уменьшено, но не устранено");
    internal static string ModeTitle(string mode)=>Mode(mode) switch{Below=>"под атласом",Left=>"левее атласа",Off=>"не двигать",_=>"авто"};
    private static string Px(float value)=>Math.Round(value).ToString(CultureInfo.InvariantCulture);
}

// 2. Compass bar icons. ManualRadarEntry markers are drawn with the compass' default
//    marker prefab (a yellow star) whatever sprite they were registered with, so the
//    real icon is put on our own marker instances (CompassMarkerTools).
internal static class M28CompassIconPolicy
{
    internal const string Overlay="overlay",Replace="replace",Off="off";
    // Own markers: the compass goal ignores the range like the atlas selection; the
    // other visible own markers rank right after it.
    internal const int GoalPriority=0,OwnPriority=1;
    internal const float IconScale=1.15f,MinIcon=18f,MaxIcon=72f,DefaultIcon=32f;
    internal static bool KnownStyle(string text)
    {
        string style=(text??"").Trim().ToLowerInvariant();
        return style==Overlay||style==Replace||style==Off;
    }
    internal static string Style(string text)
    {
        string style=(text??"").Trim().ToLowerInvariant();
        return style==Replace||style==Off?style:Overlay;
    }
    internal static string StyleTitle(string style)=>Style(style) switch{Replace=>"замена спрайта",Off=>"звёзды игры",_=>"значок поверх"};
    internal static string CustomKey(string id)=>"cw|"+(id??"");
    // Fandom atlas cell -> pixel rect in a bottom-up texture whose row 0 is the image top.
    internal static bool CellRect(int cell,int columns,int rows,int size,out float x,out float y,out float width,out float height)
    {
        x=y=width=height=0;
        if(cell<0||columns<=0||rows<=0||size<=0||cell>=columns*rows)return false;
        x=(cell%columns)*size;y=(rows-1-cell/columns)*size;width=height=size;
        return true;
    }
    // The native image carrying the marker art: a sprite named like a star first,
    // otherwise the largest one. -1 when there is none.
    internal static int PrimaryImage(IReadOnlyList<string> sprites,IReadOnlyList<float> areas)
    {
        if(sprites==null||areas==null)return -1;
        int n=Math.Min(sprites.Count,areas.Count),best=-1;
        for(int i=0;i<n;i++)
            if((sprites[i]??"").IndexOf("star",StringComparison.OrdinalIgnoreCase)>=0)return i;
        float bestArea=0;
        for(int i=0;i<n;i++)
            if(float.IsFinite(areas[i])&&areas[i]>bestArea){bestArea=areas[i];best=i;}
        return best;
    }
    internal static float IconSize(float width,float height)
    {
        float size=Math.Max(width,height);
        if(!float.IsFinite(size)||size<=0)size=DefaultIcon;
        return Math.Clamp(size*IconScale,MinIcon,MaxIcon);
    }
}

// 3. «Трекинг мира»: a pod or gordo the save holds no model for. The game creates
//    those models the first time their area streams in, so with a complete census of
//    the loaded save an absent ID means "never loaded, hence untouched", not "unknown".
internal static class M28TrackingStatePolicy
{
    internal enum Missing{Known,NeverLoaded,Conflict,Unknown}
    internal static Missing Classify(bool hasState,bool censusComplete,bool inCensus,bool ambiguous)=>
        hasState?Missing.Known:ambiguous?Missing.Conflict:censusComplete&&!inCensus?Missing.NeverLoaded:Missing.Unknown;
    internal const string PodNeverLoaded="Не открыта · участок ещё не загружался";
    internal const string GordoNeverLoaded="Не лопнул · участок ещё не загружался";
    internal static string UnvisitedSuffix(int unvisited)=>
        unvisited>0?" (из них "+unvisited.ToString(CultureInfo.InvariantCulture)+" в ещё не загружавшихся участках)":"";
}

// 4. «Открыть все стили вакпака»: VacPalette ownership kept by the PaletteDirector
//    (PaletteInventoryModel of the save).
internal static class M28PalettePolicy
{
    internal static bool Unlockable(bool vacPalette,bool owned)=>vacPalette&&!owned;
    internal static string Report(int total,int opened,int already,int entries,int failed)=>
        "Стили вакпака: открыто "+opened+" · уже было "+already+" · всего "+total+
        (entries>0?" · записей Слаймопедии "+entries:"")+
        (failed>0?" · ошибок "+failed+" (подробности в логе)":"")+". Сохраняется в сейве.";
}

// 5. «Разблокировка» cards: seven cards in two columns inside the 1100x594 body.
internal static class M28UnlockLayout
{
    internal const float Top=56f,RowStep=134f,CardHeight=126f,Gap=16f,ButtonWidth=200f,ButtonHeight=44f;
    internal static float Column(float width)=>(float)Math.Floor((width-Gap)/2f);
    internal static float X(int index,float width)=>(index%2)*(Column(width)+Gap);
    internal static float Y(int index)=>Top+(index/2)*RowStep;
    internal static bool Fits(int cards,float width,float height)=>
        cards>0&&Column(width)>=ButtonWidth+200f&&Y(cards-1)+CardHeight<=height;
}

// 6. Minimap player marker: the native map's PlayerMapMarker art. Parts are kept in
//    GUI space (x right, y down) around the marker pivot, rotating parts already
//    turned so the facing points up (the minimap is heading-up).
internal static class M28PlayerMarkerPolicy
{
    internal const float TargetSize=44f,MinSize=24f,MaxSize=72f;
    internal const int MaxParts=12,MaxTexture=4096;
    // Positive degrees turn clockwise on screen (GUI y grows downwards).
    internal static void Rotate(float x,float y,float degrees,out float rx,out float ry)
    {
        double r=degrees*Math.PI/180.0,c=Math.Cos(r),s=Math.Sin(r);
        rx=(float)(x*c-y*s);ry=(float)(x*s+y*c);
    }
    // Clockwise screen angle of a GUI vector, measured from straight up.
    internal static float Heading(float x,float y)=>
        !float.IsFinite(x)||!float.IsFinite(y)||x*x+y*y<1e-10f?0f:(float)(Math.Atan2(x,-y)*180.0/Math.PI);
    internal static float Normalize(float degrees)
    {
        if(!float.IsFinite(degrees))return 0f;
        degrees%=360f;
        if(degrees>180f)degrees-=360f;
        if(degrees<=-180f)degrees+=360f;
        return degrees;
    }
    internal static float FitScale(float width,float height,float target)
    {
        float size=Math.Max(width,height);
        if(!float.IsFinite(size)||size<=0||!float.IsFinite(target))return 1f;
        return Math.Clamp(target,MinSize,MaxSize)/size;
    }
    // Image.preserveAspect: the sprite is fitted, centred, inside its rect.
    internal static void FitAspect(float width,float height,float spriteWidth,float spriteHeight,out float w,out float h)
    {
        w=width;h=height;
        if(!(spriteWidth>0)||!(spriteHeight>0)||!(width>0)||!(height>0))return;
        float aspect=spriteWidth/spriteHeight;
        if(width/height>aspect)w=height*aspect;else h=width/aspect;
    }
    // UnityEngine.UI.Image Radial360 fill. Origin: Bottom=0, Right=1, Top=2, Left=3;
    // u,v in [0,1] with v up (texture space).
    internal static bool RadialVisible(float u,float v,float amount,int origin,bool clockwise)
    {
        if(amount>=1f)return true;
        if(!(amount>0f))return false;
        double dx=u-.5,dy=v-.5;
        if(dx*dx+dy*dy<1e-12)return true;
        double start=origin switch{1=>0.0,2=>90.0,3=>180.0,_=>270.0};
        double angle=Math.Atan2(dy,dx)*180.0/Math.PI;
        double sweep=clockwise?start-angle:angle-start;
        sweep%=360.0;
        if(sweep<0)sweep+=360.0;
        return sweep<=amount*360.0+1e-6;
    }
    // Horizontal fill (method 0): origin Left=0 / Right=1; vertical (method 1): Bottom=0 / Top=1.
    internal static bool LinearVisible(float u,float v,float amount,int method,int origin)
    {
        if(amount>=1f)return true;
        if(!(amount>0f))return false;
        float t=method==0?u:v;
        return origin==1?t>=1f-amount:t<=amount;
    }
}

// 7. CreatorToolkit test relay (M28TestIpc): gated by the opt-in file ct-test.enabled
//    in UserData\CreatorToolkit; reference tools\ct-test\TESTING.md.
internal static class M28TestPolicy
{
    internal const string OptInFile="ct-test.enabled";
    internal const int MaxDumpLines=6000,MaxDepth=16,DefaultDepth=10,MaxFind=200,MaxHits=40;
    internal static string SafeTag(string text)
    {
        var sb=new StringBuilder();
        foreach(char c in text??"")
        {
            bool ok=c>='a'&&c<='z'||c>='A'&&c<='Z'||c>='0'&&c<='9'||c=='-'||c=='_';
            if(ok)sb.Append(c);
            else if(sb.Length>0&&sb[sb.Length-1]!='_')sb.Append('_');
            if(sb.Length>=40)break;
        }
        string tag=sb.ToString().Trim('_');
        return tag.Length==0?"x":tag;
    }
    internal static int Depth(string text)=>
        int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int depth)?Math.Clamp(depth,1,MaxDepth):DefaultDepth;
    // A top-left screen pixel given as two invariant numbers inside the screen.
    internal static bool TryScreenPoint(string x,string y,float width,float height,out float px,out float py)
    {
        px=py=0;
        if(!float.TryParse(x,NumberStyles.Float,CultureInfo.InvariantCulture,out px)||
           !float.TryParse(y,NumberStyles.Float,CultureInfo.InvariantCulture,out py))return false;
        return float.IsFinite(px)&&float.IsFinite(py)&&px>=0&&py>=0&&px<width&&py<height;
    }
    internal static string Box(float x,float y,float width,float height)=>
        R(x)+","+R(y)+" "+R(width)+"x"+R(height);
    private static string R(float value)=>float.IsFinite(value)?Math.Round(value).ToString(CultureInfo.InvariantCulture):"nan";
    internal static string OneLine(string text,int max=240)
    {
        string line=(text??"").Replace("\r","").Replace('\n',' ').Trim();
        return line.Length>max?line.Substring(0,max)+"…":line;
    }
    internal static readonly string[] Help=
    {
        "CreatorToolkit test relay (opt-in file UserData\\CreatorToolkit\\"+OptInFile+"). Result: status.txt line CtTest.",
        "ct-help                         this list -> ct-help.txt",
        "ct-state                        one-line state: ready, save, menu, map, atlas panel, M28 tools",
        "ct-ui-dump <target> [depth]     UI tree -> ct-ui-<target>.txt; target map|drone|marker|compass|canvases|name:<path part>",
        "ct-ui-hit <x> <y>               EventSystem raycast at top-left screen pixel -> ct-ui-hit.txt",
        "ct-ui-find <text>               active RectTransforms whose path contains text -> ct-ui-find.txt",
        "ct-continue-test                main menu: continue ONLY when the continue save is the test save",
        "ct-atlas-panel expanded|collapsed|toggle   creator atlas panel state on the native map",
        "ct-atlas-search <text>|clear    M29: atlas search as the atlas page field sets it (%XX UTF-8 accepted); clear = the × buttons",
        "ct-atlas-state                  M29: search, hits and own markers on the page, card slot, covered marker",
        "ct-atlas-focus pod|own|drone|gordo|teleport|<layer> [n]   M29: select and centre the n-th point (test save only)",
        "ct-ghost-state                  M29: zone of each phantom route (membership/map/authored/unique/live), atlas page, compass group -> ct-ghost.txt",
        "ct-palette-sources              M29: vacpack styles and every shop / reward-kiosk entry that grants one, with its conditions -> ct-palettes-src.txt",
        "ct-layer-audit                  M29: per map layer: points, duplicates, zone, in range, navigator list, compass -> ct-layers.txt",
        "ct-minimap off|radar|terrain    minimap mode, same states as the atlas page button (saved to navigation.cfg)",
        "ct-nav state|set <key> <value>|reset|zoom in|out|reload   M29: minimap/compass settings (navigation.cfg keys: minimap, minimap.size/range/corner/transparency/orientation, compass, compass.range/markers/ghosts)",
        "ct-menu <tab> [section]         open the creator menu on a tab (4 = unlocks: section 0 cards, 1 tracking)",
        "m28-drone state|find|mode <auto|below|left|off>|climb <n|auto>|pick <i>   native drone-menu button",
        "m28-compass-dump                compass bar instances vs our markers -> m28-compass.txt",
        "m28-compass-style overlay|replace|off      how our compass markers get their icon",
        "m28-palettes                    read-only list of vacpack styles -> m28-palettes.txt",
        "m28-unlock palettes|styles|blueprints      opens the confirmation (test save only)",
        "m28-confirm palettes|styles|blueprints     accepts it when the pending title matches (test save only)",
        "m28-player-marker state|capture|dump|forget  minimap copy of the native player marker",
        "m28-custom-add <color 0-5> [name]          own marker at the player (test save only)",
        "m28-custom-goal <id|last>       compass goal; m28-custom-del <id|last> removes it (test save only)",
        "m28-tracking                    refreshes tracking, pod/gordo state tallies -> m28-tracking.txt"
    };
}
