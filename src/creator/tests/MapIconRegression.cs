using System;
using System.IO;
namespace CreatorToolkit;
internal static class MapIconRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool v,string n)=>check(v,"m14 / map icons / "+n);
        // Distance text must never touch the culture system again: the runtime is
        // globalization-invariant and the old lookup killed the overlay frame.
        C(MapDistanceText.Format(-1f)==""&&MapDistanceText.Format(float.NaN)==""&&
          MapDistanceText.Format(float.PositiveInfinity)=="","unmeasurable distance stays empty");
        C(MapDistanceText.Format(0f)=="0 м"&&MapDistanceText.Format(999.4f)=="999 м","metres below one kilometre");
        C(MapDistanceText.Format(1000f)=="1,0 км"&&MapDistanceText.Format(1234f)=="1,2 км"&&
          MapDistanceText.Format(20000f)=="20,0 км","kilometres keep the russian decimal comma");
        var progress=File.ReadAllText(Path.Combine(root,"src/creator/MapProgress.cs"));
        C(!progress.Contains("GetCultureInfo"),"no culture lookup remains on the distance path");
        C(progress.Contains("MapDistanceText.Format"),"distance text is delegated to the pure policy");
        // Find kinds resolve to real game art instead of a baked atlas cell.
        C(MapRareIcons.Names("Лучистый слайм")[0]=="iconRadiantPedia_small","radiant slime finds use radiant art");
        C(MapRareIcons.Names("Статуя для плорта · мяуровый 2")[0]=="iconDecorCheerfulStatue_small","plort statues use statue art");
        C(MapRareIcons.Names("Теневые врата")[0]=="iconMapShadowPlortDoor"&&
          MapRareIcons.Names("Сборщик теневых плортов")[0]=="iconMapShadowPlortDoor","shadow gates keep their own art");
        C(MapRareIcons.Names("Врата плортов")[0]=="iconMapPlortDoor","plort gates use gate art");
        C(MapRareIcons.Names("Приёмник плортов ×60 7")[0]=="iconGadgetRefineryLink","plort receivers use receiver art");
        C(MapRareIcons.Names("Слайм удачи")[0]=="iconSlimeLucky_small"&&
          MapRareIcons.Names("Золотой слайм")[0]=="iconSlimeGold_small","rare slime finds use slime art");
        C(MapRareIcons.Names("Лучистая руда")[0]=="iconCraftRadiantOre_small"&&
          MapRareIcons.Names("Странный алмаз")[0]=="iconCraftStrangeDiamond_small","gather kinds use craft art");
        C(MapRareIcons.Names("Неизвестная находка").Length==0&&MapRareIcons.Names(null).Length==0&&
          MapRareIcons.Names("").Length==0,"unknown kinds borrow nothing");
        C(MapRareIcons.Names("Декор")[0]=="iconDecorSimpleFlag_small","decor finds use decor art");
        bool bounded=MapRareIcons.AllNames.Count<=24;
        foreach(var name in MapRareIcons.AllNames)
            bounded&=name.Length>4&&name.StartsWith("icon",StringComparison.Ordinal);
        C(bounded,"find-kind artwork stays inside the borrowed-asset budget");
        // Wiring: the resolver, the layer aliases and the drone rule.
        var icons=File.ReadAllText(Path.Combine(root,"src/creator/NativeIconTools.cs"));
        C(!icons.Contains("Category(\"Ноды карты\",\"iconMapDetectorIdle\"")&&
          !icons.Contains("Category(\"Ноды карты\",\"iconPediaHomeWorld\""),"map nodes borrow neither the atlas globe nor the detector flask");
        C(icons.Contains("Category(\"Горшочки\",")&&icons.Contains("Category(\"Спавнеры\","),
          "pots and spawners have real layer artwork");
        C(icons.Contains("MapRareIcons.Names(point.FilterKind)"),"find kinds resolve through the icon table");
        C(icons.Contains("NativeIconPolicy.GhostDrone(point.Id,point.Name)"),"phantom drones resolve their own artwork");
        C(icons.Contains("return Live(point.SourceIcon)?point.SourceIcon:Category(point.Category);"),
          "source observations fall back to layer artwork instead of nothing");
        C(NativeIconPolicy.ExactNames("GhostDrone").Count>0&&
          NativeIconPolicy.ExactNames("GhostDrone")[0]=="iconMapExplorerDrone","ghost drone role names the native explorer drone");
        C(NativeIconPolicy.GhostDrone("component-route|1","Фантом · награда не получена")&&
          NativeIconPolicy.GhostDrone("component-drone|7","Дрон-компонент · фантом")&&
          !NativeIconPolicy.GhostDrone("ResearchDroneController_1","Исследовательский дрон")&&
          !NativeIconPolicy.GhostDrone(null,null),"only phantom route drones use explorer art");
        // Plaque: the distance line is always drawn and always states metres.
        var ui=File.ReadAllText(Path.Combine(root,"src/creator/ToolkitUi.cs"));
        C(ui.Contains("if(hover.Length>0)")&&!ui.Contains("hoveredCapsule==null"),
          "the distance plaque is never suppressed");
        C(ui.Contains("расстояние неизвестно"),"unknown player position is stated instead of dropping the plaque");
        C(ui.Contains("!capsuleBounds.Overlaps(left)?left:"),"the plaque moves instead of disappearing behind a capsule card");
        C(!ui.Contains("\\nДо метки: "),"the distance is no longer a second tooltip line that can be cut off");
    }
}
