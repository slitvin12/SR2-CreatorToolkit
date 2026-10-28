using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.UI.Gadget;

namespace CreatorToolkit;

// Owner request 2026-10-26: opt-in «Бесконечный режим строителя».
// Installed lazily on the first enable, never at boot. Read side: while the
// native build mode or one of its gadget menus evaluates, the live gadget
// inventory reports InfiniteBuilderPolicy.DisplayStock for known gadgets.
// Write side: exactly one native placement unit of the held, known gadget is
// not spent. Refunds, refinery capacity, fabrication and every other writer
// see real counts. This class never writes counts, blueprints, the refinery
// or the save; the native save reads the dictionary itself, not GetCount.
internal static class InfiniteBuildHooks
{
    private const byte KindForeign=0,KindGadget=1,KindLearned=2;
    private static bool _installed,_countHook,_spendHook,_enabled;
    private static int _scope,_suppress,_frame;
    private static IntPtr _model,_session;
    private static GadgetDirector _director;
    private static PediaDirector _pedia;
    private static Il2CppMonomiPark.SlimeRancher.ObservableValue<bool> _gadgetMode;
    private static readonly Dictionary<IntPtr,byte> Kinds=new();
    private static string _lastAuditHash="";
    internal static bool Enabled=>_enabled;
    internal static bool Installed=>_installed;
    internal static bool Available=>_countHook&&_spendHook;
    internal static int Hooks{get;private set;}
    internal static int FreeSpends{get;private set;}
    internal static int VanillaSpends{get;private set;}
    internal static int VirtualReads{get;private set;}
    internal static int GuardedWrites{get;private set;}
    internal static string LastSpend{get;private set;}="none";
    internal static string InstallError{get;private set;}="";

    internal static int Install(HarmonyLib.Harmony harmony)
    {
        if(_installed)return 0;
        _installed=true;int installed=0;
        bool Patch(Type type,string name,Type[] args,string prefix,string postfix)
        {
            try
            {
                MethodInfo original=AccessTools.Method(type,name,args);
                if(original==null)throw new MissingMethodException(type.FullName,name);
                HarmonyMethod Own(string patch)
                {
                    if(patch==null)return null;
                    var method=AccessTools.Method(typeof(InfiniteBuildHooks),patch);
                    return method==null?throw new MissingMethodException(nameof(InfiniteBuildHooks),patch):new HarmonyMethod(method);
                }
                harmony.Patch(original,prefix:Own(prefix),postfix:Own(postfix));
                installed++;return true;
            }
            catch(Exception e)
            {
                InstallError=type.Name+"."+name+": "+e.Message;
                ToolkitMod.I?.Fail("patch infinite builder "+type.Name+"."+name,e);
                return false;
            }
        }
        var ident=new[]{typeof(IdentifiableType)};
        // Real-count barriers first: pickup/storage refunds, refinery capacity
        // and fabrication limits must never observe the virtual stock.
        // Never detour AddItem(ref AddItemOptions): no ref-struct bridge (see NativeHooks).
        bool barriers=Patch(typeof(GadgetDirector),"AddItem",ident,nameof(RealBegin),nameof(RealEnd));
        barriers&=Patch(typeof(GadgetDirector),"AddItem",new[]{typeof(IdentifiableType),typeof(int)},nameof(RealBegin),nameof(RealEnd));
        barriers&=Patch(typeof(GadgetDirector),"AddItem",new[]{typeof(IdentifiableType),typeof(int),typeof(bool)},nameof(RealBegin),nameof(RealEnd));
        barriers&=Patch(typeof(GadgetDirector),"GetRefinerySpaceAvailable",ident,nameof(RealBegin),nameof(RealEnd));
        barriers&=Patch(typeof(GadgetDirector),"CanAddGadget",new[]{typeof(GadgetDefinition),typeof(int)},nameof(RealBegin),nameof(RealEnd));
        _spendHook=barriers&&Patch(typeof(GadgetDirector),"TryToSpendItems",
            new[]{typeof(Il2CppSystem.Collections.Generic.List<IdentCostEntry>),typeof(int)},nameof(SpendBegin),nameof(SpendEnd));
        bool guarded=_spendHook&&Patch(typeof(GadgetsModel),"SetCount",new[]{typeof(IdentifiableType),typeof(int)},nameof(CountWrite),null);
        // The read-side view is installed last and only after every write-side guard.
        _countHook=guarded&&Patch(typeof(GadgetsModel),"GetCount",ident,null,nameof(CountRead));
        if(_countHook)
        {
            // Gadget menus that may evaluate outside the build mode. Optional:
            // a missing scope only means that one menu shows the real count.
            void Scope(Type type,string name,Type[] args)=>Patch(type,name,args,nameof(ScopeBegin),nameof(ScopeEnd));
            Scope(typeof(NewGadgetItemButton),"SetData",new[]{typeof(GadgetItemData),typeof(GadgetUIManager),typeof(GadgetDirector)});
            Scope(typeof(NewGadgetItemButton),"SetDataAsFavorite",new[]{typeof(GadgetDefinition),typeof(GadgetUIManager),typeof(GadgetDirector)});
            Scope(typeof(GadgetFavoriteSlot),"SetData",new[]{typeof(GadgetDefinition),typeof(GadgetUIManager)});
            Scope(typeof(GadgetUIManager),"CanSelectIndex",new[]{typeof(int)});
            Scope(typeof(GadgetCategory),"IsEmpty",Type.EmptyTypes);
            Scope(typeof(NewGadgetSelectionUI),"CanPlaceItem",new[]{typeof(GadgetItemData)});
            Scope(typeof(NewGadgetSelectionUI),"UpdatePromptsForItemGrid",new[]{typeof(GadgetItemData)});
            Scope(typeof(NewGadgetSelectionUI),"PlaceNow",new[]{typeof(GadgetItemData)});
            Scope(typeof(GadgetLabelDisplay),"UpdateLabels",Type.EmptyTypes);
        }
        Hooks=installed;
        return installed;
    }

