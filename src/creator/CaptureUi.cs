using System;
using UnityEngine;

namespace CreatorToolkit;

// «Съёмка» → «Камера и запись…»: FOV, smooth zoom, FPS counter, background FPS limit, smooth physics.
internal sealed partial class ToolkitUi
{
    private bool _capturePage;
    private static readonly string[] FpsCornerTitles = { "↖ Слева", "↗ Справа", "↙ Слева", "↘ Справа" };
    private static readonly string[] BackgroundTitles = { "10", "15", "30", "60" };

    private void CapturePage(Rect area)
    {
        var cap = _app.Capture;
        float w = area.width, col = (w - 24) / 2, x = col + 24, lw = col - 40;
        L.Label(new Rect(0, 0, w - 220, 38), "Камера и запись", _heading);
        if (Button(new Rect(w - 200, 0, 200, 40), "Назад", _button)) { _capturePage = false; ClearInteraction(); return; }
        Hint(new Rect(0, 42, w - 220, 28), "Всё выключено по умолчанию; выбор сохраняется в capture.cfg.");
        bool enabled = GUI.enabled;

        // FOV.
        L.Box(new Rect(0, 80, col, 212), GUIContent.none, _card);
        L.Label(new Rect(20, 92, lw, 34), "Угол обзора (FOV)", _heading);
        if (Button(new Rect(20, 132, lw, 40), cap.CustomFov ? "Свой угол обзора: ВКЛ" : "Свой угол обзора: ВЫКЛ", cap.CustomFov ? _selectedButton : _button))
        { bool on = !cap.CustomFov; _app.Queue("camera fov", () => cap.SetCustomFov(on)); }
        L.Label(new Rect(20, 184, 132, 34), "FOV " + cap.FovValue.ToString("0") + "°", _muted);
        float fov = Slider(63, new Rect(160, 187, lw - 140, 28), cap.FovValue, CapturePolicy.MinFov, CapturePolicy.MaxFov);
        if (Math.Abs(fov - cap.FovValue) > .01f) cap.SetFov(fov);
        if (cap.CustomFov) L.Label(new Rect(20, 226, lw, 58), "Камера: " + cap.CameraFov.ToString("0") + "°" + (DetailedHelp ? " · зум и эффекты игры считаются от вашего угла." : ""), _muted);
        else Hint(new Rect(20, 226, lw, 58), "Выкл — угол из настроек игры. Включите, чтобы задать 30–120°.");

        // Smooth zoom.
        L.Box(new Rect(x, 80, col, 212), GUIContent.none, _card);
        L.Label(new Rect(x + 20, 92, lw, 34), "Плавный зум", _heading);
        if (Button(new Rect(x + 20, 132, lw, 40), cap.ZoomEnabled ? "Плавный зум: ВКЛ" : "Плавный зум: ВЫКЛ", cap.ZoomEnabled ? _selectedButton : _button))
        { bool on = !cap.ZoomEnabled; _app.Queue("smooth zoom", () => cap.SetZoom(on)); }
        L.Label(new Rect(x + 20, 182, 132, 34), "Клавиша", _muted);
        int key = NavigationChoice(new Rect(x + 160, 182, lw - 140, 34), CapturePolicy.ZoomKeys, cap.ZoomKey);
        if (key >= 0 && key != cap.ZoomKey) cap.SetZoomKey(key);
        L.Label(new Rect(x + 20, 224, 132, 34), "Зум ×" + cap.ZoomFactor.ToString("0.#"), _muted);
        float zoom = Slider(64, new Rect(x + 160, 227, lw - 140, 28), cap.ZoomFactor, CapturePolicy.MinZoom, CapturePolicy.MaxZoom);
        if (Math.Abs(zoom - cap.ZoomFactor) > .01f) cap.SetZoomFactor(zoom);
        Hint(new Rect(x + 20, 258, lw, 30), cap.ZoomKey == 4 ? "M4 — боковая кнопка мыши. Держите — приближение." : "Держите клавишу — приближение, отпустите — назад.");

        // FPS counter.
        L.Box(new Rect(0, 306, col, 168), GUIContent.none, _card);
        L.Label(new Rect(20, 318, lw, 34), "Счётчик FPS", _heading);
        if (Button(new Rect(20, 356, lw, 40), cap.ShowFps ? "Счётчик FPS: ВКЛ" : "Счётчик FPS: ВЫКЛ", cap.ShowFps ? _selectedButton : _button))
            cap.SetShowFps(!cap.ShowFps);
        int corner = NavigationChoice(new Rect(20, 404, lw, 34), FpsCornerTitles, cap.FpsCorner);
        if (corner >= 0 && corner != cap.FpsCorner) cap.SetFpsCorner(corner);
        if (cap.ShowFps) L.Label(new Rect(20, 442, lw, 28), cap.FpsText + " · виден и в меню игры", _muted);
        else Hint(new Rect(20, 442, lw, 28), "Кадры в секунду и время кадра в углу экрана.");

        // Background FPS limit.
        L.Box(new Rect(x, 306, col, 168), GUIContent.none, _card);
        L.Label(new Rect(x + 20, 318, lw, 34), "FPS в фоне", _heading);
        if (Button(new Rect(x + 20, 356, lw, 40), cap.BackgroundLimit ? "Ограничение в фоне: ВКЛ" : "Ограничение в фоне: ВЫКЛ", cap.BackgroundLimit ? _selectedButton : _button))
            cap.SetBackgroundLimit(!cap.BackgroundLimit);
        int rate = NavigationChoice(new Rect(x + 20, 404, lw, 34), BackgroundTitles, Array.IndexOf(CapturePolicy.BackgroundRates, cap.BackgroundFps));
        if (rate >= 0) cap.SetBackgroundFps(CapturePolicy.BackgroundRates[rate]);
        if (cap.Limiting) L.Label(new Rect(x + 20, 442, lw, 28), "Сейчас ограничено: " + cap.BackgroundFps + " FPS", _muted);
        else Hint(new Rect(x + 20, 442, lw, 28), "Alt+Tab — меньше нагрузки; в игре — как было.");

        // Smooth physics.
        L.Box(new Rect(0, 488, w, 104), GUIContent.none, _card);
        L.Label(new Rect(20, 498, w - 340, 34), "Плавная физика", _heading);
        if (cap.SmoothPhysics) L.Label(new Rect(20, 534, w - 340, 52), "Интерполяция включена · тел: " + cap.InterpolatedBodies + (DetailedHelp ? ". Слаймы и предметы не дрожат при высоком FPS." : ""), _muted);
        else Hint(new Rect(20, 534, w - 340, 52), "Интерполяция Rigidbody у слаймов и предметов: плавнее движение при FPS выше частоты физики.");
        if (Button(new Rect(w - 300, 520, 280, 42), cap.SmoothPhysics ? "Плавная физика: ВКЛ" : "Плавная физика: ВЫКЛ", cap.SmoothPhysics ? _selectedButton : _button))
        { bool on = !cap.SmoothPhysics; _app.Queue("smooth physics", () => cap.SetSmoothPhysics(on)); }
        GUI.enabled = enabled;
    }

