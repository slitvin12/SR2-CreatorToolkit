using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CreatorToolkit;

// M29, task 1 (atlas). Unity-free policies, linked into the offline checker.
//  * M29AtlasSearchPolicy: own markers («Мои метки») are pinned on the native atlas page:
//    neither the search text nor the layer buttons hide them there (M28 live report: the
//    search «дрон» removed the player's own markers from the map). The list on the atlas
//    page still filters them like any other point. An active search is shown as an
//    explicit chip with a clear button instead of a silent filter.
//  * M29MapCardPolicy: the selected-object card used one fixed rectangle beside the creator
//    panel. On 4:3 and 5:4 screens that rectangle covers the map centre, where Focus() puts
//    the selected object, so the card hid the very marker it describes. The card now takes
//    the first clear slot (the M28 rectangle first), never over the shown object's marker,
//    the creator panel or the native map controls, and keeps its slot while it stays clear,
//    so it does not jump while the map pans.
internal static class M29AtlasSearchPolicy
{
    internal const string OwnCategory="Мои метки";
    internal const int ChipChars=40;
    internal static string Needle(string search)=>(search??"").Trim().ToLowerInvariant();
    internal static bool Active(string search)=>Needle(search).Length>0;
    internal static bool PinnedOnMap(string category)=>string.Equals(category,OwnCategory,StringComparison.Ordinal);
    internal static string ChipHead(int hits)=>hits>0?"ПОИСК · "+hits.ToString(CultureInfo.InvariantCulture)+" НА КАРТЕ":"ПОИСК · НЕТ НА КАРТЕ";
    internal static string ChipQuery(string search)
    {
        string text=(search??"").Trim();
        if(text.Length>ChipChars)text=text.Substring(0,ChipChars-1).TrimEnd()+"…";
        return "«"+text+"»";
    }
    internal static string PageHint(string search)=>"поиск "+ChipQuery(search)+" · свои метки на карте видны всегда";
}

internal static class M29MapCardPolicy
{
    // Logical (1080p-scaled) overlay pixels, the same space as ToolkitUi.DrawMapOverlay.
    internal const float Width=420,Height=414,Edge=16,Top=136,PanelGap=16,BottomReserve=100,
        LeftTop=236,LeftLowX=132,AnchorMargin=12,CentreHalf=48,PanelMargin=8,ClearPenalty=.5f;
    internal enum Slot{Beside=0,Left=1,LeftLow=2,BesideLow=3}
    internal static readonly Slot[] Preference={Slot.Beside,Slot.Left,Slot.LeftLow,Slot.BesideLow};
    internal readonly struct Placement
    {
        internal readonly MapShieldPolicy.Box Card;
        internal readonly Slot Slot;
        internal readonly float Penalty;
        internal readonly bool Anchored;
        internal Placement(MapShieldPolicy.Box card,Slot slot,float penalty,bool anchored)
        {Card=card;Slot=slot;Penalty=penalty;Anchored=anchored;}
        internal bool Clear=>Penalty<=ClearPenalty;
    }

