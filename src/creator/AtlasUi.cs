using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CreatorToolkit;

// 0.8.1 «Карта → Атлас» (approved mockup v2): one tab row for the four explorer sections, search
// with «Ближайшая», a carousel of seven layer groups, a carousel of the chosen group's chips with
// save progress («0/19»), a list in chip sections whose treasure pods are named by their contents,
// and a detail card («Внутри», compass target, teleport, own «opened» mark). The chips switch the
// same layers the native map, the minimap and the compass read (MapTools.LayerVisible).
internal sealed partial class ToolkitUi
{
    private int _atlasGroup;
    private float _atlasGroupOffset,_atlasChipOffset;
    private readonly List<MapTools.Point> _atlasRowPoints=new();
    private float _atlasRowsUntil,_atlasRowsHeight;
    private int _atlasRowsLanguage=-1;
    private bool _atlasRowsSorted;
    private MapTools.Point _atlasReveal;
    private readonly List<AtlasRow> _atlasRows=new();
    private readonly int[] _atlasGroupCounts=new int[AtlasGroupPolicy.Groups.Length];
    private Vector3 _atlasForward;
    private int _atlasForwardFrame=-1;
    private bool _atlasHasForward;
    private GUIStyle _atlasGroupText,_atlasChipTitle,_atlasRowTitle,_atlasRowMore,_atlasRowSub,_atlasCaps,_atlasRight,
        _atlasBadge,_atlasPill,_atlasCardTitle,_atlasCount;
    private readonly Dictionary<(string,int),float> _atlasWidths=new();
    private int _atlasWidthLanguage=-1;
    private static readonly Dictionary<int,string> AtlasNumbers=new();
    private sealed class AtlasRow
    {
        internal MapTools.Point Point;
        internal string Head,Title,More="",Sub="";
        internal float Y,H;
        internal readonly List<(string Key,int Count)> Rewards=new();
    }
    private const float AtlasHeadHeight=30f,AtlasRowHeight=56f,AtlasTop=60f;

    private void AtlasStyles()
    {
        _atlasGroupText=CopyStyle(_label);_atlasGroupText.fontSize=17;_atlasGroupText.wordWrap=false;
        _atlasGroupText.fontStyle=FontStyle.Bold;_atlasGroupText.normal.textColor=TextTint(_ink);
        _atlasChipTitle=CopyStyle(_atlasChipHead);_atlasChipTitle.fontSize=15;
        _atlasRowTitle=CopyStyle(_label);_atlasRowTitle.fontSize=18;_atlasRowTitle.wordWrap=false;_atlasRowTitle.normal.textColor=TextTint(_ink);
        _atlasRowMore=CopyStyle(_atlasRowTitle);_atlasRowMore.normal.textColor=TextTint(Hex(0x8C7A66));
        _atlasRowSub=CopyStyle(_small);
        _atlasCaps=CopyStyle(_small);_atlasCaps.fontSize=13;_atlasCaps.fontStyle=FontStyle.Bold;
        _atlasRight=CopyStyle(_small);_atlasRight.alignment=TextAnchor.MiddleRight;_atlasRight.fontStyle=FontStyle.Bold;
        _atlasRight.normal.textColor=TextTint(_ink);
        _atlasCount=CopyStyle(_atlasRight);_atlasCount.fontSize=17;
        _atlasBadge=CopyStyle(_badge);_atlasBadge.fontSize=13;_atlasBadge.fontStyle=FontStyle.Bold;_atlasBadge.normal.textColor=TextTint(_ink);
        _atlasPill=CopyStyle(_atlasChipHead);_atlasPill.alignment=TextAnchor.MiddleCenter;
        _atlasCardTitle=CopyStyle(_heading);_atlasCardTitle.fontSize=23;_atlasCardTitle.wordWrap=false;_atlasCardTitle.clipping=TextClipping.Clip;
        MapChromeStyles();
        ExplorerStyles();
    }

    private float AtlasTextWidth(string text,GUIStyle style)
    {
        if(_atlasWidthLanguage!=Loc.Generation){_atlasWidthLanguage=Loc.Generation;_atlasWidths.Clear();}
        var key=(text??"",style.fontSize*8+(int)style.fontStyle);
        if(!_atlasWidths.TryGetValue(key,out float width))
        {
            if(_atlasWidths.Count>1024)_atlasWidths.Clear();
            _measureContent.text=Loc.T(text??"");width=style.CalcSize(_measureContent).x;_atlasWidths[key]=width;
        }
        return width;
    }
    private static string AtlasNumber(int value)
    {
        if(!AtlasNumbers.TryGetValue(value,out var text))
        {if(AtlasNumbers.Count>4096)AtlasNumbers.Clear();AtlasNumbers[value]=text=value.ToString();}
        return text;
    }
    private static float AtlasBadgeWidth(int value)=>value<10?24:value<100?32:value<1000?40:48;
    private static string AtlasJoin(string a,string b,string c=null,string d=null)
    {
        string result="";
        foreach(string part in new[]{a,b,c,d})
            if(!string.IsNullOrWhiteSpace(part))result=result.Length==0?Loc.T(part):result+" · "+Loc.T(part);
        return result;
    }
    private static string AtlasCut(string text,int max)=>string.IsNullOrEmpty(text)||text.Length<=max?text??"":text.Substring(0,max-1).TrimEnd()+"…";