    // Small FPS readout in the chosen corner: gameplay, our menu, the game's menus and the main menu.
    // 0.8.0: once the toolkit theme exists (first loaded world) the counter is a parchment HUD pill
    // with a status dot, the native font and ink colours; before that a plain dark pill is used.
    private GUIStyle _fpsStyle, _fpsShadow, _fpsValue, _fpsUnit, _fpsDetail;
    private string _fpsSource, _fpsValueText = "—", _fpsDetailText = "";
    private float _fpsValueWidth, _fpsUnitWidth, _fpsDetailWidth;
    private readonly GUIContent _fpsMeasure = new();
    internal void DrawFpsCounter()
    {
        var cap = _app.Capture;
        if (cap == null || !cap.ShowFps || Event.current == null || Event.current.type != EventType.Repaint) return;
        if (_app.Ready && _app.Cinema.HideHud) return;
        var matrix = GUI.matrix; var color = GUI.color;
        try
        {
            GUI.matrix = Matrix4x4.identity;
            if (_hudFrame != null && _hudLabel != null && _badgeCircle != null && !_badgeCircle.WasCollected) DrawThemedFps(cap);
            else DrawPlainFps(cap);
        }
        finally { GUI.matrix = matrix; GUI.color = color; }
    }

    private void DrawThemedFps(CaptureTools cap)
    {
        if (_fpsValue == null)
        {
            _fpsValue = CopyStyle(_hudLabel); _fpsValue.fontSize = 22; _fpsValue.fontStyle = FontStyle.Bold;
            _fpsValue.alignment = TextAnchor.MiddleLeft; _fpsValue.wordWrap = false; _fpsValue.padding = new RectOffset(0, 0, 0, 0);
            _fpsUnit = CopyStyle(_fpsValue); _fpsUnit.fontSize = 14; _fpsUnit.normal.textColor = TextTint(_ink);
            _fpsDetail = CopyStyle(_fpsUnit); _fpsDetail.fontSize = 15; _fpsDetail.fontStyle = FontStyle.Normal;
            _fpsDetail.normal.textColor = TextTint(Hex(0x665749));
            _fpsSource = null;
        }
        float fps = cap.FpsValue;
        if (!ReferenceEquals(_fpsSource, cap.FpsText))
        {
            // Strings and widths change twice a second, never per repaint.
            _fpsSource = cap.FpsText;
            _fpsValueText = fps > 0 ? Mathf.RoundToInt(fps).ToString(System.Globalization.CultureInfo.InvariantCulture) : "—";
            string ms = Loc.English ? " ms" : " мс";
            _fpsDetailText = fps > 0 ? cap.FpsFrameMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ms +
                (cap.FpsPeakMs > 0 ? (Loc.English ? " · peak " : " · пик ") + cap.FpsPeakMs.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + ms : "") : "";
            _fpsMeasure.text = _fpsValueText; _fpsValueWidth = Mathf.Ceil(_fpsValue.CalcSize(_fpsMeasure).x);
            _fpsMeasure.text = "FPS"; _fpsUnitWidth = Mathf.Ceil(_fpsUnit.CalcSize(_fpsMeasure).x);
            _fpsMeasure.text = _fpsDetailText; _fpsDetailWidth = _fpsDetailText.Length > 0 ? Mathf.Ceil(_fpsDetail.CalcSize(_fpsMeasure).x) : 0f;
        }
        Color state = fps <= 0 ? new Color(.62f, .66f, .70f) : fps >= 55 ? new Color(.16f, .62f, .40f) : fps >= 30 ? new Color(.80f, .56f, .08f) : new Color(.82f, .27f, .25f);
        float bh = 40f, m = 12f, dot = 14f;
        float bw = 16f + dot + 8f + _fpsValueWidth + 5f + _fpsUnitWidth + (_fpsDetailWidth > 0 ? 12f + _fpsDetailWidth : 0f) + 18f;
        int c = cap.FpsCorner;
        var rect = new Rect(c % 2 == 0 ? m : Screen.width - bw - m, c < 2 ? m : Screen.height - bh - m, bw, bh);
        GUI.color = Color.white;
        L.Box(rect, GUIContent.none, _hudFrame);
        float x = rect.x + 16f;
        GUI.color = new Color(Mathf.Min(1f, state.r * 1.25f), Mathf.Min(1f, state.g * 1.25f), Mathf.Min(1f, state.b * 1.25f), 1f);
        GUI.DrawTexture(new Rect(x, rect.y + (bh - dot) / 2f, dot, dot), _badgeCircle);
        GUI.color = Color.white;
        x += dot + 8f;
        _fpsValue.normal.textColor = TextTint(state);
        L.Label(new Rect(x, rect.y, _fpsValueWidth + 2f, bh), _fpsValueText, _fpsValue);
        x += _fpsValueWidth + 5f;
        L.Label(new Rect(x, rect.y + 2f, _fpsUnitWidth + 2f, bh), "FPS", _fpsUnit);
        x += _fpsUnitWidth + 12f;
        if (_fpsDetailWidth > 0) L.Label(new Rect(x, rect.y + 1f, _fpsDetailWidth + 2f, bh), _fpsDetailText, _fpsDetail);
    }

