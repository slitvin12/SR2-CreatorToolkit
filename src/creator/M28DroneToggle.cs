using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher.UI.Framework.CommonControls;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CreatorToolkit;

// M28: the native map button that opens the drone panel (MapUI._toggleDroneMenuInput /
// ToggleDroneMenuOnMouseEvent) sat under the creator atlas header, and the input
// shield keeps holes over native controls, so «свернуть атлас» also opened the drone
// panel. While the native map is open the button is found (once per map opening,
// bounded retries) and, when it overlaps the atlas panel, offset below or left of it
// (M28DroneButtonPolicy) through its anchoredPosition only. A native reposition is
// adopted as the new base; closing the map restores the native position. The control
// is also no longer a hole in the atlas input shield (Shielded).
internal sealed class DroneToggleTools
{
    private sealed class Candidate
    {
        internal RectTransform Handler;
        internal string Kind,Path;
        internal bool Active;
    }
    private const float DiscoverySeconds=.5f,AreaGrowth=2.5f,MaxScreenFraction=.5f;
    private const int MaxAttempts=12,MaxClimb=3,MaxGraphics=64,MaxComponents=4096;
    private readonly ToolkitMod _app;
    private readonly List<Candidate> _found=new();
    private readonly Dictionary<string,Il2CppSystem.Reflection.FieldInfo[]> _eventFields=new(StringComparer.Ordinal);
    private Il2CppStructArray<Vector3> _corners;
    private RectTransform _target,_parent;
    private IntPtr _ui;
    private Vector2 _base,_written,_appliedDelta;
    private bool _applied,_gaveUp,_hasBase;
    private int _attempts,_overwrites,_layers=-1;
    private float _nextAttempt;
    private MapShieldPolicy.Box _baseBox,_atlas;
    private M28DroneButtonPolicy.Placement _placement;
    private int _pick=-1,_climb=-1,_climbed;
    internal string Mode{get;private set;}=M28DroneButtonPolicy.Auto;
    internal string Status{get;private set;}="Кнопка дронов: карта не открыта";
    internal int Moves{get;private set;}
    internal int Finds{get;private set;}
    internal string Identity{get;private set;}="";
    internal bool Found=>_target!=null;
    internal DroneToggleTools(ToolkitMod app)=>_app=app;

    internal void SetMode(string mode)
    {
        Mode=M28DroneButtonPolicy.Mode(mode);
        ResetOffset();
    }
    // Test overrides: climb n levels above the click handler, or pick candidate i.
    internal void SetClimb(int levels){_climb=Math.Clamp(levels,-1,MaxClimb);Forget();}
    internal void Pick(int index){_pick=index;Forget();}
    internal void Reset(){Restore();Forget();_ui=IntPtr.Zero;}

