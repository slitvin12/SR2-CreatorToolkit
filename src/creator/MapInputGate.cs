using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using PanZoomControls = Il2CppMonomiPark.SlimeRancher.UI.Map.MapPanZoomPointerControls;
using PlaceMarkerControls = Il2CppMonomiPark.SlimeRancher.UI.Map.MapClickPlaceMarker;

namespace CreatorToolkit;

// Native half of the icon-size slider fix (rule: MapInputGatePolicy). While a
// gesture belongs to the Atlas panel or one of its cards, the native map's
// pan/zoom (MapPanZoomPointerControls) and click-to-place (MapClickPlaceMarker)
// behaviours are disabled. ExecuteEvents never calls a disabled behaviour, so a
// slider drag cannot pan the map and a panel click cannot drop a waypoint, no
// matter how many markers are drawn or whether a raycast shield is alive.
// Exactly the behaviours this gate disabled are re-enabled when the gesture
// ends, the overlay stops being ready, the atlas closes or the display changes.
internal sealed partial class MapTools
{
    private readonly List<Behaviour> _inputGateSuspended=new();
    private IntPtr _inputGateUi,_inputGateLoggedUi;
    private bool _inputGateClosed;
    internal bool NativeInputGateClosed=>_inputGateClosed;
    internal int NativeInputGateSuspended=>_inputGateSuspended.Count;
    internal int NativeInputGateTransitions {get;private set;}
    internal int NativeInputGateRestoreFailures {get;private set;}

    // Same pointer and the same top-left screen pixels as the published card rects.
    internal bool PointerOverOverlayCard()
    {
        var mouse=Mouse.current;
        if(mouse==null||OverlayCardRects.Count==0)return false;
        var screen=mouse.position.ReadValue();
        var p=new Vector2(screen.x,Screen.height-screen.y);
        for(int i=0;i<OverlayCardRects.Count;i++)if(OverlayCardRects[i].Contains(p))return true;
        return false;
    }

    private void TickNativeInputGate()
    {
        bool closed=false;
        var mouse=Mouse.current;
        if(ShieldOverlayReady&&mouse!=null)
        {
            var left=mouse.leftButton;var right=mouse.rightButton;var middle=mouse.middleButton;
            closed=MapInputGatePolicy.Next(_inputGateClosed,PointerOverOverlayCard(),
                left.isPressed||right.isPressed||middle.isPressed,
                left.wasPressedThisFrame||right.wasPressedThisFrame||middle.wasPressedThisFrame,
                left.wasReleasedThisFrame||right.wasReleasedThisFrame||middle.wasReleasedThisFrame,
                _app.UiPointerCaptured);
        }
        if(closed)CloseNativeInputGate();else OpenNativeInputGate();
    }

    private void CloseNativeInputGate()
    {
        var ui=Ui;
        if(ui==null||ui.WasCollected){OpenNativeInputGate();return;}
        if(_inputGateClosed&&_inputGateUi==ui.Pointer)
        {
            // The native map may re-enable its own handlers (page switch): keep them off.
            for(int i=_inputGateSuspended.Count-1;i>=0;i--)
            {
                var b=_inputGateSuspended[i];
                if(b==null||b.WasCollected){_inputGateSuspended.RemoveAt(i);continue;}
                if(b.enabled)b.enabled=false;
            }
            return;
        }
        OpenNativeInputGate();
        try
        {
            int pan=SuspendNativeHandlers(ui.GetComponentsInChildren<PanZoomControls>(true));
            int place=SuspendNativeHandlers(ui.GetComponentsInChildren<PlaceMarkerControls>(true));
            if(pan+place==0)
            {
                // Handlers live outside MapUI in some layouts: search its root canvas.
                var canvas=ui.GetComponentInParent<Canvas>();
                if(ShieldAlive(canvas)&&ShieldAlive(canvas.rootCanvas))
                {
                    pan=SuspendNativeHandlers(canvas.rootCanvas.GetComponentsInChildren<PanZoomControls>(true));
                    place=SuspendNativeHandlers(canvas.rootCanvas.GetComponentsInChildren<PlaceMarkerControls>(true));
                }
            }
            _inputGateClosed=true;_inputGateUi=ui.Pointer;NativeInputGateTransitions++;
            if(_inputGateLoggedUi!=ui.Pointer)
            {
                _inputGateLoggedUi=ui.Pointer;
                _app.LoggerInstance.Msg($"Atlas input gate: panel owns the pointer; native pan/zoom={pan} place-marker={place} suspended");
            }
        }
        catch{OpenNativeInputGate();throw;}
    }

    private int SuspendNativeHandlers<T>(IEnumerable<T> handlers) where T:Behaviour
    {
        int count=0;
        if(handlers==null)return 0;
        foreach(var handler in handlers)if(SuspendNativeHandler(handler))count++;
        return count;
    }

    private bool SuspendNativeHandler(Behaviour b)
    {
        if(b==null||b.WasCollected||!b.enabled)return false; // never re-enable what this gate did not disable
        b.enabled=false;_inputGateSuspended.Add(b);return true;
    }

    private void OpenNativeInputGate()
    {
        if(!_inputGateClosed&&_inputGateSuspended.Count==0)return;
        foreach(var b in _inputGateSuspended)
        {
            try{if(b!=null&&!b.WasCollected&&!b.enabled)b.enabled=true;}
            catch{NativeInputGateRestoreFailures++;}
        }
        _inputGateSuspended.Clear();_inputGateClosed=false;_inputGateUi=IntPtr.Zero;NativeInputGateTransitions++;
    }
}