    internal static void SetEnabled(bool enabled,ToolkitMod app)
    {
        if(!enabled)
        {
            bool was=_enabled;
            _enabled=false;_gadgetMode=null;_director=null;_pedia=null;_model=_session=IntPtr.Zero;Kinds.Clear();
            if(was)app?.LoggerInstance.Msg("Infinite builder disabled; the real inventory view is restored.");
            return;
        }
        if(!Available)
            throw new InvalidOperationException("Нативные перехватчики режима не установлены"+(InstallError.Length>0?": "+InstallError:"."));
        var director=app.Context?.GadgetDirector;var model=director?._model;
        if(!app.Ready||director==null||model==null)throw new InvalidOperationException("Нативный GadgetDirector ещё не готов.");
        Kinds.Clear();_director=director;_model=model.Pointer;_session=app.Session;
        _pedia=app.Context.PediaDirector;_gadgetMode=app.Context.PlayerState?.GadgetModeActive;
        _enabled=true;
        app.LoggerInstance.Msg("Infinite builder enabled hooks="+Hooks+" free="+FreeSpends+" vanilla="+VanillaSpends);
    }

    // Once per frame, before any queued action: an interrupted native call can
    // never leak a scope, and a changed save or director disables the view.
    internal static void Frame(ToolkitMod app)
    {
        _scope=0;_suppress=0;
        if(!_enabled)return;
        try
        {
            if(app.Session!=_session)
            {SetEnabled(false,app);app.Notify(InfiniteBuilderPolicy.LabelOff+" · сейв сменился");return;}
            // A streamed zone briefly has no context: no virtual view until it is live again.
            var director=app.Ready?app.Context.GadgetDirector:null;var model=director?._model;
            if(director==null||model==null){_model=IntPtr.Zero;_director=null;return;}
            if(_director==null||_director.Pointer!=director.Pointer||_model!=model.Pointer)
            {Kinds.Clear();_director=director;_model=model.Pointer;_pedia=app.Context.PediaDirector;_gadgetMode=app.Context.PlayerState?.GadgetModeActive;}
            else if(++_frame%90==0){Kinds.Clear();_pedia=app.Context.PediaDirector;_gadgetMode=app.Context.PlayerState?.GadgetModeActive;}
        }
        catch(Exception e){_model=IntPtr.Zero;app.Fail("infinite builder frame",e);}
    }

