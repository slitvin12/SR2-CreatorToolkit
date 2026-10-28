using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Input;
using Il2CppMonomiPark.SlimeRancher.Player.PlayerItems;
using Il2CppMonomiPark.SlimeRancher.World;
using UnityEngine;

namespace CreatorToolkit;

// Placement delegates registration, inventory, linked portals, analytics and
// effects to the game's GadgetItem. Never spawn a gadget through ActorModel.
internal sealed class GadgetTools
{
    internal sealed class Entry
    {
        internal GadgetDefinition Definition;
        internal string Name, Reference, Search;
        internal int Available;
    }
    private readonly ToolkitMod _app;
    internal readonly List<Entry> Catalog = new();
    private IntPtr _session;
    private sealed class Placement
    {
        internal Entry Entry;
        internal Vector3 Point,FootprintPosition;
        internal Quaternion Rotation,FootprintRotation;
        internal IntPtr Session,ItemToken;
        internal Gadget OldLast,OldBase;
        internal GadgetDefinition OldLocked;
        internal int OldSelectedSlot,TemporarySelectedSlot;
        internal bool SelectionChanged;
        internal HashSet<IntPtr> BeforeModels;
        internal int InventoryBefore,ModelCountBefore;
        internal Vector3 Normal;
        internal bool OldMode,ModeChanged,OldLinked,OldLookedOff,OldValid,OldGrounded,OldBlocked,PreviewCreated,FootprintCaptured,EventIssued;
        internal float Deadline;
        internal int ArmedFrame;
        internal int FreeSpendsBefore;
    }
    // One short native transaction only. Item/owner are resolved afresh; the
    // optional old gadget is borrowed for restoration and retired on finish.
    private Placement _pending;
    internal bool PlacementPending=>_pending!=null;
    internal bool LastPlacementSucceeded {get;private set;}
    internal Vector3 LastPlacementPoint {get;private set;}
    internal Vector3 LastPlacementNormal {get;private set;}
    internal int Placed { get; private set; }
    internal string Status { get; private set; } = "Каталог гаджетов ещё не открыт";
    internal int PreviewRoots => _pending?.PreviewCreated==true?1:0;
    internal GadgetTools(ToolkitMod app) => _app = app;
    private PlayerItemController NativeOwner
    {
        get
        {
            if(!_app.Ready)return null;
            PlayerItemController found=null;
            // The weapon/item rig is not necessarily below SceneContext.Player.
            // Resolve the typed native owner by the exact live PlayerState,
            // never pick a prefab, another loaded player, or an arbitrary item.
            foreach(var owner in UnityEngine.Object.FindObjectsOfType<PlayerItemController>(true))
            {
                if(owner==null||owner.WasCollected||owner._playerState!=_app.Context.PlayerState||
                    !owner.gameObject.scene.IsValid()||!owner.gameObject.scene.isLoaded)continue;
                if(found!=null&&found.Pointer!=owner.Pointer)
                    throw new InvalidOperationException("Найдено несколько владельцев инструмента этого игрока; размещение отменено.");
                found=owner;
            }
            return found;
        }
    }
    internal GadgetItem NativeItem => NativeOwner?.GadgetItem;
    internal bool InfiniteBuilder => InfiniteBuildHooks.Enabled;
    internal string DisplayCount(Entry entry) => entry == null ? "0" : InfiniteBuilderPolicy.Count(InfiniteBuilder, entry.Available);
    internal bool CanPlace(Entry entry) =>
        entry?.Definition != null && InfiniteBuilderPolicy.CanPlaceFromToolkit(InfiniteBuilder, entry.Available);
    private int RealCount(IdentifiableType basis)
    {
        using (InfiniteBuildHooks.RealCounts()) return _app.Context.GadgetDirector.GetItemCount(basis);
    }
    // Owner opt-in «Бесконечный режим строителя». Hooks are installed on the
    // first enable only; the mode is transient and resets with the save/world.
    internal void SetInfiniteBuilder(bool enabled)
    {
        if (enabled)
        {
            if (!_app.Ready || _app.Map.Busy) throw new InvalidOperationException("Сначала загрузите мир.");
            if (_pending != null) throw new InvalidOperationException("Дождитесь завершения уже подтверждённого размещения.");
            if (!_app.EnsureInfiniteBuilderHooks())
                throw new InvalidOperationException("Нативные перехватчики режима не установлены: " + InfiniteBuildHooks.InstallError);
        }
        InfiniteBuildHooks.SetEnabled(enabled, _app);
        RefreshCatalog();
        Status = InfiniteBuilderPolicy.Label(enabled) + (enabled
            ? " · известные гаджеты и декор: 999; при установке экземпляр и Преобразователь не тратятся"
            : " · обычный расход изготовленных экземпляров");
        _app.Notify(Status);
    }
    // Diagnostic relay helpers: the same native handlers as the player's keys.
    internal void ToggleNativeModeForTest()
    {
        var owner = NativeOwner ?? throw new InvalidOperationException("Штатный владелец инструмента не найден.");
        owner.ToggleItem(new InputEventData { Source = InputEventSource.KEYBOARD, _vecValue = Vector2.right });
        Status = "Штатный режим строительства: " + (_app.Context.PlayerState.GadgetModeActive.Value ? "ВКЛ" : "ВЫКЛ");
    }
    internal void OpenNativeMenuForTest()
    {
        if (!_app.Context.PlayerState.GadgetModeActive.Value)
            throw new InvalidOperationException("Сначала включите штатный режим строительства.");
        var native = NativeItem ?? throw new InvalidOperationException("Штатный GadgetItem недоступен.");
        native.OpenGadgetMenu();
    }
    internal void RefreshCatalog()
    {
        Catalog.Clear();
        if (!_app.Ready || _app.Map.Busy) return;
        _session = _app.Session;
        var director = _app.Context.GadgetDirector;
        if (director?._model == null) { Status = "Нативный GadgetDirector ещё не готов"; return; }
        var seen = new HashSet<IntPtr>();
        // Real stock even inside the build mode; the UI derives 999 itself.
        using (InfiniteBuildHooks.RealCounts())
        foreach (var type in NativeEnumerable.Items(director.GetAllGadgets()))
        {
            var definition = type?.TryCast<GadgetDefinition>();
            if (definition == null || definition.prefab == null || definition.IsSecondary ||
                !seen.Add(definition.Pointer)) continue;
            var basis = definition.BaseGadget ?? definition;
            // Mode ON: also the known set the native menu shows at 999
            // (blueprint obtained/unlocked, Slimepedia entry or already placed).
            if (!director.HasBlueprint(basis) && !director.HasBlueprint(definition) &&
                !(InfiniteBuilder && InfiniteBuildHooks.IsKnown(definition))) continue;
            string name;
            try { name = definition.LocalizedName.GetLocalizedString(); }
            catch { name = definition.name; }
            if (string.IsNullOrWhiteSpace(name)) name = definition.name;
            Catalog.Add(new Entry { Definition = definition, Name = name, Reference = definition.ReferenceId,
                Search = (name + " " + definition.name + " " + definition.ReferenceId).ToLowerInvariant(),
                Available = director.GetItemCount(basis) });
            if (Catalog.Count >= CarePolicy.MaxGadgets) break;
        }
        Catalog.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
        Status = (InfiniteBuilder ? "Известных гаджетов и декора: " : "Полученных чертежей: ") + Catalog.Count + (InfiniteBuilder
            ? " · бесконечный режим: экземпляры не тратятся"
            : " · размещаются только изготовленные экземпляры");
    }
    private void Guard(Entry entry)
    {
        if (!_app.Ready || _session != _app.Session || _app.Map.Busy || Time.timeScale <= 0)
            throw new InvalidOperationException("Мир сменился или ещё загружается; обновите каталог.");
        if(_pending!=null)throw new InvalidOperationException("Дождитесь завершения уже подтверждённого размещения.");
        if (entry?.Definition == null || entry.Definition.WasCollected || !Catalog.Contains(entry))
            throw new InvalidOperationException("Выберите гаджет из текущего каталога.");
        var director = _app.Context.GadgetDirector;
        var basis = entry.Definition.BaseGadget ?? entry.Definition;
        using (InfiniteBuildHooks.RealCounts())
            if ((!director.HasBlueprint(basis) && !director.HasBlueprint(entry.Definition) &&
                 !(InfiniteBuilder && InfiniteBuildHooks.IsKnown(entry.Definition))) ||
                (!InfiniteBuilder && director.GetItemCount(basis) <= 0))
                throw new InvalidOperationException(InfiniteBuilder ? "Гаджет ещё не известен: нужен чертёж, запись в Слаймопедии или установленный экземпляр." :
                    "Нужен полученный чертёж и изготовленный экземпляр в нативном инвентаре.");
        var native = NativeItem;
        if (native == null || native.WasCollected || native.GadgetItemMetadata == null)
            throw new InvalidOperationException("Штатный GadgetItem недоступен.");
        if (native.HasHeldGadget() || native._holdingPickedUpGadget || native._gadgetPlaceholderInstance != null)
            throw new InvalidOperationException("Завершите штатное строительство или уберите удерживаемый гаджет.");
        var owner=NativeOwner;
        if(owner?._gadgetModeBlocker!=null||owner?._isPlayerDead?.Value==true||owner?._isInRanchHouse?.Value==true)
            throw new InvalidOperationException("Штатное строительство заблокировано в текущем состоянии игрока.");
    }
    private bool WorldRaycast(GadgetItem native,Ray ray,float distance,out RaycastHit hit)
    {
        hit=default;float closest=float.MaxValue;int skipped=0;
        var hits=Physics.RaycastAll(ray,distance,GadgetItem.CAN_PLACE_CHECK_RAY_MASK,QueryTriggerInteraction.Ignore);
        if(hits.Length>512)return false; // Fail closed, never arbitrarily truncate a crowded ray.
        bool ownPreview=_pending?.PreviewCreated==true&&_pending.ItemToken==native.Pointer;
        for(int i=0;i<hits.Length;i++)
        {
            var candidate=hits[i];var collider=candidate.collider;
            if(collider==null||!float.IsFinite(candidate.distance))continue;
            var transform=collider.transform;
            if(ownPreview&&(
                (native._gadgetPlaceholderInstance!=null&&transform.IsChildOf(native._gadgetPlaceholderInstance.transform))||
                (native._gadgetFootprintInstance!=null&&transform.IsChildOf(native._gadgetFootprintInstance.transform))||
                (native._gadgetRangeInstance!=null&&transform.IsChildOf(native._gadgetRangeInstance.transform))))
            {skipped++;continue;}
            // Skip ONLY the exact temporary visual owned by this transaction.
            // Real gadgets, actors and all other physical blockers remain the
            // nearest hit, and must still pass the normal surface/native gates.
            if(candidate.distance<closest){closest=candidate.distance;hit=candidate;}
        }
        if(_app.Audit.Active&&skipped>0)_app.LoggerInstance.Msg("Gadget ray ignored exact owned preview colliders="+skipped+
            " surface="+(hit.collider?.name??"none")+" distance="+hit.distance+" normal="+hit.normal.ToString("F3"));
        return closest<float.MaxValue;
    }
    internal bool TryGround(out Ray ray, out RaycastHit hit, out Quaternion rotation, out string reason)
    {
        ray = default; hit = default; rotation = Quaternion.identity; reason = "";
        if (!_app.Ready || _app.Map.Busy) { reason = "Мир ещё не готов"; return false; }
        var native = NativeItem; var camera = Camera.main;
        if (native?.GadgetItemMetadata == null || camera == null) { reason = "Игровая камера / GadgetItem недоступны"; return false; }
        var metadata = native.GadgetItemMetadata;
        float distance = Mathf.Min(8f, metadata.MaxValidPlacementDist);
        if (!float.IsFinite(distance) || distance < 1f) { reason = "Нативная дистанция размещения недоступна"; return false; }
        ray = new Ray(camera.transform.position, camera.transform.forward);
        if (!WorldRaycast(native,ray,distance,out hit))
        {
            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = _app.Context.Player.transform.forward;
            var ahead = _app.Player.Controller.Motor.TransientPosition + forward * Mathf.Min(3f, distance * .5f);
            ray = new Ray(ahead + Vector3.up * 3f, Vector3.down);
            if (!WorldRaycast(native,ray,7f,out hit))
            { reason = "Перед игроком нет твёрдой поверхности"; return false; }
            // The downward probe only finds a candidate. Native placement must
            // receive the real camera-to-ground ray/hit, including its distance,
            // and a clear line of sight—not a fabricated hit from another origin.
            var target=hit;var delta=target.point-camera.transform.position;
            if(delta.sqrMagnitude<.01f){reason="Поверхность слишком близко к камере";return false;}
            ray=new Ray(camera.transform.position,delta.normalized);
            if(!WorldRaycast(native,ray,delta.magnitude+.05f,out hit)||
                hit.collider!=target.collider||Vector3.Distance(hit.point,target.point)>.1f)
            {reason="Между камерой и найденной землёй есть препятствие";return false;}
        }
        var collider = hit.collider;
        if (collider == null || collider.isTrigger || collider.attachedRigidbody != null ||
            !collider.gameObject.scene.IsValid() || !collider.gameObject.scene.isLoaded ||
            collider.GetComponentInParent<IdentifiableActor>() != null ||
            Vector3.Distance(camera.transform.position, hit.point) > distance ||
            !ExplorerPolicy.Coordinates(hit.point.x, hit.point.y, hit.point.z) ||
            !CarePolicy.NativePlacementSurface(hit.normal.y,metadata.MaxValidPlacementSlope))
        {
            if(_app.Audit.Active)_app.LoggerInstance.Msg("Gadget rejected first world surface name="+(collider?.name??"none")+
                " trigger="+(collider?.isTrigger??false)+" body="+(collider?.attachedRigidbody?.name??"none")+
                " actor="+(collider?.GetComponentInParent<IdentifiableActor>()?.name??"none")+
                " layer="+(collider?.gameObject.layer??-1)+" point="+hit.point.ToString("F3")+
                " normal="+hit.normal.ToString("F3")+" distance="+Vector3.Distance(camera.transform.position,hit.point));
            reason = "Нужна неподвижная земля в нативной дистанции и допустимом уклоне"; return false;
        }
        string surface = (collider.name + " " + collider.transform.parent?.name).ToLowerInvariant();
        if (surface.Contains("water") || surface.Contains("slimesea") || surface.Contains("lava"))
        { reason = "Вода, море слаймов и лава не подходят"; return false; }
        var facing = Vector3.ProjectOnPlane(camera.transform.forward, hit.normal);
        if (facing.sqrMagnitude < .01f) facing = Vector3.ProjectOnPlane(_app.Context.Player.transform.forward, hit.normal);
        if (facing.sqrMagnitude < .01f) { reason = "Не удалось определить направление на поверхности"; return false; }
        rotation = Quaternion.LookRotation(facing.normalized, hit.normal);
        return true;
    }
    internal void RequestPlace(Entry entry)
    {
        Guard(entry);
        if (!TryGround(out _, out var hit, out _, out string reason)) throw new InvalidOperationException(reason);
        Vector3 expected = hit.point;
        _app.Confirm("Разместить «" + entry.Name + "»?",
            (InfiniteBuilder ? "Бесконечный режим строителя: экземпляр не списывается, Преобразователь не трогается. " :
                "Один изготовленный экземпляр из нативного инвентаря. ") + "Земля, уклон, полный контакт, "+
            "пересечения и ограничения связанных порталов проверяются заново. Позиция: " + expected.ToString("F1") +
            ". Гаджет будет сохраняться игрой; убирается штатным режимом строительства.",
            () => { _app.CloseMenu(); PlaceNow(entry, expected); });
    }
    internal bool PlaceNow(Entry entry, Vector3? expected = null)
    {
        Guard(entry);
        if (!TryGround(out _, out var hit, out var rotation, out string reason))
        { Status = reason; return false; }
        if (expected.HasValue && Vector3.Distance(expected.Value, hit.point) > .25f)
            throw new InvalidOperationException("Игрок или точка размещения сдвинулись; подтвердите новую позицию.");
        var owner=NativeOwner;var native=owner.GadgetItem;
        var inventory=_app.Context.GadgetDirector._model;
        var job=new Placement{Entry=entry,Point=hit.point,Rotation=native._gadgetRotation,Session=_app.Session,ItemToken=native.Pointer,
            OldLast=native._lastPlacedGadget,OldBase=native._baseGadget,OldMode=_app.Context.PlayerState.GadgetModeActive.Value,
            OldLinked=native.IsLinkedGadgetInRange,OldLookedOff=native._hasLookedOffOfLastPlacedGadget,
            OldValid=native._isPlacementValid,OldGrounded=native._isGrounded,OldBlocked=native._isPlacementBlocked,
            ArmedFrame=Time.frameCount,Deadline=Time.unscaledTime+2f,
            OldSelectedSlot=inventory.SelectedSlotIndex.Value,OldLocked=inventory.LockedSlot.Value};
        LastPlacementSucceeded=false;_pending=job;
        var eventData=new InputEventData { Source=InputEventSource.KEYBOARD,_vecValue=Vector2.right };
        try
        {
            // Keep the native selected slot and held preview consistent. The
            // game's event handler is authoritative; a private preview alone
            // is not a player selection. Favorites/stock are never rewritten.
            job.SelectionChanged=true;
            bool selected;
            try
            {
                // TrySelectGadget selects an existing native slot; it does not
                // add an unfavorited crafted decoration to one. Use the game's
                // temporary locked-slot API, just as its catalog does. Restore
                // this borrowed selection after the one bounded transaction.
                inventory.SetLockedGadgetIfNotFavorite(entry.Definition);
                selected=inventory.TrySelectGadget(entry.Definition);
                if(_app.Audit.Active)_app.LoggerInstance.Msg("Gadget native selection selected="+selected+
                    " definition="+inventory.GetSelectedGadget()?.ReferenceId+
                    " slot="+inventory.SelectedSlotIndex.Value+" locked="+inventory.LockedSlot.Value?.ReferenceId+
                    " hasLockedSlot="+Il2CppMonomiPark.SlimeRancher.DataModel.GadgetsModel.HAS_LOCKED_GADGET);
            }
            finally{job.TemporarySelectedSlot=inventory.SelectedSlotIndex.Value;}
            if(!selected||inventory.GetSelectedGadget()!=entry.Definition)
                throw new InvalidOperationException("Нативный инвентарь отказался выбрать изготовленный гаджет.");
            if(!job.OldMode)
            {
                owner.ToggleItem(eventData);
                job.ModeChanged=_app.Context.PlayerState.GadgetModeActive.Value;
                if(!job.ModeChanged)throw new InvalidOperationException("Штатный контроллер отказался включить строительство.");
            }
            // Unity invokes Start for a newly enabled native item on a later
            // frame. Never manually call Start or duplicate its subscriptions.
            Status="Подтверждение принято; ожидаю штатную готовность инструмента (до 2 с)";
            return true;
        }
        catch{FinishPlacement(job);throw;}
    }
    internal void Tick()
    {
        var job=_pending;if(job==null)return;
        if(!_app.Ready||_app.Session!=job.Session)
        {LastPlacementSucceeded=false;Status="Мир изменился во время размещения; нативное событие не повторяется";FinishPlacement(job);return;}
        if(Time.frameCount<=job.ArmedFrame)return;
        try
        {
            if(job.EventIssued)
            {
                if(ConfirmNativePlacement(job))return;
                if(Time.unscaledTime<job.Deadline)return;
                throw new InvalidOperationException("Нативный реестр и инвентарь не подтвердили один гаджет за 1 с. "+
                    "Повтор и возврат экземпляра не выполнены; проверьте штатное строительство.");
            }
            var owner=NativeOwner;var native=owner?.GadgetItem;
            if(native==null||native.Pointer!=job.ItemToken||_app.Map.Busy||native._holdingPickedUpGadget||
                !_app.Context.PlayerState.GadgetModeActive.Value||
                _app.Context.GadgetDirector._model.GetSelectedGadget()!=job.Entry.Definition)
                throw new InvalidOperationException("Нативный инструмент или сцена изменились; повтор размещения не выполнен.");
            if(native._propertyBlock==null)
            {
                if(Time.unscaledTime<job.Deadline)return;
                throw new InvalidOperationException("Штатный инструмент не завершил инициализацию за 2 с; размещение отменено.");
            }
            var entry=job.Entry;var director=_app.Context.GadgetDirector;var metadata=native.GadgetItemMetadata;
            var basis=entry.Definition.BaseGadget??entry.Definition;
            int before=RealCount(basis);
            if(!InfiniteBuilderPolicy.CanPlaceFromToolkit(InfiniteBuilder,before)||(!director.HasBlueprint(basis)&&!director.HasBlueprint(entry.Definition)&&
                !(InfiniteBuilder&&InfiniteBuildHooks.IsKnown(entry.Definition))))
                throw new InvalidOperationException("Изготовленный экземпляр или чертёж больше недоступен.");
            if(!job.PreviewCreated)
            {
                // An empty native selection may not have a footprint yet.
                // SetHeld once, then let the game's next Update create it.
                native.SetHeldGadget(entry.Definition);job.PreviewCreated=true;
                if(native.GetPlaceholderGadget()==null)
                    throw new InvalidOperationException("Штатный предпросмотр не создан; размещение отменено.");
                return;
            }
            if(native._gadgetFootprintRendererInstance==null||native._gadgetFootprintInstance==null)
            {
                if(Time.unscaledTime<job.Deadline)return;
                throw new InvalidOperationException("Штатный предпросмотр не подготовил поверхность за 2 с; размещение отменено.");
            }
            if(!TryGround(out var ray,out var hit,out var rotation,out var reason)||
                Vector3.Distance(job.Point,hit.point)>.25f)
                throw new InvalidOperationException("Точка изменилась после подтверждения: "+reason);
            job.FootprintPosition=native._gadgetFootprintInstance.transform.position;
            job.FootprintRotation=native._gadgetFootprintInstance.transform.rotation;job.FootprintCaptured=true;
            var preview = native.GetPlaceholderGadget();
            if (preview == null || native._gadgetPlaceholderInstance == null)
                throw new InvalidOperationException("Штатный предпросмотр гаджета не создан; размещение отменено.");
            native._gadgetRotation = rotation;
            native._gadgetFootprintInstance.transform.SetPositionAndRotation(hit.point,rotation);
            preview.transform.SetPositionAndRotation(hit.point, rotation);
            Physics.SyncTransforms();
            preview.UpdateIsInRanch(_app.Context.RegionRegistry);
            // The native UpdateFootprint derives _baseGadget from its ray.
            // Its earlier weapon ray can see our newly created placeholder;
            // keep this derived stacking input consistent with the exact
            // nearest real surface validated above. Never alter either
            // gadget's stacking permissions or bypass IsPlacementValid.
            var previousBase=native._baseGadget;
            var surfaceGadget=hit.collider.GetComponentInParent<Gadget>();
            if(surfaceGadget!=null&&surfaceGadget.GetModel()==null)
                throw new InvalidOperationException("Поверхность принадлежит незарегистрированному предпросмотру; размещение отменено.");
            native._baseGadget=surfaceGadget;
            native._isGrounded = preview.IsCompletelyGrounded(metadata.GroundedCheckAdjustment, metadata.GroundedRaycastDistance);
            native._isPlacementBlocked = preview.IsOverlapping(metadata.OverlapPlacementFloorTolerance, metadata.GadgetOverlapLayers);
            native._IsLinkedGadgetInRange_k__BackingField=native.IsInLinkedGadgetRange(hit.point);
            bool rayValid=native.IsPlacementValid(ray,hit);
            bool directorValid=director.CanPlaceGadget(preview,entry.Definition);
            bool valid = rayValid && native._isGrounded && !native._isPlacementBlocked &&
                native.IsLinkedGadgetInRange && directorValid;
            if(_app.Audit.Active)_app.LoggerInstance.Msg("Gadget native placement ground="+native._isGrounded+
                " overlap="+native._isPlacementBlocked+" linked="+native.IsLinkedGadgetInRange+" rayValid="+rayValid+
                " directorValid="+directorValid+" valid="+valid+" distance="+hit.distance+
                " minVisible="+metadata.MinPlacementFootprintVisibleDist+" max="+metadata.MaxValidPlacementDist+
                " point="+hit.point.ToString("F3")+" slope="+metadata.MaxValidPlacementSlope+
                " nativeMinimumNormalY="+Mathf.Sin(metadata.MaxValidPlacementSlope*Mathf.Deg2Rad)+" definition="+entry.Reference+
                " owner="+owner.gameObject.name+" item="+native.gameObject.name+
                " rayOrigin="+ray.origin.ToString("F3")+" rayDirection="+ray.direction.ToString("F3")+
                " normal="+hit.normal.ToString("F3")+" surface="+hit.collider.name+
                " layer="+hit.collider.gameObject.layer+" footprintVisible="+native._isFootprintVisible+
                " placeholderVisible="+native.IsPlaceholderVisible+
                " nativeBaseBefore="+(previousBase?.gameObject.name??"none")+
                " nativeBaseValidated="+(surfaceGadget?.gameObject.name??"none")+
                " previewCanStack="+preview.CanStackOnStackableGadgets+
                " nativePreviewSame="+(native._gadgetPlaceholderInstanceGadget==preview)+
                " nativePreviewGround="+(native._gadgetPlaceholderInstanceGadget!=null&&
                    native._gadgetPlaceholderInstanceGadget.IsCompletelyGrounded(metadata.GroundedCheckAdjustment,metadata.GroundedRaycastDistance))+
                " cameraDistance="+Vector3.Distance(ray.origin,hit.point)+
                " cameraAimError="+Vector3.Angle(Camera.main.transform.forward,ray.direction)+
                " visibleSlopeThreshold="+metadata.FootprintVisibleSlopeThreshold+
                " placementVisibleDist="+metadata.PlacementFootprintVisibleDist);
            native.SetGadgetPlacementValidity(valid);
            if (!valid)throw new InvalidOperationException(!rayValid?
                "Штатный прицел отверг поверхность: наведите камеру на открытую землю в допустимой дистанции. Экземпляр не потрачен.":
                "Нативная проверка отвергла место: контакт, пересечение, лимит или дальность портала. Экземпляр не потрачен.");
            job.Point=hit.point;job.Normal=hit.normal;job.InventoryBefore=before;
            var registered=_app.Context.GameModel.AllGadgets();
            job.ModelCountBefore=registered.Count;job.BeforeModels=new HashSet<IntPtr>();
            for(int i=0;i<registered.Count;i++)job.BeforeModels.Add(registered[i].Pointer);
            // Exactly one native event. If it throws or its outcome is unclear,
            // never repeat it and never manufacture an inventory refund.
            var eventData=new InputEventData {Source=InputEventSource.KEYBOARD,_vecValue=Vector2.right};
            job.EventIssued=true;job.Deadline=Time.unscaledTime+1f;
            if(_app.Audit.Active)_app.LoggerInstance.Msg("Gadget native event input="+eventData.Value+
                " held="+native.HasHeldGadget()+" heldDefinition="+native._heldGadget?.ReferenceId+
                " selected="+director._model.GetSelectedGadget()?.ReferenceId+
                " selectedSlot="+director._model.SelectedSlotIndex.Value+" mode="+_app.Context.PlayerState.GadgetModeActive.Value);
            job.FreeSpendsBefore=InfiniteBuildHooks.FreeSpends;
            native.PlaceGadgetEvent(eventData);
            if(_app.Audit.Active)_app.LoggerInstance.Msg("Gadget native single event issued inventoryBefore="+before+
                " inventoryAfter="+RealCount(basis)+" freeSpends="+(InfiniteBuildHooks.FreeSpends-job.FreeSpendsBefore)+" modelsBefore="+job.ModelCountBefore+
                " modelsAfter="+_app.Context.GameModel.AllGadgets().Count+" privateLast="+(native._lastPlacedGadget?.Pointer??IntPtr.Zero));
            // Private _lastPlacedGadget is not the persistence registry and
            // may be null for decor/deferred callbacks. Verify the real model
            // and conserved inventory on a later frame; never re-issue here.
        }
        catch(Exception e){LastPlacementSucceeded=false;Status="Размещение не завершено: "+e.Message;_app.Notify(Status);_app.LoggerInstance.Warning(e.ToString());}
        finally
        {
            // An uninitialized item is still waiting: preserve only this bounded
            // request. Every attempted mutation is retired once, never repeated.
            if(_pending==job&&(LastPlacementSucceeded||Time.unscaledTime>=job.Deadline||Status.StartsWith("Размещение не завершено:",StringComparison.Ordinal)))
            {
                try{FinishPlacement(job);}
                catch(Exception e){LastPlacementSucceeded=false;Status="Размещение обработано, но очистка инструмента не подтверждена: "+e.Message;_app.Notify(Status);_app.LoggerInstance.Warning(e.ToString());}
            }
        }
    }
    private bool ConfirmNativePlacement(Placement job)
    {
        var entry=job.Entry;var basis=entry.Definition.BaseGadget??entry.Definition;
        var registry=_app.Context.GameModel.AllGadgets();
        int after=RealCount(basis);
        // Infinite builder: the hook skipped exactly this native spend, so the
        // real stock must be unchanged; otherwise exactly one unit was spent.
        bool free=InfiniteBuildHooks.FreeSpends>job.FreeSpendsBefore;
        Gadget placed=null;int added=0;
        for(int i=0;i<registry.Count;i++)
        {
            if(job.BeforeModels.Contains(registry[i].Pointer))continue;
            added++;
            var root=registry[i].GetGameObject();var candidate=root?.GetComponent<Gadget>();
            if(candidate?.IdentTypeAsDefinition==entry.Definition)placed=candidate;
        }
        if(registry.Count!=job.ModelCountBefore+1||added!=1||placed==null||placed.GetModel()==null||
            !(free?after==job.InventoryBefore:CarePolicy.SpentOne(job.InventoryBefore,after)))return false;
        var model=placed.GetModel();
        float positionError=Vector3.Distance(model.GetPos(),job.Point);
        float normalDot=Vector3.Dot(model.GetRot()*Vector3.up,job.Normal);
        if(positionError>.25f||normalDot<.99f)
            throw new InvalidOperationException("Нативная модель создана, но положение/нормаль не совпадают с подтверждённой землёй. Повтор не выполнен.");
        Placed++;LastPlacementPoint=job.Point;LastPlacementNormal=job.Normal;
        Status=free?"Размещено бесплатно: "+entry.Name+" · 1 зарегистрированный GadgetModel · инвентарь не изменён ("+after+")":
            "Размещено: "+entry.Name+" · 1 зарегистрированный GadgetModel · остаток "+after;
        entry.Available=after;LastPlacementSucceeded=true;
        if(_app.Audit.Active)_app.LoggerInstance.Msg("Gadget native registry confirmed definition="+entry.Reference+
            " inventoryBefore="+job.InventoryBefore+" inventoryAfter="+after+" free="+free+" modelsBefore="+job.ModelCountBefore+
            " modelsAfter="+registry.Count+" model="+model.Pointer+" positionError="+positionError+" normalDot="+normalDot);
        _app.Notify(Status);return true;
    }
    private void FinishPlacement(Placement job)
    {
        if(_pending!=job)return;
        _pending=null;
        if(!_app.Ready||_app.Session!=job.Session)return;
        var owner=NativeOwner;var native=owner?.GadgetItem;
        if(native==null||native.Pointer!=job.ItemToken||native._holdingPickedUpGadget)return;
        try{if(job.PreviewCreated)native.ClearHeldGadget();}
        finally
        {
            native._lastPlacedGadget=job.OldLast;native._baseGadget=job.OldBase;
            native._hasLookedOffOfLastPlacedGadget=job.OldLookedOff;
            native._gadgetRotation=job.Rotation;native._IsLinkedGadgetInRange_k__BackingField=job.OldLinked;
            native._isPlacementValid=job.OldValid;native._isGrounded=job.OldGrounded;native._isPlacementBlocked=job.OldBlocked;
            if(job.FootprintCaptured&&native._gadgetFootprintInstance!=null)
                native._gadgetFootprintInstance.transform.SetPositionAndRotation(job.FootprintPosition,job.FootprintRotation);
            if(job.SelectionChanged)
            {
                var inventory=_app.Context.GadgetDirector._model;
                bool stillOwnSelection=inventory.SelectedSlotIndex.Value==job.TemporarySelectedSlot&&
                    inventory.GetSelectedGadget()==job.Entry.Definition;
                if(inventory.LockedSlot.Value==job.Entry.Definition)inventory.LockedSlot.Set(job.OldLocked);
                if(stillOwnSelection)inventory.SelectedSlotIndex.Set(job.OldSelectedSlot);
                job.OldLocked=null;
            }
            if(job.ModeChanged&&_app.Context.PlayerState.GadgetModeActive.Value)
                owner.ToggleItem(new InputEventData {Source=InputEventSource.KEYBOARD,_vecValue=Vector2.right});
            job.OldLast=job.OldBase=null;job.BeforeModels?.Clear();job.BeforeModels=null;
        }
    }
    internal void Reset()
    {
        if(_pending!=null)FinishPlacement(_pending);
        InfiniteBuildHooks.SetEnabled(false,_app); // Transient: never carried into another save.
        Catalog.Clear(); Catalog.TrimExcess(); _session = IntPtr.Zero; Placed = 0;
        LastPlacementSucceeded=false;
        LastPlacementPoint=LastPlacementNormal=Vector3.zero;
        Status = "Каталог гаджетов ещё не открыт";
    }
}