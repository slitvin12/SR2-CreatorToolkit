using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppMonomiPark.ScriptedValue;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Il2CppMonomiPark.SlimeRancher.World;
using UnityEngine;

namespace CreatorToolkit;

internal sealed class WorldTools
{
    internal sealed class WeatherEntry
    {
        internal WeatherPatternDefinition Pattern;
        internal ZoneDefinition Zone;
        internal IWeatherState State;
        internal string Name, Search;
        internal int Tier;
    }
    private sealed class Frozen
    {
        internal Rigidbody Body;
        internal RigidbodyConstraints Constraints;
        internal Vector3 Velocity, AngularVelocity;
        internal readonly List<Behaviour> Disabled = new();
    }
    private readonly ToolkitMod _app;
    internal bool FreezeTime { get; private set; }
    internal bool FreezeMobs { get; private set; }
    internal bool DisableTarr { get; private set; }
    private WorldModel _clockModel;
    private bool _clockPaused;
    private ScriptedBool _tarr;
    private bool _tarrOriginal;
    private readonly Dictionary<IntPtr, Frozen> _frozen = new();
    private readonly List<IntPtr> _retiredFrozen = new();
    private float _nextScan, _nextWeather;
    private IntPtr _resolvedPrimaryZone,_resolvedWeatherRegistry;
    private ZoneDefinition _resolvedWeatherZone;
    private bool _resolvedMappingKnown;
    private ulong _resolvedSceneStamp;
    private float _resolvedUntil;
    private readonly HashSet<IntPtr> _registeredWeatherZones=new();
    internal int WeatherNegativeCacheHits {get;private set;}
    internal int WeatherRegistryFallbackReads {get;private set;}
    internal int WeatherRegistryCoverageFailures {get;private set;}
    internal int WeatherZoneDiscoveryScans {get;private set;}
    internal readonly List<WeatherEntry> Weather = new();
    internal ZoneDefinition WeatherZone;
    internal string ZoneName = "—";
    internal readonly List<string> ActiveWeather = new();
    internal string ActiveWeatherSummary = "Активная погода ещё не прочитана";
    internal int NativeDirectorStates {get;private set;}
    internal int FrozenCount => _frozen.Count;

    internal WorldTools(ToolkitMod app) => _app = app;

    internal void Tick()
    {
        if (FreezeTime && _clockModel != null) _clockModel.pauseWorldTime = true;
        if (DisableTarr && _tarr != null) _tarr.Value = false;
        if (FreezeMobs && Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 0.75f; FreezeLoadedSlimes(); }
    }

    internal void TickWeather()
    {
        if (Time.unscaledTime >= _nextWeather) { _nextWeather = Time.unscaledTime + 2f; RefreshWeather(false); }
    }

    internal void SetFreezeTime(bool enabled)
    {
        if (enabled == FreezeTime) return;
        if (enabled)
        {
            var director = _app.Context.TimeDirector;
            if (director.IsFastForwarding()) director.ForceFastForwardEnd();
            _clockModel = director._worldModel;
            if (_clockModel == null) throw new InvalidOperationException("Модель мирового времени недоступна.");
            _clockPaused = _clockModel.pauseWorldTime;
            _clockModel.pauseWorldTime = true;
            FreezeTime = true;
        }
        else
        {
            FreezeTime = false;
            if (_clockModel != null && !_clockModel.WasCollected) _clockModel.pauseWorldTime = _clockPaused;
            _clockModel = null;
        }
        _app.Notify(enabled ? "Мировое время заморожено. Физика и интерфейс продолжают работать." : "Мировое время восстановлено.");
    }

    internal float Hour()
    {
        try { return Mathf.Repeat(_app.Context.TimeDirector.CurrHour(),24f); } catch { return 0f; }
    }

    internal void AdvanceHours(float hours)
    {
        var time = _app.Context.TimeDirector;
        var model = time._worldModel;
        model.worldTime = SafetyPolicy.ForwardHour(time.WorldTime(), time.HoursFromNow(hours));
        _app.Notify("Время вперёд на " + hours + " ч.");
    }

