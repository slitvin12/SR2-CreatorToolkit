using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using Il2CppMonomiPark.SlimeRancher.Slime;
using UnityEngine;

namespace CreatorToolkit;

// M27 corral autofeed (creator mode, opt-in behind a confirmation). Hungry slimes
// tracked by loaded corrals get one portion of a diet food (favourite first)
// created next to them and handed to the native chomp path, which owns hunger,
// plorts and the favourite bonus. Bounded: one pass per second, at most
// PortionsPerTick portions and MaxExamined slimes, per-slime cooldowns.
// Diet entries that transform the slime (largo/tarr) are never offered.
internal sealed class CorralAutoFeedTools
{
    private readonly ToolkitMod _app;
    private readonly List<LandPlot> _corrals=new();
    private readonly Dictionary<IntPtr,float> _cooldowns=new();
    private readonly Dictionary<IntPtr,IdentifiableType> _meals=new();
    private readonly HashSet<IntPtr> _foods=new();
    private readonly HashSet<IntPtr> _seen=new();
    private readonly List<IntPtr> _expired=new();
    private readonly List<M27AutoFeedPolicy.Meal> _mealScratch=new();
    private readonly List<IdentifiableType> _typeScratch=new();
    private Il2CppSystem.Collections.Generic.List<IdentifiableType> _types;
    private Il2CppSystem.Collections.Generic.List<Identifiable> _members;
    private IntPtr _session;
    private float _nextTick,_nextScan;
    private int _catalogCount=-1;
    internal bool Enabled{get;private set;}
    internal int Corrals=>_corrals.Count;
    internal int KnownFoods=>_foods.Count;
    internal int Slimes{get;private set;}
    internal int HungrySlimes{get;private set;}
    internal int FedLastTick{get;private set;}
    internal int Portions{get;private set;}
    internal string Status{get;private set;}=M27AutoFeedPolicy.Status(false,0,0,0,0,0);
    internal CorralAutoFeedTools(ToolkitMod app)=>_app=app;

    internal void Request(bool enable)
    {
        if(!enable){Set(false);return;}
        _app.Confirm("Включить автокормление загонов?",
            "Голодные слаймы в загонах прогруженной зоны (до 300 м) получают по одной порции еды из своего рациона: "+
            "сначала любимую, иначе обычную. Еда создаётся бесплатно (режим создателя), не более 4 порций в секунду; "+
            "пережёвывание, сытость и плорты — штатные. Плорты и превращающая еда (ларго, тарр) не выдаются. "+
            "При смене мира автокормление выключается.",
            ()=>Set(true));
    }
    internal void Set(bool enabled)
    {
        if(enabled&&(!_app.Ready||_app.Map.Busy))throw new InvalidOperationException("Дождитесь загрузки мира и завершения перехода.");
        Clear();
        Enabled=enabled;_session=_app.Session;_nextTick=_nextScan=0;
        Status=enabled?"Автокормление включено · поиск загонов…":M27AutoFeedPolicy.Status(false,0,0,0,0,Portions);
    }
    internal void Reset()
    {
        Enabled=false;Clear();Portions=0;_session=IntPtr.Zero;
        Status=M27AutoFeedPolicy.Status(false,0,0,0,0,0);
    }
    private void Clear()
    {
        _corrals.Clear();_cooldowns.Clear();_meals.Clear();_foods.Clear();_seen.Clear();
        _catalogCount=-1;Slimes=HungrySlimes=FedLastTick=0;
    }

    internal void Tick()
    {
        if(!Enabled)return;
        if(_session!=_app.Session){Reset();return;}
        if(_app.Map.Busy||Time.timeScale<=0)return;
        float now=Time.unscaledTime;
        if(now<_nextTick)return;
        _nextTick=now+M27AutoFeedPolicy.TickSeconds;
        try{Feed(now);}
        catch(Exception e)
        {
            Enabled=false;Clear();
            Status="Автокормление остановлено из-за ошибки: "+e.Message;
            _app.Notify(Status);
            throw;
        }
    }

