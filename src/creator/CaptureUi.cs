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
        GUI.Label(new Rect(0, 0, w - 220, 38), "Камера и запись", _heading);
        if (Button(new Rect(w - 200, 0, 200, 40), "Назад", _button)) { _capturePage = false; ClearInteraction(); return; }
        GUI.Label(new Rect(0, 42, w - 220, 28), "Всё выключено по умолчанию; выбор сохраняется в capture.cfg.", _muted);
        bool enabled = GUI.enabled;

        // FOV.
        GUI.Box(new Rect(0, 80, col, 212), GUIContent.none, _card);
        GUI.Label(new Rect(20, 92, lw, 34), "Угол обзора (FOV)", _heading);
        if (Button(new Rect(20, 132, lw, 40), cap.CustomFov ? "Свой угол обзора: ВКЛ" : "Свой угол обзора: ВЫКЛ", cap.CustomFov ? _selectedButton : _button))
        { bool on = !cap.CustomFov; _app.Queue("camera fov", () => cap.SetCustomFov(on)); }
        GUI.Label(new Rect(20, 184, 132, 34), "FOV " + cap.FovValue.ToString("0") + "°", _muted);
        float fov = Slider(63, new Rect(160, 187, lw - 140, 28), cap.FovValue, CapturePolicy.MinFov, CapturePolicy.MaxFov);
        if (Math.Abs(fov - cap.FovValue) > .01f) cap.SetFov(fov);
        GUI.Label(new Rect(20, 226, lw, 58), cap.CustomFov ? "Камера: " + cap.CameraFov.ToString("0") + "° · зум и эффекты игры считаются от вашего угла." :
            "Выкл — угол из настроек игры. Включите, чтобы задать 30–120°.", _muted);

        // Smooth zoom.
        GUI.Box(new Rect(x, 80, col, 212), GUIContent.none, _card);
        GUI.Label(new Rect(x + 20, 92, lw, 34), "Плавный зум", _heading);
        if (Button(new Rect(x + 20, 132, lw, 40), cap.ZoomEnabled ? "Плавный зум: ВКЛ" : "Плавный зум: ВЫКЛ", cap.ZoomEnabled ? _selectedButton : _button))
        { bool on = !cap.ZoomEnabled; _app.Queue("smooth zoom", () => cap.SetZoom(on)); }
        GUI.Label(new Rect(x + 20, 182, 132, 34), "Клавиша", _muted);
        int key = NavigationChoice(new Rect(x + 160, 182, lw - 140, 34), CapturePolicy.ZoomKeys, cap.ZoomKey);
        if (key >= 0 && key != cap.ZoomKey) cap.SetZoomKey(key);
        GUI.Label(new Rect(x + 20, 224, 132, 34), "Зум ×" + cap.ZoomFactor.ToString("0.#"), _muted);
        float zoom = Slider(64, new Rect(x + 160, 227, lw - 140, 28), cap.ZoomFactor, CapturePolicy.MinZoom, CapturePolicy.MaxZoom);
        if (Math.Abs(zoom - cap.ZoomFactor) > .01f) cap.SetZoomFactor(zoom);
        GUI.Label(new Rect(x + 20, 258, lw, 30), cap.ZoomKey == 4 ? "M4 — боковая кнопка мыши. Держите — приближение." : "Держите клавишу — приближение, отпустите — назад.", _muted);

        // FPS counter.
        GUI.Box(new Rect(0, 306, col, 168), GUIContent.none, _card);
        GUI.Label(new Rect(20, 318, lw, 34), "Счётчик FPS", _heading);
        if (Button(new Rect(20, 356, lw, 40), cap.ShowFps ? "Счётчик FPS: ВКЛ" : "Счётчик FPS: ВЫКЛ", cap.ShowFps ? _selectedButton : _button))
            cap.SetShowFps(!cap.ShowFps);
        int corner = NavigationChoice(new Rect(20, 404, lw, 34), FpsCornerTitles, cap.FpsCorner);
        if (corner >= 0 && corner != cap.FpsCorner) cap.SetFpsCorner(corner);
        GUI.Label(new Rect(20, 442, lw, 28), cap.ShowFps ? cap.FpsText + " · виден и в меню игры" : "Кадры в секунду и время кадра в углу экрана.", _muted);

        // Background FPS limit.
        GUI.Box(new Rect(x, 306, col, 168), GUIContent.none, _card);
        GUI.Label(new Rect(x + 20, 318, lw, 34), "FPS в фоне", _heading);
        if (Button(new Rect(x + 20, 356, lw, 40), cap.BackgroundLimit ? "Ограничение в фоне: ВКЛ" : "Ограничение в фоне: ВЫКЛ", cap.BackgroundLimit ? _selectedButton : _button))
            cap.SetBackgroundLimit(!cap.BackgroundLimit);
        int rate = NavigationChoice(new Rect(x + 20, 404, lw, 34), BackgroundTitles, Array.IndexOf(CapturePolicy.BackgroundRates, cap.BackgroundFps));
        if (rate >= 0) cap.SetBackgroundFps(CapturePolicy.BackgroundRates[rate]);
        GUI.Label(new Rect(x + 20, 442, lw, 28), cap.Limiting ? "Сейчас ограничено: " + cap.BackgroundFps + " FPS" : "Alt+Tab — меньше нагрузки; в игре — как было.", _muted);

        // Smooth physics.
        GUI.Box(new Rect(0, 488, w, 104), GUIContent.none, _card);
        GUI.Label(new Rect(20, 498, w - 340, 34), "Плавная физика", _heading);
        GUI.Label(new Rect(20, 534, w - 340, 52), cap.SmoothPhysics ? "Интерполяция включена · тел: " + cap.InterpolatedBodies + ". Слаймы и предметы не дрожат при высоком FPS." :
            "Интерполяция Rigidbody у слаймов и предметов: плавнее движение при FPS выше частоты физики.", _muted);
        if (Button(new Rect(w - 300, 520, 280, 42), cap.SmoothPhysics ? "Плавная физика: ВКЛ" : "Плавная физика: ВЫКЛ", cap.SmoothPhysics ? _selectedButton : _button))
        { bool on = !cap.SmoothPhysics; _app.Queue("smooth physics", () => cap.SetSmoothPhysics(on)); }
        GUI.enabled = enabled;
    }

    // Small FPS readout in the chosen corner: gameplay, our menu, the game's menus and the main menu.
    private GUIStyle _fpsStyle, _fpsShadow;
    internal void DrawFpsCounter()
    {
        var cap = _app.Capture;
        if (cap == null || !cap.ShowFps || Event.current == null || Event.current.type != EventType.Repaint) return;
        if (_app.Ready && _app.Cinema.HideHud) return;
        if (_fpsStyle == null)
        {
            _fpsStyle = new GUIStyle { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _fpsShadow = new GUIStyle { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _fpsShadow.normal.textColor = new Color(0, 0, 0, .85f);
        }
        float fps = cap.FpsValue;
        _fpsStyle.normal.textColor = fps <= 0 ? Color.white : fps >= 55 ? new Color(.55f, 1f, .6f) : fps >= 30 ? new Color(1f, .9f, .45f) : new Color(1f, .5f, .45f);
        var matrix = GUI.matrix; var color = GUI.color;
        try
        {
            GUI.matrix = Matrix4x4.identity;
            string text = cap.FpsText;
            float bw = Mathf.Max(132f, text.Length * 9.6f + 24f), bh = 32f, m = 12f;
            int c = cap.FpsCorner;
            var rect = new Rect(c % 2 == 0 ? m : Screen.width - bw - m, c < 2 ? m : Screen.height - bh - m, bw, bh);
            GUI.color = new Color(0f, 0f, 0f, .55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, _fpsShadow);
            GUI.Label(rect, text, _fpsStyle);
        }
        finally { GUI.matrix = matrix; GUI.color = color; }
    }
}
