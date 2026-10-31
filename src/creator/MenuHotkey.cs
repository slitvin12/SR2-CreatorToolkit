using System;
using System.IO;
using System.Text;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CreatorToolkit;

// 0.8.1: the rebindable mod-menu key (MenuHotkeyPolicy). «Назначить…» on «Удобство» starts a
// capture: the next allowed key becomes the menu key and is saved at once; Esc cancels.
// Ctrl/Alt + key never toggles the menu (Ctrl+Home is the SOS return, Ctrl+A/C/V/X edit text).
internal sealed class MenuHotkey
{
    private readonly ToolkitMod _app;
    private bool _loaded;
    internal string Key{get;private set;}=MenuHotkeyPolicy.Default;
    internal string Display=>MenuHotkeyPolicy.Display(Key);
    internal string Footer=>MenuHotkeyPolicy.Footer(Key);
    internal bool Capturing{get;private set;}
    internal bool IsDefault=>Key==MenuHotkeyPolicy.Default;
    internal bool Typing=>MenuHotkeyPolicy.Typing(Key);
    internal MenuHotkey(ToolkitMod app)=>_app=app;

    internal static KeyControl Control(Keyboard kb,string name)=>kb==null?null:name switch
    {
        "Insert"=>kb.insertKey,"Home"=>kb.homeKey,"End"=>kb.endKey,"PageUp"=>kb.pageUpKey,"PageDown"=>kb.pageDownKey,"Delete"=>kb.deleteKey,
        "Pause"=>kb.pauseKey,"ScrollLock"=>kb.scrollLockKey,"F2"=>kb.f2Key,"F3"=>kb.f3Key,"F4"=>kb.f4Key,"F5"=>kb.f5Key,"F6"=>kb.f6Key,
        "F7"=>kb.f7Key,"F8"=>kb.f8Key,"F9"=>kb.f9Key,"F10"=>kb.f10Key,"F11"=>kb.f11Key,"F12"=>kb.f12Key,"Numpad0"=>kb.numpad0Key,
        "Numpad1"=>kb.numpad1Key,"Numpad2"=>kb.numpad2Key,"Numpad3"=>kb.numpad3Key,"Numpad4"=>kb.numpad4Key,"Numpad5"=>kb.numpad5Key,
        "Numpad6"=>kb.numpad6Key,"Numpad7"=>kb.numpad7Key,"Numpad8"=>kb.numpad8Key,"Numpad9"=>kb.numpad9Key,"NumpadPlus"=>kb.numpadPlusKey,
        "NumpadMinus"=>kb.numpadMinusKey,"NumpadMultiply"=>kb.numpadMultiplyKey,"NumpadDivide"=>kb.numpadDivideKey,
        "NumpadPeriod"=>kb.numpadPeriodKey,"NumpadEnter"=>kb.numpadEnterKey,"Backquote"=>kb.backquoteKey,"Minus"=>kb.minusKey,
        "Equals"=>kb.equalsKey,"LeftBracket"=>kb.leftBracketKey,"RightBracket"=>kb.rightBracketKey,"Backslash"=>kb.backslashKey,
        "Semicolon"=>kb.semicolonKey,"Quote"=>kb.quoteKey,"Comma"=>kb.commaKey,"Period"=>kb.periodKey,"Slash"=>kb.slashKey,"A"=>kb.aKey,
        "B"=>kb.bKey,"C"=>kb.cKey,"D"=>kb.dKey,"E"=>kb.eKey,"F"=>kb.fKey,"G"=>kb.gKey,"H"=>kb.hKey,"I"=>kb.iKey,"J"=>kb.jKey,"K"=>kb.kKey,
        "L"=>kb.lKey,"M"=>kb.mKey,"N"=>kb.nKey,"O"=>kb.oKey,"P"=>kb.pKey,"Q"=>kb.qKey,"R"=>kb.rKey,"S"=>kb.sKey,"T"=>kb.tKey,"U"=>kb.uKey,
        "V"=>kb.vKey,"W"=>kb.wKey,"X"=>kb.xKey,"Y"=>kb.yKey,"Z"=>kb.zKey,"Digit0"=>kb.digit0Key,"Digit1"=>kb.digit1Key,"Digit2"=>kb.digit2Key,
        "Digit3"=>kb.digit3Key,"Digit4"=>kb.digit4Key,"Digit5"=>kb.digit5Key,"Digit6"=>kb.digit6Key,"Digit7"=>kb.digit7Key,"Digit8"=>kb.digit8Key,
        "Digit9"=>kb.digit9Key,
        _=>null,
    };

    internal void EnsureLoaded()
    {
        if(_loaded||string.IsNullOrEmpty(_app.DataDirectory))return;
        _loaded=true;
        try
        {
            string file=Path.Combine(_app.DataDirectory,MenuHotkeyPolicy.FileName);
            if(File.Exists(file))Key=MenuHotkeyPolicy.Parse(File.ReadAllText(file,Encoding.UTF8));
        }
        catch(Exception e){_app.LoggerInstance.Warning("[hotkey] load: "+e.Message);}
    }
    // True on the frame the menu key goes down (no Ctrl/Alt held).
    internal bool Pressed(Keyboard kb)
    {
        EnsureLoaded();
        var key=Control(kb,Key)??kb.insertKey;
        if(key==null||!key.wasPressedThisFrame)return false;
        return !(kb.leftCtrlKey.isPressed||kb.rightCtrlKey.isPressed||kb.leftAltKey.isPressed||kb.rightAltKey.isPressed);
    }
    internal void StartCapture(){EnsureLoaded();Capturing=true;}
    internal void CancelCapture()=>Capturing=false;
    // Called instead of the toggle while capturing: Esc cancels, the first allowed key is taken.
    internal void TickCapture(Keyboard kb)
    {
        if(!Capturing||kb==null)return;
        if(kb.escapeKey.wasPressedThisFrame){Capturing=false;_app.Notify("Клавиша меню не изменена");return;}
        foreach(var name in MenuHotkeyPolicy.Keys)
        {
            var control=Control(kb,name);
            if(control==null||!control.wasPressedThisFrame)continue;
            Capturing=false;Set(name);return;
        }
    }
    internal void Reset(){Capturing=false;Set(MenuHotkeyPolicy.Default);}
    private void Set(string name)
    {
        Key=MenuHotkeyPolicy.Normalize(name);
        bool saved=Save();
        _app.Status=Display+" — открыть меню";
        _app.Notify("Клавиша меню: "+Display+(saved?"":" (не сохранено)"));
    }
    private bool Save()
    {
        string dir=_app.DataDirectory;
        if(string.IsNullOrEmpty(dir))return false;
        try
        {
            Directory.CreateDirectory(dir);
            string file=Path.Combine(dir,MenuHotkeyPolicy.FileName),temp=file+".tmp";
            File.WriteAllText(temp,MenuHotkeyPolicy.Serialize(Key),new UTF8Encoding(false));
            if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
            return true;
        }
        catch(Exception e){_app.LoggerInstance.Warning("[hotkey] save: "+e.Message);return false;}
    }
}