    private void Feed(float now)
    {
        if(now>=_nextScan){Scan();_nextScan=now+M27AutoFeedPolicy.ScanSeconds;}
        RefreshFoods();
        var controller=_app.Player?.Controller;
        var player=_app.Context?.Player?.GetComponentInChildren<TeleportablePlayer>(true);
        if(controller?.Motor==null||player?.SceneGroup==null){Status="Автокормление включено · игрок ещё не готов";return;}
        var origin=controller.Motor.TransientPosition;
        float clock=Time.time;
        Prune(clock);
        _types??=new Il2CppSystem.Collections.Generic.List<IdentifiableType>();
        _members??=new Il2CppSystem.Collections.Generic.List<Identifiable>();
        _seen.Clear();
        int slimes=0,hungry=0,fed=0;
        foreach(var plot in _corrals)
        {
            if(!Budget(slimes,fed))break;
            if(plot==null||plot.WasCollected||!plot.isActiveAndEnabled)continue;
            var trackers=plot._trackers;
            if(trackers==null)continue;
            var group=plot._region?.SceneGroup??player.SceneGroup;
            for(int t=0;t<trackers.Length&&Budget(slimes,fed);t++)
            {
                var tracker=trackers[t];
                if(tracker==null||tracker.WasCollected)continue;
                _types.Clear();
                tracker.GetTrackedIdentifiableTypes(_types);
                for(int k=0;k<_types.Count&&Budget(slimes,fed);k++)
                {
                    var type=_types[k];
                    if(type==null||type.TryCast<SlimeDefinition>()==null)continue;
                    _members.Clear();
                    tracker.GetTrackedItemsOfType(type,_members);
                    for(int m=0;m<_members.Count&&Budget(slimes,fed);m++)
                    {
                        var ident=_members[m];
                        if(ident==null||ident.WasCollected)continue;
                        var eater=ident.GetComponent<SlimeEat>();
                        if(eater==null||!_seen.Add(eater.Pointer))continue;
                        slimes++;
                        int outcome=Consider(eater,group,origin,clock);
                        if(outcome>0)hungry++;
                        if(outcome>1)fed++;
                    }
                }
            }
        }
        Slimes=slimes;HungrySlimes=hungry;FedLastTick=fed;Portions+=fed;
        Status=M27AutoFeedPolicy.Status(true,_corrals.Count,slimes,hungry,fed,Portions);
    }
    private static bool Budget(int examined,int fed)=>examined<M27AutoFeedPolicy.MaxExamined&&fed<M27AutoFeedPolicy.PortionsPerTick;

    // 0 = not hungry or out of range, 1 = hungry but not fed now, 2 = portion accepted.
    private int Consider(SlimeEat eater,SceneGroup group,Vector3 origin,float clock)
    {
        if(!Live(eater))return 0;
        if(!M27AutoFeedPolicy.Within((eater.transform.position-origin).sqrMagnitude))return 0;
        float hunger=eater._emotions.Get(SlimeEmotions.Emotion.HUNGER);
        if(!M27AutoFeedPolicy.Hungry(hunger,SlimeEmotions.HUNGRY_CUTOFF))return 0;
        if(_cooldowns.TryGetValue(eater.Pointer,out float until)&&!M27AutoFeedPolicy.Ready(clock,until))return 1;
        if(!CanChomp(eater))return 1;
        var food=Meal(eater.SlimeDefinition);
        if(food==null)return 1;
        if(!eater.WillNowEat(food)){Cool(eater.Pointer,clock+M27AutoFeedPolicy.DeclineCooldown);return 1;}
        bool accepted=Offer(eater,food,group);
        Cool(eater.Pointer,clock+(accepted?M27AutoFeedPolicy.SlimeCooldown:M27AutoFeedPolicy.DeclineCooldown));
        return accepted?2:1;
    }
    private static bool Live(SlimeEat eater)=>eater!=null&&!eater.WasCollected&&
        eater.gameObject.activeInHierarchy&&eater.gameObject.scene.IsValid()&&
        eater.gameObject.scene.isLoaded&&eater.SlimeDefinition!=null&&eater._emotions!=null;
    private static bool CanChomp(SlimeEat eater)=>eater.isActiveAndEnabled&&eater._isEatingEnabled&&eater.CanEat()&&
        eater._chomper!=null&&eater._chomper.CanChomp()&&
        float.IsFinite(eater._maxBiteDistance)&&eater._maxBiteDistance>0&&
        !eater.IsChomping()&&!eater.IsSleeping();

    private void Cool(IntPtr eater,float until)
    {
        if(_cooldowns.Count>=M27AutoFeedPolicy.MaxCooldowns&&!_cooldowns.ContainsKey(eater))_cooldowns.Clear();
        _cooldowns[eater]=until;
    }
    private void Prune(float clock)
    {
        if(_cooldowns.Count==0)return;
        _expired.Clear();
        foreach(var pair in _cooldowns)if(M27AutoFeedPolicy.Ready(clock,pair.Value))_expired.Add(pair.Key);
        foreach(var key in _expired)_cooldowns.Remove(key);
        _expired.Clear();
    }

