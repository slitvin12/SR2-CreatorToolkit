using System;

namespace CreatorToolkit;

// Owner request 2026-10-26: opt-in «Бесконечный режим строителя».
// Pure rules only. Inflation is a read-side view of the live gadget inventory;
// a spend is skipped only for one native placement unit of the held, known
// gadget. Real counts, the refinery, blueprints and the save are never rewritten.
internal static class InfiniteBuilderPolicy
{
    internal const int DisplayStock = 999;
    // Values in [Threshold, DisplayStock] that reach a write were derived from
    // the virtual 999, never from a real gadget stock below the threshold.
    internal const int Window = 128;
    internal const int Threshold = DisplayStock - Window;
    internal const string LabelOn = "Бесконечный режим строителя: ВКЛ";
    internal const string LabelOff = "Бесконечный режим строителя: ВЫКЛ";
    internal static string Label(bool enabled) => enabled ? LabelOn : LabelOff;
    // Known = learned blueprint (or already placed), or a really owned unit.
    internal static bool Known(bool learned, int real) => learned || real > 0;
    internal static int Display(bool enabled, bool known, int real) =>
        enabled && known && real >= 0 && real < Threshold ? DisplayStock : real;
    internal static bool ShouldInflate(bool enabled, int suppress, int scope, bool gadgetMode) =>
        enabled && suppress <= 0 && (scope > 0 || gadgetMode);
    // The native placement cost is exactly one unit of the held gadget. Moving an
    // already placed gadget (pick up + place again) keeps its native refund/spend pair.
    internal static bool SkipSpend(bool enabled, bool gadgetMode, bool pickedUp, int entries,
        int knownHeldEntries, int amount, int multiplier) =>
        enabled && gadgetMode && !pickedUp && entries == 1 && knownHeldEntries == 1 && amount == 1 && multiplier == 1;
    internal static bool LooksVirtual(int real, int written) =>
        real >= 0 && real < Threshold && written >= Threshold;
    // A write computed from the virtual stock: spends become no-ops, refunds keep
    // only their real (bounded) delta.
    internal static int TranslateVirtualWrite(int real, int written) =>
        written > DisplayStock ? real + Math.Min(written - DisplayStock, Window) : real;
    internal static bool CanPlaceFromToolkit(bool enabled, int real) => enabled || real > 0;
    internal static string Count(bool enabled, int real) => Display(enabled, true, real).ToString();
}
