using System;
using Il2CppMonomiPark.SlimeRancher.Player.PlayerItems;
using UnityEngine;

namespace CreatorToolkit;

// «Свободная установка» for the native build mode (idea: Placement Improvements on Nexus).
// No Harmony: the game's own GadgetItemMetadata limits are leased while the option is on —
// no overlap layers (gadgets may clip into each other and the terrain), any slope, a long
// ground probe (overhangs, ledges) and a longer reach. Linked-portal ranges, no-build zones,
// costs and the save stay native. Everything is restored on switch-off, save change and quit.
internal sealed class FreePlacementTools
{
    private readonly ToolkitMod _app;
    private GadgetItemMetadata _meta;
    private float _dist, _slope, _grounded, _footprint, _floor;
    private int _layers;
    private float _newDist, _newFootprint;
    internal bool Enabled { get; private set; }
    internal float Range => Enabled ? _newDist : 0f;
    internal float VanillaRange => Enabled ? _dist : 0f;
    internal string Status { get; private set; } = CapturePolicy.FreePlacementLabel(false);
    internal FreePlacementTools(ToolkitMod app) => _app = app;

    internal void Set(bool enabled)
    {
        if (enabled == Enabled) return;
        if (enabled)
        {
            if (!_app.Ready || _app.Map.Busy) throw new InvalidOperationException("Сначала загрузите мир.");
            var meta = Resolve() ?? throw new InvalidOperationException("Настройки штатного строительства недоступны.");
            _meta = meta;
            _dist = meta.MaxValidPlacementDist; _slope = meta.MaxValidPlacementSlope; _grounded = meta.GroundedRaycastDistance;
            _footprint = meta.PlacementFootprintVisibleDist; _floor = meta.OverlapPlacementFloorTolerance; _layers = meta.GadgetOverlapLayers.value;
            _newDist = CapturePolicy.FreeRange(_dist);
            _newFootprint = Math.Max(_footprint, _newDist + 2f);
            try
            {
                meta.MaxValidPlacementDist = _newDist;
                meta.PlacementFootprintVisibleDist = _newFootprint;
                meta.MaxValidPlacementSlope = CapturePolicy.FreeSlope;
                meta.GroundedRaycastDistance = Math.Max(_grounded, CapturePolicy.FreeGroundedDistance);
                var none = meta.GadgetOverlapLayers; none.value = 0; meta.GadgetOverlapLayers = none;
                Enabled = true;
            }
            catch { Restore(); throw; }
            Status = CapturePolicy.FreePlacementLabel(true) + " · без проверки пересечений и уклона, дальность " + _dist.ToString("0") + " → " + _newDist.ToString("0") + " м";
        }
        else { Restore(); Status = CapturePolicy.FreePlacementLabel(false) + " · штатные проверки места"; }
        _app.Notify(Status);
    }

    private GadgetItemMetadata Resolve()
    {
        var item = _app.Gadgets.NativeItem;
        var meta = item != null && !item.WasCollected ? item.GadgetItemMetadata : null;
        if (meta != null && !meta.WasCollected) return meta;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<GadgetItemMetadata>())
            if (candidate != null && !candidate.WasCollected) return candidate;
        return null;
    }

    // Writes back only the values that are still ours; a value changed meanwhile is kept.
    private void Restore()
    {
        var meta = _meta;
        bool was = Enabled;
        Enabled = false; _meta = null;
        if (!was || meta == null || meta.WasCollected) return;
        if (meta.MaxValidPlacementDist == _newDist) meta.MaxValidPlacementDist = _dist;
        if (meta.PlacementFootprintVisibleDist == _newFootprint) meta.PlacementFootprintVisibleDist = _footprint;
        if (meta.MaxValidPlacementSlope == CapturePolicy.FreeSlope) meta.MaxValidPlacementSlope = _slope;
        if (meta.GroundedRaycastDistance == Math.Max(_grounded, CapturePolicy.FreeGroundedDistance)) meta.GroundedRaycastDistance = _grounded;
        if (meta.OverlapPlacementFloorTolerance != _floor) { /* never written by this tool */ }
        var layers = meta.GadgetOverlapLayers;
        if (layers.value == 0) { layers.value = _layers; meta.GadgetOverlapLayers = layers; }
    }

    internal void Reset() { if (Enabled) { Restore(); Status = CapturePolicy.FreePlacementLabel(false); } }

    internal string Diagnostics()
    {
        var meta = _meta;
        string live = meta == null || meta.WasCollected ? "none" :
            "dist=" + meta.MaxValidPlacementDist.ToString("0.0") + " slope=" + meta.MaxValidPlacementSlope.ToString("0") + " grounded=" + meta.GroundedRaycastDistance.ToString("0.0") +
            " footprint=" + meta.PlacementFootprintVisibleDist.ToString("0.0") + " layers=" + meta.GadgetOverlapLayers.value;
        return "FreePlacement: enabled=" + Enabled + " vanillaDist=" + _dist.ToString("0.0") + " vanillaSlope=" + _slope.ToString("0") + " vanillaLayers=" + _layers + " live=" + live + "\n";
    }
}
