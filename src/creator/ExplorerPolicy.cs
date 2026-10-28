using System;

namespace CreatorToolkit;

// Shared pure bounds; tests exercise these without loading Unity.
internal static class ExplorerPolicy
{
    internal const int MaxCustomWaypoints=32,MaxNameLength=48,ColorCount=6;
    internal const int MaxRadarTargets=32,MaxFarmDevices=64,FeedUnitsPerTick=4;
    internal const float RadarRadius=100f,FarmRadius=30f,RadarScanSeconds=1f,FarmScanSeconds=3f;
    // Native target name/diet occupy the centre of the screen through y=170.
    // Leave that area clear rather than painting navigation over game text.
    internal const float NavigationHudTop=176f,NavigationHudRowHeight=52f,NavigationHudStride=60f;
    internal static bool Coordinates(float x,float y,float z)=>
        float.IsFinite(x)&&float.IsFinite(y)&&float.IsFinite(z)&&Math.Abs(x)<100000&&Math.Abs(y)<100000&&Math.Abs(z)<100000;
    internal static bool Within(float squaredDistance,float radius)=>
        float.IsFinite(squaredDistance)&&squaredDistance>=0&&float.IsFinite(radius)&&radius>0&&squaredDistance<=radius*radius;
    internal static int DirectionIndex(float signedDegrees)
    {
        if(!float.IsFinite(signedDegrees))return 0;
        double angle=(signedDegrees%360+360)%360;
        return (int)Math.Floor((angle+22.5)/45)%8;
    }
    internal static bool Conserved(int sourceBefore,int sourceAfter,int destinationBefore,int destinationAfter)=>
        sourceBefore>0&&sourceAfter==sourceBefore-1&&destinationBefore>=0&&destinationBefore<int.MaxValue&&destinationAfter==destinationBefore+1;
    internal static bool RareMaterial(string nativeName)=>nativeName is
        "StrangeDiamondCraft" or "SunSapCraft" or "LavaDustCraft" or "PrimordyOilCraft";
}