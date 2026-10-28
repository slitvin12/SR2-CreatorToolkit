using System;
using System.Collections.Generic;
using HarmonyLib;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CreatorToolkitAgentDirector;

/// <summary>One interactive IMGUI control drawn by Creator Toolkit in the last Repaint pass.</summary>
internal struct ImguiItem
{
    internal string Kind, Label;
    internal GUIContent Content; // resolved lazily: reading .text marshals a string
    internal int Index;
    internal bool Selected, On, Visible;
    internal Rect Local, Global; // Global = unscaled group-space rect + group origin
    internal float Scale;
    internal int Depth;
}

internal sealed class ImguiView
{
    internal int Id, Index, Depth;
    internal string Kind, Label;
    internal bool Selected, On, Visible;
    internal Rect Global;
    internal float X, Y, W, H; // screen pixels, top-left origin
    internal Vector2 Center => new(X + W / 2f, Y + H / 2f);
}

/// <summary>
/// Harmony postfixes on Creator Toolkit's IMGUI primitives. They only copy struct data into a
/// preallocated double buffer (no per-frame allocations inside the toolkit's measured Draw), track
/// genuine presses, and can force exactly one press on a chosen control ("force" click mode).
/// </summary>
internal static class UiRecorder
{
    private const int Capacity = 1024;
    private static ImguiItem[] _current = new ImguiItem[Capacity], _published = new ImguiItem[Capacity];
    private static int _currentCount, _publishedCount;
    private static bool _recording, _repaint;
    private static string _toggleTitle;
    private static int _toggleDepth;
    private static AccessTools.FieldRef<object, Vector2> _origin;
    private static AccessTools.FieldRef<object, float> _scale;
    private static AccessTools.FieldRef<object, Stack<Rect>> _groups;

    internal static int PublishedFrame = -1, Overflow;
    internal static long Passes, PressSerial;
    internal static int LastPressFrame = -1;
    internal static Rect LastPressGlobal;
    internal static string LastPressKind = "";
    internal static bool Installed;
    internal static readonly List<string> Patched = new(), Missing = new();
    internal static string InstallError = "";

    // Forced press (fallback when synthetic pointer input is not registered).
    internal static bool ForceArmed, ForceApplied;
    internal static Rect ForceGlobal;
    internal static int ForceAppliedFrame = -1;

    internal static void Install(HarmonyLib.Harmony harmony, Type modType, Type uiType)
    {
        if (Installed || modType == null || uiType == null) return;
        Installed = true;
        try
        {
            _origin = AccessTools.FieldRefAccess<Vector2>(uiType, "_groupOrigin");
            _scale = AccessTools.FieldRefAccess<float>(uiType, "_scale");
            _groups = AccessTools.FieldRefAccess<Stack<Rect>>(uiType, "_groupRects");
        }
        catch (Exception e) { InstallError = "field refs: " + e.Message; _origin = null; _scale = null; _groups = null; }
        Patch(harmony, AccessTools.DeclaredMethod(modType, "OnGUI", Type.EmptyTypes), "ToolkitMod.OnGUI", nameof(GuiPrefix), nameof(GuiPostfix));
        Patch(harmony, AccessTools.DeclaredMethod(uiType, "Button", new[] { typeof(Rect), typeof(GUIContent), typeof(GUIStyle), typeof(Rect?) }), "Button", null, nameof(ButtonPostfix));
        Patch(harmony, AccessTools.DeclaredMethod(uiType, "TabButton", new[] { typeof(Rect), typeof(string), typeof(int), typeof(bool) }), "TabButton", null, nameof(TabPostfix));
        Patch(harmony, AccessTools.DeclaredMethod(uiType, "IconButton", new[] { typeof(Rect), typeof(string), typeof(int), typeof(bool), typeof(Rect?), typeof(GUIStyle) }), "IconButton", null, nameof(IconPostfix));
        Patch(harmony, AccessTools.DeclaredMethod(uiType, "NativeCategoryButton", new[] { typeof(Rect), typeof(string), typeof(bool) }), "NativeCategoryButton", null, nameof(CategoryPostfix));
        Patch(harmony, AccessTools.DeclaredMethod(uiType, "NativeRoleButton", new[] { typeof(Rect), typeof(string), typeof(string), typeof(bool) }), "NativeRoleButton", null, nameof(RolePostfix));
        Patch(harmony, AccessTools.DeclaredMethod(uiType, "Toggle", new[] { typeof(Rect), typeof(string), typeof(bool), typeof(string) }), "Toggle", nameof(TogglePrefix), nameof(TogglePostfix));
        Patch(harmony, AccessTools.DeclaredMethod(uiType, "SlimeSwitch", new[] { typeof(Rect), typeof(bool) }), "SlimeSwitch", null, nameof(SwitchPostfix));
    }