    // Beside is exactly the M28 rectangle new(Mathf.Max(16,panelBounds.x-436),136,420,414).
    // Left starts below the native zone title/counter (0,120,260,100); LeftLow clears the
    // native zoom column (0,sh-400,116,320); both low slots end above the native hint row.
    internal static MapShieldPolicy.Box Candidate(Slot slot,float sw,float sh,MapShieldPolicy.Box panel)
    {
        float besideX=Math.Max(Edge,panel.X-PanelGap-Width);
        float lowY=Math.Max(Top,sh-BottomReserve-Height);
        return slot switch
        {
            Slot.Left=>new MapShieldPolicy.Box(Edge,LeftTop,Width,Height),
            Slot.LeftLow=>new MapShieldPolicy.Box(LeftLowX,Math.Max(LeftTop,lowY),Width,Height),
            Slot.BesideLow=>new MapShieldPolicy.Box(besideX,lowY,Width,Height),
            _=>new MapShieldPolicy.Box(besideX,Top,Width,Height)
        };
    }
    // Focus() centres the native map on the selected object.
    internal static MapShieldPolicy.Box Centre(float sw,float sh)=>new(sw*.5f-CentreHalf,sh*.5f-CentreHalf,2*CentreHalf,2*CentreHalf);
    internal static MapShieldPolicy.Box Inflate(MapShieldPolicy.Box box,float by)=>new(box.X-by,box.Y-by,box.Width+2*by,box.Height+2*by);
    internal static float Overlap(MapShieldPolicy.Box a,MapShieldPolicy.Box b)
    {
        if(!a.Valid||!b.Valid)return 0;
        float w=Math.Min(a.Right,b.Right)-Math.Max(a.X,b.X),h=Math.Min(a.Bottom,b.Bottom)-Math.Max(a.Y,b.Y);
        return w>0&&h>0?w*h:0;
    }
    // Common area as a share (0..1) of the smaller of the two rectangles.
    private static float Share(MapShieldPolicy.Box a,MapShieldPolicy.Box b)
    {
        float area=Math.Min(a.Width*a.Height,b.Width*b.Height);
        return area>0?Overlap(a,b)/area:0;
    }
    // Any contact is a hard violation (never "clear"), weighted by its size for the fallback.
    private static float Hard(float share,float weight,float tolerance=0)=>share>tolerance?1f+weight*share:0;
    // 0 = perfect. Leaving the usable map, touching the panel, native controls or the shown
    // marker (with its margin) is never clear; the centre is only a soft hint for frames where
    // the shown marker is not projected (off page, under native chrome).
    internal static float Penalty(MapShieldPolicy.Box card,float sw,float sh,MapShieldPolicy.Box panel,
        MapShieldPolicy.Box? anchor,IReadOnlyList<MapShieldPolicy.Box> obstacles)
    {
        float area=card.Width*card.Height;
        if(!card.Valid||area<=0)return float.MaxValue;
        var usable=new MapShieldPolicy.Box(0,0,Math.Max(1,sw),Math.Max(1,sh-BottomReserve));
        float penalty=Hard(Math.Max(0,1f-Overlap(card,usable)/area),8f,1e-4f);
        if(panel.Valid)penalty+=Hard(Share(card,Inflate(panel,PanelMargin)),8f);
        if(obstacles!=null)foreach(var obstacle in obstacles)penalty+=Hard(Share(card,obstacle),8f);
        if(anchor is MapShieldPolicy.Box shown&&shown.Valid)penalty+=Hard(Share(card,Inflate(shown,AnchorMargin)),16f);
        penalty+=4f*Share(card,Centre(sw,sh));
        return penalty;
    }
    // previous = the slot of the same shown object on the previous frame (null for a new object):
    // it is kept while clear. Otherwise the first clear slot in preference order wins; when none
    // is clear (tiny screens) the least bad one.
    internal static Placement Place(float sw,float sh,MapShieldPolicy.Box panel,MapShieldPolicy.Box? anchor,
        IReadOnlyList<MapShieldPolicy.Box> obstacles,Slot? previous)
    {
        bool anchored=anchor.HasValue&&anchor.Value.Valid;
        if(previous is Slot kept&&Array.IndexOf(Preference,kept)>=0)
        {
            var card=Candidate(kept,sw,sh,panel);
            float penalty=Penalty(card,sw,sh,panel,anchor,obstacles);
            if(penalty<=ClearPenalty)return new Placement(card,kept,penalty,anchored);
        }
        Placement best=default;bool found=false;
        foreach(var slot in Preference)
        {
            var card=Candidate(slot,sw,sh,panel);
            float penalty=Penalty(card,sw,sh,panel,anchor,obstacles);
            if(penalty<=ClearPenalty)return new Placement(card,slot,penalty,anchored);
            if(!found||penalty<best.Penalty){best=new Placement(card,slot,penalty,anchored);found=true;}
        }
        return best;
    }
    internal static string Name(Slot slot)=>slot switch{Slot.Left=>"left",Slot.LeftLow=>"left-low",Slot.BesideLow=>"beside-low",_=>"beside"};
    internal static string Describe(Placement placement)=>Name(placement.Slot)+" card="+
        M28TestPolicy.Box(placement.Card.X,placement.Card.Y,placement.Card.Width,placement.Card.Height)+
        " clear="+(placement.Clear?1:0)+" penalty="+placement.Penalty.ToString("0.00",CultureInfo.InvariantCulture)+
        " anchored="+(placement.Anchored?1:0);
}

