using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
namespace CreatorToolkit;
// Owner request 2026-10-26: «Бесконечный режим строителя» — known gadgets and
// decor show 999 in the build menus; a placement spends neither the crafted
// unit nor refinery resources. Counts, blueprints and the save stay real.
internal static class M24InfiniteBuilderRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool v,string n)=>check(v,"m24 / infinite builder / "+n);
        const int S=InfiniteBuilderPolicy.DisplayStock,T=InfiniteBuilderPolicy.Threshold,W=InfiniteBuilderPolicy.Window;
        C(S==999&&W==128&&T==871,"menu stock is 999 with a bounded virtual window");
        C(InfiniteBuilderPolicy.Label(true)=="Бесконечный режим строителя: ВКЛ"&&InfiniteBuilderPolicy.Label(false)=="Бесконечный режим строителя: ВЫКЛ",
          "toggle labels match the owner's wording");
        C(InfiniteBuilderPolicy.Display(true,true,0)==S&&InfiniteBuilderPolicy.Display(true,true,5)==S&&InfiniteBuilderPolicy.Display(false,true,5)==5&&
          InfiniteBuilderPolicy.Display(true,false,0)==0&&InfiniteBuilderPolicy.Display(true,true,T)==T&&InfiniteBuilderPolicy.Display(true,true,-1)==-1,
          "known gadgets show 999 only while enabled; unknown, large or invalid counts stay real");
        C(InfiniteBuilderPolicy.Known(true,0)&&InfiniteBuilderPolicy.Known(false,1)&&!InfiniteBuilderPolicy.Known(false,0),
          "known = learned blueprint or an owned unit");
        C(InfiniteBuilderPolicy.ShouldInflate(true,0,1,false)&&InfiniteBuilderPolicy.ShouldInflate(true,0,0,true)&&
          !InfiniteBuilderPolicy.ShouldInflate(true,1,1,true)&&!InfiniteBuilderPolicy.ShouldInflate(true,0,0,false)&&
          !InfiniteBuilderPolicy.ShouldInflate(false,0,1,true),
          "virtual view only inside the build mode or a gadget menu, never inside a real-count scope");
        C(InfiniteBuilderPolicy.SkipSpend(true,true,false,1,1,1,1),"one placement unit of the held known gadget is not spent");
        C(!InfiniteBuilderPolicy.SkipSpend(false,true,false,1,1,1,1)&&!InfiniteBuilderPolicy.SkipSpend(true,false,false,1,1,1,1)&&
          !InfiniteBuilderPolicy.SkipSpend(true,true,true,1,1,1,1)&&!InfiniteBuilderPolicy.SkipSpend(true,true,false,2,1,1,1)&&
          !InfiniteBuilderPolicy.SkipSpend(true,true,false,1,0,1,1)&&!InfiniteBuilderPolicy.SkipSpend(true,true,false,1,1,2,1)&&
          !InfiniteBuilderPolicy.SkipSpend(true,true,false,1,1,1,2)&&!InfiniteBuilderPolicy.SkipSpend(true,true,false,0,0,0,1),
          "disabled, outside the build mode, moving a placed gadget, crafting/refinery, unknown or multi-unit costs keep the native spend");
        C(InfiniteBuilderPolicy.LooksVirtual(0,S-1)&&InfiniteBuilderPolicy.LooksVirtual(3,S+1)&&!InfiniteBuilderPolicy.LooksVirtual(0,1)&&
          !InfiniteBuilderPolicy.LooksVirtual(T,S+1)&&!InfiniteBuilderPolicy.LooksVirtual(-1,S),
          "only writes derived from the virtual stock are recognised");
        C(InfiniteBuilderPolicy.TranslateVirtualWrite(0,S-1)==0&&InfiniteBuilderPolicy.TranslateVirtualWrite(2,S+1)==3&&
          InfiniteBuilderPolicy.TranslateVirtualWrite(2,S+100000)==2+W&&InfiniteBuilderPolicy.TranslateVirtualWrite(4,S)==4,
          "a virtual spend becomes a no-op and a refund keeps only its bounded real delta");
        C(InfiniteBuilderPolicy.CanPlaceFromToolkit(true,0)&&InfiniteBuilderPolicy.CanPlaceFromToolkit(false,1)&&
          !InfiniteBuilderPolicy.CanPlaceFromToolkit(false,0)&&InfiniteBuilderPolicy.Count(true,0)=="999"&&InfiniteBuilderPolicy.Count(false,7)=="7",
          "toolkit list and place button follow the mode");

        string Read(string name)=>File.ReadAllText(Path.Combine(root,"src","creator",name));
        string hooks=Read("InfiniteBuildHooks.cs"),gadgets=Read("GadgetTools.cs"),care=Read("CareUi.cs"),mod=Read("ToolkitMod.cs"),
            policy=Read("InfiniteBuilderPolicy.cs"),project=File.ReadAllText(Path.Combine(root,"src","creator","tests","CreatorToolkit.Tests.csproj"));
        C(!hooks.Contains("PlaceGadgetEvent")&&!hooks.Contains("HasSpendables")&&!hooks.Contains(".SetCount(")&&!hooks.Contains("AddBlueprint")&&
          !hooks.Contains("_itemCounts[")&&!hooks.Contains("PatchAll")&&!hooks.Contains("typeof(GadgetDirector.AddItemOptions"),
          "hooks never detour placement or spendables, never write counts/blueprints, never bridge ref AddItemOptions");
        C(hooks.Contains("\"TryToSpendItems\"")&&hooks.Contains("nameof(SpendBegin),nameof(SpendEnd)")&&hooks.Contains("__result=true;FreeSpends++")&&
          hooks.Contains("return Vanilla(\"error\",held)")&&hooks.Contains("item._holdingPickedUpGadget")&&
          hooks.Contains("GadgetDefinition.SameBaseGadget(paid,definition)"),
          "the spend hook skips only the held gadget's unit, keeps moves native and fails closed");
        C(hooks.Contains("_spendHook=barriers&&")&&hooks.Contains("bool guarded=_spendHook&&")&&hooks.Contains("_countHook=guarded&&"),
          "the virtual view is installed only after the barriers, the spend hook and the write guard");
        foreach(var barrier in new[]{"\"AddItem\",ident,","\"AddItem\",new[]{typeof(IdentifiableType),typeof(int)},",
            "\"AddItem\",new[]{typeof(IdentifiableType),typeof(int),typeof(bool)},","\"GetRefinerySpaceAvailable\",ident,","\"CanAddGadget\","})
            C(hooks.Contains(barrier),"real-count barrier "+barrier);
        C(hooks.Contains("__instance.Pointer!=_model")&&hooks.Contains("_scope=0;_suppress=0;")&&hooks.Contains("app.Session!=_session"),
          "only the live model inflates; scopes reset every frame; a changed save disables the mode");
        C(gadgets.Contains("_app.EnsureInfiniteBuilderHooks()")&&gadgets.Contains("InfiniteBuildHooks.SetEnabled(false,_app)")&&
          gadgets.Contains("free?after==job.InventoryBefore:CarePolicy.SpentOne(job.InventoryBefore,after)")&&
          gadgets.Contains("job.FreeSpendsBefore=InfiniteBuildHooks.FreeSpends")&&gadgets.Contains("using (InfiniteBuildHooks.RealCounts())")&&
          !gadgets.Contains("SetCount("),
          "toolkit placement installs hooks lazily, resets with the save and requires an unchanged real stock for a free placement");
        C(hooks.Contains("IsBlueprintUnlocked(")&&hooks.Contains("_pedia.IsUnlocked(")&&hooks.Contains("GetPlacedCount(definition)>0")&&
          !hooks.Contains(".Unlock(")&&gadgets.Contains("InfiniteBuildHooks.IsKnown(")&&!gadgets.Contains("if (Catalog.Count > 0) RefreshCatalog();"),
          "known set = obtained/unlocked blueprint, Slimepedia entry or placed; read-only; the toolkit list and gates use the same set");
        C(care.Contains("InfiniteBuilderPolicy.Label(infinite)")&&care.Contains("tools.SetInfiniteBuilder(!tools.InfiniteBuilder)")&&
          care.Contains("tools.DisplayCount(entry)")&&care.Contains("tools.CanPlace(_gadgetSelection)"),
          "panel toggle, 999 list and the place gate");
        C(mod.Contains("InfiniteBuildHooks.Frame(this)")&&mod.Contains("InfiniteBuildHooks.Install(HarmonyInstance)")&&
          mod.Contains("case \"builder-audit\"")&&mod.Contains("details.Append(InfiniteBuildHooks.Diagnostics())")&&
          mod.Contains("InstalledHooks = saveHooks+NativeHooks.Install(HarmonyInstance);"),
          "per-frame reset, lazy install (never at boot), read-only audit and diagnostics");
        C(policy.Contains("internal static class InfiniteBuilderPolicy")&&!policy.Contains("Il2Cpp")&&
          project.Contains("..\\InfiniteBuilderPolicy.cs"),"policy stays pure and is linked into the tests");

        // Native targets exist with the exact parameter counts the hooks bind.
        using var pe=new PEReader(File.OpenRead(Path.Combine(root,"refs","Assembly-CSharp.dll")));
        var md=pe.GetMetadataReader();
        var types=md.TypeDefinitions.Where(h=>md.GetTypeDefinition(h).GetDeclaringType().IsNil)
            .ToDictionary(h=>md.GetString(md.GetTypeDefinition(h).Namespace)+"."+md.GetString(md.GetTypeDefinition(h).Name));
        int Count(MethodDefinition method)
        {
            var reader=md.GetBlobReader(method.Signature);var header=reader.ReadSignatureHeader();
            if(header.IsGeneric)reader.ReadCompressedInteger();
            return reader.ReadCompressedInteger();
        }
        void Api(string type,string method,params int[] arities)
        {
            bool found=types.TryGetValue(type,out var h);
            var counts=found?md.GetTypeDefinition(h).GetMethods().Select(m=>md.GetMethodDefinition(m))
                .Where(m=>md.GetString(m.Name)==method).Select(Count).ToArray():Array.Empty<int>();
            C(arities.All(counts.Contains),"native target "+type+"."+method+"("+string.Join("|",arities)+")");
        }
        const string G="Il2CppMonomiPark.SlimeRancher.UI.Gadget.";
        Api("Il2Cpp.GadgetDirector","AddItem",1,2,3);
        Api("Il2Cpp.GadgetDirector","TryToSpendItems",2);
        Api("Il2Cpp.GadgetDirector","GetRefinerySpaceAvailable",1);
        Api("Il2Cpp.GadgetDirector","CanAddGadget",2);
        Api("Il2Cpp.GadgetDirector","HasBlueprint",1);
        Api("Il2Cpp.GadgetDirector","GetPlacedCount",1);
        Api("Il2Cpp.GadgetDirector","GetItemCount",1);
        Api("Il2Cpp.GadgetDefinition","SameBaseGadget",2);
        Api("Il2CppMonomiPark.SlimeRancher.DataModel.GadgetsModel","GetCount",1);
        Api("Il2CppMonomiPark.SlimeRancher.DataModel.GadgetsModel","SetCount",2);
        Api("Il2CppMonomiPark.SlimeRancher.DataModel.GadgetsModel","get__itemCounts",0);
        Api("Il2CppMonomiPark.SlimeRancher.Economy.IdentCostEntry","get_IdentType",0);
        Api("Il2CppMonomiPark.SlimeRancher.Economy.IdentCostEntry","get_Amount",0);
        Api("Il2CppMonomiPark.SlimeRancher.Player.PlayerItems.GadgetItem","OpenGadgetMenu",0);
        Api(G+"NewGadgetItemButton","SetData",3);
        Api(G+"NewGadgetItemButton","SetDataAsFavorite",3);
        Api(G+"GadgetFavoriteSlot","SetData",2);
        Api(G+"GadgetUIManager","CanSelectIndex",1);
        Api(G+"GadgetCategory","IsEmpty",0);
        Api(G+"NewGadgetSelectionUI","CanPlaceItem",1);
        Api(G+"NewGadgetSelectionUI","UpdatePromptsForItemGrid",1);
        Api(G+"NewGadgetSelectionUI","PlaceNow",1);
        Api(G+"GadgetLabelDisplay","UpdateLabels",0);
        Api("Il2Cpp.GadgetDirector","IsBlueprintUnlocked",1);
        Api("Il2Cpp.PediaDirector","IsUnlocked",1);
        Api("Il2Cpp.SceneContext","get_PediaDirector",0);
    }
}