    // One tab row for every explorer section; «Открыть атлас» stays on the right.
    private void ExplorerNavigation(float width)
    {
        float trackW=width-262;
        L.Box(new Rect(0,0,trackW,50),GUIContent.none,_card);
        int count=ExplorerSections.Length;float part=(trackW-12-6*(count-1))/count;
        _app.Custom?.EnsureLoaded();
        for(int i=0;i<count;i++)
        {
            string role=i switch{0=>"Atlas",1=>"MyMarkers",2=>"Radar",_=>"Navigation"};
            int badge=i==1?_app.Custom?.Records.Count??0:0;
            if(ExplorerTab(new Rect(6+i*(part+6),5,part,40),ExplorerSections[i],role,_explorerSection==i,badge))
            {_explorerSection=i;ClearInteraction();}
        }
        bool enabled=GUI.enabled;GUI.enabled=enabled&&!_app.Map.Busy;
        if(Button(new Rect(width-250,3,250,44),"Открыть атлас",_accentButton))_app.Queue("open atlas",_app.Map.Open);
        GUI.enabled=enabled;
    }
    private bool ExplorerTab(Rect rect,string text,string role,bool selected,int badge)
    {
        bool hovered=CanHover(rect);
        bool clicked;
        if(selected)clicked=Button(rect,GUIContent.none,_selectedButton);
        else{if(hovered)L.Box(rect,GUIContent.none,_glowBox);clicked=Pressed(rect);}
        float offset=selected?HoverOffset(rect,hovered,hovered&&Mouse.current!=null&&Mouse.current.leftButton.isPressed):0f;
        var sprite=_app.Map.Icons.Role(role);
        float textW=AtlasTextWidth(text,_atlasGroupText);
        float total=(sprite!=null?36:0)+textW+(badge>0?8+AtlasBadgeWidth(badge):0);
        float x=Mathf.Max(rect.x+4,rect.center.x-total*.5f);
        if(sprite!=null){DrawSprite(HoverChildRect(rect,new Rect(x,rect.y+4,30,30),offset),sprite);x+=36;}
        HoverText(HoverChildRect(rect,new Rect(x,rect.y-2,Mathf.Min(textW+4,rect.xMax-x),rect.height),offset),text,_atlasGroupText,offset);
        if(badge>0)
        {
            Rect pill=HoverChildRect(rect,new Rect(x+textW+8,rect.y+9,AtlasBadgeWidth(badge),22),offset);
            DrawRoundedSurface(pill,_trackStyle.normal.background,11f);
            L.Label(pill,AtlasNumber(badge),_atlasBadge);
        }
        return clicked;
    }

    private void AtlasPage(Rect area)
    {
        BeginGroup(area);
        try
        {
            var map=_app.Map;
            ExplorerNavigation(area.width);
            if(_explorerSection!=0)
            {
                // 0.8.1 (mockups 02-04): «Мои метки», «Радар», «Навигация» under the shared tab row.
                ExplorerPage(area.width,area.height);
                return;
            }
            bool enabled=GUI.enabled;
            float leftW=Mathf.Round((area.width-20)*.6f),rightX=leftW+20,rightW=area.width-rightX;
            // Search, clear (M29), return after a teleport, nearest unopened.
            float nearestW=184,returnW=map.HasReturn?150:0,clearW=44;
            float searchW=leftW-nearestW-clearW-16-(returnW>0?returnW+8:0);
            // The toolbar's search button on the native map opens the atlas with the field focused.
            if(_atlasFocusSearch&&InputEnabled){_atlasFocusSearch=false;try{FocusTextInput("atlas-search",map.Search??"",80);}catch{/* no native keyboard: the field stays clickable */}}
            string search=TextBox("atlas-search",new Rect(0,AtlasTop,searchW,42),map.Search,80,"Что ищем: «сердечный», «алмаз», «гордо»…");
            if(search!=map.Search){map.Search=search;_atlasScroll.y=0;}
            GUI.enabled=enabled&&M29AtlasSearchPolicy.Active(map.Search);
            if(Button(new Rect(searchW+8,AtlasTop,clearW,42),"×",_button))ClearAtlasSearch();
            GUI.enabled=enabled;
            if(returnW>0)
            {
                GUI.enabled=enabled&&!map.Busy;
                if(Button(new Rect(searchW+clearW+16,AtlasTop,returnW,42),"Вернуться",_button))_app.Queue("atlas return",map.Return);
                GUI.enabled=enabled;
            }
            var points=map.Filter();
            if(Button(new Rect(leftW-nearestW,AtlasTop,nearestW,42),"Ближайшая",_button))
            {
                var nearest=map.NearestInFilter();
                if(nearest==null)_app.Notify(map.HasPlayerPosition?"В текущем фильтре нет непройденных меток.":"Позиция игрока ещё неизвестна.");
                else{map.Selected=nearest;_atlasReveal=nearest;}
            }
            AtlasGroups(new Rect(0,AtlasTop+52,leftW,46),points);
            AtlasChips(new Rect(0,AtlasTop+110,leftW,52));
            AtlasList(new Rect(0,AtlasTop+174,leftW,area.height-AtlasTop-174),points);
            if(map.Selected!=null&&!points.Contains(map.Selected))map.Selected=null;
            AtlasCard(new Rect(rightX,AtlasTop,rightW,area.height-AtlasTop),map.Selected);
            GUI.enabled=enabled;
        }
        finally{EndGroup();}
    }

