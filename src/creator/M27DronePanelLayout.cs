using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher.UI.Drone;
using UnityEngine;

namespace CreatorToolkit;

// M27: keeps the native map drone panel (MapUI._droneMenu, MapDroneMenuContainer)
// clear of the creator atlas panel. Runs in LateUpdate after the game's layout.
// Only DroneMenuUI.anchoredPosition is offset; the offset is tracked so a native
// reposition is adopted as the new base instead of being fought. Closing the
// panel or the map restores the native position (and the atlas panel if this
// tool collapsed it). Placement rules: M27DronePanelPolicy.
internal sealed class DronePanelLayoutTools
{
    private const string Idle="Панель дронов не открыта";
    private readonly ToolkitMod _app;
    private Il2CppStructArray<Vector3> _corners;
    private RectTransform _target,_parent;
    private Canvas _canvas;
    private IntPtr _ui;
    private Vector2 _base,_written;
    private Vector3 _appliedLocal;
    private float _appliedShift;
    private bool _applied,_collapsed,_userExpanded,_gaveUp,_bound;
    private int _overwrites,_layers=-1,_statusShift=int.MinValue;
    private bool _statusCollapse,_statusClear;
    internal string Status{get;private set;}=Idle;
    internal float Shift=>_applied?_appliedShift:0f;
    internal int Moves{get;private set;}
    internal bool AtlasCollapsed=>_collapsed;
    internal DronePanelLayoutTools(ToolkitMod app)=>_app=app;

    internal void LateTick()
    {
        var map=_app.Map;
        if(map==null||!map.NativeMapOpen||!_app.Ready){Restore(Idle);return;}
        var container=map.Ui._droneMenu;
        if(container==null||!container._isShowing||!container.isActiveAndEnabled){Restore(Idle);return;}
        var menu=container._droneMenu;
        if(menu==null){Restore("Панель дронов недоступна");return;}
        if(_gaveUp)return;
        if(!_bound||_ui!=map.Ui.Pointer||_target==null||_parent==null)
        {
            Restore(Idle);
            if(!Bind(map,menu))return;
        }
        float tolerance=M27DronePanelPolicy.WriteTolerance;
        if(_applied&&(_target.anchoredPosition-_written).sqrMagnitude>tolerance*tolerance)
        {
            // The game moved the panel itself: adopt its position as the new base.
            _applied=false;_appliedLocal=Vector3.zero;_appliedShift=0;
            if(++_overwrites>M27DronePanelPolicy.MaxOverwrites)
            {
                _gaveUp=true;
                SetStatus("Игра сама переставляет панель дронов — сдвиг отключён до следующего открытия карты");
                return;
            }
        }
        if(!_applied)_base=_target.anchoredPosition;
        var cam=CanvasCamera();
        if(!Measure(menu,cam,out var drone))return;
        // The user re-opened the atlas panel while the drone panel is shown: respect it.
        if(_collapsed&&map.PanelExpanded)_userExpanded=true;
        if(_layers<0)_layers=MapLayerOrder.Sorted(MapTools.Categories).Length;
        float sw=Screen.width,sh=Screen.height;
        // M28: the panel also keeps clear of the drone-menu button once that is moved off the atlas.
        var toggle=_app.DroneToggle;
        MapShieldPolicy.Box button=default;
        bool known=toggle!=null&&toggle.TryBaseBox(out button);
        string mode=toggle?.Mode??M28DroneButtonPolicy.Off;
        var decision=M27DronePanelPolicy.Decide(drone,
            M28DroneButtonPolicy.Obstacle(M27DronePanelPolicy.AtlasPanel(sw,sh,true,_layers),known,button,sw,sh,mode),
            M28DroneButtonPolicy.Obstacle(M27DronePanelPolicy.AtlasPanel(sw,sh,false,_layers),known,button,sw,sh,mode),
            map.PanelExpanded,!_userExpanded,sh);
        if(decision.Collapse&&map.PanelExpanded&&!_userExpanded){map.PanelExpanded=false;_collapsed=true;}
        Apply(decision.Shift,drone,cam);
        Describe(decision.Clear);
    }

