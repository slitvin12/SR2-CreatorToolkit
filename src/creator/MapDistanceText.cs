using System;
using System.Collections.Generic;
using System.Globalization;

namespace CreatorToolkit;

// Metric distance text for tooltips and atlas rows. The game runs with the
// globalization-invariant runtime, where GetCultureInfo("ru-RU") throws and
// aborted the whole map overlay frame (no plaque, no panel, no markers past the
// first far object). The Russian decimal comma is therefore produced locally
// from the invariant formatter: no culture lookup remains on this path.
internal static class MapDistanceText
{
    // 0.8.5: lists, plaques and the compass ask for the same values every frame; each text is built
    // once (per whole metre) instead of two or three strings per call.
    private static readonly string[] Metres=new string[1001];
    private static readonly Dictionary<int,string> Kilometres=new();
    internal static string Format(float metres)
    {
        if(!float.IsFinite(metres)||metres<0)return "";
        if(metres<1000f)
        {
            int whole=(int)Math.Round(metres,MidpointRounding.ToEven);
            return Metres[whole]??=whole.ToString(CultureInfo.InvariantCulture)+" м";
        }
        int key=metres>=int.MaxValue?int.MaxValue:(int)metres;
        lock(Kilometres)
        {
            if(Kilometres.TryGetValue(key,out var cached))return cached;
            string kilometres=(metres/1000f).ToString("0.0",CultureInfo.InvariantCulture);
            string text=kilometres.Replace('.',',')+" км";
            if(Kilometres.Count>=4096)Kilometres.Clear();
            Kilometres[key]=text;
            return text;
        }
    }
}
