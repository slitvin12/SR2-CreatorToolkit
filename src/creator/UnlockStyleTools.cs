using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace CreatorToolkit;

// M27 «Разблокировка»: every slime style (classic and secret) and every gadget
// blueprint of the loaded save. Both are permanent save changes and the UI asks
// for confirmation first (UnlockUi.cs). Eligibility rules: M27UnlockPolicy.
internal sealed partial class UnlockTools
{
    internal string StylesResult { get; private set; } = "";
    internal string BlueprintsResult { get; private set; } = "";
    internal string PalettesResult { get; private set; } = "";

    internal void Styles()
    {
        var director=_app.Context?.SlimeAppearanceDirector;
        var slimes=GameContext.Instance?.SlimeDefinitions?.Slimes;
        if(director==null||slimes==null)
        {
            StylesResult="Каталог стилей слаймов ещё не загружен";
            _app.Notify(StylesResult);
            return;
        }
        int kinds=0,unlocked=0,already=0,discovered=0,failed=0;
        var looks=new List<SlimeAppearance>();
        var seen=new HashSet<IntPtr>();
        for(int i=0;i<slimes.Length;i++)
        {
            var slime=slimes[i];
            if(slime==null)continue;
            looks.Clear();seen.Clear();
            try{Collect(slime,looks,seen);}
            catch(Exception e){failed++;_app.LoggerInstance.Warning("Style list failed slime="+slime.name+": "+e.Message);continue;}
            if(looks.Count==0)continue;
            kinds++;
            foreach(var look in looks)
            {
                try
                {
                    int set=(int)look.SaveSet;
                    bool open=director.IsAppearanceUnlocked(slime,look);
                    if(M27UnlockPolicy.UnlockableStyle(set,open))
                    {
                        director.UnlockAppearance(slime,look);
                        if(director.IsAppearanceUnlocked(slime,look))unlocked++;
                        else{failed++;_app.LoggerInstance.Warning("Style stayed locked slime="+slime.name+" look="+look.name);}
                    }
                    else if(open)already++;
                    if(M27UnlockPolicy.DiscoverSecret(set,director.IsSecretStyleDiscovered(slime)))
                    {
                        director.MaybeDiscoverSecretStyle(slime,look);
                        if(director.IsSecretStyleDiscovered(slime))discovered++;
                    }
                }
                catch(Exception e)
                {
                    failed++;
                    _app.LoggerInstance.Warning("Style unlock failed slime="+slime.name+" look="+look.name+": "+e.Message);
                }
            }
        }
        StylesResult=M27UnlockPolicy.StyleReport(kinds,unlocked,already,discovered,failed);
        _app.Notify(StylesResult);
    }

    // Default appearances plus the runtime (dynamic) list, deduplicated by native pointer.
    private static void Collect(SlimeDefinition slime,List<SlimeAppearance> looks,HashSet<IntPtr> seen)
    {
        var defaults=slime.AppearancesDefault;
        if(defaults!=null)
            for(int i=0;i<defaults.Length;i++)Add(defaults[i],looks,seen);
        var extra=slime.AppearancesDynamic;
        if(extra!=null)
            for(int i=0;i<extra.Count;i++)Add(extra[i],looks,seen);
    }
    private static void Add(SlimeAppearance look,List<SlimeAppearance> looks,HashSet<IntPtr> seen)
    {
        if(look!=null&&seen.Add(look.Pointer))looks.Add(look);
    }

    internal void Blueprints()
    {
        var director=_app.Context?.GadgetDirector;
        if(director?._model==null)
        {
            BlueprintsResult="Нативный GadgetDirector ещё не готов";
            _app.Notify(BlueprintsResult);
            return;
        }
        var definitions=new List<GadgetDefinition>();
        var seen=new HashSet<IntPtr>();
        int examined=0,unlocked=0,already=0,unavailable=0,failed=0;
        // Real stock even inside the infinite build mode (see GadgetTools.RefreshCatalog).
        using(InfiniteBuildHooks.RealCounts())
        {
            foreach(var type in NativeEnumerable.Items(director.GetAllGadgets()))
            {
                var definition=type?.TryCast<GadgetDefinition>();
                if(definition!=null&&seen.Add(definition.Pointer))definitions.Add(definition);
            }
            foreach(var definition in definitions)
            {
                try
                {
                    bool hasPrefab=definition.prefab!=null,secondary=definition.IsSecondary;
                    if(!hasPrefab||secondary)continue;
                    examined++;
                    bool owned=director.HasBlueprint(definition);
                    if(!M27UnlockPolicy.UnlockableBlueprint(hasPrefab,secondary,owned)){already++;continue;}
                    director.AddBlueprint(definition,false);
                    if(director.HasBlueprint(definition))unlocked++;else unavailable++;
                }
                catch(Exception e)
                {
                    failed++;
                    _app.LoggerInstance.Warning("Blueprint unlock failed gadget="+definition.name+": "+e.Message);
                }
            }
        }
        BlueprintsResult=M27UnlockPolicy.BlueprintReport(examined,unlocked,already,unavailable,failed);
        _app.Notify(BlueprintsResult);
        try{_app.Gadgets?.RefreshCatalog();}
        catch(Exception e){_app.LoggerInstance.Warning("Gadget catalog refresh after blueprints failed: "+e.Message);}
    }