    private void Scan()
    {
        _corrals.Clear();
        var plots=_app.Context?.GameModel?.landPlots;
        if(plots==null)return;
        // Same small authoritative registry as FarmTools; unloaded plots are skipped.
        foreach(var pair in plots)
        {
            if(_corrals.Count>=M27AutoFeedPolicy.MaxCorrals)break;
            var model=pair.Value;
            if(model==null||model.typeId!=LandPlot.Id.CORRAL)continue;
            var root=model.gameObj;
            if(root==null||root.WasCollected||!root.activeInHierarchy)continue;
            var plot=root.GetComponentInChildren<LandPlot>(true);
            if(plot!=null&&!plot.WasCollected&&plot.GetPlotId()==LandPlot.Id.CORRAL)_corrals.Add(plot);
        }
    }

    // Spawnable foods come from the creator catalog (category «Еда»).
    private void RefreshFoods()
    {
        var catalog=_app.Actors?.Catalog;
        int count=catalog?.Count??0;
        if(count==_catalogCount)return;
        _catalogCount=count;_foods.Clear();_meals.Clear();
        if(catalog==null)return;
        foreach(var entry in catalog)
            if(entry!=null&&entry.CanSpawn&&!entry.IsRadiant&&!entry.IsPrototype&&entry.Category=="Еда"&&entry.Type?.prefab!=null)
                _foods.Add(entry.Type.Pointer);
    }
    private IdentifiableType Meal(SlimeDefinition definition)
    {
        if(definition==null)return null;
        if(_meals.TryGetValue(definition.Pointer,out var cached))return cached;
        IdentifiableType chosen=null;
        var map=definition.Diet?.EatMap;
        _mealScratch.Clear();_typeScratch.Clear();
        for(int i=0;map!=null&&i<map.Count&&i<256;i++)
        {
            var entry=map[i];
            var food=entry?.EatsIdent;
            if(food==null)continue;
            _typeScratch.Add(food);
            _mealScratch.Add(new M27AutoFeedPolicy.Meal(entry.IsFavorite,entry.BecomesIdent!=null,_foods.Contains(food.Pointer)));
        }
        int index=M27AutoFeedPolicy.ChooseMeal(_mealScratch);
        if(index>=0)chosen=_typeScratch[index];
        _mealScratch.Clear();_typeScratch.Clear();
        _meals[definition.Pointer]=chosen;
        return chosen;
    }

    // Same native handoff as SlimeCareTools.FeedNow, without a vacpack source.
    private bool Offer(SlimeEat eater,IdentifiableType food,SceneGroup group)
    {
        Il2CppMonomiPark.SlimeRancher.DataModel.ActorModel model=null;
        GameObject portion=null;bool attempted=false;
        try
        {
            var forward=Vector3.ProjectOnPlane(eater.transform.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<.1f)forward=Vector3.forward;
            float reach=Mathf.Min(eater._maxBiteDistance*.6f,2.4f);
            model=_app.Context.GameModel.InstantiateActorModel(food,group,
                eater.transform.position+forward*reach+Vector3.up*.25f,Quaternion.identity);
            if(model==null)throw new InvalidOperationException("Порция не получила нативную модель.");
            portion=model.GetGameObject()??InstantiationHelpers.InstantiateActorFromModel(model);
            if(portion==null)throw new InvalidOperationException("Порция не получила нативный объект.");
            if(eater.ExtractOtherId(portion)!=food)throw new InvalidOperationException("Нативная порция не идентифицируется как выбранная еда.");
            // The normal chomp path owns animation, hunger, favourite bonus, plorts
            // and destruction. No direct emotion or plort writes here.
            attempted=true;
            if(eater.MaybeSpinAndChomp(portion,false))return true;
            Destroyer.DestroyActor(portion,"CreatorToolkit autofeed declined portion");
            return false;
        }
        catch
        {
            // After a native handoff attempt the portion belongs to the game.
            if(!attempted)
            {
                if(portion!=null&&!portion.WasCollected)Destroyer.DestroyActor(portion,"CreatorToolkit autofeed pre-handoff rollback");
                else if(model!=null)_app.Context.GameModel.DestroyIdentifiableModel(model);
            }
            throw;
        }
    }
}
