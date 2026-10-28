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
            GUI.Label(new Rect(0, 0, area.width - 480, 34),
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
        GUI.Label(new Rect(0, 108, width, 46),
            "Любимая еда берётся из выбранного слота: до 8 порций, одна на голодного слайма. "+
            "Штатные сон, сытость и препятствия учитываются; новая еда не выдаётся.", _muted);
        GUI.Label(new Rect(0, 165, width * .34f, 34), "Слайм / дистанция", _tableHeading);
        GUI.Label(new Rect(width * .35f, 165, width * .37f, 34), "Любимая еда", _tableHeading);
        GUI.Label(new Rect(width * .74f, 165, width * .25f, 34), "Голод / возбуждение", _tableHeading);
        Rect view = new(0, 207, width, 300);
        GUI.Box(view, GUIContent.none, _card);
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
                GUI.Label(new Rect(60, y + 4, width * .32f - 72, careRow-16), hit.Name + "\n" + hit.Distance.ToString("F1") + " м", _wrap);
                CatalogArt(new Rect(width*.35f-10,y+12,40,40),hit.FavoriteReference);
                float foodArt=string.IsNullOrEmpty(hit.FavoriteReference)?0:48;
                GUI.Label(new Rect(width * .35f - 10+foodArt, y + 4, width * .36f - 20-foodArt, careRow-16), hit.Favorite, _muted);
                GUI.Label(new Rect(width * .74f - 10, y + 4, width * .22f, careRow-16),
                    (hit.Hunger * 100).ToString("F0") + "% / " + (hit.Agitation * 100).ToString("F0") +
                    "%\n" + (hit.Hungry ? "Голоден" : "Сыт"), _wrap);
                Fill(new Rect(12, y + careRow-5, width - 72, 1), new Color(.15f, .85f, .78f, .20f));
            }
            if (tools.Hits.Count == 0) GUI.Label(new Rect(16, 12, width - 96, 80),
                tools.Inspect ? "Поблизости нет загруженных слаймов с нативными эмоциями." : "Включите инспектор или обновите список вручную.", _muted);
        }
        finally { EndGroup(); }
        GUI.Label(new Rect(0, 524, width, 60), tools.Status, _muted);
    }
    private void GadgetPage(float width)
    {
        var tools = _app.Gadgets;
        _gadgetSearch = TextBox("gadget-search", new Rect(0, 52, width - 220 - 425, 40), _gadgetSearch);
        bool infinite = tools.InfiniteBuilder;
        if (Button(new Rect(width - 205 - 15 - 410, 52, 410, 40), InfiniteBuilderPolicy.Label(infinite),
            infinite ? _selectedButton : _button))
            _app.Queue("infinite builder", () => tools.SetInfiniteBuilder(!tools.InfiniteBuilder));
        if (Button(new Rect(width - 205, 52, 205, 40), "Обновить чертежи", _button))
            _app.Queue("gadget catalog", () => { tools.RefreshCatalog(); _gadgetSelection = null; });
        float left = (width - 18) * .56f, x = left + 18, right = width - x;
        var entries = tools.Catalog.Where(e => e.Search.Contains(_gadgetSearch.Trim().ToLowerInvariant())).ToList();
        GUI.Box(new Rect(0, 110, left, 402), GUIContent.none, _card);
        _gadgetScroll = BeginScroll(62, new Rect(10, 120, left - 20, 382), _gadgetScroll, entries.Count * 62, 62);
        try
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i]; float y = i * 62 - _gadgetScroll.y;
                if (y < 0 || y + 56 > 382) continue;
                if (Button(new Rect(0, y, left - 72, 56),GUIContent.none,
                    _gadgetSelection == entry ? _selectedButton : _button)) _gadgetSelection = entry;
                if(entry.Definition?.Icon!=null)DrawSprite(new Rect(8,y+7,40,40),entry.Definition.Icon);
                string count=tools.DisplayCount(entry);
                GUI.Label(new Rect(58,y,left-138,52),ButtonContent(entry.Name+"  ·  "+count),
                    ButtonTextStyle(ButtonContent(entry.Name+"  ·  "+count),left-138));
            }
            if (entries.Count == 0) GUI.Label(new Rect(12, 12, left - 80, 110),
                "Нет полученных чертежей по этому запросу.\nОбновите каталог; скрытые чертежи мод не открывает.", _muted);
        }
        finally { EndGroup(); }
        GUI.Box(new Rect(x, 110, right, 402), GUIContent.none, _card);
        if(_gadgetSelection?.Definition?.Icon!=null)DrawSprite(new Rect(x+18,128,64,64),_gadgetSelection.Definition.Icon);
        float art=_gadgetSelection?.Definition?.Icon!=null?80:0;
        GUI.Label(new Rect(x + 18+art, 128, right - 36-art, 74), _gadgetSelection?.Name ?? "Выберите гаджет", _heading);
        GUI.Label(new Rect(x + 18, 214, right - 36, 110),
            "Неподвижная земля перед игроком, нормаль поверхности, полный контакт и отсутствие пересечений. "+
            "Связанные порталы подчиняются ограничениям игры.", _muted);
        bool enabled = GUI.enabled; GUI.enabled = enabled && tools.CanPlace(_gadgetSelection);
        if (Button(new Rect(x + 18, 342, right - 36, 52), "Разместить 1 экземпляр…", _accentButton))
            tools.RequestPlace(_gadgetSelection);
        GUI.enabled = enabled;
        GUI.Label(new Rect(x + 18, 411, right - 36, 83),
            infinite ? "Бесконечный режим: экземпляр не списывается, Преобразователь не трогается. Лимиты и проверки места — штатные." :
            "Нужен изготовленный экземпляр. Размещение — с подтверждением; удаление и возврат — через штатное строительство.", _muted);
        // Bottom row, outside the detail card: the card has no spare height for a third text block plus a button.
        if(Button(new Rect(width-300,522,300,42),"Ресурсы Преобразователя…",_goldButton))
        {_refineryPage=true;_app.Queue("refinery catalog",_app.Refinery.Refresh);}
        GUI.Label(new Rect(0, 529, width-316, 60), tools.Status, _muted);
    }
}