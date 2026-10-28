using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using UnityEngine;
using P = CreatorToolkit.WorldSourceObservationPolicy;

namespace CreatorToolkit;

// Passive loaded-world observations only. Never creates actors, evaluates queries,
// runs spawners, loads scenes, changes native fields, or reads save files.
internal sealed class WorldSourceObservations
{
    internal sealed class Observation
    {
        internal string Key {get;init;} = "";
        internal string PhysicalKey {get;init;} = "";
        internal P.Kind Kind {get;init;}
        internal P.PositionRole PositionRole {get;init;}
        internal string TypeReference {get;init;} = "";
        internal string NativeName {get;init;} = "";
        internal string Scene {get;init;} = "";
        internal string SceneGroup {get;init;} = ""; // Empty for potential sources: owner is NOT guessed.
        internal string ZoneReference {get;init;} = "";
        internal string SourceId {get;init;} = "";
        internal long ActorId {get;init;}
        internal Vector3 Position {get;init;}
        internal bool SourceActive {get;init;}
        internal bool SourceEnabled {get;init;}
        internal bool QueryPresent {get;init;}
        internal bool EligibilityUnknown {get;init;} = true;
        internal bool Feral {get;init;}
        internal float Weight {get;init;}
        internal float ConstraintWeight {get;init;}
        internal bool HasTimeWindow {get;init;}
        internal string TimeMode {get;init;} = "";
        internal float StartHour {get;init;}
        internal float EndHour {get;init;}
        internal bool HasRadius {get;init;}
        internal float Radius {get;init;}
        internal bool AllowDirectedSpawns {get;init;}
        internal float ObservedAt {get;init;}
    }
    internal sealed class Snapshot
    {
        internal IReadOnlyList<Observation> Actual {get;init;} = Array.Empty<Observation>();
        internal IReadOnlyList<Observation> Potential {get;init;} = Array.Empty<Observation>();
        internal IReadOnlyDictionary<string,string> GoldCoverage {get;init;} = new ReadOnlyDictionary<string,string>(new Dictionary<string,string>());
        internal IReadOnlyList<string> Issues {get;init;} = Array.Empty<string>();
        internal bool CycleComplete {get;init;}
        internal bool IdentityCensusComplete {get;init;}
        internal bool ActualCensusComplete {get;init;}
        internal bool PotentialCensusComplete {get;init;}
        internal bool Incomplete {get;init;} = true;
        internal bool UnloadedScenesUnknown => true;
        internal int ModelOnlyOrInactive {get;init;}
        internal int Generation {get;init;}
        internal float StartedAt {get;init;}
        internal float PublishedAt {get;init;}
        internal string Status {get;init;} = "Наблюдения отсутствуют; отсутствие объектов не установлено.";
    }
    private readonly struct Location
    {
        internal readonly Vector3 Position;internal readonly string Scene;internal readonly bool Active;
        internal Location(Vector3 position,string scene,bool active){Position=position;Scene=scene;Active=active;}
    }
    private const int MaxRegistry=4096, MaxModels=1024, MaxActual=256, MaxPotential=4096;
    private const int MaxDirectors=256, MaxSources=512, MaxNested=32;
    private const float RefreshSeconds=3f, LifetimeSeconds=20f, PublishSeconds=.25f;
    private readonly Dictionary<string,Observation> _actual=new(StringComparer.Ordinal),_potential=new(StringComparer.Ordinal);
    private readonly Dictionary<string,IntPtr> _actorPointers=new(StringComparer.Ordinal);
    private readonly HashSet<string> _actorConflicts=new(StringComparer.Ordinal);
    private readonly Dictionary<string,string> _coverage=new(StringComparer.Ordinal);
    private readonly List<string> _issues=new();
    private IEnumerator<bool> _work;
    private IntPtr _session,_context,_model,_lookup;
    private int _generation,_modelOnly,_issueSerial;
    private bool _identityDone,_actualDone,_potentialDone,_complete,_incomplete;
    private float _started,_now,_nextCycle,_nextPublish,_lastTime;
    private Snapshot _snapshot=new();
    private static readonly Snapshot Unavailable=new();