    internal void SetHour(float hour)
    {
        var time = _app.Context.TimeDirector;
        // Forward-only: rewinding saved world time can break crop growth, cooldowns and weather history.
        float requested=Mathf.Clamp(hour,0f,23.99f);
        float delta=Mathf.Repeat(requested-Hour(),24f);
        // Float CurrHour can put an exact boundary a few ulps BEFORE midnight
        // (23.999998 instead of 00:00). Land just after the requested boundary,
        // by 0.0001 game hour, without ever moving saved time backwards.
        double target=time.HoursFromNow(delta+.0001f);
        time._worldModel.worldTime = SafetyPolicy.ForwardHour(time.WorldTime(), target);
        _app.Notify("Установлено ближайшее время суток: " + hour.ToString("F1") + " ч.");
    }

    internal void SetDisableTarr(bool disabled)
    {
        if (disabled == DisableTarr) return;
        if (disabled)
        {
            _tarr = GameContext.Instance.GameSettingsDirector.tarrEnabled;
            if (_tarr == null) throw new InvalidOperationException("Нативная настройка Варров недоступна.");
            _tarrOriginal = _tarr.Value;
            _tarr.Value = false;
            DisableTarr = true;
        }
        else
        {
            DisableTarr = false;
            if (_tarr != null && !_tarr.WasCollected) _tarr.Value = _tarrOriginal;
            _tarr = null;
        }
        _app.Notify(disabled ? "Появление Варров отключено. Уже существующие Варры не удаляются." : "Нативная настройка Варров восстановлена.");
    }

    internal void SetFreezeMobs(bool enabled)
    {
        if (enabled == FreezeMobs) return;
        FreezeMobs = enabled;
        if (enabled) FreezeLoadedSlimes(); else RestoreMobs();
        _app.Notify(enabled ? "ИИ и физика загруженных слаймов заморожены." : "ИИ и физика слаймов восстановлены.");
    }

    private void FreezeLoadedSlimes()
    {
        // A long-lived frame profile can cross many streamed regions. Release
        // snapshots for destroyed/unloaded/pooled bodies rather than retaining
        // every previously encountered slime until the profile is disabled.
        _retiredFrozen.Clear();
        foreach(var pair in _frozen)
        {
            var body=pair.Value.Body;
            if(body==null||body.WasCollected||!body.gameObject.scene.IsValid()||
                !body.gameObject.scene.isLoaded||!body.gameObject.activeInHierarchy)
            {
                RestoreFrozen(pair.Value);
                _retiredFrozen.Add(pair.Key);
            }
        }
        foreach(var key in _retiredFrozen)_frozen.Remove(key);
        _retiredFrozen.Clear();
        foreach (SubbehaviourPlexer plexer in Resources.FindObjectsOfTypeAll<SubbehaviourPlexer>())
        {
            if (plexer == null || plexer.WasCollected || plexer._emotions == null || !plexer.gameObject.scene.IsValid() || !plexer.gameObject.scene.isLoaded || !plexer.gameObject.activeInHierarchy) continue;
            var body = plexer._body ?? plexer.GetComponentInParent<Rigidbody>();
            if (body == null || _frozen.ContainsKey(body.Pointer)) continue;
            var frozen = new Frozen { Body = body, Constraints = body.constraints, Velocity = body.velocity, AngularVelocity = body.angularVelocity };
            _frozen.Add(body.Pointer, frozen);
            foreach (var ai in body.GetComponentsInChildren<Subbehaviour>(true))
            {
                if (ai != null && ai.enabled) { frozen.Disabled.Add(ai); ai.enabled = false; }
            }
            foreach (var eater in body.GetComponentsInChildren<SlimeEat>(true))
            {
                if (eater != null && eater.enabled) { frozen.Disabled.Add(eater); eater.enabled = false; }
            }
            if (!body.isKinematic) body.velocity = body.angularVelocity = Vector3.zero;
            body.constraints = RigidbodyConstraints.FreezeAll;
            body.Sleep();
        }
    }

