using System;

namespace CreatorToolkit;

// Aurora's internal trigger window may be the OFF window. Preserve the native
// target state instead of treating every "_triggerStartHour" as night/ON.
internal static class PhenomenaWindowPolicy
{
    internal static bool ActiveHours(float triggerStart,float triggerEnd,string triggerState,out float on,out float off)
    {
        on=off=0;
        if(!float.IsFinite(triggerStart)||!float.IsFinite(triggerEnd)||triggerStart<0||triggerStart>24||
            triggerEnd<0||triggerEnd>24||Math.Abs(triggerStart-triggerEnd)<.001f)return false;
        if(triggerState=="ON"){on=triggerStart;off=triggerEnd;return true;}
        if(triggerState=="OFF"){on=triggerEnd;off=triggerStart;return true;}
        return false;
    }
    internal static float After(float hour)=>((hour%24)+24+.02f)%24;
}