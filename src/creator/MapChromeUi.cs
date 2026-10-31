using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CreatorToolkit;

// 0.8.1 native map chrome (approved mockups 05/06). Toolbar «Атлас · <layers> ▼ · search ·
// nearest unopened · my markers · settings», the line under it (return, active search, legend),
// the «Слои карты» drop-down (presets, groups with chip rows and save progress, opened / fog
// toggles, icon size S/M/L, left-click mode), the card of any selected marker and the hover
// plaque with the contents of a treasure pod. Geometry: MapChromePolicy.
internal sealed partial class ToolkitUi
{
    private int _mapLayersGroup;
    private Vector2 _mapLayersScroll;
    private bool _atlasFocusSearch;
    private GUIStyle _mapBarTitle,_mapLegend,_mapTipTitle,_mapTipSub,_mapGroupHead,_mapGroupSub,_mapChipText,_mapSegment;
    private static Texture2D[] _mapGlyphs;
    private string _mapLegendText="";
    private int _mapLegendShown=-1,_mapLegendFog=-1;
    private bool _mapLegendOpened,_mapLegendFogOn;
    private MapTools.Point _mapTipPoint;
    private float _mapTipUntil;
    private string _mapTipTitleText="",_mapTipSubText="",_mapTipReward;
    private float _mapTipWidth;
    private static readonly string[] MapSizeTitles={"S","M","L"};
    private static readonly string[] MapClickTitles={"Метка","Моя","Телепорт"};
    private static readonly string[] MapRoundTips={"Поиск в атласе","Ближайшая неоткрытая","Мои метки","Настройки навигации"};

    private void MapChromeStyles()
    {
        _mapBarTitle=CopyStyle(_atlasGroupText);_mapBarTitle.fontSize=20;
        _mapLegend=CopyStyle(_atlasCaps);_mapLegend.fontSize=14;_mapLegend.alignment=TextAnchor.MiddleCenter;_mapLegend.wordWrap=false;
        _mapTipTitle=CopyStyle(_atlasRowTitle);_mapTipTitle.fontSize=17;_mapTipTitle.fontStyle=FontStyle.Bold;_mapTipTitle.normal.textColor=TextTint(Color.white);
        _mapTipSub=CopyStyle(_small);_mapTipSub.fontSize=13;_mapTipSub.normal.textColor=TextTint(Hex(0xBFD3E3));
        _mapGroupHead=CopyStyle(_atlasCaps);_mapGroupHead.fontSize=15;
        _mapGroupSub=CopyStyle(_small);_mapGroupSub.fontSize=13;
        _mapChipText=CopyStyle(_atlasRowTitle);_mapChipText.fontSize=17;
        _mapSegment=CopyStyle(_iconTab);_mapSegment.fontSize=15;_mapSegment.alignment=TextAnchor.MiddleCenter;_mapSegment.wordWrap=false;
    }
    private static Rect R(MapShieldPolicy.Box box)=>new(box.X,box.Y,box.Width,box.Height);

