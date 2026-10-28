using System;
using System.Collections.Generic;
using Il2CppMonomiPark.World;
using UnityEngine;
using SceneGroup = Il2CppMonomiPark.SlimeRancher.SceneManagement.SceneGroup;
using ZoneDefinition = Il2CppMonomiPark.SlimeRancher.World.ZoneDefinition;
using ZoneSceneGroupMapping = Il2CppMonomiPark.SlimeRancher.Map.ZoneSceneGroupMapping;

namespace CreatorToolkit;

// M27 «Трекинг мира»: opened/unopened pods, burst/alive gordos, AccessDoors of the
// save and atlas teleport points. Read-only except OpenDoor (explicit, confirmed).
// Pod and gordo truth comes from MapTools (save models, not scene presence);
// doors come from GameModel.AllDoors(). Selection and sorting: M27TrackingPolicy.
internal sealed class WorldTrackingTools
{
    private const int MaxLiveDoors=512;
    private readonly ToolkitMod _app;
    private readonly List<M27TrackingPolicy.Row> _rows=new();
    private readonly HashSet<string> _keys=new(StringComparer.Ordinal);
    private readonly Dictionary<string,MapTools.Point> _doorPoints=new(StringComparer.Ordinal);
    private readonly Dictionary<string,MapTools.Point> _liveDoors=new(StringComparer.Ordinal);
    private IntPtr _session;
    private float _nextRefresh,_nextDoorScan;
    // Zone titles per scene group (or "@id"): the game's ZoneSceneGroupMapping, else the
    // native atlas zone polygon. Successes stay for the session; misses retry later
    // (localization tables and the atlas UI load after the world).
    private readonly Dictionary<string,string> _zoneTitles=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,float> _zoneMisses=new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ZoneSceneGroupMapping> _mappings=new();
    private Dictionary<string,SceneGroup> _allGroups;
    private float _lookupUntil;
    private int _zoneLogs,_rawZones;
    internal IReadOnlyList<M27TrackingPolicy.Row> Rows=>_rows;
    internal int Version{get;private set;}
    internal int Teleporters{get;private set;}
    internal int TeleporterNodes{get;private set;}
    internal string Status{get;private set;}="Списки обновляются, пока открыт раздел «Трекинг мира»";
    internal WorldTrackingTools(ToolkitMod app)=>_app=app;

    internal void Reset()
    {
        _rows.Clear();_keys.Clear();_doorPoints.Clear();_liveDoors.Clear();
        _zoneTitles.Clear();_zoneMisses.Clear();_mappings.Clear();_allGroups=null;_lookupUntil=0;_zoneLogs=0;
        _session=IntPtr.Zero;_nextRefresh=_nextDoorScan=0;Teleporters=TeleporterNodes=0;Version++;
    }

    // Throttled to once per second; the UI calls it every frame while visible.
    internal void Refresh(bool force)
    {
        var map=_app.Map;
        if(!_app.Ready||map==null)
        {
            if(_rows.Count>0){_rows.Clear();Version++;}
            Status="Дождитесь загрузки мира";
            return;
        }
        if(_session!=_app.Session){Reset();_session=_app.Session;}
        float now=Time.unscaledTime;
        if(!force&&now<_nextRefresh)return;
        _nextRefresh=now+M27TrackingPolicy.RefreshSeconds;
        map.SamplePlayer();
        _rows.Clear();_keys.Clear();_doorPoints.Clear();_rawZones=0;
        foreach(var point in map.Points)
        {
            if(point==null)continue;
            int kind=M27TrackingPolicy.KindOf(point.Category);
            if(kind<0)continue;
            if(kind==M27TrackingPolicy.Doors)
            {
                if(!string.IsNullOrEmpty(point.Id)&&!_doorPoints.ContainsKey(point.Id))_doorPoints[point.Id]=point;
                continue;
            }
            var row=Row(map,point,kind);
            if(_keys.Add(row.Key))_rows.Add(row);
        }
        int doors=AddDoors(map,now,force);
        var model=_app.Context?.GameModel;
        Teleporters=model?.teleporters?.Count??0;
        TeleporterNodes=model?.teleporterNodes?.Count??0;
        Version++;
        Status="Обновлено: точек атласа "+(_rows.Count-doors)+" · дверей сейва "+doors+
            (map.HasPlayerPosition?"":" · позиция игрока неизвестна")+
            (_rawZones>0?" · названия зон появятся после открытия карты игры":"");
    }

