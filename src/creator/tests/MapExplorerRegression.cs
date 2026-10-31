using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CreatorToolkit;

// 0.8.1 «Карта» pages (approved mockups v2 02-04, 07): «Мои метки», radar, navigation, HUD minimap.
internal static class MapExplorerRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool ok,string name)=>check(ok,"map explorer: "+name);
        string dir=Path.Combine(root,"src","creator");
        string R(string name){string p=Path.Combine(dir,name);return File.Exists(p)?File.ReadAllText(p):"";}
        // «Мои метки»: sorting, dates, header.
        var names=new[]{"Бета","альфа","Гамма"};var dist=new[]{40f,-1f,12f};var created=new long[]{100,0,300};
        C(MapExplorerPolicy.SortMarkers(names,dist,created,0).SequenceEqual(new[]{2,0,1}),"sort by distance, unknown last");
        C(MapExplorerPolicy.SortMarkers(names,dist,created,1).SequenceEqual(new[]{1,0,2}),"sort by name, case-insensitive");
        C(MapExplorerPolicy.SortMarkers(names,dist,created,2).SequenceEqual(new[]{2,0,1}),"newest first, undated last");
        C(MapExplorerPolicy.SortMarkers(null,null,null,0).Count==0&&MapExplorerPolicy.SortMarkers(new[]{"a","b"},null,null,0).SequenceEqual(new[]{0,1}),"empty and missing data keep stored order");
        C(MapExplorerPolicy.NextSort(0)==1&&MapExplorerPolicy.NextSort(2)==0&&MapExplorerPolicy.NextSort(99)==0&&MapExplorerPolicy.MarkerSorts.Length==3,"sort link cycles three modes");
        C(MapExplorerPolicy.CreatedText(0)==""&&MapExplorerPolicy.CreatedText(1700000000).StartsWith("создана ")&&MapExplorerPolicy.CreatedText(1700000000).Length=="создана dd.MM".Length,"creation date dd.MM");
        C(MapExplorerPolicy.ListHeader(3,50)=="3 из 50 · хранятся отдельно от сейва","list header");
        // Radar rules.
        C(MapExplorerPolicy.ClampRadius(0)==25&&MapExplorerPolicy.ClampRadius(999)==250&&MapExplorerPolicy.ClampRadius(102)==100&&MapExplorerPolicy.ClampRadius(float.NaN)==100,"radius 25-250 m, 5 m steps");
        C(MapExplorerPolicy.NextColor(new int[0])==0&&MapExplorerPolicy.NextColor(new[]{0,1,3})==2&&MapExplorerPolicy.NextColor(new[]{0,1,2,3,4,5,0})==1,"first free colour, then round robin");
        C(MapExplorerPolicy.CycleColor(5)==0&&MapExplorerPolicy.CycleColor(-1)==0&&MapExplorerPolicy.CycleColor(2)==3,"RMB cycles the colour");
        C(MapExplorerPolicy.RadarCategories.Length==7&&MapExplorerPolicy.RadarCategories[0]=="Всё"&&MapExplorerPolicy.RadarCategories.Skip(1).All(MapExplorerPolicy.RadarCategory)&&!MapExplorerPolicy.RadarCategory("Игрушки"),"category strip");
        C(MapExplorerPolicy.NearText(0)=="нет рядом"&&MapExplorerPolicy.NearText(3)=="3 рядом"&&MapExplorerPolicy.OnRadar(4)=="4 на радаре","near / on-radar texts");
        C(MapExplorerPolicy.RadarSub("Ресурсы",84,"")=="Ресурс · ближайший 84 м"&&MapExplorerPolicy.RadarSub("Еда",-1,"Хлопковый слайм")=="Еда · любимая: Хлопковый слайм"&&
          MapExplorerPolicy.RadarSub("Слаймы",-1,"")=="Слайм","tracked row subtitles");
        var config=new MapExplorerPolicy.RadarConfig{On=false,Radius=130,Highlight=false,Hud=true};
        config.Targets.Add(new MapExplorerPolicy.RadarTarget{Reference="PinkSlime",Color=2});
        config.Targets.Add(new MapExplorerPolicy.RadarTarget{Reference="Carrot",Color=4,Auto=true,Owner="CottonSlime"});
        config.Targets.Add(new MapExplorerPolicy.RadarTarget{Reference="PinkSlime",Color=1});
        config.Targets.Add(new MapExplorerPolicy.RadarTarget{Reference="bad ref",Color=1});
        var back=MapExplorerPolicy.ParseRadar(MapExplorerPolicy.SerializeRadar(config),out int rejected);
        C(rejected==0&&!back.On&&back.Radius==130&&!back.Highlight&&back.Hud&&back.Targets.Count==2&&back.Targets[1].Auto&&back.Targets[1].Owner=="CottonSlime"&&back.Targets[0].Color==2,
          "radar file round trip, duplicates and invalid references dropped");
        var junk=MapExplorerPolicy.ParseRadar("version=2\nradius=abc\ntarget=X|9|0\ntarget=Y|1\nnoise\nfoo=1\non=maybe\ntarget=Z|1|1|\n",out int bad);
        C(bad==7&&junk.Targets.Count==1&&junk.Targets[0].Reference=="Z"&&junk.On&&junk.Radius==100,"radar file: invalid lines are skipped, defaults kept");
        var many=new MapExplorerPolicy.RadarConfig();for(int i=0;i<40;i++)many.Targets.Add(new MapExplorerPolicy.RadarTarget{Reference="T"+i});
        C(MapExplorerPolicy.ParseRadar(MapExplorerPolicy.SerializeRadar(many),out _).Targets.Count==MapExplorerPolicy.MaxTargets,"at most 24 radar targets");
        // Navigation pictograms.
        C(Enumerable.Range(0,4).All(c=>MapExplorerPolicy.CornerAt(MapExplorerPolicy.CornerRight(c),MapExplorerPolicy.CornerBottom(c))==c),"corner picker maps every M29 corner");
        C(MapExplorerPolicy.Opacity(30)==70&&MapExplorerPolicy.TransparencyFor(70)==30&&MapExplorerPolicy.TransparencyFor(150)==0,"opacity is the inverse of transparency");
        C(MapExplorerPolicy.GhostTitles.Length==3&&MapExplorerPolicy.OrientationTitles.Length==2&&MapExplorerPolicy.SizeLetters.SequenceEqual(new[]{"S","M","L"}),"segment titles");
        C(MapExplorerPolicy.LayersChip("Сокровища",true)=="Значки = слои атласа: Сокровища + радар"&&!MapExplorerPolicy.LayersChip("x",false).Contains("радар"),"layers chip");
        // HUD minimap rim.
        C(!MapExplorerPolicy.Rim(3,4,10,out float rx,out float ry)&&rx==3&&ry==4,"inside the disc: unchanged");
        C(MapExplorerPolicy.Rim(30,40,10,out rx,out ry)&&Math.Abs(rx-6)<1e-3&&Math.Abs(ry-8)<1e-3,"outside: pinned to the rim on the same bearing");
        MapExplorerPolicy.ChipSide(100,0,40,out float cx,out float cy);
        C(cx+40<100&&Math.Abs(cy+11)<1e-3,"distance chip sits inside the rim pin");
        // Wiring.
        string explorer=R("ExplorerUi.cs"),radar=R("RadarTools.cs"),mini=R("MiniMapUi.cs"),custom=R("CustomWaypointTools.cs"),compass=R("M27CompassMarkers.cs");
        C(explorer.Contains("MarkersPage(")&&explorer.Contains("RadarPage(")&&explorer.Contains("NavigationPage(")&&R("AtlasUi.cs").Contains("ExplorerPage(area.width,area.height)"),"pages are routed from the atlas");
        C(explorer.Contains("\"Маяк в мире\"")&&explorer.Contains("\"Цель на компасе\"")&&custom.Contains("internal void SetBeacon(")&&custom.Contains("if(!Beacon)return;"),"marker beacon and compass-goal switches");
        C(radar.Contains("MapExplorerPolicy.RadarFilePrefix")&&radar.Contains("File.Move(temporary,_file,true)")&&radar.Contains("internal int AddCorralFavorites()"),"radar per-save file, atomic write, corral favourites");
        C(radar.Contains("if(!Enabled)return;")&&compass.Contains("radar.Enabled&&radar.ShowOnHud"),"radar off: no scan, no compass offers");
        C(mini.Contains("MapExplorerPolicy.MiniMapIcons")&&mini.Contains("DrawMiniMapPreview(")&&mini.Contains("MapExplorerPolicy.Rim("),"minimap: icon cap, preview and rim pins");
    }
}
