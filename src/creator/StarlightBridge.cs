using System;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace CreatorToolkit;

// 0.8.0: cooperation with Starlight Core Essentials (ex-SR2E). Reflection only, no compile-time
// reference; completely inert when Starlight is not installed.
//  · menus are exclusive: ours does not open over a Starlight menu/pop-up and closes when one opens;
//    our HUD overlays (info HUD, explorer HUD, minimap, atlas overlay) step aside meanwhile;
//  · Starlight's own cheat switches (infinite health/energy, fog, keep inventory, NoClip) are shown
//    on «Игрок», and Starlight NoClip never runs together with our flight;
//  · a «Creator Toolkit» button in the pause menu through Starlight's public CustomPauseMenuButton.
internal static class StarlightBridge
{
    private static ToolkitMod _app;
    private static Assembly _asm;
    private static bool _resolved;
    private static int _resolveTries;
    private static PropertyInfo _menuOpen, _popUpOpen;
    private static FieldInfo _inGameData, _health, _energy, _fog, _keep;
    private static Type _noClipType;
    private static Il2CppSystem.Type _noClipIl2Cpp;
    private static bool _noClipTypeFailed;
    private static float _nextResolve, _nextMenuPoll, _nextStatePoll;
    private static bool _buttonAdded;
    private static int _buttonFailures, _closedOurMenu, _flightStops, _pauseOpens;
    private static string _buttonError = "";
    private static int _stateKey = -1;

    internal static bool Present => _asm != null;
    internal static bool MenuOpen { get; private set; }
    internal static bool NoClipActive { get; private set; }
    internal static bool InfiniteHealth { get; private set; }
    internal static bool InfiniteEnergy { get; private set; }
    internal static bool RemoveFog { get; private set; }
    internal static bool KeepInventory { get; private set; }
    // One line for «Игрок»: what Starlight itself has switched on (empty when nothing / no Starlight).
    internal static string PlayerNote { get; private set; } = "";

    internal static void Configure(ToolkitMod app) => _app = app;

