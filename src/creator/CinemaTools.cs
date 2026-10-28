using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppMonomiPark.ScriptedValue;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using UnityEngine;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;

namespace CreatorToolkit;

internal sealed class CinemaTools
{
    internal sealed class Waypoint
    {
        internal string Name;
        internal Vector3 Position;
        internal SceneGroup Group;
        internal IntPtr Session;
    }
    private readonly ToolkitMod _app;
    internal bool HideHud { get; private set; }
    internal readonly List<Waypoint> Waypoints = new();
    private ScriptedBool _hud, _vac;
    private bool _oldHud, _oldVac;
    internal CinemaTools(ToolkitMod app) => _app = app;

    internal void SetHideHud(bool hidden)
    {
        if (hidden == HideHud) return;
        if (hidden)
        {
            _hud = _app.Context.HudVisible;
            _vac = _app.Context.VacVisible;
            if (_hud == null) throw new InvalidOperationException("Нативная настройка HUD недоступна.");
            _oldHud = _hud.Value;
            _oldVac = _vac != null && _vac.Value;
            HideHud = true;
            Tick();
        }
        else RestoreHud();
        _app.Notify(hidden ? "HUD и модель вакпака скрыты. Меню Creator Toolkit остаётся доступно." : "Нативный HUD восстановлен.");
    }

    internal void Tick()
    {
        if (!HideHud) return;
        if (_hud != null && _hud.Value) _hud.Value = false;
        if (_vac != null && _vac.Value) _vac.Value = false;
    }

    private void RestoreHud()
    {
        HideHud = false;
        try
        {
            try { if (_hud != null && !_hud.WasCollected) _hud.Value = _oldHud; } finally { _hud = null; }
        }
        finally
        {
            try { if (_vac != null && !_vac.WasCollected) _vac.Value = _oldVac; } finally { _vac = null; }
        }
    }

    internal void SaveWaypoint(string name)
    {
        if (Waypoints.Count >= SafetyPolicy.MaxWaypoints) throw new InvalidOperationException("Лимит — 5 точек. Удалите ненужную точку.");
        var player = _app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true);
        if (player == null || player.SceneGroup == null) throw new InvalidOperationException("Группа сцены игрока недоступна.");
        var motor = _app.Context.Player.GetComponentInChildren<SRCharacterController>(true)?.Motor;
        if (motor == null) throw new InvalidOperationException("Мотор игрока ещё не готов.");
        Vector3 position = motor.TransientPosition;
        if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))
            throw new InvalidOperationException("Невалидные координаты точки.");
        Waypoints.Add(new Waypoint { Name = string.IsNullOrWhiteSpace(name) ? "Точка " + (Waypoints.Count + 1) : name.Trim(), Position = position, Group = player.SceneGroup, Session = _app.Session });
        _app.Notify("Сохранена точка «" + Waypoints[Waypoints.Count - 1].Name + "».");
    }

    internal void Teleport(Waypoint point)
    {
        if (point == null || !SafetyPolicy.SameSession(point.Session, _app.Session)) throw new InvalidOperationException("Точка относится к другому сейву.");
        if (!float.IsFinite(point.Position.x) || !float.IsFinite(point.Position.y) || !float.IsFinite(point.Position.z))
            throw new InvalidOperationException("Невалидные координаты точки; телепортация отменена.");
        var player = _app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true);
        if (player == null || point.Group == null) throw new InvalidOperationException("Телепортация недоступна.");
        // Even a same-island point may belong to a sector that has streamed
        // out. All buttons, saved points and SOS share one collision gate.
        _app.Map.RequestTeleport(new MapTools.Point{Id="waypoint",Name=point.Name,Group=point.Group,GroupName=point.Group.name,Position=point.Position,ExactHeight=true,AllowNearbyLanding=true});
        _app.Notify("Подготовка точки «"+point.Name+"» с проверкой безопасной посадки.");
    }

    internal void Reset() => RestoreHud();
    internal void ResetWaypoints() => Waypoints.Clear();
}
