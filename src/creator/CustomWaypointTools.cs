using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using UnityEngine;

namespace CreatorToolkit;

internal sealed class CustomWaypointTools
{
    internal const string Category="Мои метки";
    private readonly ToolkitMod _app;
    private WaypointStore _store=new();
    private IntPtr _session;
    private string _file="",_goal="";
    private bool _writable;
    private Camera _camera;
    private float _nextHud;
    internal string DraftName="Метка",Status="Метки хранятся отдельно для каждого сейва";
    internal int DraftColor;
    internal bool ShowCompass {get;private set;}
    // 0.8.1 «Маяк в мире» (mockup 02): the floating flag over the goal; the compass goal stays when off.
    internal bool Beacon {get;private set;}=true;
    internal string LastAddedId {get;private set;}="";
    internal string Heading {get;private set;}="";
    internal string Direction {get;private set;}="↑";
    internal Vector2 BeaconScreen {get;private set;}
    internal bool BeaconVisible {get;private set;}
    internal int GoalColor {get;private set;}
    internal IReadOnlyList<WaypointRecord> Records=>_store.Points;
    // M28: the custom marker shown as the compass goal (empty when hidden).
    internal string GoalId=>ShowCompass?_goal:"";
    internal string SaveKey=>_store.Save;
    internal CustomWaypointTools(ToolkitMod app)=>_app=app;
    internal void EnsureLoaded()
    {
        if(_session!=IntPtr.Zero&&_session==_app.Session)return;
        if(!_app.Ready)return;
        var metadata=NativeSaveMetadata.Read(GameContext.Instance.AutoSaveDirector);
        if(metadata==null||!metadata.HasValue||!WaypointStore.SaveKey(metadata.Value.GameName))return;
        _session=_app.Session;_store=new WaypointStore{Save=metadata.Value.GameName};
        _file=Path.Combine(_app.DataDirectory,"waypoints-"+_store.Save+".json");_writable=true;
        try
        {
            if(File.Exists(_file))
            {
                if(new FileInfo(_file).Length>128*1024)throw new InvalidDataException("Файл меток слишком велик.");
                _store=WaypointStore.Read(File.ReadAllText(_file),_store.Save);
            }
            Status="Мои метки: "+_store.Points.Count+" / "+ExplorerPolicy.MaxCustomWaypoints+" · отдельно от игрового сейва";
            if(DraftName=="Метка")DraftName="Метка "+(_store.Points.Count+1);
        }
        catch(Exception e)
        {
            _writable=false;Status="Файл меток не принят. Исходный файл не перезаписан: "+e.Message;
        }
    }
    private void Guard()
    {
        EnsureLoaded();
        if(!_writable||_session!=_app.Session||!_app.Ready||_app.Map.Busy)
            throw new InvalidOperationException("Метки недоступны во время смены мира или ошибки их файла.");
    }
    private void Commit()
    {
        string temporary=_file+".tmp."+Guid.NewGuid().ToString("N");
        try{File.WriteAllText(temporary,_store.Json());File.Move(temporary,_file,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
        Status="Мои метки: "+_store.Points.Count+" / "+ExplorerPolicy.MaxCustomWaypoints+" · сохранены";
        // The atomic write is confirmed. A UI refresh failure must not roll
        // back only memory while the successfully saved file already changed.
        try{_app.Map.RefreshIndex();}catch(Exception e){_app.Fail("custom marker display refresh",e);}
    }
    internal WaypointRecord Add(MapTools.Point point,string name,int color)
    {
        Guard();
        name=(name??"").Trim();
        if(name.Length==0)name="Метка "+(_store.Points.Count+1);
        if(name.Length>ExplorerPolicy.MaxNameLength||name.Any(char.IsControl)||point?.Group==null||
            !point.Group.IsGameplay||!ExplorerPolicy.Coordinates(point.Position.x,point.Position.y,point.Position.z)||
            color<0||color>=ExplorerPolicy.ColorCount||_store.Points.Count>=ExplorerPolicy.MaxCustomWaypoints)
            throw new InvalidOperationException("Проверьте имя, цвет, игровую зону и лимит 32 меток.");
        var record=new WaypointRecord{Id=Guid.NewGuid().ToString("N"),Name=name,Color=color,Group=point.Group.name,
            X=point.Position.x,Y=point.Position.y,Z=point.Position.z,ExactHeight=point.ExactHeight,
            Created=DateTimeOffset.UtcNow.ToUnixTimeSeconds()};
        _store.Points.Add(record);
        try{Commit();}catch{_store.Points.Remove(record);throw;}
        LastAddedId=record.Id;DraftName="Метка "+(_store.Points.Count+1);
        _app.Map.Visible.Add(Category);_app.Map.Selected=FindPoint(record.Id);return record;
    }
    internal void SaveCurrent()
    {
        Guard();
        var group=SystemContext.Instance.SceneLoader.CurrentSceneGroup;
        var motor=_app.Player.Controller?.Motor;
        if(group==null||motor==null||_app.Player.Flight||
            !_app.Player.TryNearbyMapGround(motor.TransientPosition,out var safe))
            throw new InvalidOperationException("Сохраните точку, стоя на проверенной свободной земле, без NoClip.");
        Add(new MapTools.Point{Group=group,Position=safe,ExactHeight=true,AllowNearbyLanding=true},DraftName,DraftColor);
    }
    internal List<MapTools.Point> MapPoints(IReadOnlyDictionary<string,SceneGroup> groups)
    {
        EnsureLoaded();var points=new List<MapTools.Point>(_store.Points.Count);
        foreach(var record in _store.Points)
        {
            groups.TryGetValue(record.Group,out var group);
            points.Add(new MapTools.Point{Id=record.Id,Name=record.Name,Category=Category,GroupName=record.Group,
                Group=group,Position=new Vector3(record.X,record.Y,record.Z),ExactHeight=record.ExactHeight,
                AllowNearbyLanding=true,ColorIndex=record.Color,Detail="Пользовательская метка"});
        }
        return points;
    }
    internal MapTools.Point FindPoint(string id)=>_app.Map.Points.FirstOrDefault(p=>p.Category==Category&&p.Id==id);
    internal WaypointRecord Find(string id)=>_store.Points.FirstOrDefault(p=>p.Id==id);
    internal void Rename(string id,string name,int color)
    {
        Guard();var record=Find(id)??throw new InvalidOperationException("Метка удалена.");
        string old=record.Name;int oldColor=record.Color;name=(name??"").Trim();
        if(name.Length==0||name.Length>ExplorerPolicy.MaxNameLength||name.Any(char.IsControl)||color<0||color>=ExplorerPolicy.ColorCount)
            throw new InvalidOperationException("Имя до 48 символов; выберите один из шести цветов.");
        record.Name=name;record.Color=color;
        try{Commit();}catch{record.Name=old;record.Color=oldColor;throw;}
    }
    internal void Delete(string id)
    {
        Guard();int index=_store.Points.FindIndex(p=>p.Id==id);if(index<0)return;
        var record=_store.Points[index];_store.Points.RemoveAt(index);
        try{Commit();}catch{_store.Points.Insert(index,record);throw;}
        if(_goal==id){_goal="";ShowCompass=false;Heading="";BeaconVisible=false;}
    }
    internal void SetGoal(string id)
    {
        EnsureLoaded();
        if(Find(id)==null)throw new InvalidOperationException("Выберите свою метку.");
        _goal=id;ShowCompass=true;_nextHud=0;
    }
    internal void HideGoal(){ShowCompass=false;BeaconVisible=false;Heading="";_camera=null;}
    internal void SetBeacon(string id,bool on)
    {
        if(on&&GoalId!=id)SetGoal(id);
        Beacon=on;if(!on)BeaconVisible=false;_nextHud=0;
    }
    internal void Tick()
    {
        EnsureLoaded();
        if(!ShowCompass||Time.unscaledTime<_nextHud)return;
        _nextHud=Time.unscaledTime+.25f;BeaconVisible=false;
        var record=Find(_goal);if(record==null){HideGoal();return;}
        GoalColor=record.Color;
        if(!_app.Ready||_app.Map.Busy||_app.MenuOpen||_app.Map.NativeMapOpen||_app.Cinema.HideHud)return;
        if(SystemContext.Instance.SceneLoader.CurrentSceneGroup?.name!=record.Group)
        {Heading=record.Name+" · другой остров";Direction="◎";return;}
        var position=_app.Player.Controller.Motor.TransientPosition;
        var target=new Vector3(record.X,record.Y,record.Z);var delta=target-position;
        if(_camera==null||_camera.WasCollected)_camera=Camera.main;
        var forward=_camera!=null?_camera.transform.forward:_app.Context.Player.transform.forward;
        Direction=RadarTools.Arrow(Vector3.SignedAngle(new Vector3(forward.x,0,forward.z),new Vector3(delta.x,0,delta.z),Vector3.up));
        Heading=record.Name+" · "+delta.magnitude.ToString("0")+" м";
        // X/Z map clicks have no proven ground height. Show a compass but
        // never invent an underground 3D beacon for those markers.
        if(!Beacon)return;
        if(_camera==null||!record.ExactHeight)return;
        var screen=_camera.WorldToScreenPoint(target+Vector3.up*1.5f);
        BeaconScreen=new Vector2(screen.x,Screen.height-screen.y);
        BeaconVisible=screen.z>0&&screen.x>32&&screen.x<Screen.width-32&&screen.y>180&&screen.y<Screen.height-100;
    }
    internal void ReloadForTest()
    {
        Guard();string save=_store.Save;_store=WaypointStore.Read(File.ReadAllText(_file),save);_app.Map.RefreshIndex();
    }
    internal string ExportForTest(){_app.Audit.GuardStressCopy();Guard();return _store.Json();}
    internal void DeleteOwnedAuditFile(string ownedSave)
    {
        if(!WaypointStore.SaveKey(ownedSave)||ownedSave!=_store.Save||!_app.Audit.Active)
            throw new InvalidOperationException("Owned custom-point cleanup identity mismatch.");
        if(_store.Points.Count!=0)throw new InvalidOperationException("Audit points must be explicitly removed before cleanup.");
        if(File.Exists(_file))File.Delete(_file);
    }
    internal void Reset()
    {
        _session=IntPtr.Zero;_store=new();_file=_goal="";_writable=false;_camera=null;
        ShowCompass=false;BeaconVisible=false;Heading="";_nextHud=0;Beacon=true;LastAddedId="";
    }
}