using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using UnityEngine;

namespace CreatorToolkit;

internal sealed class RadarTools
{
    internal sealed class Hit {internal string Name,Reference;internal Vector3 Position;internal bool Food;}
    // Only catalog-owned definition assets are cached, never actor models,
    // game objects, regions or the native registry itself.
    private sealed class Query {internal IdentifiableType Type;internal string Name,Reference;internal bool Food;}
    private readonly ToolkitMod _app;
    private readonly HashSet<string> _favorites=new(StringComparer.Ordinal),_materials=new(StringComparer.Ordinal);
    private readonly List<Hit> _hits=new();
    private readonly List<Query> _queries=new();
    private IntPtr _zone;
    private float _nextScan,_nextHud;
    private Camera _camera;
    internal bool FavoriteFood {get;private set;} internal bool RareResources {get;private set;}
    internal bool Enabled=>FavoriteFood||RareResources;
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
        RebuildQueries();_hits.Clear();_nextScan=0;
    }
    private void RebuildQueries()
    {
        _queries.Clear();
        if(!Enabled)return;
        var seen=new HashSet<IntPtr>();
        foreach(var entry in _app.Actors.Catalog)
        {
            var type=entry.Type;if(type==null)continue;
            string reference=type.ReferenceId;
            bool food=FavoriteFood&&_favorites.Contains(reference);
            bool resource=RareResources&&_materials.Contains(reference);
            if((food||resource)&&seen.Add(type.Pointer))
                _queries.Add(new Query{Type=type,Name=entry.Name,Reference=reference,Food=food});
        }
    }
    internal void SetModes(bool food,bool resources)
    {
        FavoriteFood=food;RareResources=resources;_materials.Clear();
        if(resources)foreach(var entry in _app.Actors.Catalog)
            if(entry.Category=="Ресурсы"&&ExplorerPolicy.RareMaterial(entry.Type.name))_materials.Add(entry.Type.ReferenceId);
        RebuildQueries();_nextScan=0;_hits.Clear();ExaminedModelCount=0;Heading="";
        if(!Enabled){_camera=null;_zone=IntPtr.Zero;}
        Status=Enabled?"Ищем только в текущей нативной зоне · до 100 м":"Радар выключен";
    }
    internal void ScanNow()
    {
        _hits.Clear();ExaminedModelCount=0;Scans++;
        if(!Enabled||!_app.Ready||_app.Map.Busy)return;
        var zone=_app.Context.PlayerZoneTracker?.GetCurrentZone();
        if(zone==null){Status="Текущая зона ещё не определена";_zone=IntPtr.Zero;return;}
        _zone=zone.Pointer;var origin=_app.Player.Controller.Motor.TransientPosition;
        // Query the game's existing per-definition index, not a newly copied
        // actor dictionary or a Unity-wide object array. Loaded/current-zone
        // checks still run on every actual matching actor.
        var index=_app.Context.GameModel.identifiablesByIdent;
        if(index==null){Status="Нативный индекс предметов ещё не готов";return;}
        foreach(var query in _queries)
        {
            if(!index.TryGetValue(query.Type,out var models)||models==null)continue;
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
                if(!ExplorerPolicy.Within((position-origin).sqrMagnitude,ExplorerPolicy.RadarRadius))continue;
                var hit=new Hit{Name=query.Name,Reference=query.Reference,Position=position,Food=query.Food};
                if(_hits.Count<ExplorerPolicy.MaxRadarTargets)_hits.Add(hit);
                else
                {
                    int farthest=0;float farDistance=-1;
                    for(int i=0;i<_hits.Count;i++){float d=(_hits[i].Position-origin).sqrMagnitude;if(d>farDistance){farDistance=d;farthest=i;}}
                    if((position-origin).sqrMagnitude<farDistance)_hits[farthest]=hit;
                }
            }
        }
        _hits.Sort((a,b)=>(a.Position-origin).sqrMagnitude.CompareTo((b.Position-origin).sqrMagnitude));
        Status="Найдено в текущей зоне: "+_hits.Count+" · любимая еда / редкие материалы";
    }
    internal void Tick()
    {
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
    internal void Reset()
    {
        FavoriteFood=RareResources=false;_hits.Clear();_queries.Clear();_favorites.Clear();_materials.Clear();_camera=null;_zone=IntPtr.Zero;
        _nextScan=_nextHud=0;Heading="";Status="Радар выключен";SlimeReference="";SlimeName="Выберите слайма";
        FavoriteNames="Любимая еда ещё не выбрана";Scans=ExaminedModelCount=0;
    }
}