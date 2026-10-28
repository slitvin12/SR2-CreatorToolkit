using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Il2CppMonomiPark.SlimeRancher.UI.Map;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace CreatorToolkit;

internal sealed partial class FeatureSuite
{
    private NativePointerLease _mapInputPointer;
    private bool _mapInputOwned,_mapInputOldClick,_mapInputOldCustom;
    private readonly List<object> _mapInputProof=new();
    private Vector2 _mapInputStart,_mapBeforePan;
    private Vector3 _mapInputAnchor;
    private float _mapBeforeZoom;
    private int _mapInputHandoffs,_mapShieldPeak,_mapUiPeak;
    private static bool NativeNavigationReady=>EventSystem.current!=null&&EventSystem.current.sendNavigationEvents&&
        Cursor.lockState!=CursorLockMode.Locked;
    private void RetireNativeWindowProbe()
    {
        _mapInputPointer?.Dispose();_mapInputPointer=null;
        if(_mapInputOwned)
        {
            _app.Map.CloseNative();
            _app.Map.ClickTeleport=_mapInputOldClick;_app.Map.CustomPointMode=_mapInputOldCustom;
        }
        _mapInputOwned=false;
    }
    private void MapInputRow(string phase)
    {
        var mouse=Mouse.current;var events=EventSystem.current;
        var module=events?.currentInputModule?.TryCast<InputSystemUIInputModule>();
        var hits=new Il2CppSystem.Collections.Generic.List<RaycastResult>();
        if(events!=null&&mouse!=null)
            events.RaycastAll(new PointerEventData(events){position=mouse.position.ReadValue()},hits);
        var hitRows=new List<object>();
        for(int i=0;i<Math.Min(4,hits.Count);i++)
            hitRows.Add(new{Object=hits[i].gameObject?.name,
                NativePanZoomParent=hits[i].gameObject?.GetComponentInParent<MapPanZoomPointerControls>()?.name});
        var controls=new List<object>();
        if(_app.Map.Ui!=null)
            foreach(var control in _app.Map.Ui.GetComponentsInChildren<MapPanZoomPointerControls>(true))
            {
                if(controls.Count>=8)break;
                controls.Add(new{Object=control.name,Active=control.isActiveAndEnabled,
                    control._isPointerActive,control._isDragging,control._sensitivity,
                    Zoom=control._viewModel?.MapZoom});
            }
        _mapInputProof.Add(new{Phase=phase,NativeMapOpen=_app.Map.NativeMapOpen,
            NativeMapHasInput=_app.Map.NativeMapHasInput,ModOpen=_app.MenuOpen,
            Handoffs=_app.Input.NativeHandoffs,Navigation=EventSystem.current?.sendNavigationEvents,
            CursorLock=Cursor.lockState.ToString(),Shields=_app.Map.ShieldCount,
            Candidates=_app.Map.CachedUiCount,Subscriptions=_app.Map.SubscriptionCount,Frame=Time.frameCount,
            NativeZoom=_app.Map.Ui?._viewModel?.MapZoom,ZoomMin=MapUtils.MIN_ZOOM,ZoomMax=MapUtils.MAX_ZOOM,
            ProjectionScale=_app.Map.NativeMapOpen?MapInputScale():0,
            BeforeWheelScale=_mapBeforeZoom,Mouse=mouse?.position.ReadValue().ToString("F3"),
            Scroll=mouse?.scroll.ReadValue().ToString("F3"),
            ScrollAction=NativeActionSnapshot(module?.scrollWheel),Hits=hitRows,NativeControls=controls,
            NativeOwnership=NativeUiPriority.Diagnostics()});
        File.WriteAllText(Path.Combine(_app.DataDirectory,"native-window-input-proof.json"),JsonSerializer.Serialize(new{
            Copy=_recovery.TestName,Scope="Actual InputSystem mouse/key events and native UI readback on isolated copy; no physical OS-input or weak-PC FPS certification.",
            Rows=_mapInputProof},new JsonSerializerOptions{WriteIndented=true}));
    }
    private float MapInputScale()=>Vector2.Distance(_app.Map.ScreenPoint(_mapInputAnchor),
        _app.Map.ScreenPoint(_mapInputAnchor+Vector3.right*100));
    private void BuildNativeWindowInputSteps()
    {
        Add("NativeInput / native map request while mod owns input",()=>{
            GuardCopy();_mapInputProof.Clear();_mapShieldPeak=_mapUiPeak=0;
            _mapInputOldClick=_app.Map.ClickTeleport;_mapInputOldCustom=_app.Map.CustomPointMode;
            _app.Map.ClickTeleport=_app.Map.CustomPointMode=false;
            _app.Input.Open();Require(_app.MenuOpen,"Mod input baseline missing before native map request.");
            _mapInputHandoffs=_app.Input.NativeHandoffs;_mapInputOwned=true;
            var root=UnityEngine.Object.FindObjectOfType<MapUIRoot>();
            Require(root!=null,"Actual native map input root is unavailable.");
            root.OpenMap(); // Not MapTools.Open: deliberately exercise native takeover.
        },.4f);
        Add("NativeInput / map received exclusive native ownership",()=>{
            GuardCopy();Require(!_app.MenuOpen&&_app.Input.NativeHandoffs>_mapInputHandoffs&&
                NativeNavigationReady&&_app.UiForTests.TextListenerRootsForTest==0,
                "Native map has a dead cursor, mod input or unreleased text listener.");
            _mapInputAnchor=Controller.Motor.TransientPosition;
            _app.Map.Ui._viewModel.CenterOnWorldPosition(new Vector2(_mapInputAnchor.x,_mapInputAnchor.z));
            Require(float.IsFinite(MapUtils.MIN_ZOOM)&&float.IsFinite(MapUtils.MAX_ZOOM)&&
                MapUtils.MAX_ZOOM>MapUtils.MIN_ZOOM,"Actual native map zoom bounds are unavailable.");
            _app.Map.Ui._viewModel.SetMapZoom(Mathf.Lerp(MapUtils.MIN_ZOOM,MapUtils.MAX_ZOOM,.5f));
            // Midpoint of actual native limits: a positive wheel pulse at a
            // clamped maximum would be an invalid "no zoom" fixture.
            _mapInputPointer=new NativePointerLease();
            _mapInputStart=new Vector2(Screen.width*.40f,Screen.height*.48f);
            _mapInputPointer.Set(_mapInputStart,false);MapInputRow("native takeover and real pointer hover");
        },.4f,()=>_app.Map.NativeMapHasInput&&_app.Map.Ui._map!=null&&_app.Map.Ui._viewModel!=null,15f);
        Add("NativeInput / real map pan press",()=>{
            GuardCopy();_mapBeforePan=_app.Map.ScreenPoint(_mapInputAnchor);
            _mapInputStart=new Vector2(Screen.width*.40f,Screen.height*.48f);
            Require(!_app.Map.OverlayContains(_mapInputStart)&&
                !_app.Map.OverlayContains(_mapInputStart+new Vector2(90,36)),
                "Native pan fixture overlaps the mod's marker/card/chrome exclusions.");
            _mapInputPointer.Set(_mapInputStart,true);
        },.12f);
        Add("NativeInput / real map pan drag",()=>{
            GuardCopy();_mapInputPointer.Set(_mapInputStart+new Vector2(45,18),true);
        },.12f);
        Add("NativeInput / real map pan drag continuation",()=>{
            GuardCopy();_mapInputPointer.Set(_mapInputStart+new Vector2(90,36),true);
        },.12f);
        Add("NativeInput / real map pan release",()=>{
            GuardCopy();_mapInputPointer.Set(_mapInputStart+new Vector2(90,36),false);
        },.2f);
        Add("NativeInput / native mouse pan changed map projection",()=>{
            GuardCopy();Require(Vector2.Distance(_mapBeforePan,_app.Map.ScreenPoint(_mapInputAnchor))>5&&
                !_app.MenuOpen&&NativeNavigationReady&&!_app.Map.Busy,
                "Real native mouse drag did not pan the map or started a teleport.");
            _mapBeforeZoom=MapInputScale();MapInputRow("mouse pan before wheel");
            var wheelPoint=_mapInputStart+new Vector2(90,36);
            Require(!_app.Map.OverlayContains(wheelPoint),"Wheel fixture overlaps a projected mod marker/card after pan.");
            _mapInputPointer.Set(wheelPoint,false,new Vector2(0,120));
        },.25f);
        Add("NativeInput / native wheel zoom changed map scale",()=>{
            GuardCopy();MapInputRow("after real positive wheel");
            Require(Mathf.Abs(MapInputScale()-_mapBeforeZoom)>.1f&&
                NativeNavigationReady&&!_app.MenuOpen,"Real native mouse wheel did not zoom the map.");
            _mapBeforeZoom=MapInputScale();
            _mapInputPointer.Set(_mapInputStart+new Vector2(90,36),false,new Vector2(0,-120));
        },.25f);
        Add("NativeInput / reverse native wheel changes map scale",()=>{
            GuardCopy();MapInputRow("after real negative wheel");
            Require(Mathf.Abs(MapInputScale()-_mapBeforeZoom)>.1f&&
                NativeNavigationReady&&!_app.MenuOpen,"Real reverse native mouse wheel did not zoom the map.");
            _mapInputPointer.Set(_mapInputStart+new Vector2(90,36),false);MapInputRow("both native wheel directions");
            _mapShieldPeak=_app.Map.ShieldCount;_mapUiPeak=_app.Map.CachedUiCount;
            ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"native-map-real-pan-zoom.png"));
            _mapInputPointer.Dispose();_mapInputPointer=null;_app.Map.CloseNative();
        },.3f);
        for(int cycle=0;cycle<3;cycle++)
        {
            int round=cycle+1;
            Add("NativeInput / reopen actual native map "+round,()=>{
                GuardCopy();Require(_app.Map.ShieldCount==0,"Closed map retained owned input shields.");
                _app.Map.Open();
            },.3f,()=>!NativeUiPriority.HasNativeFocus,15f);
            Add("NativeInput / bounded map roots and subscriptions "+round,()=>{
                GuardCopy();Require(!_app.MenuOpen&&NativeNavigationReady&&
                    _app.Map.ShieldCount<=_mapShieldPeak+2&&_app.Map.CachedUiCount<=Math.Max(2,_mapUiPeak)&&
                    _app.Map.SubscriptionCount==1,"Repeated map open grew mod shields, cached native roots or subscriptions.");
                MapInputRow("reopen "+round);_app.Map.CloseNative();
            },.4f,()=>_app.Map.NativeMapHasInput,15f);
        }
        Add("NativeInput / native pause key",()=>{
            GuardCopy();RetireNativeWindowProbe();_app.CloseMenu();
            NativeKeyboardProbe.PressAuxiliary(Keyboard.current.escapeKey);
        },.2f,()=>!NativeUiPriority.HasNativeFocus&&Time.timeScale>0,15f);
        Add("NativeInput / pause retains native ownership",()=>{
            GuardCopy();NativeKeyboardProbe.ReleaseAuxiliary();
            Require(NativeUiPriority.HasNativeFocus&&!_app.MenuOpen&&NativeNavigationReady,
                "Native pause did not receive input ownership.");
            _app.Input.Open();Require(!_app.MenuOpen,"Mod stole input from native pause.");
            MapInputRow("native pause");
        },.2f,()=>NativeUiPriority.HasNativeFocus,15f);
        Add("NativeInput / native pause close key",()=>{
            GuardCopy();NativeKeyboardProbe.PressAuxiliary(Keyboard.current.escapeKey);
        },.2f);
        Add("NativeInput / pause closed through native input",()=>{
            GuardCopy();NativeKeyboardProbe.ReleaseAuxiliary();
            Require(!NativeUiPriority.HasNativeFocus&&Time.timeScale>0,
                "Native pause did not close normally; no Time.timeScale override was used.");
            MapInputRow("pause closed");
        },.2f,()=>!NativeUiPriority.HasNativeFocus&&Time.timeScale>0,15f);
        Add("NativeInput / actual native waypoint click and guarded return start",()=>_app.MapProbe.Start(),.2f);
        Add("NativeInput / actual native waypoint click and guarded return",()=>{
            GuardCopy();Require(_app.MapProbe.Status=="PASS","Native map interaction probe: "+_app.MapProbe.Status);
        });
    }
}