using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatorToolkit;

// M27 «Разблокировка» tab (body 1100x594): permanent save unlocks behind a confirmation
// and the «Трекинг мира» lists (WorldTrackingTools). The filtered view is cached and
// rebuilt only when the kind, filter, search text or data version changes.
internal sealed partial class ToolkitUi
{
    private static readonly string[] UnlockSections={"Разблокировка","Трекинг мира"};
    private static readonly string[] UnlockTitles={
        "Открыть всю Слаймопедию","Открыть карту и ноды","Применить все улучшения",
        "Открыть все двери","Открыть все стили слаймов","Открыть все чертежи гаджетов",
        "Открыть все стили вакпака"};
    private static readonly string[] UnlockDetails={
        "Все зарегистрированные записи, включая редких слаймов.",
        "Открытие карты, нод и тумана. Будет показана игровая карта.",
        "Все зарегистрированные улучшения вакпака и персонажа.",
        "Все модели AccessDoor в текущем сейве, включая незагруженные двери.",
        "Классические и секретные стили всех видов слаймов; секретные отмечаются обнаруженными.",
        "Все чертежи гаджетов и декора с префабом; уже полученные пропускаются.",
        "Все раскраски вакпака (VacPalette) из каталога игры; уже полученные пропускаются."};
    // M28: seven cards in a four-row grid (M28UnlockLayout) carry short texts; the
    // full description above stays in the confirmation dialog.
    private static readonly string[] UnlockBlurbs={
        "Все записи, включая редких слаймов.",
        "Карта, ноды и туман; откроется игровая карта.",
        "Все улучшения вакпака и персонажа.",
        "Все двери сейва, даже незагруженные.",
        "Классические и секретные стили всех слаймов.",
        "Чертежи гаджетов и декора; полученные пропускаются.",
        "Все раскраски вакпака; полученные пропускаются."};
    private Action[] _unlockActions;
    private Action[] UnlockActions()=>_unlockActions??=new Action[]{_app.Unlocks.Pedia,_app.Unlocks.Map,_app.Unlocks.Upgrades,_app.Unlocks.Doors,
        _app.Unlocks.Styles,_app.Unlocks.Blueprints,_app.Unlocks.Palettes};
    private string UnlockResult(int i)=>i==4?_app.Unlocks.StylesResult:i==5?_app.Unlocks.BlueprintsResult:i==6?_app.Unlocks.PalettesResult:"";
    private int _unlockSection,_trackKind,_trackFilter;
    private string _trackSearch="",_trackSelected="";
    private Vector2 _trackScroll;
    private List<M27TrackingPolicy.Row> _trackView=new();
    private string _trackSummary="",_trackViewSearch;
    private int _trackViewKind=-1,_trackViewFilter=-1,_trackViewVersion=-1;

    private void ResetM27Ui()
    {
        _unlockSection=_trackKind=_trackFilter=0;_trackSearch=_trackSelected="";_trackScroll=Vector2.zero;
        _trackView=new List<M27TrackingPolicy.Row>();_trackSummary="";_trackViewSearch=null;
        _trackViewKind=_trackViewFilter=_trackViewVersion=-1;
    }

    private void UnlockPage(Rect area)
    {
        BeginGroup(area);
        try
        {
            for(int i=0;i<UnlockSections.Length;i++)
                if(Button(new Rect(i*232,0,220,40),UnlockSections[i],_unlockSection==i?_selectedButton:_button))
                {_unlockSection=i;_trackScroll=Vector2.zero;}
            if(_unlockSection==0)UnlockCards(area.width);
            else TrackingPage(area.width);
        }
        finally{EndGroup();}
    }

    private void UnlockCards(float width)
    {
        GUI.Label(new Rect(476,0,width-476,40),"Постоянные изменения сейва · каждое действие с подтверждением",_muted);
        var actions=UnlockActions();
        float col=M28UnlockLayout.Column(width);
        for(int i=0;i<UnlockTitles.Length;i++)
        {
            float x=M28UnlockLayout.X(i,width),y=M28UnlockLayout.Y(i);
            GUI.Box(new Rect(x,y,col,M28UnlockLayout.CardHeight),GUIContent.none,_card);
            GUI.Label(new Rect(x+18,y+10,col-36,30),UnlockTitles[i],_label);
            GUI.Label(new Rect(x+18,y+40,col-254,46),UnlockBlurbs[i],_muted);
            string result=UnlockResult(i);
            if(!string.IsNullOrEmpty(result))GUI.Label(new Rect(x+18,y+90,col-36,28),Ellipsize(result,col-36,_small),_small);
            if(Button(new Rect(x+col-218,y+41,M28UnlockLayout.ButtonWidth,M28UnlockLayout.ButtonHeight),"Разблокировать…",_button))
            {
                string title=UnlockTitles[i];Action action=actions[i];
                _app.Confirm(title,UnlockDetails[i]+"\nЭто постоянное изменение текущего сейва. Автоматического отката нет. Подтвердите намерение.",action);
            }
        }
    }

    private static Color TrackColor(bool? done)=>done==true?new Color(.36f,.86f,.52f,1f):done==false?new Color(.98f,.70f,.30f,1f):new Color(.62f,.66f,.70f,1f);

