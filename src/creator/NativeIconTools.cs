using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using Il2CppMonomiPark.SlimeRancher.Player.Component;
using Il2CppMonomiPark.SlimeRancher.UI.Map;
using Il2CppMonomiPark.SlimeRancher.Weather;
using UnityEngine;

namespace CreatorToolkit;

// Borrow only real native Sprite assets. Never copy game artwork into the mod,
// create a geometric replacement, or retain a scene actor/renderer for an icon.
// Exception (owner request 2026-10-25/26): treasure pods, map nodes and
// research/phantom drones draw the exact Fandom wiki map markers first
// (CanonMapIconPolicy cells of FandomAtlas.rgba); the native aliases below stay
// only as the fallback when that atlas is missing.
internal sealed class NativeIconTools
{
    // +2 for the food / plort layer badges split from «Ресурсы» (was 192).
    internal const int AssetBudget=194;
    private readonly ToolkitMod _app;
    private readonly Dictionary<string,Sprite> _categories=new(StringComparer.Ordinal);
    private readonly Dictionary<string,Sprite> _rewards=new(StringComparer.Ordinal);
    private readonly Dictionary<string,Sprite> _roles=new(StringComparer.Ordinal);
    // Bounded exact-name artwork for find kinds; filled once per census.
    private readonly Dictionary<string,Sprite> _named=new(StringComparer.Ordinal);
    private readonly Dictionary<string,NativeIconPolicy.Resolution> _categoryInfo=new(StringComparer.Ordinal);
    private readonly Dictionary<string,NativeIconPolicy.Resolution> _rewardInfo=new(StringComparer.Ordinal);
    private readonly NativeIconPolicy.Resolution[] _tabInfo=new NativeIconPolicy.Resolution[ToolkitUi.TabCount];
    private sealed class OwnerBinding
    {
        internal NativeIconPolicy.OwnerBindingState State;
        private Sprite _sprite;
        internal void Add(Sprite sprite)
        {
            bool live=Live(sprite);
            NativeIconPolicy.AddOwnerIcon(ref State,live?sprite.Pointer:IntPtr.Zero);
            if(live&&_sprite==null)_sprite=sprite;
        }
        internal Sprite Icon=>NativeIconPolicy.OwnerAvailability(State)==NativeIconPolicy.Availability.Resolved&&Live(_sprite)?_sprite:null;
    }
    private NativeIconPolicy.CensusState _census;
    private int _focusEpoch=-1,_catalogGeneration=-1;
    private bool _rebindRequested,_sceneRebindRequested;
    private float _nextCensus;
    // Parent may call from its existing scene callback. This only queues work;
    // never scans while loading or inside the scene callback/OnGUI.
    internal void RequestRebind(){_rebindRequested=true;_sceneRebindRequested=true;}
    private readonly Dictionary<string,NativeIconPolicy.Resolution> _roleInfo=new(StringComparer.Ordinal);
    private readonly Dictionary<string,string> _roleDiagnostics=new(StringComparer.Ordinal);
    private readonly Dictionary<IntPtr,string> _weatherRoles=new();
    // Shared metadata aliases retain only pointers/role keys, never extra Sprite roots.
    private readonly Dictionary<IntPtr,string> _sharedWeatherRoles=new();
    private sealed class WeatherLookup
    {
        internal WeatherPatternDefinition Pattern;
        internal string Role;
        internal NativeIconPolicy.WeatherLookupState State;
        internal bool NeedsMetadata;
    }
    private readonly Dictionary<IntPtr,WeatherLookup> _weatherLookups=new();
    private float _nextWeatherRetry;
    private readonly HashSet<string> _ambiguousWeatherPresets=new(StringComparer.Ordinal);
    private readonly Sprite[] _tabs=new Sprite[ToolkitUi.TabCount];
    private IntPtr _session,_mapUi;
    internal int DiscoveryScans {get;private set;}
    internal int CategoryCount=>_categories.Count;
    internal int RewardCount=>_rewards.Count;
    internal int TabCount=>_tabs.Count(Live);
    internal int AssetRoots=>_categories.Count+_rewards.Count+TabCount+_roles.Count+_named.Count;
    internal string Status {get;private set;}="Оригинальные игровые иконки ещё не прочитаны";
    internal NativeIconTools(ToolkitMod app)=>_app=app;
    private static bool Live(Sprite sprite)=>sprite!=null&&!sprite.WasCollected&&sprite.texture!=null;
    internal void Tick(MapTools map)
    {
        if(!_app.Ready)return;
        IntPtr ui=map.Ui?.Pointer??IntPtr.Zero;
        if(_session!=_app.Session){Reset();_session=_app.Session;_rebindRequested=true;}
        int focus=NativeUiPriority.FocusGeneration,generation=_app.Actors.Generation;
        if(_focusEpoch!=focus||_catalogGeneration!=generation||ui!=IntPtr.Zero&&ui!=_mapUi)
            _rebindRequested=true;
        _focusEpoch=focus;_catalogGeneration=generation;
        if(ui!=IntPtr.Zero)_mapUi=ui;
        if(_sceneRebindRequested)
        {
            _weatherRoles.Clear();_sharedWeatherRoles.Clear();_weatherLookups.Clear();_ambiguousWeatherPresets.Clear();_nextWeatherRetry=0;
            foreach(string key in _roleInfo.Keys.Where(k=>k.StartsWith("Weather",StringComparison.Ordinal)).ToArray())
            {_roles.Remove(key);_roleInfo.Remove(key);_roleDiagnostics.Remove(key);}
            _sceneRebindRequested=false;
        }
        if(_rebindRequested){_census=default;_rebindRequested=false;}
        // At most two censuses per coalesced session/focus/map/catalog/scene
        // event; a global cooldown also prevents adjacent events scanning each
        // frame. Idle ticks only perform the existing bounded metadata retry.
        if(Time.unscaledTime<_nextCensus||!NativeIconPolicy.TryBeginCensus(ref _census,Time.unscaledTime))
        {RetryWeatherMetadata();return;}
        _nextCensus=Time.unscaledTime+NativeIconPolicy.CensusRetrySeconds;
        DiscoveryScans++;
        // General late rebinding must not reset the four-attempt weather epoch
        // or discard per-pattern metadata/ambiguity. Only a scene/session reset
        // clears those bounded lookups; genuine focus changes rearm pure policy.
        var weatherSnapshots=_weatherLookups.Values.Select(l=>(Lookup:l,Icon:Role(l.Role),Info:RoleInfo(l.Role))).ToArray();
        var presetSnapshots=new[]{"WeatherLava","WeatherSolar"}.Where(_roleInfo.ContainsKey)
            .Select(k=>(Key:k,Icon:Role(k),Info:RoleInfo(k))).ToArray();
        _categories.Clear();_rewards.Clear();_roles.Clear();_named.Clear();Array.Clear(_tabs);
        _roleInfo.Clear();_roleDiagnostics.Clear();
        var report=new List<string>();
        _categoryInfo.Clear();_rewardInfo.Clear();
        var native=new Dictionary<string,Sprite>(StringComparer.Ordinal);
        var ambiguousNative=new HashSet<string>(StringComparer.Ordinal);
        var unloadedNative=new HashSet<string>(StringComparer.Ordinal);
        void Add(string name,Sprite sprite,string source)
        {
            if(string.IsNullOrWhiteSpace(name))return;
            if(!Live(sprite)){unloadedNative.Add(name);return;}
            if(native.TryGetValue(name,out var previous)&&previous.Pointer!=sprite.Pointer)
                ambiguousNative.Add(name);
            else native[name]=sprite;
            report.Add(source+" "+name+" -> "+sprite.name);
        }
        var clearOwner=new OwnerBinding();var droneOwner=new OwnerBinding();
        foreach(var definition in Resources.FindObjectsOfTypeAll<MapMarkerDefinition>())
            if(definition!=null&&!definition.WasCollected)
            {
                var sprite=definition.MapIcon;
                Add(definition.name,sprite,"MapMarkerDefinition");
                if(string.Equals(definition.name,"WeatherMarkerDefinition",StringComparison.Ordinal))clearOwner.Add(sprite);
                if(string.Equals(definition.name,"DroneStationMarkerDefinition",StringComparison.Ordinal))droneOwner.Add(sprite);
            }
        var pediaOwners=new List<PediaEntry>();
        foreach(var entry in Resources.FindObjectsOfTypeAll<PediaEntry>())
            if(entry!=null&&!entry.WasCollected)
            {
                pediaOwners.Add(entry);
                Add(entry.name,entry.SmallIcon??entry.Icon,"PediaEntry");
                Add(entry.PersistenceSuffix,entry.Icon??entry.SmallIcon,"PediaSuffix");
            }
        var exactSprites=new Dictionary<string,Sprite>(StringComparer.Ordinal);
        var ambiguousSprites=new HashSet<string>(StringComparer.Ordinal);
        foreach(var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            if(sprite!=null&&!sprite.WasCollected)
            {
                Add(sprite.name,sprite,"Sprite");
                if(!Live(sprite))continue;
                if(exactSprites.TryGetValue(sprite.name,out var prior)&&prior.Pointer!=sprite.Pointer)
                    ambiguousSprites.Add(sprite.name);
                else exactSprites[sprite.name]=sprite;
            }
        // Keep only artwork named by the find-kind table: bounded, no per-frame scan.
        foreach(string name in MapRareIcons.AllNames)
            if(!ambiguousSprites.Contains(name)&&exactSprites.TryGetValue(name,out var kindArt)&&Live(kindArt))
                _named[name]=kindArt;
        NativeIconPolicy.Availability PickState(string[] aliases)=>
            aliases.Any(ambiguousNative.Contains)?NativeIconPolicy.Availability.Ambiguous:
            aliases.Any(unloadedNative.Contains)?NativeIconPolicy.Availability.NotLoaded:
            NativeIconPolicy.Availability.MissingNativeAsset;
        Sprite Pick(params string[] aliases)
        {
            // Duplicate exact identity is never repaired by fuzzy matching or
            // by selecting a different alias. Same native pointer is harmless.
            if(aliases.Any(ambiguousNative.Contains))return null;
            foreach(string alias in aliases)if(native.TryGetValue(alias,out var exact)&&Live(exact))return exact;
            return null;
        }
        void Category(string name,params string[] aliases)
        {
            var sprite=Pick(aliases);
            if(Live(sprite))_categories[name]=sprite;
            _categoryInfo[name]=new(name,Live(sprite)?NativeIconPolicy.Availability.Resolved:PickState(aliases),
                sprite?.name??"","native exact category aliases");
            report.Add("CATEGORY "+name+" = "+_categoryInfo[name]);
        }
        Category("Капсулы","iconMapTreasurePod","iconTreasurePod","iconMapPod","TreasurePod","treasure_pod");
        {
            const string name="Дроны";
            var sprite=droneOwner.Icon;
            if(Live(sprite))_categories[name]=sprite;
            _categoryInfo[name]=new(name,NativeIconPolicy.OwnerAvailability(droneOwner.State),sprite?.name??"",
                "MapMarkerDefinition[DroneStationMarkerDefinition].MapIcon; owner bindings="+droneOwner.State.Owners);
            report.Add("CATEGORY "+name+" = "+_categoryInfo[name]);
        }
        Category("Телепорты","WarpTechMarkerDefinition","iconGadgetTeleportGold","iconGadgetTeleportPink");
        // No dedicated map-node sprite exists in this build (the bundles only
        // carry nodeMapEntry objects). Borrowed stand-ins (the radar flask, the
        // Home-World globe) read as other objects, so map nodes are drawn from
        // the Fandom wiki map-node marker and deliberately have no native alias.
        report.Add("CATEGORY Ноды карты = Fandom atlas cell "+CanonMapIconPolicy.MapNode+" "+CanonMapIconPolicy.Source(CanonMapIconPolicy.MapNode)+" (no native alias)");
        Category("Двери","PlortDoorMapMarkerDefinition","iconMapPlortDoor","StabilizingGateMapMarkerDefinition");
        Category("Механизмы","iconPediaCategoryScience","PediaCategoryScience");
        Category("Ресурсы","iconCraftJellyStone_small","JellystoneCraft");
        // Food and plort spawn layers split from «Ресурсы»: the game's own
        // "everything" diet badge (hen, carrot, fruit) and the pink plort. If
        // the diet badge is ever ambiguous, the exact carrot item icon answers.
        Category(MapResourceLayerPolicy.Food,"iconCategoryEverything");
        if(!_categories.ContainsKey(MapResourceLayerPolicy.Food))Category(MapResourceLayerPolicy.Food,"iconVeggieCarrot_small");
        Category(MapResourceLayerPolicy.Plorts,"iconPlortPink_small");
        Category("Гордо","GordoMarkerDefinition","iconGordoPedia");
        // Containers and spawn surfaces had no artwork at all, so their markers
        // drew nothing once those layers were enabled.
        Category("Горшочки","iconDecorHoneyPot_small","iconDecorMainPottedPlant_small");
        Category("Спавнеры","iconGadgetMarkerSlime_small","iconPediaSlimeology");
        Category(CustomWaypointTools.Category,"marker_flag","BeaconMarkerDefinition");
        // Typed native owner references select art; unrelated atlas copies
        // sharing Sprite.name cannot invalidate these authoritative bindings.
        string[] tabNames={"Игрок","Спавн","Ресурсы","Мир","Открытия","Съёмка","Карта","Удобство"};
        string[] ownerNames={"Pink","Cotton","JellystoneCraft","Angler","Gold","Phosphor","Honey","Yolky"};
        string[] suffixes={"pink_slime","cotton_slime","jellystone_craft","angler_slime","gold_slime",
            "phosphor_slime","honey_slime","yolky_slime"};
        var registry=LookupDirector.GetIfReady();
        var bindings=new OwnerBinding[_tabs.Length];
        var ownerTypes=new IntPtr[_tabs.Length];
        var ownerConflicts=new bool[_tabs.Length];
        var ownerSeen=new bool[_tabs.Length];
        var ownerSources=new string[_tabs.Length];
        for(int i=0;i<bindings.Length;i++)bindings[i]=new OwnerBinding();
        bool registryReady=registry!=null&&registry._initialized&&registry._identifiableTypeByRefId!=null;
        var pediaIndex=_app.Context?.PediaDirector?._identDict;
        if(registryReady)
            foreach(var entry in pediaOwners)
            {
                string name=entry.name;
                for(int i=0;i<bindings.Length;i++)
                {
                    if(!string.Equals(name,ownerNames[i],StringComparison.Ordinal)||
                        !string.Equals(entry.PersistenceSuffix,suffixes[i],StringComparison.Ordinal))continue;
                    ownerSeen[i]=true;
                    try
                    {
                        var type=entry.TryCast<IdentifiablePediaEntry>()?.IdentifiableType;
                        if(type==null||type.WasCollected)continue;
                        string reference=type.ReferenceId;
                        if(string.IsNullOrEmpty(reference)||!registry._identifiableTypeByRefId.TryGetValue(reference,out var registered)||
                            registered==null||registered.WasCollected)continue;
                        // If the director has selected an entry for this type,
                        // ignore unrelated loaded copies instead of first-match.
                        PediaEntry selected=null;
                        if(pediaIndex!=null&&pediaIndex.TryGetValue(registered,out selected)&&
                            selected!=null&&!selected.WasCollected&&selected.Pointer!=entry.Pointer)continue;
                        if(registered.Pointer!=type.Pointer||!string.Equals(registered.ReferenceId,reference,StringComparison.Ordinal))
                        {ownerConflicts[i]=true;ownerSources[i]="native registry identity conflict for "+reference;continue;}
                        if(ownerTypes[i]!=IntPtr.Zero&&ownerTypes[i]!=registered.Pointer)ownerConflicts[i]=true;
                        else ownerTypes[i]=registered.Pointer;
                        string source="PediaEntry["+name+", "+suffixes[i]+"] -> registered ReferenceId="+reference;
                        Sprite sprite=entry.SmallIcon;source+=" -> SmallIcon";
                        if(!Live(sprite)){sprite=entry.Icon;source+=" -> same-owner Icon";}
                        if(!Live(sprite)){sprite=registered.Icon;source+=" -> registered type.Icon";}
                        if(!Live(sprite)){sprite=registered.icon;source+=" -> same-owner icon";}
                        bindings[i].Add(sprite);ownerSources[i]=source;
                        report.Add("TAB_OWNER "+tabNames[i]+" = "+source+"; sprite="+(Live(sprite)?sprite.name:"NOT_LOADED"));
                    }
                    catch(Exception e)
                    {bindings[i].Add(null);ownerSources[i]??="owner read failed: "+e.GetType().Name;}
                }
            }
        for(int i=0;i<_tabs.Length;i++)
        {
            var state=ownerConflicts[i]?NativeIconPolicy.Availability.Ambiguous:!registryReady||
                ownerSeen[i]&&bindings[i].State.Owners==0?NativeIconPolicy.Availability.NotLoaded:
                NativeIconPolicy.OwnerAvailability(bindings[i].State);
            _tabs[i]=state==NativeIconPolicy.Availability.Resolved?bindings[i].Icon:null;
            string source=ownerSources[i]??"PediaEntry["+ownerNames[i]+", "+suffixes[i]+"] -> native registry validation; no accepted binding";
            source+="; owner bindings="+bindings[i].State.Owners;
            _tabInfo[i]=new(tabNames[i],state,_tabs[i]?.name??"",source);
            report.Add("TAB "+tabNames[i]+" = "+(_tabs[i]?.name??"UNAVAILABLE")+" ["+_tabInfo[i]+"]");
        }
        // Every lookup now uses exact observed identities. Conflicting native
        // pointers fail closed even if another alias could supply artwork.
        foreach(string role in NativeIconPolicy.Roles)
        {
            var info=NativeIconPolicy.ResolveExact(role,name=>!ambiguousSprites.Contains(name)&&
                exactSprites.TryGetValue(name,out var sprite)&&Live(sprite));
            bool ambiguous=NativeIconPolicy.ExactNames(role).Any(ambiguousSprites.Contains);
            Sprite sprite=info.Resolved&&!ambiguous?exactSprites[info.AssetName]:null;
            var names=NativeIconPolicy.ExactNames(role);
            var state=ambiguous?NativeIconPolicy.Availability.Ambiguous:
                names.Count==0?NativeIconPolicy.Availability.DeclaredUnresolved:
                names.Any(unloadedNative.Contains)?NativeIconPolicy.Availability.NotLoaded:
                NativeIconPolicy.Availability.MissingNativeAsset;
            string source=info.Resolved?info.Source:names.Count==0?
                "no dedicated exact alias declared; native metadata only":
                "exact Sprite aliases: "+string.Join(", ",names);
            StoreRole(role,sprite,source,state);
        }

        StoreRole("WeatherClear",clearOwner.Icon,
            "MapMarkerDefinition[WeatherMarkerDefinition].MapIcon; owner bindings="+clearOwner.State.Owners,
            NativeIconPolicy.OwnerAvailability(clearOwner.State));
        var database=map.Capsules.Database;
        var definitions=new Dictionary<string,List<GadgetDefinition>>(StringComparer.Ordinal);
        foreach(var definition in Resources.FindObjectsOfTypeAll<GadgetDefinition>())
        {
            if(definition==null||definition.WasCollected||string.IsNullOrWhiteSpace(definition.name))continue;
            if(!definitions.TryGetValue(definition.name,out var matches))definitions[definition.name]=matches=new();
            if(!matches.Any(d=>d.Pointer==definition.Pointer))matches.Add(definition);
        }
        var components=new Dictionary<string,List<UpgradeComponent>>(StringComparer.Ordinal);
        foreach(var component in Resources.FindObjectsOfTypeAll<UpgradeComponent>())
        {
            if(component==null||component.WasCollected||string.IsNullOrWhiteSpace(component.name))continue;
            if(!components.TryGetValue(component.name,out var matches))components[component.name]=matches=new();
            if(!matches.Any(d=>d.Pointer==component.Pointer))matches.Add(component);
        }
        var lookup=LookupDirector.GetIfReady();
        foreach(var reward in database.Assets.Values)
        {
            Sprite sprite=null;
            var state=NativeIconPolicy.Availability.MissingNativeAsset;
            string source="exact native reward identity not present in census";
            if(reward.Kind=="Blueprint"&&definitions.TryGetValue(reward.AssetName,out var defs))
            {
                state=defs.Count==1?NativeIconPolicy.Availability.NotLoaded:NativeIconPolicy.Availability.Ambiguous;
                source="GadgetDefinition exact name; distinct native objects="+defs.Count;
                if(defs.Count==1)sprite=defs[0].Icon;
            }
            else if(reward.Kind=="Component"&&components.TryGetValue(reward.AssetName,out var parts))
            {
                state=parts.Count==1?NativeIconPolicy.Availability.NotLoaded:NativeIconPolicy.Availability.Ambiguous;
                source="UpgradeComponent exact name; distinct native objects="+parts.Count;
                if(parts.Count==1)sprite=parts[0].Icon;
            }
            else if(reward.Kind=="Item")
            {
                // Capsule data points at an IdentifiableType asset, NOT at a
                // prefab name. Resolve that exact asset identity, then require
                // the actual native ReferenceId registry and pointer to agree.
                var items=_app.Actors.Catalog.Where(e=>!e.IsPrototype&&!e.IsRadiant&&e.Type!=null&&
                    !e.Type.WasCollected&&string.Equals(e.Type.name,reward.AssetName,StringComparison.Ordinal)&&
                    string.Equals(e.Reference,e.Type.ReferenceId,StringComparison.Ordinal))
                    .DistinctBy(e=>e.Type.Pointer).Take(2).ToArray();
                source="IdentifiableType exact asset name / native ReferenceId";
                if(items.Length>1)state=NativeIconPolicy.Availability.Ambiguous;
                else if(items.Length==1)
                {
                    var item=items[0];
                    if(lookup!=null&&lookup._initialized&&lookup._identifiableTypeByRefId!=null&&
                        !string.IsNullOrWhiteSpace(item.Reference)&&
                        lookup._identifiableTypeByRefId.TryGetValue(item.Reference,out var registered)&&
                        registered!=null&&!registered.WasCollected)
                    {
                        if(registered.Pointer!=item.Type.Pointer)
                        {state=NativeIconPolicy.Availability.Ambiguous;source+="; exact ID registry pointer conflicts";}
                        else
                        {sprite=registered.Icon??registered.icon;state=NativeIconPolicy.Availability.NotLoaded;source+=" "+item.Reference;}
                    }
                    else {state=NativeIconPolicy.Availability.NotLoaded;source+="; exact ID registry unavailable";}
                }
                else if(_app.Actors.Catalog.Count==0||lookup==null||!lookup._initialized)
                {state=NativeIconPolicy.Availability.NotLoaded;source+="; native catalog not ready";}
                // Never hide ambiguous/unregistered item identity with Pedia art.
            }
            if(reward.Kind!="Item"&&state!=NativeIconPolicy.Availability.Ambiguous&&
                !Live(sprite)&&!string.IsNullOrWhiteSpace(reward.PediaSuffix))
            {
                sprite=Pick(reward.PediaSuffix);
                state=PickState(new[]{reward.PediaSuffix});source+="; exact PediaSuffix "+reward.PediaSuffix;
            }
            if(Live(sprite)){_rewards[reward.Key]=sprite;state=NativeIconPolicy.Availability.Resolved;}
            _rewardInfo[reward.Key]=new(reward.Key,state,Live(sprite)?sprite.name:"",source);
            report.Add("REWARD "+reward.Key+" "+reward.AssetName+" = "+_rewardInfo[reward.Key]);
        }
        foreach(var saved in weatherSnapshots)
        {
            var sprite=saved.Icon;
            // A borrowed generic wind fallback is revalidated against this
            // census; it cannot conceal a newly ambiguous exact native role.
            if(saved.Lookup.NeedsMetadata&&saved.Info.Source.Contains("semantic native WeatherWind role fallback",StringComparison.Ordinal))
                sprite=Role("WeatherWind");
            StoreRole(saved.Lookup.Role,sprite,saved.Info.Source,Live(sprite)?saved.Info.Availability:NativeIconPolicy.Availability.NotLoaded);
        }
        foreach(var saved in presetSnapshots)StoreRole(saved.Key,saved.Icon,saved.Info.Source,saved.Info.Availability);
        // Text-only coverage extends beyond the retained 24 lookups without
        // raising sprite roots, retry budgets or invoking weather/game actions.
        var observedPatterns=new HashSet<IntPtr>();int weatherReported=0,weatherRetained=0;
        foreach(var pattern in Resources.FindObjectsOfTypeAll<WeatherPatternDefinition>())
        {
            if(pattern==null||pattern.WasCollected||!observedPatterns.Add(pattern.Pointer))continue;
            string role=WeatherRole(pattern);
            if(role!=NativeIconPolicy.WeatherUnavailable)weatherRetained++;
            if(weatherReported>=NativeIconPolicy.WeatherDiagnosticBudget)continue;
            weatherReported++;
            string exactName=System.Text.Json.JsonSerializer.Serialize(pattern.name);
            try
            {
                var metadata=pattern.Metadata;
                var entry=metadata!=null&&!metadata.WasCollected?metadata.PediaEntry:null;
                if(entry!=null&&entry.WasCollected)entry=null;
                report.Add("WEATHER_OBSERVED name="+exactName+"; retainedRole="+role+
                    "; metadata="+System.Text.Json.JsonSerializer.Serialize(metadata==null||metadata.WasCollected?null:metadata.ReferenceId)+
                    "; analytics="+System.Text.Json.JsonSerializer.Serialize(metadata==null||metadata.WasCollected?null:metadata.AnalyticsName)+
                    "; pedia="+System.Text.Json.JsonSerializer.Serialize(entry?.name)+
                    "; suffix="+System.Text.Json.JsonSerializer.Serialize(entry?.PersistenceSuffix));
            }
            catch(Exception e){report.Add("WEATHER_OBSERVED name="+exactName+"; metadata=UNKNOWN; error="+e.GetType().Name);}
        }
        report.Add("WEATHER_COVERAGE observed="+observedPatterns.Count+"; reported="+weatherReported+
            "; diagnosticTruncated="+(observedPatterns.Count>weatherReported)+
            "; observedRetained="+weatherRetained+"; retainedLookups="+_weatherRoles.Count+"; sharedMetadataAliases="+_sharedWeatherRoles.Count+"; lookupBudget="+NativeIconPolicy.WeatherBudget+
            "; lookupCoverageTruncated="+(observedPatterns.Count>weatherRetained)+
            "; unretained/unreported patterns remain UNKNOWN, not missing native assets");
        foreach(var diagnostic in _roleDiagnostics)report.Add("ROLE "+diagnostic.Key+" = "+diagnostic.Value);
        if(AssetRoots>AssetBudget)throw new InvalidOperationException("Нативный каталог иконок превысил ограничение.");
        Status="Игровые спрайты: вкладки "+TabCount+" / 8 · категории "+CategoryCount+" · награды "+RewardCount+" / "+database.Assets.Count+
            " · находки "+_named.Count;
        try{File.WriteAllText(Path.Combine(_app.DataDirectory,"native-icons.txt"),"CreatorToolkit "+ToolkitMod.Version+
            " native icon census\n"+Status+"\nDiscoveryScans="+DiscoveryScans+"\nAssetRoots="+AssetRoots+
            "\nBorrowed native Sprite assets; no owned artwork textures; no actor roots.\n"+string.Join("\n",report));}
        catch(Exception e){_app.LoggerInstance.Warning("Native icon census: "+e.Message);}
    }
    private void StoreRole(string role,Sprite sprite,string source,
        NativeIconPolicy.Availability unavailable=NativeIconPolicy.Availability.DeclaredUnresolved)
    {
        if(Live(sprite)&&!_roles.ContainsKey(role)&&AssetRoots>=AssetBudget)
        {sprite=null;source="native asset budget reached";unavailable=NativeIconPolicy.Availability.BudgetExceeded;}
        bool resolved=Live(sprite);
        if(resolved)_roles[role]=sprite;else _roles.Remove(role);
        var state=resolved?NativeIconPolicy.Availability.Resolved:unavailable;
        string asset=resolved?sprite.name:"";
        _roleInfo[role]=new(role,state,asset,source??"");
        _roleDiagnostics[role]=resolved?asset+" ["+source+"]":
            state+": native icon unavailable"+(string.IsNullOrEmpty(source)?"":" ["+source+"]");
    }
    // No scans here: at most one direct metadata read per distinct pattern,
    // capped for this census/session. Returned keys can feed NativeRoleButton.
    internal string WeatherRole(WeatherPatternDefinition pattern)
    {
        if(pattern==null||pattern.WasCollected)return NativeIconPolicy.WeatherUnavailable;
        if(_weatherRoles.TryGetValue(pattern.Pointer,out var cached))return cached;
        if(_sharedWeatherRoles.TryGetValue(pattern.Pointer,out cached))return cached;
        {
            // Zone copies may point to the very same authoritative metadata.
            // Share that exact owner's role, never guess by a similar Sprite name.
            if(_sharedWeatherRoles.Count>=NativeIconPolicy.WeatherDiagnosticBudget)return NativeIconPolicy.WeatherUnavailable;
            try
            {
                var metadata=pattern.Metadata;
                if(metadata!=null&&!metadata.WasCollected)
                    foreach(var retained in _weatherLookups.Values)
                    {
                        var owner=retained.Pattern;
                        if(owner==null||owner.WasCollected)continue;
                        var existing=owner.Metadata;
                        if(existing==null||existing.WasCollected||
                            !NativeIconPolicy.SameWeatherMetadata(existing.Pointer,metadata.Pointer))continue;
                        _sharedWeatherRoles[pattern.Pointer]=retained.Role;
                        return retained.Role;
                    }
            }
            catch(Exception e){_app.LoggerInstance.Warning("Shared weather metadata: "+e.GetType().Name);}
        }
        if(_weatherRoles.Count>=NativeIconPolicy.WeatherBudget)return NativeIconPolicy.WeatherUnavailable;
        string role="WeatherNative"+_weatherRoles.Count;
        _weatherRoles.Add(pattern.Pointer,role);
        var lookup=new WeatherLookup{Pattern=pattern,Role=role};
        _weatherLookups.Add(pattern.Pointer,lookup);
        ReadWeatherMetadata(lookup);
        return role;
    }
    private void RetryWeatherMetadata()
    {
        if(Time.unscaledTime<_nextWeatherRetry)return;
        _nextWeatherRetry=Time.unscaledTime+.25f;
        foreach(var lookup in _weatherLookups.Values)
            if(Role(lookup.Role)==null)ReadWeatherMetadata(lookup);
            else if(lookup.NeedsMetadata)ReadWeatherMetadata(lookup);
    }
    private void ReadWeatherMetadata(WeatherLookup lookup)
    {
        var pattern=lookup.Pattern;
        if(pattern==null||pattern.WasCollected||
           !NativeIconPolicy.TryBeginWeatherLookup(ref lookup.State,NativeUiPriority.FocusGeneration,Time.unscaledTime))return;
        string role=lookup.Role;
        Sprite sprite=null;string source="WeatherPatternDefinition metadata has no live icon";
        string preset=NativeIconPolicy.WeatherPresetRole(pattern.name);
        try
        {
            var metadata=pattern.Metadata;
            if(metadata!=null&&!metadata.WasCollected)
            {
                var entry=metadata.PediaEntry;
                if(entry!=null&&entry.WasCollected)entry=null;
                preset=NativeIconPolicy.WeatherPresetRole(pattern.name+" "+metadata.AnalyticsName+" "+
                    metadata.ReferenceId+" "+(entry!=null?entry.name+" "+entry.PersistenceSuffix:""));
                sprite=NativeIconPolicy.SelectWeatherIcon(()=>metadata.Icon,
                    ()=>entry?.SmallIcon,()=>entry?.Icon,Live,out var selected);
                source="WeatherPatternDefinition "+pattern.name+" -> "+(selected switch
                {
                    NativeIconPolicy.WeatherIconSource.MetadataIcon=>"Metadata.Icon",
                    NativeIconPolicy.WeatherIconSource.PediaSmallIcon=>"Metadata.PediaEntry.SmallIcon",
                    NativeIconPolicy.WeatherIconSource.PediaIcon=>"Metadata.PediaEntry.Icon",
                    _=>"Metadata/PediaEntry"
                });
                if(selected==NativeIconPolicy.WeatherIconSource.Missing)source+=" (no live metadata/Pedia icon)";
            }
        }
        catch(Exception e){sprite=null;source="weather metadata read failed: "+e.GetType().Name;}
        lookup.NeedsMetadata=!Live(sprite);
        if(!Live(sprite))source+="; lookup "+lookup.State.Attempts+"/"+NativeIconPolicy.WeatherAttemptsPerEpoch+
            (lookup.State.Attempts>=NativeIconPolicy.WeatherAttemptsPerEpoch?"; waiting for next native opening":"; bounded retry pending");
        if(!Live(sprite)&&preset=="WeatherWind"&&Role("WeatherWind") is Sprite wind)
        {
            // Only the observed native wind artwork is a semantic fallback;
            // never a rain/tornado icon. Keep missing metadata in diagnostics.
            sprite=wind;source+="; semantic native WeatherWind role fallback -> "+wind.name;
        }
        StoreRole(role,sprite,source,NativeIconPolicy.Availability.NotLoaded);
        // Clear retains exact marker-owner diagnostics; Wind retains its native role;
        // per-pattern metadata may legitimately use a different small sprite.
        if(preset=="WeatherLava"||preset=="WeatherSolar")
        {
            var existing=Role(preset);
            var merge=NativeIconPolicy.MergeWeatherPreset(existing?.Pointer??IntPtr.Zero,
                Live(sprite)?sprite.Pointer:IntPtr.Zero,_ambiguousWeatherPresets.Contains(preset));
            if(merge==NativeIconPolicy.WeatherMerge.Ambiguous)
            {
                _ambiguousWeatherPresets.Add(preset);
                StoreRole(preset,null,"conflicting native weather metadata icons",NativeIconPolicy.Availability.Ambiguous);
            }
            else if(merge==NativeIconPolicy.WeatherMerge.Replace)StoreRole(preset,sprite,source);
            else if(existing==null)StoreRole(preset,null,source);
        }
    }
    // Snapshot availability is not runtime visual acceptance. A borrowed asset
    // may unload afterwards; Role/RoleDiagnostic check its current liveness.
    private static NativeIconPolicy.Resolution CurrentInfo(NativeIconPolicy.Resolution info,Sprite sprite)=>
        info.Resolved&&!Live(sprite)?info with { Availability=NativeIconPolicy.Availability.NotLoaded,
            Source=info.Source+"; borrowed native sprite unloaded" }:info;
    private static NativeIconPolicy.Resolution Undiscovered(string key)=>
        new(key??"",NativeIconPolicy.Availability.NotDiscovered,"","no census record; not a zone-preset decision");
    internal NativeIconPolicy.Resolution RoleInfo(string role)=>
        _roleInfo.TryGetValue(role??"",out var info)?CurrentInfo(info,Role(role)):Undiscovered(role);
    internal NativeIconPolicy.Resolution CategoryInfo(string category)=>
        _categoryInfo.TryGetValue(category??"",out var info)?CurrentInfo(info,Category(category)):Undiscovered(category);
    internal NativeIconPolicy.Resolution RewardInfo(string key)=>
        _rewardInfo.TryGetValue(key??"",out var info)?CurrentInfo(info,Reward(key)):Undiscovered(key);
    internal NativeIconPolicy.Resolution TabInfo(int index)=>
        index>=0&&index<_tabInfo.Length&&!string.IsNullOrEmpty(_tabInfo[index].Role)?
        CurrentInfo(_tabInfo[index],Tab(index)):Undiscovered("Tab "+index);
    private static string IconDiagnostic(NativeIconPolicy.Resolution info)=>
        info.Availability+": "+(info.Resolved?info.AssetName:"native icon unavailable")+" ["+info.Source+"]";
    internal string RoleDiagnostic(string role)=>IconDiagnostic(RoleInfo(role))+
        ((role??"").StartsWith("Weather",StringComparison.Ordinal)?"; zone preset availability is separate":"");
    internal string CategoryDiagnostic(string category)=>IconDiagnostic(CategoryInfo(category));
    internal string RewardDiagnostic(string key)=>IconDiagnostic(RewardInfo(key));
    internal Sprite Category(string category)=>_categories.TryGetValue(category??"",out var sprite)&&Live(sprite)?sprite:null;
    internal Sprite Tab(int index)=>index>=0&&index<_tabs.Length&&Live(_tabs[index])?_tabs[index]:null;
    internal Sprite Role(string role)=>_roles.TryGetValue(role??"",out var sprite)&&Live(sprite)?sprite:null;
    private Sprite Named(string name)=>
        string.IsNullOrEmpty(name)?null:_named.TryGetValue(name,out var sprite)&&Live(sprite)?sprite:null;
    private Sprite NamedFirst(string[] names)
    {
        if(names==null)return null;
        foreach(string name in names){var sprite=Named(name);if(sprite!=null)return sprite;}
        return null;
    }
    internal Sprite Reward(string key)=>_rewards.TryGetValue(key??"",out var sprite)&&Live(sprite)?sprite:null;
    internal void VisitUiTextures(Action<Texture2D> observe)
    {
        foreach(var sprite in _categories.Values)if(Live(sprite))observe(sprite.texture);
        foreach(var sprite in _named.Values)if(Live(sprite))observe(sprite.texture);
        foreach(var sprite in _rewards.Values)if(Live(sprite))observe(sprite.texture);
        foreach(var sprite in _tabs)if(Live(sprite))observe(sprite.texture);
        foreach(var sprite in _roles.Values)if(Live(sprite))observe(sprite.texture);
    }
    internal Sprite Point(MapTools.Point point)
    {
        if(point==null)return null;
        // A discovered source without borrowed art still needs a visible marker:
        // its own layer icon answers instead of drawing nothing.
        if(point.SourceObservation)
            return Live(point.SourceIcon)?point.SourceIcon:Category(point.Category);
        if(MapResourceLayerPolicy.Owns(point.Category))
        {
            if(Live(point.SourceIcon))return point.SourceIcon;
            string reference=string.IsNullOrEmpty(point.Contents)?point.Detail:point.Contents;
            var entry=_app.Actors.Catalog.FirstOrDefault(e=>e.Reference==reference);
            var sprite=entry?.Type?.Icon??entry?.Type?.icon;
            if(Live(sprite))return sprite;
        }
        if(point.Category==FandomMapMarkers.GatherCategory)
        {
            // Exact resource nodes: in-game item icon of the primary resource.
            if(Live(point.SourceIcon))return point.SourceIcon;
            string contents=point.Contents??"";int at=contents.IndexOf("IdentifiableType.",StringComparison.Ordinal);
            if(at>=0)
            {
                int end=contents.IndexOf(',',at);string reference=end<0?contents.Substring(at):contents.Substring(at,end-at);
                var entry=_app.Actors.Catalog.FirstOrDefault(e=>!e.IsRadiant&&!e.IsPrototype&&e.Reference==reference);
                var sprite=entry?.Icon??entry?.Type?.Icon??entry?.Type?.icon;
                if(Live(sprite)){point.SourceIcon=sprite;return sprite;}
            }
            return NamedFirst(MapRareIcons.Names(point.FilterKind));
        }
        if(point.Category=="Гордо")
        {
            string kind=point.Name.Split("-гордо",StringSplitOptions.None)[0];
            var entry=_app.Actors.Catalog.FirstOrDefault(e=>e.Category=="Слаймы"&&!e.IsPrototype&&
                (e.Reference?.EndsWith("."+kind,StringComparison.OrdinalIgnoreCase)==true||
                 string.Equals(e.Type?.name,kind+"Slime",StringComparison.OrdinalIgnoreCase)||
                 string.Equals(e.Type?.name,kind,StringComparison.OrdinalIgnoreCase)));
            var sprite=entry?.Type?.Icon??entry?.Type?.icon;
            if(Live(sprite))return sprite;
        }
        if(point.Category=="Дроны")
        {
            // Phantom route drones keep the native map's explorer-drone artwork.
            var ghost=NativeIconPolicy.GhostDrone(point.Id,point.Name)?Role("GhostDrone"):null;
            if(ghost!=null)return ghost;
        }
        if(point.Category==FandomMapMarkers.RareCategory)
        {
            // Statues, gates and radiant slimes: real game art per find kind,
            // never the baked atlas cell that looked like a bush.
            var art=NamedFirst(MapRareIcons.Names(point.FilterKind));
            if(art!=null)return art;
        }
        return Category(point.Category);
    }
    internal void Reset()
    {
        _categories.Clear();_rewards.Clear();_roles.Clear();_named.Clear();Array.Clear(_tabs);
        _roleInfo.Clear();_roleDiagnostics.Clear();_weatherRoles.Clear();_sharedWeatherRoles.Clear();_weatherLookups.Clear();_nextWeatherRetry=0;_ambiguousWeatherPresets.Clear();_session=_mapUi=IntPtr.Zero;DiscoveryScans=0;
        _categoryInfo.Clear();_rewardInfo.Clear();Array.Clear(_tabInfo);_census=default;
        _focusEpoch=_catalogGeneration=-1;_nextCensus=0;_rebindRequested=_sceneRebindRequested=false;
        Status="Оригинальные игровые иконки ещё не прочитаны";
    }
}