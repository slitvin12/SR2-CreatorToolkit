using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Player;
using MelonLoader;

namespace CreatorToolkit;

// Runtime side of ModInteropPolicy: finds known mods among loaded melons and assemblies (by name,
// reflection only — never a compile-time reference) and whether anything else patches the vacpack
// slot capacity getter (Vacuum Modifications, More Vaccables, Ultimate Vacpack Overhaul or any
// other slot-limit mod). Read-only: never patches, unpatches or calls into the other mods.
internal static class ModInterop
{
    private static ToolkitMod _app;
    private static float _next = float.MinValue, _nextLog;
    private static int _logs;
    private static bool _custom;
    private static List<ModInteropPolicy.Known> _detected = new();

    internal static string Summary { get; private set; } = "pending";
    internal static IReadOnlyList<ModInteropPolicy.Known> Detected { get { Refresh(false); return _detected; } }
    internal static bool CustomVacLimits { get { Refresh(false); return _custom; } }

    internal static void Configure(ToolkitMod app) => _app = app;

    /// Mods load and patch after us (Starlight expansions even later): log once early and once later.
    internal static void Tick()
    {
        float now = UnityEngine.Time.unscaledTime;
        if (_logs >= 2 || now < _nextLog) return;
        _logs++; _nextLog = now + 45f;
        Refresh(true);
        _app?.LoggerInstance.Msg("Interop: " + Summary);
    }

    internal static void Refresh(bool force)
    {
        float now;
        try { now = UnityEngine.Time.unscaledTime; } catch { now = 0f; }
        if (!force && now < _next) return;
        _next = now + 30f;
        try
        {
            var names = new List<string>();
            foreach (var m in MelonBase.RegisteredMelons)
            {
                try { names.Add(m.Info?.Name); names.Add(m.GetType().Assembly.GetName().Name); } catch { }
            }
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { if (!a.IsDynamic) names.Add(a.GetName().Name); } catch { }
            }
            var detected = ModInteropPolicy.Detect(names);
            var owners = new List<string>();
            try
            {
                var getter = AccessTools.PropertyGetter(typeof(AmmoSlot), nameof(AmmoSlot.MaxCount));
                var info = getter == null ? null : HarmonyLib.Harmony.GetPatchInfo(getter);
                string own = _app?.HarmonyInstance?.Id;
                if (info != null) owners.AddRange(info.Owners.Where(o => o != own));
            }
            catch { }
            _detected = detected;
            _custom = ModInteropPolicy.CustomLimits(detected.Any(k => (k.Roles & ModInteropPolicy.Role.VacLimits) != 0), owners.Count);
            Summary = ModInteropPolicy.Summary(detected, _custom, owners);
        }
        catch (Exception e) { Summary = "interop-error: " + e.GetType().Name; }
    }

    /// How many items «Заполнить» / auto refill put into this slot.
    internal static int FillTarget(AmmoSlot slot)
    {
        bool custom = CustomVacLimits;
        int max = 0;
        if (custom && slot != null)
        {
            try { max = slot.MaxCount; } catch { max = 0; }
        }
        return ModInteropPolicy.FillTarget(max, custom);
    }

    internal static string FillText => ModInteropPolicy.FillText(CustomVacLimits);
    internal static string RefillText => ModInteropPolicy.RefillText(CustomVacLimits);
}