    internal void LateTick()
    {
        var map=_app.Map;
        if(map==null||!_app.Ready||!map.NativeMapOpen||map.Ui==null)
        {
            if(_ui!=IntPtr.Zero||_target!=null){Restore();Forget();_ui=IntPtr.Zero;}
            Status="Кнопка дронов: карта не открыта";
            return;
        }
        if(_ui!=map.Ui.Pointer){Restore();Forget();_ui=map.Ui.Pointer;}
        if(_target==null||_target.WasCollected)
        {
            if(_target!=null){_target=null;_parent=null;_applied=false;}
            float now=Time.unscaledTime;
            if(_attempts>=MaxAttempts||now<_nextAttempt)return;
            _attempts++;_nextAttempt=now+DiscoverySeconds;
            try{Discover(map);}
            catch(Exception e){_found.Clear();Status="Кнопка дронов: ошибка поиска — "+e.Message;return;}
            if(_target==null)
            {
                Status=M28DroneButtonPolicy.Describe(false,default)+(_attempts>=MaxAttempts?"":" · поиск "+_attempts+"/"+MaxAttempts);
                return;
            }
        }
        if(_gaveUp)return;
        float tolerance=M27DronePanelPolicy.WriteTolerance;
        if(_applied&&(_target.anchoredPosition-_written).sqrMagnitude>tolerance*tolerance)
        {
            // The game moved the button itself: adopt its position as the new base.
            _applied=false;_appliedDelta=Vector2.zero;
            if(++_overwrites>M27DronePanelPolicy.MaxOverwrites)
            {
                _gaveUp=true;
                Status="Игра сама переставляет кнопку дронов — сдвиг отключён до следующего открытия карты";
                return;
            }
        }
        if(!_applied)_base=_target.anchoredPosition;
        var cam=CanvasCamera(_target);
        if(!Measure(cam,out var button)){_hasBase=false;Status="Кнопка дронов: размеры не определены";return;}
        _baseBox=button;_hasBase=true;
        if(_layers<0)_layers=MapLayerOrder.Sorted(MapTools.Categories).Length;
        float sw=Screen.width,sh=Screen.height;
        _atlas=M27DronePanelPolicy.AtlasPanel(sw,sh,map.PanelExpanded,_layers);
        _placement=M28DroneButtonPolicy.Place(button,_atlas,sw,sh,Mode);
        Apply(_placement,button,cam);
        Status=M28DroneButtonPolicy.Describe(true,_placement)+" · режим: "+M28DroneButtonPolicy.ModeTitle(Mode);
    }

    // Where the game put the button (our offset removed); false when not bound.
    internal bool TryBaseBox(out MapShieldPolicy.Box box)
    {
        box=_baseBox;
        return _hasBase&&_target!=null&&box.Valid;
    }
    // The atlas input shield must cover this control instead of leaving a hole in it.
    internal bool Shielded(Component control)
    {
        try{return _target!=null&&!_target.WasCollected&&control!=null&&!control.WasCollected&&control.transform.IsChildOf(_target);}
        catch{return false;}
    }

    private void Forget()
    {
        _found.Clear();_target=_parent=null;_applied=_gaveUp=_hasBase=false;
        _attempts=_overwrites=_climbed=0;_nextAttempt=0;_appliedDelta=Vector2.zero;Identity="";
    }
    private void ResetOffset()
    {
        Restore();
        _gaveUp=false;_overwrites=0;
    }
    private void Restore()
    {
        try
        {
            if(_applied&&_target!=null&&!_target.WasCollected)
            {
                float tolerance=M27DronePanelPolicy.WriteTolerance;
                if((_target.anchoredPosition-_written).sqrMagnitude<=tolerance*tolerance)_target.anchoredPosition=_base;
            }
        }
        catch{}
        _applied=false;_appliedDelta=Vector2.zero;
    }

    private static Camera CanvasCamera(Component component)
    {
        var canvas=component!=null?component.GetComponentInParent<Canvas>():null;
        var root=canvas!=null?canvas.rootCanvas:null;
        return root!=null&&root.renderMode!=RenderMode.ScreenSpaceOverlay?root.worldCamera:null;
    }
    internal static string PathOf(Transform transform)
    {
        var parts=new List<string>();
        for(var t=transform;t!=null&&parts.Count<24;t=t.parent)parts.Add(t.gameObject.name);
        parts.Reverse();
        return string.Join("/",parts);
    }

