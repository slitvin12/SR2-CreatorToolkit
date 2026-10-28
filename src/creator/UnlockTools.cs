using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Event;
using Il2CppMonomiPark.SlimeRancher.UI.Map;
using Il2CppMonomiPark.SlimeRancher.World;
using Il2CppMonomiPark.World;
using UnityEngine;

namespace CreatorToolkit;

internal sealed partial class UnlockTools
{
    private readonly ToolkitMod _app;
    private bool _mapPending;
    private IntPtr _mapSession;
    private float _mapDeadline, _mapReadyAfter;
    internal int RevealedZones { get; private set; }
    internal int RevealedFogEvents { get; private set; }
    internal string MapResult { get; private set; } = "not requested";
    internal UnlockTools(ToolkitMod app) => _app = app;

    internal void Pedia()
    {
        var director = _app.Context.PediaDirector;
        int before = director._pediaModel.unlocked.Count;
        director.DebugAllUnlocked();
        int count = director._pediaModel.unlocked.Count - before;
        _app.Notify("Слаймопедия: открыто новых записей — " + count + ". Изменение сохраняется в сейве.");
    }

    internal void Upgrades()
    {
        var model = _app.Context.GameModel.player.GetUpgradeModel();
        int changed = 0;
        // IncrementUpgradeLevel rebuilds the native definition list through participant callbacks.
        // Snapshot BEFORE any mutation; iterating the live native list fails after the first upgrade.
        var definitions = new List<UpgradeDefinition>();
        var registered = model.upgradeDefinitions;
        for (int i = 0; i < registered.Count; i++) definitions.Add(registered[i]);
        foreach (UpgradeDefinition definition in definitions)
        {
            if (definition == null) continue;
            int before = model.GetUpgradeLevel(definition);
            // IncrementUpgradeLevel applies native effects and stops at the real limit; do not guess zero/one-based max levels.
            for (int i = 0; i <= Math.Max(0, definition.LevelCount); i++)
                if (!model.IncrementUpgradeLevel(definition)) break;
            if (model.GetUpgradeLevel(definition) != before) changed++;
        }
        // Native ClearAllModifiers enumerates dictionary.Keys while RemoveModifiers
        // removes those keys (confirmed IL2CPP stack). Pre-clear only this model's
        // registered actor handlers using managed snapshots, then notify normally.
        var handlers=new List<ActorUpgradeHandler>();
        var participants=model.participants;
        for(int i=0;participants!=null&&i<participants.Count;i++)
        { var handler=participants[i]?.TryCast<ActorUpgradeHandler>();if(handler!=null)handlers.Add(handler); }
        foreach(var handler in handlers)
        {
            var keys=new List<int>();
            if(handler._appliedModifiers!=null)foreach(var pair in handler._appliedModifiers)keys.Add(pair.Key);
            foreach(int key in keys)handler.RemoveModifiers(key);
        }
        model.NotifyParticipants();
        _app.Notify("Все нативные улучшения применены. Изменено типов: " + changed + ". Изменение сохраняется в сейве.");
    }

    internal void Doors()
    {
        int count = 0;
        foreach (var pair in _app.Context.GameModel.AllDoors())
        {
            var model = pair.Value;
            if (model == null || model.state == AccessDoor.State.OPEN) continue;
            model.state = AccessDoor.State.OPEN;
            model.NotifyParticipants();
            count++;
        }
        foreach (AccessDoor door in Resources.FindObjectsOfTypeAll<AccessDoor>())
        {
            if (door == null || !door.gameObject.scene.IsValid() || !door.gameObject.scene.isLoaded) continue;
            if (door._model == null) continue;
            door.ForceUpdate(true);
        }
        _app.Notify("Открыто зарегистрированных в сейве дверей: " + count + ". Сюжетные ограничения могут быть изменены.");
    }

    internal void Map()
    {
        _mapSession = _app.Session;
        _mapPending = true;
        _mapDeadline = Time.unscaledTime + 15f;
        _mapReadyAfter = Time.unscaledTime + .75f;
        RevealedZones = RevealedFogEvents = 0;
        MapResult = "waiting for native map assets";
        // Load native map UI, including its fog reveal assets, rather than hardcoding event keys or editing shared assets.
        _app.CloseMenu();
        _app.Map.Open();
        _app.Notify("Открывается игровая карта для регистрации нативных событий тумана…");
    }

