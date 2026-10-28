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