    // Player-facing zone title; the raw scene group stays searchable (Row.Group).
    // 1) ZoneSceneGroupMapping of the point's scene group (instance, then name);
    // 2) the native atlas zone polygon at the point (live doors, unconfirmed pods).
    // Never a nearest-marker guess: an unresolved point keeps its raw group name.
    private string ZoneTitle(MapTools map,MapTools.Point point)
    {
        if(point==null)return "Зона неизвестна";
        // Harvested while the atlas was open (the mapping and polygons load only with it).
        string harvested=map.ZoneTitleFor(point);
        if(harvested!=null)return harvested;
        string group=point.GroupName??"";
        string key=group.Length>0?group:"@"+point.Id;
        if(_zoneTitles.TryGetValue(key,out var title))return title;
        string raw=group.Length>0?group:"Зона неизвестна";
        float now=Time.unscaledTime;
        if(_zoneMisses.TryGetValue(key,out float retry)&&now<retry){_rawZones++;return raw;}
        string why;
        try
        {
            title=GroupZoneTitle(map,point,group,now,out why);
            if(title==null)
            {
                var zone=map.ZoneAt(point);
                title=ZoneName(zone);
                why+=title!=null?"; atlas polygon "+zone.name:zone!=null?"; atlas zone "+zone.name+" not localized":"; no atlas zone";
            }
        }
        catch(Exception e){title=null;why="error "+e.GetType().Name+": "+e.Message;}
        if(_zoneLogs<12){_zoneLogs++;_app.LoggerInstance.Msg("Tracking zone title: "+key+" -> "+(title??"(raw)")+" ("+why+")");}
        if(title==null){_zoneMisses[key]=now+M27TrackingPolicy.ZoneRetrySeconds;_rawZones++;return raw;}
        _zoneTitles[key]=title;_zoneMisses.Remove(key);
        return title;
    }
    private string GroupZoneTitle(MapTools map,MapTools.Point point,string group,float now,out string why)
    {
        if(now>=_lookupUntil)
        {
            _lookupUntil=now+M27TrackingPolicy.ZoneRetrySeconds;
            _mappings.Clear();_allGroups=null;
            foreach(var mapping in Resources.FindObjectsOfTypeAll<ZoneSceneGroupMapping>())if(mapping!=null)_mappings.Add(mapping);
        }
        var sceneGroup=point.Group!=null&&!point.Group.WasCollected?point.Group:map.GroupNamed(group);
        if(sceneGroup==null&&group.Length>0)
        {
            if(_allGroups==null)
            {
                _allGroups=new Dictionary<string,SceneGroup>(StringComparer.OrdinalIgnoreCase);
                foreach(var candidate in Resources.FindObjectsOfTypeAll<SceneGroup>())
                    if(candidate!=null&&!string.IsNullOrEmpty(candidate.name)&&!_allGroups.ContainsKey(candidate.name))_allGroups[candidate.name]=candidate;
            }
            _allGroups.TryGetValue(group,out sceneGroup);
        }
        if(sceneGroup==null){why="no scene group";return null;}
        int matched=0;
        foreach(var mapping in _mappings)
        {
            if(mapping==null||mapping.WasCollected||!mapping.TryGetValue(sceneGroup,out var zone)||zone==null)continue;
            matched++;
            string title=ZoneName(zone);
            if(title!=null){why="mapping "+mapping.name+" zone "+zone.name;return title;}
        }
        why=matched>0?"mapping zone not localized":"no mapping for "+sceneGroup.name+" ("+_mappings.Count+" mappings)";
        return null;
    }
    private static string ZoneName(ZoneDefinition zone)=>MapTools.ZoneName(zone);

    private M27TrackingPolicy.Row Row(MapTools map,MapTools.Point point,int kind)
    {
        var row=new M27TrackingPolicy.Row{Kind=kind,Id=point.Id??"",Zone=ZoneTitle(map,point),Group=point.GroupName??"",Tag=point,Distance=map.Distance(point)};
        switch(kind)
        {
            case M27TrackingPolicy.Pods:
                row.Name=map.DisplayName(point)??point.Name??"Капсула";
                switch(map.CapsuleState(point))
                {
                    case CapsuleTruthPolicy.OpenState.Open:row.Done=true;row.State="Открыта";break;
                    case CapsuleTruthPolicy.OpenState.Locked:row.Done=false;row.State="Закрыта";break;
                    default:
                        // M28: no save model inside a complete census = area never loaded = untouched.
                        if(map.PodNeverLoaded(point)){row.Done=false;row.Unvisited=true;row.State=M28TrackingStatePolicy.PodNeverLoaded;}
                        else{row.Done=null;row.State="Состояние неизвестно";}
                        break;
                }
                break;
            case M27TrackingPolicy.Gordos:
                var state=map.GordoState(point);
                row.Name=point.GordoDisplayName??point.Name??"Гордо";
                if(state==GordoMapPolicy.State.Burst){row.Done=true;row.State="Лопнул";}
                else if(state==GordoMapPolicy.State.Unknown&&map.GordoNeverLoaded(point)){row.Done=false;row.Unvisited=true;row.State=M28TrackingStatePolicy.GordoNeverLoaded;}
                else if(state==GordoMapPolicy.State.Unknown){row.Done=null;row.State="Состояние неизвестно";}
                else{row.Done=false;row.State=map.GordoStateLabel(point);}
                break;
            default:
                row.Name=point.Name??"Телепорт";
                row.Done=null;
                row.State=(point.Detail??"").Contains("StaticTeleporterNode",StringComparison.Ordinal)?"Статичный узел":"Точка телепортации";
                break;
        }
        return row;
    }

