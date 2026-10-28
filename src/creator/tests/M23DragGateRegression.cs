using System;
using System.IO;
namespace CreatorToolkit;
// Icon-size slider (14..32 px) dragged on the Atlas panel used to pan the native
// map by the exact cursor delta (+297 / -297 px). Gesture ownership is pure and
// the native/IMGUI wiring is checked on the source that is compiled into the DLL.
internal static class M23DragGateRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool v,string n)=>check(v,"m23 / drag gate / "+n);
        // Next(closedBefore, overOverlay, anyHeld, pressed, released, modDrag)
        C(MapInputGatePolicy.Next(false,true,false,false,false,false),"hovering the panel closes the gate before any press");
        C(!MapInputGatePolicy.Next(true,false,false,false,false,false),"leaving the panel with no button held reopens the map");
        C(MapInputGatePolicy.Next(false,true,true,true,false,false),"a press on the panel belongs to the panel");
        C(!MapInputGatePolicy.Next(true,false,true,true,false,false),"a press on the map belongs to the map");
        C(MapInputGatePolicy.Next(true,false,true,false,false,false),"a panel drag that leaves the panel stays absorbed");
        C(!MapInputGatePolicy.Next(false,true,true,false,false,false),"a native pan that crosses the panel is never cut in half");
        C(!MapInputGatePolicy.Next(false,true,false,false,true,false),"the release frame of a native pan still reaches the map (OnEndDrag)");
        C(MapInputGatePolicy.Next(true,false,false,false,true,false),"the release frame of a panel gesture stays absorbed");
        C(MapInputGatePolicy.Next(false,false,false,false,false,true)&&MapInputGatePolicy.Next(false,false,true,true,false,true),
          "a captured slider drag never reaches the map");
        // The reported gesture: hover the knob, press, drag 14 -> 32 -> 14 px (+297 / -297 px),
        // partly outside the panel, release outside while the slider still owns the mouse.
        bool gate=MapInputGatePolicy.Next(false,true,false,false,false,false);
        gate=MapInputGatePolicy.Next(gate,true,true,true,false,false);
        bool every=gate;
        for(int frame=0;frame<40;frame++){gate=MapInputGatePolicy.Next(gate,frame<12,true,false,false,true);every&=gate;}
        gate=MapInputGatePolicy.Next(gate,false,false,false,true,true);every&=gate;
        gate=MapInputGatePolicy.Next(gate,false,false,false,false,false);
        C(every&&!gate,"a 14<->32 px slider drag owns every frame and frees the map right after release");
        // IMGUI absorption inside DrawMapOverlay.
        C(MapInputGatePolicy.AbsorbImguiEvent(false,true,false,true,false),"MouseDrag over the panel is used");
        C(MapInputGatePolicy.AbsorbImguiEvent(false,true,false,false,true),"MouseDrag of a captured slider outside the panel is used");
        C(MapInputGatePolicy.AbsorbImguiEvent(true,false,false,true,false)&&MapInputGatePolicy.AbsorbImguiEvent(false,false,true,true,false),
          "MouseDown and MouseUp over the panel are used");
        C(!MapInputGatePolicy.AbsorbImguiEvent(false,true,false,false,false),"map drags away from the panel are left alone");
        C(!MapInputGatePolicy.AbsorbImguiEvent(false,false,false,true,true),"scroll, layout and repaint events are never swallowed");
        // Wiring.
        var gateSource=File.ReadAllText(Path.Combine(root,"src/creator/MapInputGate.cs"));
        var tools=File.ReadAllText(Path.Combine(root,"src/creator/MapTools.cs"));
        var ui=File.ReadAllText(Path.Combine(root,"src/creator/ToolkitUi.cs"));
        var mod=File.ReadAllText(Path.Combine(root,"src/creator/ToolkitMod.cs"));
        C(gateSource.Contains("Il2CppMonomiPark.SlimeRancher.UI.Map.MapPanZoomPointerControls")&&
          gateSource.Contains("Il2CppMonomiPark.SlimeRancher.UI.Map.MapClickPlaceMarker"),"the gate owns the native pan/zoom and click-to-place handlers");
        C(gateSource.Contains("MapInputGatePolicy.Next(_inputGateClosed,PointerOverOverlayCard(),")&&gateSource.Contains("_app.UiPointerCaptured"),
          "the gate uses the pure rule, the published card rects and the slider capture");
        C(gateSource.Contains("if(b==null||b.WasCollected||!b.enabled)return false;"),"the gate never re-enables a handler it did not disable");
        C(gateSource.Contains("OverlayCardRects[i].Contains(p)")&&gateSource.Contains("new Vector2(screen.x,Screen.height-screen.y)"),
          "panel hit-testing uses top-left screen pixels like the published rects");
        C(tools.Contains("TickNativeInputGate();")&&tools.Contains("OpenNativeInputGate();ClearShields();Ui=null;"),
          "tick drives the gate and closing the atlas restores native input");
        C(tools.Contains("if(!_app.Ready){OpenNativeInputGate();return;}"),"an unready session restores native input");
        C(mod.Contains("internal bool UiPointerCaptured=>_ui!=null&&_ui.PointerCaptured;"),"the mod publishes the IMGUI capture");
        C(ui.Contains("internal bool PointerCaptured=>_dragSlider>=0||_dragScroll>=0;"),"slider and scroll drags publish their capture");
        C(ui.Contains("MapInputGatePolicy.AbsorbImguiEvent(down,drag,up,map.PointerOverOverlayCard(),PointerCaptured)")&&
          ui.Contains("current.Use();")&&ui.Contains("GUIUtility.keyboardControl=0"),"DrawMapOverlay uses panel mouse events and drops keyboard focus");
        int absorb=ui.IndexOf("MapInputGatePolicy.AbsorbImguiEvent(",StringComparison.Ordinal);
        int repaintOnly=ui.IndexOf("if(Event.current==null||(Event.current.type!=EventType.Repaint&&Event.current.type!=EventType.ScrollWheel))return;",StringComparison.Ordinal);
        C(absorb>0&&repaintOnly>absorb,"mouse events are absorbed before the repaint-only early return");
    }
}