    private void TrackingPage(float width)
    {
        var tools=_app.Tracking;
        tools.Refresh(false);
        var kinds=M27TrackingPolicy.Kinds;
        for(int i=0;i<kinds.Length;i++)
            if(Button(new Rect(i*168,52,160,40),kinds[i],_trackKind==i?_selectedButton:_button))
            {_trackKind=i;_trackFilter=0;_trackScroll=Vector2.zero;_trackSelected="";}
        _trackFilter=M27TrackingPolicy.ClampFilter(_trackKind,_trackFilter);
        var filters=M27TrackingPolicy.Filters(_trackKind);
        for(int i=0;i<filters.Length;i++)
            if(Button(new Rect(i*168,100,160,40),filters[i],_trackFilter==i?_selectedButton:_button))
            {_trackFilter=i;_trackScroll=Vector2.zero;}
        string search=TextBox("tracking-search",new Rect(504,100,width-504-152,40),_trackSearch,60,"Поиск по названию, зоне или ID…");
        if(search!=_trackSearch){_trackSearch=search??"";_trackScroll=Vector2.zero;}
        if(Button(new Rect(width-140,100,140,40),"Обновить",_button))_app.Queue("tracking refresh",()=>tools.Refresh(true));
        if(_trackViewKind!=_trackKind||_trackViewFilter!=_trackFilter||_trackViewVersion!=tools.Version||
           !string.Equals(_trackViewSearch,_trackSearch,StringComparison.Ordinal))
        {
            _trackViewKind=_trackKind;_trackViewFilter=_trackFilter;_trackViewVersion=tools.Version;_trackViewSearch=_trackSearch;
            _trackView=M27TrackingPolicy.Select(tools.Rows,_trackKind,_trackFilter,_trackSearch);
            _trackSummary=tools.Summary(_trackKind,M27TrackingPolicy.Count(tools.Rows,_trackKind))+
                (_trackView.Count<M27TrackingPolicy.Count(tools.Rows,_trackKind).Total?" · в фильтре "+_trackView.Count:"");
        }
        GUI.Label(new Rect(680,52,width-680,40),Ellipsize("Отметки — из сейва; расстояние — от игрока",width-680,_small),_small);
        GUI.Label(new Rect(0,148,width,32),Ellipsize(_trackSummary,width,_muted),_muted);
        var rows=_trackView;
        GUI.Box(new Rect(0,184,width,306),GUIContent.none,_card);
        M27TrackingPolicy.Row selected=null;
        _trackScroll=BeginScroll(71,new Rect(10,192,width-20,290),_trackScroll,rows.Count*48f,48f);
        try
        {
            float rowWidth=width-72;
            for(int i=0;i<rows.Count;i++)
            {
                var row=rows[i];
                bool isSelected=row.Key==_trackSelected;
                if(isSelected)selected=row;
                float y=i*48-_trackScroll.y;
                if(y+44<0||y>290)continue;
                if(Button(new Rect(0,y,rowWidth,44),GUIContent.none,isSelected?_selectedButton:_button))_trackSelected=row.Key;
                Fill(new Rect(12,y+17,10,10),TrackColor(row.Done));
                string distance=MapTools.DistanceText(row.Distance);
                float distanceWidth=distance.Length>0?110:0,textWidth=rowWidth-44-distanceWidth;
                var text=isSelected?_selectedRowText:_muted;
                GUI.Label(new Rect(32,y+1,textWidth,24),Ellipsize(row.Name,textWidth,text),text);
                GUI.Label(new Rect(32,y+23,textWidth,20),Ellipsize(row.Zone+" · "+row.State,textWidth,_small),_small);
                if(distanceWidth>0)GUI.Label(new Rect(rowWidth-distanceWidth-8,y+1,distanceWidth,42),distance,_small);
            }
            if(rows.Count==0)GUI.Label(new Rect(16,16,width-100,96),_app.Ready?"В этом фильтре ничего нет.":tools.Status,_muted);
        }
        finally{EndGroup();}
        var point=selected?.Tag as MapTools.Point;
        bool enabled=GUI.enabled,busy=_app.Map.Busy;
        try
        {
            GUI.enabled=enabled&&point!=null&&!busy;
            if(Button(new Rect(0,500,300,44),"Показать на атласе",_accentButton))
            {var chosen=point;_app.Queue("tracking atlas",()=>_app.Map.OpenAt(chosen));}
            GUI.enabled=enabled&&point?.Group!=null&&!busy;
            if(Button(new Rect(316,500,240,44),"Телепорт",_button))
            {var chosen=point;_app.Queue("tracking teleport",()=>_app.Map.RequestTeleport(chosen));}
            bool door=selected!=null&&selected.Kind==M27TrackingPolicy.Doors&&selected.Done==false;
            GUI.enabled=enabled&&door&&!busy;
            if(Button(new Rect(572,500,280,44),"Открыть дверь…",_goldButton))
            {
                string id=selected.Id;
                _app.Confirm("Открыть дверь?","Дверь "+id+" будет открыта в модели сейва (AccessDoor). "+
                    "Это постоянное изменение текущего сейва; автоматического отката нет.",()=>tools.OpenDoor(id));
            }
        }
        finally{GUI.enabled=enabled;}
        string hint=selected==null?"Выберите строку списка.":point==null?"Место этой записи на карте неизвестно.":
            point.Group==null?"Группа сцены не подтверждена — телепорт отключён.":"Телепорт — через безопасную прогрузку зоны.";
        GUI.Label(new Rect(868,500,width-868,44),Ellipsize(hint,width-868,_small),_small);
        GUI.Label(new Rect(0,554,width,40),Ellipsize(tools.Status,width,_muted),_muted);
    }
}