    // Every AccessDoor model of the save; the place comes from the atlas index or,
    // for doors missing there, from a loaded scene object (scene group unconfirmed).
    private int AddDoors(MapTools map,float now,bool force)
    {
        var doors=_app.Context?.GameModel?.AllDoors();
        if(doors==null)return 0;
        if(force||now>=_nextDoorScan){ScanLiveDoors();_nextDoorScan=now+M27TrackingPolicy.DoorScanSeconds;}
        int count=0;
        foreach(var pair in doors)
        {
            string id=pair.Key;
            if(string.IsNullOrEmpty(id))continue;
            var model=pair.Value;
            int state=model!=null?(int)model.state:-1;
            if(!_doorPoints.TryGetValue(id,out var point))_liveDoors.TryGetValue(id,out point);
            var row=new M27TrackingPolicy.Row
            {
                Kind=M27TrackingPolicy.Doors,Id=id,Tag=point,
                Name=point?.Name??"Дверь",
                Zone=ZoneTitle(map,point),Group=point?.GroupName??"",
                Done=model==null?null:M27TrackingPolicy.DoorIsOpen(state),
                State=M27TrackingPolicy.DoorState(state)+(point==null?" · место на карте не найдено":""),
                Distance=point!=null?map.Distance(point):-1f
            };
            if(_keys.Add(row.Key)){_rows.Add(row);count++;}
        }
        return count;
    }
    private void ScanLiveDoors()
    {
        _liveDoors.Clear();
        foreach(AccessDoor door in Resources.FindObjectsOfTypeAll<AccessDoor>())
        {
            if(_liveDoors.Count>=MaxLiveDoors)break;
            if(door==null)continue;
            var go=door.gameObject;
            var scene=go.scene;
            if(!scene.IsValid()||!scene.isLoaded)continue;
            string id=door._id;
            if(string.IsNullOrEmpty(id)||_liveDoors.ContainsKey(id))continue;
            // An additive scene is not necessarily CurrentSceneGroup: keep the group
            // unknown (no teleport) instead of attaching it to the player's region.
            _liveDoors[id]=new MapTools.Point{Id=id,Name="Дверь · "+go.name,Category="Двери",GroupName=scene.name,
                Position=go.transform.position,ExactHeight=true,AllowNearbyLanding=true,
                Detail="Загруженная дверь AccessDoor; группа сцены не подтверждена"};
        }
    }

    internal string Summary(int kind,M27TrackingPolicy.Tally tally)=>
        M27TrackingPolicy.Summary(kind,tally)+
        (M27TrackingPolicy.ClampKind(kind)==M27TrackingPolicy.Teleports?" · в сейве: телепортеров "+Teleporters+" · узлов "+TeleporterNodes:"");

    // Explicit single-door change (confirmed in the UI), same path as UnlockTools.Doors.
    internal void OpenDoor(string id)
    {
        if(!_app.Ready||_app.Map.Busy)throw new InvalidOperationException("Дождитесь загрузки мира.");
        if(string.IsNullOrEmpty(id))throw new InvalidOperationException("Выберите дверь.");
        var doors=_app.Context.GameModel.AllDoors();
        if(doors==null||!doors.TryGetValue(id,out var model)||model==null)
            throw new InvalidOperationException("Дверь "+id+" не найдена в сейве.");
        if(model.state==AccessDoor.State.OPEN){_app.Notify("Дверь уже открыта");return;}
        model.state=AccessDoor.State.OPEN;
        model.NotifyParticipants();
        int updated=0;
        foreach(AccessDoor door in Resources.FindObjectsOfTypeAll<AccessDoor>())
        {
            if(door==null||!door.gameObject.scene.IsValid()||!door.gameObject.scene.isLoaded||door._model==null)continue;
            if(door._id!=id)continue;
            door.ForceUpdate(true);updated++;
        }
        _nextRefresh=0;
        _app.Notify("Дверь открыта: "+id+(updated>0?" · сцена обновлена":"")+". Изменение сохраняется в сейве.");
    }
}