    // Horizontal carousel: arrows, mouse wheel inside the strip and a position bar below it.
    private float AtlasCarousel(Rect strip,float offset,float content,out Rect view)
    {
        const float arrow=34;
        view=new Rect(strip.x+arrow+6,strip.y-4,strip.width-2*(arrow+6),strip.height+6);
        var e=Event.current;
        if(content>view.width&&InputEnabled&&PointerWithinGroups()&&e!=null&&e.type==EventType.ScrollWheel&&view.Contains(e.mousePosition))
        {offset+=(Mathf.Abs(e.delta.x)>Mathf.Abs(e.delta.y)?e.delta.x:e.delta.y)*24f;e.Use();}
        offset=AtlasGroupPolicy.CarouselClamp(offset,content,view.width);
        bool enabled=GUI.enabled;
        GUI.enabled=enabled&&offset>0;
        if(Button(new Rect(strip.x,strip.y,arrow,strip.height-4),"<",_button))offset=AtlasGroupPolicy.CarouselStep(offset,-1,content,view.width);
        GUI.enabled=enabled&&offset<content-view.width-.5f;
        if(Button(new Rect(strip.xMax-arrow,strip.y,arrow,strip.height-4),">",_button))offset=AtlasGroupPolicy.CarouselStep(offset,1,content,view.width);
        GUI.enabled=enabled;
        if(content>view.width+1)
        {
            Rect track=new(view.x+view.width*.3f,strip.yMax+1,view.width*.4f,4);
            DrawRoundedSurface(track,_trackStyle.normal.background,2f);
            float thumb=Mathf.Max(24,track.width*view.width/content);
            float x=track.x+(track.width-thumb)*(offset/Mathf.Max(1,content-view.width));
            DrawRoundedSurface(new Rect(x,track.y,thumb,track.height),_fillStyle.normal.background,2f);
        }
        return offset;
    }

    private float AtlasGroupWidth(int index)
    {
        int count=_atlasGroupCounts[index];
        return 44+AtlasTextWidth(AtlasGroupPolicy.Groups[index].Title,_atlasGroupText)+(count>0?8+AtlasBadgeWidth(count):0)+16;
    }
    private void AtlasGroups(Rect strip,List<MapTools.Point> points)
    {
        BuildAtlasRows(points);
        var groups=AtlasGroupPolicy.Groups;
        float content=0;
        for(int i=0;i<groups.Length;i++)content+=AtlasGroupWidth(i)+(i>0?8:0);
        _atlasGroupOffset=AtlasCarousel(strip,_atlasGroupOffset,content,out Rect view);
        BeginGroup(view);
        try
        {
            float x=-_atlasGroupOffset;
            for(int i=0;i<groups.Length;i++)
            {
                float w=AtlasGroupWidth(i);
                Rect pill=new(x,4,w,strip.height-4);
                if(pill.xMax>0&&pill.x<view.width&&AtlasGroupPill(pill,i))
                {
                    _atlasGroup=i;_atlasChipOffset=0;
                    _atlasGroupOffset=AtlasGroupPolicy.CarouselReveal(_atlasGroupOffset,x+_atlasGroupOffset,w,content,view.width);
                }
                x+=w+8;
            }
        }
        finally{EndGroup();}
    }
    private bool AtlasGroupPill(Rect rect,int index)
    {
        var group=AtlasGroupPolicy.Groups[index];bool selected=index==_atlasGroup;
        bool clicked=Button(rect,GUIContent.none,selected?_selectedButton:_button);
        bool hovered=CanHover(rect);
        float offset=HoverOffset(rect,hovered,hovered&&Mouse.current!=null&&Mouse.current.leftButton.isPressed);
        AtlasChipIcon(HoverChildRect(rect,new Rect(rect.x+8,rect.y+(rect.height-30)/2-2,30,30),offset),group.Chips[0]);
        float textW=AtlasTextWidth(group.Title,_atlasGroupText);
        HoverText(HoverChildRect(rect,new Rect(rect.x+44,rect.y-2,textW+4,rect.height),offset),group.Title,_atlasGroupText,offset);
        int count=_atlasGroupCounts[index];
        if(count>0)
        {
            Rect pill=HoverChildRect(rect,new Rect(rect.x+52+textW,rect.y+(rect.height-22)/2-2,AtlasBadgeWidth(count),22),offset);
            DrawRoundedSurface(pill,_trackStyle.normal.background,11f);
            L.Label(pill,AtlasNumber(count),_atlasBadge);
        }
        return clicked;
    }

