using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CreatorToolkit;

// «Камера и запись» (вкладка «Съёмка»): own FOV, smooth zoom, smooth physics, FPS counter and a
// background FPS limit. Everything is off by default; the choices persist in capture.cfg.
// The camera FOV is written after the game's own update and handed back on release, the
// Rigidbody interpolation and the frame cap are restored exactly when switched off.
internal sealed class CaptureTools
{
    private readonly ToolkitMod _app;
    internal CaptureTools(ToolkitMod app) => _app = app;

    internal bool CustomFov { get; private set; }
    internal float FovValue { get; private set; } = CapturePolicy.DefaultFov;
    internal bool ZoomEnabled { get; private set; }
    internal int ZoomKey { get; private set; }
    internal float ZoomFactor { get; private set; } = CapturePolicy.DefaultZoom;
    internal bool SmoothPhysics { get; private set; }
    internal bool ShowFps { get; private set; }
    internal int FpsCorner { get; private set; } = 1;
    internal bool BackgroundLimit { get; private set; }
    internal int BackgroundFps { get; private set; } = CapturePolicy.DefaultBackgroundFps;
    internal string Status { get; private set; } = "Камера и запись: все функции выключены";
    internal string FilePath => Path.Combine(_app.DataDirectory, "capture.cfg");

