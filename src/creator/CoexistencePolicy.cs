using System;
using System.Collections.Generic;
using System.Linq;

namespace CreatorToolkit;

// Pure rules behind HookCoexistence (no Unity, no Harmony): offline-tested in CoexistenceRegression.
// MelonLoader 0.7 ships HarmonyX: every prefix of every mod runs; a prefix returning false only
// skips the original method (and transpilers on it), postfixes always run. Creator Toolkit relies
// on that and keeps its side of the contract:
//  • prefixes/postfixes only — never transpilers, finalizers, PatchAll or UnpatchAll;
//  • its hooks run last (Priority.Last), so other mods see vanilla arguments/results first and a
//    cancel happens only while a user-enabled feature (god mode, freeze time, …) is on;
//  • a hook body never throws into the shared call chain: a fault takes the vanilla path;
//  • repeated faults isolate only Creator Toolkit's own patch on that method, never a foreign one.
internal static class CoexistencePolicy
{
    internal const int Threshold = 3;          // faults inside the window before our patch is withdrawn
    internal const long WindowMs = 60_000;
    internal const string InfiniteBuilderGroup = "infinite-builder";
    // SR2 Mod Manager reads this AssemblyMetadata (see src/modmanager/README.md).
    internal const string HookPolicyKey = "SR2ModManager.HookPolicy", HookPolicyValue = "cooperative";

    internal struct FaultWindow
    {
        internal long StartMs;
        internal int Count;
        /// True exactly once: when this fault reaches the threshold inside the window.
        internal bool Register(long nowMs)
        {
            if (Count == 0 || nowMs - StartMs > WindowMs || nowMs < StartMs) { StartMs = nowMs; Count = 0; }
            Count++;
            return Count == Threshold;
        }
    }

    // InfiniteBuildHooks keep real-count barriers and the virtual view consistent only as a set:
    // one isolated hook withdraws the whole group and switches the mode off.
    private static readonly string[] Builder = { "RealBegin", "RealEnd", "SpendBegin", "SpendEnd", "CountWrite", "CountRead", "ScopeBegin", "ScopeEnd" };
    internal static string Group(string patch) => Array.IndexOf(Builder, patch) >= 0 ? InfiniteBuilderGroup : patch ?? "";

    internal static string Feature(string patch) => patch switch
    {
        "DamageGuard" or "HealthGuard" => "Бессмертие",
        "EnergyGuard" or "EnergyResourceGuard" => "Бесконечная энергия",
        "ClockGuard" => "Заморозка времени",
        "SlimeAiGuard" => "Заморозка слаймов",
        "MenuInputMode" => "Управление меню Creator Toolkit",
        "VacpackSlotSpend" or "VacpackIndexSpend" or "VacpackTypeSpend" => "Бесконечный инвентарь",
        "LoadingBudget" => "Плавная загрузка",
        _ when Group(patch) == InfiniteBuilderGroup => "Бесконечный режим строителя",
        _ => "Функция Creator Toolkit",
    };

    internal static string IsolationNotice(string feature) =>
        "Совместимость: «" + feature + "» отключена — её перехватчик сбоил рядом с другим модом. Убран только перехватчик Creator Toolkit, чужие моды не затронуты.";

    /// One diagnostics line: how many of our hooked methods are shared, with whom, and what we withdrew.
    internal static string Summary(int hooked, int shared, IEnumerable<KeyValuePair<string, int>> owners, int foreignTranspilers, int foreignCancels, int isolated, int installFailures)
    {
        var list = (owners ?? Enumerable.Empty<KeyValuePair<string, int>>()).Where(o => !string.IsNullOrWhiteSpace(o.Key))
            .OrderByDescending(o => o.Value).ThenBy(o => o.Key, StringComparer.OrdinalIgnoreCase).Select(o => o.Key + "×" + o.Value).ToList();
        return "hooked=" + hooked + " shared=" + shared + (list.Count > 0 ? " [" + string.Join(", ", list) + "]" : "") +
               " foreign-transpilers=" + foreignTranspilers + " foreign-cancels=" + foreignCancels + " isolated=" + isolated + " install-failures=" + installFailures;
    }
}
