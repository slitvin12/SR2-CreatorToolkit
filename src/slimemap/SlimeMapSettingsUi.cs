using UnityEngine;

namespace CreatorToolkit;

// SlimeMap settings window: a compact card over the same jelly primitives as Creator Toolkit.
// Every control writes through NavigationTools, so the live minimap and navigation.cfg stay in sync.
internal sealed partial class ToolkitUi
{
    internal void DrawSlimeMapSettings()
    {
        if (Event.current == null) return;
        Styles();
        var nav = _app.Navigation;
        if (nav == null) return;
        var look = nav.Settings;
        Matrix4x4 original = GUI.matrix;
        Color originalColor = GUI.color, originalContent = GUI.contentColor, originalBackground = GUI.backgroundColor;
        try
        {
            GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white;
            float scale = Mathf.Max(.5f, Mathf.Min(1.1f, Mathf.Min((Screen.width - 48f) / 560f, (Screen.height - 48f) / 640f)));
            _scale = scale;
            _groupOrigin = Vector2.zero;
            _groupOrigins.Clear(); _groupRects.Clear();
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            float sw = Screen.width / scale, sh = Screen.height / scale;
            const float width = 560, height = 640;
            Rect window = new((sw - width) / 2, (sh - height) / 2, width, height);
            Fill(new Rect(0, 0, sw, sh), new Color(.19f, .15f, .10f, .22f));
            L.Box(window, GUIContent.none, _frame);
            BeginGroup(window);
            try
            {
                L.Label(new Rect(28, 20, width - 160, 40), "Миникарта", _heading);
                if (Button(new Rect(width - 120, 22, 92, 40), "Закрыть", _button)) { _app.CloseMenu(); return; }
                Fill(new Rect(28, 66, width - 56, 1), new Color(.15f, .85f, .78f, .35f));
                float y = 84;
                float inner = width - 56;

                L.Label(new Rect(28, y, inner, 24), "Режим", _label); y += 28;
                float mw = (inner - 20) / 3;
                for (int i = 0; i < 3; i++)
                    if (Button(new Rect(28 + i * (mw + 10), y, mw, 38), M29NavigationSettings.ModeTitles[i], look.Mode == i ? _selectedButton : _button))
                        nav.SetMode(i);
                y += 50;

                L.Label(new Rect(28, y, inner, 24), "Размер", _label); y += 28;
                for (int i = 0; i < 3; i++)
                    if (Button(new Rect(28 + i * (mw + 10), y, mw, 38), M29NavigationSettings.SizeTitles[i], look.Size == i ? _selectedButton : _button))
                        nav.Edit(s => s.Size = i);
                y += 50;

                L.Label(new Rect(28, y, inner, 24), "Угол экрана", _label); y += 28;
                float cw = (inner - 30) / 4;
                for (int i = 0; i < 4; i++)
                    if (Button(new Rect(28 + i * (cw + 10), y, cw, 38), M29NavigationSettings.CornerTitles[i], look.Corner == i ? _selectedButton : _button))
                        nav.Edit(s => s.Corner = i);
                y += 52;

                L.Label(new Rect(28, y, inner - 90, 24), "Масштаб", _label);
                L.Label(new Rect(width - 110, y, 82, 24), M29NavigationSettings.Metres(look.Range), _muted); y += 26;
                float zoom = Slider(200, new Rect(28, y, inner, 30), M29NavigationSettings.RangeToSlider(look.Range), 0f, 1f);
                nav.SetRange(M29NavigationSettings.SliderToRange(zoom)); y += 44;

                L.Label(new Rect(28, y, inner - 90, 24), "Прозрачность фона", _label);
                L.Label(new Rect(width - 110, y, 82, 24), look.Transparency + "%", _muted); y += 26;
                float transparency = Slider(201, new Rect(28, y, inner, 30), look.Transparency, 0f, 100f);
                nav.Edit(s => s.Transparency = M29NavigationSettings.ClampTransparency((int)transparency)); y += 46;

                bool north = Toggle(new Rect(28, y, inner, 56), "Север сверху", look.NorthUp, "Иначе карта поворачивается вместе с камерой");
                if (north != look.NorthUp) nav.Edit(s => s.NorthUp = north); y += 64;

                bool compass = Toggle(new Rect(28, y, inner, 56), "Компас", look.Compass, "Полоса меток над миникартой");
                if (compass != look.Compass) nav.SetCompass(compass); y += 64;

                L.Label(new Rect(28, y, inner - 90, 24), "Дальность компаса", _label);
                L.Label(new Rect(width - 110, y, 82, 24), look.CompassRange + " м", _muted); y += 26;
                float cr = Slider(202, new Rect(28, y, inner, 30), look.CompassRange, M29NavigationSettings.MinCompassRange, M29NavigationSettings.MaxCompassRange);
                nav.Edit(s => s.CompassRange = M29NavigationSettings.ClampCompassRange((int)cr)); y += 44;

                L.Label(new Rect(28, y, inner - 90, 24), "Меток компаса", _label);
                L.Label(new Rect(width - 110, y, 82, 24), look.CompassMarkers.ToString(), _muted); y += 26;
                float cm = Slider(203, new Rect(28, y, inner, 30), look.CompassMarkers, M29NavigationSettings.MinCompassMarkers, M29NavigationSettings.MaxCompassMarkers);
                nav.Edit(s => s.CompassMarkers = M29NavigationSettings.ClampMarkers((int)cm)); y += 48;

                if (Button(new Rect(28, y, inner, 40), "Сбросить вид", _button)) nav.ResetLook();
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