    private static void Patch(HarmonyLib.Harmony harmony, System.Reflection.MethodBase target, string label, string prefix, string postfix)
    {
        if (target == null) { Missing.Add(label); return; }
        try
        {
            harmony.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(UiRecorder), prefix)),
                postfix: postfix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(UiRecorder), postfix)));
            Patched.Add(label);
        }
        catch (Exception e) { Missing.Add(label + " (" + e.GetType().Name + ": " + e.Message + ")"); }
    }

    // ---- pass boundaries -------------------------------------------------------------------
    internal static void GuiPrefix()
    {
        try
        {
            var e = Event.current;
            _repaint = e != null && e.type == EventType.Repaint;
        }
        catch (Exception) { _repaint = false; }
        _recording = true;
        _toggleDepth = 0; _toggleTitle = null;
        if (_repaint) _currentCount = 0;
    }

    internal static void GuiPostfix()
    {
        _recording = false;
        if (!_repaint) return;
        _repaint = false;
        (_published, _current) = (_current, _published);
        _publishedCount = _currentCount;
        _currentCount = 0;
        try { PublishedFrame = Time.frameCount; } catch (Exception) { }
        Passes++;
    }

    /// <summary>Scene change / shutdown: forget controls that may reference destroyed state.</summary>
    internal static void Reset()
    {
        _publishedCount = 0; _currentCount = 0; PublishedFrame = -1;
        Array.Clear(_published, 0, _published.Length);
        Array.Clear(_current, 0, _current.Length);
        ForceArmed = false;
    }

    // ---- recording -------------------------------------------------------------------------
    private static Rect ToGlobal(object ui, Rect rect, out float scale)
    {
        Vector2 origin = _origin != null ? _origin(ui) : Vector2.zero;
        scale = _scale != null ? _scale(ui) : 1f;
        return new Rect(rect.x + origin.x, rect.y + origin.y, rect.width, rect.height);
    }

    private static void Record(object ui, Rect rect, string kind, string label, GUIContent content, int index, bool selected, bool on)
    {
        if (_currentCount >= Capacity) { Overflow++; return; }
        Rect global = ToGlobal(ui, rect, out float scale);
        bool visible = true; int depth = 0;
        if (_groups != null)
        {
            var groups = _groups(ui);
            if (groups != null)
            {
                depth = groups.Count;
                float cx = global.x + global.width * .5f, cy = global.y + global.height * .5f;
                foreach (var clip in groups)
                {
                    if (cx < clip.x || cy < clip.y || cx > clip.x + clip.width || cy > clip.y + clip.height) { visible = false; break; }
                }
            }
        }
        ref var item = ref _current[_currentCount++];
        item.Kind = kind; item.Label = label; item.Content = content; item.Index = index;
        item.Selected = selected; item.On = on; item.Visible = visible;
        item.Local = rect; item.Global = global; item.Scale = scale; item.Depth = depth;
    }

    private static bool Same(Rect a, Rect b) =>
        Math.Abs(a.x - b.x) < .75f && Math.Abs(a.y - b.y) < .75f && Math.Abs(a.width - b.width) < .75f && Math.Abs(a.height - b.height) < .75f;

    /// <summary>Wrappers call Button(hit, GUIContent.none, ...) first; replace that anonymous entry.</summary>
    private static void Wrap(object ui, Rect hit, string kind, string label, int index, bool selected)
    {
        if (!_recording || !_repaint) return;
        int last = _currentCount - 1;
        if (last >= 0 && _current[last].Kind == "button" && Same(_current[last].Local, hit))
        {
            ref var item = ref _current[last];
            item.Kind = kind; item.Label = label; item.Content = null; item.Index = index; item.Selected = selected;
            return;
        }
        Record(ui, hit, kind, label, null, index, selected, false);
    }

    private static void Press(object ui, Rect rect, ref bool result, string kind)
    {
        if (ForceArmed && !result && Same(ToGlobal(ui, rect, out _), ForceGlobal))
        {
            result = true;
            ForceArmed = false; ForceApplied = true;
            try { ForceAppliedFrame = Time.frameCount; } catch (Exception) { }
        }
        if (!result) return;
        PressSerial++;
        LastPressGlobal = ToGlobal(ui, rect, out _);
        LastPressKind = kind;
        try { LastPressFrame = Time.frameCount; } catch (Exception) { }
    }

    internal static void ButtonPostfix(object __instance, Rect rect, GUIContent content, ref bool __result)
    {
        if (!_recording) return;
        try
        {
            Press(__instance, rect, ref __result, "button");
            if (_repaint) Record(__instance, rect, "button", null, content, -1, false, false);
        }
        catch (Exception) { }
    }
    internal static void TabPostfix(object __instance, Rect rect, string text, int index, bool selected)
    {
        try { Wrap(__instance, rect, "tab", text, index, selected); } catch (Exception) { }
    }
    internal static void IconPostfix(object __instance, Rect rect, string text, int icon, bool selected, Rect? hitRect)
    {
        try { Wrap(__instance, hitRect ?? rect, "icon", text, icon, selected); } catch (Exception) { }
    }
    internal static void CategoryPostfix(object __instance, Rect rect, string category, bool selected)
    {
        try { Wrap(__instance, rect, "category", category, -1, selected); } catch (Exception) { }
    }
    internal static void RolePostfix(object __instance, Rect rect, string text, string role, bool selected)
    {
        try { Wrap(__instance, rect, "role", text, -1, selected); } catch (Exception) { }
    }
    internal static void TogglePrefix(string title)
    {
        _toggleDepth++; _toggleTitle = title;
    }
    internal static void TogglePostfix()
    {
        if (--_toggleDepth <= 0) { _toggleDepth = 0; _toggleTitle = null; }
    }
    internal static void SwitchPostfix(object __instance, Rect rect, bool enabled, ref bool __result)
    {
        if (!_recording) return;
        try
        {
            Press(__instance, rect, ref __result, "toggle");
            if (_repaint) Record(__instance, rect, "toggle", _toggleTitle ?? "", null, -1, false, enabled);
        }
        catch (Exception) { }
    }

    // ---- snapshot (main thread, outside the toolkit's draw) ----------------------------------
    internal static int PublishedCount => _publishedCount;

    internal static List<ImguiView> Snapshot()
    {
        var list = new List<ImguiView>(_publishedCount);
        for (int i = 0; i < _publishedCount; i++)
        {
            var item = _published[i];
            string label = item.Label;
            if (label == null && item.Content != null)
            {
                try { label = item.Content.WasCollected ? "" : item.Content.text; } catch (Exception) { label = ""; }
            }
            float s = item.Scale <= 0 ? 1f : item.Scale;
            list.Add(new ImguiView
            {
                Id = i, Kind = item.Kind, Label = label ?? "", Index = item.Index, Depth = item.Depth,
                Selected = item.Selected, On = item.On, Visible = item.Visible, Global = item.Global,
                X = item.Global.x * s, Y = item.Global.y * s, W = item.Global.width * s, H = item.Global.height * s
            });
        }
        return list;
    }

    internal static void WriteItem(JsonWriter w, ImguiView v)
    {
        w.Obj().P("id", v.Id).P("kind", v.Kind).P("label", v.Label);
        if (v.Index >= 0) w.P("index", v.Index);
        if (v.Kind == "toggle") w.P("on", v.On);
        if (v.Selected) w.P("selected", true);
        w.P("visible", v.Visible)
         .P("x", v.X, 0).P("y", v.Y, 0).P("w", v.W, 0).P("h", v.H, 0)
         .P("cx", v.Center.x, 0).P("cy", v.Center.y, 0).P("depth", v.Depth).EndObj();
    }
}

