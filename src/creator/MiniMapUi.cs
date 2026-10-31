using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatorToolkit;

// Compact gameplay minimap: a heading-up (or north-up) radar of the atlas layers that are
// currently enabled (same filters and "hide done" rule as the atlas), drawn with IMGUI only.
// No native objects are created; data is refreshed 2x/sec. M29: size, corner, zoom (30-300 m),
// background transparency and orientation come from navigation.cfg (NavigationTools).
// 0.8.1 (mockup 07): a pink «N» on the rim, the nearest 24 icons, radar hits in their target
// colour, the compass goal pinned to the rim with its distance when it lies outside, and only the
// scale under the disc. The same disc is the live preview of «Карта → Навигация».
internal sealed partial class MapTools
{
    internal bool ShowMiniMap;
    // Default zoom (M27); the live one is NavigationTools.Range.
    internal const float MiniMapRange=90f;
    internal const int MiniMapMarkers=MapExplorerPolicy.MiniMapIcons;
    internal readonly List<Point> MiniMapPoints=new();
    private readonly List<string> _miniLayers=new();
    private readonly List<float> _miniDistances=new();
    private readonly List<Point> _miniKeep=new();
    private float _nextMiniMap,_miniPreviewUntil;
    private Camera _miniMapCamera;
    private float _nextMiniMapCamera;
    internal float MiniMapYaw {get;private set;}
    // Zoom or settings changed: the marker list is rebuilt on the next frame.
    internal void RefreshMiniMapSoon()=>_nextMiniMap=0;
    // «Навигация» preview: the points are gathered for one more second even with the minimap off.
    internal void PreviewMiniMap()
    {
        if(!ShowMiniMap&&Time.unscaledTime>=_miniPreviewUntil)_nextMiniMap=0;
        _miniPreviewUntil=Time.unscaledTime+1f;
    }
    internal void TickMiniMap()
    {
        bool wanted=ShowMiniMap||Time.unscaledTime<_miniPreviewUntil;
        if(!wanted||!_app.Ready||Busy){MiniMapPoints.Clear();return;}
        if(Time.unscaledTime>=_nextMiniMapCamera||_miniMapCamera==null||_miniMapCamera.WasCollected)
        {_nextMiniMapCamera=Time.unscaledTime+1f;_miniMapCamera=Camera.main;}
        if(_miniMapCamera!=null&&!_miniMapCamera.WasCollected)
        {var forward=_miniMapCamera.transform.forward;if(forward.x*forward.x+forward.z*forward.z>1e-4f)MiniMapYaw=Mathf.Atan2(forward.x,forward.z);}
        if(Time.unscaledTime<_nextMiniMap)return;
        _nextMiniMap=Time.unscaledTime+.5f;
        _nextPlayerSample=0;SamplePlayer();
        MiniMapPoints.Clear();
        if(!HasPlayerPosition)return;
        // M29: the larger of the shown and the chosen zoom, so a zoom-out never waits for markers.
        var nav=_app.Navigation;
        float shown=nav!=null?Math.Max(nav.Range,nav.Settings.Range):MiniMapRange;
        float range=shown*1.05f,range2=range*range;
        foreach(var point in Points)
        {
            if(point==null||!LayerVisible(point))continue;
            float dx=point.Position.x-PlayerPosition.x,dz=point.Position.z-PlayerPosition.z;
            if(dx*dx+dz*dz>range2)continue;
            if(HideOpened&&Opened(point))continue;
            // M29 task 5: the navigator list rule for potential sources.
            if(SourcesActualOnly&&point.SourceObservation&&!point.LiveSource)continue;
            MiniMapPoints.Add(point);
        }
        if(MiniMapPoints.Count>MiniMapMarkers)
        {
            // M29 task 5: every enabled layer keeps a share (rounds of one point per layer, nearest first).
            _miniLayers.Clear();_miniDistances.Clear();_miniKeep.Clear();
            foreach(var point in MiniMapPoints){_miniLayers.Add(point.Category);_miniDistances.Add(Distance(point));}
            foreach(int index in M29LayerPolicy.RoundRobin(_miniLayers,_miniDistances,MiniMapMarkers))_miniKeep.Add(MiniMapPoints[index]);
            MiniMapPoints.Clear();MiniMapPoints.AddRange(_miniKeep);_miniKeep.Clear();
        }
    }
}

