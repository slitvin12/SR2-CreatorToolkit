namespace CreatorToolkit;

// Pure ownership rule for one pointer gesture on the native atlas.
//
// The Atlas panel is drawn with IMGUI. Event.current.Use() stops other IMGUI
// consumers, but it can never stop UGUI ExecuteEvents: the native map's
// MapPanZoomPointerControls (pan/zoom) and MapClickPlaceMarker (pink waypoint)
// still received MouseDown/Drag through the panel, so dragging the icon-size
// slider panned the map by exactly the cursor delta. The raycast shields could
// not prevent it: they fail open above MapShieldPolicy.MaxSources rectangles
// (one per marker, e.g. 1402 resource markers) and a re-activated Graphic has
// no canvas depth until the next canvas rebuild.
//
// true = the gesture belongs to the overlay and the native pointer handlers are
// suspended. A gesture keeps the owner chosen on its press until it ends, so a
// native pan that wanders over the panel is never cut in half (its OnEndDrag
// still arrives) and a slider drag that leaves the panel never becomes a pan.
internal static class MapInputGatePolicy
{
    internal static bool Next(bool closedBefore,bool overOverlay,bool anyHeld,bool pressedThisFrame,
        bool releasedThisFrame,bool modDragActive)
    {
        if(modDragActive)return true;           // a captured slider/scroll drag never reaches the map
        if(pressedThisFrame)return overOverlay; // the press decides who owns the gesture
        if(anyHeld||releasedThisFrame)return closedBefore; // never switch owners mid-gesture or on its release frame
        return overOverlay;                     // idle hover: closed over the panel, open over the map
    }

    // IMGUI half: press/drag/release over the panel, or any part of a captured
    // drag, is used inside DrawMapOverlay so no later OnGUI consumer sees it.
    internal static bool AbsorbImguiEvent(bool mouseDown,bool mouseDrag,bool mouseUp,bool overPanel,bool captured)
        =>(mouseDown||mouseDrag||mouseUp)&&(overPanel||captured);
}
