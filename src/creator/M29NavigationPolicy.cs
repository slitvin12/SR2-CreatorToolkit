using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CreatorToolkit;

// M29 task 4: minimap and compass settings kept in the mod's UserData folder (navigation.cfg), the
// minimap box per screen corner and size, and the smooth zoom. Task 5: the compass and the
// minimap give every enabled layer a share (a busy layer can no longer crowd out the others) and
// potential sources get the scene group they were observed in. Unity-free: linked into the tests.
internal sealed class M29NavigationSettings
{
    internal const string FileName="navigation.cfg";
    internal const int Version=1;
    internal const float MinRange=30f,MaxRange=300f,DefaultRange=90f,ZoomStep=1.25f;
    internal const int MinCompassRange=50,MaxCompassRange=1000,DefaultCompassRange=250,CompassRangeStep=25;
    internal const int MinCompassMarkers=1,MaxCompassMarkers=16,DefaultCompassMarkers=6;
    internal const int MaxTransparency=100,TransparencyStep=5;
    internal static readonly string[] Modes={"off","terrain","radar"};
    internal static readonly string[] Sizes={"small","medium","large"};
    internal static readonly string[] Corners={"top-right","top-left","bottom-right","bottom-left"};
    internal static readonly string[] GhostModes={"normal","high","pinned"};
    private static readonly string[] OnWords={"on","true","yes"},OffWords={"off","false","no"};
    internal static readonly string[] ModeTitles={"Выкл","Рельеф","Радар"};
    internal static readonly string[] SizeTitles={"Малая","Средняя","Большая"};
    internal static readonly string[] CornerTitles={"справа сверху","слева сверху","справа снизу","слева снизу"};
    internal static readonly string[] GhostTitles={"Обычный","Высокий","Всегда"};
    // Defaults = the M27/M28 behaviour: minimap off, medium, 90 m, top right, opaque, heading-up;
    // compass markers on, 250 m, six markers, phantom drones like the other drones.
    internal int Mode;
    internal int Size=1;
    internal float Range=DefaultRange;
    internal int Corner;
    internal int Transparency;
    internal bool NorthUp;
    internal bool Compass=true;
    internal int CompassRange=DefaultCompassRange;
    internal int CompassMarkers=DefaultCompassMarkers;
    internal int Ghosts;

    internal M29NavigationSettings Copy()=>(M29NavigationSettings)MemberwiseClone();
    // Reset: look, zoom and compass limits back to the defaults; the minimap mode and the compass toggle stay.
    internal void ResetLook()
    {
        Size=1;Range=DefaultRange;Corner=0;Transparency=0;NorthUp=false;
        CompassRange=DefaultCompassRange;CompassMarkers=DefaultCompassMarkers;Ghosts=0;
    }
    internal static float ClampRange(float range)=>float.IsFinite(range)?Math.Clamp(range,MinRange,MaxRange):DefaultRange;
    internal static float Whole(float range)=>ClampRange((float)Math.Round(ClampRange(range)));
    // Buttons: one step is x1.25, kept inside 30..300 m and rounded to whole metres.
    internal static float ZoomIn(float range)=>Whole(ClampRange(range)/ZoomStep);
    internal static float ZoomOut(float range)=>Whole(ClampRange(range)*ZoomStep);
    // Slider: logarithmic, 30 m at 0 and 300 m at 1, so every part of the track zooms alike.
    internal static float RangeToSlider(float range)=>(float)(Math.Log(ClampRange(range)/MinRange)/Math.Log(MaxRange/MinRange));
    internal static float SliderToRange(float t)=>Whole((float)(MinRange*Math.Pow(MaxRange/MinRange,Math.Clamp(float.IsFinite(t)?t:0f,0f,1f))));
    internal static int ClampCompassRange(int range)=>Math.Clamp((int)Math.Round(range/(double)CompassRangeStep)*CompassRangeStep,MinCompassRange,MaxCompassRange);
    internal static int ClampMarkers(int count)=>Math.Clamp(count,MinCompassMarkers,MaxCompassMarkers);
    internal static int ClampTransparency(int percent)=>Math.Clamp((int)Math.Round(percent/(double)TransparencyStep)*TransparencyStep,0,MaxTransparency);
    // Multiplier for the minimap background (rim, disc, terrain); markers and labels stay opaque.
    internal float BackgroundAlpha=>1f-ClampTransparency(Transparency)/100f;
    private static int Index(int value,string[] names)=>Math.Clamp(value,0,names.Length-1);
    internal string ModeName=>Modes[Index(Mode,Modes)];
    internal string SizeName=>Sizes[Index(Size,Sizes)];
    internal string CornerName=>Corners[Index(Corner,Corners)];
    internal string GhostName=>GhostModes[Index(Ghosts,GhostModes)];
    internal static string Metres(float value)=>Math.Round(value).ToString("0",CultureInfo.InvariantCulture)+" м";

