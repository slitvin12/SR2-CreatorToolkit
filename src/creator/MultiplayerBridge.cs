using System;
using System.Reflection;
using UnityEngine;
namespace CreatorToolkit;
// Live link to Ranching Together (SR2MP) through reflection only: Creator Toolkit has no
// reference to SR2MP/Starlight, works the same without them, and friends do not need CT.
//  • role: SR2MP.Main.Server.IsRunning (host) / SR2MP.Main.Client.IsConnected (guest);
//  • server rules: SR2MP.GlobalVariables.CheatsEnabled (the host's "Allow Cheats");
//  • spawn sync: SR2MP.GlobalVariables.ActorManager.RegisterSpawnOverNetwork(GameObject) — CT creates
//    actors through GameModel.InstantiateActorModel, which SR2MP does not intercept on its own.
// Polled twice a second through cached delegates (no per-frame reflection or boxing).
internal sealed class MultiplayerBridge
{
    private readonly ToolkitMod _app;
    private Func<bool> _isHost, _isClient, _cheats;
    private Action<GameObject> _announce;
    private float _nextProbe, _nextPoll;
    private bool _bound, _announceFailed;

    internal CoopRole Role { get; private set; } = CoopRole.Offline;
    internal bool ServerCheats { get; private set; }
    internal bool Installed { get; private set; }
    internal bool InSession => Role != CoopRole.Offline;
    internal string Footer { get; private set; } = "";
    internal bool CanSyncSpawns => _announce != null;

    internal MultiplayerBridge(ToolkitMod app) => _app = app;

    /// Every frame, before the Ready gate: refresh the session role (cheap until the poll time).
    internal void Poll()
    {
        float now = Time.unscaledTime;
        if (!_bound && now >= _nextProbe) { _nextProbe = now + 10f; try { Probe(); } catch (Exception e) { _bound = false; _app.Fail("coop probe", e); } }
        if (!_bound || now < _nextPoll) return;
        _nextPoll = now + 0.5f;
        CoopRole role; bool cheats;
        try
        {
            role = _isHost() ? CoopRole.Host : _isClient() ? CoopRole.Client : CoopRole.Offline;
            cheats = role != CoopRole.Offline && _cheats();
        }
        catch (Exception e) { _bound = false; _app.Fail("coop state", e); return; }
        if (role == Role && cheats == ServerCheats) return;
        var previous = Role;
        Role = role; ServerCheats = cheats;
        Footer = MultiplayerPolicy.Footer(role, cheats);
        if (role == CoopRole.Offline)
            _app.Notify("Кооп завершён — все функции Creator Toolkit снова доступны.");
        else
            _app.Notify("Кооп (" + MultiplayerPolicy.ModName + "): вы " + (role == CoopRole.Host ? "хост" : "гость") +
                (cheats ? ", читы сервера разрешены." : ", читы сервера выключены — читы CT тоже.") +
                (previous == CoopRole.Offline ? " Заморозка времени/мобов и ускорение недоступны." : ""));
    }