    private void RestoreMobs()
    {
        foreach (var snapshot in _frozen.Values)
            RestoreFrozen(snapshot);
        _frozen.Clear();_retiredFrozen.Clear();
    }

    private void RestoreFrozen(Frozen snapshot)
    {
            try
            {
                if (snapshot.Body != null && !snapshot.Body.WasCollected)
                {
                    snapshot.Body.constraints = snapshot.Constraints;
                    if (!snapshot.Body.isKinematic)
                    {
                        snapshot.Body.velocity = snapshot.Velocity;
                        snapshot.Body.angularVelocity = snapshot.AngularVelocity;
                    }
                    snapshot.Body.WakeUp();
                }
            }
            catch (Exception e) { _app.Fail("restore slime physics", e); }
            foreach (var ai in snapshot.Disabled) { try { if (ai != null && !ai.WasCollected) ai.enabled = true; } catch { } }
            snapshot.Disabled.Clear();snapshot.Body=null;
    }

    private static ulong WeatherSceneStamp()
    {
        ulong stamp=14695981039346656037UL;
        for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
            stamp=M20DiscoveryPolicy.AppendScene(stamp,UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).handle);
        return stamp;
    }
    internal void InvalidateWeatherResolution()
    {
        _resolvedMappingKnown=false;_resolvedWeatherZone=null;
        _resolvedPrimaryZone=_resolvedWeatherRegistry=IntPtr.Zero;_resolvedUntil=0;
        _registeredWeatherZones.Clear();
    }
    internal ZoneDefinition WeatherZoneForAudit(bool force)=>ResolveWeatherZone(force);
    internal string WeatherResolutionDiagnostics()=>
        "WeatherMapScans: "+WeatherZoneDiscoveryScans+"\nWeatherNegativeCacheHits: "+WeatherNegativeCacheHits+
        "\nWeatherRegistryFallbackReads: "+WeatherRegistryFallbackReads+"\nWeatherRegistryCoverageFailures: "+WeatherRegistryCoverageFailures+"\n";
    private ZoneDefinition ResolveWeatherZone(bool force=false)
    {
        var ctx=_app.Context;var registry=ctx?.WeatherRegistry;
        if(registry==null||registry.WasCollected||!registry.Initialized||registry._zones==null)return null;
        ZoneDefinition current=ctx?.PlayerZoneTracker?.GetCurrentZone()??ctx?.PlayerZoneTracker?.GetLatestZone();
        if(current!=null&&!current.WasCollected)
        {
            ulong stamp=WeatherSceneStamp();
            bool cached=_resolvedPrimaryZone==current.Pointer&&_resolvedWeatherRegistry==registry.Pointer&&
                M21WeatherPolicy.Reuse(_resolvedMappingKnown,force,stamp==_resolvedSceneStamp,Time.unscaledTime,_resolvedUntil,_resolvedWeatherZone!=null)&&
                (_resolvedWeatherZone==null||!_resolvedWeatherZone.WasCollected&&registry._zones.ContainsKey(_resolvedWeatherZone));
            if(!cached)
            {
                ZoneDefinition resolved=null;WeatherZoneDiscoveryScans++;
                foreach(ZoneMapData map in Resources.FindObjectsOfTypeAll<ZoneMapData>())
                    if(map!=null&&!map.WasCollected&&map.PrimaryZone==current&&map.WeatherZone!=null&&registry._zones.ContainsKey(map.WeatherZone))
                    {resolved=map.WeatherZone;break;}
                if(resolved==null&&registry._zones.ContainsKey(current))resolved=current;
                // A missing direct mapping is cached too, but NOT its fallback
                // director. Native registry directors are reread on every call.
                _resolvedPrimaryZone=current.Pointer;_resolvedWeatherRegistry=registry.Pointer;
                _resolvedSceneStamp=stamp;_resolvedWeatherZone=resolved;
                _resolvedUntil=Time.unscaledTime+M21WeatherPolicy.MappingCacheSeconds;_resolvedMappingKnown=true;
            }
            else if(_resolvedWeatherZone==null)WeatherNegativeCacheHits++;
            if(_resolvedWeatherZone!=null)return _resolvedWeatherZone;
        }
        return ResolveRegistryWeatherZone(registry);
    }
    internal ZoneDefinition WeatherFallbackForAudit()=>ResolveRegistryWeatherZone(_app.Context.WeatherRegistry);
    private ZoneDefinition ResolveRegistryWeatherZone(WeatherRegistry registry)
    {
        WeatherRegistryFallbackReads++;
        // Registry-owned ZoneConfigList is the InitModel input. Indexed typed
        // access avoids a global scene census and covariant IEnumerable casts.
        // Complete key coverage is mandatory: never select from a partial list.
        var configs=registry.ZoneConfigList;int count=configs?.Count??-1;
        if(count<0||count>M21WeatherPolicy.MaxConfigZones){WeatherRegistryCoverageFailures++;return null;}
        _registeredWeatherZones.Clear();ZoneDefinition live=null;
        M21WeatherPolicy.UniqueZone unique=default;
        for(int i=0;i<count;i++)
        {
            var config=configs[i];var zone=config==null||config.WasCollected?null:config.Zone;
            if(zone==null||zone.WasCollected||!registry._zones.TryGetValue(zone,out var data)||data==null||data.WasCollected||!_registeredWeatherZones.Add(zone.Pointer))continue;
            var director=data.Director;
            if(director==null||director.WasCollected||!director.enabled||!director.gameObject.activeInHierarchy||
                !director.gameObject.scene.IsValid()||!director.gameObject.scene.isLoaded)continue;
            var actual=director.Zone;
            if(actual==null||actual.WasCollected||actual.Pointer!=zone.Pointer){WeatherRegistryCoverageFailures++;return null;}
            unique.Add(actual.Pointer.ToInt64());live=actual;
        }
        if(!M21WeatherPolicy.CompleteRegistry(count,_registeredWeatherZones.Count,registry._zones.Count))
        {WeatherRegistryCoverageFailures++;return null;}
        return unique.Ambiguous?null:live;
    }

    internal void RefreshWeather()=>RefreshWeather(true);
    private void RefreshWeather(bool force)
    {
        bool observe=_app.ModProfiler?.Capturing==true;
        long started=observe?System.Diagnostics.Stopwatch.GetTimestamp():0,bytes=observe?GC.GetAllocatedBytesForCurrentThread():0;
        ZoneDefinition zone = ResolveWeatherZone(force);
        if(observe)_app.ModProfiler.RecordSection("weather-zone-resolve",System.Diagnostics.Stopwatch.GetTimestamp()-started,GC.GetAllocatedBytesForCurrentThread()-bytes);
        started=observe?System.Diagnostics.Stopwatch.GetTimestamp():0;bytes=observe?GC.GetAllocatedBytesForCurrentThread():0;
        RefreshActiveWeather(zone);
        if(observe)_app.ModProfiler.RecordSection("weather-active-read",System.Diagnostics.Stopwatch.GetTimestamp()-started,GC.GetAllocatedBytesForCurrentThread()-bytes);
        if (zone == WeatherZone && Weather.Count > 0) return;
        // DEBUG_GetAllStates exposes a covariant native HashSet through IEnumerable;
        // its interop enumerator/cast paths failed. Read the typed transition graph by index.
        // Snapshot before metadata callbacks, and never cache a partially built zone.
        WeatherZone = null;
        Weather.Clear();
        ZoneName = zone != null ? zone.Name?.GetLocalizedString() : "В этой зоне погодные режимы недоступны";
        if(string.IsNullOrWhiteSpace(ZoneName)||ZoneName.StartsWith("!!"))ZoneName=zone?.DebugNameKey??"—";
        if (zone == null) return;
        var nativePatterns = _app.Context.WeatherRegistry.DEBUG_GetZoneWeathers(zone);
        var patterns = new List<WeatherPatternDefinition>();
        for (int i = 0; i < nativePatterns.Count; i++) patterns.Add(nativePatterns[i]);
        var entries = new List<WeatherEntry>();
        foreach (var pattern in patterns)
        {
            if (pattern == null) continue;
            var states = new List<IWeatherState>();
            var seen = new HashSet<IntPtr>();
            void AddState(WeatherStateDefinition state)
            {
                if (state != null && seen.Add(state.Pointer)) states.Add(state.Cast<IWeatherState>());
            }
            var starting = pattern.StartingTransitions;
            if (starting != null)
                for (int i = 0; i < starting.Count; i++)
                    if (starting[i] != null) AddState(starting[i].ToState);
            var running = pattern.RunningTransitions;
            if (running != null)
                for (int i = 0; i < running.Count; i++)
                {
                    var group = running[i];
                    if (group == null) continue;
                    AddState(group.FromState);
                    var transitions = group.Transitions;
                    if (transitions != null)
                        for (int j = 0; j < transitions.Count; j++)
                            if (transitions[j] != null) AddState(transitions[j].ToState);
                }
            string identity = pattern.name + " " + (pattern.Metadata != null ? pattern.Metadata.AnalyticsName + " " + pattern.Metadata.ReferenceId : "");
            foreach (IWeatherState state in states)
            {
                if (state == null) continue;
                string stateName = state.GetName();
                entries.Add(new WeatherEntry { Zone = zone, Pattern = pattern, State = state, Tier = state.GetMapTier(), Name = pattern.name + " / " + stateName, Search = (identity + " " + stateName).ToLowerInvariant() });
            }
        }
        Weather.AddRange(entries);
        WeatherZone = zone;
    }

    private void RefreshActiveWeather(ZoneDefinition zone)
    {
        ActiveWeather.Clear();NativeDirectorStates=0;
        if(zone==null){ActiveWeatherSummary="Погодный директор текущей зоны недоступен";return;}
        var registry=_app.Context.WeatherRegistry;
        var running=registry.DEBUG_GetRunningWeather(zone);
        for(int i=0;running!=null&&i<running.Count;i++)
        {
            var pattern=running[i];var state=pattern?.CurrentState();
            if(state==null)continue;
            string title=(pattern.GetDefinition()?.name??"Погода")+" / "+state.GetName()+" · уровень "+state.GetMapTier();
            ActiveWeather.Add(title);
        }
        if(registry._zones.TryGetValue(zone,out var data)&&data?.Director!=null)
            NativeDirectorStates=data.Director._runningStates?.Count??0;
        ActiveWeatherSummary=ActiveWeather.Count==0?(NativeDirectorStates==0?
            "Сейчас: ясно · активных нативных явлений нет":
            "Завершение погоды · нативных эффектов ещё активно: "+NativeDirectorStates):
            "Сейчас: "+string.Join(" · ",ActiveWeather);
    }
    internal bool ClimateClearForTest()
    {
        RefreshWeather();
        return ActiveWeather.Count==0&&NativeDirectorStates==0;
    }

    internal WeatherEntry FindPreset(string preset)
    {
        WeatherEntry result = null;
        int best = int.MinValue;
        foreach (var entry in Weather)
        {
            string text = entry.Search;
            bool slime = text.Contains("slime") || text.Contains("слайм");
            bool rain = text.Contains("rain") || text.Contains("дожд");
            bool thunder = text.Contains("thunder") || text.Contains("lightning") || text.Contains("гроза") || text.Contains("storm");
            bool match = preset switch
            {
                "rain" => rain && !slime && !thunder,
                "thunder" => thunder && !slime && !text.Contains("wind"),
                "slime" => slime && rain,
                "pollen" => text.Contains("pollen") || text.Contains("пыльц"),
                "wind" => text.Contains("wind") || text.Contains("ветер") || text.Contains("cyclone"),
                "lava" => text.Contains("lava") || text.Contains("лав"),
                "solar" => text.Contains("solar") || text.Contains("sunflare") || text.Contains("sun_flare") || text.Contains("солнеч"),
                _ => false
            };
            // Map tier controls the map icon, not whether the native state is usable.
            // Slime Rain is registered with tier 0 and must remain selectable.
            if (!match) continue;
            int score = preset == "rain" ? -entry.Tier : entry.Tier;
            if (score > best) { best = score; result = entry; }
        }
        return result;
    }

    internal void SetWeather(WeatherEntry entry)
    {
        if (entry == null || WeatherZone == null) throw new InvalidOperationException("Такой режим не зарегистрирован для текущей зоны. Выберите нативный режим ниже.");
        if (entry.Zone != ResolveWeatherZone(true)) throw new InvalidOperationException("Зона изменилась. Обновите погодный каталог и выберите режим заново.");
        StopCurrentWeather();
        _app.Context.WeatherRegistry.DEBUG_SetZoneWeather(WeatherZone, entry.Pattern, entry.State);
        RefreshActiveWeather(WeatherZone);
        _app.Notify("Погода: " + entry.Name + " • " + ZoneName);
    }

    internal void ClearWeather()
    {
        RefreshWeather();
        StopCurrentWeather();
        RefreshActiveWeather(WeatherZone);
        _app.Notify(NativeDirectorStates==0?
            "Текущая погода очищена. Будущие нативные прогнозы сохранены.":
            "Нативная погода завершается. Будущие прогнозы сохранены.");
    }
    private void StopCurrentWeather()
    {
        var registry = _app.Context.WeatherRegistry;
        if (WeatherZone == null) throw new InvalidOperationException("Погода в текущей зоне недоступна.");
        registry._zones.TryGetValue(WeatherZone,out var zoneData);
        int directorBefore=zoneData?.Director?._runningStates?.Count??0;
        var running = registry.DEBUG_GetRunningWeather(WeatherZone);
        var copy = new List<IWeatherPattern>();
        for (int i = 0; i < running.Count; i++) copy.Add(running[i]);
        foreach (var pattern in copy)
        {
            var state = pattern.CurrentState();
            if (state != null) registry.StopPatternState(WeatherZone, pattern, state);
            // StopPatternState exits the state but leaves its instance in the
            // zone's current list. Native Register/UnregisterDirector later
            // dereferences CurrentState(), so remove the finished instance too.
            var definition=pattern.GetDefinition();
            if(definition!=null)registry.RemoveWeatherPatternInstance(WeatherZone,definition);
        }
        // DEBUG_SetZoneWeather can replace a pattern while the scene's director
        // still owns a previous effect. Stop each remaining native state using
        // its real zone parameters, never clear the director's private list.
        // Immediate is a documented native argument, not a forced shader edit.
        var director=zoneData?.Director;
        if(director!=null&&zoneData.Parameters!=null&&director._runningStates!=null)
        {
            var states=new List<IWeatherState>();
            for(int i=0;i<director._runningStates.Count;i++)states.Add(director._runningStates[i]);
            foreach(var state in states)if(state!=null)director.StopState(state,zoneData.Parameters,true);
        }
        if (registry._model._zoneDatas.TryGetValue(WeatherZone, out WeatherModel.ZoneData data))
        {
            double now = _app.Context.TimeDirector.WorldTime();
            for (int i = data.Forecast.Count - 1; i >= 0; i--)
                if (data.Forecast[i].StartTime <= now) data.Forecast.RemoveAt(i);
        }
        RefreshActiveWeather(WeatherZone);
        if(_app.Audit.Active)_app.LoggerInstance.Msg("Climate native stop zone="+WeatherZone.name+
            " patternsBefore="+copy.Count+" patternsAfter="+registry.DEBUG_GetRunningWeather(WeatherZone).Count+
            " directorBefore="+directorBefore+" directorAfter="+NativeDirectorStates);
    }

    internal void Reset()
    {
        try { SetFreezeTime(false); }
        finally
        {
            try { SetDisableTarr(false); }
            finally
            {
                FreezeMobs = false;
                try { RestoreMobs(); }
                finally
                {
                    Weather.Clear();
                    ActiveWeather.Clear();NativeDirectorStates=0;ActiveWeatherSummary="Активная погода ещё не прочитана";
                    WeatherZone = null;
                    InvalidateWeatherResolution();
                    _nextScan = _nextWeather = 0f;
                    _app.Unlocks?.Reset();
                }
            }
        }
    }
}
