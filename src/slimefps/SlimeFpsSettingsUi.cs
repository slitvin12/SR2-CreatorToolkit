using UnityEngine;

namespace CreatorToolkit;

// SlimeFPS settings window: the Creator Toolkit optimization page (textures/LOD/distance/decor,
// post-effects, runtime FPS) reused unchanged inside a standalone card, plus an FPS-counter toggle.
internal sealed partial class ToolkitUi
{
    internal void DrawSlimeFpsSettings()
    {
        if (Event.current == null) return;
        Styles();
        var cap = _app.Capture;
        Matrix4x4 original = GUI.matrix;
        Color originalColor = GUI.color, originalContent = GUI.contentColor, originalBackground = GUI.backgroundColor;
        try
        {
            GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white;
            float scale = Mathf.Max(.4f, Mathf.Min(1.1f, Mathf.Min((Screen.width - 48f) / 940f, (Screen.height - 48f) / 700f)));
            _scale = scale;
            _groupOrigin = Vector2.zero;
            _groupOrigins.Clear(); _groupRects.Clear();
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            float sw = Screen.width / scale, sh = Screen.height / scale;
            const float width = 940, height = 700;
            Rect window = new((sw - width) / 2, (sh - height) / 2, width, height);
            Fill(new Rect(0, 0, sw, sh), new Color(.19f, .15f, .10f, .22f));
            L.Box(window, GUIContent.none, _frame);
            BeginGroup(window);
            try
            {
                if (cap != null && Button(new Rect(25, 16, 220, 40), cap.ShowFps ? "Счётчик FPS: ВКЛ" : "Счётчик FPS: ВЫКЛ", cap.ShowFps ? _selectedButton : _button))
                    cap.SetShowFps(!cap.ShowFps);
                if (Button(new Rect(width - 125, 16, 100, 40), "Закрыть", _button)) { _app.CloseMenu(); return; }
                _optimizationPage = true;
                OptimizationPage(new Rect(25, 66, width - 50, height - 90));
                if (!_optimizationPage) _app.CloseMenu(); // the page's own «Назад»
            }
            finally { EndGroup(); }
        }
        finally
        {
            GUI.matrix = original;
            GUI.color = originalColor; GUI.contentColor = originalContent; GUI.backgroundColor = originalBackground;
        }
    }
}