    // ── Settings ──────────────────────────────────────────────────────────────────────────
    internal void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var s = CapturePolicy.ParseSettings(File.ReadAllText(FilePath));
            CustomFov = CapturePolicy.Flag(s, "fov.enabled");
            FovValue = CapturePolicy.Fov(CapturePolicy.Number(s, "fov.value", CapturePolicy.DefaultFov));
            ZoomEnabled = CapturePolicy.Flag(s, "zoom.enabled");
            ZoomKey = CapturePolicy.ZoomKey((int)CapturePolicy.Number(s, "zoom.key", 0));
            ZoomFactor = CapturePolicy.Zoom(CapturePolicy.Number(s, "zoom.factor", CapturePolicy.DefaultZoom));
            SmoothPhysics = CapturePolicy.Flag(s, "physics.smooth");
            ShowFps = CapturePolicy.Flag(s, "fps.show");
            FpsCorner = CapturePolicy.Corner((int)CapturePolicy.Number(s, "fps.corner", 1));
            BackgroundLimit = CapturePolicy.Flag(s, "background.limit");
            BackgroundFps = CapturePolicy.BackgroundFps((int)CapturePolicy.Number(s, "background.fps", CapturePolicy.DefaultBackgroundFps));
            // Actors awake while a save loads: the interpolation hook must already be in place.
            if (SmoothPhysics && !_app.EnsureCaptureHooks()) SmoothPhysics = false;
        }
        catch (Exception e) { _app.Fail("capture settings load", e); }
    }

    private void Save()
    {
        try
        {
            var sb = new StringBuilder("# Creator Toolkit: камера и запись\n");
            sb.Append(CapturePolicy.Line("fov.enabled", CustomFov)).Append('\n').Append(CapturePolicy.Line("fov.value", FovValue)).Append('\n')
              .Append(CapturePolicy.Line("zoom.enabled", ZoomEnabled)).Append('\n').Append(CapturePolicy.Line("zoom.key", ZoomKey)).Append('\n')
              .Append(CapturePolicy.Line("zoom.factor", ZoomFactor)).Append('\n').Append(CapturePolicy.Line("physics.smooth", SmoothPhysics)).Append('\n')
              .Append(CapturePolicy.Line("fps.show", ShowFps)).Append('\n').Append(CapturePolicy.Line("fps.corner", FpsCorner)).Append('\n')
              .Append(CapturePolicy.Line("background.limit", BackgroundLimit)).Append('\n').Append(CapturePolicy.Line("background.fps", BackgroundFps)).Append('\n');
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, sb.ToString());
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null); else File.Move(temp, FilePath);
        }
        catch (Exception e) { _app.Fail("capture settings save", e); }
    }

    private void Done(string text) { Status = text; Save(); _app.Notify(text); }

    internal void SetCustomFov(bool on) { if (on == CustomFov) return; CustomFov = on; Done(on ? "Угол обзора мода: " + FovValue.ToString("0") + "°" : "Угол обзора: как в настройках игры"); }
    // Slider drag: applied live, written to disk once the value settles.
    internal void SetFov(float value) { float v = CapturePolicy.Fov(value); if (v == FovValue) return; FovValue = v; _dirty = true; }
    internal void SetZoom(bool on) { if (on == ZoomEnabled) return; ZoomEnabled = on; Done(on ? "Плавный зум: держите " + CapturePolicy.ZoomKeys[ZoomKey] + " (×" + ZoomFactor.ToString("0.#") + ")" : "Плавный зум выключен"); }
    internal void SetZoomKey(int key) { key = CapturePolicy.ZoomKey(key); if (key == ZoomKey) return; ZoomKey = key; Done("Клавиша зума: " + CapturePolicy.ZoomKeys[key]); }
    internal void SetZoomFactor(float value) { float v = CapturePolicy.Zoom(value); if (v == ZoomFactor) return; ZoomFactor = v; _dirty = true; }
    internal void SetShowFps(bool on) { if (on == ShowFps) return; ShowFps = on; ResetFpsWindow(); Done(on ? "Счётчик FPS включён" : "Счётчик FPS выключен"); }
    internal void SetFpsCorner(int corner) { corner = CapturePolicy.Corner(corner); if (corner == FpsCorner) return; FpsCorner = corner; Done("Счётчик FPS: " + CapturePolicy.FpsCorners[corner].ToLowerInvariant()); }
    internal void SetBackgroundLimit(bool on) { if (on == BackgroundLimit) return; BackgroundLimit = on; if (!on) ReleaseLimit(); Done(on ? "Ограничение FPS в фоне: " + BackgroundFps : "Ограничение FPS в фоне выключено"); }
    internal void SetBackgroundFps(int fps) { fps = CapturePolicy.BackgroundFps(fps); if (fps == BackgroundFps) return; BackgroundFps = fps; ReleaseLimit(); Done("FPS в фоне: " + fps); }
    private bool _dirty; private float _dirtySince;

    // ── Camera: FOV and zoom (after the game's update) ───────────────────────────────────────────
    private Camera _camera;
    private float _baseFov, _written = float.NaN, _zoomT;
    private bool _owned;
    internal bool Zooming { get; private set; }
    internal bool TestZoomHold;
    internal float ZoomProgress => _zoomT;
    internal bool CameraOwned => _owned;
    internal float GameFov => _owned ? _baseFov : MainFov();
    internal float CameraFov => MainFov();
    private static float MainFov() { var c = Camera.main; return c == null || c.WasCollected ? 0f : c.fieldOfView; }

    internal void LateTick()
    {
        KeepLimit();
        bool allowed = _app.Ready && _app.Map?.Busy != true;
        bool held = allowed && ZoomEnabled && (TestZoomHold || ZoomHeld());
        Zooming = held;
        _zoomT = CapturePolicy.Step(_zoomT, held ? 1f : 0f, Time.unscaledDeltaTime);
        if (!allowed) _zoomT = 0f;
        if (!allowed || !(CustomFov || _zoomT > 0f)) { Release(); return; }
        var cam = Camera.main;
        if (cam == null || cam.WasCollected) { Release(); return; }
        if (!_owned || _camera == null || _camera.WasCollected || _camera.Pointer != cam.Pointer)
        {
            Release();
            _camera = cam; _baseFov = cam.fieldOfView; _owned = true; _written = float.NaN;
        }
        else if (CapturePolicy.ExternalChange(cam.fieldOfView, _written)) _baseFov = cam.fieldOfView; // follow the game's own FOV changes
        float fov = CapturePolicy.ZoomedFov(CustomFov ? FovValue : _baseFov, ZoomFactor, _zoomT);
        cam.fieldOfView = fov;
        _written = cam.fieldOfView;
    }

    private bool ZoomHeld()
    {
        if (_app.MenuOpen || _app.Map.NativeMapOpen || NativeUiPriority.HasNativeFocus || !WindowFocused()) return false;
        var kb = Keyboard.current; var mouse = Mouse.current;
        switch (ZoomKey)
        {
            case 0: return kb != null && kb.cKey.isPressed;
            case 1: return kb != null && kb.zKey.isPressed;
            case 2: return kb != null && kb.vKey.isPressed;
            case 3: return mouse != null && mouse.middleButton.isPressed;
            default: return mouse != null && mouse.backButton.isPressed;
        }
    }

    private void Release()
    {
        try
        {
            if (_owned && _camera != null && !_camera.WasCollected && float.IsFinite(_written) &&
                !CapturePolicy.ExternalChange(_camera.fieldOfView, _written))
                _camera.fieldOfView = _baseFov;
        }
        finally { _owned = false; _camera = null; _written = float.NaN; }
    }

    // ── Smooth physics: Rigidbody interpolation on actors ──────────────────────────────────────
    private readonly Dictionary<IntPtr, Rigidbody> _bodies = new();
    private bool _sweepPending;
    private float _nextPrune;
    internal int InterpolatedBodies => _bodies.Count;
    internal int SweptActors { get; private set; }

    internal void SetSmoothPhysics(bool on)
    {
        if (on == SmoothPhysics) return;
        if (on)
        {
            if (!_app.EnsureCaptureHooks()) throw new InvalidOperationException("Перехватчик актёров не установлен: " + CaptureHooks.InstallError);
            SmoothPhysics = true; _sweepPending = true;
        }
        else { SmoothPhysics = false; RestoreBodies(); }
        Done(on ? "Плавная физика: интерполяция слаймов и предметов включена" : "Плавная физика выключена, интерполяция возвращена");
    }

    // Called from the IdentifiableActor.Awake postfix and the one-time sweep. Only bodies that
    // had no interpolation are touched, so the game's or another mod's choice is kept.
    internal void Interpolate(Component actor)
    {
        if (!SmoothPhysics || actor == null || actor.WasCollected) return;
        var body = actor.GetComponent<Rigidbody>();
        if (body == null || body.WasCollected || body.interpolation != RigidbodyInterpolation.None) return;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        _bodies[body.Pointer] = body;
    }

    private void Sweep()
    {
        _sweepPending = false;
        int seen = 0;
        foreach (var actor in UnityEngine.Object.FindObjectsOfType<IdentifiableActor>())
        { seen++; Interpolate(actor); }
        SweptActors = seen;
    }

    private void RestoreBodies()
    {
        foreach (var body in _bodies.Values)
        {
            try { if (body != null && !body.WasCollected && body.interpolation == RigidbodyInterpolation.Interpolate) body.interpolation = RigidbodyInterpolation.None; }
            catch { /* destroyed with its actor */ }
        }
        _bodies.Clear();
    }

    private void Prune()
    {
        var dead = new List<IntPtr>();
        foreach (var pair in _bodies)
        {
            bool alive;
            try { alive = pair.Value != null && !pair.Value.WasCollected; } catch { alive = false; }
            if (!alive) dead.Add(pair.Key);
        }
        foreach (var key in dead) _bodies.Remove(key);
    }

    // ── FPS counter and the background limit (every frame, menu or gameplay) ──────────
    private int _frames; private float _elapsed, _worst;
    internal string FpsText { get; private set; } = "FPS —";
    internal float FpsValue { get; private set; }
    private void ResetFpsWindow() { _frames = 0; _elapsed = _worst = 0f; FpsText = "FPS —"; FpsValue = 0f; }
    private M25ScalarLease<int> _rateLease, _vsyncLease;
    private bool _limited;
    internal bool Limiting => _limited;

    // Unity keeps Application.isFocused = true for a window that was never activated (started in
    // the background, minimized from outside), so the foreground window is checked as well.
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    private float _nextFocusProbe;
    private bool _foreground = true;
    private bool WindowFocused()
    {
        if (!Application.isFocused) return false;
        float now = Time.unscaledTime;
        if (now >= _nextFocusProbe)
        {
            _nextFocusProbe = now + .25f;
            try
            {
                IntPtr window = GetForegroundWindow();
                _foreground = window == IntPtr.Zero || GetWindowThreadProcessId(window, out uint pid) == 0 || pid == (uint)Environment.ProcessId;
            }
            catch { _foreground = true; }
        }
        return _foreground;
    }

    internal void Tick()
    {
        if (ShowFps)
        {
            float dt = Time.unscaledDeltaTime;
            if (float.IsFinite(dt) && dt > 0f) { _frames++; _elapsed += dt; if (dt > _worst) _worst = dt; }
            if (_elapsed >= CapturePolicy.FpsWindow) { FpsText = CapturePolicy.FpsLabel(_frames, _elapsed, _worst); FpsValue = _frames / _elapsed; _frames = 0; _elapsed = _worst = 0f; }
        }
        bool want = CapturePolicy.LimitBackground(BackgroundLimit, WindowFocused(), _app.Benchmark?.Active == true);
        if (want && !_limited) ApplyLimit();
        else if (!want && _limited) ReleaseLimit();
        if (_dirty && Time.unscaledTime - _dirtySince > .6f) { _dirty = false; Save(); }
        else if (!_dirty) _dirtySince = Time.unscaledTime;
        if (SmoothPhysics && _app.Ready)
        {
            if (_sweepPending) Sweep();
            if (Time.unscaledTime >= _nextPrune) { _nextPrune = Time.unscaledTime + 20f; Prune(); }
        }
    }

    private void ApplyLimit()
    {
        _vsyncLease = new M25ScalarLease<int>(() => QualitySettings.vSyncCount, v => QualitySettings.vSyncCount = v, 0);
        _rateLease = new M25ScalarLease<int>(() => Application.targetFrameRate, v => Application.targetFrameRate = v, BackgroundFps);
        try { _vsyncLease.Apply(); _rateLease.Apply(); _limited = true; }
        catch { ReleaseLimit(); throw; }
    }

    private void ReleaseLimit()
    {
        try { _rateLease?.Restore(); }
        finally { try { _vsyncLease?.Restore(); } finally { _rateLease = _vsyncLease = null; _limited = false; } }
    }

    // The game re-applies its own frame settings (start-up, options, scene loads). After all Updates
    // the background cap is taken again, and the game's current values become the ones handed back
    // later: nothing older is written over them (the game sets cap and VSync together).
    private int _relimits;
    private void KeepLimit()
    {
        if (!_limited || (Application.targetFrameRate == BackgroundFps && QualitySettings.vSyncCount == 0)) return;
        _relimits++;
        _rateLease = _vsyncLease = null; _limited = false;
        try { ApplyLimit(); }
        catch (Exception e) { _app.Fail("background fps", e); }
    }

    // ── Lifetime ──────────────────────────────────────────────────────────────────────────
    // Save change / unload: the camera and bodies belong to the old world; settings stay.
    internal void ResetSession()
    {
        Release(); _zoomT = 0f; Zooming = false; TestZoomHold = false;
        _bodies.Clear(); _sweepPending = SmoothPhysics;
    }
    internal void Shutdown()
    {
        if (_dirty) { _dirty = false; Save(); }
        Release(); RestoreBodies(); ReleaseLimit();
    }

    internal string Diagnostics() =>
        "CaptureFov: custom=" + CustomFov + " value=" + FovValue.ToString("0") + " owned=" + _owned + " camera=" + CameraFov.ToString("0.00") + " game=" + GameFov.ToString("0.00") + "\n" +
        "CaptureZoom: enabled=" + ZoomEnabled + " key=" + CapturePolicy.ZoomKeys[ZoomKey] + " factor=" + ZoomFactor.ToString("0.0") + " held=" + Zooming + " t=" + _zoomT.ToString("0.000") + "\n" +
        "CapturePhysics: smooth=" + SmoothPhysics + " hook=" + CaptureHooks.Available + " bodies=" + _bodies.Count + " swept=" + SweptActors + " awakes=" + CaptureHooks.Awakes + "\n" +
        "CaptureFps: show=" + ShowFps + " corner=" + FpsCorner + " text=" + FpsText + " background=" + BackgroundLimit + " fps=" + BackgroundFps + " limiting=" + _limited + " relimits=" + _relimits + " focused=" + Application.isFocused + "/" + _foreground +
        " target=" + Application.targetFrameRate + " vsync=" + QualitySettings.vSyncCount + "\n";

    // Local diagnostic relay: "capture <key> <value>" (same setters as the UI).
    internal bool Handle(string command)
    {
        if (!command.StartsWith("capture ", StringComparison.Ordinal)) return false;
        string[] p = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 3) return true;
        bool on = p[2] == "on" || p[2] == "1";
        float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float n);
        switch (p[1])
        {
            case "fov": _app.Queue("camera fov", () => SetCustomFov(on)); break;
            case "fov-value": SetFov(n); break;
            case "zoom": _app.Queue("smooth zoom", () => SetZoom(on)); break;
            case "zoom-factor": SetZoomFactor(n); break;
            case "zoom-key": SetZoomKey((int)n); break;
            case "zoom-hold": TestZoomHold = on; break;
            case "physics": _app.Queue("smooth physics", () => SetSmoothPhysics(on)); break;
            case "fps": SetShowFps(on); break;
            case "fps-corner": SetFpsCorner((int)n); break;
            case "background": SetBackgroundLimit(on); break;
            case "background-fps": SetBackgroundFps((int)n); break;
        }
        return true;
    }
}

