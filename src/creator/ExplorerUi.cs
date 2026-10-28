using System;
using System.Linq;
using UnityEngine;

namespace CreatorToolkit;

internal sealed partial class ToolkitUi
{
    private int _explorerSection,_customEditColor;
    private string _customSelection="",_customEditId="",_customEditName="";
    private Vector2 _customScroll,_radarScroll;
    private ActorTools.Entry _radarSelection;
    private static readonly string[] ExplorerSections={"Атлас","Мои метки","Радар","Ферма","Навигация"};
    private static readonly string[] MarkerColorNames={"Розовый","Бирюза","Золото","Лаванда","Лайм","Голубой"};
    private static readonly Color[] MarkerColors={
        new(.95f,.33f,.60f),new(.12f,.74f,.69f),new(1f,.69f,.16f),
        new(.65f,.47f,.95f),new(.46f,.77f,.23f),new(.26f,.64f,.96f)};
    internal static Color MarkerColor(int index)=>MarkerColors[Mathf.Clamp(index,0,MarkerColors.Length-1)];

    private void ResetExplorerUi()
    {
        _explorerSection=_customEditColor=0;_customSelection=_customEditId=_customEditName="";
        _customScroll=_radarScroll=Vector2.zero;_radarSelection=null;
    }
    internal void ShowExplorerForTest(int section,string id="")
    {
        ShowTabForTest(6);_explorerSection=Mathf.Clamp(section,0,ExplorerSections.Length-1);_customSelection=id;
    }
    internal bool ExplorerSectionVisibleForTest(int section)=>
        _app.MenuOpen&&_tab==6&&_explorerSection==section;
    private void ExplorerNavigation(float width)
    {
        int count=ExplorerSections.Length;float part=(width-8*(count-1))/count;
        for(int i=0;i<count;i++)
            if(NativeRoleButton(new Rect(i*(part+8),44,part,40),ExplorerSections[i],
                i switch{0=>"Atlas",1=>"MyMarkers",2=>"Radar",3=>"Farm",_=>"Compass"},_explorerSection==i))
            {_explorerSection=i;ClearInteraction();}
    }
    private void ExplorerPage(float width)
    {
        if(_explorerSection==1)CustomWaypointPage(width);
        else if(_explorerSection==2)RadarPage(width);
        else if(_explorerSection==4)NavigationPage(width);
        else FarmPage(width);
    }
    private int ColorChoices(Rect rect,int selected,string prefix)
    {
        float w=(rect.width-5*6)/6;
        for(int i=0;i<6;i++)
        {
            var r=new Rect(rect.x+i*(w+6),rect.y,w,rect.height);
            if(Button(r,(i+1).ToString(),i==selected?_selectedButton:_button))selected=i;
            bool hovered=InputEnabled&&r.Contains(Pointer());
            float offset=HoverOffset(r,hovered,hovered&&UnityEngine.InputSystem.Mouse.current!=null&&
                UnityEngine.InputSystem.Mouse.current.leftButton.isPressed);
            Fill(HoverChildRect(r,new Rect(r.x+12,r.yMax-6,r.width-24,3),offset),MarkerColor(i));
        }
        return selected;
    }
    private void CustomWaypointPage(float width)
    {
        var tools=_app.Custom;tools.EnsureLoaded();
        float left=(width-18)*.58f,x=left+18,right=width-x;
        GUI.Label(new Rect(0,100,width,34),"Свои цветные метки · "+tools.Records.Count+" / 32",_heading);
        tools.DraftName=TextBox("custom-new-name",new Rect(0,145,left,40),tools.DraftName,48,"Название новой метки…");
        tools.DraftColor=ColorChoices(new Rect(0,197,left,40),tools.DraftColor,"new");
        bool enabled=GUI.enabled;GUI.enabled=enabled&&tools.Records.Count<32;
        if(Button(new Rect(0,249,(left-8)/2,44),"Сохранить у игрока",_accentButton))
            _app.Queue("custom current position",tools.SaveCurrent);
        if(Button(new Rect((left+8)/2,249,(left-8)/2,44),"Поставить на карте",_button))
            _app.Queue("custom placement map",()=>{_app.Map.ClickTeleport=false;_app.Map.CustomPointMode=true;_app.Map.Open();});
        GUI.enabled=enabled;
        Rect view=new(0,309,left,230);GUI.Box(view,GUIContent.none,_card);
        _customScroll=BeginScroll(40,new Rect(10,319,left-20,210),_customScroll,tools.Records.Count*48);
        try
        {
            for(int i=0;i<tools.Records.Count;i++)
            {
                var record=tools.Records[i];float y=i*48-_customScroll.y;
                if(y+44<0||y>210)continue;
                Rect row=new(0,y,left-72,44);
                if(Button(row,record.Name,_customSelection==record.Id?_selectedButton:_button))_customSelection=record.Id;
                bool hovered=InputEnabled&&row.Contains(Pointer());
                float offset=HoverOffset(row,hovered,hovered&&UnityEngine.InputSystem.Mouse.current!=null&&
                    UnityEngine.InputSystem.Mouse.current.leftButton.isPressed);
                Fill(HoverChildRect(row,new Rect(row.x+5,row.y+10,4,24),offset),MarkerColor(record.Color));
            }
            if(tools.Records.Count==0)GUI.Label(new Rect(12,8,left-80,90),"Пока нет своих меток.\nСохраните позицию на земле или нажмите ЛКМ на карте.",_muted);
        }
        finally{EndGroup();}
        GUI.Box(new Rect(x,145,right,394),GUIContent.none,_card);
        var selected=tools.Find(_customSelection);
        if(selected==null)
        {GUI.Label(new Rect(x+18,165,right-36,100),"Выберите свою метку.\nИмя и цвет можно изменить; удаление требует подтверждения.",_muted);}
        else
        {
            if(_customEditId!=selected.Id)
            {_customEditId=selected.Id;_customEditName=selected.Name;_customEditColor=selected.Color;}
            _customEditName=TextBox("custom-edit-name",new Rect(x+16,161,right-32,40),_customEditName,48,"Название метки…");
            _customEditColor=ColorChoices(new Rect(x+16,209,right-32,40),_customEditColor,"edit");
            if(Button(new Rect(x+16,257,right-32,40),"Сохранить имя и цвет",_button))
            {
                string id=selected.Id,name=_customEditName;int color=_customEditColor;
                _app.Queue("custom rename",()=>tools.Rename(id,name,color));
            }
            var point=tools.FindPoint(selected.Id);
            GUI.Label(new Rect(x+16,305,right-32,48),point?.Group==null?"Зона этой метки сейчас недоступна. Телепорт отключён.":selected.ExactHeight?"Маяк + компас · проверенная наземная высота":"Компас · высота посадки будет проверена при телепорте",_muted);
            GUI.enabled=enabled&&point?.Group!=null;
            if(Button(new Rect(x+16,365,(right-40)/2,40),"На карте",_button))
            {var chosen=point;_app.Queue("show custom map marker",()=>_app.Map.OpenAt(chosen));}
            if(Button(new Rect(x+24+(right-40)/2,365,(right-40)/2,40),"Телепорт",_accentButton))
            {var chosen=point;_app.Queue("safe custom teleport",()=>_app.Map.RequestTeleport(chosen));}
            GUI.enabled=enabled;
            if(Button(new Rect(x+16,415,right-32,40),tools.ShowCompass?"Выбрать этот маяк / компас":"Маяк / компас к метке",_selectedButton))
            {string id=selected.Id;_app.Queue("custom navigation goal",()=>{tools.SetGoal(id);_app.CloseMenu();});}
            if(Button(new Rect(x+16,465,(right-40)/2,40),"Скрыть маяк",_button))tools.HideGoal();
            if(Button(new Rect(x+24+(right-40)/2,465,(right-40)/2,40),"Удалить…",_button))
            {
                string id=selected.Id;
                _app.Confirm("Удалить свою метку?","Будет удалена только выбранная пользовательская метка. Капсулы и другие объекты игры не затрагиваются.",()=>tools.Delete(id));
            }
        }
        GUI.Label(new Rect(0,551,width,42),tools.Status,_muted);
    }
    private void RadarPage(float width)
    {
        var tools=_app.Radar;float left=(width-18)*.55f,x=left+18,right=width-x;
        GUI.Label(new Rect(0,100,width,34),"Любимая еда и редкие материалы · до 100 м",_heading);
        var slimes=Filter("","Слаймы",false).Where(e=>!e.IsPrototype).ToList();
        if(_radarSelection==null)
            _radarSelection=slimes.FirstOrDefault(e=>e.Type.ReferenceId==tools.SlimeReference);
        Catalog(new Rect(0,145,left,394),slimes,ref _radarScroll,ref _radarSelection);
        if(_radarSelection!=null&&_radarSelection.Type.ReferenceId!=tools.SlimeReference)
        {var chosen=_radarSelection;_app.Queue("select radar slime",()=>tools.SetSlime(chosen));}
        GUI.Box(new Rect(x,145,right,394),GUIContent.none,_card);
        if(_radarSelection!=null)SpritePreview(new Rect(x+16,165,50,50),_radarSelection);
        GUI.Label(new Rect(x+80,165,right-96,45),tools.SlimeName,_wrap);
        GUI.Label(new Rect(x+16,226,right-32,72),"Любимая еда из нативной SlimeDiet:\n"+tools.FavoriteNames,_muted);
        if(Button(new Rect(x+16,310,right-32,44),tools.FavoriteFood?"Любимая еда: ВКЛ":"Любимая еда: ВЫКЛ",tools.FavoriteFood?_selectedButton:_button))
        {bool food=!tools.FavoriteFood,rare=tools.RareResources;_app.Queue("radar favorite food",()=>tools.SetModes(food,rare));}
        if(Button(new Rect(x+16,364,right-32,44),tools.RareResources?"Редкие материалы: ВКЛ":"Редкие материалы: ВЫКЛ",tools.RareResources?_selectedButton:_button))
        {bool food=tools.FavoriteFood,rare=!tools.RareResources;_app.Queue("radar rare materials",()=>tools.SetModes(food,rare));}
        // M27: nearby radar targets and enabled atlas layers as native markers on the top compass.
        var compass=_app.Compass;bool markers=compass!=null&&compass.Enabled;
        if(Button(new Rect(x+16,418,right-32,44),markers?"Метки на компасе: ВКЛ":"Метки на компасе: ВЫКЛ",markers?_selectedButton:_button))
        {bool on=!markers;_app.Queue("compass markers",()=>_app.Compass.SetEnabled(on));}
        // Top-aligned 16 px block below the compass button, inside the card (M27 live
        // test: three centred 17 px lines overflowed onto the button).
        GUI.Label(new Rect(x+16,468,right-32,68),tools.Status+"\n"+(compass!=null?compass.Status:""),_atlasDetail);
        if(DetailedHelp)GUI.Label(new Rect(0,550,width,46),"Ближайшая цель · обновление раз в секунду · только загруженная зона; скрытые залежи не угадываются.",_muted);
    }
    // M29 task 4: minimap and compass settings; every change is saved to navigation.cfg.
    private static readonly int[] CornerOrder={1,0,3,2};
    private static readonly string[] CornerArrows={"↖","↗","↙","↘"};
    private int NavigationChoice(Rect rect,string[] titles,int selected)
    {
        int clicked=-1;float w=(rect.width-8*(titles.Length-1))/titles.Length;
        for(int i=0;i<titles.Length;i++)
            if(Button(new Rect(rect.x+i*(w+8),rect.y,w,rect.height),titles[i],i==selected?_selectedButton:_button))clicked=i;
        return clicked;
    }
    private void NavigationPage(float width)
    {
        var nav=_app.Navigation;
        if(nav==null){GUI.Label(new Rect(0,100,width,34),"Настройки навигации недоступны",_heading);return;}
        var s=nav.Settings;
        float col=(width-18)/2,x=col+18,lx=16,lw=col-32,cx=lx+148,cw=lw-148;
        GUI.Label(new Rect(0,100,width,34),"Мини-карта и компас · сохраняется сразу",_heading);
        // Minimap card.
        GUI.Box(new Rect(0,145,col,394),GUIContent.none,_card);
        GUI.Label(new Rect(lx,153,lw,30),"Мини-карта",_wrap);
        GUI.Label(new Rect(lx,190,140,34),"Режим",_muted);
        int mode=NavigationChoice(new Rect(cx,190,cw,34),M29NavigationSettings.ModeTitles,s.Mode);
        if(mode>=0&&mode!=s.Mode)_app.Queue("minimap mode",()=>nav.SetMode(mode));
        GUI.Label(new Rect(lx,234,140,34),"Размер",_muted);
        int size=NavigationChoice(new Rect(cx,234,cw,34),M29NavigationSettings.SizeTitles,s.Size);
        if(size>=0)nav.Edit(n=>n.Size=size);
        GUI.Label(new Rect(lx,278,140,34),"Масштаб "+M29NavigationSettings.Metres(s.Range),_muted);
        if(Button(new Rect(cx,278,40,34),"−",_button))nav.ZoomIn();
        float t=M29NavigationSettings.RangeToSlider(s.Range);
        float slid=Slider(60,new Rect(cx+50,281,cw-100,28),t,0f,1f);
        if(Math.Abs(slid-t)>1e-4f)nav.SetRange(M29NavigationSettings.SliderToRange(slid));
        if(Button(new Rect(cx+cw-40,278,40,34),"+",_button))nav.ZoomOut();
        GUI.Label(new Rect(lx,322,140,34),"Угол",_muted);
        int corner=NavigationChoice(new Rect(cx,322,cw,34),CornerArrows,Array.IndexOf(CornerOrder,s.Corner));
        if(corner>=0)nav.Edit(n=>n.Corner=CornerOrder[corner]);
        GUI.Label(new Rect(lx,366,140,34),"Прозр. "+s.Transparency+" %",_muted);
        int clear=M29NavigationSettings.ClampTransparency(Mathf.RoundToInt(Slider(61,new Rect(cx,369,cw,28),s.Transparency,0,M29NavigationSettings.MaxTransparency)));
        if(clear!=s.Transparency)nav.Edit(n=>n.Transparency=clear,false);
        GUI.Label(new Rect(lx,410,140,34),"Ориентация",_muted);
        int north=NavigationChoice(new Rect(cx,410,cw,34),new[]{"По взгляду","Север сверху"},s.NorthUp?1:0);
        if(north>=0)nav.Edit(n=>n.NorthUp=north==1);
        var terrain=_app.MiniMapTerrain;
        GUI.Label(new Rect(lx,454,lw,80),M29NavigationSettings.ModeTitles[Mathf.Clamp(s.Mode,0,2)]+" · "+M29NavigationSettings.SizeTitles[Mathf.Clamp(s.Size,0,2)]+
            " · "+M29NavigationSettings.CornerTitles[Mathf.Clamp(s.Corner,0,3)]+" · фон "+(100-s.Transparency)+" %"+(s.NorthUp?" · север сверху":" · по взгляду")+
            (nav.Zooming?" · масштаб "+M29NavigationSettings.Metres(nav.Range):"")+"\n"+(s.Mode==1&&terrain!=null?terrain.Status:"Слои мини-карты — включённые слои атласа."),_atlasDetail);
        // Compass card.
        float rx=x+16,rcx=rx+148;
        GUI.Box(new Rect(x,145,col,394),GUIContent.none,_card);
        GUI.Label(new Rect(rx,153,lw,30),"Компас",_wrap);
        var compass=_app.Compass;bool markers=compass!=null&&compass.Enabled;
        if(Button(new Rect(rx,190,lw,34),markers?"Метки на компасе: ВКЛ":"Метки на компасе: ВЫКЛ",markers?_selectedButton:_button))
        {bool on=!markers;_app.Queue("compass markers",()=>nav.SetCompass(on));}
        GUI.Label(new Rect(rx,234,140,34),"Дальн. "+s.CompassRange+" м",_muted);
        int reach=M29NavigationSettings.ClampCompassRange(Mathf.RoundToInt(Slider(62,new Rect(rcx,237,cw,28),s.CompassRange,M29NavigationSettings.MinCompassRange,M29NavigationSettings.MaxCompassRange)));
        if(reach!=s.CompassRange)nav.Edit(n=>n.CompassRange=reach,false);
        GUI.Label(new Rect(rx,278,140,34),"Меток",_muted);
        if(Button(new Rect(rcx,278,40,34),"−",_button))nav.Edit(n=>n.CompassMarkers--);
        GUI.Label(new Rect(rcx+50,278,cw-100,34),s.CompassMarkers+" из "+M29NavigationSettings.MaxCompassMarkers,_badge);
        if(Button(new Rect(rcx+cw-40,278,40,34),"+",_button))nav.Edit(n=>n.CompassMarkers++);
        GUI.Label(new Rect(rx,322,140,34),"Фантомы",_muted);
        int ghosts=NavigationChoice(new Rect(rcx,322,cw,34),M29NavigationSettings.GhostTitles,s.Ghosts);
        if(ghosts>=0)nav.Edit(n=>n.Ghosts=ghosts);
        GUI.Label(new Rect(rx,362,lw,44),s.Ghosts==2?"Ближайший фантомный дрон зоны с наградой — всегда, без предела дальности.":
            s.Ghosts==1?"Фантомные дроны раньше других слоёв, как свои метки.":"Фантомные дроны — наравне с другими дронами.",_muted);
        if(Button(new Rect(rx,410,lw,34),"Сбросить вид и пределы",_button))_app.Queue("navigation reset",nav.ResetLook);
        GUI.Label(new Rect(rx,450,lw,86),(compass!=null?compass.Status:"Компас недоступен")+"\nСначала по одной метке на каждый слой.",_atlasDetail);
        GUI.Label(new Rect(0,550,width,46),nav.FilePath+"\n"+nav.Status,_muted);
    }
    private void FarmPage(float width)
    {
        var tools=_app.Farm;float col=(width-18)/2,x=col+18;
        GUI.Label(new Rect(0,100,width,34),"Фермерский помощник · установленные устройства",_heading);
        GUI.Box(new Rect(0,145,col,200),GUIContent.none,_card);
        NativeRequiredIcon(new Rect(18,162,42,42),"PinkPlort");
        GUI.Label(new Rect(76,164,col-92,40),"Сбор плортов",_heading);
        GUI.Label(new Rect(18,219,col-36,54),"Купленный сборщик загона · до 30 м.",_muted);
        if(Button(new Rect(18,283,col-36,48),tools.Collect?"Автосбор: ВКЛ · отключить":"Автосбор: ВЫКЛ · включить…",tools.Collect?_selectedButton:_button))
            tools.Request(!tools.Collect,tools.Feed);
        GUI.Box(new Rect(x,145,col,200),GUIContent.none,_card);
        NativeRequiredIcon(new Rect(x+18,162,42,42),"Feeder");
        GUI.Label(new Rect(x+76,164,col-92,40),"Пополнение кормушек",_heading);
        GUI.Label(new Rect(x+18,219,col-36,54),"Из выбранного слота в кормушку · до 4 шт./с.",_muted);
        if(Button(new Rect(x+18,283,col-36,48),tools.Feed?"Кормушки: ВКЛ · отключить":"Кормушки: ВЫКЛ · включить…",tools.Feed?_selectedButton:_button))
            tools.Request(tools.Collect,!tools.Feed);
        // M27: corral autofeed - hungry corral slimes get one portion of their own diet.
        var feed=_app.AutoFeed;bool feeding=feed!=null&&feed.Enabled;
        GUI.Box(new Rect(0,357,width,112),GUIContent.none,_card);
        NativeRequiredIcon(new Rect(18,370,42,42),"Farm");
        GUI.Label(new Rect(76,372,width-424,36),"Автокормление загонов",_heading);
        GUI.Label(new Rect(76,410,width-424,52),"Голодным слаймам в загонах — по порции из их рациона (сначала любимая) · до 300 м.",_muted);
        if(feed!=null&&Button(new Rect(width-330,384,312,48),feeding?"Автокорм: ВКЛ · отключить":"Автокорм: ВЫКЛ · включить…",feeding?_selectedButton:_button))
            feed.Request(!feeding);
        GUI.Box(new Rect(0,481,width,62),GUIContent.none,_card);
        GUI.Label(new Rect(18,486,width-36,26),tools.Status,_wrap);
        GUI.Label(new Rect(18,512,width-36,26),feed!=null?feed.Status:"",_muted);
        GUI.Label(new Rect(0,551,width,42),"Изменяет инвентарь · включение с подтверждением.",_muted);
    }
    // A required native-only role never falls back to the schematic atlas.
    // Missing/unloaded assets stay visibly unavailable, with cached provenance.
    private void NativeRequiredIcon(Rect rect,string role)
    {
        var sprite=_app.Map.Icons.Role(role);
        if(sprite!=null)DrawSprite(rect,sprite);
        else GUI.Label(rect,new GUIContent("нет\nиконки",_app.Map.Icons.RoleDiagnostic(role)),_small);
    }
    private void CustomMapCard(Rect rect,MapTools.Point point)
    {
        var tools=_app.Custom;
        GUI.Box(rect,GUIContent.none,_frame);
        NativeRequiredIcon(new Rect(rect.x+16,rect.y+14,36,36),"Compass");
        Fill(new Rect(rect.x+12,rect.y+16,3,34),MarkerColor(point.ColorIndex));
        GUI.Label(new Rect(rect.x+62,rect.y+8,rect.width-122,58),point.Name,_atlasDetail);
        if(Button(new Rect(rect.xMax-52,rect.y+8,44,44),"×",_button)){_app.Map.Selected=null;return;}
        GUI.Label(new Rect(rect.x+16,rect.y+74,rect.width-32,104),
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
        GUI.Label(new Rect(rect.x+16,rect.y+358,rect.width-32,56),"Метка хранится отдельно\nот игрового сейва.",_muted);
    }
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
    private void ExplorerHudRow(Rect rect,string direction,string text,Color stripe)
    {
        GUI.Box(rect,GUIContent.none,_hudFrame);Fill(new Rect(rect.x+8,rect.y+12,3,28),stripe);
        GUI.Label(new Rect(rect.x+18,rect.y+9,35,34),direction,_hudLabel);
        GUI.Label(new Rect(rect.x+57,rect.y+9,rect.width-73,34),text,_hudLabel);
    }
}