/// <summary>Synthetic InputSystem mouse events (same technique as the toolkit's own UI probes).</summary>
internal static class InputSim
{
    internal static bool Down { get; private set; }
    internal static Vector2 LastScreen { get; private set; }

    internal static Mouse RequireMouse()
    {
        var mouse = Mouse.current ?? throw new GateException(409, "InputSystem Mouse.current отсутствует.");
        if (!mouse.enabled) InputSystem.EnableDevice(mouse);
        return mouse;
    }

    internal static bool RealButtonHeld()
    {
        var mouse = Mouse.current;
        return mouse != null && !Down && mouse.leftButton.isPressed;
    }

    /// <summary>Queues pointer state at a screen point (pixels, top-left origin like IMGUI).</summary>
    internal static unsafe void Send(Vector2 screenTopLeft, bool down)
    {
        var mouse = RequireMouse();
        var position = new Vector2(screenTopLeft.x, Screen.height - screenTopLeft.y);
        var buffer = StateEvent.From(mouse, out InputEventPtr ptr, Allocator.Temp);
        try
        {
            InputControlExtensions.WriteValueIntoEvent<Vector2>(mouse.position, position, ptr);
            InputControlExtensions.WriteValueIntoEvent<float>(mouse.leftButton, down ? 1f : 0f, ptr);
            InputSystem.QueueEvent(ptr);
            Down = down; LastScreen = screenTopLeft;
        }
        finally { buffer.Dispose(); }
    }

    /// <summary>Never leave a synthetic button held (scene change, abort, shutdown).</summary>
    internal static void ReleaseIfHeld()
    {
        if (!Down) return;
        try { Send(LastScreen, false); } catch (Exception) { }
        Down = false;
    }
}
