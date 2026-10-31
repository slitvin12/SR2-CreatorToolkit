using System;

namespace CreatorToolkit;

// 0.8.1: the key that opens and closes the mod menu (Insert by default), chosen on «Удобство».
// Names are UnityEngine.InputSystem.Key names (physical US-layout positions). Esc stays the
// close key; F1 (native pedia), Tab, Enter, Space, Backspace, arrows, modifiers and lock keys
// are never offered. Kept in UserData\CreatorToolkit\hotkey.cfg as «menu.key=Name».
internal static class MenuHotkeyPolicy
{
    internal const string FileName="hotkey.cfg",Default="Insert",Prefix="menu.key=";
    internal static readonly string[] Keys=
    {
        "Insert","Home","End","PageUp","PageDown","Delete","Pause","ScrollLock",
        "F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12",
        "Numpad0","Numpad1","Numpad2","Numpad3","Numpad4","Numpad5","Numpad6","Numpad7","Numpad8","Numpad9",
        "NumpadPlus","NumpadMinus","NumpadMultiply","NumpadDivide","NumpadPeriod","NumpadEnter",
        "Backquote","Minus","Equals","LeftBracket","RightBracket","Backslash","Semicolon","Quote","Comma","Period","Slash",
        "A","B","C","D","E","F","G","H","I","J","K","L","M","N","O","P","Q","R","S","T","U","V","W","X","Y","Z",
        "Digit0","Digit1","Digit2","Digit3","Digit4","Digit5","Digit6","Digit7","Digit8","Digit9",
    };
    // Keys that also type or edit text: they do nothing while a mod text field has focus.
    internal static bool Typing(string key)
    {
        if(string.IsNullOrEmpty(key)||key is "Insert" or "PageUp" or "PageDown" or "Pause" or "ScrollLock")return false;
        return !(key.Length>1&&key[0]=='F'&&char.IsDigit(key[1])); // F2..F12 never type
    }
    internal static bool Allowed(string key)=>!string.IsNullOrEmpty(key)&&Array.IndexOf(Keys,key)>=0;
    internal static string Normalize(string key)
    {
        string k=(key??"").Trim();
        foreach(var name in Keys)if(string.Equals(name,k,StringComparison.OrdinalIgnoreCase))return name;
        return Default;
    }
    internal static string Parse(string text)
    {
        foreach(var raw in (text??"").Split('\n'))
        {
            string line=raw.Trim();
            if(line.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase))return Normalize(line.Substring(Prefix.Length));
        }
        return Default;
    }
    internal static string Serialize(string key)=>"# Creator Toolkit: menu key (UnityEngine.InputSystem.Key name)\n"+Prefix+Normalize(key)+"\n";
    internal static string Display(string key)
    {
        string k=Normalize(key);
        if(k.Length==1)return k;
        if(k.StartsWith("Digit",StringComparison.Ordinal))return k.Substring(5);
        if(k.StartsWith("Numpad",StringComparison.Ordinal))
        {
            string rest=k.Substring(6);
            return "Num "+(rest switch{"Plus"=>"+","Minus"=>"−","Multiply"=>"*","Divide"=>"/","Period"=>".","Enter"=>"Enter",_=>rest});
        }
        return k switch
        {
            "PageUp"=>"Page Up","PageDown"=>"Page Down","ScrollLock"=>"Scroll Lock","Backquote"=>"`","Minus"=>"-","Equals"=>"=",
            "LeftBracket"=>"[","RightBracket"=>"]","Backslash"=>"\\","Semicolon"=>";","Quote"=>"'","Comma"=>",","Period"=>".","Slash"=>"/",
            _=>k,
        };
    }
    // Footer and hints: «Insert  ·  Esc».
    internal static string Footer(string key)=>Display(key)+"  ·  Esc";
}
