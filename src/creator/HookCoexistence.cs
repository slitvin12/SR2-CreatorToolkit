using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace CreatorToolkit;

// Soft coexistence with any other Harmony mod (rules: CoexistencePolicy). Tracks Creator Toolkit's
// own feature hooks (NativeHooks, InfiniteBuildHooks); the audit save guard is deliberately not
// tracked and never withdrawn. Only our patch methods are ever unpatched — by (original, our patch),
// never by owner wildcard — so foreign prefixes, postfixes and transpilers stay exactly as installed.
internal static class HookCoexistence
{
    private sealed class Entry
    {
        internal MethodBase Original;
        internal MethodInfo Patch;
        internal string Name, Target;
        internal bool Isolated;
    }
    private static readonly List<Entry> Entries = new();
    private static readonly Dictionary<string, CoexistencePolicy.FaultWindow> Windows = new(StringComparer.Ordinal);
    private static readonly Queue<string> Pending = new();
    private static HarmonyLib.Harmony _harmony;
    private static ToolkitMod _app;
    private static float _nextCensus = 15f;
    private static int _censusRuns;
    internal static int Tracked { get { lock (Entries) return Entries.Count; } }
    internal static int IsolatedCount { get; private set; }
    internal static int InstallFailures { get; private set; }
    internal static string Summary { get; private set; } = "pending";

    internal static void Configure(HarmonyLib.Harmony harmony, ToolkitMod app) { _harmony = harmony; _app = app; }

    internal static void Track(MethodBase original, MethodInfo patch)
    {
        if (original == null || patch == null) return;
        lock (Entries) Entries.Add(new Entry { Original = original, Patch = patch, Name = patch.Name, Target = (original.DeclaringType?.Name ?? "?") + "." + original.Name });
    }

    // Install failed: a missing game method, or a foreign patch made Harmony's rebuild throw.
    // Withdraw a half-applied patch of ours only; the original and foreign patches stay as they were.
    internal static void InstallFailed(MethodBase original, MethodInfo patch, Exception error)
    {
        InstallFailures++;
        if (original == null || patch == null || _harmony == null) return;
        try
        {
            var info = HarmonyLib.Harmony.GetPatchInfo(original);
            if (info != null && info.Prefixes.Concat(info.Postfixes).Any(p => p.owner == _harmony.Id && p.PatchMethod == patch))
                _harmony.Unpatch(original, patch);
            _app?.LoggerInstance.Warning("Coexistence: " + patch.Name + " on " + original.Name + " not installed (" + error?.GetType().Name +
                "); the feature keeps vanilla behaviour, other mods' patches are untouched.");
        }
        catch (Exception e) { _app?.LoggerInstance.Warning("Coexistence: could not withdraw half-applied " + patch.Name + ": " + e.Message); }
    }

    // Called from a Creator Toolkit hook body instead of letting the exception reach the shared
    // call chain (it would skip the original and every other mod's postfix). Returns true: run the
    // original — the vanilla path. Isolation itself happens on the next frame, outside the hook.
    internal static bool Fault(string patchName, Exception error)
    {
        try
        {
            lock (Windows)
            {
                Windows.TryGetValue(patchName ?? "", out var window);
                if (window.Register(Environment.TickCount64)) Pending.Enqueue(patchName ?? "");
                Windows[patchName ?? ""] = window;
            }
            _app?.Fail("hook " + patchName, error);
        }
        catch { /* A fault report must never become a second fault inside the hook. */ }
        return true;
    }

    internal static void Tick()
    {
        string name = null;
        lock (Windows) if (Pending.Count > 0) name = Pending.Dequeue();
        if (name != null) Isolate(name);
        float now = UnityEngine.Time.unscaledTime;
        // Starlight expansions and late mods patch after us: census once early, once a minute later.
        if (_censusRuns < 2 && now >= _nextCensus) { _censusRuns++; _nextCensus = now + 45f; Census(true); }
    }

    private static void Isolate(string patchName)
    {
        if (_harmony == null) return;
        string group = CoexistencePolicy.Group(patchName);
        List<Entry> victims;
        lock (Entries) victims = Entries.Where(e => !e.Isolated && CoexistencePolicy.Group(e.Name) == group).ToList();
        int removed = 0;
        foreach (var e in victims)
        {
            try { _harmony.Unpatch(e.Original, e.Patch); e.Isolated = true; removed++; }
            catch (Exception ex) { _app?.LoggerInstance.Warning("Coexistence: unpatch " + e.Name + " from " + e.Target + " failed: " + ex.Message); }
        }
        IsolatedCount += removed;
        if (group == CoexistencePolicy.InfiniteBuilderGroup) InfiniteBuildHooks.Withdraw("перехватчики отключены ради совместимости с другими модами", _app);
        string feature = CoexistencePolicy.Feature(patchName);
        _app?.LoggerInstance.Warning("Coexistence: " + patchName + " faulted " + CoexistencePolicy.Threshold + " times; withdrew " + removed +
            " Creator Toolkit patch(es) of group '" + group + "' (" + feature + "). Foreign patches were not touched.");
        _app?.Notify(CoexistencePolicy.IsolationNotice(feature));
        Census(false);
    }

    internal static void Census(bool log)
    {
        if (_harmony == null) return;
        try
        {
            List<Entry> snapshot;
            lock (Entries) snapshot = Entries.Where(e => !e.Isolated).ToList();
            var owners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int hooked = 0, shared = 0, transpilers = 0, cancels = 0;
            foreach (var original in snapshot.Select(e => e.Original).Distinct())
            {
                hooked++;
                HarmonyLib.Patches info;
                try { info = HarmonyLib.Harmony.GetPatchInfo(original); } catch { continue; }
                if (info == null) continue;
                var foreign = info.Owners.Where(o => o != _harmony.Id).Distinct().ToList();
                if (foreign.Count == 0) continue;
                shared++;
                foreach (var o in foreign) owners[o] = owners.TryGetValue(o, out int n) ? n + 1 : 1;
                transpilers += info.Transpilers.Count(p => p.owner != _harmony.Id);
                cancels += info.Prefixes.Count(p => p.owner != _harmony.Id && p.PatchMethod?.ReturnType == typeof(bool));
            }
            Summary = CoexistencePolicy.Summary(hooked, shared, owners, transpilers, cancels, IsolatedCount, InstallFailures);
            if (log) _app?.LoggerInstance.Msg("Coexistence: " + Summary);
        }
        catch (Exception e) { Summary = "census-error: " + e.GetType().Name; }
    }
}