    private string _plainFpsSource, _plainFpsText;
    partial void OnLanguageChanged() { _fpsSource = null; _plainFpsSource = null; }

    private void DrawPlainFps(CaptureTools cap)
    {
        if (_fpsStyle == null)
        {
            _fpsStyle = new GUIStyle { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _fpsShadow = new GUIStyle { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _fpsShadow.normal.textColor = new Color(0, 0, 0, .85f);
        }
        float fps = cap.FpsValue;
        _fpsStyle.normal.textColor = fps <= 0 ? Color.white : fps >= 55 ? new Color(.55f, 1f, .6f) : fps >= 30 ? new Color(1f, .9f, .45f) : new Color(1f, .5f, .45f);
        if (!ReferenceEquals(_plainFpsSource, cap.FpsText))
        {
            _plainFpsSource = cap.FpsText;
            _plainFpsText = Loc.English ? cap.FpsText.Replace(" мс", " ms").Replace(" · пик ", " · peak ") : cap.FpsText;
        }
        string text = _plainFpsText;
        float bw = Mathf.Max(132f, text.Length * 9.6f + 24f), bh = 32f, m = 12f;
        int c = cap.FpsCorner;
        var rect = new Rect(c % 2 == 0 ? m : Screen.width - bw - m, c < 2 ? m : Screen.height - bh - m, bw, bh);
        GUI.color = new Color(0f, 0f, 0f, .55f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        L.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, _fpsShadow);
        L.Label(rect, text, _fpsStyle);
    }
}
