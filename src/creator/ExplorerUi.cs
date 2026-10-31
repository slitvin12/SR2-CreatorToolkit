using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CreatorToolkit;

// 0.8.1 «Карта» sections (approved mockups 02-04): «Мои метки» (create card, sorted list,
// selected marker card with instant saving), «Радар» (what to track: search, categories,
// tiles; tracked list with colours and «N рядом») and «Навигация» (minimap with a live
// preview, compass). Pure rules: MapExplorerPolicy.
internal sealed partial class ToolkitUi
{
    private int _explorerSection,_customEditColor,_markerSort,_radarCategory,_radarRightFrame=-1,_groupNameFrame=-1;
    private string _customSelection="",_customEditId="",_customEditName="",_customSeenAdded="",_radarSearch="",_groupNameCache;
    private float _customRenameAt=-1f,_radarCategoryOffset,_radarRadiusDraft,_ringCameraAt;
    private Vector2 _customScroll,_radarScroll,_radarListScroll;
    private Camera _ringCamera;
    private GUIStyle _explorerLabel,_explorerRowLabel,_explorerTile,_explorerRightMuted,_explorerCenter,_explorerLink,_explorerPillText,_explorerPillTextWhite;
    private readonly List<string> _markerNames=new();
    private readonly List<float> _markerDistances=new();
    private readonly List<long> _markerCreated=new();
    private readonly MapTools.Point _markerZonePoint=new();
    private readonly List<ActorTools.Entry> _radarTiles=new();
    private readonly HashSet<string> _radarTileSeen=new(StringComparer.Ordinal);
    private readonly int[] _radarCounts=new int[MapExplorerPolicy.RadarCategories.Length];
    private readonly ActorTools.Entry[] _radarCategoryIcons=new ActorTools.Entry[MapExplorerPolicy.RadarCategories.Length];
    private int _radarTilesGeneration=-1,_radarTilesCategory=-1;
    private string _radarTilesSearch;
    private const float PageTop=72f;
    // 0.8.0: «Ферма» moved to «Удобство» (automation lives there); legacy section 3 = farm, 4 = navigation.
    private static readonly string[] ExplorerSections={"Атлас","Мои метки","Радар","Навигация"};
    private static readonly string[] MarkerColorNames={"Розовый","Бирюза","Золото","Лаванда","Лайм","Голубой"};
    private static readonly Color[] MarkerColors={
        new(.95f,.33f,.60f),new(.12f,.74f,.69f),new(1f,.69f,.16f),
        new(.65f,.47f,.95f),new(.46f,.77f,.23f),new(.26f,.64f,.96f)};
    internal static Color MarkerColor(int index)=>MarkerColors[Mathf.Clamp(index,0,MarkerColors.Length-1)];

    private void ExplorerStyles()
    {
        _explorerLabel=CopyStyle(_atlasRowTitle);_explorerLabel.fontSize=16;_explorerLabel.fontStyle=FontStyle.Bold;_explorerLabel.alignment=TextAnchor.MiddleLeft;
        _explorerRowLabel=CopyStyle(_explorerLabel);_explorerRowLabel.fontSize=15;
        _explorerTile=CopyStyle(_small);_explorerTile.fontSize=13;_explorerTile.fontStyle=FontStyle.Bold;_explorerTile.wordWrap=true;
        _explorerTile.alignment=TextAnchor.UpperCenter;_explorerTile.normal.textColor=TextTint(_ink);
        _explorerRightMuted=CopyStyle(_small);_explorerRightMuted.alignment=TextAnchor.MiddleRight;
        _explorerCenter=CopyStyle(_small);_explorerCenter.alignment=TextAnchor.MiddleCenter;
        _explorerLink=CopyStyle(_small);_explorerLink.fontStyle=FontStyle.Bold;_explorerLink.alignment=TextAnchor.MiddleCenter;_explorerLink.normal.textColor=TextTint(_ink);
        _explorerPillText=CopyStyle(_atlasBadge);_explorerPillText.fontSize=12;
        _explorerPillTextWhite=CopyStyle(_explorerPillText);_explorerPillTextWhite.fontSize=13;_explorerPillTextWhite.normal.textColor=TextTint(Color.white);
    }
    private void ResetExplorerUi()
    {
        _explorerSection=_customEditColor=0;_customSelection=_customEditId=_customEditName=_customSeenAdded="";_customRenameAt=-1f;
        _customScroll=_radarScroll=_radarListScroll=Vector2.zero;_radarSearch="";_radarCategory=0;_radarCategoryOffset=0;
        _radarTilesGeneration=-1;_ringCamera=null;
    }
    // Test/IPC indices keep the 0.7 numbering: 0 atlas, 1 markers, 2 radar, 3 farm, 4 navigation.
    internal void ShowExplorerForTest(int section,string id="")
    {
        if(section==3){ShowFarmForTest();return;}
        ShowTabForTest(6);_explorerSection=section>=4?3:Mathf.Clamp(section,0,ExplorerSections.Length-1);_customSelection=id;
    }
    internal bool ExplorerSectionVisibleForTest(int section)=>
        section==3?_app.MenuOpen&&_tab==7&&_farmPage:_app.MenuOpen&&_tab==6&&_explorerSection==(section>=4?3:section);
    // Sections 1-3 under the shared tab row of the «Карта» tab (AtlasPage).
    private void ExplorerPage(float width,float height)
    {
        if(_explorerSection==1)MarkersPage(width,height);
        else if(_explorerSection==2)RadarPage(width,height);
        else NavigationPage(width,height);
    }

