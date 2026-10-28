using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;
using UnityEngine;

namespace CreatorToolkit;

// Map progress: native save state (plort statues/doors, research drones) plus
// the player's own "done" marks, distances from the player and nearest target.
// Marks live beside mod settings (UserData/CreatorToolkit), never in .sav bytes.
internal sealed class MapMarkStore
{
    public int Version {get;set;}=1;
    public string Save {get;set;}="";
    public List<string> Marks {get;set;}=new();
}
internal sealed partial class MapTools
{
    internal const int MaxMarks=4096;
    private readonly HashSet<string> _filledSlots=new(StringComparer.Ordinal);
    private readonly HashSet<string> _readDrones=new(StringComparer.Ordinal);
    private readonly HashSet<string> _marks=new(StringComparer.Ordinal);
    private IntPtr _marksSession;
    private string _marksFile="",_marksSave="";
    private bool _marksWritable;
    private float _nextPlayerSample,_nextProgress;
    private string _progress="";
    private int _stateVersion;
    internal string MarksStatus {get;private set;}="Отметки «собрано» ещё не загружены";
    internal bool SortByDistance;
    internal bool HasPlayerPosition {get;private set;}
    internal Vector3 PlayerPosition {get;private set;}
    internal int MarkCount=>_marks.Count;

    private static string MarkKey(Point point)=>point.Category+"|"+point.Id;
    internal bool Marked(Point point)=>point?.Id!=null&&_marks.Count>0&&_marks.Contains(MarkKey(point));
    internal bool CanMark(Point point)=>point!=null&&!string.IsNullOrEmpty(point.Id)&&!point.SourceObservation&&!point.DroneObservation&&
        point.Category!=CustomWaypointTools.Category&&point.Id.Length<=160&&point.Category.Length<=40;

