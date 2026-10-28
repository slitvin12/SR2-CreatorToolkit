using Il2Cpp;
using System;
using Il2CppMonomiPark.SlimeRancher.Player;
using UnityEngine;
namespace CreatorToolkit;
internal sealed partial class PlayerTools
{
    private bool _checkpointValid;
    private IntPtr _checkpointSession,_checkpointGroup;
    private Vector3 _checkpointPosition;
    internal bool HasCheckpoint=>_checkpointValid&&_checkpointSession==_app.Session;
    internal string CheckpointStatus {get;private set;}="Точка возврата не задана · хранится только до смены сейва";
    internal void RefillVitals()
    {
        if(!_app.Ready||_app.Map.Busy)throw new InvalidOperationException("Дождитесь загрузки мира.");
        var state=_app.Context.PlayerState;
        state.SetHealth(state.GetMaxHealth());state.SetEnergy(state.GetMaxEnergy());
        _app.Notify("Здоровье и энергия восстановлены.");
    }
    internal void RememberCheckpoint()
    {
        if(!_app.Ready||_app.Map.Busy||_motor==null||Flight)throw new InvalidOperationException("Для точки возврата встаньте на землю и выключите полёт.");
        var player=_app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true);
        if(player?.SceneGroup==null||!TryMapGround(_motor.TransientPosition,true,out var ground)||!IsClearPosition(ground))
            throw new InvalidOperationException("Безопасная поверхность точки не подтверждена.");
        _checkpointPosition=ground;_checkpointSession=_app.Session;_checkpointGroup=player.SceneGroup.Pointer;_checkpointValid=true;
        CheckpointStatus="Точка возврата · "+ground.ToString("F1")+" · текущий остров";_app.Notify(CheckpointStatus);
    }
    internal void RecallCheckpoint()
    {
        if(!_app.Ready||_app.Map.Busy||!HasCheckpoint)throw new InvalidOperationException("Нет точки возврата текущей сессии.");
        var player=_app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true);
        if(player?.SceneGroup==null||player.SceneGroup.Pointer!=_checkpointGroup)
            throw new InvalidOperationException("Точка на другом острове. Используйте безопасный переход через карту.");
        if(!TryMapGround(_checkpointPosition,true,out var ground)||!IsClearPosition(ground))
            throw new InvalidOperationException("Место точки занято или земля больше не подтверждена.");
        PlaceForMap(ground);_app.Notify("Возврат к временной точке.");
    }
    private void ClearCheckpoint()
    {
        _checkpointValid=false;_checkpointSession=_checkpointGroup=IntPtr.Zero;_checkpointPosition=default;
        CheckpointStatus="Точка возврата не задана · хранится только до смены сейва";
    }
}