    // Toolbar glyphs: tiny white distance-field textures, tinted at draw time; process lifetime.
    private void MapGlyph(Rect rect,int glyph,Color color)
    {
        if(_mapGlyphs==null)
        {
            const int size=64;
            _mapGlyphs=new Texture2D[MapChromePolicy.Count];
            for(int g=0;g<_mapGlyphs.Length;g++)
            {
                var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                var pixels=new Color32[size*size];
                for(int y=0;y<size;y++)
                    for(int x=0;x<size;x++)
                    {
                        // Texture rows run bottom-up; the policy's y runs down.
                        float a=MapChromePolicy.Glyph(g,(x+.5f)/size,1f-(y+.5f)/size,size);
                        pixels[y*size+x]=new Color32(255,255,255,(byte)Mathf.RoundToInt(a*255));
                    }
                texture.SetPixels32(pixels);texture.Apply(false,true);
                texture.hideFlags=HideFlags.HideAndDontSave;
                _mapGlyphs[g]=texture;
            }
        }
        var saved=GUI.color;GUI.color=color;
        try{GUI.DrawTexture(rect,_mapGlyphs[Mathf.Clamp(glyph,0,_mapGlyphs.Length-1)]);}
        finally{GUI.color=saved;}
    }
    // Compact on/off switch (mockup 06): a rounded track and a round knob.
    private bool MiniSwitch(Rect rect,bool on)
    {
        bool hover=CanHover(rect);
        if(hover)L.Box(new Rect(rect.x-2,rect.y-2,rect.width+4,rect.height+4),GUIContent.none,_glowBox);
        DrawRoundedSurface(rect,(on?_selectedButton:_trackStyle).normal.background,rect.height*.5f);
        float knob=rect.height-6;
        Rect dot=new(on?rect.xMax-knob-3:rect.x+3,rect.y+3,knob,knob);
        var saved=GUI.color;GUI.color=on?Color.white:new Color(1f,1f,1f,.9f);
        try{GUI.DrawTexture(dot,MiniDiscTexture(false));}
        finally{GUI.color=saved;}
        return Pressed(rect);
    }
    // Segmented control: returns the pressed segment or -1.
    private int Segmented(Rect rect,string[] titles,int selected)
    {
        DrawRoundedSurface(rect,_trackStyle.normal.background,rect.height*.5f);
        int pressed=-1;float part=(rect.width-6)/titles.Length;
        for(int i=0;i<titles.Length;i++)
        {
            Rect seg=new(rect.x+3+i*part,rect.y+3,part,rect.height-6);
            if(i==selected)DrawRoundedSurface(seg,_selectedButton.normal.background,seg.height*.5f);
            else if(CanHover(seg))L.Box(seg,GUIContent.none,_glowBox);
            L.Label(seg,titles[i],_mapSegment);
            if(Pressed(seg))pressed=i;
        }
        return pressed;
    }
    private void OpenMenuFromMap(string label,int section,bool focusSearch=false)
    {
        var map=_app.Map;
        _app.Queue(label,()=>{map.CloseNative();ShowExplorerForTest(section);_atlasFocusSearch=focusSearch;_app.Input.OpenAfterNativeClose();});
    }
    private string MapLayersLabel(MapTools map)
    {
        int preset=map.MatchingPreset();
        if(preset>=0)return AtlasGroupPolicy.Presets[preset].Title;
        int active=0;
        foreach(var group in AtlasGroupPolicy.Groups)active+=map.GroupActive(group);
        return active==0?"Слои выключены":"Слои: "+active;
    }