    // ---- discovery ----------------------------------------------------------
    private void Discover(MapTools map)
    {
        _found.Clear();Finds++;
        var ui=map.Ui;
        var input=ui._toggleDroneMenuInput;
        IntPtr inputPointer=input!=null?input.Pointer:IntPtr.Zero;
        string inputName=input!=null?input.name??"":"";
        var exclude=ui._droneMenu!=null?ui._droneMenu.transform:null;
        var canvas=ui.GetComponentInParent<Canvas>();
        Transform root=canvas!=null&&canvas.rootCanvas!=null?canvas.rootCanvas.transform:ui.transform;
        bool Matches(Il2CppMonomiPark.SlimeRancher.Input.InputEvent e)
        {
            if(e==null)return false;
            if(inputPointer!=IntPtr.Zero)return e.Pointer==inputPointer||inputName.Length>0&&e.name==inputName;
            return (e.name??"").IndexOf("drone",StringComparison.OrdinalIgnoreCase)>=0;
        }
        // 1. The game's own pointer->input relays bound to the drone-menu InputEvent.
        foreach(var c in root.GetComponentsInChildren<InvokeInputEventOnPointerClick>(true))
            if(c!=null&&Matches(c._event))Add(c,"invoke",exclude);
        foreach(var c in root.GetComponentsInChildren<PerformInputEventOnPointerClick>(true))
            if(c!=null&&Matches(c._event))Add(c,"perform",exclude);
        // 2. Persistent UnityEvent calls of MapUI.ToggleDroneMenu* (Button, EventTrigger, any UnityEvent field).
        if(_found.Count==0)
        {
            foreach(var b in ui.GetComponentsInChildren<Button>(true))
                if(b!=null&&Mentions(b.onClick,"DroneMenu"))Add(b,"button",exclude);
            foreach(var t in ui.GetComponentsInChildren<EventTrigger>(true))
                if(t!=null&&TriggerMentions(t,"DroneMenu"))Add(t,"trigger",exclude);
        }
        if(_found.Count==0)
        {
            var all=ui.GetComponentsInChildren<MonoBehaviour>(true);
            for(int i=0;i<all.Length&&i<MaxComponents;i++)
            {
                var c=all[i];
                if(c!=null&&FieldEventsMention(c,"DroneMenu"))Add(c,"event-field",exclude);
            }
        }
        // 3. A selectable control named like the drone button.
        if(_found.Count==0)
            foreach(var s in ui.GetComponentsInChildren<Selectable>(true))
                if(s!=null&&PathOf(s.transform).IndexOf("drone",StringComparison.OrdinalIgnoreCase)>=0)Add(s,"name",exclude);
        _found.Sort((a,b)=>b.Active.CompareTo(a.Active));
        if(_found.Count==0)return;
        int index=_pick>=0&&_pick<_found.Count?_pick:0;
        var handler=_found[index].Handler;
        var target=Climb(handler,ui.transform);
        var parent=target!=null?target.parent:null;
        _parent=parent!=null?parent.TryCast<RectTransform>():null;
        if(_parent==null){_target=null;return;}
        _target=target;
        Identity=_found[index].Kind+" "+PathOf(target)+(_climbed>0?" (+"+_climbed+" ур.)":"");
        _app.LoggerInstance.Msg("[M28] drone button: "+Identity+" candidates="+_found.Count);
    }
    private void Add(Component component,string kind,Transform exclude)
    {
        var rect=component.transform.TryCast<RectTransform>();
        if(rect==null)return;
        if(exclude!=null&&rect.IsChildOf(exclude))return;
        foreach(var f in _found)if(f.Handler.Pointer==rect.Pointer)return;
        _found.Add(new Candidate{Handler=rect,Kind=kind,Path=PathOf(rect),Active=rect.gameObject.activeInHierarchy});
    }
    private static bool Mentions(UnityEventBase e,string key)
    {
        if(e==null)return false;
        int n=e.GetPersistentEventCount();
        for(int i=0;i<n&&i<16;i++)
        {
            string method=e.GetPersistentMethodName(i);
            if(method!=null&&method.IndexOf(key,StringComparison.OrdinalIgnoreCase)>=0)return true;
        }
        return false;
    }
    private static bool TriggerMentions(EventTrigger trigger,string key)
    {
        var entries=trigger.triggers;
        for(int i=0;entries!=null&&i<entries.Count&&i<16;i++)
            if(Mentions(entries[i]?.callback,key))return true;
        return false;
    }
    // UnityEvent fields of any game behaviour (Il2Cpp reflection, cached per type).
    private bool FieldEventsMention(Component component,string key)
    {
        try
        {
            foreach(var e in EventFields(component))if(Mentions(e,key))return true;
        }
        catch{}
        return false;
    }
    internal IEnumerable<UnityEventBase> EventFields(Component component)
    {
        var type=component.GetIl2CppType();
        string name=type.FullName??type.Name;
        if(!_eventFields.TryGetValue(name,out var fields))
        {
            var list=new List<Il2CppSystem.Reflection.FieldInfo>();
            var baseType=Il2CppInterop.Runtime.Il2CppType.Of<UnityEventBase>();
            var flags=Il2CppSystem.Reflection.BindingFlags.Instance|Il2CppSystem.Reflection.BindingFlags.Public|Il2CppSystem.Reflection.BindingFlags.NonPublic;
            for(var t=type;t!=null&&list.Count<16;t=t.BaseType)
            {
                string ns=t.Namespace??"";
                if(ns.StartsWith("UnityEngine",StringComparison.Ordinal)||ns.StartsWith("System",StringComparison.Ordinal))break;
                var declared=t.GetFields(flags|Il2CppSystem.Reflection.BindingFlags.DeclaredOnly);
                for(int i=0;declared!=null&&i<declared.Length;i++)
                {
                    var field=declared[i];
                    if(field!=null&&baseType.IsAssignableFrom(field.FieldType))list.Add(field);
                }
            }
            fields=list.ToArray();
            _eventFields[name]=fields;
        }
        foreach(var field in fields)
        {
            UnityEventBase value=null;
            try{value=field.GetValue(component)?.TryCast<UnityEventBase>();}catch{}
            if(value!=null)yield return value;
        }
    }
    // The click handler may be the icon inside the visible button: climb while the
    // parent is not much larger and holds no other control.
    private RectTransform Climb(RectTransform handler,Transform stop)
    {
        _climbed=0;
        var current=handler;
        var cam=CanvasCamera(handler);
        for(int level=0;level<MaxClimb;level++)
        {
            if(_climb>=0&&level>=_climb)break;
            var parent=current.parent!=null?current.parent.TryCast<RectTransform>():null;
            if(parent==null||parent.Pointer==stop.Pointer||parent.GetComponent<Canvas>()!=null)break;
            if(_climb<0)
            {
                if(!ScreenBox(current,cam,Vector3.zero,out var inner)||!ScreenBox(parent,cam,Vector3.zero,out var outer))break;
                float a=inner.Width*inner.Height,b=outer.Width*outer.Height;
                if(!(a>0)||b>a*AreaGrowth)break;
                bool other=false;
                foreach(var s in parent.GetComponentsInChildren<Selectable>(true))
                    if(s!=null&&!s.transform.IsChildOf(current)){other=true;break;}
                if(other)break;
            }
            current=parent;_climbed++;
        }
        return current;
    }

