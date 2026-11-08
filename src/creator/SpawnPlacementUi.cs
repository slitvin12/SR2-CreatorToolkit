using System;
using UnityEngine;
namespace CreatorToolkit;
// 0.8.7 «Выбрать место…» overlay: a ring on the surface (green = can place, red = not), an arrow
// for the facing and a dot for every actor of the batch, plus a compact top panel with the keys.
// Drawn while the menu is open but stepped aside; only the shared IMGUI wrappers (L.*, Fill).
internal sealed partial class ToolkitUi
{
    private static readonly Color PlacementOk=new(.42f,.95f,.72f,.95f),PlacementBad=new(1f,.45f,.4f,.95f),PlacementShade=new(0f,0f,0f,.5f);
    private void BeginPlacement(ActorTools.Entry entry,int count)
    {
        if(entry==null)return;
        try{_app.Placement.Begin(entry,count);ClearInteraction();}
        catch(Exception e){_app.Fail("spawn placement",e);}
    }
    internal void DrawPlacement()
    {
        var tool=_app.Placement;
        if(tool==null||!tool.Showing)return;
        Styles();
        var matrix=GUI.matrix;var color=GUI.color;bool enabled=GUI.enabled;
        try
        {
            _scale=1;_groupOrigin=Vector2.zero;_groupOrigins.Clear();_groupRects.Clear();
            GUI.matrix=Matrix4x4.identity;GUI.color=Color.white;GUI.enabled=true;
            var cam=Camera.main;
            if(cam!=null&&tool.HasPoint&&Event.current?.type==EventType.Repaint)DrawPlacementMarker(cam,tool);
            float width=Mathf.Min(900,Screen.width-32);Rect rect=new((Screen.width-width)/2,20,width,132);
            L.Box(rect,GUIContent.none,_hudFrame);
            L.Label(new Rect(rect.x+18,rect.y+8,width*.42f,34),tool.Gordo?"Место для Гордо":"Место и поворот",_label);
            L.Label(new Rect(rect.x+width*.42f,rect.y+8,width*.58f-18,34),(tool.Entry?.Name??"")+(tool.Gordo?"":" × "+tool.Count),_muted);
            L.Label(new Rect(rect.x+18,rect.y+46,width-210,30),tool.Valid?"Можно поставить":tool.Problem??"Наведите курсор на землю",tool.Valid?_label:_muted);
            L.Label(new Rect(rect.x+width-190,rect.y+46,172,30),Mathf.RoundToInt(tool.Yaw)+"°"+(tool.Placed>0?" · +"+tool.Placed:""),_label);
            L.Label(new Rect(rect.x+18,rect.y+82,width-36,42),"ЛКМ — поставить · колесо или Q/E — поворот (Shift — точнее) · R — лицом к вам · ПКМ — назад · Esc — закрыть меню",_small);
        }
        finally{GUI.matrix=matrix;GUI.color=color;GUI.enabled=enabled;}
    }
    private void DrawPlacementMarker(Camera cam,SpawnPlacementTools tool)
    {
        Color tint=tool.Valid?PlacementOk:PlacementBad;
        Vector3 center=tool.Valid?tool.Ground:tool.Point;
        float radius=tool.Radius;
        const int segments=48;
        bool hasPrevious=false;Vector2 previous=default;
        for(int i=0;i<=segments;i++)
        {
            float a=i*(Mathf.PI*2f/segments);
            bool visible=PlacementScreen(cam,center+new Vector3(Mathf.Cos(a)*radius,.06f,Mathf.Sin(a)*radius),out var point);
            if(visible&&hasPrevious)PlacementLine(previous,point,3f,tint);
            hasPrevious=visible;previous=point;
        }
        Vector3 facing=Quaternion.Euler(0f,tool.Yaw,0f)*Vector3.forward,side=Vector3.Cross(Vector3.up,facing),lift=Vector3.up*.06f;
        Vector3 tip=center+facing*(radius+.9f)+lift;
        if(PlacementScreen(cam,center+lift,out var start)&&PlacementScreen(cam,tip,out var end))
        {
            PlacementLine(start,end,4f,tint);
            if(PlacementScreen(cam,tip-facing*.45f+side*.3f,out var left))PlacementLine(end,left,4f,tint);
            if(PlacementScreen(cam,tip-facing*.45f-side*.3f,out var right))PlacementLine(end,right,4f,tint);
        }
        if(!tool.Valid)return;
        var positions=tool.Positions;
        for(int i=0;i<positions.Count;i++)
            if(PlacementScreen(cam,positions[i],out var p)){Fill(new Rect(p.x-5,p.y-5,10,10),PlacementShade);Fill(new Rect(p.x-3,p.y-3,6,6),tint);}
    }
    private static bool PlacementScreen(Camera cam,Vector3 world,out Vector2 gui)
    {
        Vector3 screen=cam.WorldToScreenPoint(world);
        gui=new Vector2(screen.x,Screen.height-screen.y);
        return screen.z>.05f&&float.IsFinite(screen.x)&&float.IsFinite(screen.y);
    }
    private static void PlacementLine(Vector2 start,Vector2 end,float size,Color tint)
    {
        int steps=Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(start,end)/(size*1.25f)),1,240);
        for(int i=0;i<=steps;i++)
        {
            Vector2 p=Vector2.Lerp(start,end,i/(float)steps);
            Fill(new Rect(p.x-size*.5f,p.y-size*.5f,size,size),tint);
        }
    }
}
