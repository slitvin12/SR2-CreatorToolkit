using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CreatorToolkit;

// 0.8.1 «Карта» pages (approved mockups 02-04 and 07): pure rules for «Мои метки», the radar
// targets and their per-save file, the navigation pictograms and the HUD minimap rim.
internal static class MapExplorerPolicy
{
    // ---- Мои метки ----
    internal static readonly string[] MarkerSorts={"По расстоянию","По имени","Сначала новые"};
    internal const float RenameDelay=.8f;
    internal static int NextSort(int mode)=>(Math.Clamp(mode,0,MarkerSorts.Length-1)+1)%MarkerSorts.Length;
    // 0 distance (unknown last), 1 name, 2 newest first (unknown last); ties keep the stored order.
    internal static List<int> SortMarkers(IReadOnlyList<string> names,IReadOnlyList<float> distances,IReadOnlyList<long> created,int mode)
    {
        int n=names?.Count??0;var order=new List<int>(n);
        for(int i=0;i<n;i++)order.Add(i);
        order.Sort((a,b)=>
        {
            int c=0;
            if(mode==1)c=string.Compare(names[a]??"",names[b]??"",StringComparison.OrdinalIgnoreCase);
            else if(mode==2)
            {
                long x=created!=null&&a<created.Count?created[a]:0,y=created!=null&&b<created.Count?created[b]:0;
                c=(x<=0)!=(y<=0)?(x<=0?1:-1):y.CompareTo(x);
            }
            else
            {
                float x=distances!=null&&a<distances.Count?distances[a]:-1,y=distances!=null&&b<distances.Count?distances[b]:-1;
                bool ux=!(x>=0),uy=!(y>=0);
                c=ux!=uy?(ux?1:-1):ux?0:x.CompareTo(y);
            }
            return c!=0?c:a.CompareTo(b);
        });
        return order;
    }
    internal static string CreatedText(long unixSeconds)
    {
        if(unixSeconds<=0)return "";
        try{return "создана "+DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("dd.MM",CultureInfo.InvariantCulture);}
        catch{return "";}
    }
    internal static string ListHeader(int count,int max)=>count+" из "+max+" · хранятся отдельно от сейва";

    // ---- Радар ----
    internal const int MinRadius=25,MaxRadius=250,DefaultRadius=100,RadiusStep=5,MaxTargets=24,Colors=6,MaxHighlights=12;
    internal static readonly string[] RadarCategories={"Всё","Слаймы","Ларго","Гордо","Еда","Плорты","Ресурсы"};
    internal static bool RadarCategory(string category)=>category is "Слаймы" or "Ларго" or "Гордо" or "Еда" or "Плорты" or "Ресурсы";
    internal static int ClampRadius(float metres)=>float.IsFinite(metres)?
        Math.Clamp((int)Math.Round(metres/RadiusStep)*RadiusStep,MinRadius,MaxRadius):DefaultRadius;
    // The first colour not used yet, else round robin.
    internal static int NextColor(IReadOnlyList<int> used)
    {
        int count=used?.Count??0;
        for(int c=0;c<Colors;c++)
        {
            bool taken=false;
            for(int i=0;i<count&&!taken;i++)taken=used[i]==c;
            if(!taken)return c;
        }
        return count%Colors;
    }
    internal static int CycleColor(int color)=>((color%Colors+Colors)%Colors+1)%Colors;
    internal static string Kind(string category)=>category switch
    {
        "Слаймы"=>"Слайм","Ларго"=>"Ларго","Гордо"=>"Гордо","Еда"=>"Еда","Плорты"=>"Плорт",_=>"Ресурс"
    };
    internal static string NearText(int near)=>near<=0?"нет рядом":near+" рядом";
    // «Ресурс · ближайший 84 м», «Еда · любимая: Хлопковый слайм».
    internal static string RadarSub(string category,float nearest,string owner)
    {
        var sb=new StringBuilder(Kind(category));
        if(!string.IsNullOrEmpty(owner))sb.Append(" · любимая: ").Append(owner);
        else if(nearest>=0&&float.IsFinite(nearest))sb.Append(" · ближайший ").Append(MapDistanceText.Format(nearest));
        return sb.ToString();
    }
    internal static string OnRadar(int count)=>count+" на радаре";

