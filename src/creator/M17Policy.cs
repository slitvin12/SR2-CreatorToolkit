using System;
namespace CreatorToolkit;
internal static class M17Policy
{
 internal static float FarClip(float original,float near)=>float.IsFinite(original)&&float.IsFinite(near)&&original>near?Math.Max(near+1,Math.Min(original,180f)):original;
}
