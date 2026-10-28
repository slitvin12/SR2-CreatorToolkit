using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Slime;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using UnityEngine;

namespace CreatorToolkit;

internal sealed class ActorTools
{
    internal sealed class Entry
    {
        internal IdentifiableType Type;
        internal string Name, Reference, Category, Family, Search;
        internal int PediaOrder = CatalogOrder.Unknown;
        internal CatalogOrder Order;
        internal string Prototype;
        internal string UnavailableReason;
        internal bool IsRadiant;
        internal string RadiantToken;
        internal Sprite RadiantIcon;
        internal string CatalogKey => IsRadiant&&RadiantCatalogPolicy.TryGet(RadiantToken,out var species)
            ?species.Key:RadiantCatalogPolicy.CatalogKey(Reference,IsRadiant);
        internal Sprite Icon => IsRadiant?RadiantIcon:Type?.Icon;
        internal bool IsPrototype=>!string.IsNullOrEmpty(Prototype);
        internal bool CanSpawn=>Type!=null&&string.IsNullOrEmpty(UnavailableReason);
        public override string ToString() => Name;
    }
    private readonly ToolkitMod _app;
    private readonly PrototypeTools _prototypes=new();
    internal readonly List<Entry> Catalog = new();
    internal int Generation { get; private set; }
    internal int CanonicalCount { get; private set; }
    internal string OrderingStatus { get; private set; } = "Ожидание Слаймопедии";
    internal int PrototypeApplied=>_prototypes.Applied;
    internal int PrototypeFaceted=>_prototypes.Faceted;
    private readonly List<GameObject> _owned = new();
    // Session-scoped, exceptional model-only rollback failures; no Tick retry.
    private readonly List<ActorModel> _failedRadiantModels = new();
    private float _nextCatalog;
    private sealed class GordoJob {internal Entry Entry;internal GameObject Snare;internal GadgetModel Model;internal IntPtr Session;internal int Frame;internal float Deadline;}
    private GordoJob _gordoJob;
    private readonly List<GameObject> _ownedGordoSnares=new();
    internal bool GordoSpawnPending=>_gordoJob!=null;
    internal string GordoSpawnStatus {get;private set;}="Гордо создаются по одному через нативную ловушку";
    private static bool IsGordo(IdentifiableType type)=>type?.prefab!=null&&type.prefab.GetComponent<GordoIdentifiable>()!=null;
    internal int OwnedCount
    {
        get { _owned.RemoveAll(go => go == null || go.WasCollected); _ownedGordoSnares.RemoveAll(go=>go==null||go.WasCollected);return _owned.Count+_failedRadiantModels.Count+_ownedGordoSnares.Count; }
    }

    internal ActorTools(ToolkitMod app) => _app = app;
    private static bool NeedsDroneModel(IdentifiableType type)
    {
        string id=type?.ReferenceId??"";
        // These native families require their dedicated drone model/station APIs.
        return id=="IdentifiableType.ExplorerDrone"||id=="IdentifiableType.RanchDrone"||id=="IdentifiableType.ComponentAcqDrone";
    }
    private static bool MatchesPrefabIdentity(IdentifiableType type)
    {
        if(type?.prefab==null)return false;
        // Liquid aliases (not shared slime/largo prefabs) can point at another
        // actor ID. Snow's alias points at Snowball and breaks native cleanup.
        if(!(type.ReferenceId??"").StartsWith("LiquidDefinition.",StringComparison.Ordinal))return true;
        return !IdentifiableType.TryGetId(type.prefab,out var prefabType)||prefabType==null||prefabType==type;
    }
    internal void Tick()
    {
        TickGordoSpawn();
        if (Catalog.Count == 0 && Time.unscaledTime >= _nextCatalog)
        {
            _nextCatalog = Time.unscaledTime + 5f;
            try { RefreshCatalog(); } catch (Exception e) { _app.Fail("catalog", e); }
        }
    }