    // Per-save radar file: radar-<save>.cfg, «key=value» lines; unknown or invalid lines are skipped.
    internal sealed class RadarTarget{internal string Reference="",Owner="";internal int Color;internal bool Auto;}
    internal sealed class RadarConfig
    {
        internal bool On=true,Highlight=true,Hud=true;
        internal int Radius=DefaultRadius;
        internal readonly List<RadarTarget> Targets=new();
    }
    internal const string RadarFilePrefix="radar-",RadarFileSuffix=".cfg";
    internal static bool ValidReference(string value)
    {
        if(string.IsNullOrEmpty(value)||value.Length>160)return false;
        foreach(char c in value)if(char.IsControl(c)||c=='|'||c=='='||char.IsWhiteSpace(c))return false;
        return true;
    }
    internal static string SerializeRadar(RadarConfig config)
    {
        var sb=new StringBuilder();
        sb.Append("# Creator Toolkit radar · per save\nversion=1\n");
        sb.Append("on=").Append(config.On?1:0).Append('\n');
        sb.Append("radius=").Append(ClampRadius(config.Radius).ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("highlight=").Append(config.Highlight?1:0).Append('\n');
        sb.Append("hud=").Append(config.Hud?1:0).Append('\n');
        var seen=new HashSet<string>(StringComparer.Ordinal);
        int written=0;
        foreach(var t in config.Targets)
        {
            if(t==null||!ValidReference(t.Reference)||!seen.Add(t.Reference)||written>=MaxTargets)continue;
            written++;
            sb.Append("target=").Append(t.Reference).Append('|').Append(Math.Clamp(t.Color,0,Colors-1).ToString(CultureInfo.InvariantCulture))
              .Append('|').Append(t.Auto?1:0).Append('|').Append(ValidReference(t.Owner)?t.Owner:"").Append('\n');
        }
        return sb.ToString();
    }
    internal static RadarConfig ParseRadar(string text,out int rejected)
    {
        rejected=0;var config=new RadarConfig();
        if(string.IsNullOrEmpty(text))return config;
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var raw in text.Split('\n'))
        {
            string line=raw.Trim();
            if(line.Length==0||line[0]=='#')continue;
            int eq=line.IndexOf('=');
            if(eq<=0){rejected++;continue;}
            string key=line.Substring(0,eq).Trim().ToLowerInvariant(),value=line.Substring(eq+1).Trim();
            switch(key)
            {
                case "version":if(value!="1")rejected++;break;
                case "on":if(Flag(value,out bool on))config.On=on;else rejected++;break;
                case "highlight":if(Flag(value,out bool lit))config.Highlight=lit;else rejected++;break;
                case "hud":if(Flag(value,out bool hud))config.Hud=hud;else rejected++;break;
                case "radius":
                    if(int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int r))config.Radius=ClampRadius(r);else rejected++;
                    break;
                case "target":
                {
                    var parts=value.Split('|');
                    if(parts.Length<3||!ValidReference(parts[0])||!int.TryParse(parts[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out int color)||
                        color<0||color>=Colors||!Flag(parts[2],out bool auto)||config.Targets.Count>=MaxTargets||!seen.Add(parts[0]))
                    {rejected++;break;}
                    string owner=parts.Length>3&&ValidReference(parts[3])?parts[3]:"";
                    config.Targets.Add(new RadarTarget{Reference=parts[0],Color=color,Auto=auto,Owner=owner});
                    break;
                }
                default:rejected++;break;
            }
        }
        return config;
    }
    private static bool Flag(string value,out bool on)
    {
        on=value=="1"||value.Equals("true",StringComparison.OrdinalIgnoreCase)||value.Equals("on",StringComparison.OrdinalIgnoreCase);
        return on||value=="0"||value.Equals("false",StringComparison.OrdinalIgnoreCase)||value.Equals("off",StringComparison.OrdinalIgnoreCase);
    }

    // ---- Навигация ----
    // Screen-corner pictogram, M29 corner ids: 0 top-right, 1 top-left, 2 bottom-right, 3 bottom-left.
    internal static bool CornerRight(int corner)=>corner is 0 or 2;
    internal static bool CornerBottom(int corner)=>corner is 2 or 3;
    internal static int CornerAt(bool right,bool bottom)=>bottom?(right?2:3):(right?0:1);
    internal static int Opacity(int transparency)=>100-M29NavigationSettings.ClampTransparency(transparency);
    internal static int TransparencyFor(int opacity)=>M29NavigationSettings.ClampTransparency(100-Math.Clamp(opacity,0,100));
    internal static readonly string[] OrientationTitles={"По взгляду","Север вверх"};
    internal static readonly string[] GhostTitles={"Обычно","Важно","Всегда"};
    internal static readonly string[] SizeLetters={"S","M","L"};
    internal static string LayersChip(string layers,bool radar)=>"Значки = слои атласа: "+layers+(radar?" + радар":"");

    // ---- HUD-мини-карта ----
    internal const int MiniMapIcons=24;
    // A point beyond the disc is pinned to its rim along the same bearing (true when pinned).
    internal static bool Rim(float x,float y,float limit,out float rx,out float ry)
    {
        float d=MathF.Sqrt(x*x+y*y);
        if(!(limit>0)||d<=limit||d<1e-4f){rx=x;ry=y;return false;}
        rx=x/d*limit;ry=y/d*limit;return true;
    }
    // The distance chip sits on the inner side of a rim pin.
    internal static void ChipSide(float x,float y,float chipWidth,out float cx,out float cy)
    {
        float d=MathF.Sqrt(x*x+y*y);
        if(d<1e-4f){cx=x+18;cy=y-11;return;}
        float ux=x/d,uy=y/d;
        cx=x-ux*(chipWidth*.5f+22)-chipWidth*.5f;cy=y-uy*20-11;
    }
}
