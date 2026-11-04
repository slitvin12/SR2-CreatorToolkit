using System;
using System.IO;
using System.Text;
using UnityEngine.InputSystem;

namespace CreatorToolkit;

// 0.8.3 «Настройки → Горячие клавиши»: rebindable keys for flight, god mode, energy, info HUD,
// FPS counter and the clean frame (ActionHotkeyPolicy). Capture works like the menu key: the
// next allowed key is taken and saved at once, Esc cancels. Actions go through ToolkitMod.Queue,
// so the co-op gate and the session check apply exactly as for the on-screen switches.
internal sealed class ActionHotkeys
{
    private readonly ToolkitMod _app;
    private bool _loaded;
    private readonly string[] _keys=ActionHotkeyPolicy.Empty();
    internal int Capturing{get;private set;}=-1;
    internal int Fired{get;private set;}
    internal ActionHotkeys(ToolkitMod app)=>_app=app;

    internal string Key(int action){EnsureLoaded();return action>=0&&action<_keys.Length?_keys[action]:null;}
    internal string Display(int action)=>ActionHotkeyPolicy.Display(Key(action));
    internal bool Blocked(int action)=>Key(action)!=null&&!ActionHotkeyPolicy.Active(Key(action),_app.Hotkey?.Key);
    internal string Summary
    {
        get
        {
            EnsureLoaded();var text=new StringBuilder();
            for(int i=0;i<_keys.Length;i++)text.Append(i>0?" ":"").Append(ActionHotkeyPolicy.Ids[i]).Append('=').Append(_keys[i]??"-");
            return text.ToString();
        }
    }

    internal void EnsureLoaded()
    {
        if(_loaded||string.IsNullOrEmpty(_app.DataDirectory))return;
        _loaded=true;
        try
        {
            string file=Path.Combine(_app.DataDirectory,ActionHotkeyPolicy.FileName);
            if(!File.Exists(file))return;
            var parsed=ActionHotkeyPolicy.Parse(File.ReadAllText(file,Encoding.UTF8));
            Array.Copy(parsed,_keys,_keys.Length);
        }
        catch(Exception e){_app.LoggerInstance.Warning("[hotkeys] load: "+e.Message);}
    }
    internal void StartCapture(int action)
    {
        EnsureLoaded();
        if(action<0||action>=_keys.Length)return;
        _app.Hotkey?.CancelCapture();
        Capturing=action;
    }
    internal void CancelCapture()=>Capturing=-1;
    internal void Clear(int action)
    {
        EnsureLoaded();Capturing=-1;
        if(action<0||action>=_keys.Length||_keys[action]==null)return;
        _keys[action]=null;
        bool saved=Save();
        _app.Notify(ActionHotkeyPolicy.Titles[action]+": клавиша снята"+(saved?"":" (не сохранено)"));
    }
    // Called instead of the action keys while capturing (menu open): Esc cancels, the first allowed key is taken.
    internal void TickCapture(Keyboard kb)
    {
        if(Capturing<0||kb==null)return;
        if(kb.escapeKey.wasPressedThisFrame){Capturing=-1;_app.Notify("Клавиша не изменена");return;}
        foreach(var name in MenuHotkeyPolicy.Keys)
        {
            var control=MenuHotkey.Control(kb,name);
            if(control==null||!control.wasPressedThisFrame)continue;
            int action=Capturing;Capturing=-1;
            string denied=ActionHotkeyPolicy.Assign(_keys,action,name,_app.Hotkey?.Key,out int moved);
            if(denied!=null){_app.Notify(denied);return;}
            bool saved=Save();
            _app.Notify(ActionHotkeyPolicy.Titles[action]+": "+Display(action)+
                (moved>=0?" · снята с «"+ActionHotkeyPolicy.Titles[moved]+"»":"")+(saved?"":" (не сохранено)"));
            return;
        }
    }
    // Gameplay and menu: an action fires on the frame its key goes down. Ctrl/Alt + key never fires,
    // the menu key always wins, typing keys wait while a mod text field has focus.
    internal void Tick(Keyboard kb,bool textFocused)
    {
        if(kb==null||Capturing>=0)return;
        EnsureLoaded();
        if(kb.leftCtrlKey.isPressed||kb.rightCtrlKey.isPressed||kb.leftAltKey.isPressed||kb.rightAltKey.isPressed)return;
        string menu=_app.Hotkey?.Key;
        for(int i=0;i<_keys.Length;i++)
        {
            string key=_keys[i];
            if(!ActionHotkeyPolicy.Active(key,menu))continue;
            var control=MenuHotkey.Control(kb,key);
            if(control==null||!control.wasPressedThisFrame)continue;
            if(textFocused&&MenuHotkeyPolicy.Typing(key))continue;
            Run(i);
        }
    }
    internal void Run(int action)
    {
        if(action<0||action>=_keys.Length)return;
        string id=ActionHotkeyPolicy.Ids[action];
        if(id=="fps")
        {
            var cap=_app.Capture;
            if(cap==null)return;
            Fired++;cap.SetShowFps(!cap.ShowFps);return;
        }
        if(!_app.Ready){_app.Notify("Сначала загрузите сейв.");return;}
        Fired++;
        var player=_app.Player;
        switch(id)
        {
            case "flight":{bool on=!player.Flight;_app.Queue("flight",()=>player.SetFlight(on));break;}
            case "god":_app.Queue("hotkey god mode",()=>{player.GodMode=!player.GodMode;_app.Notify(player.GodMode?"Бессмертие: ВКЛ":"Бессмертие: ВЫКЛ");});break;
            case "energy":_app.Queue("hotkey infinite energy",()=>{player.InfiniteEnergy=!player.InfiniteEnergy;_app.Notify(player.InfiniteEnergy?"Бесконечная энергия: ВКЛ":"Бесконечная энергия: ВЫКЛ");});break;
            case "hud":{var tools=_app.Convenience;bool show=!tools.ShowHud;_app.Queue("info HUD",()=>{tools.SetHud(show);_app.Notify(show?"Инфо-HUD: ВКЛ":"Инфо-HUD: ВЫКЛ");});break;}
            case "frame":{var cinema=_app.Cinema;bool hide=!cinema.HideHud;_app.Queue("hide HUD",()=>cinema.SetHideHud(hide));break;}
        }
    }
    private bool Save()
    {
        string dir=_app.DataDirectory;
        if(string.IsNullOrEmpty(dir))return false;
        try
        {
            Directory.CreateDirectory(dir);
            string file=Path.Combine(dir,ActionHotkeyPolicy.FileName),temp=file+".tmp";
            File.WriteAllText(temp,ActionHotkeyPolicy.Serialize(_keys),new UTF8Encoding(false));
            if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
            return true;
        }
        catch(Exception e){_app.LoggerInstance.Warning("[hotkeys] save: "+e.Message);return false;}
    }
}