// IdentifiableActor.Awake postfix for «Плавная физика»: installed on the first enable only,
// Priority.Last, never cancels anything; with the option off it returns immediately.
internal static class CaptureHooks
{
    internal static bool Installed { get; private set; }
    internal static bool Available { get; private set; }
    internal static string InstallError { get; private set; } = "";
    internal static int Awakes { get; private set; }

    internal static int Install(HarmonyLib.Harmony harmony)
    {
        if (Installed) return 0;
        Installed = true;
        MethodInfo original = null; HarmonyMethod post = null;
        try
        {
            original = AccessTools.Method(typeof(IdentifiableActor), "Awake", Type.EmptyTypes);
            if (original == null) throw new MissingMethodException(nameof(IdentifiableActor), "Awake");
            var method = AccessTools.Method(typeof(CaptureHooks), nameof(ActorAwake));
            if (method == null) throw new MissingMethodException(nameof(CaptureHooks), nameof(ActorAwake));
            post = new HarmonyMethod(method, HarmonyLib.Priority.Last); // Coexistence: after other mods' patches.
            harmony.Patch(original, postfix: post);
            HookCoexistence.Track(original, post.method);
            Available = true;
            return 1;
        }
        catch (Exception e)
        {
            HookCoexistence.InstallFailed(original, post?.method, e);
            InstallError = "IdentifiableActor.Awake: " + e.Message;
            ToolkitMod.I?.Fail("patch smooth physics", e);
            return 0;
        }
    }

    private static void ActorAwake(IdentifiableActor __instance)
    {
        var app = ToolkitMod.I;
        if (app?.Capture == null || !app.Capture.SmoothPhysics) return;
        try { Awakes++; app.Capture.Interpolate(__instance); }
        catch (Exception e) { HookCoexistence.Fault(nameof(ActorAwake), e); }
    }
}
