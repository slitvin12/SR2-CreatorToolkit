using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CreatorToolkit;

// Third-party Slime Rancher 2 mods Creator Toolkit knows and adapts to: Nexus Mods entries whose
// hooks were checked against ours, plus the three mods with official support in Ranching Together
// (Vacuum Modifications, Placement Improvements, Better Drones). Pure data and rules — no Unity,
// MelonLoader or Harmony. Detection is by MelonInfo name or assembly name only: Creator Toolkit
// never references another mod at compile time, so a missing or updated mod cannot break loading.
internal static class ModInteropPolicy
{
    [Flags]
    internal enum Role
    {
        None = 0,
        VacLimits = 1,       // own vacpack slot capacity (AmmoSlot.MaxCount) — fill/refill follow it
        GodMode = 2,         // blocks player damage, same as «Бессмертие»
        InfiniteEnergy = 4,  // blocks energy spending, same as «Бесконечная энергия»
        Placement = 8,       // changes native gadget placement; our gadget tool goes through the native GadgetItem
        Drones = 16,
        Hud = 32,
        Time = 64,
        Multiplayer = 128,
        Framework = 256,
    }

    internal sealed class Known
    {
        internal string Name = "";
        internal int Nexus;
        internal string[] Aliases = Array.Empty<string>();
        internal Role Roles;
        internal bool RanchingTogether; // «Official Mod Support» in Ranching Together
        internal string Url => "https://www.nexusmods.com/slimerancher2/mods/" + Nexus;
    }

    private static Known K(string name, int nexus, Role roles, bool rt, params string[] aliases) =>
        new() { Name = name, Nexus = nexus, Roles = roles, RanchingTogether = rt, Aliases = aliases ?? Array.Empty<string>() };

    internal static readonly IReadOnlyList<Known> All = new[]
    {
        K("Vacuum Modifications", 45, Role.VacLimits, true, "VacuumModifications"),
        K("Placement Improvements", 179, Role.Placement, true, "BuildImprovements", "nl.lunarsnail.buildimprovements"),
        K("Better Drones", 177, Role.Drones, true, "BetterDrones", "de.pyeight.betterdrones"),
        K("More Vaccables", 42, Role.VacLimits, false, "MoreVaccablesMod", "MoreVaccables"),
        K("Ultimate Vacpack Overhaul", 145, Role.VacLimits, false, "UltimateVacpackOverhaul"),
        K("Infinite Health", 57, Role.GodMode, false, "InfiniteHealth"),
        K("Infinite Energy", 25, Role.InfiniteEnergy, false, "InfiniteEnergy"),
        K("Dynamic HUD", 123, Role.Hud, false, "DynamicHud"),
        K("Cube Slimes", 14, Role.Time, false, "CubeSlimes"),
        K("Ranching Together", 118, Role.Multiplayer, false, "SR2MP", "Multiplayer - Ranching Together"),
        K("Starlight", 60, Role.Framework, false, "SR2E", "Starlight (formerly SR2E)"),
    };

    /// «Vacuum Modifications», «VacuumModifications» and «vacuum-modifications» are the same key.
    internal static string Norm(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var b = new StringBuilder(s.Length);
        foreach (char c in s) if (char.IsLetterOrDigit(c)) b.Append(char.ToLowerInvariant(c));
        return b.ToString();
    }

    internal static Known Find(string nameOrAssembly)
    {
        string n = Norm(nameOrAssembly);
        if (n.Length == 0) return null;
        foreach (var k in All)
            if (Norm(k.Name) == n || k.Aliases.Any(a => Norm(a) == n)) return k;
        return null;
    }

    internal static List<Known> Detect(IEnumerable<string> names) =>
        (names ?? Enumerable.Empty<string>()).Select(Find).Where(k => k != null).Distinct()
            .OrderBy(k => k.Name, StringComparer.Ordinal).ToList();

    // Vacpack fill target. Vanilla keeps the historical 100 (may exceed native capacity on purpose);
    // with a slot-limit mod (known by name, or anything else patching AmmoSlot.MaxCount) the slot's
    // own MaxCount wins, so the cheat neither undercuts a raised limit nor overshoots a lowered one.
    internal const int MaxCustomFill = 9999;
    internal static bool CustomLimits(bool knownLimitMod, int foreignMaxCountPatches) => knownLimitMod || foreignMaxCountPatches > 0;
    internal static int FillTarget(int maxCount, bool customLimits) =>
        customLimits && maxCount > 0 ? Math.Min(maxCount, MaxCustomFill) : SafetyPolicy.AmmoFill;
    internal static string FillText(bool customLimits) => customLimits ? "до лимита слота" : SafetyPolicy.AmmoFill + " шт.";
    internal static string RefillText(bool customLimits) =>
        customLimits ? "¼ лимита → лимит слота" : SafetyPolicy.RefillThreshold + " → " + SafetyPolicy.AmmoFill + " шт.";

    internal static string Tags(Known k)
    {
        if (k == null) return "";
        var t = new List<string>();
        if ((k.Roles & Role.VacLimits) != 0) t.Add("vac-limits");
        if ((k.Roles & Role.GodMode) != 0) t.Add("same-as-god-mode");
        if ((k.Roles & Role.InfiniteEnergy) != 0) t.Add("same-as-infinite-energy");
        if ((k.Roles & Role.Placement) != 0) t.Add("native-placement");
        if (k.RanchingTogether) t.Add("RT");
        return t.Count == 0 ? "" : "(" + string.Join(",", t) + ")";
    }

    /// One diagnostics line: «mods=[Better Drones(RT), Vacuum Modifications(vac-limits,RT)] vac-limits=slot [owner]».
    internal static string Summary(IEnumerable<Known> detected, bool customLimits, IEnumerable<string> limitOwners)
    {
        var d = (detected ?? Enumerable.Empty<Known>()).Where(k => k != null).Distinct().OrderBy(k => k.Name, StringComparer.Ordinal).ToList();
        var owners = (limitOwners ?? Enumerable.Empty<string>()).Where(o => !string.IsNullOrWhiteSpace(o))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(o => o, StringComparer.OrdinalIgnoreCase).ToList();
        string mods = d.Count == 0 ? "none" : string.Join(", ", d.Select(k => k.Name + Tags(k)));
        return "mods=[" + mods + "] vac-limits=" + (customLimits ? "slot" + (owners.Count > 0 ? " [" + string.Join(", ", owners) + "]" : "") : "vanilla");
    }
}