    // ---- shared bits ----
    // The six marker colours (mockup 02); returns the pressed one or -1.
    private int ColorDots(Rect rect,int selected,float dot,float gap)
    {
        int pressed=-1;
        for(int i=0;i<MarkerColors.Length;i++)
        {
            Rect r=new(rect.x+i*(dot+gap),rect.y+(rect.height-dot)/2,dot,dot);
            bool hover=CanHover(r);
            var saved=GUI.color;
            try
            {
                if(i==selected)
                {
                    GUI.color=MarkerColor(i);GUI.DrawTexture(new Rect(r.x-5,r.y-5,dot+10,dot+10),_badgeCircle);
                    GUI.color=Color.white;GUI.DrawTexture(new Rect(r.x-2,r.y-2,dot+4,dot+4),_badgeCircle);
                }
                else if(hover){GUI.color=new Color(1f,1f,1f,.95f);GUI.DrawTexture(new Rect(r.x-4,r.y-4,dot+8,dot+8),_badgeCircle);}
                GUI.color=MarkerColor(i);GUI.DrawTexture(r,_badgeCircle);
            }
            finally{GUI.color=saved;}
            if(Pressed(new Rect(r.x-4,r.y-4,dot+8,dot+8)))pressed=i;
        }
        return pressed;
    }
    // A coloured ring around the game's own waypoint flag.
    private void MarkerBadge(Rect rect,int color)
    {
        var saved=GUI.color;
        try
        {
            GUI.color=MarkerColor(color);GUI.DrawTexture(rect,_badgeCircle);
            float inset=Mathf.Max(3f,rect.width*.12f);
            GUI.color=new Color(1f,.99f,.96f,1f);GUI.DrawTexture(new Rect(rect.x+inset,rect.y+inset,rect.width-2*inset,rect.height-2*inset),_badgeCircle);
        }
        finally{GUI.color=saved;}
        var flag=_app.Map.Icons.Category(CustomWaypointTools.Category);
        float f=rect.width*.6f;
        if(flag!=null)DrawSprite(new Rect(rect.center.x-f/2,rect.center.y-f/2,f,f),flag);
    }
    private void Dot(Rect rect,Color color)
    {
        var saved=GUI.color;GUI.color=color;
        try{GUI.DrawTexture(rect,_badgeCircle);}finally{GUI.color=saved;}
    }
    private bool TextLink(Rect rect,string text,GUIStyle style,bool chevron=false)
    {
        if(CanHover(rect))L.Box(rect,GUIContent.none,_glowBox);
        const float glyph=16;
        L.Label(new Rect(rect.x,rect.y,rect.width-(chevron?glyph+6:0),rect.height),text,style);
        if(chevron)MapGlyph(new Rect(rect.xMax-glyph-4,rect.center.y-glyph/2,glyph,glyph),MapChromePolicy.Chevron,_ink);
        return Pressed(rect);
    }
    private void Pill(Rect rect,string text,Texture2D surface)
    {
        DrawRoundedSurface(rect,surface,rect.height*.5f);
        L.Label(rect,text,_explorerPillText);
    }
    private float PillWidth(string text)=>AtlasTextWidth(text,_explorerPillText)+22;
    private string CurrentGroupName()
    {
        if(_groupNameFrame!=Time.frameCount)
        {
            _groupNameFrame=Time.frameCount;
            try{_groupNameCache=Il2Cpp.SystemContext.Instance?.SceneLoader?.CurrentSceneGroup?.name;}catch{_groupNameCache=null;}
        }
        return _groupNameCache;
    }
    private bool ViewForward(out Vector3 forward)
    {
        if(_atlasForwardFrame!=Time.frameCount)
        {
            _atlasForwardFrame=Time.frameCount;_atlasHasForward=false;
            try
            {
                var camera=Camera.main;
                if(camera!=null&&!camera.WasCollected)
                {var f=camera.transform.forward;_atlasForward=new Vector3(f.x,0,f.z);_atlasHasForward=_atlasForward.sqrMagnitude>1e-4f;}
            }
            catch{_atlasHasForward=false;}
        }
        forward=_atlasForward;return _atlasHasForward;
    }
    private string ArrowTo(float x,float z)
    {
        var map=_app.Map;
        if(!map.HasPlayerPosition||!ViewForward(out var forward))return "";
        var delta=new Vector3(x-map.PlayerPosition.x,0,z-map.PlayerPosition.z);
        return delta.sqrMagnitude<1f?"":RadarTools.Arrow(Vector3.SignedAngle(forward,delta,Vector3.up));
    }