    private float AtlasChipWidth(AtlasGroupPolicy.Chip chip)=>Mathf.Max(158,52+AtlasTextWidth(chip.Title,_atlasChipTitle)+16);
    private void AtlasChips(Rect strip)
    {
        var map=_app.Map;
        _atlasGroup=Mathf.Clamp(_atlasGroup,0,AtlasGroupPolicy.Groups.Length-1);
        var chips=AtlasGroupPolicy.Groups[_atlasGroup].Chips;
        float content=0;
        for(int i=0;i<chips.Length;i++)content+=AtlasChipWidth(chips[i])+(i>0?8:0);
        _atlasChipOffset=AtlasCarousel(strip,_atlasChipOffset,content,out Rect view);
        BeginGroup(view);
        try
        {
            float x=-_atlasChipOffset;
            foreach(var chip in chips)
            {
                float w=AtlasChipWidth(chip);
                Rect rect=new(x,4,w,strip.height-4);
                if(rect.xMax>0&&rect.x<view.width&&AtlasChip(rect,chip)){map.ToggleChip(chip);_atlasScroll.y=0;}
                x+=w+8;
            }
        }
        finally{EndGroup();}
    }
    private bool AtlasChip(Rect rect,AtlasGroupPolicy.Chip chip)
    {
        var map=_app.Map;bool active=map.ChipActive(chip);
        bool clicked=Button(rect,GUIContent.none,active?_selectedButton:_button);
        bool hovered=CanHover(rect);
        float offset=HoverOffset(rect,hovered,hovered&&Mouse.current!=null&&Mouse.current.leftButton.isPressed);
        Color saved=GUI.color;
        if(!active)GUI.color=new Color(saved.r,saved.g,saved.b,saved.a*.55f);
        try{AtlasChipIcon(HoverChildRect(rect,new Rect(rect.x+8,rect.y+(rect.height-32)/2-2,32,32),offset),chip);}
        finally{GUI.color=saved;}
        float textX=rect.x+48,textW=rect.width-56;
        HoverText(HoverChildRect(rect,new Rect(textX,rect.y+2,textW,22),offset),chip.Title,_atlasChipTitle,offset);
        var (total,done)=map.ChipCount(chip.Id);
        if(chip.Progress&&total>0)
        {
            float barW=Mathf.Max(20,textW-58);
            Rect bar=HoverChildRect(rect,new Rect(textX,rect.y+31,barW,5),offset);
            DrawRoundedSurface(bar,_trackStyle.normal.background,2.5f);
            if(done>0)DrawRoundedSurface(new Rect(bar.x,bar.y,Mathf.Max(5,bar.width*done/total),bar.height),_fillStyle.normal.background,2.5f);
            L.Label(HoverChildRect(rect,new Rect(textX+barW+4,rect.y+22,textW-barW-4,20),offset),AtlasNumber(done)+"/"+AtlasNumber(total),_atlasRight);
        }
        else L.Label(HoverChildRect(rect,new Rect(textX,rect.y+23,textW,20),offset),total>0?AtlasNumber(total)+" "+Loc.T("на карте"):"нет на карте",_small);
        return clicked;
    }
    // Chip art: a named role, an exact find-kind sprite, the canon wiki cell or the layer icon.
    private void AtlasChipIcon(Rect rect,AtlasGroupPolicy.Chip chip)
    {
        var map=_app.Map;
        if(chip==null)return;
        if(chip.Art.StartsWith("role:",StringComparison.Ordinal))
        {var role=map.Icons.Role(chip.Art.Substring(5));if(role!=null){DrawSprite(rect,role);return;}}
        else if(chip.Art.Length>0)
        {var art=map.Icons.Kind(chip.Art);if(art!=null){DrawSprite(rect,art);return;}}
        int cell=CanonMapIconPolicy.Cell(chip.Layer,chip.Id=="ghosts"?"component-route":"",null);
        if(CanonIcon(rect,cell))return;
        var sprite=map.Icons.Category(chip.Layer);
        if(sprite!=null)DrawSprite(rect,sprite);
        else FandomIcon(rect,FandomMapMarkers.CategoryIcon(chip.Layer));
    }

