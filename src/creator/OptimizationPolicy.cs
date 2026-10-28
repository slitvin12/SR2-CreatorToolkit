using System;

namespace CreatorToolkit;

// Pure policy; no Unity, filesystem, preferences, saves, native pointers or hooks.
internal readonly record struct OptimizationRequest(int Mip, int RenderPercent,
    int FoliagePercent, int ParticlePercent, bool Smooth)
{
    internal int ParticleMax {get;init;}
    internal OptimizationRequest Clamped() => new(OptimizationPolicy.Mip(Mip),
        OptimizationPolicy.Render(RenderPercent), OptimizationPolicy.Density(FoliagePercent),
        OptimizationPolicy.Density(ParticlePercent), Smooth){ParticleMax=M25RuntimePolicy.ParticleMax(ParticleMax)};
    internal static OptimizationRequest Full => new(0,100,100,100,false);
    internal static OptimizationRequest Potato => new(3,70,40,50,true);
}

internal static class OptimizationPolicy
{
    // Fail closed until this player/interop rendering path is validated offline.
    internal static bool NativeModelBatchWritesSupported=>false;
    internal static bool NativeRenderScalingSupported=>false;
    internal static bool NativeFoliageReductionSupported=>false;
    internal const int UiTextureBudget=512, CameraBudget=24, ParticleBudget=128, FoliageBudget=256;
    internal const int ActorBatch=8, OtherBatch=8, UploadMs=2, RenderersPerFrame=32, IoRequests=8;
    internal static int Mip(int value)=>Math.Clamp(value,0,4);
    internal static int Render(int value)=>Math.Clamp(value,50,100);
    internal static int Density(int value)=>Math.Clamp(value,10,100);
    internal static int Rounded(float value,int fallback,int minimum,int maximum)=>
        float.IsFinite(value)?Math.Clamp((int)MathF.Round(Math.Clamp(value,minimum,maximum)),minimum,maximum):fallback;
    internal static string MipLabel(int value)=>Mip(value) switch
    {0=>"Полный размер",1=>"½ исходного размера",2=>"¼ исходного размера",3=>"⅛ исходного размера",_=>"1/16 исходного размера"};
    internal static int SourceDimension(int dimension,int mip)=>
        dimension<=0?0:Math.Max(1,dimension>>Mip(mip));
    internal static int NativeBatch(int original,int maximum)=>
        original<=0?original:Math.Min(original,Math.Max(1,maximum));
    internal static int ParticleCap(int original,int percent)
    {
        if(original<=0)return 0;
        int minimum=Math.Min(16,original);
        return Math.Clamp((int)((long)original*Density(percent)/100),minimum,original);
    }
    internal static bool Owns(int current,int expected)=>current==expected;
    internal static bool Owns(float current,float expected)=>float.IsFinite(current)&&
        float.IsFinite(expected)&&Math.Abs(current-expected)<.0001f;
    internal static bool AmbientParticle(string name,bool collision,bool trigger)
    {
        if(collision||trigger)return false;
        string n=(name??"").ToLowerInvariant();
        foreach(string critical in new[]{"slime","tarr","damage","attack","danger","warning","hit","portal",
            "teleport","shock","lightning","electric","explosion","boom","hazard","lava","drone","beacon","marker","gadget"})
            if(n.Contains(critical))return false;
        foreach(string ambient in new[]{"ambient","dust","pollen","leaves","leaf","grass","wind",
            "cloud","snow","rain","waterfall","foam","sparkle","firefly","butterfly"})
            if(n.Contains(ambient))return true;
        return false;
    }
    internal static bool Foliage(string name,bool instanced,bool initialized)
    {
        if(!instanced||!initialized)return false;
        string n=(name??"").ToLowerInvariant();
        foreach(string critical in new[]{"gadget","food","fruit","veggie","harvest","carrot","lettuce","crop"})
            if(n.Contains(critical))return false;
        foreach(string visual in new[]{"grass","fern","foliage","bush","shrub","flower","leaves","leaf"})
            if(n.Contains(visual))return true;
        return false;
    }
}