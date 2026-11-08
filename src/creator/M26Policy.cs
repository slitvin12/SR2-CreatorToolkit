using System;
using System.Globalization;
namespace CreatorToolkit;
// Pure limits shared by UI and runtime. No native objects or save writes.
internal static class M26RefineryPolicy
{
    internal static int Cap(int nativeMax)=>nativeMax<=0?throw new ArgumentOutOfRangeException(nameof(nativeMax)):Math.Min(999,nativeMax);
    internal static int Addition(int before,int nativeMax,int room,bool fill)
    {
        if(before<0||room<0)throw new ArgumentOutOfRangeException();
        int remaining=Math.Max(0,Cap(nativeMax)-before);
        return Math.Min(room,fill?remaining:Math.Min(100,remaining));
    }
}
internal static class M26ListPolicy
{
    internal const float BottomPadding=16f;
    internal static float Extent(float content)=>!float.IsFinite(content)||content<0?BottomPadding:content+BottomPadding;
    internal static float CareButtonWidth(float width)=>Math.Max(0,(width-24)/3);
}
internal static class M26RatePolicy
{
    // 0.8.7: any finite rate from ×0.05 to ×5 (slider and presets); ×1 is the ordinary tempo.
    internal const float MinRate=.05f,MaxRate=5f;
    internal static bool Allowed(float rate)=>float.IsFinite(rate)&&rate>=MinRate-.00001f&&rate<=MaxRate+.00001f;
    // Hundredths below ×1, 0.05 steps above; within 3 % of ×1 snaps to exactly ×1.
    internal static float Snap(float rate)
    {
        if(!float.IsFinite(rate))return 1f;
        rate=Math.Clamp(rate,MinRate,MaxRate);
        if(Math.Abs(rate-1f)<.03f)return 1f;
        double snapped=rate<1f?Math.Round(rate,2,MidpointRounding.AwayFromZero):Math.Round(rate*20.0,MidpointRounding.AwayFromZero)/20.0;
        return Math.Clamp((float)snapped,MinRate,MaxRate);
    }
    // Logarithmic slider: slow motion gets most of the travel, ×1 sits at ~65 %.
    internal static float ToSlider(float rate)=>(float)((Math.Log(Math.Clamp(float.IsFinite(rate)?rate:1f,MinRate,MaxRate))-Math.Log(MinRate))/(Math.Log(MaxRate)-Math.Log(MinRate)));
    internal static float FromSlider(float t)=>(float)Math.Exp(Math.Log(MinRate)+Math.Clamp(float.IsFinite(t)?t:0f,0f,1f)*(Math.Log(MaxRate)-Math.Log(MinRate)));
    // Slow motion shortens the physics step by the same factor (the same physics steps per real
    // second: smooth rigidbodies); acceleration keeps the native step (never coarser physics).
    internal static float FixedStep(float nativeStep,float rate)=>
        !float.IsFinite(nativeStep)||nativeStep<=0f||!Allowed(rate)||rate>=1f?nativeStep:Math.Max(.0005f,nativeStep*rate);
    internal static string Label(float rate)=>rate.ToString("0.##",CultureInfo.InvariantCulture);
    internal static bool Matches(float actual,float expected)=>float.IsFinite(actual)&&Math.Abs(actual-expected)<.00001f;
    // TimeDirector.Pause() remembers the Unity scale that was current when the game paused and
    // Unpause() restores it. If that remembered value is our temporary ×0.25/×5, hand the native
    // pause the ordinary tempo instead; the pause itself (current scale) is never touched.
    internal static bool ShouldRepairSavedScale(float current,float rate,bool paused,float saved)=>
        paused&&rate!=1f&&Allowed(rate)&&!Matches(current,rate)&&Matches(saved,rate);
}
