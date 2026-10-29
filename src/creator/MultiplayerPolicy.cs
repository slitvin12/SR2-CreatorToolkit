using System;
using System.Collections.Generic;
namespace CreatorToolkit;
// Co-op rules for Ranching Together (SR2MP, a Starlight expansion). Pure policy: no Unity,
// no SR2MP reference. MultiplayerBridge reads the live session through reflection.
// Server rules mirror SR2MP itself: its ConsoleCheatPatch blocks cheat commands for the host
// and every guest while the server's "allow_cheats" is off (GlobalVariables.CheatsEnabled).
internal enum CoopRole { Offline, Host, Client }

internal enum CoopKind
{
    Free,       // UI, HUD, atlas, markers, graphics, read-only inspectors: always allowed
    Cheat,      // spawning, money, vitals, teleports, unlocks: needs the server's allow_cheats
    HostWorld,  // weather and clock: the host owns them (SR2MP sends them only host → guests)
    SoloOnly,   // time freeze, mob freeze, simulation rate: cannot be synchronised at all
}

internal static class MultiplayerPolicy
{
    internal const string ModName = "Ranching Together";

    // Exact Queue labels / Confirm titles. Every literal label in the sources must be listed
    // here (offline regression scans the sources), so a new action can not slip through unclassified.
    private static readonly HashSet<string> SoloOnly = new(StringComparer.Ordinal)
    {
        "freeze time", "freeze mobs", "cinematic time rate", "m26 acceptance time rate", "Ускорить симуляцию в 5 раз?",
    };
    private static readonly HashSet<string> HostWorld = new(StringComparer.Ordinal)
    {
        "weather", "clear weather", "native weather", "set hour", "set time", "time +1", "time +6", "tarr",
        "Дождаться твёрдой фазы Aurora?",
    };
    private static readonly HashSet<string> Cheat = new(StringComparer.Ordinal)
    {
        // creation, inventory, money, vitals
        "spawn", "fill ammo", "currency max", "currency 100000", "currency 10000", "refill vitals",
        "auto refill request", "infinite builder", "free placement", "flight",
        "refinery fill request", "refinery all +100 request", "refinery selected fill request", "refinery selected +100 request",
        "m26 acceptance refinery request",
        // movement (SR2MP: warp / position / noclip)
        "teleport waypoint", "map click teleport", "custom map teleport", "safe custom teleport", "tracking teleport",
        "m27 acceptance tracking teleport", "teleport atlas marker", "recall checkpoint", "safe native phenomenon travel",
        "atlas return", "return from atlas",
        // confirmations: permanent save changes and free items
        "Заменить содержимое слота?", "Включить автопополнение?", "Включить автокормление загонов?", "Включить фермерского помощника?",
        "Открыть дверь?", "Заполнить Преобразователь?", "Добавить ресурсы Преобразователя?", "Накормить голодных слаймов вокруг?",
        "Открыть весь атлас?", "Открыть всю Слаймопедию", "Открыть карту и ноды", "Применить все улучшения", "Открыть все двери",
        "Открыть все стили слаймов", "Открыть все чертежи гаджетов", "Открыть все стили вакпака",
    };
    private const string PlacePrefix = "Разместить «"; // GadgetTools: "Разместить «<gadget>»?"
    // Explicitly free: local UI, markers, graphics, inspectors, rescue/return-from-transit, cleanup of own spawns.
    private static readonly HashSet<string> FreeKnown = new(StringComparer.Ordinal)
    {
        "toggle map mark", "runtime sleep", "refinery catalog", "refinery refresh", "compass markers", "tracking refresh", "tracking atlas",
        "temporary FPS cap removal", "runtime FPS", "restore FPS cap", "maximum FPS", "frame profile", "LOD only",
        "slime inspector", "slime inspection", "select radar slime", "radar rare materials", "radar favorite food",
        "phenomena inspector", "phenomena census", "native phenomena census", "native gadget menu", "native build mode", "gadget catalog", "catalog",
        "show custom map marker", "create custom map waypoint", "custom rename", "custom placement map", "custom navigation goal",
        "custom marker settings", "custom map navigation", "custom current position", "center nearest marker", "capsule on atlas",
        "nearest unopened capsule", "open atlas", "atlas settings", "atlas point", "minimap mode", "navigation reset",
        "save waypoint", "delete waypoint", "remember checkpoint", "select ammo slot",
        "safe transit return", "cancel safe transit", "safe position tracking", "SOS rescue", "cleanup owned",
        "restore profile", "explorer profile", "reset player", "info HUD", "hide HUD",
        "graphics preset", "graphics optimization", "detailed graphics",
        "m19-graphics-shadows", "m19-graphics-full", "m19-graphics-effects", "m19-graphics-decor", "m19-graphics-custom",
        "diagnostic potato", "diagnostic graphics restore", "diagnostic geometry restore", "diagnostic geometry low",
        "diagnostic camera restore", "diagnostic camera distance",
        "m27 acceptance tracking refresh", "m27 acceptance tracking atlas", "m27 acceptance compass markers", "m27 acceptance autofeed off",
        "Удалить свою метку?", "Test",
        "coop: effects off",
        // 0.7.0: camera, smooth physics (local rendering only) and own save names (a local file)
        "camera fov", "smooth zoom", "smooth physics", "save name",
    };

