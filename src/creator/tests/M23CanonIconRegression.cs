using System;
using System.IO;
using System.Security.Cryptography;
namespace CreatorToolkit;
// Owner request 2026-10-25/26: pods, map nodes and research/phantom drones show
// the exact Slime Rancher 2 Fandom wiki map markers; nothing is drawn by the mod.
internal static class M23CanonIconRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool v,string n)=>check(v,"m23 / canon map icons / "+n);
        C(CanonMapIconPolicy.CategoryCell("Капсулы")==CanonMapIconPolicy.TreasurePod,"treasure pods use the wiki treasure-pod marker");
        C(CanonMapIconPolicy.CategoryCell("Ноды карты")==CanonMapIconPolicy.MapNode,"map nodes use the wiki map-link marker");
        C(CanonMapIconPolicy.Cell("Дроны","ResearchDroneController_18096_CAB-28473b9a","Исследовательский дрон")==CanonMapIconPolicy.ResearchDrone,
          "static research drones use the wiki research-drone marker");
        C(CanonMapIconPolicy.Cell("Дроны","component-route|a","Фантом · место появления")==CanonMapIconPolicy.PhantomDrone&&
          CanonMapIconPolicy.Cell("Дроны","component-drone|7","Дрон-компонент · фантом")==CanonMapIconPolicy.PhantomDrone,
          "phantom routes and live phantoms use the wiki ghostly-drone marker");
        C(CanonMapIconPolicy.Cell("Гордо","component-route|a","Фантом")==CanonMapIconPolicy.None&&CanonMapIconPolicy.Cell(null,null,null)==CanonMapIconPolicy.None&&
          !CanonMapIconPolicy.Owns("Ресурсы")&&!CanonMapIconPolicy.Owns("Еда")&&!CanonMapIconPolicy.Owns("Плорты")&&
          !CanonMapIconPolicy.Owns("Телепорты")&&!CanonMapIconPolicy.Owns(""),"other layers keep their exact native game artwork");
        C(CanonMapIconPolicy.Label(CanonMapIconPolicy.TreasurePod)=="Капсула с сокровищами"&&CanonMapIconPolicy.Label(CanonMapIconPolicy.MapNode)=="Узел карты"&&
          CanonMapIconPolicy.Label(CanonMapIconPolicy.ResearchDrone)=="Поисковый дрон"&&CanonMapIconPolicy.Label(CanonMapIconPolicy.PhantomDrone)=="Призрачный дрон"&&
          CanonMapIconPolicy.Label(CanonMapIconPolicy.None)=="","badge labels follow the Russian wiki map legend");
        C(CanonMapIconPolicy.Source(CanonMapIconPolicy.TreasurePod)=="Map treasure pod marker.png"&&CanonMapIconPolicy.Source(CanonMapIconPolicy.MapNode)=="Map link marker.png"&&
          CanonMapIconPolicy.Source(CanonMapIconPolicy.ResearchDrone)=="Map res drone marker SR2.png"&&
          CanonMapIconPolicy.Source(CanonMapIconPolicy.PhantomDrone)=="Map drone marker SR2.png"&&CanonMapIconPolicy.Source(CanonMapIconPolicy.None)=="",
          "every badge names the wiki file it was copied from");
        // Byte-identical wiki artwork: the RGBA of each FandomAtlas cell is pinned
        // to the decoded ru.slimerancher.fandom.com marker file (64 x 64 px).
        var path=Path.Combine(root,"src/creator/FandomAtlas.rgba");
        byte[] atlas=File.Exists(path)?File.ReadAllBytes(path):null;
        const int columns=6,rows=4,cell=64,width=columns*cell;
        bool sized=atlas!=null&&atlas.Length==width*rows*cell*4;
        C(sized,"the wiki atlas keeps its 6 x 4 cells of 64 px");
        string CellSha(int index)
        {
            if(!sized||index<0)return "";
            var block=new byte[cell*cell*4];int x0=index%columns*cell,y0=index/columns*cell;
            for(int y=0;y<cell;y++)Buffer.BlockCopy(atlas,((y0+y)*width+x0)*4,block,y*cell*4,cell*4);
            return Convert.ToHexString(SHA256.HashData(block)).ToLowerInvariant();
        }
        C(CellSha(CanonMapIconPolicy.TreasurePod)=="f7a332f3e108b9a3a3ea161d8839b19d7ceaec8d687ba06754d2cbb46e1984e6","the pod badge is the exact wiki treasure-pod file");
        C(CellSha(CanonMapIconPolicy.MapNode)=="c74228d8e12dfe5e62ee3337a6445b2d6d322fc2975a87321e11f62a0041531e","the map-node badge is the exact wiki map-link file");
        C(CellSha(CanonMapIconPolicy.ResearchDrone)=="ec319d9a21afa7afe730ab307d2378f8c6b3f6132f57e148f1ef32fc26ab4638","the research-drone badge is the exact wiki file");
        C(CellSha(CanonMapIconPolicy.PhantomDrone)=="447f059c53543aab111ffee42036802c4cf371955506b9c6a74b8978b7510a2a","the ghostly-drone badge is the exact wiki file");
        // No mod-drawn atlas, loader or resource remains in the build.
        var csproj=File.ReadAllText(Path.Combine(root,"src/creator/CreatorToolkit.csproj"));
        C(!csproj.Contains("CanonMapIcons")&&csproj.Contains("LogicalName=\"CreatorToolkit.FandomAtlas.rgba\""),"only the wiki atlas is embedded");
        C(!File.Exists(Path.Combine(root,"src/creator/CanonMapIcons.rgba"))&&!File.Exists(Path.Combine(root,"src/creator/CanonMapIcons.cs")),
          "the hand-drawn badge atlas and its loader are gone");
        var fandom=File.ReadAllText(Path.Combine(root,"src/creator/FandomMapMarkers.cs"));
        C(fandom.Contains("new Texture2D(w,h,TextureFormat.RGBA32,true)")&&fandom.Contains("FilterMode.Trilinear")&&fandom.Contains("texture.Apply(true,true)")&&
          fandom.Contains("HideFlags.HideAndDontSave"),"the wiki atlas builds its mip chain once so 14-32 px markers stay smooth");
        // Wiring: the wiki badge is drawn before borrowed art wherever it appears.
        var ui=File.ReadAllText(Path.Combine(root,"src/creator/ToolkitUi.cs"));
        var mini=File.ReadAllText(Path.Combine(root,"src/creator/MiniMapUi.cs"));
        C(ui.Contains("bool drawn=FandomIcon(rect,index);")&&!ui.Contains("CanonMapIcons."),"canon badges draw through the wiki atlas path");
        C(ui.Contains("bool canonDrawn=CanonIcon(iconRect,canonMarker);"),"map markers draw the wiki badge first");
        C(ui.Contains("CanonIcon(iconRect,CanonMapIconPolicy.CategoryCell(category))"),"layer buttons show the wiki badge");
        C(ui.Contains("CanonIcon(podRect,CanonMapIconPolicy.TreasurePod)"),"capsule cards show the wiki pod badge");
        C(ui.Contains("CanonIcon(detailIcon,CanonMapIconPolicy.Cell(selected?.Category,selected?.Id,selected?.Name))"),"atlas details show the wiki badge");
        C(ui.Contains("!CanonIcon(iconRect,canonCell)"),"atlas list rows fall back to the wiki badge");
        C(mini.Contains("CanonIcon(icon,CanonMapIconPolicy.Cell(point.Category,point.Id,point.Name))"),"the minimap shows the wiki badge");
        var icons=File.ReadAllText(Path.Combine(root,"src/creator/NativeIconTools.cs"));
        C(!icons.Contains("iconMapDetector")&&icons.Contains("CanonMapIconPolicy.Source(CanonMapIconPolicy.MapNode)"),
          "map nodes borrow no detector flask and the census names the wiki file");
        var suite=File.ReadAllText(Path.Combine(root,"src/creator/FeatureSuite.cs"));
        C(suite.Contains("CanonMapIconPolicy.Owns(category)?FandomMapMarkers.Atlas!=null"),"the in-game suite requires the wiki atlas for canon layers");
    }
}