    // ---- 02 «Мои метки» ----
    private float MarkerDistance(WaypointRecord record)
    {
        var map=_app.Map;
        if(record==null||!map.HasPlayerPosition||CurrentGroupName()!=record.Group)return -1f;
        float dx=record.X-map.PlayerPosition.x,dz=record.Z-map.PlayerPosition.z;
        return Mathf.Sqrt(dx*dx+dz*dz);
    }
    private string MarkerRegion(WaypointRecord record)
    {
        _markerZonePoint.Id=record.Id;_markerZonePoint.GroupName=record.Group;_markerZonePoint.Category=CustomWaypointTools.Category;
        return _app.Map.ZoneTitleFor(_markerZonePoint);
    }
    private void MarkersPage(float width,float height)
    {
        var tools=_app.Custom;tools.EnsureLoaded();_app.Map.SamplePlayer();
        float top=PageTop,leftW=648,rightX=leftW+20,rightW=width-rightX;
        bool enabled=GUI.enabled;
        if(tools.LastAddedId.Length>0&&tools.LastAddedId!=_customSeenAdded){_customSeenAdded=tools.LastAddedId;_customSelection=tools.LastAddedId;}
        // Create card: name, colour, «Поставить здесь» / «Указать на карте».
        L.Box(new Rect(0,top,leftW,118),GUIContent.none,_card);
        const float dot=28,gap=9;float dotsW=6*dot+5*gap;
        tools.DraftName=TextBox("custom-new-name",new Rect(18,top+14,leftW-56-dotsW,42),tools.DraftName,48,"Название новой метки…");
        int draft=ColorDots(new Rect(leftW-20-dotsW,top+14,dotsW,42),tools.DraftColor,dot,gap);
        if(draft>=0)tools.DraftColor=draft;
        GUI.enabled=enabled&&tools.Records.Count<ExplorerPolicy.MaxCustomWaypoints;
        float half=(leftW-46)/2;
        if(Button(new Rect(18,top+66,half,40),"Поставить здесь",_accentButton))_app.Queue("custom current position",tools.SaveCurrent);
        if(Button(new Rect(28+half,top+66,half,40),"Указать на карте",_button))
            _app.Queue("custom placement map",()=>{_app.Map.ClickTeleport=false;_app.Map.CustomPointMode=true;_app.Map.Open();});
        GUI.enabled=enabled;
        MarkerList(new Rect(0,top+130,leftW,height-top-130),tools);
        MarkerCard(new Rect(rightX,top,rightW,height-top),tools);
        GUI.enabled=enabled;
    }
    private void MarkerList(Rect rect,CustomWaypointTools tools)
    {
        L.Box(rect,GUIContent.none,_card);
        var records=tools.Records;
        L.Label(new Rect(rect.x+20,rect.y+10,rect.width-250,28),MapExplorerPolicy.ListHeader(records.Count,ExplorerPolicy.MaxCustomWaypoints),_atlasRowSub);
        if(TextLink(new Rect(rect.xMax-228,rect.y+8,210,32),MapExplorerPolicy.MarkerSorts[Mathf.Clamp(_markerSort,0,2)],_atlasRight,true))
        {_markerSort=MapExplorerPolicy.NextSort(_markerSort);_customScroll.y=0;}
        _markerNames.Clear();_markerDistances.Clear();_markerCreated.Clear();
        foreach(var record in records){_markerNames.Add(record.Name);_markerDistances.Add(MarkerDistance(record));_markerCreated.Add(record.Created);}
        var order=MapExplorerPolicy.SortMarkers(_markerNames,_markerDistances,_markerCreated,_markerSort);
        const float rowH=58;
        Rect view=new(rect.x+6,rect.y+46,rect.width-12,rect.height-52);
        _customScroll=BeginScroll(40,view,_customScroll,order.Count*rowH,rowH);
        try
        {
            float w=view.width-(M26ListPolicy.Extent(order.Count*rowH)>view.height?48:4);
            string goal=tools.GoalId;
            for(int k=0;k<order.Count;k++)
            {
                int i=order[k];var record=records[i];float y=k*rowH-_customScroll.y;
                if(y+rowH<=0||y>=view.height)continue;
                Rect row=new(2,y+1,w,rowH-4);
                bool selected=record.Id==_customSelection;
                if(selected)
                {
                    DrawRoundedSurface(row,_selectedButton.normal.background,14f);
                    var tint=GUI.color;GUI.color=new Color(.86f,.97f,.94f,1f);
                    try{DrawRoundedSurface(new Rect(row.x+2,row.y+2,row.width-4,row.height-4),_card.normal.background,12f);}
                    finally{GUI.color=tint;}
                }
                else if(CanHover(row))L.Box(row,GUIContent.none,_glowBox);
                if(Pressed(row))_customSelection=record.Id;
                MarkerBadge(new Rect(row.x+10,row.y+(row.height-34)/2,34,34),record.Color);
                float right=row.xMax-12,distance=_markerDistances[i];
                L.Label(new Rect(right-90,row.y,90,row.height),distance>=0?MapTools.DistanceText(distance):"—",_atlasRight);
                right-=100;
                if(record.Id==goal)
                {
                    Rect pill=new(right-72,row.y+(row.height-24)/2,72,24);
                    DrawRoundedSurface(pill,_trackStyle.normal.background,12f);
                    MapGlyph(new Rect(pill.x+7,pill.y+4,16,16),MapChromePolicy.Target,_accent);
                    L.Label(new Rect(pill.x+24,pill.y,pill.width-28,pill.height),"цель",_explorerPillText);
                    right-=80;
                }
                float textX=row.x+56,textW=Mathf.Max(40,right-textX);
                L.Label(new Rect(textX,row.y+5,textW,26),RawContent(record.Name??""),_atlasRowTitle); // the player's own text: never translated
                L.Label(new Rect(textX,row.y+30,textW,20),AtlasJoin(MarkerRegion(record)??"Зона не найдена",record.ExactHeight?null:"высота не проверена"),_atlasRowSub);
            }
            if(order.Count==0)
                L.Label(new Rect(16,10,view.width-40,90),"Пока нет своих меток.\n«Поставить здесь» — где стоит игрок, «Указать на карте» — щелчком по атласу.",_muted);
        }
        finally{EndGroup();}
    }
    private void FlushRename(CustomWaypointTools tools)
    {
        if(_customRenameAt<0)return;
        _customRenameAt=-1f;
        var record=tools.Find(_customEditId);
        string name=(_customEditName??"").Trim();
        if(record==null||name.Length==0||name==record.Name)return;
        string id=record.Id;int color=record.Color;
        _app.Queue("custom rename",()=>tools.Rename(id,name,color));
    }
    private void MarkerCard(Rect rect,CustomWaypointTools tools)
    {
        var map=_app.Map;bool enabled=GUI.enabled;
        L.Box(rect,GUIContent.none,_card);
        float x=rect.x+24,w=rect.width-48,y=rect.y+18;
        L.Label(new Rect(x,y,w,22),"ВЫБРАННАЯ МЕТКА",_atlasCaps);
        var selected=tools.Find(_customSelection);
        if(selected==null)
        {
            FlushRename(tools);
            L.Label(new Rect(x,y+34,w,110),tools.Records.Count==0?"Создайте первую метку слева.":
                DetailedHelp?"Выберите метку в списке.\nИмя и цвет сохраняются сразу; удаление — с подтверждением.":"Выберите метку в списке.",_muted);
            return;
        }
        if(_customEditId!=selected.Id){FlushRename(tools);_customEditId=selected.Id;_customEditName=selected.Name;_customEditColor=selected.Color;}
        string name=TextBox("custom-edit-name",new Rect(x,y+28,w,44),_customEditName,48,"Название метки…");
        if(name!=_customEditName){_customEditName=name;_customRenameAt=Time.unscaledTime+MapExplorerPolicy.RenameDelay;}
        if(_customRenameAt>=0&&Time.unscaledTime>=_customRenameAt)FlushRename(tools);
        float distance=MarkerDistance(selected);
        string where=distance>=0?(MapTools.DistanceText(distance)+" "+ArrowTo(selected.X,selected.Z)).TrimEnd():"другой остров";
        L.Label(new Rect(x+2,y+80,w,22),AtlasJoin(MarkerRegion(selected)??"Зона не найдена",where,MapExplorerPolicy.CreatedText(selected.Created)),_atlasRowSub);
        L.Label(new Rect(x,y+116,w,22),"ЦВЕТ",_atlasCaps);
        _customEditColor=selected.Color;
        int color=ColorDots(new Rect(x+5,y+144,w-10,44),_customEditColor,34,9);
        if(color>=0&&color!=selected.Color)
        {
            string id=selected.Id,typed=(_customEditName??"").Trim();
            string keep=typed.Length>0?typed:selected.Name;_customRenameAt=-1f;
            _app.Queue("custom color",()=>tools.Rename(id,keep,color));
        }
        string goal=tools.GoalId;bool isGoal=goal==selected.Id,beacon=isGoal&&tools.Beacon;
        GUI.enabled=enabled&&selected.ExactHeight;
        L.Label(new Rect(x,y+206,w-70,34),"Маяк в мире",_explorerLabel);
        if(MiniSwitch(new Rect(rect.xMax-24-52,y+209,52,28),beacon))
        {string id=selected.Id;bool on=!beacon;_app.Queue("custom beacon",()=>tools.SetBeacon(id,on));}
        GUI.enabled=enabled;
        L.Label(new Rect(x,y+248,w-70,34),"Цель на компасе",_explorerLabel);
        if(MiniSwitch(new Rect(rect.xMax-24-52,y+251,52,28),isGoal))
        {string id=selected.Id;bool on=!isGoal;_app.Queue("custom navigation goal",()=>{if(on)tools.SetGoal(id);else tools.HideGoal();});}
        var point=tools.FindPoint(selected.Id);
        string note=point?.Group==null?"Зона этой метки сейчас недоступна. Телепорт отключён.":
            !selected.ExactHeight?"Точка с карты: маяк появится после проверки высоты при телепорте.":null;
        if(note!=null)L.Label(new Rect(x,y+290,w,44),note,_atlasDetail);
        float buttons=rect.yMax-140;
        GUI.enabled=enabled&&point?.Group!=null;
        if(Button(new Rect(x,buttons,w,44),"Показать на карте",_accentButton))
        {var chosen=point;_app.Queue("show custom map marker",()=>_app.Map.OpenAt(chosen));}
        float half=(w-10)/2;
        GUI.enabled=enabled&&point?.Group!=null&&!map.Busy;
        if(Button(new Rect(x,buttons+52,half,40),"Телепорт",_button))
        {var chosen=point;_app.Queue("safe custom teleport",()=>_app.Map.RequestTeleport(chosen));}
        GUI.enabled=enabled;
        if(Button(new Rect(x+half+10,buttons+52,half,40),"Удалить",_button))
        {
            string id=selected.Id;
            _app.Confirm("Удалить свою метку?","Будет удалена только выбранная пользовательская метка. Капсулы и другие объекты игры не затрагиваются.",()=>tools.Delete(id));
        }
        L.Label(new Rect(x,rect.yMax-40,w,26),"Изменения сохраняются сразу",_explorerCenter);
    }