    private static bool LiveGadgetMode()
    {
        try{return _gadgetMode!=null&&!_gadgetMode.WasCollected&&_gadgetMode.Value;}
        catch{return false;}
    }

    // GetCount postfix: only the live model, only known gadgets, only while the
    // build mode or a scoped gadget menu evaluates, never inside a real scope.
    private static void CountRead(GadgetsModel __instance,IdentifiableType __0,ref int __result)
    {
        if(!_enabled||_suppress>0||__result>=InfiniteBuilderPolicy.Threshold||__instance==null||__0==null)return;
        try
        {
            if(__instance.Pointer!=_model||
                !InfiniteBuilderPolicy.ShouldInflate(_enabled,_suppress,_scope,_scope>0||LiveGadgetMode()))return;
            byte kind=Kind(__0);if(kind==KindForeign)return;
            int shown=InfiniteBuilderPolicy.Display(true,InfiniteBuilderPolicy.Known(kind==KindLearned,__result),__result);
            if(shown!=__result){__result=shown;VirtualReads++;}
        }
        catch(Exception e){ToolkitMod.I?.Fail("infinite builder count view",e);}
    }

    // SetCount prefix. Known writers run inside real barriers; an unknown writer
    // that derived its value from the virtual stock keeps only its real delta.
    private static void CountWrite(GadgetsModel __instance,IdentifiableType __0,ref int __1)
    {
        if(!_enabled||_suppress>0||__1<InfiniteBuilderPolicy.Threshold||__instance==null||__0==null)return;
        try
        {
            if(__instance.Pointer!=_model||Kind(__0)==KindForeign)return;
            int real;_suppress++;
            try{real=__instance.GetCount(__0);}
            finally{if(_suppress>0)_suppress--;}
            if(!InfiniteBuilderPolicy.LooksVirtual(real,__1))return;
            int written=__1;__1=InfiniteBuilderPolicy.TranslateVirtualWrite(real,written);GuardedWrites++;
            LastSpend=Stamp()+" guarded "+__0.ReferenceId+" "+written+"->"+__1;
            ToolkitMod.I?.LoggerInstance.Warning("Infinite builder kept a real count: "+LastSpend);
        }
        catch(Exception e){ToolkitMod.I?.Fail("infinite builder count guard",e);}
    }

    // TryToSpendItems prefix. HasItem/GetItemCount/SetCount inside always see
    // real counts. Fail closed: anything unexpected keeps the native spend.
    private static bool SpendBegin(GadgetDirector __instance,Il2CppSystem.Collections.Generic.List<IdentCostEntry> __0,int __1,ref bool __result)
    {
        _suppress++;
        if(!_enabled)return true;
        string held="none";
        try
        {
            var app=ToolkitMod.I;
            if(app==null||!app.Ready||app.Session!=_session||__instance==null||_director==null||
                __instance.Pointer!=_director.Pointer||__0==null)return Vanilla("foreign",held);
            bool mode=app.Context.PlayerState?.GadgetModeActive?.Value==true;
            var item=mode?app.Gadgets?.NativeItem:null;
            var definition=item?._heldGadget;held=definition?.ReferenceId??"none";
            bool moved=item==null||item._holdingPickedUpGadget;
            int entries=__0.Count,known=0,amount=0;
            if(entries==1&&definition!=null)
            {
                var cost=__0[0];amount=cost.Amount;
                var type=cost.IdentType;var paid=type?.TryCast<GadgetDefinition>();
                if(paid!=null&&(paid.Pointer==definition.Pointer||GadgetDefinition.SameBaseGadget(paid,definition))&&
                    InfiniteBuilderPolicy.Known(Kind(type)==KindLearned,__instance.GetItemCount(type)))known=1;
            }
            if(!InfiniteBuilderPolicy.SkipSpend(true,mode,moved,entries,known,amount,__1))
                return Vanilla(!mode?"not-build-mode":moved?"moved":"cost",held);
            __result=true;FreeSpends++;LastSpend=Stamp()+" free "+held;
            app.LoggerInstance.Msg("Infinite builder: native placement unit not spent definition="+held+" free="+FreeSpends);
            return false;
        }
        catch(Exception e){ToolkitMod.I?.Fail("infinite builder spend",e);return Vanilla("error",held);}
    }
    private static void SpendEnd(){if(_suppress>0)_suppress--;}
    private static bool Vanilla(string reason,string held)
    {VanillaSpends++;LastSpend=Stamp()+" vanilla:"+reason+" "+held;return true;}
    private static void RealBegin()=>_suppress++;
    private static void RealEnd(){if(_suppress>0)_suppress--;}
    private static void ScopeBegin()=>_scope++;
    private static void ScopeEnd(){if(_scope>0)_scope--;}
    private static string Stamp()=>DateTime.Now.ToString("HH:mm:ss");

