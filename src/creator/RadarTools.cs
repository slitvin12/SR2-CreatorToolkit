using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using UnityEngine;

namespace CreatorToolkit;

// 0.8.1 radar (approved mockup 03): the player picks what to track - slimes, largos, gordos,
// food, plorts, resources. Every target has its own colour, the nearest distance and an
// «N рядом» count inside the chosen radius of the current native zone. Hits feed the HUD row,
// world rings, the compass and the minimap. Targets, radius and switches are kept per save in
// radar-<save>.cfg (atomic write). The legacy fixtures (SetSlime/SetModes: a slime's favourite
// diet and rare materials) stay memory-only.
internal sealed class RadarTools
{
    internal sealed class Hit {internal string Name,Reference;internal Vector3 Position;internal bool Food;internal int Color;internal Sprite Icon;}
    internal sealed class Target
    {
        internal string Reference="",Name="",Category="",Owner="",OwnerName="";
        internal int Color;internal bool Auto,Food;
        internal int Near;internal float Nearest=-1;
        internal IdentifiableType Type;internal ActorTools.Entry Entry;
        internal bool Gordo=>Category=="Гордо";
    }
    // Only catalog-owned definition assets are cached, never actor models,
    // game objects, regions or the native registry itself.
    private sealed class Query {internal IdentifiableType Type;internal Target Target;}
    private readonly ToolkitMod _app;
    private readonly HashSet<string> _favorites=new(StringComparer.Ordinal);
    private readonly List<Target> _targets=new();
    private readonly List<Hit> _hits=new();
    private readonly List<Query> _queries=new();
    private readonly Dictionary<string,ActorTools.Entry> _entries=new(StringComparer.Ordinal);
    private MapExplorerPolicy.RadarConfig _pending;
    private int _entriesGeneration=-1,_gordoTargets;
    private IntPtr _zone,_session;
    private string _file="";
    private bool _writable,_legacy;
    private float _nextScan,_nextHud;
    private Camera _camera;
    internal bool On {get;private set;}=true;
    internal int Radius {get;private set;}=MapExplorerPolicy.DefaultRadius;
    internal bool Highlight {get;private set;}=true;
    internal bool ShowOnHud {get;private set;}=true;
    internal IReadOnlyList<Target> Targets=>_targets;
    internal bool FavoriteFood {get;private set;} internal bool RareResources {get;private set;}
    internal bool Enabled=>On&&_targets.Count>0;
    internal int TargetCount=>_hits.Count;
    internal int QueryDefinitionCount=>_queries.Count;
    internal int ExaminedModelCount {get;private set;}
    internal int Scans {get;private set;}
    internal string SlimeReference {get;private set;}="";
    internal string SlimeName {get;private set;}="Выберите слайма";
    internal string FavoriteNames {get;private set;}="Любимая еда ещё не выбрана";
    internal string Status {get;private set;}="Радар выключен";
    internal string Heading {get;private set;}="";
    internal string Direction {get;private set;}="↑";
    internal IReadOnlyList<Hit> Hits=>_hits;
    internal RadarTools(ToolkitMod app)=>_app=app;
    private static readonly string[] Arrows={"↑","↗","→","↘","↓","↙","←","↖"};
    internal static string Arrow(float angle)=>Arrows[ExplorerPolicy.DirectionIndex(angle)];

