using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using LycoreiaSdk;
using UnityEngine;
namespace CreatorToolkit;
internal readonly struct CoopPeer
{
    internal readonly string Id, Name;
    internal readonly GameObject Root;
    internal readonly Transform Transform;
    internal readonly Color Color;
    internal CoopPeer(string id, string name, GameObject root, Transform transform, Color color)
    { Id = id; Name = name; Root = root; Transform = transform; Color = color; }
}
// Live link to Ranching Together (SR2MP) and Lycoreia without any compile-time reference:
//  • Lycoreia 2.x (SDK v1): bound via LycoreiaSdk.LycoreiaClient (one reflection lookup for
//    Lycoreia.Api.LycoreiaApi, stable across Lycoreia updates).
//  • Legacy pre-SDK Lycoreia 2.x & Ranching Together (SR2MP): reflection fallback.
// Polled twice a second through cached delegates / SDK events (no per-frame reflection).
internal sealed class MultiplayerBridge
{
    private readonly ToolkitMod _app;
    private Func<bool> _isHost, _isClient, _cheats, _chatOpen, _uiOpen, _streamerMode;
    private Func<string> _hostId;
    private Action<GameObject> _announce;
    private FieldInfo _vacExpelDepth;
    private IDictionary _remoteAvatars;
    private FieldInfo _avatarId, _avatarName, _avatarRoot, _avatarTransform, _avatarColor;
    private readonly List<CoopPeer> _peers = new();
    private float _nextProbe, _nextPoll, _nextPlayerCheatLog;
    private bool _bound, _useLycoreiaSdk, _sdkEventsSubscribed, _sdkDirty, _announceFailed, _lycoreiaWarned, _lycoreiaCheatsFallbackWarned;

    internal CoopRole Role { get; private set; } = CoopRole.Offline;
    internal bool ServerCheats { get; private set; }
    internal bool Installed { get; private set; }
    internal bool InSession => Role != CoopRole.Offline;
    internal string Footer { get; private set; } = "";
    internal bool CanSyncSpawns => _useLycoreiaSdk || _announce != null;
    internal bool ChatOpen { get { if (_useLycoreiaSdk) return LycoreiaClient.TypingOpen; if (!_bound || _chatOpen == null) return false; try { return _chatOpen(); } catch { return false; } } }
    internal bool UiOpen { get { if (_useLycoreiaSdk) return LycoreiaClient.OverlayOpen; if (!_bound || _uiOpen == null) return false; try { return _uiOpen(); } catch { return false; } } }
    internal string CoopSaveKey { get; private set; } = "";
    internal IReadOnlyList<CoopPeer> Peers => _peers;

    internal MultiplayerBridge(ToolkitMod app)
    {
        _app = app;
        try { LycoreiaClient.Register(_app.Info?.Name ?? Assembly.GetExecutingAssembly().GetName().Name, ToolkitMod.Version); } catch { }
    }

