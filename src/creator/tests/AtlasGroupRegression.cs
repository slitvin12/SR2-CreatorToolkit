using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CreatorToolkit;

// 0.8.1 atlas page (approved mockup v2): groups, chips and their layer semantics.
internal static class AtlasGroupRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool ok,string name)=>check(ok,"atlas groups: "+name);
        string dir=Path.Combine(root,"src","creator");
        string R(string name){string p=Path.Combine(dir,name);return File.Exists(p)?File.ReadAllText(p):"";}
        var groups=AtlasGroupPolicy.Groups;
        C(groups.Select(g=>g.Title).SequenceEqual(new[]{"Сокровища","Пути","Ресурсы","Еда","Слаймы","Метки игры","Мои метки"}),"seven groups in mockup order");
        var ids=groups.SelectMany(g=>g.Chips).Select(c=>c.Id).ToList();
        C(ids.Count==ids.Distinct().Count()&&ids.All(id=>AtlasGroupPolicy.Find(id)?.Id==id),"chip ids are unique and indexed");
        C(groups.SelectMany(g=>g.Chips).All(c=>c.Title.Length>0&&c.One.Length>0&&c.Layer.Length>0),"every chip has a title, a singular and a layer");
        // Every stored layer (and «Мои метки») is reachable through at least one chip.
        string[] layers={"Капсулы","Гордо","Телепорты","Ноды карты","Дроны","Ресурсы","Двери","Механизмы","Горшочки","Спавнеры","Ресурсы: ценные","Редкое","Еда","Плорты"};
        C(R("MapTools.cs").Contains("internal static readonly string[] Categories={\"Капсулы\",\"Гордо\",\"Телепорты\",\"Ноды карты\",\"Дроны\",\"Ресурсы\",\"Двери\",\"Механизмы\",\"Горшочки\",\"Спавнеры\",FandomMapMarkers.GatherCategory,FandomMapMarkers.RareCategory,MapResourceLayerPolicy.Food,MapResourceLayerPolicy.Plorts};"),
          "the layer list under test matches MapTools.Categories");
        C(layers.All(l=>AtlasGroupPolicy.Siblings(l).Length>0)&&AtlasGroupPolicy.Siblings(AtlasGroupPolicy.Own).Length==1,"every map layer and own markers have a chip");
        C(layers.Where(l=>l!="Дроны"&&l!="Редкое").All(l=>!AtlasGroupPolicy.Shared(l)&&AtlasGroupPolicy.ChipOf(l,"",false)==AtlasGroupPolicy.Siblings(l)[0].Id),
          "plain layers map to their single chip");
        C(AtlasGroupPolicy.ChipOf("Дроны","",false)=="drones"&&AtlasGroupPolicy.ChipOf("Дроны","",true)=="ghosts","drones split into research and ghostly");
        C(AtlasGroupPolicy.RareChip("Статуя для плорта · мяуровый 2")=="statues"&&AtlasGroupPolicy.RareChip("Врата плортов 3")=="gates"&&
          AtlasGroupPolicy.RareChip("Теневые врата")=="gates"&&AtlasGroupPolicy.RareChip("Приёмник плортов ×60 7")=="receivers"&&
          AtlasGroupPolicy.RareChip("Сборщик теневых плортов")=="receivers"&&AtlasGroupPolicy.RareChip("Лучистый слайм · розовый")=="radiant"&&
          AtlasGroupPolicy.RareChip("Золотой слайм")=="rareslimes"&&AtlasGroupPolicy.RareChip("Слайм удачи")=="rareslimes"&&
          AtlasGroupPolicy.RareChip("Неизвестная находка")=="decor"&&AtlasGroupPolicy.RareChip(null)=="decor","every rare kind lands on one chip");
        C(AtlasGroupPolicy.ChipOf("Неизвестный слой","",false)==""&&AtlasGroupPolicy.ChipOf(null,null,false)=="","unknown layers have no chip");
        C(AtlasGroupPolicy.Order("pods")<AtlasGroupPolicy.Order("teleports")&&AtlasGroupPolicy.Order("teleports")<AtlasGroupPolicy.Order("own")&&
          AtlasGroupPolicy.Order("nope")==int.MaxValue&&AtlasGroupPolicy.GroupOf("food")==3&&AtlasGroupPolicy.GroupOf("nope")==-1,"list order follows groups");
        // Toggle semantics on a shared layer.
        var on=new HashSet<string>();var hidden=new HashSet<string>();
        var statues=AtlasGroupPolicy.Find("statues");var gates=AtlasGroupPolicy.Find("gates");
        AtlasGroupPolicy.Toggle(statues,on,hidden);
        C(on.Contains("Редкое")&&AtlasGroupPolicy.Active(statues,on,hidden)&&!AtlasGroupPolicy.Active(gates,on,hidden)&&
          AtlasGroupPolicy.Visible("Редкое","statues",on,hidden)&&!AtlasGroupPolicy.Visible("Редкое","decor",on,hidden),"one chip switches a shared layer on for itself only");
        AtlasGroupPolicy.Toggle(gates,on,hidden);
        C(AtlasGroupPolicy.Active(gates,on,hidden)&&AtlasGroupPolicy.Active(statues,on,hidden),"a sibling joins without touching the others");
        AtlasGroupPolicy.Toggle(statues,on,hidden);AtlasGroupPolicy.Toggle(gates,on,hidden);
        C(!on.Contains("Редкое")&&hidden.Count==0,"the last sibling off turns the layer off and forgets hidden marks");
        var food=AtlasGroupPolicy.Find("food");
        AtlasGroupPolicy.Toggle(food,on,hidden);bool foodOn=on.Contains("Еда");AtlasGroupPolicy.Toggle(food,on,hidden);
        C(foodOn&&!on.Contains("Еда")&&hidden.Count==0,"a plain chip is its layer switch");
        AtlasGroupPolicy.Toggle(statues,on,hidden);AtlasGroupPolicy.SetLayer("Редкое",true,on,hidden);
        C(on.Contains("Редкое")&&hidden.Count==0&&AtlasGroupPolicy.Active(gates,on,hidden),"the native panel switches whole layers with every sibling");
        AtlasGroupPolicy.SetLayer("Редкое",false,on,hidden);
        C(!on.Contains("Редкое")&&!AtlasGroupPolicy.Visible("Редкое","",on,hidden),"layer off hides every chip");
        // Texts.
        C(AtlasGroupPolicy.PodTitle(new[]{"Странный алмаз","Плорт"},"Капсула")=="Странный алмаз и ещё 1"&&
          AtlasGroupPolicy.PodTitle(new[]{"Странный алмаз"},"Капсула")=="Странный алмаз"&&AtlasGroupPolicy.PodTitle(null,"Капсула")=="Капсула","pod rows are named by their contents");
        C(AtlasGroupPolicy.PodKind(new[]{"Предмет","Чертёж"})=="чертёж"&&AtlasGroupPolicy.PodKind(new[]{"Предмет"})=="ресурсы"&&
          AtlasGroupPolicy.PodKind(new[]{"Компонент улучшения"})=="компонент улучшения"&&AtlasGroupPolicy.PodKind(null)=="","pod kind line");
        C(AtlasGroupPolicy.Items(1)=="1 предмет"&&AtlasGroupPolicy.Items(3)=="3 предмета"&&AtlasGroupPolicy.Items(5)=="5 предметов"&&
          AtlasGroupPolicy.Items(11)=="11 предметов"&&AtlasGroupPolicy.Items(21)=="21 предмет"&&AtlasGroupPolicy.Items(112)=="112 предметов","russian plural for items");
        // Carousels.
        C(AtlasGroupPolicy.CarouselClamp(-5,500,300)==0&&AtlasGroupPolicy.CarouselClamp(900,500,300)==200&&AtlasGroupPolicy.CarouselClamp(50,200,300)==0&&
          AtlasGroupPolicy.CarouselClamp(float.NaN,500,300)==0,"carousel offset stays inside the content");
        C(AtlasGroupPolicy.CarouselStep(0,1,1000,300)==180&&AtlasGroupPolicy.CarouselStep(180,-1,1000,300)==0&&AtlasGroupPolicy.CarouselStep(0,1,1000,100)==80,"arrow steps move most of a viewport");
        C(AtlasGroupPolicy.CarouselReveal(0,400,100,1000,300)==200&&AtlasGroupPolicy.CarouselReveal(300,100,50,1000,300)==100&&
          AtlasGroupPolicy.CarouselReveal(100,150,50,1000,300)==100,"the selected pill is revealed with the smallest move");
        // Statue art and translations.
        C(MapRareIcons.Names("Статуя для плорта · бум 4")[0]=="iconDecorGoldenStatueBoom_small"&&MapRareIcons.StatuePlort("Статуя для плорта · бум 4")=="iconPlortBoom_small"&&
          MapRareIcons.Names("Статуя для плорта · теневой")[0]=="iconPlortShadow_small","every statue species has its own art");
        var loc=R("LocEnglish.tsv").Split('\n').Select(l=>l.TrimEnd('\r')).Where(l=>l.Length>0&&l[0]!='#').Select(l=>l.Split('\t')[0]).ToList();
        C(loc.Count==loc.Distinct().Count(),"the english table has unique keys");
        var keys=new HashSet<string>(loc);
        C(groups.All(g=>keys.Contains(g.Title))&&groups.SelectMany(g=>g.Chips).All(c=>keys.Contains(c.Title)&&keys.Contains(c.One)),"group and chip titles are translated");
        var atlas=R("AtlasUi.cs");
        C(atlas.Contains("map.ToggleChip(chip)")&&atlas.Contains("AtlasGroupPolicy.Groups[_atlasGroup].Chips")&&atlas.Contains("_app.Compass?.Enabled!=true"),"wiring: chips toggle layers, the compass target checks the compass");
        C(R("MiniMapUi.cs").Contains("LayerVisible")&&R("M27CompassMarkers.cs").Contains("LayerVisible"),"wiring: minimap and compass share the atlas visibility rule");
        MapChrome(C,R,groups);
    }

    // 0.8.1 native map (mockups 05/06): toolbar, «Слои карты», presets, legend, plaque, fog hiding.
    private static void MapChrome(Action<bool,string> C,Func<string,string> R,AtlasGroupPolicy.Group[] groups)
    {
        var presets=AtlasGroupPolicy.Presets;
        C(presets.Select(p=>p.Title).SequenceEqual(new[]{"Минимум","Сокровища","Ресурсы","Всё"})&&
          presets.SelectMany(p=>p.Chips).All(id=>AtlasGroupPolicy.Find(id)!=null),"map: four presets over existing chips");
        var layers=new HashSet<string>();var hidden=new HashSet<string>();bool presetOk=true;
        for(int i=0;i<presets.Length;i++)
        {AtlasGroupPolicy.ApplyPreset(presets[i],layers,hidden);presetOk&=AtlasGroupPolicy.MatchingPreset(layers,hidden)==i;}
        C(presetOk,"map: every preset applies and is recognised again");
        var treasure=groups[0];
        AtlasGroupPolicy.SetGroup(treasure,false,layers,hidden);
        C(AtlasGroupPolicy.ActiveCount(treasure,layers,hidden)==0&&AtlasGroupPolicy.MatchingPreset(layers,hidden)==-1&&
          groups.Skip(1).All(g=>AtlasGroupPolicy.ActiveCount(g,layers,hidden)==g.Chips.Length),"map: a group switch turns off only its own chips");
        AtlasGroupPolicy.SetGroup(treasure,true,layers,hidden);
        C(AtlasGroupPolicy.MatchingPreset(layers,hidden)==3,"map: switching the group back restores «Всё»");
        var bar=MapChromePolicy.Toolbar(1920);var line=MapChromePolicy.Line(1920);var drop=MapChromePolicy.Layers(1920,1080);
        C(bar.X==1300&&bar.Y==20&&bar.Width==600&&bar.Height==60&&line.Y>=bar.Bottom&&line.X==bar.X&&line.Width==bar.Width&&
          drop.Right==bar.Right&&drop.Y==88&&drop.Bottom==1080-MapChromePolicy.BottomReserve,"map: toolbar, line and drop-down stack at the right edge");
        C(MapChromePolicy.RoundX(3)+MapChromePolicy.Round==MapChromePolicy.ToolbarWidth-12&&
          MapChromePolicy.RoundX(1)-MapChromePolicy.RoundX(0)==MapChromePolicy.Round+MapChromePolicy.RoundGap&&
          MapChromePolicy.GroupWidth>=200,"map: four round buttons right of a wide layers pill");
        C(MapChromePolicy.Layers(1024,600).Height>=MapChromePolicy.MinLayersHeight&&
          MapChromePolicy.Bounds(1920,1080,false).Bottom==line.Bottom&&MapChromePolicy.Bounds(1920,1080,true).Bottom==drop.Bottom,
          "map: the drop-down keeps a usable height; bounds cover what is drawn");
        C(MapChromePolicy.Sizes.All(v=>v>=MapOverlayLayout.MinSize&&v<=MapOverlayLayout.MaxSize)&&MapChromePolicy.SizeIndex(14)==0&&
          MapChromePolicy.SizeIndex(22)==1&&MapChromePolicy.SizeIndex(32)==2&&MapChromePolicy.ClickMode(true,true)==1&&
          MapChromePolicy.ClickMode(false,true)==2&&MapChromePolicy.ClickMode(false,false)==0,"map: icon size S/M/L and the left-click mode");
        C(MapChromePolicy.Legend(12,true,true,23)=="На карте: 12 · открытое скрыто · в тумане скрыто: 23"&&
          MapChromePolicy.Legend(3,false,true,0)=="На карте: 3"&&MapChromePolicy.Legend(-1,false,false,5)=="На карте: 0","map: legend under the toolbar");
        C(MapChromePolicy.Glyph(MapChromePolicy.Target,.5f,.5f,64)==1f&&MapChromePolicy.Glyph(MapChromePolicy.Target,.02f,.02f,64)==0f&&
          MapChromePolicy.Glyph(MapChromePolicy.Search,.42f,.17f,64)>.5f&&MapChromePolicy.Glyph(MapChromePolicy.Gear,.5f,.5f,64)==0f&&
          MapChromePolicy.Glyph(99,.5f,.5f,64)==0f,"map: toolbar glyphs from distance fields");
        var free=MapChromePolicy.TipPlace(800,500,300,56,1920,1080,new List<MapShieldPolicy.Box>());
        var card=new MapShieldPolicy.Box(830,400,420,414);
        var moved=MapChromePolicy.TipPlace(800,500,300,56,1920,1080,new[]{card});
        var edge=MapChromePolicy.TipPlace(1900,1070,300,56,1920,1080,null);
        C(free.X==822&&free.Y==472&&M29MapCardPolicy.Overlap(moved,card)==0&&moved.Right<=800&&edge.Right<=1904&&
          edge.Bottom<=1080-MapChromePolicy.TipBottom&&float.IsFinite(MapChromePolicy.TipPlace(0,0,500,56,200,100,null).X),
          "map: the hover plaque sits beside its marker, clear of the card and inside the map");
        var chrome=new[]{new MapShieldPolicy.Box(0,0,720,112),new MapShieldPolicy.Box(0,120,260,100),new MapShieldPolicy.Box(0,680,116,320),bar,line};
        var closed=M29MapCardPolicy.Place(1920,1080,new MapShieldPolicy.Box(1920,20,0,0),null,chrome,null);
        var open=M29MapCardPolicy.Place(1920,1080,drop,null,chrome.Take(4).ToArray(),null);
        C(closed.Clear&&closed.Card.Right<=1920&&closed.Card.Y>=line.Bottom&&open.Clear&&open.Card.Right<=drop.X,
          "map: the card sits under the toolbar line, or beside the open drop-down");
        string ui=R("ToolkitUi.cs"),chromeUi=R("MapChromeUi.cs"),map=R("MapTools.cs"),fog=R("MapFog.cs"),atlasUi=R("AtlasUi.cs");
        C(ui.Contains("MapToolbar(toolbar,map);")&&ui.Contains("MapLayersPanel(layersRect,map);")&&ui.Contains("Rect used=MapLine(lineFull,map,shown,repaint);")&&
          ui.Contains("map.OverlayCardRects.Add(new Rect(toolbar.x*_scale"),"wiring: the toolbar, the drop-down and the line draw last and are shielded");
        C(chromeUi.Contains("OpenMenuFromMap(\"atlas search from map\",0,true)")&&atlasUi.Contains("FocusTextInput(\"atlas-search\",map.Search??\"\",80)")&&
          chromeUi.Contains("map.ApplyPreset(chosen)")&&chromeUi.Contains("map.SetGroup(group,active==0)"),"wiring: search opens the focused atlas field; presets and group switches");
        C(map.Contains("&&!FogHides(p,ref fogged)")&&map.Contains("if(!HideFogged||p==Selected||!Fog.Fogged(p))return false;")&&map.Contains("Fog.Tick(this);")&&
          fog.Contains("_canvasGroup")&&fog.Contains("view.GetMapPosition("),"wiring: markers under the native fog are hidden, the selected one stays");
        C(chromeUi.Contains("else MapCardActions(")||ui.Contains("else MapCardActions(new Rect(rect.x+12,rect.yMax-54,rect.width-24,42),point);"),
          "wiring: map cards carry «Цель / Телепорт / Отметить»");
    }
}
