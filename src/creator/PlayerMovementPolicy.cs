using System;

namespace CreatorToolkit;

internal static class PlayerMovementPolicy
{
    internal const float MaxSpeed=10f,MaxJumpHeight=5f;
    internal static float JumpHeight(float value)=>
        float.IsFinite(value)?Math.Clamp(value,1f,MaxJumpHeight):1f;
    // For unchanged native gravity, ballistic height follows v².
    // A height ×4 means launch velocity ×2, not ×4.
    internal static float JumpVelocityFactor(float height)=>MathF.Sqrt(JumpHeight(height));
    internal static bool PreserveSpend(int available,int requested)=>
        available>0&&requested>0&&requested<=available;
}