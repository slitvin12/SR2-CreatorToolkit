namespace CreatorToolkit;

// Map badges for the mod's capsule / map-node / drone layers (owner request
// 2026-10-26: only exact Slime Rancher 2 Fandom or game artwork, nothing drawn
// by the mod). The cells index FandomAtlas.rgba, whose 64 px cells are
// byte-identical RGBA copies of the ru.slimerancher.fandom.com interactive-map
// marker files named by Source(). Pure: unit-tested and hash-pinned offline.
internal static class CanonMapIconPolicy
{
    internal const int None=-1,MapNode=0,ResearchDrone=5,PhantomDrone=6,TreasurePod=8;

    internal static int CategoryCell(string category)=>category switch
    {
        "Капсулы"=>TreasurePod,
        "Ноды карты"=>MapNode,
        "Дроны"=>ResearchDrone,
        _=>None
    };
    internal static bool Owns(string category)=>CategoryCell(category)!=None;
    // Phantom routes and live component drones share the drone layer but use
    // the wiki's ghostly-drone marker; static research drones never match GhostDrone.
    internal static int Cell(string category,string id,string name)
    {
        int cell=CategoryCell(category);
        return cell==ResearchDrone&&NativeIconPolicy.GhostDrone(id,name)?PhantomDrone:cell;
    }
    // Marker names as the Russian Fandom map legend shows them.
    internal static string Label(int cell)=>cell switch
    {
        TreasurePod=>"Капсула с сокровищами",
        MapNode=>"Узел карты",
        ResearchDrone=>"Поисковый дрон",
        PhantomDrone=>"Призрачный дрон",
        _=>""
    };
    // Wiki file ("Файл:<name>") each atlas cell was copied from.
    internal static string Source(int cell)=>cell switch
    {
        TreasurePod=>"Map treasure pod marker.png",
        MapNode=>"Map link marker.png",
        ResearchDrone=>"Map res drone marker SR2.png",
        PhantomDrone=>"Map drone marker SR2.png",
        _=>""
    };
}
