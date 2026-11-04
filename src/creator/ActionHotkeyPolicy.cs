using System;
using System.Text;

namespace CreatorToolkit;

// 0.8.3 «Настройки»: optional keys for the most used switches. Pure policy (offline-tested).
// Every action starts unbound. A key is one MenuHotkeyPolicy key (never Esc, F1, Tab, Enter,
// Space, arrows or modifiers), never the menu key and never shared by two actions: assigning a
// taken key moves it. Kept in UserData\CreatorToolkit\hotkeys.cfg as «<id>=<Key>» lines.
internal static class ActionHotkeyPolicy
{
    internal const string FileName="hotkeys.cfg";
    internal static readonly string[] Ids={"flight","god","energy","hud","fps","frame"};
    internal static readonly string[] Titles={"Полёт / NoClip","Бессмертие","Бесконечная энергия","Инфо-HUD","Счётчик FPS","Чистый кадр"};
    internal static int Count=>Ids.Length;
    internal static string[] Empty()=>new string[Ids.Length];
    internal static int Index(string id)=>Array.IndexOf(Ids,(id??"").Trim().ToLowerInvariant());

    // The canonical key name, or null when the key is not allowed.
    internal static string Key(string name)
    {
        string k=(name??"").Trim();
        if(k.Length==0)return null;
        foreach(var allowed in MenuHotkeyPolicy.Keys)if(string.Equals(allowed,k,StringComparison.OrdinalIgnoreCase))return allowed;
        return null;
    }
    internal static string[] Parse(string text)
    {
        var keys=Empty();
        foreach(var raw in (text??"").Split('\n'))
        {
            string line=raw.Trim();
            if(line.Length==0||line[0]=='#')continue;
            int eq=line.IndexOf('=');
            if(eq<=0)continue;
            int index=Index(line.Substring(0,eq));
            string key=Key(line.Substring(eq+1));
            if(index<0||key==null||Array.IndexOf(keys,key)>=0)continue; // unknown, unbound or a duplicate: the first line wins
            keys[index]=key;
        }
        return keys;
    }
    internal static string Serialize(string[] keys)
    {
        var text=new StringBuilder("# Creator Toolkit: action keys (UnityEngine.InputSystem.Key names, empty = none)\n");
        for(int i=0;i<Ids.Length;i++)
            text.Append(Ids[i]).Append('=').Append(keys!=null&&i<keys.Length?Key(keys[i])??"":"").Append('\n');
        return text.ToString();
    }
    // null — assigned (a previous owner of the key is unbound and reported in «moved»), else the refusal.
    internal static string Assign(string[] keys,int action,string key,string menuKey,out int moved)
    {
        moved=-1;
        if(keys==null||keys.Length!=Ids.Length||action<0||action>=Ids.Length)return "Нет такого действия.";
        string k=Key(key);
        if(k==null)return "Эта клавиша не подходит: Esc, F1, Tab, Enter, пробел и стрелки заняты.";
        if(string.Equals(k,MenuHotkeyPolicy.Normalize(menuKey),StringComparison.Ordinal))return "Эта клавиша уже открывает меню.";
        for(int i=0;i<keys.Length;i++)
            if(i!=action&&string.Equals(keys[i],k,StringComparison.Ordinal)){keys[i]=null;moved=i;}
        keys[action]=k;
        return null;
    }
    // The menu key always wins: an action bound to it is ignored until rebound.
    internal static bool Active(string key,string menuKey)=>key!=null&&!string.Equals(key,MenuHotkeyPolicy.Normalize(menuKey),StringComparison.Ordinal);
    internal static string Display(string key)=>Key(key)==null?"—":MenuHotkeyPolicy.Display(key);
}