    // Toolbar: «Атлас» opens the menu atlas, the layers pill toggles «Слои карты».
    private void MapToolbar(Rect bar,MapTools map)
    {
        L.Box(bar,GUIContent.none,_frame);
        BeginGroup(bar);
        try
        {
            Rect title=new(6,6,134,48);
            if(CanHover(title))L.Box(title,GUIContent.none,_glowBox);
            var atlasIcon=map.Icons.Role("Atlas");
            if(atlasIcon!=null)DrawSprite(new Rect(12,12,36,36),atlasIcon);
            L.Label(new Rect(54,6,86,48),"Атлас",_mapBarTitle);
            if(Pressed(title))OpenMenuFromMap("atlas from map",0);
            Fill(new Rect(MapChromePolicy.GroupX-6,14,2,32),new Color(.91f,.87f,.80f,1f));
            Rect pill=new(MapChromePolicy.GroupX,8,MapChromePolicy.GroupWidth,44);
            if(Button(pill,GUIContent.none,map.PanelExpanded?_selectedButton:_button))map.PanelExpanded=!map.PanelExpanded;
            float x=pill.x+12;int icons=0;
            foreach(var group in AtlasGroupPolicy.Groups)
            {
                foreach(var chip in group.Chips)
                {
                    if(icons>=3||!map.ChipActive(chip))continue;
                    AtlasChipIcon(new Rect(x,pill.y+7,28,28),chip);x+=20;icons++;
                }
                if(icons>=3)break;
            }
            if(icons>0)x+=12;
            string label=MapLayersLabel(map);
            float labelW=pill.xMax-30-x;
            L.Label(new Rect(x,pill.y-2,labelW,pill.height),Ellipsize(label,labelW,_atlasGroupText),_atlasGroupText);
            MapGlyph(new Rect(pill.xMax-28,pill.y+11,18,18),MapChromePolicy.Chevron,_ink);
            for(int i=0;i<MapChromePolicy.RoundButtons;i++)
            {
                Rect round=new(MapChromePolicy.RoundX(i),8,MapChromePolicy.Round,MapChromePolicy.Round);
                bool active=i==2&&map.CustomPointMode;
                bool clicked=Button(round,GUIContent.none,active?_selectedButton:_button);
                Rect glyph=new(round.x+11,round.y+9,22,22);
                if(i==2){var flag=map.Icons.Role("MyMarkers");if(flag!=null)DrawSprite(glyph,flag);}
                else MapGlyph(glyph,i==0?MapChromePolicy.Search:i==1?MapChromePolicy.Target:MapChromePolicy.Gear,new Color(.42f,.38f,.33f,1f));
                if(CanHover(round))_mapRoundTip=i;
                if(!clicked)continue;
                if(i==0)OpenMenuFromMap("atlas search from map",0,true);
                else if(i==1)MapNearest(map);
                else if(i==2)OpenMenuFromMap("my markers from map",1);
                else OpenMenuFromMap("navigation from map",4);
            }
        }
        finally{EndGroup();}
    }
    private int _mapRoundTip=-1;
    // Width the line drew on the last repaint (markers left of it stay free); tip obstacles.
    private float _mapLineUsed=MapChromePolicy.ToolbarWidth;
    private readonly List<MapShieldPolicy.Box> _mapTipObstacles=new();
    private void MapNearest(MapTools map)
    {
        var nearest=map.NearestInFilter(true);
        if(nearest==null){_app.Notify(map.HasPlayerPosition?"Среди включённых слоёв нет неоткрытых меток на этой странице.":"Позиция игрока ещё неизвестна.");return;}
        map.Selected=nearest;Vector2 c=new(nearest.Position.x,nearest.Position.z);
        _app.Queue("center nearest marker",()=>map.Ui._viewModel.CenterOnWorldPosition(c));
    }
    // The line under the toolbar: «Вернуться», the active search chip and the legend.
    private Rect MapLine(Rect line,MapTools map,int shown,bool repaint)
    {
        float right=line.xMax;
        if(_mapRoundTip>=0)
        {
            string tip=MapRoundTips[_mapRoundTip];
            float w=AtlasTextWidth(tip,_mapLegend)+28;
            Rect r=new(right-w,line.y+2,w,32);
            DrawRoundedSurface(r,_frame.normal.background,16f);L.Label(r,tip,_mapLegend);
            return new Rect(r.x,line.y,line.xMax-r.x,line.height);
        }
        if(_mapLegendShown!=shown||_mapLegendOpened!=map.HideOpened||_mapLegendFogOn!=map.HideFogged||_mapLegendFog!=map.FogHiddenOnPage)
        {
            _mapLegendShown=shown;_mapLegendOpened=map.HideOpened;_mapLegendFogOn=map.HideFogged;_mapLegendFog=map.FogHiddenOnPage;
            _mapLegendText=MapChromePolicy.Legend(shown,map.HideOpened,map.HideFogged,map.FogHiddenOnPage);
        }
        string legend=map.CustomPointMode?"ЛКМ по суше — своя метка «"+_app.Custom.DraftName+"»":map.ClickTeleport?"ЛКМ по суше — телепорт":_mapLegendText;
        float legendW=Mathf.Min(line.width,AtlasTextWidth(legend,_mapLegend)+28);
        Rect legendRect=new(right-legendW,line.y+2,legendW,32);
        var saved=GUI.color;
        if(map.CustomPointMode||map.ClickTeleport)DrawRoundedSurface(legendRect,_selectedButton.normal.background,16f);
        else{GUI.color=new Color(1f,1f,1f,.88f);DrawRoundedSurface(legendRect,_frame.normal.background,16f);GUI.color=saved;}
        L.Label(legendRect,Ellipsize(legend,legendW-16,_mapLegend),_mapLegend);
        float left=legendRect.x-8;
        if(M29AtlasSearchPolicy.Active(map.Search))
        {
            Rect chip=new(Mathf.Max(line.x,left-232),line.y-2,Mathf.Min(232,left-line.x),44);
            if(chip.width>120){AtlasSearchChip(chip,map);left=chip.x-8;}
        }
        if(map.HasReturn&&left-line.x>=120)
        {
            bool enabled=GUI.enabled;GUI.enabled=enabled&&!map.Busy;
            if(Button(new Rect(left-120,line.y,120,36),"Вернуться",_button))_app.Queue("return from atlas",map.Return);
            GUI.enabled=enabled;left-=128;
        }
        return Rect.MinMaxRect(Mathf.Max(line.x,left),line.y,line.xMax,line.yMax);
    }

