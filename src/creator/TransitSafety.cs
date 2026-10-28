using System;

namespace CreatorToolkit;

// Pure, allocation-free policy shared with offline tests. A single ray hit,
// elapsed time since pressing a button, or a changed scene-model ID is NOT ready.
internal sealed class TransitSafety
{
    internal const float CollisionWarmupSeconds = .75f;
    internal const float PlacementSettleSeconds = .25f;
    internal const float QueryIntervalSeconds = .1f;
    internal const float LoadDeadlineSeconds = 45f;
    internal const float MaximumGroundDrift = .18f;
    private float _since = float.NaN, _x, _y, _z;
    private int _samples;
    internal int Samples => _samples;
    internal float StableSeconds(float now) => float.IsNaN(_since) ? 0f : Math.Max(0f, now - _since);

    internal bool Observe(bool ready, float now, float x, float y, float z)
    {
        if (!ready || !float.IsFinite(now) || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
        { Reset(); return false; }
        float dx = x - _x, dy = y - _y, dz = z - _z;
        if (float.IsNaN(_since) || now < _since || dx * dx + dy * dy + dz * dz > MaximumGroundDrift * MaximumGroundDrift)
        { _since = now; _samples = 0; _x = x; _y = y; _z = z; }
        // Compare against the beginning of this stable window, not just the
        // previous sample: a slowly drifting platform is not stable ground.
        _samples++;
        return _samples >= 4 && now - _since >= CollisionWarmupSeconds;
    }

    internal void Reset() { _since = float.NaN; _samples = 0; _x = _y = _z = 0f; }
}