    internal string Serialize()
    {
        var sb=new StringBuilder(400);
        sb.Append("# Creator Toolkit M29: minimap and compass settings. Unknown keys and bad values are ignored.\n");
        Line(sb,"version",Version.ToString(CultureInfo.InvariantCulture));
        Line(sb,"minimap",ModeName);
        Line(sb,"minimap.size",SizeName);
        Line(sb,"minimap.range",Whole(Range).ToString("0",CultureInfo.InvariantCulture));
        Line(sb,"minimap.corner",CornerName);
        Line(sb,"minimap.transparency",ClampTransparency(Transparency).ToString(CultureInfo.InvariantCulture));
        Line(sb,"minimap.orientation",NorthUp?"north":"heading");
        Line(sb,"compass",Compass?"on":"off");
        Line(sb,"compass.range",ClampCompassRange(CompassRange).ToString(CultureInfo.InvariantCulture));
        Line(sb,"compass.markers",ClampMarkers(CompassMarkers).ToString(CultureInfo.InvariantCulture));
        Line(sb,"compass.ghosts",GhostName);
        return sb.ToString();
    }
    private static void Line(StringBuilder sb,string key,string value)=>sb.Append(key).Append('=').Append(value).Append('\n');
    // One line for the status and the test relay.
    internal string Describe()
    {
        var sb=new StringBuilder(260);
        foreach(string line in Serialize().Split('\n'))
        {
            if(line.Length==0||line[0]=='#'||line.StartsWith("version=",StringComparison.Ordinal))continue;
            if(sb.Length>0)sb.Append(' ');
            sb.Append(line);
        }
        return sb.ToString();
    }

    // One key=value pair; false (and nothing changes) for an unknown key or a value that is not accepted.
    internal bool Set(string key,string value)
    {
        key=(key??"").Trim().ToLowerInvariant();value=(value??"").Trim().ToLowerInvariant();
        switch(key)
        {
            case "version": return int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out _);
            case "minimap": return Pick(value,Modes,ref Mode);
            case "minimap.size": return Pick(value,Sizes,ref Size);
            case "minimap.range":
                if(!float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out float range)||!float.IsFinite(range))return false;
                Range=Whole(range);return true;
            case "minimap.corner": return Pick(value,Corners,ref Corner);
            case "minimap.transparency":
                if(!int.TryParse(value.TrimEnd('%'),NumberStyles.Integer,CultureInfo.InvariantCulture,out int percent))return false;
                Transparency=ClampTransparency(percent);return true;
            case "minimap.orientation":
                if(value=="north"){NorthUp=true;return true;}
                if(value=="heading"){NorthUp=false;return true;}
                return false;
            case "compass":
                if(Array.IndexOf(OnWords,value)>=0){Compass=true;return true;}
                if(Array.IndexOf(OffWords,value)>=0){Compass=false;return true;}
                return false;
            case "compass.range":
                if(!int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int metres))return false;
                CompassRange=ClampCompassRange(metres);return true;
            case "compass.markers":
                if(!int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int count))return false;
                CompassMarkers=ClampMarkers(count);return true;
            case "compass.ghosts": return Pick(value,GhostModes,ref Ghosts);
            default: return false;
        }
    }
    private static bool Pick(string value,string[] names,ref int field)
    {
        int index=Array.IndexOf(names,value);
        if(index<0)return false;
        field=index;return true;
    }
    // Tolerant reader: blank lines and comment lines are skipped; unknown keys and bad values
    // are counted as rejected and leave the default; numbers are clamped to their ranges.
    internal static M29NavigationSettings Parse(string text,out int applied,out int rejected)
    {
        var settings=new M29NavigationSettings();applied=rejected=0;
        foreach(string raw in (text??"").Split('\n'))
        {
            string line=raw.Trim();
            if(line.Length==0||line[0]=='#'||line[0]==';')continue;
            int eq=line.IndexOf('=');
            if(eq<=0){rejected++;continue;}
            if(settings.Set(line.Substring(0,eq),line.Substring(eq+1)))applied++;else rejected++;
        }
        return settings;
    }
}