    internal void Tick()
    {
        if (!_mapPending) return;
        if (!SafetyPolicy.SameSession(_mapSession, _app.Session)) { MapResult="cancelled: save changed";Reset();return; }
        if (Time.unscaledTime < _mapReadyAfter) return;
        // Use the validated live display selected by MapTools. FindObjectOfType
        // can pick an uninitialized MapUI retained by the native display pool.
        var ui = _app.Map.Ui;
        if (!_app.Map.NativeMapOpen || ui == null || ui._map == null || ui._viewModel == null)
        {
            if (Time.unscaledTime > _mapDeadline) { MapResult="failed: "+_app.Map.DisplayStatus;Reset();throw new InvalidOperationException("Карта не загрузилась: "+MapResult); }
            return;
        }
        // The active map alone is not the entire atlas. Read every native map
        // prefab's reveal events, without changing any shared prefab/asset.
        var fogs = new List<FogMapElement>();
        var fogSeen = new HashSet<IntPtr>();
        void AddFog(FogMapElement fog)
        {
            if (fog != null && fogSeen.Add(fog.Pointer)) fogs.Add(fog);
        }
        foreach (var fog in Resources.FindObjectsOfTypeAll<FogMapElement>()) AddFog(fog);
        var activeFogs=ui._fogReveal?._fogElements;
        for(int i=0;activeFogs!=null&&i<activeFogs.Count;i++)AddFog(activeFogs[i]);
        var mapping = ui._mapPrefabMapping;
        if (mapping?._entries != null)
            for (int i = 0; i < mapping._entries.Count; i++)
            {
                var entry = mapping._entries[i];
                if (entry?.Prefab == null) continue;
                foreach (var fog in entry.Prefab.GetComponentsInChildren<FogMapElement>(true)) AddFog(fog);
            }
        var zones = new Dictionary<IntPtr,ZoneDefinition>();
        void AddZone(ZoneDefinition zone)
        {
            if(zone!=null&&!zones.ContainsKey(zone.Pointer))zones.Add(zone.Pointer,zone);
        }
        foreach (var data in Resources.FindObjectsOfTypeAll<ZoneMapData>())
            AddZone(data?.PrimaryZone);
        var registeredZones=_app.Context.MapDirector.ZoneMappings;
        for(int i=0;registeredZones!=null&&i<registeredZones.Count;i++)AddZone(registeredZones[i]?.Zone);
        var events = new Dictionary<IntPtr,StaticGameEvent>();
        void AddEvent(StaticGameEvent gameEvent)
        {
            if(gameEvent!=null&&!events.ContainsKey(gameEvent.Pointer))events.Add(gameEvent.Pointer,gameEvent);
        }
        foreach(var fog in fogs)AddEvent(fog.FogRevealEvent);
        var nodes=Resources.FindObjectsOfTypeAll<MapNodeActivator>();
        foreach(var node in nodes)if(node!=null)AddEvent(node._fogRevealEvent);
        MapResult=$"assets: zones={zones.Count} fogObjects={fogs.Count} events={events.Count}";
        // A constructed MapUI can precede its asynchronous page/fog assets.
        // Zero events is NOT a successful unlock. Wait without mutating first.
        if(zones.Count==0||events.Count==0)
        {
            if(Time.unscaledTime>_mapDeadline)
            {Reset();throw new InvalidOperationException("Нативные данные карты не готовы: "+MapResult);}
            return;
        }
        var director = _app.Context.eventDirector;
        try
        {
            foreach(var zone in zones.Values)_app.Context.zoneUnlockEvent.RaiseEventForData(zone);
            foreach(var gameEvent in events.Values)director.RaiseEvent(gameEvent.Cast<IGameEvent>());
            foreach (var fog in fogs)
                if (fog.gameObject.scene.IsValid() && fog.gameObject.scene.isLoaded) fog.SetRevealState(true);
            foreach (var node in nodes)
                if (node!=null&&node.gameObject.scene.IsValid()&&node.gameObject.scene.isLoaded) node.UpdateHologramState();
            // This is a runtime view list, never a shared prefab/asset.
            if(mapping?._entries!=null)
                for(int i=0;i<mapping._entries.Count;i++)
                {
                    var map=mapping._entries[i]?.Map;
                    if(map!=null&&!ui._viewModel._availableMaps.Contains(map))ui._viewModel._availableMaps.Add(map);
                }
            foreach (MapUIFogReveal reveal in Resources.FindObjectsOfTypeAll<MapUIFogReveal>())
                if (reveal != null && reveal.gameObject.scene.IsValid() && reveal.gameObject.scene.isLoaded) reveal.RevealKnownZones();
            ui._viewModel.PopulateFilters();
            RevealedZones=zones.Count;RevealedFogEvents=events.Count;
            MapResult=$"complete: zones={RevealedZones} fogEvents={RevealedFogEvents}";
        }
        finally { Reset(); }
        _app.Notify("Атлас открыт: зон — " + RevealedZones + ", событий тумана — " + RevealedFogEvents + ". Подписи локаций и все нативные страницы карты доступны.");
    }

    internal void Reset() { _mapPending = false; _mapSession = IntPtr.Zero; }
    internal bool MapPending => _mapPending;
}
