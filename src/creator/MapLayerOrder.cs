using System;
using System.Collections.Generic;

namespace CreatorToolkit;

// Atlas layer presentation. The data still comes from MapTools.Categories; this
// only fixes the order used by the compact native-map panel and the atlas page
// filter row, and the short caption that fits the narrow two-column grid.
// Deliberately free of game and native types so the offline checker can link it
// directly: the two fandom layer names mirror FandomMapMarkers.GatherCategory and
// FandomMapMarkers.RareCategory, and a source check fails if those ever drift.
internal static class MapLayerOrder
{
    internal const string FandomGather = "Ресурсы: ценные";
    internal const string FandomRare = "Редкое";

    // Unique physical objects first, then finds and spawn surfaces. Crafting
    // materials, food and plort spawns are separate layers (MapResourceLayerPolicy).
    internal static readonly string[] Ranked =
    {
        "Капсулы","Гордо","Двери","Механизмы","Телепорты","Ноды карты","Дроны",
        "Горшочки","Спавнеры","Ресурсы","Еда","Плорты",FandomGather,FandomRare,
    };
    internal const int Objects = 7;

    // Atlas page filter pills: every layer fits in two rows above the list
    // controls, never fewer than six pills per row (8 px gaps).
    internal static int FilterColumns(int layers) => Math.Max(6, (layers + 1) / 2);
    internal static float FilterWidth(float total, int layers)
    {
        int columns = FilterColumns(layers);
        return (total - 8f * (columns - 1)) / columns;
    }

    // Compact native-map panel: a two-column grid per group (40 px rows, 24 px
    // header gap) from y=196; the controls below and the panel follow the grid
    // (12 layers: controls at 504, panel 780 px).
    internal const float PanelGridTop = 196f;
    internal static float PanelControlsTop(int layers)
    {
        int objects = Math.Min(Objects, Math.Max(0, layers)), finds = Math.Max(0, layers - Objects);
        return PanelGridTop + ((objects + 1) / 2) * 40f + 24f + ((finds + 1) / 2) * 40f + 24f - 20f;
    }
    internal static float PanelHeight(int layers) => PanelControlsTop(layers) + 276f;

    internal static string[] Sorted(IEnumerable<string> available)
    {
        var layers = available ?? Array.Empty<string>();
        var ordered = new List<string>(Ranked.Length);
        foreach (var rank in Ranked)
            foreach (var layer in layers)
                if (string.Equals(rank, layer, StringComparison.Ordinal) && !ordered.Contains(rank)) ordered.Add(rank);
        // A layer this build does not know yet must stay reachable instead of
        // silently disappearing from the panel.
        foreach (var layer in layers)
            if (!string.IsNullOrEmpty(layer) && !ordered.Contains(layer)) ordered.Add(layer);
        return ordered.ToArray();
    }

    internal static string Short(string category) => category switch
    {
        "Ноды карты" => "Ноды",
        FandomGather => "Ценные",
        _ => category,
    };
}