    private static void Resolve()
    {
        if (_resolved) return;
        float now = Time.unscaledTime;
        if (now < _nextResolve) return;
        _nextResolve = now + 5f;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { if (!a.IsDynamic && a.GetName().Name == "Starlight") { _asm = a; break; } } catch { }
        }
        if (_asm == null) { if (++_resolveTries >= 6) _resolved = true; return; }
        _resolved = true;
        const BindingFlags S = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags I = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        try
        {
            var menu = _asm.GetType("Starlight.Utils.MenuEUtil");
            _menuOpen = menu?.GetProperty("isAnyMenuOpen", S);
            _popUpOpen = menu?.GetProperty("isAnyPopUpOpen", S);
            _inGameData = _asm.GetType("Starlight.Managers.StarlightSaveManager")?.GetField("inGameData", S);
            var data = _asm.GetType("Starlight.Storage.StarlightCustomInGameData");
            _health = data?.GetField("InfiniteHealthActive", I);
            _energy = data?.GetField("InfiniteEnergyActive", I);
            _fog = data?.GetField("RemoveFogActive", I);
            _keep = data?.GetField("KeepInventoryActive", I);
            _noClipType = _asm.GetType("Starlight.Components.NoClipComponent");
        }
        catch (Exception e) { _buttonError = "resolve: " + e.GetType().Name; }
        try { _app?.LoggerInstance.Msg("Starlight bridge: " + Describe()); } catch { }
    }

    internal static void Tick()
    {
        Resolve();
        if (_asm == null || _app == null) return;
        float now = Time.unscaledTime;
        if (now >= _nextMenuPoll)
        {
            _nextMenuPoll = now + .25f;
            bool open = ReadBool(_menuOpen) || ReadBool(_popUpOpen);
            if (open != MenuOpen)
            {
                MenuOpen = open;
                if (open && _app.MenuOpen) { _closedOurMenu++; _app.CloseMenu(); }
            }
        }
        if (now < _nextStatePoll) return;
        _nextStatePoll = now + 1f;
        ReadState();
        TryAddPauseButton();
    }

    private static bool ReadBool(PropertyInfo property)
    {
        if (property == null) return false;
        try { return property.GetValue(null) is true; } catch { return false; }
    }

    private static void ReadState()
    {
        bool ready = _app.Ready;
        bool health = false, energy = false, fog = false, keep = false, noclip = false;
        if (ready)
        {
            try
            {
                object data = _inGameData?.GetValue(null);
                if (data != null)
                {
                    health = _health?.GetValue(data) is true; energy = _energy?.GetValue(data) is true;
                    fog = _fog?.GetValue(data) is true; keep = _keep?.GetValue(data) is true;
                }
            }
            catch { }
            noclip = ReadNoClip();
        }
        InfiniteHealth = health; InfiniteEnergy = energy; RemoveFog = fog; KeepInventory = keep; NoClipActive = noclip;
        int key = (health ? 1 : 0) | (energy ? 2 : 0) | (fog ? 4 : 0) | (keep ? 8 : 0) | (noclip ? 16 : 0);
        if (key != _stateKey)
        {
            _stateKey = key;
            if (key == 0) PlayerNote = "";
            else
            {
                var sb = new StringBuilder("Starlight: уже включено — ");
                bool first = true;
                void Add(bool on, string name) { if (!on) return; if (!first) sb.Append(", "); sb.Append(name); first = false; }
                Add(health, "бессмертие"); Add(energy, "бесконечная энергия"); Add(noclip, "NoClip");
                Add(keep, "сохранение инвентаря"); Add(fog, "карта без тумана");
                PlayerNote = sb.ToString();
            }
        }
        // Two noclips fight over the motor: Starlight's wins, ours steps back.
        if (noclip && _app.Player != null && _app.Player.Flight)
        {
            _flightStops++;
            _app.Queue("flight", () => { _app.Player.SetFlight(false); _app.Notify("Полёт Creator Toolkit выключен: включён NoClip Starlight."); });
        }
    }

    private static bool ReadNoClip()
    {
        if (_noClipType == null || _noClipTypeFailed) return false;
        try
        {
            _noClipIl2Cpp ??= Il2CppInterop.Runtime.Il2CppType.From(_noClipType, false);
            if (_noClipIl2Cpp == null) { _noClipTypeFailed = true; return false; }
            var camera = _app.Context?.Camera;
            if (camera == null) return false;
            return camera.GetComponent(_noClipIl2Cpp) != null;
        }
        catch { _noClipTypeFailed = true; return false; }
    }

    private static void TryAddPauseButton()
    {
        if (_buttonAdded || _buttonFailures >= 3 || !_app.Ready) return;
        try
        {
            var lang = _asm.GetType("Starlight.Utils.LanguageEUtil");
            var add = lang?.GetMethod("AddTranslation", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), typeof(string), typeof(string) }, null);
            var buttonType = _asm.GetType("Starlight.Buttons.CustomPauseMenuButton");
            if (add == null || buttonType == null) { _buttonFailures = 3; _buttonError = "API not found"; return; }
            object label = add.Invoke(null, new object[] { "Creator Toolkit", "b.creator_toolkit_pause", "UI" });
            if (label == null) { _buttonFailures++; _buttonError = "no label"; return; }
            Activator.CreateInstance(buttonType, label, 4, (Action)OpenFromPause);
            _buttonAdded = true;
            _app.LoggerInstance.Msg("Starlight: «Creator Toolkit» pause-menu button registered.");
        }
        catch (Exception e)
        {
            _buttonFailures++;
            _buttonError = (e.InnerException ?? e).GetType().Name + ": " + (e.InnerException ?? e).Message;
            if (_buttonFailures >= 3) _app.LoggerInstance.Warning("Starlight pause button unavailable: " + _buttonError);
        }
    }

    // Pause menu → resume the game the native way, then open our menu once the pause UI is gone.
    private static void OpenFromPause()
    {
        _pauseOpens++;
        try
        {
            var resume = ScriptableObject.CreateInstance<Il2CppMonomiPark.SlimeRancher.UI.Pause.ResumePauseItemModel>();
            try { resume.InvokeBehavior(); }
            finally { UnityEngine.Object.Destroy(resume); }
        }
        catch
        {
            try { Il2Cpp.SystemContext.Instance.SceneLoader.UnpauseGame(); } catch { }
        }
        _app?.Input.OpenAfterNativeClose();
    }

    private static string Describe() =>
        "present=" + (_asm != null) + " version=" + (_asm?.GetName().Version?.ToString() ?? "-") +
        " menuApi=" + (_menuOpen != null) + " saveFlags=" + (_inGameData != null && _health != null) + " noclipType=" + (_noClipType != null);

    internal static string Diagnostics()
    {
        if (_asm == null) return "Starlight: absent\n";
        return "Starlight: " + Describe() + " menuOpen=" + MenuOpen + " noclip=" + NoClipActive + " flags=" + _stateKey +
            " pauseButton=" + _buttonAdded + (_buttonError.Length > 0 ? " (" + _buttonError + ")" : "") +
            " closedOurMenu=" + _closedOurMenu + " flightStops=" + _flightStops + " pauseOpens=" + _pauseOpens + "\n";
    }
}
