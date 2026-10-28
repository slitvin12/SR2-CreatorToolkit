using System;
namespace CreatorToolkit;
// Pure motion: fractional wheel deltas, bounded position, no row snapping.
internal static class ScrollMotionPolicy
{
    internal static float Clamp(float value,float limit)=>!float.IsFinite(value)?0:Math.Clamp(value,0,float.IsFinite(limit)?Math.Max(0,limit):0);
    internal static float Wheel(float target,float delta,float row,float limit)=>
        !float.IsFinite(delta)||!float.IsFinite(row)||row<=0?Clamp(target,limit):Clamp(target+delta*row,limit);
    internal static float Step(float current,float target,float seconds,float limit)
    {
        current=Clamp(current,limit);target=Clamp(target,limit);
        if(!float.IsFinite(seconds)||seconds<=0)return current;
        float next=current+(target-current)*(1-(float)Math.Exp(-Math.Min(seconds,.1f)/.065f));
        return Math.Abs(target-next)<.05f?target:Clamp(next,limit);
    }
}