    // Rows in chip sections (the filter's own order inside a section), rebuilt when the shown
    // set changes, the language switches or every two seconds for save progress and pod names.
    private void BuildAtlasRows(List<MapTools.Point> points)
    {
        var map=_app.Map;
        bool same=_atlasRowsLanguage==Loc.Generation&&Time.unscaledTime<_atlasRowsUntil&&
            _atlasRowsSorted==map.SortByDistance&&_atlasRowPoints.Count==points.Count;
        for(int i=0;same&&i<points.Count;i++)same=ReferenceEquals(points[i],_atlasRowPoints[i]);
        if(same)return;
        _atlasRowPoints.Clear();_atlasRowPoints.AddRange(points);
        _atlasRowsLanguage=Loc.Generation;_atlasRowsUntil=Time.unscaledTime+2f;_atlasRowsSorted=map.SortByDistance;
        _atlasRows.Clear();Array.Clear(_atlasGroupCounts,0,_atlasGroupCounts.Length);
        var order=new List<(int Order,int Index,MapTools.Point Point)>(points.Count);
        for(int i=0;i<points.Count;i++)
        {
            var point=points[i];string chip=MapTools.ChipOf(point);
            order.Add((AtlasGroupPolicy.Order(chip),i,point));
            int group=AtlasGroupPolicy.GroupOf(chip);if(group>=0)_atlasGroupCounts[group]++;
        }
        order.Sort((a,b)=>a.Order!=b.Order?a.Order.CompareTo(b.Order):a.Index.CompareTo(b.Index));
        float y=0;int start=0;
        while(start<order.Count)
        {
            int end=start;
            while(end<order.Count&&order[end].Order==order[start].Order)end++;
            var first=order[start].Point;var chip=AtlasGroupPolicy.Find(MapTools.ChipOf(first));
            string tail;
            if(chip?.Progress==true)
            {
                int left=0;
                for(int i=start;i<end;i++)if(!map.Opened(order[i].Point))left++;
                tail=Loc.T(left+" не открыто");
            }
            else tail=AtlasNumber(end-start);
            _atlasRows.Add(new AtlasRow{Head=(Loc.T(chip?.Title??first.Category)+" · "+tail).ToUpperInvariant(),Y=y,H=AtlasHeadHeight});
            y+=AtlasHeadHeight;
            for(int i=start;i<end;i++){_atlasRows.Add(AtlasPointRow(order[i].Point,chip,y));y+=AtlasRowHeight;}
            start=end;
        }
        _atlasRowsHeight=y;
    }
    private AtlasRow AtlasPointRow(MapTools.Point point,AtlasGroupPolicy.Chip chip,float y)
    {
        var map=_app.Map;
        var row=new AtlasRow{Point=point,Y=y,H=AtlasRowHeight};
        string zone=map.ZoneTitleFor(point),one=chip?.One??point.Category;
        if(point.Category=="Капсулы")
        {
            var view=map.Capsules.Get(point.Id);
            var rewards=view.Mismatch&&view.NativeObserved?view.NativeRewards:view.Rewards;
            var kinds=new List<string>(rewards.Count);
            foreach(var reward in rewards)
            {
                kinds.Add(reward.Kind);
                if(row.Rewards.Count<3)row.Rewards.Add((reward.Key,reward.Count));
            }
            if(rewards.Count>0&&!string.IsNullOrWhiteSpace(rewards[0].Title))
            {
                row.Title=Loc.T(rewards[0].Title);
                row.More=rewards.Count>1?Loc.T(" и ещё "+(rewards.Count-1)):"";
            }
            else row.Title=Loc.T(map.DisplayName(point));
            row.Sub=AtlasJoin(one,zone,AtlasGroupPolicy.PodKind(kinds));
        }
        else
        {
            row.Title=Loc.T(map.DisplayName(point));
            // Statues, gates and receivers: the wiki line says what to bring and what it opens.
            row.Sub=point.Category==FandomMapMarkers.RareCategory?AtlasJoin(zone,FandomSummary(point)):AtlasJoin(one,zone);
        }
        row.Sub=AtlasCut(row.Sub,70);
        return row;
    }