    private void Probe()
    {
        Assembly asm = null;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            string n = a.FullName;
            if (n != null && n.StartsWith("SR2MP,", StringComparison.Ordinal)) { asm = a; break; }
        }
        if (asm == null) return;
        Installed = true;
        const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var main = asm.GetType("SR2MP.Main");
        var globals = asm.GetType("SR2MP.GlobalVariables");
        var cheats = globals?.GetProperty("CheatsEnabled", S)?.GetGetMethod(true);
        if (_cheats == null && cheats != null) _cheats = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), cheats);
        _isHost ??= Bind(main, "Server", "IsRunning");
        _isClient ??= Bind(main, "Client", "IsConnected");
        if (_announce == null)
        {
            try // Optional: without it co-op rules still apply, only CT spawns stay local.
            {
                var manager = globals?.GetField("ActorManager", S)?.GetValue(null);
                var register = manager?.GetType().GetMethod("RegisterSpawnOverNetwork", I, null, new[] { typeof(GameObject) }, null);
                if (register != null && register.ReturnType == typeof(void))
                    _announce = (Action<GameObject>)Delegate.CreateDelegate(typeof(Action<GameObject>), manager, register);
            }
            catch (Exception e) { _announce = null; _app.LoggerInstance.Warning("Ranching Together spawn sync unavailable: " + e.Message); }
        }
        _bound = _isHost != null && _isClient != null && _cheats != null;
        if (_bound)
            _app.LoggerInstance.Msg("Ranching Together detected: co-op rules active" + (_announce != null ? ", spawn sync available." : ", spawn sync unavailable (API changed)."));
    }

    private static Func<bool> Bind(Type main, string holder, string flag)
    {
        const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var instance = main?.GetProperty(holder, S)?.GetValue(null);
        var getter = instance?.GetType().GetProperty(flag, I)?.GetGetMethod(true);
        return getter == null || getter.ReturnType != typeof(bool) ? null : (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), instance, getter);
    }

    /// null — the action may run; otherwise the reason (already worded for the status line).
    internal string Deny(string label)
    {
        if (Role == CoopRole.Offline) return null;
        var kind = MultiplayerPolicy.Classify(label);
        if (kind == CoopKind.Free || TurningOff(label)) return null;
        return MultiplayerPolicy.Deny(kind, Role, ServerCheats);
    }

    // Toggle labels: switching a running effect OFF is always allowed.
    private bool TurningOff(string label) => label switch
    {
        "flight" => _app.Player?.Flight == true,
        "freeze time" => _app.World?.FreezeTime == true,
        "freeze mobs" => _app.World?.FreezeMobs == true,
        "tarr" => _app.World?.DisableTarr == true,
        "infinite builder" => _app.Gadgets?.InfiniteBuilder == true,
        "auto refill request" => _app.Convenience?.Refilling == true,
        "cinematic time rate" or "m26 acceptance time rate" => _app.CinematicRate?.Active == true,
        _ => false,
    };

    /// Ready tick, first in the list: effects that the session does not allow are switched off.
    internal void Enforce()
    {
        if (Role == CoopRole.Offline) return;
        bool off = false;
        var world = _app.World;
        if (world != null)
        {
            if (world.FreezeTime) { world.SetFreezeTime(false); off = true; }
            if (world.FreezeMobs) { world.SetFreezeMobs(false); off = true; }
            if (world.DisableTarr && !MultiplayerPolicy.Allows(CoopKind.HostWorld, Role, ServerCheats)) { world.SetDisableTarr(false); off = true; }
        }
        if (_app.CinematicRate?.Active == true) { _app.CinematicRate.Reset(); off = true; }
        if (!MultiplayerPolicy.PlayerCheats(Role, ServerCheats))
        {
            var p = _app.Player;
            if (p != null)
            {
                if (p.GodMode || p.InfiniteEnergy || p.InfiniteItems || p.HighJump || p.SpeedMultiplier > 1.001f)
                { p.GodMode = p.InfiniteEnergy = p.InfiniteItems = p.HighJump = false; p.SpeedMultiplier = 1f; off = true; }
                if (p.Flight) { p.SetFlight(false); off = true; }
            }
            if (_app.Gadgets?.InfiniteBuilder == true) { _app.Gadgets.SetInfiniteBuilder(false); off = true; }
            if (_app.AutoFeed?.Enabled == true) { _app.AutoFeed.Set(false); off = true; }
            if (_app.Farm?.Enabled == true && _app.Map?.Busy != true) { _app.Farm.SetModes(false, false); off = true; }
            if (_app.Convenience?.Refilling == true) { _app.Convenience.StopRefill("Кооп: автопополнение выключено"); off = true; }
        }
        if (off)
            _app.Notify(MultiplayerPolicy.Deny(CoopKind.Cheat, Role, ServerCheats) ??
                "Кооп (" + MultiplayerPolicy.ModName + "): заморозка времени, мобов и ускорение симуляции выключены — они не синхронизируются.");
    }

    /// After CT created an actor: let SR2MP replicate it (owner = this player). Offline — no-op.
    internal void AnnounceSpawn(GameObject go)
    {
        if (Role == CoopRole.Offline || _announce == null || go == null || go.WasCollected) return;
        try { _announce(go); }
        catch (Exception e)
        {
            if (!_announceFailed) { _announceFailed = true; _app.Fail("coop spawn sync", e); }
        }
    }
}