    // ---- 03 «Радар» ----
    private List<ActorTools.Entry> RadarTiles()
    {
        string needle=(_radarSearch??"").Trim().ToLowerInvariant();
        if(_radarTilesGeneration==_app.Actors.Generation&&_radarTilesCategory==_radarCategory&&_radarTilesSearch==needle)return _radarTiles;
        _radarTilesGeneration=_app.Actors.Generation;_radarTilesCategory=_radarCategory;_radarTilesSearch=needle;
        _radarTiles.Clear();_radarTileSeen.Clear();Array.Clear(_radarCounts,0,_radarCounts.Length);
        var categories=MapExplorerPolicy.RadarCategories;
        string wanted=categories[Mathf.Clamp(_radarCategory,0,categories.Length-1)];
        foreach(var entry in _app.Actors.Catalog)
        {
            if(!RadarTools.Trackable(entry)||!_radarTileSeen.Add(entry.Type.ReferenceId))continue;
            int c=Array.IndexOf(categories,entry.Category);
            if(c>0&&_radarCategoryIcons[c]==null)_radarCategoryIcons[c]=entry;
            if(needle.Length>0&&(entry.Search==null||!entry.Search.Contains(needle)))continue;
            _radarCounts[0]++;if(c>0)_radarCounts[c]++;
            if(_radarCategory==0||entry.Category==wanted)_radarTiles.Add(entry);
        }
        if(_radarCategoryIcons[0]==null)_radarCategoryIcons[0]=_radarCategoryIcons[1];
        return _radarTiles;
    }
    private float RadarCategoryWidth(int index)
    {
        int count=_radarCounts[index];
        return 46+AtlasTextWidth(MapExplorerPolicy.RadarCategories[index],_atlasGroupText)+(count>0?8+AtlasBadgeWidth(count):0)+16;
    }
    private void RadarCategoryStrip(Rect strip)
    {
        var categories=MapExplorerPolicy.RadarCategories;
        float content=0;
        for(int i=0;i<categories.Length;i++)content+=RadarCategoryWidth(i)+(i>0?8:0);
        _radarCategoryOffset=AtlasCarousel(strip,_radarCategoryOffset,content,out Rect view);
        BeginGroup(view);
        try
        {
            float x=-_radarCategoryOffset;
            for(int i=0;i<categories.Length;i++)
            {
                float w=RadarCategoryWidth(i);
                Rect pill=new(x,4,w,strip.height-4);
                if(pill.xMax>0&&pill.x<view.width)
                {
                    bool selected=i==_radarCategory;
                    if(Button(pill,GUIContent.none,selected?_selectedButton:_button))
                    {
                        _radarCategory=i;_radarScroll.y=0;
                        _radarCategoryOffset=AtlasGroupPolicy.CarouselReveal(_radarCategoryOffset,x+_radarCategoryOffset,w,content,view.width);
                    }
                    bool hovered=CanHover(pill);
                    float offset=HoverOffset(pill,hovered,hovered&&Mouse.current!=null&&Mouse.current.leftButton.isPressed);
                    var icon=_radarCategoryIcons[i];
                    if(icon!=null)SpritePreview(HoverChildRect(pill,new Rect(pill.x+9,pill.y+(pill.height-30)/2-2,30,30),offset),icon);
                    float textW=AtlasTextWidth(categories[i],_atlasGroupText);
                    HoverText(HoverChildRect(pill,new Rect(pill.x+46,pill.y-2,textW+4,pill.height),offset),categories[i],_atlasGroupText,offset);
                    int count=_radarCounts[i];
                    if(count>0)
                    {
                        Rect badge=HoverChildRect(pill,new Rect(pill.x+54+textW,pill.y+(pill.height-22)/2-2,AtlasBadgeWidth(count),22),offset);
                        DrawRoundedSurface(badge,_trackStyle.normal.background,11f);
                        L.Label(badge,AtlasNumber(count),_atlasBadge);
                    }
                }
                x+=w+8;
            }
        }
        finally{EndGroup();}
    }
    private void RadarPage(float width,float height)
    {
        var tools=_app.Radar;tools.EnsureLoaded();
        float top=PageTop,leftW=660,rightX=leftW+18,rightW=width-rightX;
        bool enabled=GUI.enabled;
        // Master switch, radius and the count of tracked targets.
        L.Box(new Rect(0,top,leftW,52),GUIContent.none,_card);
        if(MiniSwitch(new Rect(18,top+12,52,28),tools.On)){bool on=!tools.On;_app.Queue("radar switch",()=>tools.SetOn(on));}
        L.Label(new Rect(82,top,150,52),"Радар включён",_explorerLabel);
        Fill(new Rect(236,top+13,1,26),new Color(.55f,.47f,.38f,.25f));
        L.Label(new Rect(250,top,70,52),"Радиус",_muted);
        // The count pill is measured first: a longer (English) caption shortens the slider instead of covering the metres.
        string count=MapExplorerPolicy.OnRadar(tools.Targets.Count);
        float countW=PillWidth(count),countX=leftW-18-countW;
        float metresX=Mathf.Min(482,countX-70),sliderW=Mathf.Clamp(metresX-332,90,150);
        float shown=_dragSlider==65?_radarRadiusDraft:tools.Radius;
        float value=Slider(65,new Rect(322,top+12,sliderW,28),shown,MapExplorerPolicy.MinRadius,MapExplorerPolicy.MaxRadius);
        int metres=MapExplorerPolicy.ClampRadius(value);
        if(_dragSlider==65)_radarRadiusDraft=value;
        else if(metres!=tools.Radius)_app.Queue("radar radius",()=>tools.SetRadius(metres));
        L.Label(new Rect(metresX,top,64,52),metres+" м",_explorerLabel);
        Pill(new Rect(countX,top+14,countW,24),count,_trackStyle.normal.background);
        // What to track: search, categories and tiles.
        Rect search=new(0,top+64,leftW,44);
        string typed=TextBox("radar-search",search,_radarSearch,60,"Что отметить? Слайм, еда, ресурс, игрушка, гордо…");
        if(typed!=_radarSearch){_radarSearch=typed;_radarScroll.y=0;}
        MapGlyph(new Rect(search.xMax-36,search.y+11,22,22),MapChromePolicy.Search,new Color(.45f,.39f,.33f,.8f));
        var tiles=RadarTiles();
        RadarCategoryStrip(new Rect(0,top+120,leftW,46));
        RadarGrid(new Rect(0,top+180,leftW,height-top-180),tiles,tools);
        RadarTracked(new Rect(rightX,top,rightW,height-top),tools);
        GUI.enabled=enabled;
    }
    private void RadarGrid(Rect rect,List<ActorTools.Entry> tiles,RadarTools tools)
    {
        L.Box(rect,GUIContent.none,_card);
        const int columns=5;const float tileH=112,gap=10;
        int rows=(tiles.Count+columns-1)/columns;
        Rect view=new(rect.x+10,rect.y+10,rect.width-16,rect.height-16);
        float content=rows*(tileH+gap);
        _radarScroll=BeginScroll(41,view,_radarScroll,content,tileH+gap);
        try
        {
            float inner=view.width-(M26ListPolicy.Extent(content)>view.height?48:2);
            float tileW=(inner-gap*(columns-1))/columns;
            var mouse=Mouse.current;
            bool right=mouse!=null&&mouse.rightButton.wasPressedThisFrame&&_radarRightFrame!=Time.frameCount&&InputEnabled;
            for(int i=0;i<tiles.Count;i++)
            {
                int row=i/columns,col=i%columns;
                float y=row*(tileH+gap)-_radarScroll.y;
                if(y+tileH<=0||y>=view.height)continue;
                Rect tile=new(col*(tileW+gap),y,tileW,tileH);
                var entry=tiles[i];var target=tools.Tracked(entry.Type.ReferenceId);
                bool hover=CanHover(tile);
                if(target!=null)
                {
                    DrawRoundedSurface(tile,_selectedButton.normal.background,16f);
                    DrawRoundedSurface(new Rect(tile.x+3,tile.y+3,tile.width-6,tile.height-6),_card.normal.background,13f);
                }
                else DrawRoundedSurface(tile,_button.normal.background,16f);
                if(hover)L.Box(tile,GUIContent.none,_glowBox);
                SpritePreview(new Rect(tile.center.x-29,tile.y+12,58,58),entry);
                L.Label(new Rect(tile.x+4,tile.y+72,tile.width-8,38),entry.Name,_explorerTile);
                Rect corner=new(tile.xMax-28,tile.y+6,22,22);
                if(target!=null){Dot(corner,MarkerColor(target.Color));MapGlyph(new Rect(corner.x+3,corner.y+3,16,16),MapChromePolicy.Check,Color.white);}
                else{DrawRoundedSurface(corner,_trackStyle.normal.background,11f);MapGlyph(new Rect(corner.x+4,corner.y+4,14,14),MapChromePolicy.Plus,new Color(.45f,.39f,.33f,.9f));}
                if(Pressed(tile)){var chosen=entry;_app.Queue("radar target",()=>tools.Toggle(chosen));}
                else if(right&&target!=null&&hover)
                {_radarRightFrame=Time.frameCount;string reference=target.Reference;_app.Queue("radar colour",()=>tools.CycleColor(reference));}
            }
            if(tiles.Count==0)
                L.Label(new Rect(8,8,view.width-60,80),_app.Actors.Catalog.Count==0?"Каталог ещё загружается…":"Ничего не найдено. Измените поиск или категорию.",_muted);
        }
        finally{EndGroup();}
        Hint(new Rect(rect.x+14,rect.yMax+2,rect.width-28,20),"ЛКМ — отметить или снять · ПКМ по отмеченной плитке — другой цвет.",_small);
    }
    private void RadarTracked(Rect rect,RadarTools tools)
    {
        bool enabled=GUI.enabled;
        L.Box(rect,GUIContent.none,_card);
        float x=rect.x+22,w=rect.width-44;
        var targets=tools.Targets;
        float titleW=AtlasTextWidth("На радаре",_mapBarTitle);
        L.Label(new Rect(x,rect.y+14,titleW+4,34),"На радаре",_mapBarTitle);
        L.Label(new Rect(x+titleW+12,rect.y+14,60,34),targets.Count.ToString(),_muted);
        if(targets.Count>0&&TextLink(new Rect(rect.xMax-128,rect.y+16,106,30),"Очистить",_explorerRightMuted))
            _app.Confirm("Очистить радар?","С радара будут сняты все цели этого сейва.",tools.Clear);
        const float rowH=50;
        float listBottom=rect.yMax-(DetailedHelp?184:148);
        Rect view=new(rect.x+8,rect.y+56,rect.width-16,listBottom-(rect.y+56));
        _radarListScroll=BeginScroll(42,view,_radarListScroll,targets.Count*rowH,rowH);
        string remove=null,cycle=null;
        try
        {
            float rw=view.width-(M26ListPolicy.Extent(targets.Count*rowH)>view.height?48:4);
            for(int i=0;i<targets.Count;i++)
            {
                var t=targets[i];float y=i*rowH-_radarListScroll.y;
                if(y+rowH<=0||y>=view.height)continue;
                Rect row=new(2,y,rw,rowH-2);
                if(i>0)Fill(new Rect(row.x+12,row.y,row.width-24,1),new Color(.55f,.47f,.38f,.16f));
                Rect dot=new(row.x+8,row.center.y-6,12,12);
                Dot(dot,MarkerColor(t.Color));
                if(Pressed(new Rect(row.x+2,row.y+8,24,row.height-16)))cycle=t.Reference;
                if(t.Entry!=null)SpritePreview(new Rect(row.x+28,row.center.y-17,34,34),t.Entry);
                Rect close=new(row.xMax-30,row.center.y-14,28,28);
                if(CanHover(close))L.Box(close,GUIContent.none,_glowBox);
                MapGlyph(new Rect(close.x+6,close.y+6,16,16),MapChromePolicy.Cross,new Color(.45f,.39f,.33f,.85f));
                if(Pressed(close))remove=t.Reference;
                string near=MapExplorerPolicy.NearText(t.Near);
                float nearW=PillWidth(near);
                Pill(new Rect(close.x-8-nearW,row.center.y-12,nearW,24),near,(t.Near>0?_selectedButton:_trackStyle).normal.background);
                float textX=row.x+70,textW=Mathf.Max(40,close.x-16-nearW-textX);
                float nameW=Mathf.Min(AtlasTextWidth(t.Name,_atlasRowTitle),textW-(t.Auto?54:0));
                L.Label(new Rect(textX,row.y+3,nameW+2,26),t.Name,_atlasRowTitle);
                if(t.Auto)Pill(new Rect(textX+nameW+8,row.y+7,44,20),"авто",_trackStyle.normal.background);
                L.Label(new Rect(textX,row.y+27,textW,20),MapExplorerPolicy.RadarSub(t.Category,t.Nearest,t.Auto?t.OwnerName:null),_atlasRowSub);
            }
            if(targets.Count==0)
                L.Label(new Rect(14,6,view.width-28,110),"Пока пусто. Нажмите на плитку слева — она появится здесь со своим цветом.",_muted);
        }
        finally{EndGroup();}
        if(remove!=null){string reference=remove;_app.Queue("radar remove",()=>tools.Remove(reference));}
        if(cycle!=null){string reference=cycle;_app.Queue("radar colour",()=>tools.CycleColor(reference));}
        if(DetailedHelp)L.Label(new Rect(x,rect.yMax-176,w,34),tools.Status,_small);
        if(Button(new Rect(x,rect.yMax-138,w,42),"+ Любимая еда слаймов из моих загонов",_button))
            _app.Queue("radar corral favourites",()=>
            {
                int added=tools.AddCorralFavorites();
                _app.Notify(added>0?"Радар: добавлено любимой еды — "+added:"Радар: новой любимой еды в загонах не найдено");
            });
        L.Label(new Rect(x,rect.yMax-88,w-70,34),"Подсвечивать в мире",_explorerLabel);
        if(MiniSwitch(new Rect(rect.xMax-22-52,rect.yMax-85,52,28),tools.Highlight)){bool on=!tools.Highlight;_app.Queue("radar highlight",()=>tools.SetHighlight(on));}
        L.Label(new Rect(x,rect.yMax-48,w-70,34),"На компасе и мини-карте",_explorerLabel);
        if(MiniSwitch(new Rect(rect.xMax-22-52,rect.yMax-45,52,28),tools.ShowOnHud)){bool on=!tools.ShowOnHud;_app.Queue("radar hud",()=>tools.SetShowOnHud(on));}
        GUI.enabled=enabled;
    }

