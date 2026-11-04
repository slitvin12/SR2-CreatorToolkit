using System;

namespace CreatorToolkit;

// Pure, deterministic policies: no Unity, MelonLoader, network, or bridge dependencies.
internal static class SafetyPolicy
{
    internal const int MaxWaypoints = 5;
    internal const int AmmoFill = 100;
    internal const int RefillThreshold = 25;
    internal const int MaxSpawnBatch = 25;
    internal const float MaxReturnOffset = 3f;
    internal const float MaxReturnHeightDelta = 2f;
    internal static int SpawnCount(int count) => Math.Clamp(count, 1, MaxSpawnBatch);
    internal static float Speed(float speed) => float.IsFinite(speed) ? Math.Clamp(speed, 1f, PlayerMovementPolicy.MaxSpeed) : 1f;
    internal static bool NeedsRefill(int count) => count>0&&count<=RefillThreshold;
    // Refill point for a custom fill target (slot-limit mods): a quarter of it; vanilla 100 keeps 25.
    internal static int RefillAt(int target) => target==AmmoFill?RefillThreshold:Math.Max(1,target/4);
    internal static bool NeedsRefill(int count,int target) => count>0&&count<=RefillAt(target);
    internal static bool NearbyReturnOffset(float x,float y,float z) =>
        float.IsFinite(x)&&float.IsFinite(y)&&float.IsFinite(z)&&
        x*x+z*z<=MaxReturnOffset*MaxReturnOffset+.0001f&&Math.Abs(y)<=MaxReturnHeightDelta;
    /// Typed currency amount: digits with optional group separators; anything else is 0.
    internal static int ParseCurrency(string text) => TryParseCurrency(text, out int value) ? value : 0;
    /// 0.8.6: the same input for «Установить»: false for an empty field, signs and letters; «0» is a valid target.
    internal static bool TryParseCurrency(string text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        long parsed = 0; bool digits = false;
        foreach (char c in text)
        {
            if (c >= '0' && c <= '9') { digits = true; parsed = Math.Min(int.MaxValue, parsed * 10 + (c - '0')); }
            else if (c != ' ' && c != '\u00a0' && c != '\u202f' && c != '_' && c != ',' && c != '.' && c != '\'') return false;
        }
        if (!digits) return false;
        value = (int)parsed;
        return true;
    }
    /// 0.8.6: signed change that makes the balance exactly the target («Установить», «Обнулить»). The target is
    /// clamped to the native cap; a balance already above the cap (another mod) is never raised, only lowered.
    internal static int CurrencySetDelta(int current, int target, int maximum)
    {
        if (current < 0 || maximum < 0 || target < 0) return 0;
        long goal = Math.Min((long)target, Math.Max((long)maximum, current));
        return (int)(goal - current);
    }
    internal static int CurrencyDelta(int current, int requested, int maximum)
    {
        if (current < 0 || maximum < 0 || requested < 0) return 0;
        return (int)Math.Min((long)requested, Math.Max(0L, (long)maximum - current));
    }
    internal static double ForwardHour(double worldTime, double nextHour)
    {
        if (!double.IsFinite(worldTime) || !double.IsFinite(nextHour) || nextHour < worldTime)
            throw new ArgumentOutOfRangeException(nameof(nextHour), "Time must move forwards.");
        return nextHour;
    }
    internal static bool SameSession(IntPtr expected, IntPtr current) => expected != IntPtr.Zero && expected == current;
}
