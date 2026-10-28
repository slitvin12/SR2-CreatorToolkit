using System;
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
    internal static bool Allowed(float rate)=>rate==.25f||rate==1f||rate==5f;
    internal static bool Matches(float actual,float expected)=>float.IsFinite(actual)&&Math.Abs(actual-expected)<.00001f;
    // TimeDirector.Pause() remembers the Unity scale that was current when the game paused and
    // Unpause() restores it. If that remembered value is our temporary ×0.25/×5, hand the native
    // pause the ordinary tempo instead; the pause itself (current scale) is never touched.
    internal static bool ShouldRepairSavedScale(float current,float rate,bool paused,float saved)=>
        paused&&rate!=1f&&Allowed(rate)&&!Matches(current,rate)&&Matches(saved,rate);
}
