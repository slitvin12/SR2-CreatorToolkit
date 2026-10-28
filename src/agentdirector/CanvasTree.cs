using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;
using TmpText = Il2CppTMPro.TMP_Text;
using UiButton = UnityEngine.UI.Button;
using UiSelectable = UnityEngine.UI.Selectable;
using UiSlider = UnityEngine.UI.Slider;
using UiText = UnityEngine.UI.Text;
using UiToggle = UnityEngine.UI.Toggle;

namespace CreatorToolkitAgentDirector;

internal sealed class CanvasHit
{
    internal int Id;
    internal UiSelectable Selectable;
    internal string Path, Name, Text, Kind;
    internal Rect Screen;
    internal bool Interactable, OnScreen;
    internal Vector2 Center => new(Screen.x + Screen.width / 2f, Screen.y + Screen.height / 2f);
}

/// <summary>Active uGUI (Canvas) hierarchy of the native game UI: tree dump and selectable search.</summary>
internal static class CanvasTree
{
    private static Il2CppStructArray<Vector3> _corners;

    internal static Camera CameraFor(Canvas canvas)
    {
        if (canvas == null) return null;
        var root = canvas.rootCanvas ?? canvas;
        if (root.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return root.worldCamera ?? Camera.main;
    }

    /// <summary>Screen-space box in pixels with a top-left origin (same convention as IMGUI).</summary>
    internal static bool ScreenRect(Transform t, Camera cam, out Rect rect)
    {
        rect = default;
        var rt = t == null ? null : t.TryCast<RectTransform>();
        if (rt == null) return false;
        _corners ??= new Il2CppStructArray<Vector3>(4);
        rt.GetWorldCorners(_corners);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            Vector3 world = _corners[i];
            Vector2 p = cam == null ? new Vector2(world.x, world.y) : RectTransformUtility.WorldToScreenPoint(cam, world);
            minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x);
            minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y);
        }
        rect = new Rect(minX, UnityEngine.Screen.height - maxY, maxX - minX, maxY - minY);
        return true;
    }

    internal static bool OnScreen(Rect r) =>
        r.width > 0.5f && r.height > 0.5f && r.x + r.width > 0 && r.y + r.height > 0 &&
        r.x < UnityEngine.Screen.width && r.y < UnityEngine.Screen.height;

    internal static string PathOf(Transform t)
    {
        var names = new List<string>();
        for (int i = 0; t != null && i < 24; i++, t = t.parent) names.Add(t.gameObject.name);
        names.Reverse();
        return string.Join("/", names);
    }

    private static string Clip(string s) => s == null ? null : (s.Length > 160 ? s.Substring(0, 157) + "…" : s);

    internal static string OwnText(GameObject go)
    {
        try
        {
            var tmp = go.GetComponent<TmpText>();
            if (tmp != null) return tmp.text;
            var text = go.GetComponent<UiText>();
            if (text != null) return text.text;
        }
        catch (Exception) { }
        return null;
    }

    internal static string TextUnder(GameObject go)
    {
        try
        {
            var tmp = go.GetComponentInChildren<TmpText>(false);
            if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text)) return tmp.text;
            var text = go.GetComponentInChildren<UiText>(false);
            if (text != null) return text.text;
        }
        catch (Exception) { }
        return null;
    }

    internal static string KindOf(UiSelectable s)
    {
        if (s.TryCast<UiButton>() != null) return "button";
        if (s.TryCast<UiToggle>() != null) return "toggle";
        if (s.TryCast<UiSlider>() != null) return "slider";
        try { return s.GetIl2CppType().Name; } catch (Exception) { return "selectable"; }
    }

    private static List<Canvas> RootCanvases()
    {
        var roots = new List<Canvas>();
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c == null || c.WasCollected || !c.isActiveAndEnabled || !c.isRootCanvas) continue;
            roots.Add(c);
        }
        roots.Sort((a, b) => b.sortingOrder.CompareTo(a.sortingOrder));
        return roots;
    }

    /// <summary>Compact list of active root canvases (cheap; included in /state).</summary>
    internal static void WriteRoots(JsonWriter w)
    {
        w.Arr();
        foreach (var c in RootCanvases())
            w.Obj().P("name", c.gameObject.name).P("sortingOrder", c.sortingOrder).P("renderMode", c.renderMode.ToString())
             .P("children", c.transform.childCount).EndObj();
        w.EndArr();
    }

    /// <summary>Nested tree of active GameObjects under active root canvases, bounded by node budget.</summary>
    internal static int WriteTree(JsonWriter w, int maxNodes, int maxDepth, bool rects, string rootFilter)
    {
        int budget = Math.Max(1, maxNodes);
        w.Arr();
        foreach (var c in RootCanvases())
        {
            if (budget <= 0) break;
            if (!string.IsNullOrEmpty(rootFilter) && c.gameObject.name.IndexOf(rootFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            Node(w, c.transform, CameraFor(c), 0, maxDepth, ref budget, rects, c);
        }
        w.EndArr();
        return budget;
    }

    private static void Node(JsonWriter w, Transform t, Camera cam, int depth, int maxDepth, ref int budget, bool rects, Canvas canvas)
    {
        budget--;
        var go = t.gameObject;
        w.Obj().P("name", go.name);
        if (canvas != null) w.P("sortingOrder", canvas.sortingOrder).P("renderMode", canvas.renderMode.ToString());
        try
        {
            var components = go.GetComponents<Component>();
            var names = new StringBuilder();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                string n = comp.GetIl2CppType().Name;
                if (n == "RectTransform" || n == "CanvasRenderer" || n == "Transform") continue;
                if (names.Length > 0) names.Append(',');
                names.Append(n);
            }
            if (names.Length > 0) w.P("components", names.ToString());
        }
        catch (Exception) { }
        try
        {
            var selectable = go.GetComponent<UiSelectable>();
            if (selectable != null)
            {
                w.P("interactable", selectable.IsInteractable());
                var toggle = selectable.TryCast<UiToggle>();
                if (toggle != null) w.P("isOn", toggle.isOn);
                var slider = selectable.TryCast<UiSlider>();
                if (slider != null) w.P("value", slider.value);
            }
        }
        catch (Exception) { }
        string text = OwnText(go);
        if (!string.IsNullOrWhiteSpace(text)) w.P("text", Clip(text));
        if (rects && ScreenRect(t, cam, out var r))
            w.P("x", r.x, 0).P("y", r.y, 0).P("w", r.width, 0).P("h", r.height, 0);
        int childCount = t.childCount;
        if (childCount > 0)
        {
            if (depth >= maxDepth) w.P("childrenTruncated", childCount);
            else
            {
                w.Arr("children");
                int skipped = 0;
                for (int i = 0; i < childCount; i++)
                {
                    var child = t.GetChild(i);
                    if (child == null || !child.gameObject.activeSelf) continue;
                    if (budget <= 0) { skipped++; continue; }
                    Node(w, child, cam, depth + 1, maxDepth, ref budget, rects, null);
                }
                w.EndArr();
                if (skipped > 0) w.P("childrenTruncated", skipped);
            }
        }
        w.EndObj();
    }

    /// <summary>Active, enabled uGUI selectables (buttons, toggles, sliders…) of the native UI.</summary>
    internal static List<CanvasHit> Selectables(bool includeOffscreen)
    {
        var list = new List<CanvasHit>();
        foreach (var s in Object.FindObjectsByType<UiSelectable>(FindObjectsSortMode.None))
        {
            if (s == null || s.WasCollected || !s.isActiveAndEnabled) continue;
            var hit = new CanvasHit
            {
                Selectable = s, Name = s.gameObject.name, Path = PathOf(s.transform),
                Interactable = s.IsInteractable(), Kind = KindOf(s), Text = Clip(TextUnder(s.gameObject))
            };
            var cam = CameraFor(s.GetComponentInParent<Canvas>());
            hit.OnScreen = ScreenRect(s.transform, cam, out var r) && OnScreen(r);
            hit.Screen = r;
            if (!includeOffscreen && !hit.OnScreen) continue;
            list.Add(hit);
        }
        // Stable ids (cid): reading order top-to-bottom, left-to-right, then hierarchy path.
        list.Sort((p, q) =>
        {
            int c = Mathf.RoundToInt(p.Screen.y).CompareTo(Mathf.RoundToInt(q.Screen.y));
            if (c == 0) c = Mathf.RoundToInt(p.Screen.x).CompareTo(Mathf.RoundToInt(q.Screen.x));
            return c != 0 ? c : string.CompareOrdinal(p.Path, q.Path);
        });
        for (int i = 0; i < list.Count; i++) list[i].Id = i;
        return list;
    }

    internal static void WriteHit(JsonWriter w, CanvasHit h)
    {
        w.Obj().P("id", h.Id).P("kind", h.Kind).P("name", h.Name).P("text", h.Text).P("path", h.Path)
         .P("interactable", h.Interactable).P("onScreen", h.OnScreen)
         .P("x", h.Screen.x, 0).P("y", h.Screen.y, 0).P("w", h.Screen.width, 0).P("h", h.Screen.height, 0).EndObj();
    }
}