    /// Every frame, before the Ready gate: refresh the session role (cheap until the poll time).
    internal void Poll()
    {
        float now = Time.unscaledTime;
        if (!_bound && now >= _nextProbe) { _nextProbe = now + 10f; try { Probe(); } catch (Exception e) { _bound = false; _app.Fail("coop probe", e); } }
        if (_bound && _app.MenuOpen && UiOpen) _app.CloseMenu();
        if (!_bound || (!_sdkDirty && now < _nextPoll)) return;
        _sdkDirty = false;
        _nextPoll = now + 0.5f;
        CoopRole role; bool cheats;
        if (_useLycoreiaSdk)
        {
            role = LycoreiaClient.Role switch
            {
                LycoreiaRole.Host => CoopRole.Host,
                LycoreiaRole.Guest => CoopRole.Client,
                _ => CoopRole.Offline,
            };
            cheats = role != CoopRole.Offline && LycoreiaClient.CheatsAllowed;
        }
        else
        {
            try
            {
                role = _isHost() ? CoopRole.Host : _isClient() ? CoopRole.Client : CoopRole.Offline;
                cheats = role != CoopRole.Offline && _cheats();
            }
            catch (Exception e) { _bound = false; _peers.Clear(); CoopSaveKey = ""; _app.Fail("coop state", e); return; }
        }
        RefreshCoopContext(role);
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

    private void RefreshCoopContext(CoopRole role)
    {
        if (_useLycoreiaSdk)
        {
            try
            {
                CoopSaveKey = role == CoopRole.Client ? BuildLycoreiaSaveKey(LycoreiaClient.SessionKey) : "";
            }
            catch { CoopSaveKey = role == CoopRole.Client ? "coop_guest" : ""; }

            _peers.Clear();
            if (role == CoopRole.Offline) return;
            try
            {
                var players = LycoreiaClient.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (p == null || p.IsLocal) continue;
                    var tr = p.Transform;
                    if (tr == null || tr.WasCollected) continue;
                    var root = tr.gameObject;
                    if (root == null || root.WasCollected) continue;
                    _peers.Add(new CoopPeer(p.Id ?? "", p.Name ?? "", root, tr, p.Color));
                }
            }
            catch { }
            return;
        }

        try
        {
            if (role == CoopRole.Client)
            {
                string raw = _hostId?.Invoke();
                CoopSaveKey = BuildCoopSaveKey(raw);
            }
            else CoopSaveKey = "";
        }
        catch { CoopSaveKey = role == CoopRole.Client ? "coop_guest" : ""; }

        _peers.Clear();
        if (role == CoopRole.Offline || _remoteAvatars == null || _avatarRoot == null || _avatarTransform == null) return;
        bool hideNames = false;
        try { hideNames = _streamerMode != null && _streamerMode(); } catch { }
        try
        {
            foreach (DictionaryEntry entry in _remoteAvatars)
            {
                object av = entry.Value;
                if (av == null) continue;
                var root = _avatarRoot.GetValue(av) as GameObject;
                var tr = _avatarTransform.GetValue(av) as Transform;
                if (root == null || root.WasCollected || tr == null || tr.WasCollected) continue;
                string id = _avatarId?.GetValue(av) as string ?? entry.Key as string ?? "";
                string name = hideNames ? "Player" : (_avatarName?.GetValue(av) as string ?? "");
                Color color = _avatarColor?.GetValue(av) is Color c ? c : Color.cyan;
                _peers.Add(new CoopPeer(id, name, root, tr, color));
            }
        }
        catch { }
    }