    // «Слои карты»: presets, groups (one expanded with its chips), then the map settings.
    private void MapLayersPanel(Rect panel,MapTools map)
    {
        L.Box(panel,GUIContent.none,_frame);
        BeginGroup(panel);
        try
        {
            float w=panel.width,h=panel.height;
            L.Label(new Rect(18,10,w-80,40),"Слои карты",_atlasCardTitle);
            if(Button(new Rect(w-54,10,42,40),"×",_button))map.PanelExpanded=false;
            int preset=map.MatchingPreset();
            var titles=new string[AtlasGroupPolicy.Presets.Length];
            for(int i=0;i<titles.Length;i++)titles[i]=AtlasGroupPolicy.Presets[i].Title;
            int chosen=Segmented(new Rect(14,58,w-28,40),titles,preset);
            if(chosen>=0){map.ApplyPreset(chosen);int g=AtlasGroupPolicy.GroupOf(AtlasGroupPolicy.Presets[chosen].Chips.Length>0?AtlasGroupPolicy.Presets[chosen].Chips[0]:"pods");if(g>=0)_mapLayersGroup=g;}
            const float settings=196f;
            Rect list=new(6,108,w-12,h-108-settings);
            var groups=AtlasGroupPolicy.Groups;
            _mapLayersGroup=Mathf.Clamp(_mapLayersGroup,-1,groups.Length-1);
            float content=groups.Length*48f+(_mapLayersGroup>=0?groups[_mapLayersGroup].Chips.Length*44f+4f:0f);
            _mapLayersScroll=BeginScroll(33,list,_mapLayersScroll,content,44f);
            try
            {
                float rowW=list.width-(M26ListPolicy.Extent(content)>list.height?48:0);
                float y=-_mapLayersScroll.y;
                for(int g=0;g<groups.Length;g++)
                {
                    MapGroupRow(new Rect(0,y,rowW,46),map,g);y+=48;
                    if(g!=_mapLayersGroup)continue;
                    foreach(var chip in groups[g].Chips){MapChipRow(new Rect(0,y,rowW,42),map,chip);y+=44;}
                    y+=4;
                }
            }
            finally{EndGroup();}
            float sy=h-settings+8;
            Fill(new Rect(14,sy-6,w-28,2),new Color(.93f,.90f,.84f,1f));
            L.Label(new Rect(18,sy,w-110,36),"Скрывать открытое и собранное",_atlasRowTitle);
            if(MiniSwitch(new Rect(w-74,sy+5,56,28),map.HideOpened))map.HideOpened=!map.HideOpened;
            sy+=40;
            L.Label(new Rect(18,sy,w-110,36),"Скрывать в тумане (неисследованное)",_atlasRowTitle);
            if(MiniSwitch(new Rect(w-74,sy+5,56,28),map.HideFogged))map.HideFogged=!map.HideFogged;
            sy+=44;
            L.Label(new Rect(18,sy,120,40),"Значки",_atlasRowTitle);
            int size=Segmented(new Rect(w-200,sy,182,40),MapSizeTitles,MapChromePolicy.SizeIndex(map.MarkerSize));
            if(size>=0)map.MarkerSize=MapChromePolicy.Sizes[size];
            sy+=48;
            L.Label(new Rect(18,sy,80,40),"ЛКМ",_atlasRowTitle);
            int mode=Segmented(new Rect(96,sy,w-114,40),MapClickTitles,MapChromePolicy.ClickMode(map.CustomPointMode,map.ClickTeleport));
            if(mode>=0)
            {
                map.CustomPointMode=mode==1;map.ClickTeleport=mode==2;
                map.TeleportStatus=mode==1?"Нажмите ЛКМ по суше: сохранится своя метка":mode==2?"Нажмите ЛКМ по суше на карте":"ЛКМ ставит обычную метку";
            }
        }
        finally{EndGroup();}
    }
    private void MapGroupRow(Rect row,MapTools map,int index)
    {
        var group=AtlasGroupPolicy.Groups[index];
        int active=map.GroupActive(group),total=group.Chips.Length;
        bool expanded=index==_mapLayersGroup;
        Fill(new Rect(row.x+8,row.y,row.width-16,2),new Color(.94f,.91f,.85f,1f));
        Rect toggle=new(row.xMax-62,row.y+11,50,26);
        Rect head=new(row.x,row.y,row.width-70,row.height);
        if(CanHover(head))L.Box(head,GUIContent.none,_glowBox);
        if(Pressed(head))_mapLayersGroup=expanded?-1:index;
        MapGlyph(new Rect(row.x+8,row.y+15,16,16),expanded?MapChromePolicy.Chevron:MapChromePolicy.ChevronRight,new Color(.6f,.56f,.5f,1f));
        AtlasChipIcon(new Rect(row.x+30,row.y+8,30,30),group.Chips[0]);
        L.Label(new Rect(row.x+68,row.y+2,row.width-140,22),Loc.T(group.Title).ToUpperInvariant(),_mapGroupHead);
        bool progress=false;
        foreach(var chip in group.Chips)progress|=chip.Progress;
        string sub;
        if(expanded)sub=active+"/"+total+(progress?" · прогресс сейва":"");
        else
        {
            var names=new List<string>(total);
            foreach(var chip in group.Chips)names.Add(Loc.T(chip.Title).ToLowerInvariant());
            sub=string.Join(", ",names);
        }
        L.Label(new Rect(row.x+68,row.y+23,row.width-140,20),Ellipsize(sub,row.width-140,_mapGroupSub),_mapGroupSub);
        if(MiniSwitch(toggle,active>0))map.SetGroup(group,active==0);
    }
    private void MapChipRow(Rect row,MapTools map,AtlasGroupPolicy.Chip chip)
    {
        bool active=map.ChipActive(chip);
        Rect toggle=new(row.xMax-62,row.y+8,50,26);
        if(CanHover(row))L.Box(row,GUIContent.none,_glowBox);
        var saved=GUI.color;
        if(!active)GUI.color=new Color(saved.r,saved.g,saved.b,saved.a*.55f);
        try{AtlasChipIcon(new Rect(row.x+30,row.y+5,32,32),chip);}
        finally{GUI.color=saved;}
        var (total,done)=map.ChipCount(chip.Id);
        float textW=row.width-220;
        L.Label(new Rect(row.x+70,row.y+(chip.Progress&&total>0?1:6),textW,26),chip.Title,_mapChipText);
        if(chip.Progress&&total>0)
        {
            Rect bar=new(row.x+70,row.y+30,Mathf.Min(150,textW),5);
            DrawRoundedSurface(bar,_trackStyle.normal.background,2.5f);
            if(done>0)DrawRoundedSurface(new Rect(bar.x,bar.y,Mathf.Max(5,bar.width*done/total),bar.height),_fillStyle.normal.background,2.5f);
            L.Label(new Rect(row.xMax-150,row.y,82,row.height),AtlasNumber(done)+"/"+AtlasNumber(total),_atlasRight);
        }
        else L.Label(new Rect(row.xMax-150,row.y,82,row.height),total>0?AtlasNumber(total):"—",_atlasRight);
        bool switched=MiniSwitch(toggle,active);
        if(switched||Pressed(row))map.ToggleChip(chip);
    }