    private void AtlasList(Rect rect,List<MapTools.Point> points)
    {
        var map=_app.Map;
        L.Box(rect,GUIContent.none,_card);
        BuildAtlasRows(points);
        bool sourcesShown=map.Visible.Contains("Горшочки")||map.Visible.Contains("Спавнеры");
        float right=rect.xMax-10;
        Rect sortRect=new(right-168,rect.y+8,168,32);right=sortRect.x-8;
        if(Button(sortRect,map.SortByDistance?"По расстоянию":"По порядку",map.SortByDistance?_selectedButton:_button))
        {map.SortByDistance=!map.SortByDistance;_atlasScroll.y=0;}
        Rect hideRect=new(right-170,rect.y+8,170,32);right=hideRect.x-8;
        if(Button(hideRect,map.HideOpened?"Открытое скрыто":"Открытое видно",map.HideOpened?_selectedButton:_button))
        {map.HideOpened=!map.HideOpened;_atlasScroll.y=0;}
        if(sourcesShown)
        {
            Rect sourceRect=new(right-150,rect.y+8,150,32);right=sourceRect.x-8;
            if(Button(sourceRect,map.SourcesActualOnly?"Только живые":"И места появления",map.SourcesActualOnly?_selectedButton:_button))
                map.SourcesActualOnly=!map.SourcesActualOnly;
        }
        RowStatus(new Rect(rect.x+16,rect.y+8,right-rect.x-20,32),"Показано: "+points.Count,
            M29AtlasSearchPolicy.Active(map.Search)?M29AtlasSearchPolicy.PageHint(map.Search):sourcesShown?"только загруженные сцены":null);
        Rect view=new(rect.x+6,rect.y+46,rect.width-12,rect.height-52);
        if(_atlasReveal!=null)
        {
            foreach(var row in _atlasRows)if(row.Point==_atlasReveal){_atlasScroll.y=Mathf.Max(0,row.Y-AtlasHeadHeight-40);break;}
            _atlasReveal=null;
        }
        _atlasScroll=BeginScroll(13,view,_atlasScroll,_atlasRowsHeight,AtlasRowHeight);
        try
        {
            float width=view.width-(M26ListPolicy.Extent(_atlasRowsHeight)>view.height?48:4);
            foreach(var row in _atlasRows)
            {
                float y=row.Y-_atlasScroll.y;
                if(y+row.H<=0||y>=view.height)continue;
                if(row.Point==null)
                {
                    L.Label(new Rect(14,y+4,width-20,row.H-6),row.Head,_atlasCaps);
                    continue;
                }
                AtlasRowDraw(new Rect(2,y+1,width,row.H-4),row);
            }
            if(points.Count==0)
                L.Label(new Rect(16,10,view.width-70,90),map.Points.Count==0?map.IndexStatus:
                    "Ничего не найдено. Измените поиск или включите нужные слои в группах выше.",_muted);
        }
        finally{EndGroup();}
    }
    private void AtlasRowDraw(Rect row,AtlasRow model)
    {
        var map=_app.Map;var point=model.Point;
        bool selected=map.Selected==point,hovered=CanHover(row);
        if(selected)DrawRoundedSurface(row,_selectedButton.normal.background,14f);
        else if(hovered)L.Box(row,GUIContent.none,_glowBox);
        if(Pressed(row))map.Selected=point;
        AtlasPointIcon(new Rect(row.x+10,row.y+(row.height-38)/2,38,38),point);
        float right=row.xMax-8;
        float distance=map.Distance(point);
        if(distance>=0)
        {
            AtlasDirection(new Rect(right-22,row.y+(row.height-22)/2,22,22),point);
            right-=30;
            L.Label(new Rect(right-72,row.y,72,row.height),MapTools.DistanceText(distance),_atlasRight);
            right-=80;
        }
        for(int i=Mathf.Min(3,model.Rewards.Count)-1;i>=0;i--)
        {
            var (key,count)=model.Rewards[i];
            var icon=map.Icons.Reward(key);
            if(icon==null)continue;
            Rect cell=new(right-34,row.y+(row.height-34)/2,34,34);
            DrawSprite(cell,icon);
            if(count>1)
            {
                Rect badge=new(cell.xMax-22,cell.yMax-14,26,16);
                DrawRoundedSurface(badge,_trackStyle.normal.background,8f);
                L.Label(badge,"×"+AtlasNumber(count),_atlasBadge);
            }
            right-=40;
        }
        float textX=row.x+58,textW=Mathf.Max(40,right-textX-6);
        L.Label(new Rect(textX,row.y+4,textW,26),model.Title,_atlasRowTitle);
        if(model.More.Length>0)
        {
            float titleW=AtlasTextWidth(model.Title,_atlasRowTitle);
            if(titleW<textW-60)L.Label(new Rect(textX+titleW+2,row.y+4,textW-titleW-2,26),model.More,_atlasRowMore);
        }
        L.Label(new Rect(textX,row.y+29,textW,20),model.Sub,_atlasRowSub);
    }
    // A small compass dial: the dot shows where the point lies relative to the camera heading.
    private void AtlasDirection(Rect rect,MapTools.Point point)
    {
        var map=_app.Map;
        if(_atlasForwardFrame!=Time.frameCount)
        {
            _atlasForwardFrame=Time.frameCount;_atlasHasForward=false;
            try
            {
                var camera=Camera.main;
                if(camera!=null&&!camera.WasCollected)
                {var forward=camera.transform.forward;_atlasForward=new Vector3(forward.x,0,forward.z);_atlasHasForward=_atlasForward.sqrMagnitude>1e-4f;}
            }
            catch{_atlasHasForward=false;}
        }
        DrawRoundedSurface(rect,_trackStyle.normal.background,rect.width*.5f);
        if(!_atlasHasForward||!map.HasPlayerPosition||point==null)return;
        float dx=point.Position.x-map.PlayerPosition.x,dz=point.Position.z-map.PlayerPosition.z;
        if(dx*dx+dz*dz<1f)return;
        float angle=Mathf.Atan2(dx,dz)-Mathf.Atan2(_atlasForward.x,_atlasForward.z);
        float radius=rect.width*.5f-5f;
        Vector2 dot=rect.center+new Vector2(Mathf.Sin(angle),-Mathf.Cos(angle))*radius;
        Color saved=GUI.color;GUI.color=_pink;
        try{GUI.DrawTexture(new Rect(dot.x-4,dot.y-4,8,8),_badgeCircle);}
        finally{GUI.color=saved;}
    }
    private void AtlasPointIcon(Rect rect,MapTools.Point point)
    {
        var map=_app.Map;
        if(point==null)return;
        if(point.Category=="Капсулы")
        {
            if(!CanonIcon(rect,CanonMapIconPolicy.TreasurePod)){var pod=map.Icons.Category("Капсулы");if(pod!=null)DrawSprite(rect,pod);}
            return;
        }
        int canonCell=CanonMapIconPolicy.Cell(point.Category,point.Id,point.Name);
        Sprite icon=canonCell<0?map.Icons.Point(point):null;
        if(icon!=null)DrawSprite(rect,icon);
        else if(!CanonIcon(rect,canonCell)&&!FandomIcon(rect,FandomMapMarkers.IconFor(point)))
        {
            var nativeRow=canonCell<0?null:map.Icons.Point(point);
            if(nativeRow!=null)DrawSprite(rect,nativeRow);else L.Label(rect,"—",_badge);
        }
        // A plort statue also shows the plort it asks for.
        if(icon!=null&&point.Category==FandomMapMarkers.RareCategory)
        {
            string plort=MapRareIcons.StatuePlort(point.Name);
            var art=plort==null?null:map.Icons.Art(plort);
            if(art!=null&&art.Pointer!=icon.Pointer)
                DrawSprite(new Rect(rect.xMax-rect.width*.5f,rect.yMax-rect.height*.5f,rect.width*.56f,rect.height*.56f),art);
        }
    }