// Minimap disc: diameter per size and its box per screen corner.
internal static class M29MiniMapLayout
{
    internal const float Margin=24f,InfoTop=72f,InfoHeight=144f,Gap=12f,Label=28f;
    // The native bottom HUD (health and energy at the left, newbucks and time at the right) starts
    // at ~84.5 % of the screen height (1920x1440 capture); bottom corners stay above it.
    internal const float BottomHud=.16f;
    // Medium is the M27 disc (unchanged default).
    internal static float Diameter(int size,float screenHeight)
    {
        float h=float.IsFinite(screenHeight)&&screenHeight>0?screenHeight:1080f;
        return size switch{0=>Math.Clamp(h*.16f,130f,200f),2=>Math.Clamp(h*.30f,220f,360f),_=>Math.Clamp(h*.22f,170f,260f)};
    }
    // 0.8.1 «Навигация» preview: the disc keeps the HUD proportions of S/M/L — L fills the preview
    // area, S and M shrink by the same ratio as on screen (never below half the area).
    internal static float PreviewFraction(int size,float screenHeight)=>
        Math.Clamp(Diameter(size,screenHeight)/Diameter(2,screenHeight),.5f,1f);
    internal static bool Right(int corner)=>corner is 0 or 2;
    internal static bool Bottom(int corner)=>corner is 2 or 3;
    // The info HUD (ConvenienceTools.HudPlacement 0 = top right, 1 = top left, 2 = bottom centre) is
    // 144 px high from y=72: a top minimap on its side moves below it, as the M27 top-right one did.
    internal static MapShieldPolicy.Box Box(int corner,float sw,float sh,float size,bool infoHud,int infoPlacement)
    {
        bool right=Right(corner);
        float x=right?sw-size-Margin:Margin;
        float y=Bottom(corner)?sh-sh*BottomHud-size-Label:
            infoHud&&infoPlacement==(right?0:1)?InfoTop+InfoHeight+Gap:Margin;
        return new MapShieldPolicy.Box(x,Math.Max(Margin*.5f,y),size,size);
    }
    // Heading-up turns the map by the view yaw; north-up keeps the map still (yaw 0) and turns the
    // player's cone instead (clockwise screen degrees, same sense as M28PlayerMarkerPolicy.Rotate).
    internal static double MapYaw(bool northUp,double yaw)=>northUp||!double.IsFinite(yaw)?0:yaw;
    internal static float PlayerTurn(bool northUp,double yaw)=>northUp&&double.IsFinite(yaw)?(float)(yaw*180.0/Math.PI):0f;
}

// Smooth zoom: the shown range follows the chosen one exponentially in log space.
internal static class M29MiniMapZoom
{
    internal const float Speed=11.5f,Settle=.002f;
    internal static float Step(float current,float target,float dt)
    {
        target=M29NavigationSettings.ClampRange(target);
        if(!float.IsFinite(current)||current<=0)return target;
        if(!float.IsFinite(dt)||dt<=0)return current;
        double k=1-Math.Exp(-Speed*Math.Min(dt,.25f));
        double next=Math.Exp(Math.Log(current)+(Math.Log(target)-Math.Log(current))*k);
        return Math.Abs(next-target)<=target*Settle?target:(float)next;
    }
    // The terrain composite is rebuilt once the shown range left the composed one.
    internal static bool Stale(float composed,float range)=>!(composed>0)||!(range>0)||Math.Abs(composed-range)>range*Settle;
    // Composed terrain drawn at the live zoom (1 = the disc), never larger than the disc.
    internal static float TextureScale(float composed,float range)=>
        composed>0&&range>0&&float.IsFinite(composed/range)?Math.Min(1f,composed/range):1f;
}