    // ---- 04 «Навигация» ----
    private int CornerPicker(Rect rect,int corner)
    {
        DrawRoundedSurface(rect,_trackStyle.normal.background,12f);
        int pressed=-1;
        for(int c=0;c<4;c++)
        {
            bool right=MapExplorerPolicy.CornerRight(c),bottom=MapExplorerPolicy.CornerBottom(c);
            Vector2 at=new(right?rect.xMax-22:rect.x+22,bottom?rect.yMax-19:rect.y+19);
            Rect hit=new(at.x-21,at.y-18,42,36);
            bool hover=CanHover(hit);
            float d=c==corner?26:20;
            if(c==corner)Dot(new Rect(at.x-d/2-3,at.y-d/2-3,d+6,d+6),Color.white);
            Dot(new Rect(at.x-d/2,at.y-d/2,d,d),c==corner?_accent:hover?new Color(.74f,.70f,.64f):new Color(.85f,.82f,.77f));
            if(Pressed(hit))pressed=c;
        }
        return pressed;
    }
    // Segmented choice (also used by the capture and convenience pages).
    private int NavigationChoice(Rect rect,string[] titles,int selected)
    {
        int clicked=-1;float w=(rect.width-8*(titles.Length-1))/titles.Length;
        for(int i=0;i<titles.Length;i++)
            if(Button(new Rect(rect.x+i*(w+8),rect.y,w,rect.height),titles[i],i==selected?_selectedButton:_button))clicked=i;
        return clicked;
    }
    // The chip names a preset («Сокровища») or the custom set, never the toolbar wording «Слои: N».
    private string MiniMapLayersName(MapTools map)
    {
        int preset=map.MatchingPreset();
        if(preset>=0)return AtlasGroupPolicy.Presets[preset].Title;
        int active=0;
        foreach(var group in AtlasGroupPolicy.Groups)active+=map.GroupActive(group);
        return active==0?"выключены":"свои ("+active+")";
    }
    private void NavigationPage(float width,float height)
    {
        var nav=_app.Navigation;var map=_app.Map;
        if(nav==null){L.Label(new Rect(0,PageTop,width,34),"Настройки навигации недоступны",_heading);return;}
        var s=nav.Settings;float top=PageTop;
        float leftW=718,rightX=leftW+18,rightW=width-rightX;
        // Minimap card: live preview and its settings.
        L.Box(new Rect(0,top,leftW,height-top),GUIContent.none,_card);
        L.Label(new Rect(24,top+14,300,32),"Мини-карта",_mapBarTitle);
        L.Label(new Rect(leftW-244,top+16,220,28),"сохраняется сразу",_explorerRightMuted);
        Rect disc=new(32,top+70,252,252);
        map.PreviewMiniMap();
        DrawMiniMapPreview(disc,s,nav.Range);
        L.Label(new Rect(8,disc.yMax+14,300,24),"Предпросмотр · как будет в игре",_explorerCenter);
        float lx=304,cx=462,cw=leftW-cx-22,y=top+62;
        L.Label(new Rect(lx,y,156,40),"Режим",_explorerRowLabel);
        int mode=Segmented(new Rect(cx,y,cw,40),M29NavigationSettings.ModeTitles,s.Mode);
        if(mode>=0&&mode!=s.Mode)_app.Queue("minimap mode",()=>nav.SetMode(mode));
        y+=49;
        L.Label(new Rect(lx,y,156,40),"Размер",_explorerRowLabel);
        int size=Segmented(new Rect(cx,y,cw,40),MapExplorerPolicy.SizeLetters,s.Size);
        if(size>=0)nav.Edit(n=>n.Size=size);
        y+=49;
        L.Label(new Rect(lx,y,156,40),"Масштаб",_explorerRowLabel);
        float t=M29NavigationSettings.RangeToSlider(s.Range);
        float slid=Slider(60,new Rect(cx,y+6,cw-76,28),t,0f,1f);
        if(Math.Abs(slid-t)>1e-4f)nav.SetRange(M29NavigationSettings.SliderToRange(slid));
        L.Label(new Rect(cx+cw-70,y,70,40),M29NavigationSettings.Metres(s.Range),_atlasRight);
        y+=49;
        L.Label(new Rect(lx,y,156,40),"Непрозрачность",_explorerRowLabel);
        int opacity=MapExplorerPolicy.Opacity(s.Transparency);
        int chosen=Mathf.RoundToInt(Slider(61,new Rect(cx,y+6,cw-76,28),opacity,0,100));
        int clear=MapExplorerPolicy.TransparencyFor(chosen);
        if(clear!=s.Transparency)nav.Edit(n=>n.Transparency=clear,false);
        L.Label(new Rect(cx+cw-70,y,70,40),MapExplorerPolicy.Opacity(s.Transparency)+" %",_atlasRight);
        y+=49;
        L.Label(new Rect(lx,y,156,40),"Ориентация",_explorerRowLabel);
        int north=Segmented(new Rect(cx,y,cw,40),MapExplorerPolicy.OrientationTitles,s.NorthUp?1:0);
        if(north>=0)nav.Edit(n=>n.NorthUp=north==1);
        y+=49;
        L.Label(new Rect(lx,y+16,156,40),"Угол экрана",_explorerRowLabel);
        int corner=CornerPicker(new Rect(cx,y,124,72),s.Corner);
        if(corner>=0)nav.Edit(n=>n.Corner=corner);
        if(DetailedHelp)L.Label(new Rect(cx+136,y,cw-136,72),M29NavigationSettings.CornerTitles[Mathf.Clamp(s.Corner,0,3)],_small);
        y+=86;
        // «Значки = слои атласа: <слои> + радар · Изменить →»: the minimap shows the atlas layers.
        Rect chip=new(24,Mathf.Max(y,disc.yMax+52),leftW-48,40);
        bool radarIcons=_app.Radar.Enabled&&_app.Radar.ShowOnHud;
        if(CanHover(chip))L.Box(chip,GUIContent.none,_glowBox);
        DrawRoundedSurface(chip,_trackStyle.normal.background,12f);
        L.Label(new Rect(chip.x+16,chip.y,chip.width-150,chip.height),MapExplorerPolicy.LayersChip(MiniMapLayersName(map),radarIcons),_small);
        L.Label(new Rect(chip.xMax-136,chip.y,120,chip.height),"Изменить →",_atlasRight);
        if(Pressed(chip)){_explorerSection=0;ClearInteraction();}
        var terrain=_app.MiniMapTerrain;
        if(DetailedHelp&&s.Mode==1&&terrain!=null)L.Label(new Rect(24,height-40,leftW-48,30),terrain.Status,_small);
        // Compass card.
        Rect rc=new(rightX,top,rightW,height-top);
        L.Box(rc,GUIContent.none,_card);
        float x=rc.x+22,w=rc.width-44;y=top+14;
        L.Label(new Rect(x,y,220,32),"Компас",_mapBarTitle);
        var compass=_app.Compass;bool markers=compass!=null&&compass.Enabled;
        if(MiniSwitch(new Rect(rc.xMax-22-52,y+2,52,28),markers)){bool on=!markers;_app.Queue("compass markers",()=>nav.SetCompass(on));}
        L.Label(new Rect(x,y+36,w,22),"Метки целей на полосе компаса",_small);
        y=top+86;
        L.Label(new Rect(x,y,w-90,30),"Дальность",_explorerLabel);
        L.Label(new Rect(rc.xMax-22-100,y,100,30),s.CompassRange+" м",_atlasRight);
        int reach=M29NavigationSettings.ClampCompassRange(Mathf.RoundToInt(Slider(62,new Rect(x,y+32,w,28),s.CompassRange,M29NavigationSettings.MinCompassRange,M29NavigationSettings.MaxCompassRange)));
        if(reach!=s.CompassRange)nav.Edit(n=>n.CompassRange=reach,false);
        y=top+160;
        L.Label(new Rect(x,y,w-130,36),"Максимум меток",_explorerLabel);
        Rect plus=new(rc.xMax-22-36,y,36,36),minus=new(plus.x-36-52,y,36,36);
        bool enabled=GUI.enabled;
        GUI.enabled=enabled&&s.CompassMarkers>M29NavigationSettings.MinCompassMarkers;
        if(Button(minus,"−",_button))nav.Edit(n=>n.CompassMarkers--);
        GUI.enabled=enabled&&s.CompassMarkers<M29NavigationSettings.MaxCompassMarkers;
        if(Button(plus,"+",_button))nav.Edit(n=>n.CompassMarkers++);
        GUI.enabled=enabled;
        L.Label(new Rect(minus.xMax,y,plus.x-minus.xMax,36),s.CompassMarkers.ToString(),_explorerLink);
        y=top+218;
        L.Label(new Rect(x,y,w,30),"Призрачные дроны",_explorerLabel);
        int ghosts=Segmented(new Rect(x,y+34,w,40),MapExplorerPolicy.GhostTitles,s.Ghosts);
        if(ghosts>=0)nav.Edit(n=>n.Ghosts=ghosts);
        Hint(new Rect(x,y+82,w,44),s.Ghosts==2?"Ближайший призрачный дрон зоны с наградой — всегда, без предела дальности.":
            s.Ghosts==1?"Призрачные дроны раньше других слоёв, как свои метки.":"Призрачные дроны — наравне с другими дронами.",_atlasDetail);
        if(DetailedHelp)L.Label(new Rect(x,y+130,w,96),(compass!=null?compass.Status:"Компас недоступен")+"\n"+nav.Status,_atlasDetail);
        if(TextLink(new Rect(x,rc.yMax-54,w,36),"Сбросить вид и лимиты",_explorerLink))_app.Queue("navigation reset",nav.ResetLook);
    }

