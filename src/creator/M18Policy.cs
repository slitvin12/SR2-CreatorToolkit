using System;
namespace CreatorToolkit;
internal static class M18Policy
{
    internal static bool HeavyEffect(string name)=>name is "ScreenSpaceAmbientOcclusion" or "ScreenSpaceReflection" or "ScreenSpaceGlobalIllumination" or "VolumetricClouds" or "ContactShadows" or "MicroShadowing" or "MotionBlur" or "DepthOfField" or "Bloom";
    internal static float LowLod(float original)=>float.IsFinite(original)&&original>0?Math.Min(original,Math.Max(float.Epsilon,original/3f)):throw new ArgumentOutOfRangeException(nameof(original));
    internal static float FarClip(float original,float near)=>float.IsFinite(original)&&float.IsFinite(near)&&near>=0&&original>near+1?Math.Min(original,Math.Max(near+1,90)):throw new ArgumentOutOfRangeException(nameof(original));
}