    // Card of a selected non-pod marker (pods keep CapsuleCard, own markers CustomMapCard).
    private void MapPointCard(Rect rect,MapTools.Point point)
    {
        var map=_app.Map;
        L.Box(rect,GUIContent.none,_frame);
        var chip=AtlasGroupPolicy.Find(MapTools.ChipOf(point));
        AtlasPointIcon(new Rect(rect.x+16,rect.y+16,50,50),point);
        string title=map.DisplayName(point);
        float titleW=rect.width-140;
        L.Label(new Rect(rect.x+78,rect.y+10,titleW,34),title,AtlasTextWidth(title,_atlasCardTitle)<=titleW?_atlasCardTitle:_atlasRowTitle);
        if(Button(new Rect(rect.xMax-54,rect.y+8,44,44),"×",_button)){map.Selected=null;return;}
        string distance=MapTools.DistanceText(map.Distance(point));
        L.Label(new Rect(rect.x+78,rect.y+44,rect.width-94,22),AtlasJoin(chip?.One,map.ZoneTitleFor(point),distance),_small);
        string state=AtlasState(point,out bool done);
        if(state.Length>0)
        {
            float pillW=Mathf.Min(rect.width-94,AtlasTextWidth(state,_atlasPill)+26);
            Rect pill=new(rect.x+78,rect.y+70,pillW,24);
            DrawRoundedSurface(pill,(done?_selectedButton:_goldButton).normal.background,12f);
            L.Label(pill,state,_atlasPill);
        }
        AtlasPointDetail(new Rect(rect.x+14,rect.y+104,rect.width-28,rect.height-104-64),point);
        MapCardActions(new Rect(rect.x+12,rect.yMax-54,rect.width-24,42),point);
    }
    // «Цель» closes the map so the compass leads to the shown object; teleport; mark as opened.
    private void MapCardActions(Rect rect,MapTools.Point point)
    {
        var map=_app.Map;bool enabled=GUI.enabled;
        float w=(rect.width-16)/3;
        if(Button(new Rect(rect.x,rect.y,w,rect.height),"Цель",_button))
        {
            if(_app.Compass?.Enabled!=true)_app.Notify("Компас выключен: включите его в «Навигации».");
            else{var chosen=point;_app.Queue("atlas point",()=>{map.Selected=chosen;map.CloseNative();});}
        }
        GUI.enabled=enabled&&point.Group!=null&&point.SourceObservation!=true&&!map.Busy;
        if(Button(new Rect(rect.x+w+8,rect.y,w,rect.height),"Телепорт",_accentButton))
        {var chosen=point;_app.Queue("teleport atlas marker",()=>map.RequestTeleport(chosen));}
        GUI.enabled=enabled&&map.CanMark(point);
        bool marked=map.Marked(point);
        if(Button(new Rect(rect.x+2*(w+8),rect.y,w,rect.height),marked?"Снять отметку":"Отметить",marked?_selectedButton:_button))
        {var chosen=point;_app.Queue("toggle map mark",()=>map.ToggleMark(chosen));}
        GUI.enabled=enabled;
    }