    // ---- farm (0.8.0: «Удобство → Ферма…»; the cards start at «top», the heading is drawn by the caller) ----
    private void FarmPage(float width,float top)
    {
        var tools=_app.Farm;float col=(width-18)/2,x=col+18,o=top-145;
        L.Box(new Rect(0,145+o,col,200),GUIContent.none,_card);
        NativeRequiredIcon(new Rect(18,162+o,42,42),"PinkPlort");
        L.Label(new Rect(76,164+o,col-92,40),"Сбор плортов",_heading);
        Hint(new Rect(18,219+o,col-36,54),"Купленный сборщик загона · до 30 м.");
        if(Button(new Rect(18,283+o,col-36,48),tools.Collect?"Автосбор: ВКЛ · отключить":"Автосбор: ВЫКЛ · включить…",tools.Collect?_selectedButton:_button))
            tools.Request(!tools.Collect,tools.Feed);
        L.Box(new Rect(x,145+o,col,200),GUIContent.none,_card);
        NativeRequiredIcon(new Rect(x+18,162+o,42,42),"Feeder");
        L.Label(new Rect(x+76,164+o,col-92,40),"Пополнение кормушек",_heading);
        Hint(new Rect(x+18,219+o,col-36,54),"Из выбранного слота в кормушку · до 4 шт./с.");
        if(Button(new Rect(x+18,283+o,col-36,48),tools.Feed?"Кормушки: ВКЛ · отключить":"Кормушки: ВЫКЛ · включить…",tools.Feed?_selectedButton:_button))
            tools.Request(tools.Collect,!tools.Feed);
        // M27: corral autofeed - hungry corral slimes get one portion of their own diet.
        var feed=_app.AutoFeed;bool feeding=feed!=null&&feed.Enabled;
        L.Box(new Rect(0,357+o,width,112),GUIContent.none,_card);
        NativeRequiredIcon(new Rect(18,370+o,42,42),"Farm");
        L.Label(new Rect(76,372+o,width-424,36),"Автокормление загонов",_heading);
        Hint(new Rect(76,410+o,width-424,52),"Голодным слаймам в загонах — по порции из их рациона (сначала любимая) · до 300 м.");
        if(feed!=null&&Button(new Rect(width-330,384+o,312,48),feeding?"Автокорм: ВКЛ · отключить":"Автокорм: ВЫКЛ · включить…",feeding?_selectedButton:_button))
            feed.Request(!feeding);
        L.Box(new Rect(0,481+o,width,62),GUIContent.none,_card);
        L.Label(new Rect(18,486+o,width-36,26),tools.Status,_wrap);
        L.Label(new Rect(18,512+o,width-36,26),feed!=null?feed.Status:"",_muted);
        Hint(new Rect(0,551+o,width,42),"Изменяет инвентарь · включение с подтверждением.");
    }
    // A required native-only role never falls back to the schematic atlas.
    // Missing/unloaded assets stay visibly unavailable, with cached provenance.
    private void NativeRequiredIcon(Rect rect,string role)
    {
        var sprite=_app.Map.Icons.Role(role);
        if(sprite!=null)DrawSprite(rect,sprite);
        else L.Label(rect,new GUIContent(Loc.T("нет\nиконки"),_app.Map.Icons.RoleDiagnostic(role)),_small);
    }
    private void CustomMapCard(Rect rect,MapTools.Point point)
    {
        var tools=_app.Custom;
        L.Box(rect,GUIContent.none,_frame);
        NativeRequiredIcon(new Rect(rect.x+16,rect.y+14,36,36),"Compass");
        Fill(new Rect(rect.x+12,rect.y+16,3,34),MarkerColor(point.ColorIndex));
        L.Label(new Rect(rect.x+62,rect.y+8,rect.width-122,58),point.Name,_atlasDetail);
        if(Button(new Rect(rect.xMax-52,rect.y+8,44,44),"×",_button)){_app.Map.Selected=null;return;}
        L.Label(new Rect(rect.x+16,rect.y+74,rect.width-32,104),
            "Своя метка · "+MarkerColorNames[Mathf.Clamp(point.ColorIndex,0,5)]+"\n"+
            (point.ExactHeight?"Наземный маяк и компас":"Компас; высота земли проверится при переходе")+"\n"+
            "Телепорт — через безопасную прогрузку зоны.",_atlasDetail);
        if(Button(new Rect(rect.x+16,rect.y+190,rect.width-32,44),"Маяк / компас к этой метке",_selectedButton))
        {string id=point.Id;_app.Queue("custom map navigation",()=>{tools.SetGoal(id);_app.Map.CloseNative();});}
        bool enabled=GUI.enabled;GUI.enabled=enabled&&point.Group!=null&&!_app.Map.Busy;
        if(Button(new Rect(rect.x+16,rect.y+246,rect.width-32,44),"Безопасный телепорт",_accentButton))
        {var chosen=point;_app.Queue("custom map teleport",()=>_app.Map.RequestTeleport(chosen));}
        GUI.enabled=enabled;
        if(Button(new Rect(rect.x+16,rect.y+302,rect.width-32,44),"Изменить имя / цвет / удалить…",_button))
        {string id=point.Id;_app.Queue("custom marker settings",()=>{_app.Map.CloseNative();ShowExplorerForTest(1,id);_app.Input.OpenAfterNativeClose();});}
        Hint(new Rect(rect.x+16,rect.y+358,rect.width-32,56),"Метка хранится отдельно\nот игрового сейва.");
    }