    private void EnsureMarks()
    {
        if(_marksSession!=IntPtr.Zero&&_marksSession==_app.Session)return;
        if(!_app.Ready)return;
        var metadata=NativeSaveMetadata.Read(GameContext.Instance.AutoSaveDirector);
        if(metadata==null||!metadata.HasValue||!WaypointStore.SaveKey(metadata.Value.GameName))return;
        _marksSession=_app.Session;_marks.Clear();_marksSave=metadata.Value.GameName;_stateVersion++;
        _marksFile=Path.Combine(_app.DataDirectory,"map-marks-"+_marksSave+".json");_marksWritable=true;
        try
        {
            if(File.Exists(_marksFile))
            {
                if(new FileInfo(_marksFile).Length>512*1024)throw new InvalidDataException("файл отметок слишком велик");
                var store=JsonSerializer.Deserialize<MapMarkStore>(File.ReadAllText(_marksFile));
                if(store==null||store.Version!=1||store.Save!=_marksSave||store.Marks==null||store.Marks.Count>MaxMarks)
                    throw new InvalidDataException("неверная версия или сейв");
                foreach(string mark in store.Marks)if(!string.IsNullOrWhiteSpace(mark)&&mark.Length<=220)_marks.Add(mark);
            }
            MarksStatus="Отметки «собрано»: "+_marks.Count+" · отдельно для сейва";
        }
        catch(Exception e)
        {
            _marks.Clear();_marksWritable=false;
            MarksStatus="Файл отметок не принят и не перезаписан: "+e.Message;
        }
    }
    internal void ToggleMark(Point point)
    {
        EnsureMarks();
        if(!CanMark(point))throw new InvalidOperationException("Эту метку нельзя отметить.");
        if(!_marksWritable||_marksSession!=_app.Session)throw new InvalidOperationException("Отметки недоступны: "+MarksStatus);
        string key=MarkKey(point);bool added=_marks.Add(key);
        if(!added)_marks.Remove(key);
        else if(_marks.Count>MaxMarks){_marks.Remove(key);throw new InvalidOperationException("Слишком много отметок.");}
        string temporary=_marksFile+".tmp."+Guid.NewGuid().ToString("N");
        try
        {
            var store=new MapMarkStore{Save=_marksSave,Marks=_marks.OrderBy(m=>m,StringComparer.Ordinal).ToList()};
            File.WriteAllText(temporary,JsonSerializer.Serialize(store,new JsonSerializerOptions{WriteIndented=true}));
            File.Move(temporary,_marksFile,true);
        }
        catch
        {
            if(added)_marks.Remove(key);else _marks.Add(key);
            throw;
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
        _stateVersion++;_filterResult=null;_nextProgress=0;
        MarksStatus="Отметки «собрано»: "+_marks.Count+" · сохранены";
        if(HideOpened&&added&&Selected==point)Selected=null;
    }

    // Called with the pod census (map or menu open, twice a second).
    private void RefreshProgressState()
    {
        EnsureMarks();
        int before=_filledSlots.Count+_readDrones.Count*7919;
        _filledSlots.Clear();_readDrones.Clear();
        var model=_app.Ready?_app.Context?.GameModel:null;
        if(model==null)return;
        try
        {
            var slots=model.AllSlots();
            if(slots!=null)foreach(var pair in slots)
            {
                var slot=pair.Value;
                if(!string.IsNullOrEmpty(pair.Key)&&slot!=null&&!slot.WasCollected&&slot.filled)_filledSlots.Add(pair.Key);
            }
        }
        catch{_filledSlots.Clear();}
        try
        {
            var research=model.AllResearchDrones();
            if(research!=null)foreach(var pair in research)
                if(!string.IsNullOrEmpty(pair.Key)&&pair.Value!=null&&pair.Value.read)_readDrones.Add(pair.Key);
        }
        catch{_readDrones.Clear();}
        if(before!=_filledSlots.Count+_readDrones.Count*7919){_stateVersion++;_nextProgress=0;}
    }
    // "slot:a,slot:b": done when every native puzzle slot of the object is filled.
    internal bool StateDone(Point point)
    {
        if(string.IsNullOrEmpty(point?.StateKey))return false;
        foreach(string part in point.StateKey.Split(','))
        {
            if(!part.StartsWith("slot:",StringComparison.Ordinal))return false;
            if(!_filledSlots.Contains(part.Substring(5)))return false;
        }
        return true;
    }
    internal bool HasNativeProgress(Point point)=>point!=null&&(point.Category=="Капсулы"&&CapsuleState(point)!=CapsuleTruthPolicy.OpenState.Unknown||
        point.Category=="Гордо"&&GordoState(point)!=GordoMapPolicy.State.Unknown||
        point.Category=="Дроны"&&!point.DroneObservation&&point.Id!=null&&!point.Id.StartsWith("component-",StringComparison.Ordinal)||
        !string.IsNullOrEmpty(point.StateKey));
    internal string ProgressLabel(Point point)
    {
        if(point==null)return "";
        if(Marked(point))return "Отмечено вами: собрано";
        if(!HasNativeProgress(point))return "";
        return Opened(point)?"Пройдено (по сейву)":"Не пройдено (по сейву)";
    }

    // One compact line: done/total for layers that have save state or marks.
    internal string ProgressSummary()
    {
        if(Time.unscaledTime<_nextProgress)return _progress;
        _nextProgress=Time.unscaledTime+1f;
        var parts=new List<string>();
        void Count(string title,Func<Point,bool> select)
        {
            int total=0,done=0;
            foreach(var point in Points)if(select(point)){total++;if(Opened(point))done++;}
            if(total>0)parts.Add(title+" "+done+"/"+total);
        }
        Count("Капсулы",p=>p.Category=="Капсулы"&&CapsuleState(p)!=CapsuleTruthPolicy.OpenState.Unknown);
        Count("Гордо",p=>p.Category=="Гордо"&&GordoState(p)!=GordoMapPolicy.State.Unknown);
        Count("Дроны",p=>p.Category=="Дроны"&&HasNativeProgress(p));
        Count("Врата",p=>p.Category==FandomMapMarkers.RareCategory&&p.FilterKind=="Врата плортов"&&!string.IsNullOrEmpty(p.StateKey));
        Count("Статуи",p=>p.Category==FandomMapMarkers.RareCategory&&p.FilterKind=="Статуя для плорта");
        int marks=0;foreach(var point in Points)if(Marked(point))marks++;
        if(marks>0)parts.Add("Мои отметки "+marks);
        return _progress=parts.Count==0?"Прогресс появится после загрузки сейва":string.Join(" · ",parts);
    }

    internal void SamplePlayer()
    {
        if(Time.unscaledTime<_nextPlayerSample)return;
        _nextPlayerSample=Time.unscaledTime+.25f;
        try
        {
            var motor=_app.Ready?_app.Context?.Player?.GetComponentInChildren<SRCharacterController>(true)?.Motor:null;
            if(motor==null){HasPlayerPosition=false;return;}
            var position=motor.TransientPosition;
            HasPlayerPosition=float.IsFinite(position.x)&&float.IsFinite(position.z);PlayerPosition=position;
        }
        catch{HasPlayerPosition=false;}
    }
    // Horizontal metres; -1 when the player position is unknown.
    internal float Distance(Point point)
    {
        if(point==null||!HasPlayerPosition)return -1f;
        float dx=point.Position.x-PlayerPosition.x,dz=point.Position.z-PlayerPosition.z;
        return Mathf.Sqrt(dx*dx+dz*dz);
    }
    // Formatting lives in a culture-free policy: a named-culture lookup throws
    // in the globalization-invariant game runtime and used to abort the overlay.
    internal static string DistanceText(float metres)=>MapDistanceText.Format(metres);
    // Nearest point of the current filter that is not done yet.
    internal Point NearestInFilter(bool currentPage=false)
    {
        SamplePlayer();
        if(!HasPlayerPosition)return null;
        Point best=null;float bestDistance=float.MaxValue;
        foreach(var point in Filter(currentPage))
        {
            if(currentPage&&PinnedOnly(point))continue;
            if(Opened(point))continue;
            float distance=Distance(point);
            if(distance>=0&&distance<bestDistance){best=point;bestDistance=distance;}
        }
        return best;
    }
}
