using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using UnityEngine;
namespace CreatorToolkit;
internal sealed partial class MapTools
{
    private readonly Dictionary<string,Vector3> _ghostAnchors=new(StringComparer.Ordinal);
    // M29: zone recorded for each route in GhostDroneIndex.tsv (column 6); M29GhostZonePolicy.
    private readonly Dictionary<string,string> _ghostAuthored=new(StringComparer.Ordinal);
    private bool _ghostMapResolved;private int _ghostMapAttempts;
    internal int GhostMapResolutions {get;private set;}
    internal int GhostLiveBindings {get;private set;}
    internal IEnumerable<Point> GhostRoutes=>Points.Where(p=>p.Id.StartsWith("component-route|",StringComparison.Ordinal));
    internal string GhostZoneSummary=>M29GhostZonePolicy.Summary(GhostRoutes.Select(p=>p.ZoneSource));
    // M29: route (spawner id) -> the reward TreasurePod on its ComponentAcqDroneSpawner. That pod
    // sits on the route anchor where nothing is visible, so the phantom route shows its reward
    // and the capsule layer never lists it (CapsuleMapPolicy.DroneRewardRoute).
    private readonly Dictionary<string,string> _ghostRewardPods=new(StringComparer.Ordinal);
    private readonly HashSet<string> _ghostAwarded=new(StringComparer.Ordinal);
    internal int GhostRewardPodCount=>_ghostRewardPods.Count;
    internal string GhostRewardPod(string spawnerId)=>spawnerId!=null&&_ghostRewardPods.TryGetValue(spawnerId,out var pod)?pod:null;
    private IEnumerable<(string Id,double X,double Y,double Z)> GhostAnchorList()=>
        _ghostAnchors.Select(p=>(p.Key,(double)p.Value.x,(double)p.Value.y,(double)p.Value.z));
    // Static capsule row (spawnerId null: matched by its route anchor) or live pod on a spawner.
    private string TakeGhostReward(string podId,string spawnerId,Vector3 position)
    {
        string route=!string.IsNullOrEmpty(spawnerId)?spawnerId:CapsuleMapPolicy.DroneRewardRoute(position.x,position.y,position.z,GhostAnchorList());
        if(string.IsNullOrEmpty(route)||string.IsNullOrWhiteSpace(podId))return null;
        _ghostRewardPods[route]=podId;return route;
    }
    internal string GhostRewardSummary(string spawnerId)
    {
        string pod=GhostRewardPod(spawnerId);if(pod==null)return null;
        var view=Capsules.Get(pod);string summary=view.Baked?view.RewardSummary:view.Title;
        return string.IsNullOrWhiteSpace(summary)?null:summary;
    }
    private string GhostRewardText(string spawnerId)=>GhostRewardSummary(spawnerId) is string summary?"\nНаграда дрона: "+summary:"";
    // DisplayName(): hover, lists and compass name what the phantom of a route hands out.
    internal string GhostRouteReward(Point point)=>point?.Id!=null&&point.Id.StartsWith("component-route|",StringComparison.Ordinal)?
        GhostRewardSummary(point.Id.Substring(16)):null;
    // The save keeps the reward pod: OPEN means this phantom already paid out.
    private bool GhostRewardTaken(string spawnerId)
    {
        string pod=GhostRewardPod(spawnerId);
        return pod!=null&&!_ambiguousPodIds.Contains(pod)&&Capsules.GetState(pod)==CapsuleTruthPolicy.OpenState.Open;
    }
    // Opened(): a phantom route whose reward was received counts as done («скрывать открытые»).
    internal bool GhostPaid(Point point)
    {
        if(point?.Id==null||!point.Id.StartsWith("component-route|",StringComparison.Ordinal))return false;
        string id=point.Id.Substring(16);return _ghostAwarded.Contains(id)||GhostRewardTaken(id);
    }
    private void LoadGhostRoutes(string catalog)
    {
        _ghostAnchors.Clear();_ghostAuthored.Clear();_ghostMapResolved=false;_ghostMapAttempts=0;
        _ghostRewardPods.Clear();_ghostAwarded.Clear();
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("CreatorToolkit.GhostDroneIndex.tsv");
        if(stream==null)return;using var reader=new StreamReader(stream);
        if(reader.ReadLine()!="#catalog="+catalog||catalog.Length==0)return;
        var zones=GhostZones();
        string line;
        while((line=reader.ReadLine())!=null)
        {
            var c=line.TrimEnd('\r').Split('\t');if(c.Length<5||_ghostAnchors.Count>=64)continue;
            if(!float.TryParse(c[2],NumberStyles.Float,CultureInfo.InvariantCulture,out float x)||!float.TryParse(c[3],NumberStyles.Float,CultureInfo.InvariantCulture,out float y)||!float.TryParse(c[4],NumberStyles.Float,CultureInfo.InvariantCulture,out float z))continue;
            if(!float.IsFinite(x)||!float.IsFinite(y)||!float.IsFinite(z)||_ghostAnchors.ContainsKey(c[0]))continue;
            // M29: test/aggregate groups (AllZones...) also list real zone scenes, so "the only
            // group listing the scene" left 7 of 12 routes without a zone (no atlas, no compass).
            string authored=c.Length>5?c[5].Trim():"";
            var position=new Vector3(x,y,z);_ghostAnchors.Add(c[0],position);_ghostAuthored[c[0]]=authored;
            var route=new Point{Id="component-route|"+c[0],Category="Дроны",Name="Фантом · место появления",
                Position=position,ExactHeight=false,SourceScene=c[1],Contents="ghost phantom фантом дрон компонент "+c[0],
                Detail="Начало нативного маршрута, не текущее положение дрона. Условия появления и получение награды пока неизвестны."};
            BindGhost(route,M29GhostZonePolicy.Resolve(ScenePath(c[1]),authored,zones));
            Points.Add(route);
        }
    }
    // Every gameplay group of the index with its located scene references (BuildGordoSceneMembership).
    private List<M29GhostZonePolicy.Zone> GhostZones()
    {
        var zones=new List<M29GhostZonePolicy.Zone>(_groups.Count);
        foreach(string name in _groups.Keys)
        {
            _gordoScenes.TryGetValue(name,out var scenes);
            zones.Add(new M29GhostZonePolicy.Zone(name,_gordoSceneComplete.Contains(name),scenes));
        }
        return zones;
    }
    private void BindGhost(Point route,M29GhostZonePolicy.Binding binding)
    {
        SceneGroup group=binding.Bound?GroupNamed(binding.Group):null;
        route.Group=group;route.GroupName=group!=null?group.name:binding.Group;route.ZoneSource=binding.Source;
    }
    // The map reference (ZoneSceneGroupMapping) exists only with the atlas UI: while the atlas is
    // open, routes bound by a weaker static source retry once with it (a few ticks at most).
    private void ResolveGhostZonesWithMaps()
    {
        if(_ghostMapResolved||!NativeMapOpen)return;
        var pending=GhostRoutes.Where(p=>M29GhostZonePolicy.MapMayImprove(p.ZoneSource)).ToList();
        if(pending.Count==0||++_ghostMapAttempts>5){_ghostMapResolved=true;return;}
        var zones=GhostZones();int pages=0;
        bool HasPage(string name)
        {
            try{if(MapForGroup(GroupNamed(name))==null)return false;pages++;return true;}
            catch{return false;}
        }
        foreach(var route in pending)
        {
            _ghostAuthored.TryGetValue(route.Id.Substring(16),out var authored);
            var binding=M29GhostZonePolicy.Resolve(ScenePath(route.SourceScene),authored,zones,HasPage);
            if(binding.Source!=M29GhostZonePolicy.MapPage)continue;
            BindGhost(route,binding);GhostMapResolutions++;_filterResult=null;
        }
        if(pages>0)_ghostMapResolved=true;
    }
    // The native drone model of a route names the scene group the game keeps it in.
    private void BindGhostLive(Point route,SceneGroup live)
    {
        if(route==null||live==null||live.WasCollected)return;
        string name=live.name??"";
        var binding=M29GhostZonePolicy.WithLive(new M29GhostZonePolicy.Binding(route.GroupName,route.ZoneSource),name,GroupNamed(name)!=null);
        if(binding.Source!=M29GhostZonePolicy.Live)return;
        if(route.ZoneSource!=M29GhostZonePolicy.Live||!string.Equals(route.GroupName,name,StringComparison.OrdinalIgnoreCase))
        {GhostLiveBindings++;_filterResult=null;}
        route.Group=GroupNamed(name);route.GroupName=route.Group.name;route.ZoneSource=M29GhostZonePolicy.Live;
    }
    private void ResetGhostRouteStates()
    {
        foreach(var point in Points)if(point.Id.StartsWith("component-route|",StringComparison.Ordinal))
        {
            string id=point.Id.Substring(16);if(_ghostAnchors.TryGetValue(id,out var anchor))point.Position=anchor;
            // M29: the reward pod in the save says whether this phantom already paid out.
            bool paid=GhostRewardTaken(id);string pod=GhostRewardPod(id);
            point.ExactHeight=false;point.Name=paid?"Фантом · награда получена":"Фантом · место появления";
            point.Detail=(paid?"Награда уже получена (капсула дрона открыта). ":"")+
                "Начало нативного маршрута, не текущий дрон. Условия появления не подтверждены."+GhostRewardText(id);
            point.Contents="ghost phantom фантом дрон компонент "+id+(pod!=null?" "+pod+" "+Capsules.Get(pod).Search:"");
        }
        _ghostAwarded.Clear();
    }
}
