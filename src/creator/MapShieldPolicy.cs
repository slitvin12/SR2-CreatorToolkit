using System;
using System.Collections.Generic;

namespace CreatorToolkit;

// Pure, top-left screen-pixel policy: never manufacture a background/panel hit area.
internal static class MapShieldPolicy
{
    internal const int MaxSources = 512, MaxExclusions = 64, MaxPieces = 2048;
    internal readonly struct Box
    {
        internal readonly float X, Y, Width, Height;
        internal Box(float x,float y,float width,float height)
        {X=x;Y=y;Width=width;Height=height;}
        internal float Right=>X+Width;
        internal float Bottom=>Y+Height;
        internal bool Valid=>float.IsFinite(X)&&float.IsFinite(Y)&&float.IsFinite(Width)&&float.IsFinite(Height)&&
            Width>0&&Height>0&&float.IsFinite(Right)&&float.IsFinite(Bottom);
    }
    internal static bool TryClip(Box source,Box bounds,out Box clipped)
    {
        clipped=default;
        if(!source.Valid||!bounds.Valid)return false;
        float x=Math.Max(source.X,bounds.X),y=Math.Max(source.Y,bounds.Y);
        float right=Math.Min(source.Right,bounds.Right),bottom=Math.Min(source.Bottom,bounds.Bottom);
        clipped=new Box(x,y,right-x,bottom-y);
        return clipped.Valid;
    }
    // Invalid exclusions or pathological workloads fail open (no native UI blocked).
    // Subtract, do not enlarge or merge: all output remains a subset of source pixels.
    internal static List<Box> Build(IReadOnlyList<Box> sources,IReadOnlyList<Box> exclusions,Box viewport)
    {
        var result=new List<Box>();
        if(!viewport.Valid||sources==null||exclusions==null||sources.Count>MaxSources||exclusions.Count>MaxExclusions)return result;
        foreach(var exclusion in exclusions)if(!exclusion.Valid)return result;
        foreach(var source in sources)
        {
            if(!TryClip(source,viewport,out var clipped))continue;
            var pieces=new List<Box>{clipped};
            foreach(var exclusion in exclusions)
            {
                var next=new List<Box>();
                foreach(var piece in pieces)
                {
                    if(!TryClip(piece,exclusion,out var overlap)){next.Add(piece);continue;}
                    Add(next,new Box(piece.X,piece.Y,piece.Width,overlap.Y-piece.Y));
                    Add(next,new Box(piece.X,overlap.Bottom,piece.Width,piece.Bottom-overlap.Bottom));
                    Add(next,new Box(piece.X,overlap.Y,overlap.X-piece.X,overlap.Height));
                    Add(next,new Box(overlap.Right,overlap.Y,piece.Right-overlap.Right,overlap.Height));
                }
                if(next.Count+result.Count>MaxPieces)return new List<Box>();
                pieces=next;
                if(pieces.Count==0)break;
            }
            if(result.Count+pieces.Count>MaxPieces)return new List<Box>();
            result.AddRange(pieces);
        }
        return result;
    }
    // The same exclusions protect native raycasts AND reject mod actions in
    // those holes. Unknown layout disables mod actions without blocking native UI.
    internal static bool AllowModPointer(bool controlsKnown,IReadOnlyList<Box> controls,float x,float y)
    {
        if(!controlsKnown||controls==null||controls.Count>MaxExclusions||!float.IsFinite(x)||!float.IsFinite(y))return false;
        foreach(var box in controls)
        {
            if(!box.Valid)return false;
            if(x>=box.X&&x<box.Right&&y>=box.Y&&y<box.Bottom)return false;
        }
        return true;
    }
    private static void Add(List<Box> boxes,Box box){if(box.Valid)boxes.Add(box);}
}
