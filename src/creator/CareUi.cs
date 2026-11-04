using System;
using System.Linq;
using UnityEngine;

namespace CreatorToolkit;

internal sealed partial class ToolkitUi
{
    private int _creationSection;
    private Vector2 _careScroll, _gadgetScroll;
    private string _gadgetSearch = "";
    private GadgetTools.Entry _gadgetSelection;
    // 0.8.5: the filtered blueprint list is rebuilt only when the search or the catalog changes (it was a
    // LINQ copy plus one lower-cased search string per entry on every GUI event).
    private readonly System.Collections.Generic.List<GadgetTools.Entry> _gadgetView = new();
    private string _gadgetViewSearch; private int _gadgetViewVersion = -1;
    private System.Collections.Generic.List<GadgetTools.Entry> GadgetView(GadgetTools tools)
    {
        string search = _gadgetSearch ?? "";
        if (_gadgetViewVersion == tools.Version && string.Equals(_gadgetViewSearch, search)) return _gadgetView;
        _gadgetViewVersion = tools.Version; _gadgetViewSearch = search;
        string needle = search.Trim().ToLowerInvariant();
        _gadgetView.Clear();
        foreach (var e in tools.Catalog) if (needle.Length == 0 || e.Search.Contains(needle)) _gadgetView.Add(e);
        return _gadgetView;
    }
    private void CatalogArt(Rect rect,string reference)
    {
        if(string.IsNullOrEmpty(reference))return;
        foreach(var entry in _app.Actors.Catalog)
            if(entry.Reference==reference){SpritePreview(rect,entry);return;}
    }
    private void ResetCreationUi()
    {
        _creationSection = 0; _careScroll = _gadgetScroll = Vector2.zero;
        _gadgetSearch = ""; _gadgetSelection = null;
        _refineryPage=_refineryDropdown=false;_refinerySelection="";_refineryScroll=Vector2.zero;
    }
    internal void ShowCreationForTest(int section)
    {
        ShowTabForTest(1); _creationSection = Mathf.Clamp(section, 0, 2);
    }
    internal bool CreationSectionVisibleForTest(int section) => _app.MenuOpen && _tab == 1 && _creationSection == section;
    private void CreationNavigation(float width)
    {
        string[] names = { "Каталог", "Забота", "Гаджеты" };
        const float w = 146;
        for (int i = 0; i < names.Length; i++)
            if (Button(new Rect(width - 3 * (w + 8) + i * (w + 8), 0, w, 34), names[i],
                _creationSection == i ? _selectedButton : _button))
            {
                _creationSection = i; ClearInteraction();
                if (i == 1 && !_app.Care.Inspect) _app.Care.SetInspect(true);
                if (i == 2) { _app.Gadgets.RefreshCatalog(); _gadgetSelection = null; }
            }
    }
    private void CreationPage(Rect area)
    {
        if (_creationSection == 0) { SpawnPage(area); return; }
        BeginGroup(area);
        try
        {
            if(_refineryPage){RefineryPage(area);return;}
            L.Label(new Rect(0, 0, area.width - 480, 34),
                _creationSection == 1 ? "Забота о слаймах · до 25 м" : "Штатные гаджеты и декорации", _heading);
            CreationNavigation(area.width);
            if (_creationSection == 1) CarePage(area.width);
            else GadgetPage(area.width);
        }
        finally { EndGroup(); }
    }
    private void CarePage(float width)
    {
        var tools = _app.Care;
        float careButtonWidth=M26ListPolicy.CareButtonWidth(width);
        if (Button(new Rect(0, 52, careButtonWidth, 42), tools.Inspect ? "Инспектор: ВКЛ" : "Инспектор: ВЫКЛ",
            tools.Inspect ? _selectedButton : _button))
            _app.Queue("slime inspector", () => tools.SetInspect(!tools.Inspect));
        if (Button(new Rect(careButtonWidth+12, 52, careButtonWidth, 42), "Обновить рядом", _button)) _app.Queue("slime inspection", tools.ScanNow);
        if (Button(new Rect(2*(careButtonWidth+12), 52, careButtonWidth, 42), "Накормить вокруг…", _accentButton)) tools.RequestFeed();
        L.Label(new Rect(0, 108, width, 46),
            "Любимая еда берётся из выбранного слота: до 8 порций, одна на голодного слайма. "+
            "Штатные сон, сытость и препятствия учитываются; новая еда не выдаётся.", _muted);
        L.Label(new Rect(0, 165, width * .34f, 34), "Слайм / дистанция", _tableHeading);
        L.Label(new Rect(width * .35f, 165, width * .37f, 34), "Любимая еда", _tableHeading);
        L.Label(new Rect(width * .74f, 165, width * .25f, 34), "Голод / возбуждение", _tableHeading);
        Rect view = new(0, 207, width, 300);
        L.Box(view, GUIContent.none, _card);
        float careRow=82;
        foreach(var hit in tools.Hits)
        {
            float nameHeight=_wrap.CalcHeight(ButtonContent(hit.Name+"\n"+hit.Distance.ToString("F1")+" м"),width*.32f-72);
            float foodHeight=_muted.CalcHeight(ButtonContent(hit.Favorite),width*.36f-68);
            careRow=Mathf.Max(careRow,Mathf.Max(nameHeight,foodHeight)+24);
        }
        _careScroll = BeginScroll(61, new Rect(10, 217, width - 20, 280), _careScroll, tools.Hits.Count * careRow, careRow);
        try
        {
            for (int i = 0; i < tools.Hits.Count; i++)
            {
                var hit = tools.Hits[i]; float y = i * careRow - _careScroll.y;
                if (y+careRow<=0 || y>=280) continue;
                CatalogArt(new Rect(10,y+12,40,40),hit.Reference);
                L.Label(new Rect(60, y + 4, width * .32f - 72, careRow-16), hit.Name + "\n" + hit.Distance.ToString("F1") + " м", _wrap);
                CatalogArt(new Rect(width*.35f-10,y+12,40,40),hit.FavoriteReference);
                float foodArt=string.IsNullOrEmpty(hit.FavoriteReference)?0:48;
                L.Label(new Rect(width * .35f - 10+foodArt, y + 4, width * .36f - 20-foodArt, careRow-16), hit.Favorite, _muted);
                L.Label(new Rect(width * .74f - 10, y + 4, width * .22f, careRow-16),
                    (hit.Hunger * 100).ToString("F0") + "% / " + (hit.Agitation * 100).ToString("F0") +
                    "%\n" + (hit.Hungry ? "Голоден" : "Сыт"), _wrap);
                Fill(new Rect(12, y + careRow-5, width - 72, 1), new Color(.15f, .85f, .78f, .20f));
            }
            if (tools.Hits.Count == 0) L.Label(new Rect(16, 12, width - 96, 80),
                tools.Inspect ? "Поблизости нет загруженных слаймов с нативными эмоциями." : "Включите инспектор или обновите список вручную.", _muted);
        }
        finally { EndGroup(); }
        L.Label(new Rect(0, 524, width, 60), tools.Status, _muted);
    }
    // 0.8.5: search and refresh on top; the list and the chosen item side by side (icon, name, stock,
    // what the current modes mean, the place button at the foot of the card); the build modes and the
    // refinery in one row under the cards; the status as one quiet line.
    private void GadgetPage(float width)
    {
        var tools = _app.Gadgets;
        _gadgetSearch = TextBox("gadget-search", new Rect(0, 52, width - 205 - 12, 40), _gadgetSearch, 60, "Поиск чертежа…");
        if (Button(new Rect(width - 205, 52, 205, 40), "Обновить чертежи", _button))
            _app.Queue("gadget catalog", () => { tools.RefreshCatalog(); _gadgetSelection = null; });
        bool infinite = tools.InfiniteBuilder, free = _app.FreePlacement.Enabled;
        const float top = 106, cardH = 400;
        float left = (width - 18) * .56f, x = left + 18, right = width - x;
        var entries = GadgetView(tools);
        L.Box(new Rect(0, top, left, cardH), GUIContent.none, _card);
        _gadgetScroll = BeginScroll(62, new Rect(10, top + 10, left - 20, cardH - 20), _gadgetScroll, entries.Count * 62, 62);
        try
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i]; float y = i * 62 - _gadgetScroll.y;
                if (y + 56 <= 0 || y >= cardH - 20) continue;
                Rect face = new(0, y, left - 72, 56);
                if (Button(face, GUIContent.none, _gadgetSelection == entry ? _selectedButton : _button)) _gadgetSelection = entry;
                float offset = JellyOffset(face);
                if (entry.Definition?.Icon != null) DrawSprite(RowChild(new Rect(8, y + 7, 40, 40), offset, true), entry.Definition.Icon);
                string count = tools.DisplayCount(entry);
                var caption = ButtonContent(entry.Name + "  ·  " + count);
                HoverText(RowChild(new Rect(58, y, left - 138, 52), offset), caption, ButtonTextStyle(caption, left - 138), offset);
            }
            if (entries.Count == 0) L.Label(new Rect(12, 12, left - 80, 110),
                "Нет полученных чертежей по этому запросу.\nОбновите каталог; скрытые чертежи мод не открывает.", _muted);
        }
        finally { EndGroup(); }
        // The chosen item: icon, name and stock on top, the modes in two short lines, the button at the foot.
        L.Box(new Rect(x, top, right, cardH), GUIContent.none, _card);
        var chosen = _gadgetSelection;
        var icon = chosen?.Definition?.Icon;
        float art = icon != null ? 86 : 0, tx = x + 20 + art, tw = right - 40 - art;
        if (icon != null) DrawSprite(new Rect(x + 20, top + 18, 72, 72), icon);
        L.Label(new Rect(tx, top + 16, tw, 44), Ellipsize(chosen?.Name ?? "Выберите гаджет", tw, _heading), _heading);
        L.Label(new Rect(tx, top + 58, tw, 30), chosen == null ? "Слева — полученные чертежи" : "В наличии: " + tools.DisplayCount(chosen), _muted);
        float fy = top + 108;
        Fill(new Rect(x + 20, fy - 8, right - 40, 1), new Color(.85f, .80f, .72f, .8f));
        GadgetFact(new Rect(x + 20, fy, right - 40, 28), infinite ? "Экземпляр не списывается" : "Списывается 1 экземпляр", infinite);
        GadgetFact(new Rect(x + 20, fy + 30, right - 40, 28), free ? "Без проверки пересечений и уклона" : "Штатные проверки места", free);
        Hint(new Rect(x + 20, fy + 70, right - 40, 92),
            "Неподвижная земля перед игроком, нормаль поверхности, полный контакт и отсутствие пересечений. "+
            "Связанные порталы подчиняются ограничениям игры.");
        Hint(new Rect(x + 20, fy + 164, right - 40, 50),
            infinite ? "Бесконечный режим: Преобразователь не трогается." :
            "Нужен изготовленный экземпляр. Удаление и возврат — через штатное строительство.", _small);
        bool enabled = GUI.enabled; GUI.enabled = enabled && tools.CanPlace(_gadgetSelection);
        if (Button(new Rect(x + 20, top + cardH - 20 - 52, right - 40, 52), "Разместить 1 экземпляр…", _accentButton))
            tools.RequestPlace(_gadgetSelection);
        GUI.enabled = enabled;
        // Build modes and the refinery: one row under the cards.
        float bw = (width - 24) / 3f, by = top + cardH + 14;
        if (Button(new Rect(0, by, bw, 42), InfiniteBuilderPolicy.Label(infinite), infinite ? _selectedButton : _button))
            _app.Queue("infinite builder", () => tools.SetInfiniteBuilder(!tools.InfiniteBuilder));
        // «Свободная установка» (native build mode): clipping, any slope, overhangs and a longer reach.
        if (Button(new Rect(bw + 12, by, bw, 42), CapturePolicy.FreePlacementLabel(free), free ? _selectedButton : _button))
            _app.Queue("free placement", () => _app.FreePlacement.Set(!_app.FreePlacement.Enabled));
        if (Button(new Rect(2 * (bw + 12), by, bw, 42), "Ресурсы Преобразователя…", _goldButton))
        { _refineryPage = true; _app.Queue("refinery catalog", _app.Refinery.Refresh); }
        string status = free ? _app.FreePlacement.Status : tools.Status;
        L.Label(new Rect(0, by + 48, width, 24), Ellipsize(status, width, _small), _small);
    }
    // One short line about a build mode: a mint dot when the mode is on.
    private void GadgetFact(Rect rect, string text, bool special)
    {
        var saved = GUI.color;
        GUI.color = special ? _accent : new Color(.70f, .64f, .56f, 1f);
        try { GUI.DrawTexture(new Rect(rect.x, rect.center.y - 5, 10, 10), MiniDiscTexture(false)); }
        finally { GUI.color = saved; }
        L.Label(new Rect(rect.x + 18, rect.y, rect.width - 18, rect.height), Ellipsize(text, rect.width - 18, _muted), _muted);
    }
}