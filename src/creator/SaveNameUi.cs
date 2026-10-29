using UnityEngine;

namespace CreatorToolkit;

// «Удобства» → «Имя сейва…»: own name of the loaded save, shown in the main menu save list.
internal sealed partial class ToolkitUi
{
    private bool _saveNamePage;
    private string _saveNameDraft = "";

    private void OpenSaveNameUi() { _saveNamePage = true; _saveNameDraft = _app.SaveNames.CurrentName; ClearInteraction(); }

    private void SaveNamePage(Rect area)
    {
        var names = _app.SaveNames;
        float w = area.width;
        BeginGroup(area);
        try
        {
            GUI.Label(new Rect(0, 0, w - 220, 38), "Имя сейва", _heading);
            if (Button(new Rect(w - 200, 0, 200, 40), "Назад", _button)) { _saveNamePage = false; ClearInteraction(); return; }
            GUI.Box(new Rect(0, 76, w, 300), GUIContent.none, _card);
            int slot = names.CurrentSlot;
            GUI.Label(new Rect(24, 96, w - 48, 34), slot > 0 ? "Текущий сейв: «Файл " + slot + "»" + (names.CurrentName.Length > 0 ? " · сейчас «" + names.CurrentName + "»" : " · без имени") :
                "Текущий сейв не определён — загрузите мир.", _label);
            _saveNameDraft = TextBox("save-name", new Rect(24, 148, w - 48, 40), _saveNameDraft, CapturePolicy.MaxSaveName, "Например: Ферма на Радужных полях");
            bool enabled = GUI.enabled; GUI.enabled = enabled && slot > 0;
            float bw = (w - 64) / 2;
            if (Button(new Rect(24, 208, bw, 46), "Сохранить имя", _accentButton)) { string name = _saveNameDraft; _app.Queue("save name", () => names.SetCurrentName(name)); }
            GUI.enabled = enabled && slot > 0 && names.CurrentName.Length > 0;
            if (Button(new Rect(40 + bw, 208, bw, 46), "Убрать имя", _button)) { _saveNameDraft = ""; _app.Queue("save name", () => names.SetCurrentName("")); }
            GUI.enabled = enabled;
            GUI.Label(new Rect(24, 272, w - 48, 92), "В главном меню («Файлы сохранений») вместо «Файл " + (slot > 0 ? slot : 1) + "» будет это имя. До " + CapturePolicy.MaxSaveName +
                " символов. Сами сейвы не меняются: имена хранятся в UserData\\CreatorToolkit\\save-names.cfg, удаление мода вернёт обычные подписи.", _muted);
            GUI.Label(new Rect(0, 400, w, 72), names.Status, _tableHeading);
        }
        finally { EndGroup(); }
    }
}
