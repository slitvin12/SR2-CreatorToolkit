using System;
using System.IO;
using System.Linq;
namespace CreatorToolkit;
// Owner request 2026-10-26: crafting resources (jellystone, royal jelly ...) are
// their own atlas layer, separate from food and from plort spawn areas.
internal static class M23ResourceLayerRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool v,string n)=>check(v,"m23 / resource layers / "+n);
        const string M=MapResourceLayerPolicy.Materials,F=MapResourceLayerPolicy.Food,P=MapResourceLayerPolicy.Plorts;
        C(M=="Ресурсы"&&F=="Еда"&&P=="Плорты"&&MapResourceLayerPolicy.Layers.SequenceEqual(new[]{M,F,P}),"three layers with stable Russian names");
        // Every type found in the installed game's DiscoverableResources records.
        string[] craft={"JellystoneCraft","RoyalJellyCraft","RadiantOreCraft","SilkySandCraft","BuzzWaxCraft","WildHoneyCraft","DeepBrineCraft",
            "TinPetalCraft","BlackIndigoniumCraft","StrangeDiamondCraft","DreamBubbleCraft","PerfectSnowflakeCraft","AquaGlassCraft",
            "PrimordyOilCraft","SlimeFossilCraft","SunSapCraft","MagmaCombCraft","LavaDustCraft"};
        C(craft.All(n=>MapResourceLayerPolicy.Classify("IdentifiableType."+n)==M&&MapResourceLayerPolicy.Classify(n,"Еда")==M),
          "jellystone, royal jelly and every crafting material stay in «Ресурсы»");
        string[] food={"CarrotVeggie","OnionVeggie","LettuceVeggie","BeetVeggie","TaterVeggie","PogoFruit","CuberryFruit","PomegraniteFruit",
            "MangoFruit","PearFruit","PolaricherryFruit","Hen","Chick","Rooster","SeaHen","SeaChick","StonyHen","StonyChick","BriarHen",
            "BriarChick","CandiedHen","CandiedChick","PaintedHen","PaintedChick","ThunderHen","ThunderChick","MoondewNectar"};
        C(food.All(n=>MapResourceLayerPolicy.Classify("IdentifiableType."+n)==F&&MapResourceLayerPolicy.Classify(n,"Ресурсы")==F),
          "fruit, veggies, hens, chicks and nectar move to «Еда»");
        string[] plorts={"PinkPlort","GoldPlort","YolkyPlort","ShadowPlort","PhosphorPlort","CottonPlort","AnglerPlort","BattyPlort",
            "RingtailPlort","TwinPlort","SloomberPlort","HunterPlort","SaberPlort","TanglePlort","DervishPlort"};
        C(plorts.All(n=>MapResourceLayerPolicy.Classify("IdentifiableType."+n)==P),"slime spawn plort lists move to «Плорты»");
        C(MapResourceLayerPolicy.Classify("IdentifiableType.FutureSnack","Еда")==F&&MapResourceLayerPolicy.Classify("IdentifiableType.FutureGoo","Плорты")==P&&
          MapResourceLayerPolicy.Classify("IdentifiableType.FutureThing")==M&&MapResourceLayerPolicy.Classify("IdentifiableType.FutureThing","Слаймы")==M,
          "a future type without a known suffix follows the live catalog, else stays in «Ресурсы»");
        C(MapResourceLayerPolicy.Classify("")==null&&MapResourceLayerPolicy.Classify(null)==null&&MapResourceLayerPolicy.Classify("  ","Еда")==null,
          "records without a type are skipped instead of drawing a nameless marker");
        C(MapResourceLayerPolicy.TypeName("IdentifiableType.RoyalJellyCraft")=="RoyalJellyCraft"&&MapResourceLayerPolicy.TypeName("PinkPlort")=="PinkPlort"&&
          MapResourceLayerPolicy.TypeName(null)=="","type names lose only the reference prefix");
        C(MapResourceLayerPolicy.Owns(M)&&MapResourceLayerPolicy.Owns(F)&&MapResourceLayerPolicy.Owns(P)&&!MapResourceLayerPolicy.Owns(MapLayerOrder.FandomGather)&&
          !MapResourceLayerPolicy.Owns("Капсулы")&&!MapResourceLayerPolicy.Owns(null),"only the three spawn layers resolve per-item icons");
        C(MapResourceLayerPolicy.Detail(M).Contains("ресурса")&&MapResourceLayerPolicy.Detail(F).Contains("еды")&&MapResourceLayerPolicy.Detail(P).Contains("плорт")&&
          MapResourceLayerPolicy.Detail(M)=="Место возможного появления ресурса, не подтверждённый живой предмет.",
          "each layer explains its marker and crafting keeps the original text");
        // Layer order, the compact native-map panel and the atlas filter pills.
        string[] categories={"Капсулы","Гордо","Телепорты","Ноды карты","Дроны",M,"Двери","Механизмы","Горшочки","Спавнеры",
            MapLayerOrder.FandomGather,MapLayerOrder.FandomRare,F,P};
        var order=MapLayerOrder.Sorted(categories);
        C(order.Length==14&&order.Skip(MapLayerOrder.Objects).SequenceEqual(new[]{"Горшочки","Спавнеры",M,F,P,MapLayerOrder.FandomGather,MapLayerOrder.FandomRare}),
          "food and plorts sit right after the crafting layer");
        C(MapLayerOrder.PanelControlsTop(12)==504f&&MapLayerOrder.PanelHeight(12)==780f,"twelve layers keep the original panel geometry");
        C(MapLayerOrder.PanelControlsTop(14)==544f&&MapLayerOrder.PanelHeight(14)==820f,"two more layers add exactly one grid row and the panel grows with it");
        C(MapLayerOrder.FilterColumns(12)==6&&MapLayerOrder.FilterColumns(14)==7&&Math.Abs(MapLayerOrder.FilterWidth(1100f,12)-(1100f-40f)/6f)<.001f&&
          (14+MapLayerOrder.FilterColumns(14)-1)/MapLayerOrder.FilterColumns(14)==2&&MapLayerOrder.FilterWidth(1100f,14)>140f,
          "atlas filter pills stay in two rows above the list controls");
        // Wiring.
        string Read(string n)=>File.ReadAllText(Path.Combine(root,"src","creator",n));
        string map=Read("MapTools.cs"),icons=Read("NativeIconTools.cs"),ui=Read("ToolkitUi.cs"),order2=Read("MapLayerOrder.cs");
        C(map.Contains("MapResourceLayerPolicy.Food,MapResourceLayerPolicy.Plorts};")&&map.Contains("string layer=MapResourceLayerPolicy.Classify(id,entry?.Category);")&&
          map.Contains("Category=layer,")&&!map.Contains("Category=\"Ресурсы\""),"discoverable records are classified into their own layer");
        C(order2.Contains("\"Ресурсы\",\"Еда\",\"Плорты\""),"the layer order mirrors the policy names");
        C(icons.Contains("if(MapResourceLayerPolicy.Owns(point.Category))"),"food and plort markers show their exact in-game item icon");
        C(icons.Contains("Category(MapResourceLayerPolicy.Food,\"iconCategoryEverything\");")&&icons.Contains("Category(MapResourceLayerPolicy.Plorts,\"iconPlortPink_small\");")&&
          icons.Contains("Category(MapResourceLayerPolicy.Food,\"iconVeggieCarrot_small\");"),"new layer buttons borrow exact native game sprites");
        C(ui.Contains("map.PanelExpanded?MapLayerOrder.PanelHeight(layers.Length):56")&&ui.Contains("Rect panel=panelBounds;")&&
          ui.Contains("new Rect(16,controlsY+190,292,42)")&&!ui.Contains("PanelExpanded?780:56"),"panel, marker exclusion and controls grow together");
        C(ui.Contains("152+(i/filterColumns)*44")&&ui.Contains("MapLayerOrder.FilterWidth(area.width,filterLayers.Length)"),"atlas page filters use the shared two-row layout");
    }
}