// M29, task 2 (phantom drones). The twelve native component-drone routes (GhostDroneIndex.tsv)
// got a world zone only when exactly one IsGameplay SceneGroup listed the route's scene. The
// game also ships test and aggregate groups (AllZones, SceneRestructuringTest, DenExpansion_Test)
// that list real zone scenes too, so 7 of 12 routes stayed without a zone and fell out of the
// atlas pages and the compass (M28 live audit). A route now takes, in this order:
//   membership  the only complete SceneGroup (every AssetReference located) listing its scene;
//   map         several owners: the only one with a native atlas page (map reference:
//               ZoneSceneGroupMapping -> MapDirector.GetMapForZone, loaded with the atlas UI);
//   authored    the zone recorded for the route in GhostDroneIndex.tsv (column 6), when it is
//               one of the owners or when no group lists the scene (membership unreadable);
//   unique      the only owner, even with incomplete membership;
//   live        the scene group of the route's native drone model (ComponentAcqDroneModel)
//               overrides all of the above while the save runs.
internal static class M29GhostZonePolicy
{
    internal const string Membership="membership",MapPage="map",Authored="authored",Unique="unique",Live="live",None="none";
    internal static readonly string[] Sources={Membership,MapPage,Authored,Unique,Live,None};
    internal readonly struct Zone
    {
        internal readonly string Name;
        internal readonly bool Complete;
        internal readonly IEnumerable<string> Scenes;
        internal Zone(string name,bool complete,IEnumerable<string> scenes){Name=(name??"").Trim();Complete=complete;Scenes=scenes;}
        internal bool Owns(string scene)
        {
            string target=Scene(scene);
            if(Scenes==null||target.Length==0)return false;
            foreach(string path in Scenes)if(string.Equals(Scene(path),target,StringComparison.OrdinalIgnoreCase))return true;
            return false;
        }
    }
    internal readonly struct Binding
    {
        internal readonly string Group,Source;
        internal Binding(string group,string source){Group=(group??"").Trim();Source=Group.Length==0?None:source??None;}
        internal bool Bound=>!string.IsNullOrEmpty(Group);
    }
    internal static string Scene(string path)=>(path??"").Trim().Replace('\\','/');
    private static bool Same(string a,string b)=>string.Equals((a??"").Trim(),(b??"").Trim(),StringComparison.OrdinalIgnoreCase);
    // hasMap: does the named group have a native atlas page (null while the atlas UI is not loaded).
    internal static Binding Resolve(string scene,string authored,IEnumerable<Zone> zones,Func<string,bool> hasMap=null)
    {
        var known=(zones??Array.Empty<Zone>()).Where(z=>z.Name.Length>0).ToList();
        var owners=known.Where(z=>z.Owns(scene)).ToList();
        var complete=owners.Where(z=>z.Complete).ToList();
        if(complete.Count==1)return new Binding(complete[0].Name,Membership);
        var pool=complete.Count>1?complete:owners;
        if(pool.Count>1&&hasMap!=null)
        {
            var paged=pool.Where(z=>hasMap(z.Name)).ToList();
            if(paged.Count==1)return new Binding(paged[0].Name,MapPage);
        }
        if(!string.IsNullOrWhiteSpace(authored))
            foreach(var zone in owners.Count==0?known:pool)if(Same(zone.Name,authored))return new Binding(zone.Name,Authored);
        return owners.Count==1?new Binding(owners[0].Name,Unique):new Binding("",None);
    }
    // Only the weaker static sources can still improve once the atlas (map reference) is loaded.
    internal static bool MapMayImprove(string source)=>source!=Membership&&source!=MapPage&&source!=Live;
    // The route's native drone model knows its scene group; a group known to the index wins.
    internal static Binding WithLive(Binding current,string modelGroup,bool known)=>
        known&&!string.IsNullOrWhiteSpace(modelGroup)?new Binding(modelGroup,Live):current;
    internal static string Label(string source)=>source switch
    {
        Membership=>"зона по сцене маршрута",
        MapPage=>"зона по справочнику карты",
        Authored=>"зона по индексу маршрутов",
        Unique=>"зона по единственной группе сцены",
        Live=>"зона по живому дрону",
        _=>"зона не определена"
    };
    internal static string Summary(IEnumerable<string> sources)
    {
        var list=(sources??Array.Empty<string>()).Select(s=>Array.IndexOf(Sources,s)>=0?s:None).ToList();
        return "routes="+list.Count+" bound="+list.Count(s=>s!=None)+string.Concat(Sources.Select(s=>" "+s+"="+list.Count(x=>x==s)));
    }
}