    // ---- placement ----------------------------------------------------------
    // Top-left screen box of the button and its visible graphics, our offset removed.
    private bool Measure(Camera cam,out MapShieldPolicy.Box box)
    {
        box=default;
        var offset=_applied?_parent.TransformVector(new Vector3(_appliedDelta.x,_appliedDelta.y,0f)):Vector3.zero;
        if(!ScreenBox(_target,cam,offset,out var union))return false;
        var graphics=_target.GetComponentsInChildren<Graphic>(false);
        for(int i=0;graphics!=null&&i<graphics.Length&&i<MaxGraphics;i++)
        {
            var g=graphics[i];
            if(g==null||!g.enabled||g.color.a<=0f)continue;
            if(ScreenBox(g.rectTransform,cam,offset,out var part))union=M28DroneButtonPolicy.Union(union,part);
        }
        float sw=Screen.width,sh=Screen.height;
        if(!union.Valid||union.Width>sw*MaxScreenFraction||union.Height>sh*MaxScreenFraction)return false;
        box=union;
        return true;
    }
    private bool ScreenBox(RectTransform rect,Camera cam,Vector3 offset,out MapShieldPolicy.Box box)
    {
        box=default;
        if(rect==null||rect.WasCollected)return false;
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
        box=new MapShieldPolicy.Box(x0,y0,x1-x0,y1-y0);
        return box.Valid;
    }
    // Screen offset (top-left pixels) -> parent-local anchoredPosition offset.
    private void Apply(M28DroneButtonPolicy.Placement placement,MapShieldPolicy.Box button,Camera cam)
    {
        Vector2 delta=Vector2.zero;
        if(placement.Moved)
        {
            var from=new Vector2(button.X+button.Width*.5f,Screen.height-(button.Y+button.Height*.5f));
            var to=from+new Vector2(placement.Dx,-placement.Dy);
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent,from,cam,out var a)||
               !RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent,to,cam,out var b))return;
            delta=b-a;
            if(!float.IsFinite(delta.x)||!float.IsFinite(delta.y))return;
        }
        float tolerance=M27DronePanelPolicy.WriteTolerance;
        if((_applied?(delta-_appliedDelta).sqrMagnitude:delta.sqrMagnitude)<=tolerance*tolerance)return;
        _target.anchoredPosition=_base+delta;
        _written=_target.anchoredPosition;
        _appliedDelta=delta;
        _applied=delta.sqrMagnitude>tolerance*tolerance;
        Moves++;
    }

    // ---- diagnostics --------------------------------------------------------
    internal string Describe()=>"mode="+Mode+" found="+Found+" attempts="+_attempts+" finds="+Finds+" moves="+Moves+
        " applied="+_applied+" delta="+_appliedDelta+" gaveUp="+_gaveUp+" overwrites="+_overwrites+
        " base="+(_hasBase?M28TestPolicy.Box(_baseBox.X,_baseBox.Y,_baseBox.Width,_baseBox.Height):"-")+
        " atlas="+(_atlas.Valid?M28TestPolicy.Box(_atlas.X,_atlas.Y,_atlas.Width,_atlas.Height):"-")+
        " placement="+_placement.Side+" "+Math.Round(_placement.Dx)+","+Math.Round(_placement.Dy)+" clear="+_placement.Clear+
        " target="+(Identity.Length>0?Identity:"-")+" | "+Status;
    internal string FindReport()
    {
        var text=new StringBuilder();
        var map=_app.Map;
        text.AppendLine(Describe());
        if(map==null||!map.NativeMapOpen||map.Ui==null){text.AppendLine("native map is not open");return text.ToString();}
        var input=map.Ui._toggleDroneMenuInput;
        text.AppendLine("toggleDroneMenuInput="+(input!=null?input.name:"null")+" pick="+_pick+" climb="+_climb+" climbed="+_climbed);
        for(int i=0;i<_found.Count;i++)
        {
            var f=_found[i];
            var cam=CanvasCamera(f.Handler);
            string box=ScreenBox(f.Handler,cam,Vector3.zero,out var b)?M28TestPolicy.Box(b.X,b.Y,b.Width,b.Height):"-";
            text.AppendLine("#"+i+" "+f.Kind+" active="+f.Active+" box="+box+" "+f.Path);
        }
        if(_target!=null&&!_target.WasCollected)
        {
            var parent=_target.parent;
            text.AppendLine("target="+PathOf(_target)+" anchored="+_target.anchoredPosition+" base="+_base+" anchors="+_target.anchorMin+"-"+_target.anchorMax+
                " pivot="+_target.pivot+" parentLayout="+(parent!=null&&parent.GetComponent<LayoutGroup>()!=null)+
                " fitter="+(_target.GetComponent<ContentSizeFitter>()!=null));
        }
        return text.ToString();
    }
}