    internal void RefreshCatalog()
    {
        var lookup = LookupDirector.GetIfReady();
        if (lookup == null || !lookup._initialized || lookup._identifiableTypeByRefId == null) return;
        var entries = new List<Entry>();
        // Items is the game's actual encyclopedia order, including locked
        // entries. Reading it does not unlock or alter anything in the save.
        var pediaRanks = new Dictionary<IntPtr,int>();
        var typeRanks = new Dictionary<IntPtr,int>();
        var radiantIcons = new Dictionary<IntPtr,Sprite>();
        var conflictingRadiantIcons = new HashSet<IntPtr>();
        var categories = _app.Context?.PediaDirector?.Categories ?? lookup.PediaCategories;
        int order = 0;
        int categoryCount=categories==null?0:categories.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<IPediaRuntimeCategory>>().Count;
        for(int i=0;i<categoryCount;i++)
        {
            var runtime = categories[i]?.TryCast<PediaRuntimeCategory>();
            var items = runtime?.Items;
            int itemCount=items==null?0:items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<PediaEntry>>().Count;
            for(int j=0;j<itemCount;j++)
            {
                var item=items[j];if(item==null||pediaRanks.ContainsKey(item.Pointer))continue;
                int rank=order++;pediaRanks.Add(item.Pointer,rank);
                var identifiable=item.TryCast<IdentifiablePediaEntry>()?.IdentifiableType;
                if(identifiable!=null&&!typeRanks.ContainsKey(identifiable.Pointer))typeRanks.Add(identifiable.Pointer,rank);
                // Variant artwork is resolved only in this existing refresh census.
                // Missing or conflicting variant art stays null, never ordinary Type.Icon.
                try
                {
                    var radiantPedia=item.TryCast<RadiantSlimePediaEntry>();
                    var radiantDefinition=radiantPedia?.SlimeDefinition;
                    if(radiantDefinition!=null&&!conflictingRadiantIcons.Contains(radiantDefinition.Pointer))
                    {
                        var icon=radiantPedia.Icon;
                        if(icon!=null)
                        {
                            if(radiantIcons.TryGetValue(radiantDefinition.Pointer,out var previous)&&previous.Pointer!=icon.Pointer)
                            { radiantIcons.Remove(radiantDefinition.Pointer);conflictingRadiantIcons.Add(radiantDefinition.Pointer); }
                            else radiantIcons[radiantDefinition.Pointer]=icon;
                        }
                    }
                }
                catch { /* Unready variant art must not abort the ordinary catalogue. */ }
            }
        }
        var groups=new Dictionary<string,IdentifiableTypeGroup>(StringComparer.Ordinal);
        foreach(var group in Resources.FindObjectsOfTypeAll<IdentifiableTypeGroup>())
            if(group!=null&&!groups.ContainsKey(group.name))groups.Add(group.name,group);
        bool In(string group,IdentifiableType type)=>groups.TryGetValue(group,out var g)&&g.IsMember(type);
        int PediaRank(IdentifiableType type)
        {
            if(typeRanks.TryGetValue(type.Pointer,out int r))return r;
            PediaEntry item=null;
            var nativeIndex=_app.Context?.PediaDirector?._identDict;
            if(nativeIndex!=null)nativeIndex.TryGetValue(type,out item);
            return item!=null&&pediaRanks.TryGetValue(item.Pointer,out r)?r:CatalogOrder.Unknown;
        }
        var seen = new HashSet<IntPtr>();
        var types = new List<IdentifiableType>();
        var registered=new HashSet<IntPtr>();
        foreach (var pair in lookup._identifiableTypeByRefId) if (pair.Value != null){types.Add(pair.Value);registered.Add(pair.Value.Pointer);}
        var definitions=GameContext.Instance?.SlimeDefinitions?.Slimes;
        for(int i=0;definitions!=null&&i<definitions.Length;i++)if(definitions[i]!=null)types.Add(definitions[i]);
        foreach(var definition in Resources.FindObjectsOfTypeAll<SlimeDefinition>())if(definition!=null)types.Add(definition);
        foreach (IdentifiableType type in types)
        {
            if (type == null || type.IsPlayer || (type.IsSceneObject&&!IsGordo(type)) || !seen.Add(type.Pointer)) continue;
            if(type.prefab==null&&type.TryCast<SlimeDefinition>()==null)continue;
            // Gadgets/decorations require GadgetModel + placement APIs, never BasicActorModel.
            // They are not part of this slime/food/plort/resource actor spawner.
            if (type.TryCast<GadgetDefinition>() != null || NeedsDroneModel(type) || (type.prefab!=null&&!MatchesPrefabIdentity(type))) continue;
            string reference = type.ReferenceId ?? type.name;
            string name = type.name;
            try { string localized = type.LocalizedName?.GetLocalizedString(); if (!string.IsNullOrWhiteSpace(localized) && !localized.StartsWith("!!")) name = localized; } catch { }
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("!!")) name = reference;
            name = name.Replace("_", " ").Trim();
            string identity = (reference + " " + type.name + " " + (type.groupType != null ? type.groupType.name : "")).ToLowerInvariant();
            var slime=type.TryCast<SlimeDefinition>();
            string family=In("FruitGroup",type)?"Фрукты":In("VeggieGroup",type)?"Овощи":In("MeatGroup",type)?"Мясо":
                In("FoodGroup",type)||In("NectarFoodGroup",type)||type.IsAnimal?"Прочее питание":
                In("CraftGroup",type)||In("RefineryGroup",type)?"Материалы":"Прочее";
            string category = IsGordo(type)?"Гордо":type.IsPlort ? "Плорты"
                : slime!=null&&slime.IsLargo ? "Ларго"
                : slime!=null ? "Слаймы"
                : family=="Фрукты"||family=="Овощи"||family=="Мясо"||family=="Прочее питание" ? "Еда"
                : "Ресурсы";
            int primary=PediaRank(type),secondary=0;
            if(slime!=null&&slime.IsLargo)
            {
                primary=secondary=CatalogOrder.Unknown;var parents=slime.BaseSlimes;
                for(int i=0;parents!=null&&i<parents.Length;i++)
                {
                    if(parents[i]==null)continue;int rank=PediaRank(parents[i]);
                    if(rank<primary){secondary=primary;primary=rank;}else if(rank<secondary)secondary=rank;
                }
            }
            string unavailable=type.prefab==null?"У типа нет нативного префаба.":!registered.Contains(type.Pointer)?"Тип не зарегистрирован в нативном каталоге этой версии.":reference=="SlimeDefinition.GlitchTarr"?"Незавершённый глитч-варр: нативный актор не принимает модель в этой версии.":null;
            entries.Add(new Entry { Type = type, Name = name+(unavailable!=null?" · неготовый":""), Reference = reference, Category = category, Family=family,
                PediaOrder=primary,Order=new CatalogOrder(CatalogOrder.CategoryRank(category),CatalogOrder.FamilyRank(category,family),primary,secondary,reference),
                Search = (name + " " + identity).ToLowerInvariant(),UnavailableReason=unavailable });
        }
        // Current shipping assets contain neither Rad nor Mosaic definitions.
        // Honest visual previews use a known native save-safe base, never fake IDs.
        var pink=entries.Find(e=>e.Type.TryCast<SlimeDefinition>()!=null&&e.Type.name=="Pink");
        if(pink!=null)
        {
            foreach(var prototype in new[]{("rad","Радиоактивный слайм · прототип","rad radioactive радиоактивный"),("mosaic","Мозаичный слайм · прототип","mosaic мозаичный")})
                if(!entries.Exists(e=>e.Type.TryCast<SlimeDefinition>()!=null&&string.Equals(e.Type.name,prototype.Item1=="rad"?"Rad":"Mosaic",StringComparison.OrdinalIgnoreCase)))
                    entries.Add(new Entry{Type=pink.Type,Name=prototype.Item2,Reference="CreatorToolkit.VisualPreview."+prototype.Item1,Category="Эксперименты",Family="Визуальный прототип",Order=new CatalogOrder(5,0,CatalogOrder.Unknown,0,prototype.Item1),Search=(prototype.Item2+" "+prototype.Item3).ToLowerInvariant(),Prototype=prototype.Item1});
        }
        AddRadiantEntries(entries,radiantIcons);
        entries.Sort((a,b)=>a.Order.CompareTo(b.Order));
        CanonicalCount=entries.FindAll(e=>!e.IsPrototype&&!e.IsRadiant&&e.PediaOrder!=CatalogOrder.Unknown).Count;
        OrderingStatus=pediaRanks.Count>0?"Порядок Слаймопедии · "+CanonicalCount+" типов":"Порядок Слаймопедии недоступен · непроверенные типы по ID";
        Catalog.Clear();
        Catalog.AddRange(entries);
        Generation++;
        _app.Notify("Каталог: " + Catalog.Count + " записей, включая явно помеченные прототипы и неготовые типы.");
    }

    // Resolve only exact registry identities. Never infer a species from prefab/order.
    private static SlimeDefinition ResolveRadiantBase(RadiantCatalogPolicy.Species species, out string reason)
    {
        var lookup=LookupDirector.GetIfReady();
        if(lookup==null||!lookup._initialized||lookup._identifiableTypeByRefId==null)
        { reason="Нативный каталог ещё не готов."; return null; }
        var candidates=new HashSet<IntPtr>();
        SlimeDefinition definition=null;
        foreach(var pair in lookup._identifiableTypeByRefId)
        {
            var type=pair.Value;if(type==null)continue;
            var slime=type.TryCast<SlimeDefinition>();
            if(!string.Equals(pair.Key,species.Reference,StringComparison.Ordinal)&&
                !string.Equals(type.ReferenceId,species.Reference,StringComparison.Ordinal)&&
                !(slime!=null&&string.Equals(slime.name,species.Token,StringComparison.Ordinal)))continue;
            if(candidates.Add(type.Pointer))definition=slime;
        }
        IdentifiableType registered=null;
        lookup._identifiableTypeByRefId.TryGetValue(species.Reference,out registered);
        bool exact=definition!=null&&registered!=null&&registered.Pointer==definition.Pointer&&
            string.Equals(definition.ReferenceId,species.Reference,StringComparison.Ordinal)&&
            string.Equals(definition.name,species.Token,StringComparison.Ordinal)&&!definition.IsPlayer&&!definition.IsSceneObject;
        // Do not touch prefab components for an absent/ambiguous/wrong identity.
        reason=RadiantCatalogPolicy.Availability(candidates.Count,exact,definition!=null&&definition.IsLargo,
            false,false,0);
        if(candidates.Count!=1||!exact||definition.IsLargo)return null;
        var prefab=definition.prefab;
        var components=prefab==null?null:prefab.GetComponents<SlimeRadiant>();
        reason=RadiantCatalogPolicy.Availability(1,true,false,definition.RadiantBase!=null,prefab!=null,
            components==null?0:components.Length);
        return definition;
    }

    private static void AddRadiantEntries(List<Entry> entries,Dictionary<IntPtr,Sprite> icons)
    {
        int index=0;
        foreach(var species in RadiantCatalogPolicy.Ordered)
        {
            // A missing type still has a stable catalogue key, not a fake native ID.
            var entry=new Entry { IsRadiant=true,RadiantToken=species.Token,
                Name="Лучистый · "+species.Russian,Category="Слаймы",Family="Лучистые",
                Order=new CatalogOrder(CatalogOrder.CategoryRank("Слаймы"),1,index++,0,species.Key),
                Search=species.Search(species.Russian) };
            try
            {
                var definition=ResolveRadiantBase(species,out string reason);
                entry.Type=definition;
                entry.Reference=definition?.ReferenceId;
                entry.UnavailableReason=reason;
                string name=species.Russian;
                if(definition!=null)
                {
                    try
                    {
                        string localized=definition.LocalizedName?.GetLocalizedString();
                        if(!string.IsNullOrWhiteSpace(localized)&&!localized.StartsWith("!!"))name=localized;
                    }
                    catch { /* Honest species-name fallback, not ordinary artwork. */ }
                    if(icons.TryGetValue(definition.Pointer,out var icon))entry.RadiantIcon=icon;
                }
                entry.Name="Лучистый · "+name;
                entry.Search=species.Search(name);
            }
            catch(Exception e)
            { entry.UnavailableReason="Нативный лучистый вариант недоступен: "+e.Message; }
            entries.Add(entry);
        }
    }

    private SlimeDefinition RequireRadiantEntry(Entry entry)
    {
        if(entry==null||!entry.IsRadiant||entry.IsPrototype||!Catalog.Contains(entry)||
            !RadiantCatalogPolicy.TryGet(entry.RadiantToken,out var species))
            throw new InvalidOperationException("Лучистая запись устарела или отсутствует в текущем каталоге.");
        var definition=ResolveRadiantBase(species,out string reason);
        if(reason!=null)throw new InvalidOperationException(reason);
        if(entry.Type==null||definition==null||entry.Type.Pointer!=definition.Pointer||
            !string.Equals(entry.Reference,definition.ReferenceId,StringComparison.Ordinal)||
            !string.Equals(entry.CatalogKey,species.Key,StringComparison.Ordinal))
            throw new InvalidOperationException("Нативная идентичность лучистого вида изменилась; обновите каталог.");
        return definition;
    }

    // Batch is committed to ownership only after every native variant verifies.
    // On failure roll back the in-flight actor AND earlier actors in this batch.
    private void SpawnRadiant(Entry entry,SceneContext ctx,TeleportablePlayer player,Camera cam,int count,Vector3 origin)
    {
        if(ctx.GameModel==null)throw new InvalidOperationException("Игровая модель недоступна.");
        var pending=new List<(ActorModel Model,GameObject Object)>();
        ActorModel currentModel=null;
        GameObject currentObject=null;
        try
        {
            for(int i=0;i<count;i++)
            {
                var definition=RequireRadiantEntry(entry); // Revalidate before each mutation.
                Vector3 offset=count==1?Vector3.zero:cam.transform.right*((i%5-2)*0.28f)+Vector3.up*(i/5*0.22f);
                currentModel=ctx.GameModel.InstantiateActorModel(definition,player.SceneGroup,origin+offset,Quaternion.identity);
                if(currentModel==null)throw new InvalidOperationException("Не удалось создать лучистый ActorModel.");
                currentObject=currentModel.GetGameObject()??InstantiationHelpers.InstantiateActorFromModel(currentModel);
                if(currentObject==null)throw new InvalidOperationException("Не удалось создать лучистый актор.");
                var components=currentObject.GetComponents<SlimeRadiant>();
                if(components==null||components.Length!=1||components[0]==null)
                    throw new InvalidOperationException("Созданный актор не содержит единственный SlimeRadiant.");
                var radiant=components[0];
                radiant.SetRadiant(definition);
                var applied=radiant.RadiantBaseType;
                if(!radiant.IsRadiant||applied==null||applied.Pointer!=definition.Pointer)
                    throw new InvalidOperationException("Нативный актор не подтвердил точный лучистый вариант.");
                pending.Add((currentModel,currentObject));
                currentModel=null;currentObject=null;
            }
            foreach(var actor in pending){_owned.Add(actor.Object);_app.Coop?.AnnounceSpawn(actor.Object);}
            _app.Notify("Создано: "+entry.Name+" × "+pending.Count+". Только эти объекты доступны для очистки."+
                (_app.Coop?.InSession==true?" В коопе друзья видят обычный вариант: сияние не синхронизируется.":""));
        }
        catch(Exception creationError)
        {
            var errors=new List<Exception>{creationError};
            // Remove the success commit first. A failed native rollback is retained
            // ONLY as cleanup-retry ownership; the spawn still throws below.
            if(currentObject!=null)_owned.Remove(currentObject);
            if(!RollbackRadiantActor(ctx,currentModel,currentObject,errors)&&
                currentObject!=null&&!currentObject.WasCollected&&!_owned.Contains(currentObject))
                _owned.Add(currentObject);
            for(int i=pending.Count-1;i>=0;i--)
            {
                var actor=pending[i];_owned.Remove(actor.Object);
                if(!RollbackRadiantActor(ctx,actor.Model,actor.Object,errors)&&
                    actor.Object!=null&&!actor.Object.WasCollected&&!_owned.Contains(actor.Object))
                    _owned.Add(actor.Object);
            }
            if(errors.Count>1)throw new AggregateException("Лучистый спавн отменён; нативный rollback сообщил ошибки.",errors);
            throw;
        }
    }

    private bool RollbackRadiantActor(SceneContext ctx,ActorModel model,GameObject go,List<Exception> errors)
    {
        // True means native cleanup/object-destruction calls returned successfully,
        // not a runtime certification. False keeps a surviving object retryable.
        if(go!=null)
        {
            try { Destroyer.DestroyActor(go,"CreatorToolkit radiant spawn rollback"); return true; }
            catch(Exception e) { errors.Add(e); }
        }
        bool modelRemoved=false;
        try { if(model!=null)ctx.GameModel.DestroyIdentifiableModel(model); modelRemoved=true; }
        catch(Exception e) { errors.Add(e); }
        // Never destroy the object blindly while model removal is unconfirmed.
        if(!modelRemoved)
        {
            // A surviving GO is retained by the caller in _owned, never twice.
            // Without a GO keep the exact model for explicit Cleanup in this session.
            if(go==null&&model!=null&&!_failedRadiantModels.Exists(m=>m.Pointer==model.Pointer))
                _failedRadiantModels.Add(model);
            return false;
        }
        try { if(go!=null)UnityEngine.Object.Destroy(go); return true; }
        catch(Exception e) { errors.Add(e); return false; }
    }

    internal void Spawn(Entry entry, int requested, float lateral = 0f, float distance = 2.5f)
    {
        if(entry?.IsRadiant==true&&!entry.CanSpawn)
            throw new InvalidOperationException((entry.UnavailableReason??"Нативный лучистый тип недоступен.")+" Создание отключено.");
        if (entry == null || entry.Type == null) throw new InvalidOperationException("Выберите объект в каталоге.");
        if(!entry.CanSpawn)throw new InvalidOperationException(entry.UnavailableReason+" Создание отключено для защиты сейва.");
        if(IsGordo(entry.Type))
        {
            // Ranching Together replicates actors, not SnareModel gordos: a co-op gordo would exist only here.
            if(_app.Coop?.InSession==true)throw new InvalidOperationException("Кооп: Гордо создаются через нативную ловушку, которую "+MultiplayerPolicy.ModName+" не синхронизирует. Доступно в одиночной игре.");
            BeginGordoSpawn(entry);return;
        }
        if(entry.IsRadiant)RequireRadiantEntry(entry);
        if (entry.Type.TryCast<GadgetDefinition>() != null)
            throw new InvalidOperationException("Декорации требуют штатного размещения GadgetModel и не поддерживаются акторным спавнером.");
        if(NeedsDroneModel(entry.Type))
            throw new InvalidOperationException("Дрон требует своей модели и станции; этот каталог создаёт слаймов, еду, плорты и ресурсы.");
        if(!MatchesPrefabIdentity(entry.Type))
            throw new InvalidOperationException("Тип префаба отличается от записи каталога. Выберите основную запись этого предмета.");
        if (entry.Type.IsPlayer || entry.Type.IsSceneObject || entry.Type.prefab == null)
            throw new InvalidOperationException("Этот тип привязан к нативной сцене и не является свободно создаваемым актором.");
        SceneContext ctx = _app.Context;
        Camera cam = Camera.main;
        if (ctx == null || cam == null) throw new InvalidOperationException("Игровая камера недоступна.");
        TeleportablePlayer player = ctx.Player.GetComponentInChildren<TeleportablePlayer>(true);
        if (player == null || player.SceneGroup == null) throw new InvalidOperationException("Группа сцены игрока недоступна.");
        int count = SafetyPolicy.SpawnCount(requested);
        if(!float.IsFinite(distance)||!float.IsFinite(lateral))throw new InvalidOperationException("Невалидное смещение спавна.");
        Vector3 origin = cam.transform.position + cam.transform.forward * Mathf.Clamp(distance,1.5f,12f) + cam.transform.right*Mathf.Clamp(lateral,-3f,3f);
        if(entry.IsRadiant)
        { SpawnRadiant(entry,ctx,player,cam,count,origin);return; }
        int created = 0;
        for (int i = 0; i < count; i++)
        {
            Vector3 offset = count == 1 ? Vector3.zero : cam.transform.right * ((i % 5 - 2) * 0.28f) + Vector3.up * (i / 5 * 0.22f);
            // Native GameModel registration makes spawned actors real game actors, not unregistered prefab clones.
            var model = ctx.GameModel.InstantiateActorModel(entry.Type, player.SceneGroup, origin + offset, Quaternion.identity);
            if (model == null) throw new InvalidOperationException("Не удалось создать ActorModel: " + entry.Name);
            GameObject go;
            try
            {
                go = model.GetGameObject() ?? InstantiationHelpers.InstantiateActorFromModel(model);
                if (go == null) throw new InvalidOperationException("Не удалось создать актор: " + entry.Name);
            }
            catch
            {
                // Do not leave an untracked registered model behind after prefab failure.
                ctx.GameModel.DestroyIdentifiableModel(model);
                throw;
            }
            _owned.Add(go);
            if(entry.IsPrototype)_prototypes.Apply(go,entry.Prototype);
            _app.Coop?.AnnounceSpawn(go); // Co-op: SR2MP replicates the actor to everyone (no-op offline).
            created++;
        }
        _app.Notify("Создано: " + entry.Name + " × " + created + ". Только эти объекты доступны для очистки."+
            (entry.IsPrototype&&_app.Coop?.InSession==true?" Настройки прототипа видны только вам.":""));
    }

    // Gordo is NOT an ActorModel. The native SnareModel owns its persistence,
    // feeding participants and exact species. One deferred transaction per request.
    private void BeginGordoSpawn(Entry entry)
    {
        if(_gordoJob!=null)throw new InvalidOperationException("Гордо уже создаётся. Дождитесь завершения.");
        if(!_app.Ready||_app.Map.Busy||!Catalog.Contains(entry)||!entry.CanSpawn||!IsGordo(entry.Type))
            throw new InvalidOperationException("Запись Гордо устарела или мир загружается.");
        var player=_app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true);var cam=Camera.main;
        if(player?.SceneGroup==null||cam==null)throw new InvalidOperationException("Игровая сцена недоступна.");
        GadgetDefinition snareType=null;
        foreach(var type in NativeEnumerable.Items(_app.Context.GadgetDirector.GetAllGadgets()))
        {var d=type?.TryCast<GadgetDefinition>();if(d?.prefab!=null&&d.prefab.GetComponentInChildren<GordoSnare>(true)!=null)
         {if(snareType==null||string.CompareOrdinal(d.ReferenceId,snareType.ReferenceId)<0)snareType=d;}}
        if(snareType==null)throw new InvalidOperationException("Нативный тип ловушки Гордо не найден.");
        Vector3 forward=Vector3.ProjectOnPlane(cam.transform.forward,Vector3.up).normalized;
        Vector3 target=player.transform.position+forward*9f;
        if(!_app.Player.TryMapGround(target,false,out var ground))throw new InvalidOperationException("Перед игроком нет подтверждённой земли для Гордо.");
        foreach(var other in UnityEngine.Object.FindObjectsOfType<GordoEat>())
            if(other!=null&&Vector3.Distance(other.transform.position,ground)<8f)throw new InvalidOperationException("Другой Гордо слишком близко; выберите свободное место.");
        GameObject go=null;GadgetModel model=null;
        try
        {
            _app.Context.GameModel.InstantiateGadget(snareType,player.SceneGroup,ground,Quaternion.identity,false,out go,out model);
            if(go==null||model?.TryCast<SnareModel>()==null||go.GetComponentInChildren<GordoSnare>(true)==null)
                throw new InvalidOperationException("Нативная ловушка не создала SnareModel.");
            _gordoJob=new GordoJob{Entry=entry,Snare=go,Model=model,Session=_app.Session,Frame=Time.frameCount,Deadline=Time.unscaledTime+8f};
            GordoSpawnStatus="Подготовка Гордо · 1 экземпляр";_app.Notify(GordoSpawnStatus);
        }
        catch{if(go!=null)Destroyer.DestroyGadget(go,"CreatorToolkit failed Gordo preparation");else if(model!=null)_app.Context.GameModel.DestroyIdentifiableModel(model);throw;}
    }
    private void TickGordoSpawn()
    {
        var job=_gordoJob;if(job==null||Time.frameCount<=job.Frame+1)return;
        try
        {
            if(job.Session!=_app.Session||!_app.Ready||Time.unscaledTime>job.Deadline)throw new InvalidOperationException("Подготовка Гордо прервана или истёк срок ожидания.");
            var snare=job.Snare?.GetComponentInChildren<GordoSnare>(true);var model=job.Model?.TryCast<SnareModel>();
            if(snare==null||model==null||snare._model==null)
            {if(Time.unscaledTime<job.Deadline)return;throw new InvalidOperationException("Модель ловушки ещё не готова.");}
            model.SnareGordo(job.Entry.Type);
            if(!model.HasSnaredGordo()||model.gordoTypeId!=job.Entry.Type||!snare.HasSnaredGordo())
                throw new InvalidOperationException("Нативная ловушка не подтвердила точный тип Гордо.");
            _ownedGordoSnares.Add(job.Snare);_gordoJob=null;
            GordoSpawnStatus="Создан Гордо · "+job.Entry.Name+" · на нативной ловушке";_app.Notify(GordoSpawnStatus);
        }
        catch(Exception e)
        {
            _gordoJob=null;GordoSpawnStatus="Создание Гордо отменено: "+e.Message;
            if(job.Session==_app.Session&&job.Snare!=null&&!job.Snare.WasCollected)
            {try{Destroyer.DestroyGadget(job.Snare,"CreatorToolkit Gordo transaction rollback");}catch(Exception cleanup){_ownedGordoSnares.Add(job.Snare);_app.Fail("Gordo rollback",cleanup);}}
            _app.Fail("Gordo spawn",e);
        }
    }

    internal void Cleanup()
    {
        if(_gordoJob!=null)throw new InvalidOperationException("Дождитесь завершения создания Гордо.");
        int removed = 0;
        for(int i=_ownedGordoSnares.Count-1;i>=0;i--)
        {var go=_ownedGordoSnares[i];if(go==null||go.WasCollected){_ownedGordoSnares.RemoveAt(i);continue;}
         try{Destroyer.DestroyGadget(go,"CreatorToolkit owned Gordo snare cleanup");_ownedGordoSnares.RemoveAt(i);removed++;}
         catch(Exception e){_app.Fail("cleanup Gordo snare",e);}}
        var failed = new List<GameObject>();
        foreach (GameObject go in _owned)
        {
            if (go == null || go.WasCollected) continue;
            try { Destroyer.DestroyActor(go, "CreatorToolkit owned actor cleanup"); removed++; }
            catch (Exception e) { failed.Add(go); _app.Fail("cleanup", e); }
        }
        _owned.Clear();
        _owned.AddRange(failed);
        var failedModels = new List<ActorModel>();
        foreach(var model in _failedRadiantModels)
        {
            try
            {
                var gameModel=_app.Context?.GameModel;
                if(gameModel==null)throw new InvalidOperationException("Игровая модель недоступна для очистки оставшейся лучистой модели.");
                gameModel.DestroyIdentifiableModel(model);
                removed++;
            }
            catch(Exception e) { failedModels.Add(model); _app.Fail("cleanup radiant model",e); }
        }
        _failedRadiantModels.Clear();
        _failedRadiantModels.AddRange(failedModels);
        int failedCount=failed.Count+failedModels.Count;
        string result = "Удалено созданных модом объектов: " + removed + ". Нативные объекты мира не затронуты.";
        if (failedCount == 0) { _prototypes.Clear(); _app.Notify(result); }
        else _app.Status = result + " Не удалось удалить: " + failedCount + " (моделей без объекта: " + failedModels.Count + "); можно повторить очистку.";
    }

    internal GameObject SpawnAtForTest(Entry entry,Vector3 position)
    {
        _app.Audit.GuardStressCopy();
        if(entry==null||!entry.CanSpawn||entry.IsPrototype||entry.IsRadiant||!MatchesPrefabIdentity(entry.Type)||
            !ExplorerPolicy.Coordinates(position.x,position.y,position.z))
            throw new InvalidOperationException("Owned native actor fixture is invalid.");
        var player=_app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true);
        var model=_app.Context.GameModel.InstantiateActorModel(entry.Type,player.SceneGroup,position,Quaternion.identity);
        if(model==null)throw new InvalidOperationException("Owned actor fixture model was not registered.");
        try
        {
            var go=model.GetGameObject()??InstantiationHelpers.InstantiateActorFromModel(model);
            if(go==null)throw new InvalidOperationException("Owned actor fixture object was not instantiated.");
            _owned.Add(go);return go;
        }
        catch{_app.Context.GameModel.DestroyIdentifiableModel(model);throw;}
    }

    private CurrencyDefinition Newbucks()
    {
        var list = LookupDirector.GetIfReady()?.CurrencyList;
        if (list == null) throw new InvalidOperationException("Список валют недоступен.");
        var currency = list.GetDefaultCurrency();
        if (currency == null) throw new InvalidOperationException("Нью-баксы не найдены.");
        return currency;
    }

    internal int Currency()
    {
        try { return _app.Context.PlayerState.GetCurrency(Newbucks().Cast<ICurrency>()); } catch { return 0; }
    }

    internal void AddCurrency(int requested, bool setMaximum = false)
    {
        var player = _app.Context.PlayerState;
        var currency = Newbucks().Cast<ICurrency>();
        int current = player.GetCurrency(currency);
        int maximum = player._model.maxCurrency;
        int delta = SafetyPolicy.CurrencyDelta(current, setMaximum ? int.MaxValue : requested, maximum);
        if (delta > 0) player.AddCurrency(currency, delta, false, null);
        _app.Notify("Нью-баксы: " + player.GetCurrency(currency) + " / " + maximum);
    }

    internal bool CanStore(Entry entry,int index)
    {
        if(entry?.IsRadiant==true)return false; // Native radiant ammo metadata is not implemented.
        var ammo=_app.Context?.PlayerState?.Ammo;
        if(entry?.Type==null||!entry.CanSpawn||entry.IsPrototype||ammo?.Slots==null||index<0||index>=ammo.Slots.Length)return false;
        var slot=ammo.Slots[index];
        return slot!=null&&slot.IsUnlocked&&slot.Definition!=null&&slot.Definition.IsAllowed(entry.Type);
    }

    internal void RequestAmmo(Entry entry)
    {
        if (entry == null) { _app.Notify("Выберите ресурс."); return; }
        if(entry.IsRadiant)throw new InvalidOperationException("Лучистый вариант пока нельзя выдавать в вакпак без нативной metadata.");
        var ammo = _app.Context.PlayerState.Ammo;
        if (ammo == null || ammo.Slots == null) throw new InvalidOperationException("Вакпак ещё не инициализирован.");
        int index = ammo._selectedAmmoIdx;
        if (index < 0 || index >= ammo.Slots.Length) throw new InvalidOperationException("Выберите доступный слот вакпака.");
        var slot = ammo.Slots[index];
        if (!slot.IsUnlocked) throw new InvalidOperationException("Выбранный слот ещё закрыт.");
        if(!CanStore(entry,index))throw new InvalidOperationException("Этот ресурс несовместим с выбранным слотом.");
        Action fill = () => FillAmmo(entry, index);
        if (slot.Count > 0 && slot.Id != entry.Type)
            _app.Confirm("Заменить содержимое слота?", "Содержимое выбранного слота будет заменено на " + (ModInterop.CustomVacLimits ? "«" + entry.Name + "» до лимита слота" : "100 шт. «" + entry.Name + "»") + ". Изменение попадёт в сейв.", fill);
        else _app.Queue("fill ammo", fill);
    }

    internal void FillAmmoForTest(Entry entry, int index) => FillAmmo(entry, index, false);

    private void FillAmmo(Entry entry, int index, bool notify = true)
    {
        if(entry?.IsRadiant==true)throw new InvalidOperationException("Выдача лучистого варианта в вакпак отключена: metadata не реализована.");
        var ammo = _app.Context.PlayerState.Ammo;
        if (index < 0 || index >= ammo.Slots.Length) throw new InvalidOperationException("Слот больше не существует.");
        var slot = ammo.Slots[index];
        if (!slot.IsUnlocked) throw new InvalidOperationException("Слот закрыт.");
        if(!CanStore(entry,index))throw new InvalidOperationException("Этот ресурс несовместим с выбранным слотом. Содержимое не изменено.");
        var before = slot.Metadata;
        var beforeAppearance = slot.Appearance;
        int beforeCount = slot.Count;
        int target = ModInterop.FillTarget(slot); // 100, or the slot's own limit with a slot-limit mod (Vacuum Modifications…)
        try
        {
            ammo.Clear(index);
            if (!ammo.MaybeAddResource(entry.Type, index, target, true))
                throw new InvalidOperationException("Этот тип нельзя хранить в выбранном слоте.");
            // Use the native Count setter (including its callbacks), not a raw backing-field write.
            slot.Count = target;
            if (ammo.GetSlotCount(index) != target) throw new InvalidOperationException("Нативный слот не принял " + target + " шт.");
        }
        catch
        {
            slot.SetFromMetadata(before);
            slot.Appearance = beforeAppearance;
            slot.Count = beforeCount;
            throw;
        }
        if (notify) _app.Notify("Слот " + (index + 1) + ": " + entry.Name + " × " + target + (ModInterop.CustomVacLimits ? " (лимит слота)." : " (может превышать штатную ёмкость)."));
    }

    internal void ResetTracking()
    {
        _gordoJob=null;_ownedGordoSnares.Clear();
        _prototypes.Clear();
        _owned.Clear(); // Never carry ownership into another save, and never delete a user's native actors.
        _failedRadiantModels.Clear(); // Forget failed models on session/save change; never destroy across sessions.
        Catalog.Clear();
        Generation++;
        _nextCatalog = 0f;
    }
}
