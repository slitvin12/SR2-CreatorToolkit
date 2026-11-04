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
    // 0.8.5: tally and percent of the summary bar, pod rewards per row (rebuilt with the view).
    private M27TrackingPolicy.Tally _trackTally;
    private string _trackPercent="";
    private readonly Dictionary<string,(string Key,int Count)[]> _trackRewards=new();
    private FrameGlide _trackBar; // 0.8.5: the summary bar fills smoothly

    private void ResetM27Ui()
    {
        _unlockSection=_trackKind=_trackFilter=0;_trackSearch=_trackSelected="";_trackScroll=Vector2.zero;
        _trackView=new List<M27TrackingPolicy.Row>();_trackSummary="";_trackViewSearch=null;
        _trackViewKind=_trackViewFilter=_trackViewVersion=-1;
        _trackTally=default;_trackPercent="";_trackRewards.Clear();
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
        L.Label(new Rect(476,0,width-476,40),"Постоянные изменения сейва · каждое действие с подтверждением",_muted);
        var actions=UnlockActions();
        float col=M28UnlockLayout.Column(width);
        for(int i=0;i<UnlockTitles.Length;i++)
        {
            float x=M28UnlockLayout.X(i,width),y=M28UnlockLayout.Y(i);
            L.Box(new Rect(x,y,col,M28UnlockLayout.CardHeight),GUIContent.none,_card);
            L.Label(new Rect(x+18,y+10,col-36,30),UnlockTitles[i],_label);
            Hint(new Rect(x+18,y+40,col-254,46),UnlockBlurbs[i]);
            string result=UnlockResult(i);
            if(!string.IsNullOrEmpty(result))L.Label(new Rect(x+18,y+90,col-36,28),Ellipsize(result,col-36,_small),_small);
            if(Button(new Rect(x+col-218,y+41,M28UnlockLayout.ButtonWidth,M28UnlockLayout.ButtonHeight),"Разблокировать…",_button))
            {
                string title=UnlockTitles[i];Action action=actions[i];
                _app.Confirm(title,UnlockDetails[i]+"\nЭто постоянное изменение текущего сейва. Автоматического отката нет. Подтвердите намерение.",action);
            }
        }
    }

    // 0.8.5: every row carries its marker's own icon (canon wiki / native atlas art), a done / pending
    // badge, pod rewards and a direction dial; the list buttons show their layer icon (TrackerRowPolicy).
    private void TrackingPage(float width)
    {
        var tools=_app.Tracking;
        tools.Refresh(false);
        var kinds=M27TrackingPolicy.Kinds;
        for(int i=0;i<kinds.Length;i++)
            if(TrackKindButton(new Rect(i*198,52,190,40),i,_trackKind==i))
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
            _trackTally=M27TrackingPolicy.Count(tools.Rows,_trackKind);
            _trackSummary=tools.Summary(_trackKind,_trackTally)+(_trackView.Count<_trackTally.Total?" · в фильтре "+_trackView.Count:"");
            _trackPercent=TrackerRowPolicy.Percent(TrackerRowPolicy.Progress(_trackKind,_trackTally.Total,_trackTally.Done));
            _trackRewards.Clear();
        }
        Hint(new Rect(800,52,width-800,40),"Отметки — из сейва; расстояние — от игрока",_small);
        // Summary with a progress bar of the list (hidden for teleports).
        float progress=TrackerRowPolicy.Progress(_trackKind,_trackTally.Total,_trackTally.Done);
        float barW=progress>=0?240:0,summaryW=width-(barW>0?barW+16:0);
        L.Label(new Rect(0,148,summaryW,32),Ellipsize(_trackSummary,summaryW,_muted),_muted);
        if(progress>=0)
        {
            Rect bar=new(width-barW,160,barW-60,10);
            DrawRoundedSurface(bar,_trackStyle.normal.background,5f);
            float shown=GlideTo(ref _trackBar,progress,UiMotionPolicy.BarSeconds);
            if(shown>.001f)DrawRoundedSurface(new Rect(bar.x,bar.y,Mathf.Max(10,bar.width*shown),bar.height),_fillStyle.normal.background,5f);
            L.Label(new Rect(width-52,148,52,32),_trackPercent,_atlasRight);
        }
        var rows=_trackView;
        L.Box(new Rect(0,184,width,306),GUIContent.none,_card);
        M27TrackingPolicy.Row selected=null;
        _trackScroll=BeginScroll(71,new Rect(10,192,width-20,290),_trackScroll,rows.Count*TrackerRowPolicy.Row,TrackerRowPolicy.Row);
        try
        {
            float rowWidth=width-72;
            for(int i=0;i<rows.Count;i++)
            {
                var row=rows[i];
                bool isSelected=row.Key==_trackSelected;
                if(isSelected)selected=row;
                float y=i*TrackerRowPolicy.Row-_trackScroll.y;
                if(y+TrackerRowPolicy.Face<0||y>290)continue;
                Rect face=new(0,y,rowWidth,TrackerRowPolicy.Face);
                if(Button(face,GUIContent.none,isSelected?_selectedButton:_button))_trackSelected=row.Key;
                float offset=JellyOffset(face);
                var point=row.Tag as MapTools.Point;
                Rect icon=RowChild(new Rect(TrackerRowPolicy.IconX,y+4,TrackerRowPolicy.Icon,TrackerRowPolicy.Icon),offset,true);
                if(point!=null)AtlasPointIcon(icon,point);else TrackKindIcon(icon,row.Kind);
                TrackBadge(new Rect(icon.xMax-11,icon.yMax-11,TrackerRowPolicy.Badge14,TrackerRowPolicy.Badge14),TrackerRowPolicy.Badge(row.Done,row.Unvisited));
                float right=rowWidth-10;
                string distance=MapTools.DistanceText(row.Distance);
                bool hasDistance=distance.Length>0;
                if(hasDistance)
                {
                    if(point!=null)AtlasDirection(RowChild(new Rect(right-TrackerRowPolicy.Dial,y+11,TrackerRowPolicy.Dial,TrackerRowPolicy.Dial),offset,true),point);
                    right-=TrackerRowPolicy.Dial+8;
                    L.Label(RowChild(new Rect(right-TrackerRowPolicy.DistanceW,y,TrackerRowPolicy.DistanceW,40),offset),distance,_atlasRight);
                    right-=TrackerRowPolicy.DistanceW+8;
                }
                var rewards=TrackRewards(row,point);
                int slots=TrackerRowPolicy.RewardSlots(row.Kind,rewards?.Length??0);
                for(int r=slots-1;r>=0;r--)
                {
                    var (key,count)=rewards[r];
                    var sprite=_app.Map.Icons.Reward(key);
                    right-=TrackerRowPolicy.RewardStep;
                    if(sprite==null)continue;
                    Rect cell=RowChild(new Rect(right+2,y+8,TrackerRowPolicy.Reward,TrackerRowPolicy.Reward),offset,true);
                    DrawSprite(cell,sprite);
                    if(count>1)
                    {
                        Rect badge=new(cell.xMax-20,cell.yMax-13,24,15);
                        DrawRoundedSurface(badge,_trackStyle.normal.background,7.5f);
                        L.Label(badge,"×"+AtlasNumber(count),_atlasBadge);
                    }
                }
                float textX=TrackerRowPolicy.TextX,textW=TrackerRowPolicy.TextWidth(rowWidth,hasDistance,slots);
                var text=isSelected?_selectedRowText:_muted;
                L.Label(RowChild(new Rect(textX,y+1,textW,24),offset),Ellipsize(row.Name,textW,text),text);
                L.Label(RowChild(new Rect(textX,y+23,textW,20),offset),Ellipsize(row.Zone+" · "+row.State,textW,_small),_small);
            }
            if(rows.Count==0)L.Label(new Rect(16,16,width-100,96),_app.Ready?"В этом фильтре ничего нет.":tools.Status,_muted);
        }
        finally{EndGroup();}
        var pick=selected?.Tag as MapTools.Point;
        bool enabled=GUI.enabled,busy=_app.Map.Busy;
        try
        {
            GUI.enabled=enabled&&pick!=null&&!busy;
            if(Button(new Rect(0,500,300,44),"Показать на атласе",_accentButton))
            {var chosen=pick;_app.Queue("tracking atlas",()=>_app.Map.OpenAt(chosen));}
            GUI.enabled=enabled&&pick?.Group!=null&&!busy;
            if(Button(new Rect(316,500,240,44),"Телепорт",_button))
            {var chosen=pick;_app.Queue("tracking teleport",()=>_app.Map.RequestTeleport(chosen));}
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
        string hint=selected==null?"Выберите строку списка.":pick==null?"Место этой записи на карте неизвестно.":
            pick.Group==null?"Группа сцены не подтверждена — телепорт отключён.":"Телепорт — через безопасную прогрузку зоны.";
        L.Label(new Rect(868,500,width-868,44),Ellipsize(hint,width-868,_small),_small);
        L.Label(new Rect(0,554,width,40),Ellipsize(tools.Status,width,_muted),_muted);
    }
    // List button: the layer icon, then the caption; the icon follows the jelly face.
    private bool TrackKindButton(Rect rect,int kind,bool selected)
    {
        bool clicked=Button(rect,GUIContent.none,selected?_selectedButton:_button);
        float offset=JellyOffset(rect);
        TrackKindIcon(HoverChildRect(rect,new Rect(rect.x+10,rect.y+4,30,30),offset),kind);
        string text=M27TrackingPolicy.Kinds[M27TrackingPolicy.ClampKind(kind)];
        HoverText(HoverChildRect(rect,new Rect(rect.x+44,rect.y-2,rect.width-50,rect.height),offset),text,IconTextStyle(text,rect.width-50),offset);
        return clicked;
    }
    // Icon of a list (and of rows without an atlas point, e.g. save-only doors): canon wiki cell,
    // native atlas category sprite, Fandom category marker.
    private void TrackKindIcon(Rect rect,int kind)
    {
        string category=TrackerRowPolicy.Category(kind);
        if(CanonIcon(rect,CanonMapIconPolicy.Cell(category,"",null)))return;
        var sprite=_app.Map.Icons.Category(category);
        if(sprite!=null){DrawSprite(rect,sprite);return;}
        FandomIcon(rect,FandomMapMarkers.CategoryIcon(category));
    }
    // Done = green check, pending = orange ring, never loaded = grey ring, unknown = no badge.
    private void TrackBadge(Rect rect,int badge)
    {
        if(badge==TrackerRowPolicy.BadgeNone)return;
        Color color=badge==TrackerRowPolicy.BadgeDone?new Color(.30f,.78f,.47f,1f):
            badge==TrackerRowPolicy.BadgePending?new Color(.98f,.60f,.22f,1f):new Color(.56f,.62f,.70f,1f);
        var saved=GUI.color;
        try
        {
            GUI.color=Color.white;GUI.DrawTexture(new Rect(rect.x-2,rect.y-2,rect.width+4,rect.height+4),MiniDiscTexture(false));
            GUI.color=color;GUI.DrawTexture(rect,MiniDiscTexture(false));
        }
        finally{GUI.color=saved;}
        MapGlyph(new Rect(rect.x+2,rect.y+2,rect.width-4,rect.height-4),badge==TrackerRowPolicy.BadgeDone?MapChromePolicy.Check:MapChromePolicy.Ring,Color.white);
    }
    // Up to three rewards of a pod row (the atlas's own reward data), cached until the view changes.
    private (string Key,int Count)[] TrackRewards(M27TrackingPolicy.Row row,MapTools.Point point)
    {
        if(row==null||point==null||row.Kind!=M27TrackingPolicy.Pods)return null;
        if(_trackRewards.TryGetValue(row.Key,out var cached))return cached;
        (string Key,int Count)[] result;
        try
        {
            var view=_app.Map.Capsules.Get(point.Id);
            var rewards=view.Mismatch&&view.NativeObserved?view.NativeRewards:view.Rewards;
            int n=Math.Min(TrackerRowPolicy.MaxRewards,rewards.Count);
            result=new (string Key,int Count)[n];
            for(int i=0;i<n;i++)result[i]=(rewards[i].Key,rewards[i].Count);
        }
        catch{result=Array.Empty<(string Key,int Count)>();}
        if(_trackRewards.Count<1024)_trackRewards[row.Key]=result;
        return result;
    }
}