    private static byte Kind(IdentifiableType type)
    {
        IntPtr key=type.Pointer;
        if(Kinds.TryGetValue(key,out byte kind))return kind;
        kind=KindForeign;
        var definition=type.TryCast<GadgetDefinition>();
        if(definition!=null&&_director!=null)
        {
            _suppress++;
            try{kind=IsLearned(definition)?KindLearned:KindGadget;}
            finally{if(_suppress>0)_suppress--;}
        }
        if(Kinds.Count>=4096)Kinds.Clear();
        Kinds[key]=kind;return kind;
    }
    // Known = discovered by the player: an obtained or an unlocked blueprint
    // (own, base or linked half), an unlocked Slimepedia entry, or already
    // placed. Live check 2026-10-26: a progressed save reported HasBlueprint
    // false for every gadget its build menu lists, so HasBlueprint alone left
    // only the placed types at 999. Read-only: nothing here unlocks anything.
    private static bool IsLearned(GadgetDefinition definition)
    {
        var basis=definition.BaseGadget;var linked=definition.LinkedDefinition;
        return Blueprint(definition)||(basis!=null&&Blueprint(basis))||(linked!=null&&Blueprint(linked))||
            InPedia(definition)||(basis!=null&&InPedia(basis))||_director.GetPlacedCount(definition)>0;
    }
    private static bool Blueprint(GadgetDefinition definition)=>
        _director.HasBlueprint(definition)||_director.IsBlueprintUnlocked(definition);
    private static bool InPedia(GadgetDefinition definition)
    {
        try{return _pedia!=null&&!_pedia.WasCollected&&_pedia.IsUnlocked(definition);}
        catch{return false;}
    }
    // Toolkit list/place gate in the mode: the same known set as the native menu.
    internal static bool IsKnown(GadgetDefinition definition)=>
        _enabled&&_director!=null&&definition!=null&&Kind(definition)==KindLearned;

    // Toolkit code that must read real stock even inside the build mode.
    internal readonly struct RealScope:IDisposable
    {
        private readonly bool _active;
        internal RealScope(bool active){_active=active;if(active)_suppress++;}
        public void Dispose(){if(_active&&_suppress>0)_suppress--;}
    }
    internal static RealScope RealCounts()=>new(true);

