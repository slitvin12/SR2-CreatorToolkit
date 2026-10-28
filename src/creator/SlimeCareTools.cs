using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.Slime;
using UnityEngine;

namespace CreatorToolkit;

internal sealed class SlimeCareTools
{
    // Value snapshots only. Raw identity tokens are compared against a fresh
    // live native index; never construct a wrapper from a possibly stale pointer.
    internal sealed class Hit
    {
        internal string Name, Reference, Favorite, FavoriteReference;
        internal IntPtr ActorToken;
        internal float Hunger, Agitation, Distance;
        internal bool Hungry;
    }
    private readonly ToolkitMod _app;
    internal readonly List<Hit> Hits = new();
    private IntPtr _session;
    private float _nextScan, _nextFeed;
    internal bool Inspect { get; private set; }
    internal int Examined { get; private set; }
    internal int Meals { get; private set; }
    internal bool Truncated { get; private set; }
    internal string Status { get; private set; } = "Инспектор выключен";
    internal int NativeActorRoots => 0;
    internal SlimeCareTools(ToolkitMod app) => _app = app;
    internal void SetInspect(bool enabled)
    {
        if (!_app.Ready || _app.Map.Busy) throw new InvalidOperationException("Дождитесь загрузки мира.");
        Inspect = enabled; _session = _app.Session; _nextScan = 0;
        if (!enabled) { Hits.Clear(); Examined = 0; Truncated = false; Status = "Инспектор выключен"; }
    }
    private static bool Live(SlimeEat eater) => eater != null && !eater.WasCollected &&
        eater.gameObject.activeInHierarchy && eater.gameObject.scene.IsValid() &&
        eater.gameObject.scene.isLoaded && eater.SlimeDefinition != null && eater._emotions != null;
    private string FavoriteNames(SlimeDefinition definition)
    {
        var favorites = definition?.Diet?.FavoriteIdents;
        if (favorites == null || favorites.Length == 0) return "Нет любимой еды";
        var names = new List<string>();
        for (int i = 0; i < favorites.Length; i++)
        {
            var type = favorites[i]; if (type == null) continue;
            string name = type.name;
            foreach (var entry in _app.Actors.Catalog) if (entry.Type == type) { name = entry.Name; break; }
            if (!names.Contains(name)) names.Add(name);
        }
        return names.Count == 0 ? "Нет любимой еды" : string.Join(", ", names);
    }
    internal void ScanNow()
    {
        Hits.Clear(); Examined = 0; Truncated = false;
        if (!_app.Ready || _app.Map.Busy) return;
        _session = _app.Session;
        var origin = _app.Player.Controller.Motor.TransientPosition;
        var index = _app.Context.GameModel.identifiablesByIdent;
        if (index == null) { Status = "Нативный индекс слаймов ещё не готов"; return; }
        foreach (var entry in _app.Actors.Catalog)
        {
            if (entry.IsPrototype || entry.Type?.TryCast<SlimeDefinition>() == null) continue;
            if (!index.TryGetValue(entry.Type, out var models) || models == null) continue;
            for (int i = 0; i < models.Count; i++)
            {
                if (++Examined > CarePolicy.MaxExamined) { Truncated = true; break; }
                var root = models[i]?.GetGameObject();
                if (root == null || root.WasCollected || !root.activeInHierarchy) continue;
                var actor = root.GetComponent<IdentifiableActor>();
                if (actor == null || actor.identType != entry.Type) continue;
                float d2 = (actor.transform.position - origin).sqrMagnitude;
                if (!CarePolicy.Within(d2)) continue;
                var eater = root.GetComponent<SlimeEat>();
                if (!Live(eater)) continue;
                float hunger = eater._emotions.Get(SlimeEmotions.Emotion.HUNGER);
                float agitation = eater._emotions.Get(SlimeEmotions.Emotion.AGITATION);
                if (!float.IsFinite(hunger) || !float.IsFinite(agitation)) continue;
                var hit = new Hit { Name = entry.Name, Reference = entry.Reference, ActorToken = actor.Pointer,
                    Distance = Mathf.Sqrt(d2), Hunger = Mathf.Clamp01(hunger), Agitation = Mathf.Clamp01(agitation),
                    Favorite = FavoriteNames(eater.SlimeDefinition),
                    FavoriteReference=eater.SlimeDefinition.Diet?.FavoriteIdents?.Length>0?
                        eater.SlimeDefinition.Diet.FavoriteIdents[0]?.ReferenceId:"",
                    Hungry = hunger >= SlimeEmotions.HUNGRY_CUTOFF };
                if (Hits.Count < CarePolicy.MaxTargets) Hits.Add(hit);
                else
                {
                    int farthest = 0;
                    for (int j = 1; j < Hits.Count; j++) if (Hits[j].Distance > Hits[farthest].Distance) farthest = j;
                    if (hit.Distance < Hits[farthest].Distance) Hits[farthest] = hit;
                }
            }
            if (Truncated) break;
        }
        Hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        Status = "Слаймов до 25 м: " + Hits.Count + " · ближайшие " + CarePolicy.MaxTargets +
            (Truncated ? " · достигнут предел просмотра индекса" : "");
    }
    private SlimeEat Resolve(Hit hit)
    {
        if (!_app.Ready || _session != _app.Session || hit == null || _app.Map.Busy) return null;
        var type = LookupDirector.GetIfReady()?._identifiableTypeByRefId;
        if (type == null || !type.TryGetValue(hit.Reference, out var definition) ||
            !_app.Context.GameModel.identifiablesByIdent.TryGetValue(definition, out var models)) return null;
        for (int i = 0; models != null && i < models.Count; i++)
        {
            var go = models[i]?.GetGameObject();
            if (go == null || go.WasCollected || !go.activeInHierarchy) continue;
            var actor = go.GetComponent<IdentifiableActor>();
            if (actor?.Pointer != hit.ActorToken || !CarePolicy.Within(
                (actor.transform.position - _app.Player.Controller.Motor.TransientPosition).sqrMagnitude)) continue;
            var eater = go.GetComponent<SlimeEat>();
            return Live(eater) ? eater : null;
        }
        return null;
    }
    private static SlimeDiet.EatMapEntry FavoriteMeal(SlimeEat eater, IdentifiableType food)
    {
        var map = eater.GetEatMapById(food);
        for (int i = 0; map != null && i < map.Count; i++)
        {
            var meal = map[i];
            // No plort-fed transformations, tarrification or forced diet.
            if (meal != null && meal.IsFavorite && meal.BecomesIdent == null) return meal;
        }
        return null;
    }
    private bool Visible(SlimeEat eater)
    {
        var camera = Camera.main;
        Vector3 from = camera != null ? camera.transform.position : _app.Player.Controller.Motor.TransientPosition + Vector3.up;
        Vector3 to = eater.transform.position + Vector3.up * .3f, delta = to - from;
        if (delta.sqrMagnitude < .01f) return true;
        if (!Physics.Raycast(from, delta.normalized, out var hit, delta.magnitude,
            _app.Player.Controller.Motor.CollidableLayers, QueryTriggerInteraction.Ignore)) return true;
        return hit.collider != null && hit.collider.GetComponentInParent<SlimeEat>() == eater;
    }
    internal void RequestFeed()
    {
        _app.Confirm("Накормить голодных слаймов вокруг?",
            "Только любимая еда из выбранного слота вакпака, до 8 порций за нажатие и одна на слайма до 25 м. "+
            "Без еды, при сытости, сне или препятствии ничего не расходуется. Кормление и получение плортов выполняет сама игра.",
            () => { _app.CloseMenu(); FeedNow(); });
    }
    internal bool CanOffer(SlimeEat eater,IdentifiableType food)
    {
        if(!Live(eater)||!eater.isActiveAndEnabled||!eater._isEatingEnabled||!eater.CanEat()||
            eater._chomper==null||!eater._chomper.CanChomp()||
            !float.IsFinite(eater._maxBiteDistance)||eater._maxBiteDistance<=0||
            eater.IsChomping()||eater.IsSleeping()||FavoriteMeal(eater,food)==null||!eater.WillNowEat(food))return false;
        float hunger=eater._emotions.Get(SlimeEmotions.Emotion.HUNGER);
        return float.IsFinite(hunger)&&hunger>=SlimeEmotions.HUNGRY_CUTOFF&&Visible(eater);
    }
    internal int FeedNow()
    {
        if (!_app.Ready || _app.Map.Busy || Time.timeScale <= 0) throw new InvalidOperationException("Мир не готов к кормлению.");
        if (Time.unscaledTime < _nextFeed) { Status = "Подождите 2 секунды между кормлениями"; return 0; }
        _nextFeed = Time.unscaledTime + CarePolicy.FeedCooldown;
        ScanNow();
        var ammo = _app.Context.PlayerState.Ammo;
        int selected = ammo?._selectedAmmoIdx ?? -1;
        if (ammo?.Slots == null || selected < 0 || selected >= ammo.Slots.Length) return 0;
        var slot = ammo.Slots[selected]; var food = slot?.Id;
        bool isFood = false;
        foreach (var entry in _app.Actors.Catalog) if (entry.Type == food) { isFood = entry.Category == "Еда" && entry.CanSpawn; break; }
        if (!isFood || food?.prefab == null || !slot.IsUnlocked || slot.Count <= 0)
        { Status = "Выберите слот с любимой едой; пустой слот не расходуется"; return 0; }
        var player = _app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true);
        if (player?.SceneGroup == null) throw new InvalidOperationException("Группа сцены игрока недоступна.");
        int budget = CarePolicy.MealBudget(slot.Count), accepted = 0;
        foreach (var snapshot in Hits)
        {
            if (accepted >= budget || slot.Id != food || slot.Count <= 0 || ammo._selectedAmmoIdx != selected) break;
            var eater = Resolve(snapshot);
            if (!CanOffer(eater,food)) continue;
            var metadata = slot.Metadata; var appearance = slot.Appearance; int before = slot.Count;
            Il2CppMonomiPark.SlimeRancher.DataModel.ActorModel model = null;
            GameObject portion = null; bool handedOff = false, handoffAttempted = false;
            try
            {
                var popped = ammo.PopFromSelectedSlot();
                bool preserved=_app.Player.InfiniteItems&&slot.Count==before&&slot.Id==food;
                if (popped?.Id != food || !(CarePolicy.SpentOne(before, slot.Count)||preserved))
                    throw new InvalidOperationException("Вакпак не подтвердил расход ровно одной порции.");
                // The native pop supplies one portion. Only the explicitly
                // enabled infinite-vacpack cheat may preserve its source count.
                var forward=Vector3.ProjectOnPlane(eater.transform.forward,Vector3.up).normalized;
                if(forward.sqrMagnitude<.1f)forward=Vector3.forward;
                float reach=Mathf.Min(eater._maxBiteDistance*.6f,2.4f);
                model = _app.Context.GameModel.InstantiateActorModel(food, player.SceneGroup,
                    eater.transform.position + forward*reach + Vector3.up * .25f, Quaternion.identity);
                if (model == null) throw new InvalidOperationException("Порция не получила нативную модель.");
                portion = model.GetGameObject() ?? InstantiationHelpers.InstantiateActorFromModel(model);
                if (portion == null) throw new InvalidOperationException("Порция не получила нативный объект.");
                var portionId=eater.ExtractOtherId(portion);
                if(portionId!=food)throw new InvalidOperationException("Нативная порция не идентифицируется как выбранная еда.");
                // The normal chomp path owns animation, hunger, favorite bonus,
                // plorts and destruction. No direct emotion/plort writes here.
                handoffAttempted = true;
                handedOff = eater.MaybeSpinAndChomp(portion,false);
                if(_app.Audit.Active)_app.LoggerInstance.Msg("Care native target="+snapshot.Name+
                    " actor="+snapshot.ActorToken+" accepted="+handedOff+" chomping="+eater.IsChomping()+
                    " hunger="+eater._emotions.Get(SlimeEmotions.Emotion.HUNGER)+" bite="+eater._maxBiteDistance+
                    " canChomp="+eater._chomper.CanChomp()+" nextChomp="+eater._chomper._nextChompTime+
                    " worldTime="+_app.Context.TimeDirector.WorldTime()+" portionId="+portionId?.ReferenceId+
                    " sourceId="+food.ReferenceId+" reach="+reach);
                if (!handedOff)
                {
                    Destroyer.DestroyActor(portion, "CreatorToolkit declined favorite portion");
                    model = null;
                    slot.SetFromMetadata(metadata); slot.Appearance = appearance; slot.Count = before;
                    continue;
                }
                accepted++; Meals++;
            }
            catch
            {
                // Once a native chomp has accepted the portion, do not refund
                // and duplicate it. A thrown native handoff is ambiguous.
                if (!handoffAttempted)
                {
                    if (portion != null && !portion.WasCollected) Destroyer.DestroyActor(portion, "CreatorToolkit pre-handoff portion rollback");
                    else if (model != null) _app.Context.GameModel.DestroyIdentifiableModel(model);
                    slot.SetFromMetadata(metadata); slot.Appearance = appearance; slot.Count = before;
                }
                throw;
            }
        }
        Status = accepted > 0 ? "Передано любимых порций: " + accepted + " · штатное пережёвывание и плорты" :
            "Подходящих голодных слаймов в прямой видимости не найдено; еда не потрачена";
        _app.Notify(Status);
        return accepted;
    }
    internal void Tick()
    {
        if (!Inspect) return;
        if (_session != _app.Session) { Reset(); return; }
        if (!_app.Ready || _app.Map.Busy) { Hits.Clear(); return; }
        if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + CarePolicy.ScanSeconds; ScanNow(); }
    }
    internal void Reset()
    {
        Inspect = false; Hits.Clear(); Hits.TrimExcess(); _session = IntPtr.Zero;
        _nextScan = _nextFeed = 0; Examined = Meals = 0; Truncated = false; Status = "Инспектор выключен";
    }
}