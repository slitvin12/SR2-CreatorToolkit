using System;
using System.Collections.Generic;

namespace CreatorToolkit;

// Screen-space map markers. Every projected object is drawn exactly on its own
// map position: nothing is merged, clustered or displaced, so an icon always sits
// inside its true pixel. All icons share one fixed player-chosen size; where
// neighbours land on the same pixel the icons simply overlap, because honest
// coordinates matter more than a tidy picture.
internal static class MapOverlayLayout
{
    internal const float MinSize = 14f;
    internal const float MaxSize = 32f;
    internal const float DefaultSize = 22f;
    internal const int NeighbourCell = 50;
    internal const int NeighbourChecks = 24;

    internal sealed class Marker<T>
    {
        internal readonly T Item;
        internal readonly float X, Y;    // exact projected map position
        internal readonly float Size;    // one fixed size for the whole atlas
        internal readonly bool Crowded;  // shares its pixel with a neighbour
        internal Marker(float x, float y, float size, bool crowded, T item)
        { X = x; Y = y; Size = size; Crowded = crowded; Item = item; }
        internal float Radius => Size * .5f;
        internal float HitBox => Size;
    }

    internal static bool Valid(float x, float y) =>
        float.IsFinite(x) && float.IsFinite(y) && Math.Abs(x) <= 10000000f && Math.Abs(y) <= 10000000f;

    internal static List<Marker<T>> Place<T>(IEnumerable<(float X, float Y, T Item)> points, float baseSize)
    {
        float icon = Clamp(baseSize);
        var markers = new List<Marker<T>>();
        var cells = new Dictionary<(int X, int Y), List<Marker<T>>>();
        foreach (var point in points)
        {
            if (!Valid(point.X, point.Y)) continue;
            var marker = new Marker<T>(point.X, point.Y, icon, !Fits(cells, point.X, point.Y, icon), point.Item);
            markers.Add(marker);
            var cell = Cell(point.X, point.Y);
            if (!cells.TryGetValue(cell, out var bucket)) cells[cell] = bucket = new List<Marker<T>>();
            bucket.Add(marker);
        }
        return markers;
    }

    private static bool Fits<T>(Dictionary<(int X, int Y), List<Marker<T>>> cells, float x, float y, float size)
    {
        int checks = NeighbourChecks;
        var own = Cell(x, y);
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (!cells.TryGetValue((own.X + dx, own.Y + dy), out var bucket)) continue;
            for (int i = 0; i < bucket.Count; i++)
            {
                float ox = x - bucket[i].X, oy = y - bucket[i].Y;
                if (ox * ox + oy * oy < size * size) return false;
                if (--checks <= 0) return true;
            }
        }
        return true;
    }

    private static (int X, int Y) Cell(float x, float y) =>
        ((int)Math.Floor(x / NeighbourCell), (int)Math.Floor(y / NeighbourCell));

    private static float Clamp(float size) =>
        !float.IsFinite(size) ? DefaultSize : size < MinSize ? MinSize : size > MaxSize ? MaxSize : size;
}