    internal void Reset()=>Restore(Idle);

    private bool Bind(MapTools map,DroneMenuUI menu)
    {
        _target=menu.transform.TryCast<RectTransform>();
        var parent=_target!=null?_target.parent:null;
        _parent=parent!=null?parent.TryCast<RectTransform>():null;
        _canvas=_target!=null?_target.GetComponentInParent<Canvas>():null;
        _ui=map.Ui.Pointer;
        _bound=_target!=null&&_parent!=null;
        if(!_bound)SetStatus("Панель дронов: RectTransform недоступен, сдвиг не применяется");
        return _bound;
    }
    private Camera CanvasCamera()
    {
        if(_canvas==null)_canvas=_target.GetComponentInParent<Canvas>();
        var root=_canvas!=null?_canvas.rootCanvas:null;
        return root!=null&&root.renderMode!=RenderMode.ScreenSpaceOverlay?root.worldCamera:null;
    }

    // Top-left screen box of the visible panel with our own offset removed: the
    // panel root (the card) united with the tab strip, content and border. Live
    // 0.6.0 test: content+tabs alone measured ~330 px of a 630 px card, so a
    // 1056 px shift pushed the card off-screen. Children only count alone when
    // the root is a stretched container (M27DronePanelPolicy.UsableRoot).
    private bool Measure(DroneMenuUI menu,Camera cam,out MapShieldPolicy.Box box)
    {
        box=default;
        var offset=_applied?_parent.TransformVector(_appliedLocal):Vector3.zero;
        float minX=float.MaxValue,minY=float.MaxValue,maxX=float.MinValue,maxY=float.MinValue;
        bool any=Include(menu._mainContentTransform,offset,cam,ref minX,ref minY,ref maxX,ref maxY);
        var tabs=menu._tabbedControl;
        if(tabs!=null)any|=Include(tabs.transform.TryCast<RectTransform>(),offset,cam,ref minX,ref minY,ref maxX,ref maxY);
        var border=menu._contentBorder;
        if(border!=null)any|=Include(border.transform.TryCast<RectTransform>(),offset,cam,ref minX,ref minY,ref maxX,ref maxY);
        float rx0=float.MaxValue,ry0=float.MaxValue,rx1=float.MinValue,ry1=float.MinValue;
        if(Include(_target,offset,cam,ref rx0,ref ry0,ref rx1,ref ry1)&&
           M27DronePanelPolicy.UsableRoot(rx1-rx0,ry1-ry0,Screen.width,Screen.height))
        {
            minX=Math.Min(minX,rx0);minY=Math.Min(minY,ry0);maxX=Math.Max(maxX,rx1);maxY=Math.Max(maxY,ry1);any=true;
        }
        if(!any||maxX-minX<1f||maxY-minY<1f)
        {
            SetStatus("Панель дронов: размеры не определены, сдвиг не применяется");
            return false;
        }
        box=new MapShieldPolicy.Box(minX,minY,maxX-minX,maxY-minY);
        return box.Valid;
    }