    internal static CoopKind Classify(string label)
    {
        if (string.IsNullOrEmpty(label)) return CoopKind.Free;
        if (SoloOnly.Contains(label)) return CoopKind.SoloOnly;
        if (HostWorld.Contains(label)) return CoopKind.HostWorld;
        if (Cheat.Contains(label) || label.StartsWith(PlacePrefix, StringComparison.Ordinal)) return CoopKind.Cheat;
        return CoopKind.Free;
    }

    /// The label is explicitly classified (offline regression: every literal action label must be).
    internal static bool Known(string label) => !string.IsNullOrEmpty(label) && (FreeKnown.Contains(label) ||
        SoloOnly.Contains(label) || HostWorld.Contains(label) || Cheat.Contains(label) || label.StartsWith(PlacePrefix, StringComparison.Ordinal));

    internal static bool Allows(CoopKind kind, CoopRole role, bool serverCheats) => Deny(kind, role, serverCheats) == null;

    /// null — allowed; otherwise the reason shown to the player.
    internal static string Deny(CoopKind kind, CoopRole role, bool serverCheats)
    {
        if (role == CoopRole.Offline || kind == CoopKind.Free) return null;
        switch (kind)
        {
            case CoopKind.SoloOnly:
                return "Кооп (" + ModName + "): заморозка времени, мобов и ускорение симуляции не синхронизируются — доступно только в одиночной игре.";
            case CoopKind.HostWorld:
                if (role == CoopRole.Client) return "Кооп: погодой и временем управляет хост — попросите его.";
                return serverCheats ? null : CheatsOff(role);
            default:
                return serverCheats ? null : CheatsOff(role);
        }
    }

    private static string CheatsOff(CoopRole role) => role == CoopRole.Host
        ? "Кооп: читы на сервере выключены. Включите «Allow Cheats» в настройках " + ModName + " и перезапустите сервер."
        : "Кооп: хост не разрешил читы (" + ModName + " → Allow Cheats).";

    /// Persistent player effects (god mode, infinite energy/items, flight, speed, high jump) are cheats too.
    internal static bool PlayerCheats(CoopRole role, bool serverCheats) => Allows(CoopKind.Cheat, role, serverCheats);

    /// Short footer text for the menu; "" when offline.
    internal static string Footer(CoopRole role, bool serverCheats) => role switch
    {
        CoopRole.Host => serverCheats ? "Кооп · хост · читы вкл" : "Кооп · хост · без читов",
        CoopRole.Client => serverCheats ? "Кооп · гость · читы вкл" : "Кооп · гость · без читов",
        _ => "",
    };
}