    // Only this method reads native objects. Parent calls from Update, never OnGUI.
    internal void Tick(SceneContext context,IntPtr session,float now,bool requested,int budget=32)
    {
        if(!requested||context is null||session==IntPtr.Zero||!float.IsFinite(now))
        { if(_session!=IntPtr.Zero||_work!=null)InvalidateSession();return; }
        _now=now;
        try
        {
            if(context.WasCollected||context==null) { InvalidateSession();return; }
            var model=context.GameModel;var lookup=LookupDirector.GetIfReady();
            if(model is null||model.WasCollected||lookup is null||lookup.WasCollected||lookup==null||!lookup._initialized)
            { InvalidateSession();return; }
            if(_session!=session||_context!=context.Pointer||_model!=model.Pointer||_lookup!=lookup.Pointer||now<_lastTime)
            {
                InvalidateSession();_session=session;_context=context.Pointer;_model=model.Pointer;_lookup=lookup.Pointer;
            }
            _lastTime=_now=now;
            if(_work!=null&&!P.Fresh(_started,now,LifetimeSeconds))
            { FailCycle("Превышен срок census; старые actual/potential observations сняты.");return; }
            if(_work==null&&now>=_nextCycle)
            {
                ClearCycle();_started=now;_nextPublish=now;
                _work=Census(context,lookup).GetEnumerator();
            }
            int units=Math.Clamp(budget,1,64);bool finished=false;
            for(int i=0;i<units&&_work!=null;i++)
                if(!_work.MoveNext()) { _work=null;_complete=true;finished=true;_nextCycle=now+RefreshSeconds; }
            if(finished)
            { Publish();_nextPublish=now+PublishSeconds; }
        }
        catch(Exception e) { FailCycle("Native census прерван: "+P.Text(e.Message)); }
    }
    internal Snapshot GetSnapshot(float now) => P.Fresh(_snapshot.StartedAt,now,LifetimeSeconds)?_snapshot:Unavailable;
    internal void InvalidateSession()
    { DropWork();_session=_context=_model=_lookup=IntPtr.Zero;_nextCycle=0;_lastTime=0; }
    internal void InvalidateScene() { DropWork();_nextCycle=0; }
    private void DropWork()
    {
        // Do NOT resume/dispose an old native enumerator across a lifecycle boundary.
        // Dropping the sole managed iterator root performs no cross-session native calls.
        _work=null;_generation++;ClearCycle();_snapshot=new Snapshot {Generation=_generation};
    }
    private void ClearCycle()
    {
        _actual.Clear();_potential.Clear();_actorPointers.Clear();_actorConflicts.Clear();_coverage.Clear();_issues.Clear();
        foreach(string name in P.GoldNames)_coverage[name]="Не проверено";
        _modelOnly=_issueSerial=0;_identityDone=_actualDone=_potentialDone=_complete=_incomplete=false;
    }
    private void Issue(string message)
    { _incomplete=true;_issueSerial++;if(_issues.Count<16&&!_issues.Contains(message))_issues.Add(P.Text(message)); }
    private void FailCycle(string reason)
    { _work=null;ClearCycle();Issue(reason);_started=_now;_nextCycle=_now+RefreshSeconds;Publish(); }
    private void Publish()
    {
        _snapshot=new Snapshot {Actual=Array.AsReadOnly(_actual.Values.ToArray()),Potential=Array.AsReadOnly(_potential.Values.ToArray()),
            GoldCoverage=new ReadOnlyDictionary<string,string>(new Dictionary<string,string>(_coverage,StringComparer.Ordinal)),
            Issues=Array.AsReadOnly(_issues.ToArray()),Generation=_generation,StartedAt=_started,PublishedAt=_now,
            CycleComplete=_complete,IdentityCensusComplete=_identityDone,ActualCensusComplete=_actualDone,PotentialCensusComplete=_potentialDone,
            Incomplete=_incomplete||!_complete||!_identityDone||!_actualDone||!_potentialDone,ModelOnlyOrInactive=_modelOnly,
            Status="Загруженные сцены · реальные горшочки отдельно от мест появления"};
    }
    private static bool Registered(LookupDirector lookup,IdentifiableType type)
    {
        if(type is null||type.WasCollected||type==null)return false;
        string reference=type.ReferenceId;
        return P.ValidKey(reference)&&lookup._identifiableTypeByRefId!=null&&
            lookup._identifiableTypeByRefId.TryGetValue(reference,out var registered)&&registered!=null&&registered.Pointer==type.Pointer;
    }
    private static bool TryLocation(GameObject go,out Location location)
    {
        location=default;
        if(go is null||go.WasCollected||go==null||!go.scene.IsValid()||!go.scene.isLoaded)return false;
        var position=go.transform.position;
        if(!P.FinitePosition(position.x,position.y,position.z))return false;
        location=new Location(position,P.Text(go.scene.name),go.activeInHierarchy);return true;
    }
    private string SourceKey(UnityEngine.Component source) => _generation.ToString(CultureInfo.InvariantCulture)+"|"+source.Pointer.ToInt64().ToString("x",CultureInfo.InvariantCulture);
    private IEnumerable<bool> Census(SceneContext context,LookupDirector lookup)
    {
        var registry=lookup._identifiableTypeByRefId;
        if(registry==null||registry.Count==0||registry.Count>MaxRegistry) { Issue("Registered-type census недоступен или превышает лимит.");yield break; }
        int registryCount=registry.Count;
        var gold=new Dictionary<string,IdentifiableType>(StringComparer.Ordinal);
        var conflicts=new HashSet<string>(StringComparer.Ordinal);
        foreach(var pair in registry)
        {
            var type=pair.Value;
            if(type is not null&&!type.WasCollected&&type!=null&&P.IsGoldName(type.name))
            {
                string name=type.name;
                if(!string.Equals(pair.Key,type.ReferenceId,StringComparison.Ordinal)||!Registered(lookup,type))Issue("Неканоничная registered gold identity: "+name);
                else if(gold.TryGetValue(name,out var previous)&&previous.Pointer!=type.Pointer)conflicts.Add(name);
                else gold[name]=type;
            }
            yield return true;
        }
        if(registry.Count!=registryCount)throw new InvalidOperationException("Registered-type registry изменился во время census.");
        _identityDone=true;
        foreach(string name in P.GoldNames)
        {
            if(conflicts.Contains(name)) { gold.Remove(name);_coverage[name]="Конфликт native identities";Issue("Неоднозначный gold type: "+name); }
            else if(!gold.TryGetValue(name,out var type)) { _coverage[name]="Отсутствует registered type";Issue("Gold type не зарегистрирован: "+name); }
            else if(type.prefab==null||!IdentifiableType.TryGetId(type.prefab,out var prefabType)||prefabType==null||prefabType.Pointer!=type.Pointer)
            { gold.Remove(name);_coverage[name]="Prefab identity не подтверждена";Issue("Gold prefab identity не подтверждена: "+name); }
            else _coverage[name]=P.Text(type.ReferenceId);
            yield return true;
        }
        // Actual containers are admitted by exact loaded ZoneDefinition crate tables,
        // not substring matches or a guess that every breakable object is a pot.
        var containerTypes=new Dictionary<string,IdentifiableType>(StringComparer.Ordinal);
        foreach(var entry in gold.Values)containerTypes[entry.ReferenceId]=entry;
        var loadedZones=ZoneDirector._zones;
        if(loadedZones!=null)foreach(var zonePair in loadedZones)
        {
            var crates=zonePair.Key?._crates;
            for(int i=0;crates!=null&&i<Math.Min(crates.Length,MaxNested);i++)
            {
                var type=crates[i]?.Type;
                if(Registered(lookup,type)&&type.prefab!=null&&type.prefab.GetComponent<BreakOnImpactBase>()!=null)
                    containerTypes[type.ReferenceId]=type;
                yield return true;
            }
        }
        int modelsRead=0;bool modelsComplete=true;
        var models=context.GameModel.identifiablesByIdent;
        if(models==null) { Issue("Native model index недоступен.");modelsComplete=false; }
        else foreach(var pair in containerTypes)
        {
            if(!Registered(lookup,pair.Value))throw new InvalidOperationException("Gold registration изменилась.");
            if(models.TryGetValue(pair.Value,out var list)&&list!=null)
            {
                int count=list.Count;
                for(int i=0;i<count;i++)
                {
                    if(modelsRead++>=MaxModels||_actual.Count>=MaxActual) { Issue("Actual model/row budget исчерпан.");modelsComplete=false;break; }
                    try { ReadActual(lookup,pair.Value,list[i]); }catch(Exception e) { Issue("Actual read: "+P.Text(e.Message));modelsComplete=false; }
                    yield return true;
                }
                if(list.Count!=count)throw new InvalidOperationException("Gold model list изменился во время census.");
            }
            yield return true;
            if(!modelsComplete&&modelsRead>=MaxModels)break;
        }
        // A live component census supplements the model index, never guesses an
        // absent model to be present. Reuse exact actor identity for deduplication.
        int liveContainers=0;
        foreach(var container in UnityEngine.Object.FindObjectsOfType<BreakOnImpactBase>())
        {
            if(++liveContainers>MaxActual){Issue("Live container census budget исчерпан.");modelsComplete=false;break;}
            if(container!=null&&!container.WasCollected&&TryLocation(container.gameObject,out var location)&&location.Active&&
                IdentifiableType.TryGetId(container.gameObject,out var type)&&Registered(lookup,type)&&
                containerTypes.ContainsKey(type.ReferenceId))
            {
                var actor=container.GetComponent<IdentifiableActor>();var actorModel=actor?._model;
                if(actorModel!=null&&!actorModel.WasCollected)ReadActual(lookup,type,actorModel);
            }
            yield return true;
        }
        _actualDone=modelsComplete&&gold.Count==P.GoldNames.Count;
        int potentialIssues=_issueSerial;
        foreach(bool step in GoldSources(lookup,gold))yield return step;
        foreach(bool step in SlimeSources(lookup))yield return step;
        _potentialDone=_issueSerial==potentialIssues&&gold.Count==P.GoldNames.Count;
    }
    private void ReadActual(LookupDirector lookup,IdentifiableType type,IdentifiableModel model)
    {
        if(model is null||model.WasCollected||!Registered(lookup,type)||model.ident==null||model.ident.Pointer!=type.Pointer)
        { Issue("Model/type identity не подтверждена.");return; }
        var go=model.GetGameObject();
        if(!TryLocation(go,out var location)||!location.Active) { _modelOnly++;return; }
        bool exact=IdentifiableType.TryGetId(go,out var current)&&current!=null&&current.Pointer==type.Pointer;
        bool isGold=P.IsGoldName(type.name);
        if(!(isGold?P.ActualAllowed(P.Kind.ActualGold,exact,true,true,true,location.Active,true):P.ActualContainerAllowed(exact,true,true,true,location.Active,true))) { Issue("Live GO type identity не подтверждена.");return; }
        long actor=model.actorId.Value;
        string key=_generation.ToString(CultureInfo.InvariantCulture)+"|"+type.ReferenceId+"|"+actor.ToString(CultureInfo.InvariantCulture);
        if(_actorConflicts.Contains(key))return;
        if(_actorPointers.TryGetValue(key,out var previous)&&previous!=model.Pointer)
        { _actual.Remove(key);_actorConflicts.Add(key);Issue("ActorId collision: actual observation снято.");return; }
        _actorPointers[key]=model.Pointer;
        _actual[key]=new Observation {Key=key,PhysicalKey=P.PhysicalPointKey(P.Kind.ActualGold,actor.ToString(CultureInfo.InvariantCulture),""),Kind=isGold?P.Kind.ActualGold:P.Kind.ActualContainer,PositionRole=P.PositionRole.ObservedActor,
            TypeReference=type.ReferenceId,NativeName=P.Text(type.name),ActorId=actor,Position=location.Position,Scene=location.Scene,
            SceneGroup=P.Text(model.SceneGroup?.name),SourceActive=true,SourceEnabled=true,EligibilityUnknown=false,ObservedAt=_now};
    }
    private IEnumerable<bool> GoldSources(LookupDirector lookup,Dictionary<string,IdentifiableType> gold)
    {
        var zones=ZoneDirector._zones;
        if(zones==null) { Issue("Loaded ZoneDirector registry недоступен.");yield break; }
        int directors=0,sources=0,zoneCount=zones.Count;
        foreach(var pair in zones)
        {
            var list=pair.Value;if(list==null) { Issue("ZoneDirector list отсутствует.");yield return true;continue; }
            int count=list.Count;
            for(int d=0;d<count;d++)
            {
                if(++directors>MaxDirectors) { Issue("ZoneDirector budget исчерпан.");yield break; }
                var director=list[d];
                if(director is null||director.WasCollected||director==null||!TryLocation(director.gameObject,out _)) { yield return true;continue; }
                var zone=director.Zone;
                if(zone==null||pair.Key==null||zone.Pointer!=pair.Key.Pointer||!P.ValidKey(zone.ReferenceId)) { Issue("Zone identity не подтверждена.");yield return true;continue; }
                var crates=zone._crates;var spawners=director._crateSpawners;
                if(crates==null||spawners==null) { Issue("Crate source tables недоступны.");yield return true;continue; }
                int spawnerCount=spawners.Count;
                for(int s=0;s<spawnerCount;s++)
                {
                    if(++sources>MaxSources||_potential.Count>=MaxPotential) { Issue("Gold source budget исчерпан.");yield break; }
                    var source=spawners[s];
                    if(source is null||source.WasCollected||source==null||!TryLocation(source.gameObject,out var location)) { yield return true;continue; }
                    if(source._zoneDirector==null||source._zoneDirector.Pointer!=director.Pointer) { Issue("Crate source owner не подтверждён.");yield return true;continue; }
                    if(crates.Length>MaxNested)Issue("Crate table обрезана budget.");
                    for(int c=0;c<Math.Min(crates.Length,MaxNested);c++)
                    {
                        var crate=crates[c];var type=crate?.Type;
                        if(Registered(lookup,type)&&type.prefab!=null&&type.prefab.GetComponent<BreakOnImpactBase>()!=null)
                        {
                            float weight=crate.Weight;
                            if(!P.Weight(weight))Issue("Некорректный crate weight.");
                            else
                            {
                                string key=SourceKey(source)+"|gold|"+c.ToString(CultureInfo.InvariantCulture);
                                _potential[key]=new Observation {Key=key,PhysicalKey=P.PhysicalPointKey(P.Kind.PotentialContainer,SourceKey(source),"origin"),Kind=P.IsGoldName(type.name)?P.Kind.PotentialGold:P.Kind.PotentialContainer,PositionRole=P.PositionRole.SourceOrigin,
                                    TypeReference=type.ReferenceId,NativeName=P.Text(type.name),Scene=location.Scene,Position=location.Position,
                                    ZoneReference=zone.ReferenceId,Weight=weight,QueryPresent=crate.Condition!=null,
                                    SourceActive=location.Active,SourceEnabled=source.enabled,ObservedAt=_now};
                            }
                        }
                        yield return true;
                        if(_potential.Count>=MaxPotential) { Issue("Potential row budget исчерпан.");yield break; }
                    }
                }
                if(spawners.Count!=spawnerCount)throw new InvalidOperationException("Crate registry изменился.");
                yield return true;
            }
            if(list.Count!=count)throw new InvalidOperationException("ZoneDirector list изменился.");
            yield return true;
        }
        if(zones.Count!=zoneCount)throw new InvalidOperationException("Zone registry изменился.");
    }
    private IEnumerable<bool> SlimeSources(LookupDirector lookup)
    {
        var cells=CellDirector._allCellDirectors;
        if(cells==null) { Issue("Loaded CellDirector registry недоступен.");yield break; }
        int cellCount=cells.Count,sources=0;
        if(cellCount>MaxDirectors)Issue("CellDirector budget исчерпан.");
        for(int d=0;d<Math.Min(cellCount,MaxDirectors);d++)
        {
            var cell=cells[d];
            if(cell is null||cell.WasCollected||cell==null||!TryLocation(cell.gameObject,out _)) { yield return true;continue; }
            var spawners=cell._spawners;
            if(spawners==null) { Issue("Slime spawner list отсутствует.");yield return true;continue; }
            int count=spawners.Count;
            for(int s=0;s<count;s++)
            {
                if(++sources>MaxSources||_potential.Count>=MaxPotential) { Issue("Slime source budget исчерпан.");yield break; }
                var source=spawners[s];
                if(source is null||source.WasCollected||source==null||!TryLocation(source.gameObject,out var origin)) { yield return true;continue; }
                var constraints=source.Constraints;var locations=source.SpawnLocs;
                if(constraints==null) { Issue("Slime constraints отсутствуют.");yield return true;continue; }
                string zone="";var zoneDirector=cell._zoneDirector;
                if(zoneDirector!=null&&zoneDirector.Zone!=null)zone=P.Text(zoneDirector.Zone.ReferenceId);
                if(constraints.Length>MaxNested)Issue("Slime constraints обрезаны budget.");
                for(int c=0;c<Math.Min(constraints.Length,MaxNested);c++)
                {
                    var constraint=constraints[c];var members=constraint?.Slimeset?.Members;
                    if(members==null) { Issue("SlimeSet не готов; prefab conversion не вызывается.");yield return true;continue; }
                    if(members.Length>MaxNested)Issue("SlimeSet members обрезаны budget.");
                    for(int m=0;m<Math.Min(members.Length,MaxNested);m++)
                    {
                        var member=members[m];var type=member?.IdentType;
                        if(type==null||type.TryCast<SlimeDefinition>()==null||!Registered(lookup,type)) { Issue("Exact registered slime member не подтверждён.");yield return true;continue; }
                        int length=locations==null?0:locations.Length;
                        if(length>MaxNested)Issue("SpawnLocs обрезаны budget.");
                        for(int l=0;l<Math.Max(1,Math.Min(length,MaxNested));l++)
                        {
                            Location location=origin;
                            if(length>0&&!TryLocation(locations[l],out location)) { Issue("Configured SpawnLoc не загружен.");yield return true;continue; }
                            float weight=member.Weight,cw=constraint.Weight,radius=source.Radius;
                            var window=constraint.Window;
                            if(!P.Weight(weight)||!P.Weight(cw)||!float.IsFinite(radius)||radius<0||
                                window!=null&&(!float.IsFinite(window.StartHour)||!float.IsFinite(window.EndHour)))Issue("Slime source numeric metadata некорректны.");
                            else
                            {
                                string key=SourceKey(source)+"|slime|"+c+"|"+m+"|"+l;
                                _potential[key]=new Observation {Key=key,PhysicalKey=P.PhysicalPointKey(P.Kind.PotentialSlime,SourceKey(source),length>0?locations[l].Pointer.ToInt64().ToString("x",CultureInfo.InvariantCulture):"origin"),Kind=P.Kind.PotentialSlime,
                                    PositionRole=length>0?P.PositionRole.ConfiguredSpawnLocation:P.PositionRole.SourceOrigin,
                                    TypeReference=type.ReferenceId,NativeName=P.Text(type.name),Scene=location.Scene,ZoneReference=zone,
                                    SourceId=P.ValidKey(source._id)?source._id:"",Position=location.Position,Weight=weight,ConstraintWeight=cw,
                                    HasTimeWindow=window!=null,TimeMode=window==null?"":P.Text(window.TimeMode.ToString()),
                                    StartHour=window?.StartHour??0,EndHour=window?.EndHour??0,Feral=constraint.Feral,
                                    QueryPresent=source._allowSpawnQuery!=null,HasRadius=true,Radius=radius,AllowDirectedSpawns=source.AllowDirectedSpawns,
                                    SourceActive=origin.Active,SourceEnabled=source.enabled,ObservedAt=_now};
                            }
                            yield return true;
                            if(_potential.Count>=MaxPotential) { Issue("Potential row budget исчерпан.");yield break; }
                        }
                    }
                }
                yield return true;
            }
            if(spawners.Count!=count)throw new InvalidOperationException("Slime spawner list изменился.");
            yield return true;
        }
        if(cells.Count!=cellCount)throw new InvalidOperationException("CellDirector registry изменился.");
    }
}