internal sealed partial class ToolkitUi
{
    private static readonly M29NavigationSettings DefaultNavigation=new();
    internal void DrawMiniMap()
    {
        var map=_app.Map;
        if(map==null||!map.ShowMiniMap||!_app.Ready||_app.MenuOpen||map.NativeMapOpen||map.Busy||_app.Cinema.HideHud||Time.timeScale<=0)return;
        if(Event.current==null||Event.current.type!=EventType.Repaint)return;
        if(!map.HasPlayerPosition)return;
        Styles();
        var matrix=GUI.matrix;var color=GUI.color;var content=GUI.contentColor;var background=GUI.backgroundColor;
        try
        {
            _scale=1;_groupOrigin=Vector2.zero;_groupOrigins.Clear();
            GUI.matrix=Matrix4x4.identity;GUI.color=GUI.contentColor=GUI.backgroundColor=Color.white;
            // M29 task 4: size, corner, zoom, background transparency and orientation (navigation.cfg).
            var nav=_app.Navigation;var look=nav?.Settings??DefaultNavigation;
            float size=M29MiniMapLayout.Diameter(look.Size,Screen.height);
            var tools=_app.Convenience;
            var slot=M29MiniMapLayout.Box(look.Corner,Screen.width,Screen.height,size,tools.ShowHud,tools.HudPlacement);
            Rect box=new(slot.X,slot.Y,slot.Width,slot.Height);
            Vector2 center=box.center;
            float range=nav!=null?nav.Range:MapTools.MiniMapRange;
            var terrain=_app.MiniMapTerrain;
            bool terrainOn=terrain!=null&&terrain.Enabled;
            DrawMiniDisc(box,look,range,Vector2.zero,1f);
            // M29: captions sit on a parchment chip (the HUD panel surface), centred under the map and kept
            // on screen, so they stay legible over bright or busy scenery.
            float Caption(string text,float y)
            {
                var caption=ButtonContent(text);
                float chipWidth=Mathf.Min(_muted.CalcSize(caption).x+24f,Screen.width-16f);
                float chipX=Mathf.Clamp(center.x-chipWidth/2f,8f,Mathf.Max(8f,Screen.width-8f-chipWidth));
                DrawRoundedSurface(new Rect(chipX,y,chipWidth,26f),_hudFrame.normal.background,12f);
                L.Label(new Rect(chipX+12f,y+1f,chipWidth-16f,24f),caption,_muted);
                return y+30f;
            }
            // 0.8.1 (mockup 07): only the scale under the disc.
            float below=Caption(M29NavigationSettings.Metres(range),box.yMax+4f);
            // M29: the minimap now starts with the game, so a composite without calibrated art (atlas not opened yet) says why it is dark.
            var terrainTexture=terrainOn?terrain.TerrainTexture:null;
            if(terrainOn&&(terrainTexture==null||terrain.Layers==0))
                Caption("рельеф: откройте карту игры в этой зоне",M29MiniMapLayout.Bottom(look.Corner)?box.y-30f:below);
        }
        finally{GUI.matrix=matrix;GUI.color=color;GUI.contentColor=content;GUI.backgroundColor=background;}
    }
    // «Карта → Навигация»: the same disc inside the menu (scaled matrix, nested groups).
    // 0.8.1: the disc follows S/M/L inside the preview area and lies on an opaque «world» disc, so
    // the opacity setting mixes with scenery colours as in game instead of greying over the card.
    private void DrawMiniMapPreview(Rect area,M29NavigationSettings look,float range)
    {
        if(Event.current==null||Event.current.type!=EventType.Repaint)return;
        var color=GUI.color;
        try
        {
            if(!_app.Map.HasPlayerPosition){_app.Map.SamplePlayer();}
            float d=Mathf.Round(area.width*M29MiniMapLayout.PreviewFraction(look.Size,Screen.height));
            Rect box=new(Mathf.Round(area.center.x-d/2),Mathf.Round(area.center.y-d/2),d,d);
            float fade=look.Mode==0?.45f:1f;
            GUI.color=new Color(.30f,.46f,.42f,fade);GUI.DrawTexture(box,MiniDiscTexture(false));
            GUI.color=color;
            DrawMiniDisc(box,look,range,_groupOrigin,fade);
            if(look.Mode==0)
            {
                float chipW=Mathf.Min(area.width-24,AtlasTextWidth("мини-карта выключена",_explorerCenter)+28);
                Rect chip=new(area.center.x-chipW/2,area.center.y+34,chipW,28);
                DrawRoundedSurface(chip,_hudFrame.normal.background,14f);
                L.Label(chip,"мини-карта выключена",_explorerCenter);
            }
        }
        finally{GUI.color=color;}
    }
    // White antialiased disc / thin ring textures for the minimap body and rim (tinted by GUI.color).
    private Texture2D _miniDisc,_miniRim;
    private Texture2D MiniDiscTexture(bool rim)
    {
        var cached=rim?_miniRim:_miniDisc;
        if(cached!=null&&!cached.WasCollected)return cached;
        const int size=256;const float r=size/2f,width=5f;
        var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        var pixels=new Color32[size*size];
        for(int y=0;y<size;y++)
            for(int x=0;x<size;x++)
            {
                float dx=x+.5f-r,dy=y+.5f-r,d=Mathf.Sqrt(dx*dx+dy*dy);
                float a=Mathf.Clamp01(r-d);
                if(rim)a*=Mathf.Clamp01(d-(r-width));
                pixels[y*size+x]=new Color32(255,255,255,(byte)Mathf.RoundToInt(a*255));
            }
        texture.SetPixels32(pixels);texture.Apply(false,true);
        texture.hideFlags=HideFlags.HideAndDontSave;
        if(rim)_miniRim=texture;else _miniDisc=texture;
        return texture;
    }
    // One minimap disc. clip: the GUI group origin (pre-matrix) of the caller, so the rotations
    // pivot on the right point inside nested menu groups; zero for the HUD.
    private void DrawMiniDisc(Rect box,M29NavigationSettings look,float range,Vector2 clip,float fade)
    {
        var map=_app.Map;
        var baseMatrix=GUI.matrix;
        float size=box.width,radius=size/2;
        Vector2 center=box.center;
        var terrain=_app.MiniMapTerrain;
        bool terrainOn=terrain!=null&&terrain.Enabled;
        var terrainTexture=terrainOn?terrain.TerrainTexture:null;
        float alpha=look.BackgroundAlpha*fade;
        // Heading-up turns the map by the view yaw; north-up keeps it still and turns the player instead.
        double yaw=M29MiniMapLayout.MapYaw(look.NorthUp,map.MiniMapYaw);
        float turn=M29MiniMapLayout.PlayerTurn(look.NorthUp,map.MiniMapYaw);
        float cos=(float)Math.Cos(yaw),sin=(float)Math.Sin(yaw),scale=(radius-14)/range;
        // Per-frame player sample when the terrain layer runs (smooth scrolling), else the 2 Hz atlas sample.
        var player=terrainOn&&terrain.HasPlayer?terrain.Player:map.PlayerPosition;
        try
        {
            if(terrainTexture!=null)
            {
                // M27: real atlas page crop (north-up composite) rotated heading-up around the centre.
                GUI.color=new Color(.04f,.10f,.12f,alpha);GUI.DrawTexture(box,MiniDiscTexture(false));
                GUI.color=new Color(1f,1f,1f,alpha);
                float terrainRadius=radius-6f,degrees=M27MiniMapPolicy.ScreenDegrees(yaw);
                // M29: the composite was made for terrain.ComposedRange; drawn at the live zoom until the next one.
                float drawn=terrainRadius*M29MiniMapZoom.TextureScale(terrain.ComposedRange,range);
                M27MiniMapPolicy.PivotTranslation(center.x+clip.x,center.y+clip.y,degrees,out double tx,out double ty);
                Vector2 shift=new((terrain.CenterX-player.x)*scale,-(terrain.CenterZ-player.z)*scale);
                GUI.matrix=baseMatrix*Matrix4x4.TRS(new Vector3((float)tx,(float)ty,0f),Quaternion.Euler(0f,0f,degrees),Vector3.one);
                GUI.DrawTexture(new Rect(center.x+shift.x-drawn,center.y+shift.y-drawn,drawn*2,drawn*2),terrainTexture);
                GUI.matrix=baseMatrix;
                GUI.color=new Color(.55f,.92f,.86f,.95f*fade);GUI.DrawTexture(box,MiniDiscTexture(true));
            }
            else
            {
                // Mockup 07: an opaque-looking dark slate disc with a thin light rim (a separate ring texture,
                // so the rim never shows through the translucent body).
                GUI.color=new Color(.13f,.18f,.22f,.9f*alpha);GUI.DrawTexture(box,MiniDiscTexture(false));
                GUI.color=new Color(.55f,.92f,.86f,.95f*fade);GUI.DrawTexture(box,MiniDiscTexture(true));
                // Two faint distance rings (half and full zoom).
                GUI.color=new Color(.72f,.92f,.90f,.22f*fade);
                MapGlyph(new Rect(center.x-radius*.62f,center.y-radius*.62f,radius*1.24f,radius*1.24f),MapChromePolicy.Ring,GUI.color);
                MapGlyph(new Rect(center.x-radius*.3f,center.y-radius*.3f,radius*.6f,radius*.6f),MapChromePolicy.Ring,GUI.color);
            }
            GUI.color=fade<1f?new Color(1f,1f,1f,.6f):Color.white;
            Vector2 Project(float dx,float dz)=>new(center.x+(dx*cos-dz*sin)*scale,center.y-(dx*sin+dz*cos)*scale);
            // M28: the native player marker's view cone sits under the point icons, as on the atlas.
            var playerMarker=_app.PlayerMarker;
            playerMarker?.Draw(center,radius,true,turn,clip);
            float limit=range*range;
            foreach(var point in map.MiniMapPoints)
            {
                float dx=point.Position.x-player.x,dz=point.Position.z-player.z;
                if(dx*dx+dz*dz>limit)continue;
                var at=Project(dx,dz);Rect icon=new(at.x-10,at.y-10,20,20);
                // Pods, map nodes and research/phantom drones: mod canon badges first.
                if(!CanonIcon(icon,CanonMapIconPolicy.Cell(point.Category,point.Id,point.Name)))
                {
                    var sprite=map.Icons.Point(point);
                    if(sprite!=null)DrawSprite(icon,sprite);
                    else if(!FandomIcon(icon,FandomMapMarkers.IconFor(point)))Fill(new Rect(at.x-3,at.y-3,6,6),new Color(.8f,1f,.94f,.9f));
                }
                if(point.ExactHeight&&Math.Abs(point.Position.y-player.y)>6f)
                    L.Label(new Rect(at.x+6,at.y-14,16,16),point.Position.y>player.y?"↑":"↓",_badge);
            }
            // 0.8.1: radar hits in their target colour.
            var radar=_app.Radar;
            if(radar!=null&&radar.Enabled&&radar.ShowOnHud)
            {
                var hits=radar.Hits;
                for(int i=0;i<hits.Count;i++)
                {
                    var hit=hits[i];
                    float dx=hit.Position.x-player.x,dz=hit.Position.z-player.z;
                    if(dx*dx+dz*dz>limit)continue;
                    var at=Project(dx,dz);Color c=MarkerColor(hit.Color);
                    Dot(new Rect(at.x-12,at.y-12,24,24),new Color(c.r,c.g,c.b,.30f));
                    MapGlyph(new Rect(at.x-13,at.y-13,26,26),MapChromePolicy.Ring,c);
                    if(hit.Icon!=null)DrawSprite(new Rect(at.x-8,at.y-8,16,16),hit.Icon);
                }
            }
            MiniMapGoal(center,radius,scale,player,Project);
            // 0.8.1: a pink «N» badge on the rim.
            var north=Project(0,range);
            Vector2 toNorth=north-center;
            if(toNorth.sqrMagnitude>1e-3f)north=center+toNorth.normalized*(radius-2f);
            Dot(new Rect(north.x-13,north.y-13,26,26),Color.white);
            Dot(new Rect(north.x-11,north.y-11,22,22),_pink);
            L.Label(new Rect(north.x-11,north.y-11,22,22),"N",_explorerPillTextWhite);
            // Player: the copied native marker (portrait, ring, pin) or, before the first copy, an arrow along the view.
            if(playerMarker==null||!playerMarker.Draw(center,radius,false,turn,clip))
            {
                if(turn!=0f)
                {
                    M27MiniMapPolicy.PivotTranslation(center.x+clip.x,center.y+clip.y,turn,out double ax,out double ay);
                    GUI.matrix=baseMatrix*Matrix4x4.TRS(new Vector3((float)ax,(float)ay,0f),Quaternion.Euler(0f,0f,turn),Vector3.one);
                }
                MapGlyph(new Rect(center.x-13,center.y-14,26,26),MapChromePolicy.Arrow,_accent);
                GUI.matrix=baseMatrix;
            }
        }
        finally{GUI.matrix=baseMatrix;}
    }
    // The compass goal (own marker or the atlas selection): inside it is drawn in place with a ring,
    // outside it is pinned to the rim along its bearing with a distance chip.
    private void MiniMapGoal(Vector2 center,float radius,float scale,Vector3 player,Func<float,float,Vector2> project)
    {
        var map=_app.Map;var custom=_app.Custom;
        MapTools.Point goal=null;Color tint=_pink;bool own=false;
        string id=custom?.GoalId??"";
        if(id.Length>0){goal=custom.FindPoint(id);own=goal!=null;if(own)tint=MarkerColor(goal.ColorIndex);}
        if(goal==null)goal=map.Selected;
        if(goal?.Group==null||goal.Group.name!=CurrentGroupName())return;
        float dx=goal.Position.x-player.x,dz=goal.Position.z-player.z;
        var at=project(dx,dz);
        bool pinned=MapExplorerPolicy.Rim(at.x-center.x,at.y-center.y,radius-15f,out float rx,out float ry);
        at=new Vector2(center.x+rx,center.y+ry);
        Dot(new Rect(at.x-14,at.y-14,28,28),Color.white);
        Dot(new Rect(at.x-12,at.y-12,24,24),tint);
        if(own){var flag=map.Icons.Category(CustomWaypointTools.Category);if(flag!=null)DrawSprite(new Rect(at.x-8,at.y-8,16,16),flag);}
        else{var sprite=map.Icons.Point(goal);if(sprite!=null)DrawSprite(new Rect(at.x-9,at.y-9,18,18),sprite);}
        if(!pinned)return;
        string text=MapTools.DistanceText(Mathf.Sqrt(dx*dx+dz*dz));
        float w=AtlasTextWidth(text,_explorerPillText)+16;
        MapExplorerPolicy.ChipSide(rx,ry,w,out float cx,out float cy);
        Rect chip=new(center.x+cx,center.y+cy,w,22);
        DrawRoundedSurface(chip,_hudFrame.normal.background,11f);
        L.Label(chip,text,_explorerPillText);
    }
}