    // ---- HUD: goal row, world beacon, radar row and world rings ----
    internal void DrawExplorerHud()
    {
        if(!_app.Ready||_app.MenuOpen||_app.Map.NativeMapOpen||_app.Map.Busy||_app.Cinema.HideHud||
            Time.timeScale<=0||Event.current?.type!=EventType.Repaint)return;
        bool goal=_app.Custom.ShowCompass,radar=_app.Radar.Enabled;
        if(!goal&&!radar)return;
        Styles();var matrix=GUI.matrix;var color=GUI.color;var content=GUI.contentColor;var background=GUI.backgroundColor;
        try
        {
            _scale=1;_groupOrigin=Vector2.zero;_groupOrigins.Clear();
            GUI.matrix=Matrix4x4.identity;GUI.color=GUI.contentColor=GUI.backgroundColor=Color.white;
            if(radar&&_app.Radar.Highlight)RadarWorldRings();
            float width=Mathf.Min(520,Screen.width-64),x=(Screen.width-width)/2,y=ExplorerPolicy.NavigationHudTop;
            if(goal)
            {
                ExplorerHudRow(new Rect(x,y,width,ExplorerPolicy.NavigationHudRowHeight),_app.Custom.Direction,_app.Custom.Heading,MarkerColor(_app.Custom.GoalColor));
                y+=ExplorerPolicy.NavigationHudStride;
                if(_app.Custom.BeaconVisible)
                {
                    var at=_app.Custom.BeaconScreen;
                    // Lightweight world-projected beacon: the genuine game's
                    // waypoint flag floats over a soft jelly halo. No hard
                    // screen-space light pole, extra HDRP light or saved actor.
                    Color tint=MarkerColor(_app.Custom.GoalColor),previous=GUI.color;
                    GUI.color=new Color(tint.r,tint.g,tint.b,.20f);
                    GUI.DrawTexture(new Rect(at.x-28,at.y-55,56,56),_badgeCircle);
                    GUI.color=new Color(1,1,1,.92f);
                    GUI.DrawTexture(new Rect(at.x-21,at.y-48,42,42),_badgeCircle);
                    GUI.color=previous;
                    var native=_app.Map.Icons.Category(CustomWaypointTools.Category);
                    if(native!=null)DrawSprite(new Rect(at.x-16,at.y-44,32,32),native);
                    GUI.color=new Color(tint.r,tint.g,tint.b,.45f);
                    GUI.DrawTexture(new Rect(at.x-6,at.y-5,12,12),_badgeCircle);
                    GUI.color=previous;
                }
            }
            if(radar)ExplorerHudRow(new Rect(x,y,width,ExplorerPolicy.NavigationHudRowHeight),_app.Radar.Direction,_app.Radar.Heading,_accent);
        }
        finally{GUI.matrix=matrix;GUI.color=color;GUI.contentColor=content;GUI.backgroundColor=background;}
    }
    // «Подсвечивать в мире»: a ring in the target colour over the nearest hits (screen space only).
    private void RadarWorldRings()
    {
        if(_ringCamera==null||_ringCamera.WasCollected||Time.unscaledTime>=_ringCameraAt)
        {_ringCamera=Camera.main;_ringCameraAt=Time.unscaledTime+1f;}
        if(_ringCamera==null)return;
        var hits=_app.Radar.Hits;int drawn=0;
        var saved=GUI.color;
        try
        {
            for(int i=0;i<hits.Count&&drawn<MapExplorerPolicy.MaxHighlights;i++)
            {
                var hit=hits[i];
                var screen=_ringCamera.WorldToScreenPoint(hit.Position+Vector3.up*.6f);
                if(!(screen.z>.5f))continue;
                float px=screen.x,py=Screen.height-screen.y;
                if(px<-40||px>Screen.width+40||py<-40||py>Screen.height+40)continue;
                float size=Mathf.Clamp(1100f/screen.z,24f,72f);
                Color c=MarkerColor(hit.Color);
                GUI.color=new Color(c.r,c.g,c.b,.16f);
                GUI.DrawTexture(new Rect(px-size*.62f,py-size*.62f,size*1.24f,size*1.24f),_badgeCircle);
                MapGlyph(new Rect(px-size/2,py-size/2,size,size),MapChromePolicy.Ring,new Color(c.r,c.g,c.b,.95f));
                drawn++;
            }
        }
        finally{GUI.color=saved;}
    }
    private void ExplorerHudRow(Rect rect,string direction,string text,Color stripe)
    {
        L.Box(rect,GUIContent.none,_hudFrame);Fill(new Rect(rect.x+8,rect.y+12,3,28),stripe);
        L.Label(new Rect(rect.x+18,rect.y+9,35,34),direction,_hudLabel);
        L.Label(new Rect(rect.x+57,rect.y+9,rect.width-73,34),text,_hudLabel);
    }
}