    // Hover plaque model: pods are named by their contents («Сердечный элемент»), the sub line
    // says what it is («Сокровищница · компонент улучшения · 186 м»). Rebuilt twice a second.
    private void MapTipModel(MapTools.Point point)
    {
        if(ReferenceEquals(point,_mapTipPoint)&&Time.unscaledTime<_mapTipUntil)return;
        var map=_app.Map;
        _mapTipPoint=point;_mapTipUntil=Time.unscaledTime+.5f;_mapTipReward=null;
        var chip=AtlasGroupPolicy.Find(MapTools.ChipOf(point));
        string distance=MapTools.DistanceText(map.Distance(point));
        if(distance.Length==0)distance="расстояние неизвестно";
        string opened=map.Opened(point)?(point.Category=="Капсулы"?"открыта":"пройдено"):null;
        if(point.Category=="Капсулы")
        {
            var view=map.Capsules.Get(point.Id);
            var rewards=view.Mismatch&&view.NativeObserved?view.NativeRewards:view.Rewards;
            var kinds=new List<string>(rewards.Count);
            foreach(var reward in rewards)kinds.Add(reward.Kind);
            if(rewards.Count>0&&!string.IsNullOrWhiteSpace(rewards[0].Title))
            {
                _mapTipTitleText=Loc.T(rewards[0].Title)+(rewards.Count>1?" +"+(rewards.Count-1):"");
                _mapTipReward=rewards[0].Key;
            }
            else _mapTipTitleText=Loc.T(map.DisplayName(point));
            _mapTipSubText=Loc.T(AtlasJoin(chip?.One??"Сокровищница",AtlasGroupPolicy.PodKind(kinds),distance,opened));
        }
        else
        {
            _mapTipTitleText=Loc.T(map.DisplayName(point));
            _mapTipSubText=Loc.T(AtlasJoin(chip?.One??point.Category,map.ZoneTitleFor(point),distance,opened));
        }
        _mapTipWidth=Mathf.Clamp(Mathf.Max(AtlasTextWidth(_mapTipTitleText,_mapTipTitle),AtlasTextWidth(_mapTipSubText,_mapTipSub))+76,220,520);
    }
    private void MapTipDraw(Rect tip,MapTools.Point point)
    {
        var saved=GUI.color;GUI.color=new Color(.17f,.27f,.38f,.96f);
        try{DrawRoundedSurface(tip,_frame.normal.background,12f);}
        finally{GUI.color=saved;}
        Rect icon=new(tip.x+8,tip.y+(tip.height-40)/2,40,40);
        var reward=_mapTipReward!=null?_app.Map.Icons.Reward(_mapTipReward):null;
        if(reward!=null){DrawRoundedSurface(icon,_trackStyle.normal.background,10f);DrawSprite(new Rect(icon.x+3,icon.y+3,34,34),reward);}
        else AtlasPointIcon(icon,point);
        L.Label(new Rect(tip.x+56,tip.y+5,tip.width-64,26),_mapTipTitleText,_mapTipTitle);
        L.Label(new Rect(tip.x+56,tip.y+30,tip.width-64,20),_mapTipSubText,_mapTipSub);
    }
}
