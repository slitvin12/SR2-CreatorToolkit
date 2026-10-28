using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Player;
using UnityEngine;

namespace CreatorToolkit;

// Native corral devices remain authoritative. No actor deletion, fake loot,
// unbounded world scan, raw GameModel writes or artificial storage capacity.
internal sealed class FarmTools
{
    private readonly ToolkitMod _app;
    private readonly List<PlortCollector> _collectors=new();
    private readonly List<SlimeFeeder> _feeders=new();
    private IntPtr _session;
    private float _nextScan,_nextTick,_nextCollect;
    internal bool Collect {get;private set;} internal bool Feed {get;private set;}
    internal bool Enabled=>Collect||Feed;
    internal int CollectorCount=>_collectors.Count;
    internal int FeederCount=>_feeders.Count;
    internal int ScannedPlotCount {get;private set;}
    internal int TransferredUnits {get;private set;} internal int CollectionRequests {get;private set;}
    internal string Status {get;private set;}="Фермерский помощник выключен";
    internal FarmTools(ToolkitMod app)=>_app=app;
    internal void Request(bool collect,bool feed)
    {
        if(!(collect&&!Collect||feed&&!Feed)){SetModes(collect,feed);return;}
        _app.Confirm("Включить фермерского помощника?",
            "В радиусе 30 м будут использоваться только уже установленные нативные сборщики плортов и кормушки загонов. "+
            "Еда перекладывается из выбранного слота вакпака, максимум 4 шт./с, без создания предметов и превышения ёмкости. "+
            "Инвентарь и содержимое устройств сохраняются игрой. При смене мира помощник выключается.",
            ()=>SetModes(collect,feed));
    }
    internal void SetModes(bool collect,bool feed)
    {
        if(!_app.Ready||_app.Map.Busy)throw new InvalidOperationException("Дождитесь загрузки мира и завершения перехода.");
        Collect=collect;Feed=feed;_session=_app.Session;_nextScan=_nextTick=_nextCollect=0;
        if(!Enabled){_collectors.Clear();_feeders.Clear();ScannedPlotCount=0;Status="Фермерский помощник выключен";}
    }
    private bool Available(Behaviour device)
    {
        if(device==null||device.WasCollected||!device.isActiveAndEnabled)return false;
        var plot=device.GetComponentInParent<LandPlot>();
        return plot!=null&&!plot.WasCollected&&plot.GetPlotId()==LandPlot.Id.CORRAL;
    }
    internal void ScanNow()
    {
        _collectors.Clear();_feeders.Clear();ScannedPlotCount=0;
        if(!_app.Ready||_app.Map.Busy)return;
        // The native plot registry is small and authoritative. A global Unity
        // object census stalled the frame even when no ranch devices existed.
        // Never instantiate unloaded plots or retain the registry/model roots.
        var plots=_app.Context.GameModel.landPlots;
        if(plots==null)return;
        foreach(var pair in plots)
        {
            var model=pair.Value;ScannedPlotCount++;
            if(model==null||model.typeId!=LandPlot.Id.CORRAL)continue;
            var root=model.gameObj;
            if(root==null||root.WasCollected||!root.activeInHierarchy)continue;
            var collector=root.GetComponentInChildren<PlortCollector>(true);
            if(_collectors.Count<ExplorerPolicy.MaxFarmDevices&&Available(collector)&&collector._model!=null&&collector._storage!=null)
                _collectors.Add(collector);
            var feeder=root.GetComponentInChildren<SlimeFeeder>(true);
            if(_feeders.Count<ExplorerPolicy.MaxFarmDevices&&Available(feeder)&&feeder._model!=null&&feeder._storage!=null)
                _feeders.Add(feeder);
        }
    }
    private bool Nearby(Behaviour device)=>Available(device)&&
        ExplorerPolicy.Within((device.transform.position-_app.Player.Controller.Motor.TransientPosition).sqrMagnitude,ExplorerPolicy.FarmRadius);
    private static int Total(AmmoSlotManager ammo,IdentifiableType type)
    {
        int total=0;
        for(int i=0;ammo?.Slots!=null&&i<ammo.Slots.Length;i++)
            if(ammo.Slots[i]?.Id==type)total=checked(total+ammo.GetSlotCount(i));
        return total;
    }
    private sealed class Snapshot
    {
        internal AmmoSlot Slot;
        internal AmmoSlot.AmmoMetadata Metadata;
        internal SlimeAppearance.AppearanceSaveSet Appearance;
        internal int Count;
        internal void Restore(){Slot.SetFromMetadata(Metadata);Slot.Appearance=Appearance;Slot.Count=Count;}
    }
    internal bool TransferOne(SlimeFeeder feeder)
    {
        if(!Feed||!_app.Ready||_session!=_app.Session||_app.Map.Busy||!Nearby(feeder)||feeder._storage==null||!feeder._storage.IsActive())return false;
        var source=_app.Context.PlayerState.Ammo;
        int index=source._selectedAmmoIdx;
        if(index<0||index>=source.Slots.Length)return false;
        var slot=source.Slots[index];var type=slot?.Id;
        if(type==null||slot.Count<=0||!slot.IsUnlocked)return false;
        bool food=false;
        foreach(var entry in _app.Actors.Catalog)if(entry.Type==type){food=entry.Category=="Еда";break;}
        if(!food)return false;
        var destination=feeder._storage.GetRelevantAmmo();
        if(destination?.Slots==null||destination.Pointer==source.Pointer||!feeder._storage.CanAnySlotAccept(slot.Metadata))return false;
        var sourceSnapshot=new Snapshot{Slot=slot,Metadata=slot.Metadata,Appearance=slot.Appearance,Count=slot.Count};
        var snapshots=new List<Snapshot>(destination.Slots.Length);
        foreach(var target in destination.Slots)
            if(target!=null)snapshots.Add(new Snapshot{Slot=target,Metadata=target.Metadata,Appearance=target.Appearance,Count=target.Count});
        int before=Total(destination,type);
        try
        {
            var popped=source.PopFromSelectedSlot();
            if(popped?.Id!=type||slot.Count!=sourceSnapshot.Count-1)
                throw new InvalidOperationException("Вакпак не подтвердил снятие одной порции.");
            if(!feeder._storage.MaybeAddToAnySlot(popped))
            {foreach(var snapshot in snapshots)snapshot.Restore();sourceSnapshot.Restore();return false;}
            if(!ExplorerPolicy.Conserved(sourceSnapshot.Count,slot.Count,before,Total(destination,type)))
                throw new InvalidOperationException("Нативная передача не сохранила общее количество еды.");
            TransferredUnits++;return true;
        }
        catch
        {
            foreach(var snapshot in snapshots)snapshot.Restore();
            sourceSnapshot.Restore();throw;
        }
    }
    internal void WorkNow()
    {
        if(!Enabled||_session!=_app.Session||!_app.Ready||_app.Map.Busy||_app.MenuOpen||_app.Map.NativeMapOpen||Time.timeScale<=0)return;
        if(Collect&&Time.unscaledTime>=_nextCollect)
        {
            _nextCollect=Time.unscaledTime+3f;
            foreach(var collector in _collectors)
            {
                if(!Nearby(collector)||collector._storage==null||!collector._storage.IsActive()||
                    collector._joints!=null&&collector._joints.Count>0)continue;
                collector.StartCollection();CollectionRequests++;
            }
        }
        if(Feed)
        {
            int moved=0;
            foreach(var feeder in _feeders)
                while(moved<ExplorerPolicy.FeedUnitsPerTick&&TransferOne(feeder))moved++;
        }
        int collectors=0,feeders=0;
        foreach(var device in _collectors)if(Nearby(device))collectors++;
        foreach(var device in _feeders)if(Nearby(device))feeders++;
        Status="Устройств в радиусе 30 м: сборщики "+collectors+" / кормушки "+feeders+
            " · переложено "+TransferredUnits+" шт. · запусков сбора "+CollectionRequests;
    }
    internal void Tick()
    {
        if(!Enabled)return;
        if(_session!=_app.Session){Reset();return;}
        if(!_app.Ready||_app.Map.Busy)return;
        try
        {
            if(Time.unscaledTime>=_nextScan){_nextScan=Time.unscaledTime+ExplorerPolicy.FarmScanSeconds;ScanNow();}
            if(Time.unscaledTime>=_nextTick){_nextTick=Time.unscaledTime+1f;WorkNow();}
        }
        catch{Reset();throw;}
    }
    internal void Reset()
    {
        Collect=Feed=false;_collectors.Clear();_feeders.Clear();_session=IntPtr.Zero;
        _nextScan=_nextTick=_nextCollect=0;TransferredUnits=CollectionRequests=ScannedPlotCount=0;Status="Фермерский помощник выключен";
    }
}