    // Read-only evidence: the live inventory dictionary (gadgets and refinery
    // resources) hashed. Equal hashes before/after a free placement prove that
    // neither the inventory nor the refinery changed.
    internal static string Audit(ToolkitMod app)
    {
        if(!app.Ready)return "BuilderAudit: not-ready\n";
        var model=app.Context.GadgetDirector?._model;
        if(model?._itemCounts==null)return "BuilderAudit: no-model\n";
        var lines=new List<string>();int gadgets=0,resources=0;long units=0;
        foreach(var pair in model._itemCounts)
        {
            var key=pair.Key;if(key==null)continue;
            bool gadget=key.TryCast<GadgetDefinition>()!=null;
            if(gadget)gadgets++;else resources++;
            units+=pair.Value;
            lines.Add((gadget?"G ":"R ")+key.ReferenceId+"="+pair.Value);
        }
        lines.Sort(StringComparer.Ordinal);
        string body=string.Join("\n",lines);
        string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).Substring(0,16);
        int placed=app.Context.GameModel.AllGadgets().Count;
        string knowledge=Knowledge(app,out string summary);
        string file=Path.Combine(app.DataDirectory,"builder_audit_"+DateTime.Now.ToString("HHmmss")+".txt");
        File.WriteAllText(file,"hash="+hash+"\nplaced="+placed+"\nenabled="+_enabled+"\nfree="+FreeSpends+"\n"+body+
            "\n# knowledge (not hashed) "+summary+"\n"+knowledge+"\n");
        string previous=_lastAuditHash;_lastAuditHash=hash;
        return "BuilderAudit: entries="+lines.Count+" gadgets="+gadgets+" resources="+resources+" units="+units+
            " placed="+placed+" hash="+hash+" previous="+(previous.Length>0?previous:"none")+
            " same="+(previous==hash)+" "+summary+" file="+Path.GetFileName(file)+"\n";
    }

    // Per-gadget evidence for the known set: blueprint obtained/unlocked,
    // Slimepedia, placed and real stock. Read-only.
    private static string Knowledge(ToolkitMod app,out string summary)
    {
        summary="known=n/a";
        var director=app.Context.GadgetDirector;var model=director?._model;
        if(director==null||model==null)return "";
        var pedia=app.Context.PediaDirector;var rows=new List<string>();
        int total=0,has=0,unlocked=0,inPedia=0,placedTypes=0,known=0,noPrefab=0;
        using(RealCounts())
        foreach(var type in NativeEnumerable.Items(director.GetAllGadgets()))
        {
            var definition=type?.TryCast<GadgetDefinition>();if(definition==null)continue;
            var basis=definition.BaseGadget??definition;total++;
            bool h=director.HasBlueprint(definition)||director.HasBlueprint(basis);
            bool u=director.IsBlueprintUnlocked(definition)||director.IsBlueprintUnlocked(basis);
            bool p=false;try{p=pedia!=null&&pedia.IsUnlocked(definition);}catch{}
            int count=director.GetPlacedCount(definition),real=director.GetItemCount(basis);
            bool k=_enabled&&_director!=null&&Kind(definition)==KindLearned;
            if(h)has++;if(u)unlocked++;if(p)inPedia++;if(count>0)placedTypes++;if(k)known++;if(definition.prefab==null)noPrefab++;
            rows.Add("B "+definition.ReferenceId+" has="+(h?1:0)+" unlocked="+(u?1:0)+" pedia="+(p?1:0)+" placed="+count+
                " real="+real+" known="+(k?1:0)+" prefab="+(definition.prefab!=null?1:0)+" secondary="+(definition.IsSecondary?1:0));
        }
        rows.Sort(StringComparer.Ordinal);
        summary="gadgetTypes="+total+" has="+has+" unlocked="+unlocked+" pedia="+inPedia+" placedTypes="+placedTypes+
            " known="+(_enabled?known.ToString():"off")+" noPrefab="+noPrefab+" sets="+(model.blueprints?.Count??-1)+"/"+
            (model.availBlueprints?.Count??-1)+"/"+(model.registeredBlueprints?.Count??-1);
        return string.Join("\n",rows);
    }

    internal static string Diagnostics()=>
        "InfiniteBuilder: "+_enabled+"\nInfiniteBuilderHooks: "+Hooks+"\nInfiniteBuilderAvailable: "+Available+
        "\nInfiniteBuilderGadgetMode: "+(_enabled&&LiveGadgetMode())+"\nInfiniteBuilderFreeSpends: "+FreeSpends+
        "\nInfiniteBuilderVanillaSpends: "+VanillaSpends+"\nInfiniteBuilderVirtualReads: "+VirtualReads+
        "\nInfiniteBuilderGuardedWrites: "+GuardedWrites+"\nInfiniteBuilderLastSpend: "+LastSpend+
        (InstallError.Length>0?"\nInfiniteBuilderInstallError: "+InstallError:"")+"\n";
}
