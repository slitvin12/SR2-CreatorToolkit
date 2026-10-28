using System;

namespace CreatorToolkit;

// Splits the game's DiscoverableResources spawn records into three atlas layers
// (owner request 2026-10-26): crafting materials (jellystone, royal jelly,
// radiant ore, buzz wax ...) stay in «Ресурсы»; fruit, veggies, hens/chicks
// and moondew nectar move to «Еда»; the plort lists of slime spawn areas move
// to «Плорты». The stable IdentifiableType name suffix decides (every type in
// the installed game's records has one); the live catalog category (ActorTools)
// answers only for a future type without a known suffix. Pure and free of game
// types so the offline checker links it directly.
internal static class MapResourceLayerPolicy
{
    internal const string Materials="Ресурсы",Food="Еда",Plorts="Плорты";
    internal static readonly string[] Layers={Materials,Food,Plorts};
    private const string Prefix="IdentifiableType.";

    internal static bool Owns(string category)=>category==Materials||category==Food||category==Plorts;

    internal static string TypeName(string reference)
    {
        string name=reference?.Trim()??"";
        return name.StartsWith(Prefix,StringComparison.Ordinal)?name.Substring(Prefix.Length):name;
    }

    // Layer of one record type; null when the record names no type at all
    // (such records have nothing to show and are skipped).
    internal static string Classify(string reference,string catalogCategory=null)
    {
        string name=TypeName(reference);
        if(name.Length==0)return null;
        if(name.EndsWith("Plort",StringComparison.Ordinal))return Plorts;
        if(name.EndsWith("Craft",StringComparison.Ordinal))return Materials;
        if(name.EndsWith("Fruit",StringComparison.Ordinal)||name.EndsWith("Veggie",StringComparison.Ordinal)||
           name.EndsWith("Nectar",StringComparison.Ordinal)||name.EndsWith("Hen",StringComparison.Ordinal)||
           name.EndsWith("Chick",StringComparison.Ordinal)||name=="Rooster")return Food;
        return catalogCategory switch {Plorts=>Plorts,Food=>Food,_=>Materials};
    }

    internal static string Detail(string layer)=>layer switch
    {
        Food=>"Место возможного появления еды, не подтверждённый живой предмет.",
        Plorts=>"Зона слаймов, дающих этот плорт (данные игры), не подтверждённый живой предмет.",
        _=>"Место возможного появления ресурса, не подтверждённый живой предмет."
    };
}
