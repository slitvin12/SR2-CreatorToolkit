using System;
namespace CreatorToolkit;
[Flags]
internal enum M19Effects {None=0,AO=1,Reflections=2,IndirectLight=4,Clouds=8,ContactShadows=16,MicroShadows=32,MotionBlur=64,DepthOfField=128,Bloom=256,All=511}
internal readonly record struct M19GraphicsRequest(int LodPercent,int Distance,bool HideDecor,bool DisableShadows,M19Effects DisabledEffects)
{
    internal static M19GraphicsRequest Full=>new(100,0,false,false,M19Effects.None);
    internal static M19GraphicsRequest Maximum=>new(25,90,true,true,M19Effects.All);
    internal M19GraphicsRequest Clamped()=>new(Math.Clamp(LodPercent,10,100),Distance<=0?0:Math.Clamp(Distance,40,1000),HideDecor,DisableShadows,DisabledEffects&M19Effects.All);
    internal bool Any=>LodPercent<100||Distance>0||HideDecor||DisableShadows||DisabledEffects!=M19Effects.None;
}
internal static class M19GraphicsPolicy
{
    internal static M19Effects Effect(string name)=>name switch{
        "ScreenSpaceAmbientOcclusion"=>M19Effects.AO,"ScreenSpaceReflection"=>M19Effects.Reflections,
        "ScreenSpaceGlobalIllumination"=>M19Effects.IndirectLight,"VolumetricClouds"=>M19Effects.Clouds,
        "ContactShadows"=>M19Effects.ContactShadows,"MicroShadowing"=>M19Effects.MicroShadows,
        "MotionBlur"=>M19Effects.MotionBlur,"DepthOfField"=>M19Effects.DepthOfField,"Bloom"=>M19Effects.Bloom,_=>M19Effects.None};
    internal static bool Disable(string name,M19Effects mask){var bit=Effect(name);return bit!=M19Effects.None&&(mask&bit)!=0;}
    internal static float Lod(float before,int percent)
    {
        if(!float.IsFinite(before)||before<=0)throw new ArgumentOutOfRangeException(nameof(before));
        return Math.Min(before,Math.Max(float.Epsilon,(float)((double)before*Math.Clamp(percent,10,100)/100d)));
    }
    internal static float FarClip(float before,float near,int distance)
    {
        if(!float.IsFinite(before)||!float.IsFinite(near)||near<0||before<=near+1)throw new ArgumentOutOfRangeException(nameof(before));
        return distance<=0?before:Math.Min(before,Math.Max(near+1,Math.Clamp(distance,40,1000)));
    }
}