    private static string BuildLycoreiaSaveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "coop_guest";
        if (WaypointStore.SaveKey(key)) return key;
        if (key.Length > 80 && WaypointStore.SaveKey(key.Substring(0, 80))) return key.Substring(0, 80);
        return BuildCoopSaveKey(key);
    }

    private static string BuildCoopSaveKey(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "coop_guest";
        var chars = new char[Math.Min(raw.Length, 48)];
        int n = 0;
        for (int i = 0; i < raw.Length && n < chars.Length; i++)
        {
            char c = raw[i];
            chars[n++] = (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-') ? c : '_';
        }
        string candidate = n > 0 ? "coop_" + new string(chars, 0, n) : "coop_guest";
        return WaypointStore.SaveKey(candidate) ? candidate : "coop_guest";
    }

    private void Probe()
    {
        if (LycoreiaClient.Available) { BindLycoreiaSdk(); return; }
        Assembly asm = null, lycoreia = null;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            string n = a.FullName;
            if (n == null) continue;
            if (n.StartsWith("SR2MP,", StringComparison.Ordinal)) asm = a;
            else if (n.StartsWith("Lycoreia,", StringComparison.Ordinal)) lycoreia = a;
        }
        if (lycoreia != null)
        {
            if (lycoreia.GetType("Lycoreia.Api.LycoreiaApi") != null)
            {
                if (LycoreiaClient.Available) { BindLycoreiaSdk(); return; }
                _nextProbe = Time.unscaledTime + 1f;
                return;
            }
            ProbeLycoreiaLegacy(lycoreia);
            return;
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
        _chatOpen ??= StaticFlagOrField(asm.GetType("SR2MP.Components.UI.ChatHud"), "IsOpen", "open");
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

    private void BindLycoreiaSdk()
    {
        Installed = true;
        _useLycoreiaSdk = true;
        _bound = true;
        MultiplayerPolicy.ModName = "Lycoreia";
        if (!_sdkEventsSubscribed)
        {
            _sdkEventsSubscribed = true;
            LycoreiaClient.RoleChanged += _ => _sdkDirty = true;
            LycoreiaClient.CheatsAllowedChanged += allowed =>
            {
                _sdkDirty = true;
                if (!allowed && _app.Ready) { ServerCheats = false; Enforce(); }
            };
        }
        _app.LoggerInstance.Msg("Lycoreia " + LycoreiaClient.ModVersion + " detected via SDK (API v" + LycoreiaClient.ApiVersion + "): co-op rules active, spawn sync available.");
    }

    private void ProbeLycoreiaLegacy(Assembly asm)
    {
        Installed = true;
        MultiplayerPolicy.ModName = "Lycoreia";
        const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        _isHost ??= StaticFlagOrField(asm.GetType("Lycoreia.Compat.ModFacade+Server"), "IsRunning", null);
        _isClient ??= StaticFlagOrField(asm.GetType("Lycoreia.Compat.ModFacade+Client"), "IsConnected", null);
        var sessionProp = asm.GetType("Lycoreia.Mod")?.GetProperty("Session", S);
        var sessionField = sessionProp == null ? asm.GetType("Lycoreia.Mod")?.GetField("Session", S) : null;
        var sessionType = sessionProp?.PropertyType ?? sessionField?.FieldType;
        object GetSession() => sessionProp != null ? sessionProp.GetValue(null) : sessionField?.GetValue(null);

        if (_cheats == null)
        {
            var hostRule = BoolReader(sessionType, "AllowCheats");
            var guestRule = BoolReader(sessionType, "HostAllowsCheats");
            var isHost = _isHost;
            if ((sessionProp != null || sessionField != null) && isHost != null && hostRule != null && guestRule != null)
                _cheats = () =>
                {
                    // Host: the rule it sends to guests; guest: the rule the host approved it with.
                    object live = GetSession();
                    if (live == null) return false;
                    return isHost() ? hostRule(live) : guestRule(live);
                };
            else if (_isHost != null && _isClient != null)
            {
                _cheats = () => false;
                if (!_lycoreiaCheatsFallbackWarned)
                {
                    _lycoreiaCheatsFallbackWarned = true;
                    _app.LoggerInstance.Warning("Lycoreia session cheat rule not found; defaulting co-op cheats to OFF.");
                }
            }
        }
        if (_hostId == null && (sessionProp != null || sessionField != null) && sessionType != null)
        {
            var hostIdReader = StringReader(sessionType, "HostId");
            if (hostIdReader != null) _hostId = () => { object live = GetSession(); return live == null ? null : hostIdReader(live); };
        }
        if (_chatOpen == null)
        {
            var chatFlag = StaticFlagOrField(asm.GetType("Lycoreia.Owned.Components.UI.ChatHud"), "IsOpen", "open")
                ?? StaticFlagOrField(asm.GetType("SR2MP.Components.UI.ChatHud"), "IsOpen", "open");
            var gateFlag = StaticFlagOrField(asm.GetType("Lycoreia.Owned.Components.UI.InputGate"), "Blocked", "blocked");
            if (chatFlag != null || gateFlag != null)
                _chatOpen = () => (chatFlag != null && chatFlag()) || (gateFlag != null && gateFlag());
        }
        if (_uiOpen == null)
        {
            var lycMenu = StaticFlagOrField(asm.GetType("Lycoreia.Owned.Components.UI.LycoreiaMenu"), "IsOpen", "_isOpen");
            var coopWheel = StaticFlagOrField(asm.GetType("Lycoreia.Owned.Components.UI.CoopWheel"), "Busy", "open");
            var whatsNew = StaticFlagOrField(asm.GetType("Lycoreia.Owned.Components.UI.WhatsNewPopup"), "IsOpen", null);
            if (lycMenu != null || coopWheel != null || whatsNew != null)
                _uiOpen = () => (lycMenu != null && lycMenu()) || (coopWheel != null && coopWheel()) || (whatsNew != null && whatsNew());
        }
        _streamerMode ??= StaticFlagOrField(asm.GetType("Lycoreia.Compat.ModFacade"), "StreamerMode", null)
            ?? StaticFlagOrField(asm.GetType("Lycoreia.Game.Prefs"), "StreamerMode", null);
        if (_vacExpelDepth == null)
        {
            var depthFld = asm.GetType("Lycoreia.Game.Hooks.VacuumExpelHook")?.GetField("Depth", S);
            if (depthFld?.FieldType == typeof(int)) _vacExpelDepth = depthFld;
        }
        if (_remoteAvatars == null)
        {
            try
            {
                _remoteAvatars = asm.GetType("Lycoreia.Game.Avatars.RemoteAvatars")?.GetField("All", S)?.GetValue(null) as IDictionary;
                var avType = asm.GetType("Lycoreia.Game.Avatars.RemoteAvatar");
                if (avType != null)
                {
                    _avatarId = avType.GetField("PlayerId", I);
                    _avatarName = avType.GetField("Name", I);
                    _avatarRoot = avType.GetField("Root", I);
                    _avatarTransform = avType.GetField("transform", I);
                    _avatarColor = avType.GetField("Colour", I);
                }
            }
            catch { _remoteAvatars = null; }
        }
        if (_announce == null)
        {
            try // Optional: Lycoreia publishes spawns from its InstantiateActor postfix; CT spawns bypass it.
            {
                var postfix = asm.GetType("Lycoreia.Game.Hooks.InstantiateActorHook")?.GetMethod("Postfix", S, null, new[] { typeof(GameObject) }, null);
                if (postfix != null && postfix.ReturnType == typeof(void))
                    _announce = (Action<GameObject>)Delegate.CreateDelegate(typeof(Action<GameObject>), postfix);
            }
            catch (Exception e) { _announce = null; _app.LoggerInstance.Warning("Lycoreia spawn sync unavailable: " + e.Message); }
        }
        _bound = _isHost != null && _isClient != null && _cheats != null;
        if (_bound)
            _app.LoggerInstance.Msg("Lycoreia " + asm.GetName().Version + " detected: co-op rules active" + (_announce != null ? ", spawn sync available." : ", spawn sync unavailable (API changed)."));
        else if (!_lycoreiaWarned)
        {
            _lycoreiaWarned = true;
            _app.LoggerInstance.Warning("Lycoreia " + asm.GetName().Version + " found, but its session API was not recognised: co-op rules inactive.");
        }
    }

    private static Func<object, bool> BoolReader(Type holder, string name)
    {
        if (holder == null) return null;
        const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var p = holder.GetProperty(name, I);
        if (p?.PropertyType == typeof(bool)) return obj => p.GetValue(obj) is true;
        var f = holder.GetField(name, I);
        if (f?.FieldType == typeof(bool)) return obj => f.GetValue(obj) is true;
        return null;
    }

    private static Func<object, string> StringReader(Type holder, string name)
    {
        if (holder == null) return null;
        const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var p = holder.GetProperty(name, I);
        if (p?.PropertyType == typeof(string)) return obj => p.GetValue(obj) as string;
        var f = holder.GetField(name, I);
        if (f?.FieldType == typeof(string)) return obj => f.GetValue(obj) as string;
        return null;
    }

    private static Func<bool> StaticFlagOrField(Type holder, string propName, string fieldName)
    {
        if (holder == null) return null;
        const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var getter = holder.GetProperty(propName, S)?.GetGetMethod(true);
        if (getter != null && getter.ReturnType == typeof(bool))
            return (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
        if (fieldName != null)
        {
            var f = holder.GetField(fieldName, S);
            if (f?.FieldType == typeof(bool)) return () => f.GetValue(null) is true;
        }
        return null;
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
        string denied = MultiplayerPolicy.Deny(kind, Role, ServerCheats);
        if (denied != null && _useLycoreiaSdk && kind != CoopKind.SoloOnly && !(kind == CoopKind.HostWorld && Role == CoopRole.Client))
            return LycoreiaClient.CheatsDeniedReason ?? denied;
        return denied;
    }

    internal IDisposable BeginAction(string label, out string denied)
    {
        denied = Deny(label);
        if (denied != null) return null;
        if (!_useLycoreiaSdk || Role == CoopRole.Offline) return null;
        var kind = MultiplayerPolicy.Classify(label);
        if (kind == CoopKind.Free || TurningOff(label)) return null;
        var scope = LycoreiaClient.BeginCheat(CheatKind(label));
        if (!scope.Allowed)
        {
            denied = scope.Reason ?? MultiplayerPolicy.Deny(kind, Role, false);
            scope.Dispose();
            return null;
        }
        return scope;
    }

    internal bool AllowPlayerCheat(string kind = "player")
    {
        if (Role == CoopRole.Offline) return true;
        if (_useLycoreiaSdk)
        {
            if (LycoreiaClient.CheatsAllowed && Time.unscaledTime < _nextPlayerCheatLog) return true;
            using var scope = LycoreiaClient.BeginCheat(kind);
            if (scope.Allowed) { _nextPlayerCheatLog = Time.unscaledTime + 2f; return true; }
            _app.Notify(scope.Reason ?? MultiplayerPolicy.Deny(CoopKind.Cheat, Role, false));
            return false;
        }
        if (MultiplayerPolicy.PlayerCheats(Role, ServerCheats)) return true;
        _app.Notify(MultiplayerPolicy.Deny(CoopKind.Cheat, Role, ServerCheats));
        return false;
    }

    private static string CheatKind(string label) => label switch
    {
        "spawn" => "spawn",
        "currency max" or "currency add" or "currency set" or "currency 100000" or "currency 10000" or "Уменьшить нью-баксы?" or "Обнулить нью-баксы?" => "currency",
        "teleport waypoint" or "map click teleport" or "custom map teleport" or "safe custom teleport" or "tracking teleport"
            or "m27 acceptance tracking teleport" or "teleport atlas marker" or "recall checkpoint"
            or "safe native phenomenon travel" or "atlas return" or "return from atlas" => "teleport",
        "weather" or "clear weather" or "native weather" or "Дождаться твёрдой фазы Aurora?" => "weather",
        "set hour" or "set time" or "time +1" or "time +6" => "time",
        "Открыть дверь?" or "Открыть весь атлас?" or "Открыть всю Слаймопедию" or "Открыть карту и ноды"
            or "Применить все улучшения" or "Открыть все двери" or "Открыть все стили слаймов"
            or "Открыть все чертежи гаджетов" or "Открыть все стили вакпака" => "unlock",
        "refill vitals" or "auto refill request" or "Включить автопополнение?" or "fill ammo"
            or "refinery fill request" or "refinery all +100 request" or "refinery selected fill request"
            or "refinery selected +100 request" or "m26 acceptance refinery request"
            or "Включить автокормление загонов?" or "Включить фермерского помощника?"
            or "Накормить голодных слаймов вокруг?" or "Заполнить Преобразователь?"
            or "Добавить ресурсы Преобразователя?" => "vitals",
        "infinite builder" or "free placement" or "Заменить содержимое слота?" => "builder",
        "flight" or "hotkey god mode" or "hotkey infinite energy" or "explorer profile" or "tarr" => "player",
        _ when label != null && label.StartsWith("Разместить «", StringComparison.Ordinal) => "builder",
        _ => label,
    };

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
        "cinematic time pin" => _app.CinematicRate?.Pinned == true,
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
                if (p.GodMode || p.InfiniteEnergy || p.InfiniteItems || p.HighJump || p.SpeedMultiplier > 1.001f || p.JumpHeightMultiplier > 1.001f)
                { p.GodMode = p.InfiniteEnergy = p.InfiniteItems = p.HighJump = false; p.SpeedMultiplier = 1f; p.JumpHeightMultiplier = 1f; off = true; }
                if (p.Flight) { p.SetFlight(false); off = true; }
            }
            if (_app.Gadgets?.InfiniteBuilder == true) { _app.Gadgets.SetInfiniteBuilder(false); off = true; }
            if (_app.FreePlacement?.Enabled == true) { _app.FreePlacement.Set(false); off = true; }
            if (_app.Convenience?.Refilling == true) { _app.Convenience.StopRefill("Кооп: автопополнение выключено"); off = true; }
        }
        if (!MultiplayerPolicy.Allows(CoopKind.HostWorld, Role, ServerCheats))
        {
            if (_app.AutoFeed?.Enabled == true) { _app.AutoFeed.Set(false); off = true; }
            if (_app.Farm?.Enabled == true && _app.Map?.Busy != true) { _app.Farm.SetModes(false, false); off = true; }
        }
        if (off)
            _app.Notify(MultiplayerPolicy.Deny(CoopKind.Cheat, Role, ServerCheats) ??
                "Кооп (" + MultiplayerPolicy.ModName + "): заморозка времени, мобов и ускорение симуляции выключены — они не синхронизируются.");
    }

    /// After CT created an actor: let Lycoreia / SR2MP replicate it (owner = this player). Offline — no-op.
    internal void AnnounceSpawn(GameObject go)
    {
        if (Role == CoopRole.Offline || go == null || go.WasCollected) return;
        if (_useLycoreiaSdk)
        {
            if (!LycoreiaClient.PublishSpawn(go, out string reason) && !string.IsNullOrEmpty(reason) && !_announceFailed)
            {
                _announceFailed = true;
                _app.LoggerInstance.Warning("Lycoreia spawn sync refused: " + reason);
            }
            return;
        }
        if (_announce == null) return;
        bool bumped = false;
        try
        {
            if (_vacExpelDepth != null)
            {
                int depth = (int)_vacExpelDepth.GetValue(null);
                _vacExpelDepth.SetValue(null, depth + 1);
                bumped = true;
            }
            _announce(go);
        }
        catch (Exception e)
        {
            if (!_announceFailed) { _announceFailed = true; _app.Fail("coop spawn sync", e); }
        }
        finally
        {
            if (bumped)
            {
                try { int depth = (int)_vacExpelDepth.GetValue(null); _vacExpelDepth.SetValue(null, Math.Max(0, depth - 1)); } catch { }
            }
        }
    }
}
