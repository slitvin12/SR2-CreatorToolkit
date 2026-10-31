using System;
using System.Collections.Generic;

namespace CreatorToolkit;

// 0.8.1 native map chrome (approved mockups 05/06): the atlas toolbar in the top-right corner
// («Атлас · <layers> ▼ · search · nearest · my markers · settings»), the legend line under it
// and the «Слои карты» drop-down. Logical 1080p pixels: the GUI space of
// ToolkitUi.DrawMapOverlay (uniform scale clamp(Screen.height/1080,.6,1.3)). Pure: no Unity,
// unit-tested offline and shared with M27DronePanelPolicy, which keeps the native drone panel
// clear of it.
internal static class MapChromePolicy
{
    internal const float Inset=20f,Top=20f,ToolbarWidth=600f,ToolbarHeight=60f,
        LineTop=88f,LineHeight=40f,LayersTop=88f,LayersWidth=440f,BottomReserve=84f,
        Round=44f,RoundGap=6f,GroupX=150f,MinLayersHeight=240f;
    internal const int RoundButtons=4;
    internal static MapShieldPolicy.Box Toolbar(float sw)=>new(sw-Inset-ToolbarWidth,Top,ToolbarWidth,ToolbarHeight);
    // The line under the toolbar (return, search chip, legend) while the drop-down is closed.
    internal static MapShieldPolicy.Box Line(float sw)=>new(sw-Inset-ToolbarWidth,LineTop,ToolbarWidth,LineHeight);
    internal static MapShieldPolicy.Box Layers(float sw,float sh)=>
        new(sw-Inset-LayersWidth,LayersTop,LayersWidth,Math.Max(MinLayersHeight,sh-BottomReserve-LayersTop));
    // Everything the overlay may cover: the native drone panel keeps clear of it.
    internal static MapShieldPolicy.Box Bounds(float sw,float sh,bool layersOpen)
    {
        var bar=Toolbar(sw);
        float bottom=layersOpen?Layers(sw,sh).Bottom:LineTop+LineHeight;
        return new MapShieldPolicy.Box(bar.X,bar.Y,bar.Width,bottom-bar.Y);
    }
    // Round toolbar buttons, toolbar-relative x: 0 search, 1 nearest, 2 my markers, 3 settings.
    internal static float RoundX(int index)=>ToolbarWidth-12f-(RoundButtons-index)*Round-(RoundButtons-1-index)*RoundGap;
    internal static float GroupWidth=>RoundX(0)-8f-GroupX;

    // Marker icon size S / M / L inside MapOverlayLayout's 14..32 px range.
    internal static readonly float[] Sizes={16f,22f,30f};
    internal static int SizeIndex(float size)
    {
        int best=0;
        for(int i=1;i<Sizes.Length;i++)if(Math.Abs(Sizes[i]-size)<Math.Abs(Sizes[best]-size))best=i;
        return best;
    }
    // Left click: 0 the game's own waypoint, 1 a colored own marker, 2 teleport.
    internal static int ClickMode(bool custom,bool teleport)=>custom?1:teleport?2:0;

    // «На карте: 12 · открытое скрыто · в тумане скрыто: 23».
    internal static string Legend(int shown,bool hideOpened,bool hideFog,int fogged)
    {
        string text="На карте: "+Math.Max(0,shown);
        if(hideOpened)text+=" · открытое скрыто";
        if(hideFog&&fogged>0)text+=" · в тумане скрыто: "+fogged;
        return text;
    }

    // Hover plaque beside its marker: right, left, above, below — the first one clear of every
    // obstacle (selected card, toolbar, line / layers, native chrome), else the least covered.
    internal const float TipGap=22f,TipTop=90f,TipBottom=100f,TipHeight=56f;
    internal static MapShieldPolicy.Box TipPlace(float x,float y,float w,float h,float sw,float sh,
        IReadOnlyList<MapShieldPolicy.Box> obstacles)
    {
        MapShieldPolicy.Box best=default;float bestArea=float.MaxValue;
        for(int i=0;i<4;i++)
        {
            float cx=i==0?x+TipGap:i==1?x-TipGap-w:x-w*.5f;
            float cy=i<2?y-h*.5f:i==2?y-TipGap-h:y+TipGap;
            var box=new MapShieldPolicy.Box(Clamp(cx,16f,sw-w-16f),Clamp(cy,TipTop,sh-TipBottom-h),w,h);
            float area=0;
            if(obstacles!=null)foreach(var o in obstacles)area+=M29MapCardPolicy.Overlap(box,o);
            if(area<=0)return box;
            if(area<bestArea){bestArea=area;best=box;}
        }
        return best;
    }
    // Never throws on tiny screens (hi<lo): the low edge wins.
    private static float Clamp(float v,float lo,float hi)=>Math.Max(lo,Math.Min(v,hi));