    // M28 «Открыть все стили вакпака»: every VacPalette of the native catalog becomes
    // owned through PaletteDirector.SetOwned (PaletteInventoryModel of the save).
    internal void Palettes()
    {
        var director=_app.Context?.PaletteDirector;
        var list=director?._palettes;
        if(director==null||list==null||director._paletteInventoryModel==null)
        {
            PalettesResult="Каталог стилей вакпака ещё не загружен";
            _app.Notify(PalettesResult);
            return;
        }
        var palettes=CollectPalettes(list);
        var pedia=_app.Context.PediaDirector;
        int opened=0,already=0,entries=0,failed=0;
        foreach(var palette in palettes)
        {
            try
            {
                bool owned=director.IsOwned(palette);
                if(M28PalettePolicy.Unlockable(true,owned))
                {
                    director.SetOwned(palette);
                    if(director.IsOwned(palette))opened++;
                    else{failed++;_app.LoggerInstance.Warning("Vac palette stayed locked: "+palette.name);}
                }
                else already++;
                var entry=palette.PediaEntry;
                if(pedia!=null&&entry!=null&&!entry.WasCollected&&!pedia.IsUnlocked(entry)&&pedia.Unlock(entry,false))entries++;
            }
            catch(Exception e)
            {
                failed++;
                _app.LoggerInstance.Warning("Vac palette unlock failed palette="+palette.name+": "+e.Message);
            }
        }
        PalettesResult=M28PalettePolicy.Report(palettes.Count,opened,already,entries,failed);
        _app.Notify(PalettesResult);
        _app.LoggerInstance.Msg("[M28] "+PalettesResult);
    }

    private static List<Il2CppMonomiPark.SlimeRancher.Ranch.VacPalette> CollectPalettes(Il2CppMonomiPark.SlimeRancher.Ranch.PaletteList list)
    {
        var result=new List<Il2CppMonomiPark.SlimeRancher.Ranch.VacPalette>();
        var seen=new HashSet<IntPtr>();
        int count=list.Count;
        for(int i=0;i<count&&i<4096;i++)
        {
            var vac=list[i]?.TryCast<Il2CppMonomiPark.SlimeRancher.Ranch.VacPalette>();
            if(vac!=null&&!vac.WasCollected&&seen.Add(vac.Pointer))result.Add(vac);
        }
        return result;
    }

    // Read-only catalog dump for the m28-palettes test command.
    internal string PaletteReport()
    {
        var text=new System.Text.StringBuilder();
        var director=_app.Context?.PaletteDirector;
        var list=director?._palettes;
        text.AppendLine("director="+(director!=null)+" list="+(list!=null)+" inventory="+(director?._paletteInventoryModel!=null)+" result="+PalettesResult);
        if(director==null||list==null)return text.ToString();
        var pedia=_app.Context.PediaDirector;
        int count=list.Count,vacs=0,owned=0;
        text.AppendLine("catalog="+count);
        for(int i=0;i<count&&i<512;i++)
        {
            var palette=list[i];
            if(palette==null||palette.WasCollected){text.AppendLine(i+" | null");continue;}
            bool vac=palette.TryCast<Il2CppMonomiPark.SlimeRancher.Ranch.VacPalette>()!=null;if(vac)vacs++;
            string own=Read(()=>director.IsOwned(palette).ToString());if(own=="True")owned++;
            var entry=ReadObject(()=>palette.PediaEntry);
            text.Append(i).Append(" | ").Append(palette.name)
                .Append(" | type=").Append(Read(()=>palette.GetIl2CppType().Name))
                .Append(" | vac=").Append(vac)
                .Append(" | ref=").Append(Read(()=>palette.ReferenceId))
                .Append(" | name=").Append(Read(()=>palette.DisplayName?.GetLocalizedString()))
                .Append(" | owned=").Append(own)
                .Append(" | pedia=").Append(entry!=null?entry.name:"-")
                .Append(" | pediaUnlocked=").Append(entry!=null&&pedia!=null?Read(()=>pedia.IsUnlocked(entry).ToString()):"-")
                .Append(" | icon=").Append(Read(()=>palette.Icon?.name)).AppendLine();
        }
        text.AppendLine("vac="+vacs+" owned="+owned);
        return text.ToString();
    }
    private static string Read(Func<string> read){try{return read()??"";}catch(Exception e){return "!"+e.GetType().Name;}}
    private static T ReadObject<T>(Func<T> read) where T:class{try{return read();}catch{return null;}}
}
