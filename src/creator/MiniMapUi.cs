using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatorToolkit;

// Compact gameplay minimap: a heading-up (or north-up) radar of the atlas layers that are
// currently enabled (same filters and "hide done" rule as the atlas), drawn with IMGUI only.
// No native objects are created; data is refreshed 2x/sec. M29: size, corner, zoom (30-300 m),
// background transparency and orientation come from navigation.cfg (NavigationTools).
internal sealed partial class MapTools
{
    internal bool ShowMiniMap;
    // Default zoom (M27); the live one is NavigationTools.Range.
    internal const float MiniMapRange=90f;
    internal const int MiniMapMarkers=80;
    internal readonly List<Point> MiniMapPoints=new();
    private readonly List<string> _miniLayers=new();
    private readonly List<float> _miniDistances=new();
    private readonly List<Point> _miniKeep=new();
    private float _nextMiniMap;
    private Camera _miniMapCamera;
    private float _nextMiniMapCamera;
    internal float MiniMapYaw {get;private set;}
    // Zoom or settings changed: the marker list is rebuilt on the next frame.
    internal void RefreshMiniMapSoon()=>_nextMiniMap=0;
    internal void TickMiniMap()
    {
        if(!ShowMiniMap||!_app.Ready||Busy){MiniMapPoints.Clear();return;}
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
            if(point==null||!Visible.Contains(point.Category))continue;
            float dx=point.Position.x-PlayerPosition.x,dz=point.Position.z-PlayerPosition.z;
            if(dx*dx+dz*dz>range2)continue;
            if(HideOpened&&Opened(point))continue;
            // M29 task 5: the navigator list rule for potential sources.
            if(SourcesActualOnly&&point.SourceObservation&&!point.LiveSource)continue;
            MiniMapPoints.Add(point);
        }
        if(MiniMapPoints.Count>80)
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
            float size=M29MiniMapLayout.Diameter(look.Size,Screen.height),radius=size/2;
            var tools=_app.Convenience;
            var slot=M29MiniMapLayout.Box(look.Corner,Screen.width,Screen.height,size,tools.ShowHud,tools.HudPlacement);
            Rect box=new(slot.X,slot.Y,slot.Width,slot.Height);
            Vector2 center=box.center;
            var terrain=_app.MiniMapTerrain;
            bool terrainOn=terrain!=null&&terrain.Enabled;
            var terrainTexture=terrainOn?terrain.TerrainTexture:null;
            float range=nav!=null?nav.Range:MapTools.MiniMapRange,alpha=look.BackgroundAlpha;
            // Heading-up turns the map by the view yaw; north-up keeps it still and turns the player instead.
            double yaw=M29MiniMapLayout.MapYaw(look.NorthUp,map.MiniMapYaw);
            float turn=M29MiniMapLayout.PlayerTurn(look.NorthUp,map.MiniMapYaw);
            float cos=(float)Math.Cos(yaw),sin=(float)Math.Sin(yaw),scale=(radius-14)/range;
            // Per-frame player sample when the terrain layer runs (smooth scrolling), else the 2 Hz atlas sample.
            var player=terrainOn&&terrain.HasPlayer?terrain.Player:map.PlayerPosition;
            if(terrainTexture!=null)
            {
                // M27: real atlas page crop (north-up composite) rotated heading-up around the centre.
                GUI.color=new Color(.30f,.88f,.82f,.92f*alpha);GUI.DrawTexture(box,_badgeCircle);
                GUI.color=new Color(.04f,.10f,.12f,alpha);GUI.DrawTexture(new Rect(box.x+3,box.y+3,size-6,size-6),_badgeCircle);
                GUI.color=new Color(1f,1f,1f,alpha);
                float terrainRadius=radius-6f,degrees=M27MiniMapPolicy.ScreenDegrees(yaw);
                // M29: the composite was made for terrain.ComposedRange; drawn at the live zoom until the next one.
                float drawn=terrainRadius*M29MiniMapZoom.TextureScale(terrain.ComposedRange,range);
                M27MiniMapPolicy.PivotTranslation(center.x,center.y,degrees,out double tx,out double ty);
                Vector2 shift=new((terrain.CenterX-player.x)*scale,-(terrain.CenterZ-player.z)*scale);
                GUI.matrix=Matrix4x4.TRS(new Vector3((float)tx,(float)ty,0f),Quaternion.Euler(0f,0f,degrees),Vector3.one);
                GUI.DrawTexture(new Rect(center.x+shift.x-drawn,center.y+shift.y-drawn,drawn*2,drawn*2),terrainTexture);
                GUI.matrix=Matrix4x4.identity;
            }
            else
            {
                GUI.color=new Color(.05f,.12f,.14f,.62f*alpha);GUI.DrawTexture(box,_badgeCircle);
                GUI.color=new Color(.30f,.88f,.82f,.35f*alpha);
                GUI.DrawTexture(new Rect(center.x-radius*.5f,center.y-radius*.5f,radius,radius),_badgeCircle);
                GUI.color=new Color(.05f,.12f,.14f,.70f*alpha);
                GUI.DrawTexture(new Rect(center.x-radius*.5f+2,center.y-radius*.5f+2,radius-4,radius-4),_badgeCircle);
            }
            GUI.color=Color.white;
            Vector2 Project(float dx,float dz)=>new(center.x+(dx*cos-dz*sin)*scale,center.y-(dx*sin+dz*cos)*scale);
            // North marker on the rim.
            var north=Project(0,range);
            L.Label(new Rect(north.x-14,north.y-14,28,28),"С",_badge);
            // M28: the native player marker's view cone sits under the point icons, as on the atlas.
            var playerMarker=_app.PlayerMarker;
            playerMarker?.Draw(center,radius,true,turn);
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
            // Player: the copied native marker (portrait, ring, pin) or, before the first copy, an arrow along the view.
            if(playerMarker==null||!playerMarker.Draw(center,radius,false,turn))
            {
                if(turn!=0f)
                {
                    M27MiniMapPolicy.PivotTranslation(center.x,center.y,turn,out double ax,out double ay);
                    GUI.matrix=Matrix4x4.TRS(new Vector3((float)ax,(float)ay,0f),Quaternion.Euler(0f,0f,turn),Vector3.one);
                }
                L.Label(new Rect(center.x-14,center.y-16,28,28),"↑",_badge);
                GUI.matrix=Matrix4x4.identity;
            }
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
            float below=Caption(M29NavigationSettings.Metres(range)+" · слоёв атласа: "+map.Visible.Count+(look.NorthUp?" · север":""),box.yMax);
            // M29: the minimap now starts with the game, so a composite without calibrated art (atlas not opened yet) says why it is dark.
            if(terrainOn&&(terrainTexture==null||terrain.Layers==0))
                Caption("рельеф: откройте карту игры в этой зоне",M29MiniMapLayout.Bottom(look.Corner)?box.y-30f:below);
        }
        finally{GUI.matrix=matrix;GUI.color=color;GUI.contentColor=content;GUI.backgroundColor=background;}
    }
}
