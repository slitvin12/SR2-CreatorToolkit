using System;

namespace CreatorToolkit;

internal static class StressComparisonPlan
{
    internal const int TotalReloads=12;
    private static void Guard(int index)
    {if(index<0||index>=TotalReloads)throw new ArgumentOutOfRangeException(nameof(index));}
    internal static bool Armed(int index)
    {Guard(index);return index>=3&&index<9;}
    internal static string Name(int index)
    {
        Guard(index);
        string block=index<3?"A1_off":index<6?"B1_on":index<9?"B2_on":"A2_off";
        return "control_"+block+"_"+(index%3+1);
    }
}