// Compass selection with a share for every layer (task 5).
internal static class M29CompassPolicy
{
    internal const string RadarLayer="Радар",GhostLayer="Фантомные дроны";
    internal readonly struct Candidate
    {
        internal readonly string Key,Layer;
        internal readonly int Priority;
        internal readonly float Distance;
        internal Candidate(string key,string layer,int priority,float distance){Key=key;Layer=layer??"";Priority=priority;Distance=distance;}
    }
    private static float Known(float distance)=>float.IsFinite(distance)&&distance>=0?distance:float.MaxValue;
    private static int Compare(Candidate a,Candidate b)
    {
        int p=a.Priority.CompareTo(b.Priority);if(p!=0)return p;
        int d=Known(a.Distance).CompareTo(Known(b.Distance));if(d!=0)return d;
        return string.CompareOrdinal(a.Key,b.Key);
    }
    // Priority 0 (selection, compass goal, a pinned phantom drone) ignores the range; the others need a known distance within it.
    internal static bool Eligible(Candidate candidate,float range)=>
        !string.IsNullOrEmpty(candidate.Key)&&(candidate.Priority<=0||Known(candidate.Distance)<=range);
    private static List<Candidate> Sorted(IEnumerable<Candidate> candidates,float range)
    {
        var best=new Dictionary<string,Candidate>(StringComparer.Ordinal);
        if(candidates!=null)
            foreach(var candidate in candidates)
                if(Eligible(candidate,range)&&(!best.TryGetValue(candidate.Key,out var old)||Compare(candidate,old)<0))best[candidate.Key]=candidate;
        var all=new List<Candidate>(best.Values);
        all.Sort(Compare);
        return all;
    }
    // 1) priority 0 targets; 2) the best target of every layer that has one in range (layers in the
    // order of those targets: priority, distance) before any layer gets a second marker; 3) the
    // remaining slots by priority, distance, key. Keys are unique.
    internal static List<Candidate> Pick(IEnumerable<Candidate> candidates,int max,float range)
    {
        var result=new List<Candidate>();
        if(max<=0)return result;
        var all=Sorted(candidates,range);
        var taken=new HashSet<string>(StringComparer.Ordinal);
        var layers=new HashSet<string>(StringComparer.Ordinal);
        foreach(var candidate in all){if(result.Count>=max)break;if(candidate.Priority<=0&&taken.Add(candidate.Key))result.Add(candidate);}
        foreach(var candidate in all)
        {
            if(result.Count>=max)break;
            if(candidate.Priority>0&&!taken.Contains(candidate.Key)&&layers.Add(candidate.Layer)){taken.Add(candidate.Key);result.Add(candidate);}
        }
        foreach(var candidate in all){if(result.Count>=max)break;if(taken.Add(candidate.Key))result.Add(candidate);}
        return result;
    }
    // Layers with a target in range (priority 0 targets aside) and how many of them the limit left without a marker.
    internal static int Layers(IEnumerable<Candidate> candidates,float range)
    {
        var layers=new HashSet<string>(StringComparer.Ordinal);
        foreach(var candidate in Sorted(candidates,range))if(candidate.Priority>0)layers.Add(candidate.Layer);
        return layers.Count;
    }
    internal static int Starved(IEnumerable<Candidate> candidates,IEnumerable<Candidate> picked,float range)
    {
        var shown=new HashSet<string>(StringComparer.Ordinal);
        if(picked!=null)foreach(var candidate in picked)if(candidate.Priority>0)shown.Add(candidate.Layer);
        var missing=new HashSet<string>(StringComparer.Ordinal);
        foreach(var candidate in Sorted(candidates,range))if(candidate.Priority>0&&!shown.Contains(candidate.Layer))missing.Add(candidate.Layer);
        return missing.Count;
    }
    // Phantom route drones: normal = like the other drones (2), high = like own markers (1), pinned =
    // the zone's nearest route ignores the range (0) and the other routes are high.
    internal static int GhostPriority(int mode,bool nearest)=>mode>=2?(nearest?0:1):mode==1?1:2;
}

// Layer switching (task 5): what an enabled layer needs to reach the navigator list and the HUD.
internal static class M29LayerPolicy
{
    internal const string Pots="Горшочки",Spawners="Спавнеры";
    internal static bool SourceLayer(string category)=>category==Pots||category==Spawners;
    // The source census ran only for the atlas (menu tab or native map with a source layer); the
    // minimap and the compass need it as well while one of them is on and a source layer is enabled.
    internal static bool HudNeedsSources(bool miniMap,bool compass,bool sourceLayerOn)=>sourceLayerOn&&(miniMap||compass);
    // Census steps per frame: the atlas (game paused) keeps 32, the live HUD uses fewer.
    internal static int CensusBudget(bool atlas)=>atlas?32:12;
    // Scene group of a source point: the native owner when known (live gold and containers); a potential
    // source has none, but it is only observed in a loaded scene, i.e. one of the current scene group.
    internal static string SourceGroup(string owner,string current,bool sceneLoaded)=>
        !string.IsNullOrWhiteSpace(owner)?owner.Trim():sceneLoaded&&!string.IsNullOrWhiteSpace(current)?current.Trim():"";
    // Minimap cap: rounds of one point per layer, each round nearest first, so every enabled layer
    // keeps a share of the markers. Returns indices into the input lists.
    internal static List<int> RoundRobin(IReadOnlyList<string> layers,IReadOnlyList<float> distances,int max)
    {
        var result=new List<int>();
        if(layers==null||distances==null||max<=0)return result;
        int count=Math.Min(layers.Count,distances.Count);
        float D(int i)=>float.IsFinite(distances[i])&&distances[i]>=0?distances[i]:float.MaxValue;
        int Order(int a,int b){int c=D(a).CompareTo(D(b));return c!=0?c:a.CompareTo(b);}
        var groups=new Dictionary<string,List<int>>(StringComparer.Ordinal);
        for(int i=0;i<count;i++)
        {
            string key=layers[i]??"";
            if(!groups.TryGetValue(key,out var list))groups[key]=list=new List<int>();
            list.Add(i);
        }
        foreach(var list in groups.Values)list.Sort(Order);
        var round=new List<int>();
        for(int r=0;result.Count<max;r++)
        {
            round.Clear();
            foreach(var list in groups.Values)if(r<list.Count)round.Add(list[r]);
            if(round.Count==0)break;
            round.Sort(Order);
            foreach(int i in round){if(result.Count>=max)break;result.Add(i);}
        }
        return result;
    }
}
