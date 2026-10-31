using System;
using System.Collections.Generic;

namespace CreatorToolkit;

// Exact native artwork for every "Редкое" and "Ресурсы: ценные" find kind. The
// baked atlas cells used to answer for these rows, which is why radiant slime
// spawns looked like bushes and plort statues looked like blank squares. Each
// kind now names real game sprites; an unknown kind stays unresolved instead of
// borrowing unrelated art.
internal static class MapRareIcons
{
    private static readonly (string Kind,string[] Names)[] Table =
    {
        ("Врата плортов",new[]{"iconMapPlortDoor"}),
        ("Теневые врата",new[]{"iconMapShadowPlortDoor"}),
        ("Сборщик теневых плортов",new[]{"iconMapShadowPlortDoor","iconMapPlortDoor"}),
        ("Приёмник плортов",new[]{"iconGadgetRefineryLink","iconMapPlortDoor"}),
        // 0.8.1: a plort statue shows the game's own golden statue of its slime species (the
        // decor gadget art), then the plort it asks for; «?» keeps the generic statue.
        ("Статуя для плорта",new[]{"iconDecorGoldenStatuePink_small","iconDecorCheerfulStatue_small"}),
        ("Статуя для плорта · скалистый",new[]{"iconDecorGoldenStatueRock_small","iconPlortRock_small"}),
        ("Статуя для плорта · бум",new[]{"iconDecorGoldenStatueBoom_small","iconPlortBoom_small"}),
        ("Статуя для плорта · удильщик",new[]{"iconDecorGoldAnglerStatue_small","iconPlortAngler_small"}),
        ("Статуя для плорта · охотник",new[]{"iconDecorGoldenStatueHunter_small","iconPlortHunter_small"}),
        ("Статуя для плорта · нетопырь",new[]{"iconDecorGoldenStatueBatty_small","iconPlortBatty_small"}),
        ("Статуя для плорта · фосфорный",new[]{"iconDecorGoldenStatuePhosphor_small","iconPlortPhosphor_small"}),
        ("Статуя для плорта · порхающий",new[]{"iconDecorGoldenFlutterStatue_small","iconPlortFlutter_small"}),
        ("Статуя для плорта · мяуровый",new[]{"iconDecorGoldenStatueTabby_small","iconPlortTabby_small"}),
        ("Статуя для плорта · теневой",new[]{"iconPlortShadow_small"}),
        ("Статуя для плорта · сламбер",new[]{"iconDecorGoldenStatueSloomber_small","iconPlortSloomber_small"}),
        ("Статуя для плорта · розовый",new[]{"iconDecorGoldenStatuePink_small","iconPlortPink_small"}),
        ("Статуя для плорта · медовый",new[]{"iconDecorGoldenStatueHoney_small","iconPlortHoney_small"}),
        ("Статуя для плорта · двойняшка",new[]{"iconDecorGoldenStatueTwin_small","iconPlortTwin_small"}),
        ("Лучистый слайм",new[]{"iconRadiantPedia_small","iconRadiantSlimePink_small"}),
        ("Золотой слайм",new[]{"iconSlimeGold_small"}),
        ("Слайм удачи",new[]{"iconSlimeLucky_small"}),
        ("Декор",new[]{"iconDecorSimpleFlag_small","iconDecorMushroomPlanter_small"}),
        ("Лучистая руда",new[]{"iconCraftRadiantOre_small"}),
        ("Странный алмаз",new[]{"iconCraftStrangeDiamond_small"}),
        ("Желейный камень",new[]{"iconCraftJellyStone_small"}),
        ("Жужжащий воск",new[]{"iconCraftBuzzWax_small"}),
        ("Дикий мёд",new[]{"iconCraftWildHoney_small"}),
        ("Насыщенный солевой раствор",new[]{"iconCraftDeepBrine_small"}),
        ("Нектар лунной росы",new[]{"iconCategoryNectar_small","iconCraftRoyalJelly_small"}),
        ("Солнечный сок",new[]{"iconCraftSunSap_small"})
    };
    private static string[] _all;
    // The plort a statue asks for (drawn as a badge over the statue art), or null.
    internal static string StatuePlort(string name)
    {
        foreach(string art in Names(name))
            if(art.StartsWith("iconPlort",StringComparison.Ordinal))return art;
        return null;
    }
    // Kinds carry a " · species N" suffix, so the longest declared prefix wins:
    // "Теневые врата" must never be answered by the shorter "Врата плортов".
    internal static string[] Names(string filterKind)
    {
        string kind=(filterKind??"").Trim();
        if(kind.Length==0)return Array.Empty<string>();
        string[] best=Array.Empty<string>();int bestLength=0;
        foreach(var row in Table)
        {
            if(row.Kind.Length<=bestLength)continue;
            bool match=string.Equals(kind,row.Kind,StringComparison.Ordinal)||
                kind.StartsWith(row.Kind+" ",StringComparison.Ordinal);
            if(!match)continue;
            best=row.Names;bestLength=row.Kind.Length;
        }
        return best;
    }
    internal static IReadOnlyList<string> AllNames
    {
        get
        {
            if(_all!=null)return _all;
            var seen=new HashSet<string>(StringComparer.Ordinal);
            var names=new List<string>();
            foreach(var row in Table)
                foreach(string name in row.Names)
                    if(seen.Add(name))names.Add(name);
            return _all=names.ToArray();
        }
    }
}