    internal bool UserExpanded=>_userExpanded;
    // Acceptance diagnostics only: current screen boxes of the parts (our offset included).
    internal string DescribeParts()
    {
        var map=_app.Map;
        var container=map!=null&&map.NativeMapOpen?map.Ui._droneMenu:null;
        var menu=container!=null?container._droneMenu:null;
        if(menu==null)return "none";
        var canvas=menu.GetComponentInParent<Canvas>();
        var root=canvas!=null?canvas.rootCanvas:null;
        var cam=root!=null&&root.renderMode!=RenderMode.ScreenSpaceOverlay?root.worldCamera:null;
        string Part(string name,RectTransform rect)
        {
            float x0=float.MaxValue,y0=float.MaxValue,x1=float.MinValue,y1=float.MinValue;
            if(rect==null||!Include(rect,Vector3.zero,cam,ref x0,ref y0,ref x1,ref y1))return name+"=-";
            return name+"="+(int)x0+","+(int)y0+" "+(int)(x1-x0)+"x"+(int)(y1-y0);
        }
        var tabs=menu._tabbedControl;var border=menu._contentBorder;
        return Part("root",menu.transform.TryCast<RectTransform>())+" "+
            Part("tabs",tabs!=null?tabs.transform.TryCast<RectTransform>():null)+" "+
            Part("content",menu._mainContentTransform)+" "+
            Part("border",border!=null?border.transform.TryCast<RectTransform>():null);
    }
    private bool Include(RectTransform rect,Vector3 offset,Camera cam,ref float minX,ref float minY,ref float maxX,ref float maxY)
    {
        if(rect==null||!rect.gameObject.activeInHierarchy)return false;
        _corners??=new Il2CppStructArray<Vector3>(4);
        rect.GetWorldCorners(_corners);
        float x0=float.MaxValue,y0=float.MaxValue,x1=float.MinValue,y1=float.MinValue;
        for(int i=0;i<4;i++)
        {
            Vector2 screen=RectTransformUtility.WorldToScreenPoint(cam,_corners[i]-offset);
            if(!float.IsFinite(screen.x)||!float.IsFinite(screen.y))return false;
            float y=Screen.height-screen.y;
            x0=Math.Min(x0,screen.x);x1=Math.Max(x1,screen.x);y0=Math.Min(y0,y);y1=Math.Max(y1,y);
        }
        minX=Math.Min(minX,x0);maxX=Math.Max(maxX,x1);minY=Math.Min(minY,y0);maxY=Math.Max(maxY,y1);
        return true;
    }

    // Screen shift (pixels, downwards) -> parent-local anchoredPosition offset.
    private void Apply(float shift,MapShieldPolicy.Box drone,Camera cam)
    {
        if(!float.IsFinite(shift)||shift<0)shift=0;
        float tolerance=M27DronePanelPolicy.WriteTolerance;
        if(_applied?Math.Abs(shift-_appliedShift)<=tolerance:shift<=tolerance)return;
        float dy=0;
        if(shift>tolerance)
        {
            // Unity screen y grows upwards; the box is top-left based.
            var top=new Vector2(drone.X+drone.Width*.5f,Screen.height-drone.Y);
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent,top,cam,out var a)||
               !RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent,top-new Vector2(0,shift),cam,out var b))return;
            dy=b.y-a.y;
            if(!float.IsFinite(dy))return;
        }
        _target.anchoredPosition=_base+new Vector2(0,dy);
        _written=_target.anchoredPosition;
        _appliedLocal=new Vector3(0,dy,0);
        _applied=shift>tolerance;
        _appliedShift=_applied?shift:0;
        Moves++;
    }

    private void Describe(bool clear)
    {
        int shift=(int)Math.Round(Shift);
        if(shift==_statusShift&&_collapsed==_statusCollapse&&clear==_statusClear)return;
        _statusShift=shift;_statusCollapse=_collapsed;_statusClear=clear;
        Status=M27DronePanelPolicy.Describe(new M27DronePanelPolicy.Decision(Shift,_collapsed,clear));
    }
    private void SetStatus(string text){Status=text;_statusShift=int.MinValue;}

    private void Restore(string reason)
    {
        if(!_applied&&!_collapsed&&!_gaveUp&&!_bound&&_overwrites==0)
        {
            if(Status!=reason)Status=reason;
            return;
        }
        try
        {
            if(_applied&&_target!=null)
            {
                float tolerance=M27DronePanelPolicy.WriteTolerance;
                if((_target.anchoredPosition-_written).sqrMagnitude<=tolerance*tolerance)_target.anchoredPosition=_base;
            }
            if(_collapsed&&!_userExpanded&&_app.Map!=null)_app.Map.PanelExpanded=true;
        }
        finally
        {
            _applied=_collapsed=_userExpanded=_gaveUp=_bound=false;
            _appliedLocal=Vector3.zero;_appliedShift=0;_overwrites=0;
            _target=_parent=null;_canvas=null;_ui=IntPtr.Zero;
            SetStatus(reason);
        }
    }
}
