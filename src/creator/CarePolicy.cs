using System;

namespace CreatorToolkit;

// Pure bounds. Inspection never creates objects; one-shot feeding spends real
// selected-slot portions, and native hunger/eating rules remain authoritative.
internal static class CarePolicy
{
    internal const float Radius = 25f;
    internal const int MaxTargets = 32;
    internal const int MaxExamined = 4096;
    internal const int MaxMeals = 8;
    internal const float ScanSeconds = 1f;
    internal const float FeedCooldown = 2f;
    internal const int MaxGadgets = 512;
    internal static bool Within(float squaredDistance, float radius = Radius) =>
        float.IsFinite(squaredDistance) && float.IsFinite(radius) &&
        squaredDistance >= 0 && radius > 0 && radius <= Radius &&
        squaredDistance <= radius * radius;
    internal static bool Surface(float normalY, float maxSlope) =>
        float.IsFinite(normalY) && float.IsFinite(maxSlope) && normalY > 0 &&
        maxSlope >= 0 && maxSlope <= 60 &&
        normalY + .00001f >= MathF.Cos(maxSlope * MathF.PI / 180f);
    // Native GadgetItemMetadata measures inclination from the horizontal:
    // 90 = straight up only, 0 = sideways allowed, -90 = unrestricted.
    // It is NOT the maximum deviation of the normal from the vertical.
    // Keep our additional <=45-degree terrain limit as well as the native
    // sine threshold. IsPlacementValid remains the final authority.
    internal static bool NativePlacementSurface(float normalY,float nativeInclination) =>
        float.IsFinite(nativeInclination) && nativeInclination >= -90f && nativeInclination <= 90f &&
        Surface(normalY,45f) && normalY <= 1.00001f &&
        normalY >= MathF.Sin(nativeInclination * MathF.PI / 180f);
    internal static bool SpentOne(int before, int after) => before > 0 && after == before - 1;
    internal static int MealBudget(int count) => Math.Clamp(count, 0, MaxMeals);
}