    private string AtlasState(MapTools.Point point,out bool done)
    {
        var map=_app.Map;done=false;
        if(map.Marked(point)){done=true;return "Отмечено вами";}
        if(point.Category=="Капсулы")
            switch(map.CapsuleState(point))
            {
                case CapsuleTruthPolicy.OpenState.Open:done=true;return "Открыта";
                case CapsuleTruthPolicy.OpenState.Locked:return "Не открыта";
                default:return "Состояние неизвестно";
            }
        if(!map.HasNativeProgress(point))return "";
        done=map.Opened(point);
        return done?"Пройдено":"Не пройдено";
    }
    private void AtlasCard(Rect rect,MapTools.Point selected)
    {
        var map=_app.Map;bool enabled=GUI.enabled;
        L.Box(rect,GUIContent.none,_card);
        if(selected==null)
        {
            L.Label(new Rect(rect.x+22,rect.y+22,rect.width-44,140),DetailedHelp?
                "Выберите метку в списке.\nГруппы и чипы сверху включают слои: включённое видно здесь, на карте, на мини-карте и в компасе.":
                "Выберите метку в списке.",_muted);
            return;
        }
        bool pod=selected.Category=="Капсулы";
        var chip=AtlasGroupPolicy.Find(MapTools.ChipOf(selected));
        AtlasPointIcon(new Rect(rect.x+18,rect.y+18,54,54),selected);
        string title=pod?"Сокровищница":map.DisplayName(selected);
        float titleW=rect.width-100;
        L.Label(new Rect(rect.x+84,rect.y+12,titleW,34),title,AtlasTextWidth(title,_atlasCardTitle)<=titleW?_atlasCardTitle:_atlasRowTitle);
        string distance=MapTools.DistanceText(map.Distance(selected));
        L.Label(new Rect(rect.x+84,rect.y+44,titleW,22),AtlasJoin(pod?null:chip?.One,map.ZoneTitleFor(selected),distance,
            selected.ExactHeight?"высота точная":null),_small);
        string state=AtlasState(selected,out bool done);
        if(state.Length>0)
        {
            float pillW=Mathf.Min(titleW,AtlasTextWidth(state,_atlasPill)+26);
            Rect pill=new(rect.x+84,rect.y+70,pillW,24);
            DrawRoundedSurface(pill,(done?_selectedButton:_goldButton).normal.background,12f);
            L.Label(pill,state,_atlasPill);
        }
        float top=rect.y+108,buttons=rect.yMax-154;
        Rect body=new(rect.x+14,top,rect.width-28,buttons-top-8);
        if(pod)AtlasPodContents(body,selected);else AtlasPointDetail(body,selected);
        float x=rect.x+16,w=rect.width-32,half=(w-8)/2;
        GUI.enabled=enabled&&(!selected.SourceObservation||map.MapForPoint(selected)!=null);
        if(Button(new Rect(x,buttons,w,44),"Показать на карте",_accentButton)){var point=selected;_app.Queue("atlas point",()=>map.OpenAt(point));}
        GUI.enabled=enabled;
        if(Button(new Rect(x,buttons+52,half,40),"Цель компаса",_button))
        {
            var point=selected;
            if(_app.Compass?.Enabled!=true)_app.Notify("Компас выключен: включите его в «Навигации».");
            else _app.Queue("atlas point",()=>{map.Selected=point;_app.CloseMenu();});
        }
        GUI.enabled=enabled&&selected.Group!=null&&!map.Busy;
        if(Button(new Rect(x+half+8,buttons+52,half,40),"Телепорт",_button))
        {var point=selected;_app.Queue("teleport atlas marker",()=>map.RequestTeleport(point));}
        GUI.enabled=enabled&&map.CanMark(selected);
        bool marked=map.Marked(selected);
        if(Button(new Rect(x,buttons+100,w,40),marked?"Снять отметку":"Отметить открытой",marked?_selectedButton:_button))
        {var point=selected;_app.Queue("toggle map mark",()=>map.ToggleMark(point));}
        GUI.enabled=enabled;
    }
    // «Внутри · 3 предмета»: icon, official name, kind and count of every reward.
    private void AtlasPodContents(Rect rect,MapTools.Point point)
    {
        var map=_app.Map;var view=map.Capsules.Get(point.Id);
        var rewards=view.Mismatch&&view.NativeObserved?view.NativeRewards:view.Rewards;
        if(_capsuleId!=point.Id){_capsuleId=point.Id;_capsuleScroll=Vector2.zero;}
        L.Label(new Rect(rect.x+4,rect.y,rect.width-8,22),(Loc.T("Внутри")+" · "+Loc.T(AtlasGroupPolicy.Items(rewards.Count))).ToUpperInvariant(),_atlasCaps);
        string truth=view.Mismatch?"Расхождение с базой · показано то, что в игре":
            view.Verification==CapsuleTruthPolicy.RewardStatus.Unverifiable?"Сверка неполная · награды из базы мода":
            view.NativeVerified?"Сверено с капсулой в игре":view.Baked?"Из базы мода · в игре ещё не сверено":"Награды неизвестны";
        Rect viewport=new(rect.x,rect.y+26,rect.width,rect.height-52);
        float extent=rewards.Count*64f;
        _capsuleScroll=BeginScroll(31,viewport,_capsuleScroll,extent,64f);
        try
        {
            float width=viewport.width-(M26ListPolicy.Extent(extent)>viewport.height?48:0);
            for(int i=0;i<rewards.Count;i++)
            {
                float y=i*64-_capsuleScroll.y;
                if(y+60<0||y>viewport.height)continue;
                var reward=rewards[i];
                Rect row=new(0,y,width,58);
                L.Box(row,GUIContent.none,_frame);
                var icon=map.Icons.Reward(reward.Key);
                if(icon!=null)DrawSprite(new Rect(10,y+9,40,40),icon);
                else L.Label(new Rect(10,y+9,40,40),"?",_badge);
                L.Label(new Rect(60,y+5,width-128,26),reward.Title,_atlasRowTitle);
                L.Label(new Rect(60,y+31,width-128,20),reward.Kind,_atlasRowSub);
                if(reward.Count>0)L.Label(new Rect(width-70,y,58,58),"×"+AtlasNumber(reward.Count),_atlasCount);
            }
            if(rewards.Count==0)L.Label(new Rect(4,4,viewport.width-8,viewport.height-8),view.Description,_atlasDetail);
        }
        finally{EndGroup();}
        L.Label(new Rect(rect.x+4,rect.yMax-22,rect.width-8,22),truth,_small);
    }
    private void AtlasPointDetail(Rect rect,MapTools.Point selected)
    {
        var map=_app.Map;
        string detail=selected.SourceObservation||FandomMapMarkers.IsFandom(selected.Category)?selected.Detail:
            selected.WikiHint!=null?"Вики: "+selected.WikiHint:null;
        if(selected.Category=="Гордо")detail=map.GordoStateLabel(selected)+(detail!=null?"\n"+detail:"");
        detail??=map.ProgressLabel(selected) is string progress&&progress.Length>0?progress:"Координаты: "+selected.Position.ToString("F0");
        if(_sourceDetailId!=selected.Id){_sourceDetailId=selected.Id;_sourceDetailScroll=Vector2.zero;}
        float height=Mathf.Max(rect.height,_atlasDetail.CalcHeight(ButtonContent(detail),rect.width-56)+12);
        _sourceDetailScroll=BeginScroll(32,rect,_sourceDetailScroll,height,24);
        try{L.Label(new Rect(4,4-_sourceDetailScroll.y,rect.width-56,height),detail,_atlasDetail);}
        finally{EndGroup();}
    }
}