    // Toolbar glyphs drawn from a distance field (the game font has no ◎ ⚙ ▼ 🔍).
    internal const int Search=0,Target=1,Gear=2,Chevron=3,ChevronRight=4,Count=10;
    // 0.8.1 «Карта» pages: a thin ring, the navigation arrow (points up), plus, check and cross.
    internal const int Ring=5,Arrow=6,Plus=7,Check=8,Cross=9;
    // Coverage 0..1 of glyph g at (x,y) in [0,1]², y down; a one-pixel smooth edge at size px.
    internal static float Glyph(int glyph,float x,float y,float size)
    {
        float aa=1.2f/Math.Max(8f,size);
        float d=glyph switch
        {
            Search=>Math.Min(RingField(x,y,.42f,.42f,.25f,.075f),Segment(x,y,.61f,.61f,.84f,.84f)-.075f),
            Target=>Math.Min(RingField(x,y,.5f,.5f,.34f,.065f),Dist(x,y,.5f,.5f)-.12f),
            Gear=>GearField(x,y),
            Chevron=>Math.Min(Segment(x,y,.24f,.38f,.5f,.64f),Segment(x,y,.5f,.64f,.76f,.38f))-.075f,
            ChevronRight=>Math.Min(Segment(x,y,.38f,.24f,.64f,.5f),Segment(x,y,.64f,.5f,.38f,.76f))-.075f,
            Ring=>RingField(x,y,.5f,.5f,.42f,.06f),
            Arrow=>ArrowField(x,y),
            Plus=>Math.Min(Segment(x,y,.5f,.2f,.5f,.8f),Segment(x,y,.2f,.5f,.8f,.5f))-.075f,
            Check=>Math.Min(Segment(x,y,.22f,.52f,.42f,.72f),Segment(x,y,.42f,.72f,.8f,.3f))-.08f,
            Cross=>Math.Min(Segment(x,y,.27f,.27f,.73f,.73f),Segment(x,y,.73f,.27f,.27f,.73f))-.07f,
            _=>1f
        };
        return Math.Clamp(.5f-d/aa,0f,1f);
    }
    private static float Dist(float x,float y,float cx,float cy)=>MathF.Sqrt((x-cx)*(x-cx)+(y-cy)*(y-cy));
    private static float RingField(float x,float y,float cx,float cy,float r,float half)=>Math.Abs(Dist(x,y,cx,cy)-r)-half;
    private static float Segment(float x,float y,float ax,float ay,float bx,float by)
    {
        float dx=bx-ax,dy=by-ay,len=dx*dx+dy*dy;
        float t=len<=0?0:Math.Clamp(((x-ax)*dx+(y-ay)*dy)/len,0f,1f);
        return Dist(x,y,ax+t*dx,ay+t*dy);
    }
    // Arrowhead (.5,.1) → (.84,.88) → (.5,.68) → (.16,.88): negative inside.
    private static readonly float[] ArrowPoints={.5f,.1f,.84f,.88f,.5f,.68f,.16f,.88f};
    private static float ArrowField(float x,float y)
    {
        var p=ArrowPoints;int n=p.Length/2;bool inside=false;float best=float.MaxValue;
        for(int i=0,j=n-1;i<n;j=i++)
        {
            float xi=p[2*i],yi=p[2*i+1],xj=p[2*j],yj=p[2*j+1];
            if((yi>y)!=(yj>y)&&x<(xj-xi)*(y-yi)/(yj-yi)+xi)inside=!inside;
            best=Math.Min(best,Segment(x,y,xi,yi,xj,yj));
        }
        return inside?-best:best;
    }
    private static float GearField(float x,float y)
    {
        float r=Dist(x,y,.5f,.5f),a=MathF.Atan2(y-.5f,x-.5f);
        // Eight teeth: the outline radius steps between .30 and .40.
        float tooth=MathF.Cos(8f*a)>.25f?.41f:.31f;
        float outer=r-tooth,hole=.13f-r;
        return Math.Max(outer,hole);
    }
}
