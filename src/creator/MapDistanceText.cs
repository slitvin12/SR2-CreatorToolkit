using System;
using System.Globalization;

namespace CreatorToolkit;

// Metric distance text for tooltips and atlas rows. The game runs with the
// globalization-invariant runtime, where GetCultureInfo("ru-RU") throws and
// aborted the whole map overlay frame (no plaque, no panel, no markers past the
// first far object). The Russian decimal comma is therefore produced locally
// from the invariant formatter: no culture lookup remains on this path.
internal static class MapDistanceText
{
    internal static string Format(float metres)
    {
        if(!float.IsFinite(metres)||metres<0)return "";
        if(metres<1000f)
            return ((int)Math.Round(metres,MidpointRounding.ToEven)).ToString(CultureInfo.InvariantCulture)+" м";
        string kilometres=(metres/1000f).ToString("0.0",CultureInfo.InvariantCulture);
        return kilometres.Replace('.',',')+" км";
    }
}