    // ---- per-save file ----
    internal void EnsureLoaded()
    {
        if(_session!=IntPtr.Zero&&_session==_app.Session)return;
        if(!_app.Ready)return;
        var metadata=NativeSaveMetadata.Read(GameContext.Instance.AutoSaveDirector);
        if(metadata==null||!metadata.HasValue||!WaypointStore.SaveKey(metadata.Value.GameName))return;
        // A legacy fixture set up before the first tick of this session keeps its memory-only targets.
        if(_legacy){_session=_app.Session;return;}
        _session=_app.Session;_pending=null;ClearState();
        _file=Path.Combine(_app.DataDirectory,MapExplorerPolicy.RadarFilePrefix+metadata.Value.GameName+MapExplorerPolicy.RadarFileSuffix);
        _writable=true;
        try
        {
            if(File.Exists(_file))
            {
                if(new FileInfo(_file).Length>64*1024)throw new InvalidDataException("Файл радара слишком велик.");
                var config=MapExplorerPolicy.ParseRadar(File.ReadAllText(_file),out _);
                On=config.On;Radius=config.Radius;Highlight=config.Highlight;ShowOnHud=config.Hud;
                if(config.Targets.Count>0)_pending=config;
            }
        }
        catch(Exception e){_writable=false;Status="Файл радара не принят, исходный файл не перезаписан: "+e.Message;}
        ResolvePending();
    }
    // Saved targets wait for the creator catalog of the loaded world.
    private void ResolvePending()
    {
        if(_pending==null||_app.Actors.Catalog.Count==0)return;
        var config=_pending;_pending=null;
        foreach(var saved in config.Targets)
        {
            var entry=FindEntry(saved.Reference);
            if(entry==null||Tracked(saved.Reference)!=null||_targets.Count>=MapExplorerPolicy.MaxTargets)continue;
            var target=NewTarget(entry,saved.Color,saved.Auto,false);
            target.Owner=saved.Owner??"";target.OwnerName=FindEntry(target.Owner)?.Name??"";
            _targets.Add(target);
        }
        RebuildQueries();_nextScan=0;Describe();
    }
    private void Persist()
    {
        if(_legacy||!_writable||string.IsNullOrEmpty(_file))return;
        var config=new MapExplorerPolicy.RadarConfig{On=On,Radius=Radius,Highlight=Highlight,Hud=ShowOnHud};
        foreach(var t in _targets)config.Targets.Add(new MapExplorerPolicy.RadarTarget{Reference=t.Reference,Color=t.Color,Auto=t.Auto,Owner=t.Owner});
        string temporary=_file+".tmp."+Guid.NewGuid().ToString("N");
        try{File.WriteAllText(temporary,MapExplorerPolicy.SerializeRadar(config));File.Move(temporary,_file,true);}
        catch(Exception e){Status="Радар: настройки не сохранены ("+e.Message+")";}
        finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch{}}
    }
    private void Ready()
    {
        EnsureLoaded();ResolvePending();
        if(_session==IntPtr.Zero||_session!=_app.Session)throw new InvalidOperationException("Радар станет доступен после загрузки сейва.");
    }

    // ---- catalog ----
    internal ActorTools.Entry FindEntry(string reference)
    {
        if(string.IsNullOrEmpty(reference))return null;
        if(_entriesGeneration!=_app.Actors.Generation||_entries.Count==0)
        {
            _entries.Clear();_entriesGeneration=_app.Actors.Generation;
            foreach(var entry in _app.Actors.Catalog)
                if(Trackable(entry)&&!_entries.ContainsKey(entry.Type.ReferenceId))_entries[entry.Type.ReferenceId]=entry;
        }
        return _entries.TryGetValue(reference,out var found)?found:null;
    }
    internal static bool Trackable(ActorTools.Entry entry)=>entry?.Type!=null&&!entry.IsPrototype&&!entry.IsRadiant&&
        MapExplorerPolicy.RadarCategory(entry.Category);
    internal Target Tracked(string reference)
    {
        foreach(var t in _targets)if(t.Reference==reference)return t;
        return null;
    }
    private Target NewTarget(ActorTools.Entry entry,int color,bool auto,bool food)=>new()
    {
        Reference=entry.Type.ReferenceId,Name=entry.Name,Category=entry.Category,Color=color,Auto=auto,Food=food,
        Type=entry.Type,Entry=entry
    };
    private int FreeColor()
    {
        var used=new List<int>(_targets.Count);
        foreach(var t in _targets)used.Add(t.Color);
        return MapExplorerPolicy.NextColor(used);
    }
    private void RebuildQueries()
    {
        _queries.Clear();_gordoTargets=0;
        if(!Enabled)return;
        var seen=new HashSet<IntPtr>();
        foreach(var target in _targets)
        {
            if(target.Type==null)continue;
            if(target.Gordo){_gordoTargets++;continue;}
            if(seen.Add(target.Type.Pointer))_queries.Add(new Query{Type=target.Type,Target=target});
        }
    }
    private void Changed(bool persist=true)
    {
        RebuildQueries();_nextScan=0;_nextHud=0;
        if(!Enabled){_hits.Clear();Heading="";_camera=null;_zone=IntPtr.Zero;foreach(var t in _targets){t.Near=0;t.Nearest=-1;}}
        Describe();
        if(persist)Persist();
    }
    private void Describe()
    {
        Status=!On?"Радар выключен":_targets.Count==0?"Радар · выберите, что отметить":
            "На радаре: "+_targets.Count+" · текущая зона, до "+Radius+" м";
    }

    // ---- page actions (persisted) ----
    internal void SetOn(bool on){Ready();_legacy=false;On=on;Changed();}
    internal void SetRadius(int metres)
    {
        int radius=MapExplorerPolicy.ClampRadius(metres);
        if(radius==Radius)return;
        Ready();Radius=radius;Changed();
    }
    internal void SetHighlight(bool on){Ready();Highlight=on;Changed();}
    internal void SetShowOnHud(bool on){Ready();ShowOnHud=on;Changed();}
    // Adds the entry with a free colour, or removes it when it is already tracked.
    internal void Toggle(ActorTools.Entry entry)
    {
        Ready();
        if(!Trackable(entry))throw new InvalidOperationException("Этот предмет нельзя отметить на радаре.");
        var existing=Tracked(entry.Type.ReferenceId);
        if(existing!=null)_targets.Remove(existing);
        else
        {
            if(_targets.Count>=MapExplorerPolicy.MaxTargets)throw new InvalidOperationException("На радаре не больше "+MapExplorerPolicy.MaxTargets+" целей.");
            _targets.Add(NewTarget(entry,FreeColor(),false,false));
            On=true;
        }
        _legacy=false;Changed();
    }
    internal void CycleColor(string reference)
    {
        Ready();var target=Tracked(reference);if(target==null)return;
        target.Color=MapExplorerPolicy.CycleColor(target.Color);
        foreach(var hit in _hits)if(hit.Reference==reference)hit.Color=target.Color;
        Changed();
    }
    internal void Remove(string reference)
    {
        Ready();var target=Tracked(reference);if(target==null)return;
        _targets.Remove(target);Changed();
    }
    internal void Clear(){Ready();_targets.Clear();FavoriteFood=RareResources=false;Changed();}
    // «+ Любимая еда слаймов из моих загонов»: the native diets of the slimes living in loaded corrals.
    internal int AddCorralFavorites()
    {
        Ready();
        var plots=_app.Context?.GameModel?.landPlots;
        if(plots==null)throw new InvalidOperationException("Загоны ещё не загружены.");
        var types=new Il2CppSystem.Collections.Generic.List<IdentifiableType>();
        var slimes=new List<SlimeDefinition>();var seen=new HashSet<IntPtr>();
        int corrals=0;
        foreach(var pair in plots)
        {
            var model=pair.Value;
            if(model==null||model.typeId!=LandPlot.Id.CORRAL)continue;
            var root=model.gameObj;
            if(root==null||root.WasCollected||!root.activeInHierarchy)continue;
            var plot=root.GetComponentInChildren<LandPlot>(true);
            var trackers=plot!=null&&!plot.WasCollected?plot._trackers:null;
            if(trackers==null)continue;
            corrals++;
            for(int t=0;t<trackers.Length;t++)
            {
                var tracker=trackers[t];
                if(tracker==null||tracker.WasCollected)continue;
                types.Clear();tracker.GetTrackedIdentifiableTypes(types);
                for(int k=0;k<types.Count;k++)
                {
                    var slime=types[k]?.TryCast<SlimeDefinition>();
                    if(slime!=null&&seen.Add(slime.Pointer))slimes.Add(slime);
                }
            }
        }
        if(corrals==0)throw new InvalidOperationException("Загоны не загружены: вернитесь на ранчо.");
        int added=0;
        foreach(var slime in slimes)
        {
            var native=slime.Diet?.FavoriteIdents;
            for(int i=0;native!=null&&i<native.Length;i++)
            {
                var type=native[i];var entry=type!=null?FindEntry(type.ReferenceId):null;
                if(entry==null||Tracked(entry.Type.ReferenceId)!=null||_targets.Count>=MapExplorerPolicy.MaxTargets)continue;
                var target=NewTarget(entry,FreeColor(),true,true);
                target.Owner=slime.ReferenceId;target.OwnerName=FindEntry(slime.ReferenceId)?.Name??"";
                _targets.Add(target);added++;
            }
        }
        if(added>0)On=true;
        _legacy=false;Changed();
        return added;
    }

    // ---- legacy fixtures (memory only) ----
    internal void SetSlime(ActorTools.Entry entry)
    {
        var slime=entry?.Type?.TryCast<SlimeDefinition>();
        if(slime==null||entry.IsPrototype)throw new InvalidOperationException("Выберите нативного слайма.");
        _favorites.Clear();var names=new List<string>();
        var native=slime.Diet?.FavoriteIdents;
        for(int i=0;native!=null&&i<native.Length;i++)
        {
            var type=native[i];if(type==null||!_favorites.Add(type.ReferenceId))continue;
            var known=_app.Actors.Catalog.FirstOrDefault(e=>e.Type==type);
            names.Add(known?.Name??"Название любимой еды недоступно");
        }
        SlimeReference=entry.Type.ReferenceId;SlimeName=entry.Name;
        FavoriteNames=names.Count==0?"У этого слайма нет нативной любимой еды":string.Join(" · ",names);
        if(FavoriteFood||RareResources)SetModes(FavoriteFood,RareResources);
    }
    internal void SetModes(bool food,bool resources)
    {
        _legacy=true;_pending=null;FavoriteFood=food;RareResources=resources;_targets.Clear();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var entry in _app.Actors.Catalog)
        {
            var type=entry.Type;if(type==null||entry.IsPrototype||entry.IsRadiant)continue;
            string reference=type.ReferenceId;
            bool favorite=food&&_favorites.Contains(reference);
            bool rare=resources&&entry.Category=="Ресурсы"&&ExplorerPolicy.RareMaterial(type.name);
            if((favorite||rare)&&seen.Add(reference))
            {
                var target=NewTarget(entry,favorite?0:2,favorite,favorite);
                if(favorite){target.Owner=SlimeReference;target.OwnerName=SlimeName;}
                _targets.Add(target);
            }
        }
        On=food||resources;
        _hits.Clear();ExaminedModelCount=0;Heading="";
        Changed(false);
        if(Enabled)Status="Ищем только в текущей нативной зоне · до "+Radius+" м";
    }

    // ---- scanning ----
    private void Offer(Hit hit,Vector3 origin)
    {
        if(_hits.Count<ExplorerPolicy.MaxRadarTargets){_hits.Add(hit);return;}
        int farthest=0;float farDistance=-1;
        for(int i=0;i<_hits.Count;i++){float d=(_hits[i].Position-origin).sqrMagnitude;if(d>farDistance){farDistance=d;farthest=i;}}
        if((hit.Position-origin).sqrMagnitude<farDistance)_hits[farthest]=hit;
    }
    private static Hit HitFor(Target target,Vector3 position)=>new()
    {
        Name=target.Name,Reference=target.Reference,Position=position,Food=target.Food||target.Auto,Color=target.Color,
        Icon=target.Entry?.Icon??target.Type?.Icon
    };
    private static void Count(Target target,float distance,bool near)
    {
        if(target.Nearest<0||distance<target.Nearest)target.Nearest=distance;
        if(near)target.Near++;
    }
    internal void ScanNow()
    {
        _hits.Clear();ExaminedModelCount=0;Scans++;
        foreach(var t in _targets){t.Near=0;t.Nearest=-1;}
        if(!Enabled||!_app.Ready||_app.Map.Busy)return;
        var zone=_app.Context.PlayerZoneTracker?.GetCurrentZone();
        if(zone==null){Status="Текущая зона ещё не определена";_zone=IntPtr.Zero;return;}
        _zone=zone.Pointer;var origin=_app.Player.Controller.Motor.TransientPosition;
        float radius=Radius;
        // Query the game's existing per-definition index, not a newly copied
        // actor dictionary or a Unity-wide object array. Loaded/current-zone
        // checks still run on every actual matching actor.
        var index=_app.Context.GameModel.identifiablesByIdent;
        if(index==null){Status="Нативный индекс предметов ещё не готов";return;}
        foreach(var query in _queries)
        {
            if(!index.TryGetValue(query.Type,out var models)||models==null)continue;
            var target=query.Target;
            int count=models.Count;
            for(int m=0;m<count;m++)
            {
                var model=models[m];ExaminedModelCount++;
                var go=model?.GetGameObject();
                if(go==null||go.WasCollected||!go.activeInHierarchy)continue;
                var actor=go.GetComponent<IdentifiableActor>();
                if(actor==null||actor.WasCollected||actor.identType!=query.Type||
                    actor.RegionMember==null||!actor.RegionMember.IsInZone(zone))continue;
                var position=actor.transform.position;
                float d2=(position-origin).sqrMagnitude;
                bool near=ExplorerPolicy.Within(d2,radius);
                Count(target,Mathf.Sqrt(d2),near);
                if(near)Offer(HitFor(target,position),origin);
            }
        }
        if(_gordoTargets>0)ScanGordos(origin,radius);
        _hits.Sort((a,b)=>(a.Position-origin).sqrMagnitude.CompareTo((b.Position-origin).sqrMagnitude));
        int total=0;foreach(var t in _targets)total+=t.Near;
        Status="Рядом: "+total+" · целей на радаре: "+_targets.Count+" · текущая зона, до "+Radius+" м";
    }
    // Gordos are scene objects: their persistent models and the atlas positions of this island.
    private void ScanGordos(Vector3 origin,float radius)
    {
        var models=_app.Context.GameModel.AllGordos();
        var group=SystemContext.Instance?.SceneLoader?.CurrentSceneGroup;
        if(models==null||group==null)return;
        var map=_app.Map;
        foreach(var pair in models)
        {
            var model=pair.Value;if(model==null||model.WasCollected)continue;
            var type=model.identifiableType;if(type==null)continue;
            Target target=null;
            foreach(var t in _targets)if(t.Gordo&&t.Type!=null&&t.Type.Pointer==type.Pointer){target=t;break;}
            if(target==null)continue;
            var point=map.GordoPoint(pair.Key);
            if(point?.Group==null||point.Group.name!=group.name||map.GordoState(point)==GordoMapPolicy.State.Burst)continue;
            float d2=(point.Position-origin).sqrMagnitude;
            bool near=ExplorerPolicy.Within(d2,radius);
            Count(target,Mathf.Sqrt(d2),near);
            if(near)Offer(HitFor(target,point.Position),origin);
        }
    }
    internal void Tick()
    {
        EnsureLoaded();ResolvePending();
        if(!Enabled)return;
        if(!_app.Ready||_app.Map.Busy){_hits.Clear();Heading="";return;}
        var zone=_app.Context.PlayerZoneTracker?.GetCurrentZone();
        if((zone?.Pointer??IntPtr.Zero)!=_zone){_hits.Clear();_nextScan=0;Heading="";}
        if(Time.unscaledTime>=_nextScan)
        {_nextScan=Time.unscaledTime+ExplorerPolicy.RadarScanSeconds;ScanNow();}
        if(Time.unscaledTime<_nextHud)return;
        _nextHud=Time.unscaledTime+.25f;
        if(_hits.Count==0){Heading="Радар · цели поблизости не найдены";Direction="◎";return;}
        var delta=_hits[0].Position-_app.Player.Controller.Motor.TransientPosition;
        if(_camera==null||_camera.WasCollected)_camera=Camera.main;
        var forward=_camera!=null?_camera.transform.forward:_app.Context.Player.transform.forward;
        Direction=Arrow(Vector3.SignedAngle(new Vector3(forward.x,0,forward.z),new Vector3(delta.x,0,delta.z),Vector3.up));
        Heading=_hits[0].Name+" · "+delta.magnitude.ToString("0")+" м";
    }
    private void ClearState()
    {
        _targets.Clear();_hits.Clear();_queries.Clear();_favorites.Clear();_camera=null;_zone=IntPtr.Zero;_gordoTargets=0;
        _nextScan=_nextHud=0;Heading="";Status="Радар выключен";SlimeReference="";SlimeName="Выберите слайма";
        FavoriteNames="Любимая еда ещё не выбрана";Scans=ExaminedModelCount=0;FavoriteFood=RareResources=false;
        On=true;Radius=MapExplorerPolicy.DefaultRadius;Highlight=ShowOnHud=true;
    }
    // Memory only: the per-save file is read again in the next session.
    internal void Reset()
    {
        ClearState();_session=IntPtr.Zero;_pending=null;_legacy=false;_writable=false;_file="";
    }
}
