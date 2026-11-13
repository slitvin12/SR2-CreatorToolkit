using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace CreatorToolkit;
// 0.8.0 offline regression: English UI table (game language, live switch), «?» hints,
// infinite builder / free placement on the player page, the FPS card, the lighter map page
// and the reflection-only Starlight bridge. Source wiring and data only; no Unity, no game.
internal static class M31Regression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool b,string s)=>check(b,"0.8 / "+s);
        string dir=Path.Combine(root,"src","creator");
        string R(string name){string p=Path.Combine(dir,name);return File.Exists(p)?File.ReadAllText(p):"";}
        Table(C,R("LocEnglish.tsv"));
        string loc=R("Loc.cs"),mod=R("ToolkitMod.cs"),ui=R("ToolkitUi.cs"),explorer=R("ExplorerUi.cs"),bridge=R("StarlightBridge.cs"),
            input=R("InputCapture.cs"),player=R("PlayerTools.cs"),capture=R("CaptureUi.cs"),project=R("CreatorToolkit.csproj");
        // Language: game locale first, English for every non-Russian locale, live polling.
        C(loc.Contains("director.GetCurrentLocaleCode()")&&loc.Contains("StartsWith(\"ru\", StringComparison.OrdinalIgnoreCase)")&&
          loc.Contains("Application.systemLanguage")&&loc.Contains("\"CreatorToolkit.LocEnglish.tsv\""),"language: native locale code, ru* = Russian, everything else English");
        C(mod.Contains("(\"ui language\",Loc.Tick)")&&mod.Contains("case \"lang-en\"")&&mod.Contains("case \"loc-misses\"")&&mod.Contains("UiLanguage: "),
          "language: ticked live, test overrides and diagnostics");
        C(project.Contains("LogicalName=\"CreatorToolkit.LocEnglish.tsv\"")&&project.Contains("LogicalName=\"CreatorToolkit.icon.png\"")&&!project.Contains(".en.tsv\""),
          "package: table and icon are embedded (no culture suffix, no satellite assembly)");
        C(loc.Contains("_sceneReady ? ReadGameLocale() : null")&&loc.Contains("loader.IsSceneLoadInProgress")&&loc.Contains("if (code != _candidate) { _candidate = code; return; }")&&
          mod.Contains("OnSceneWasLoaded(int buildIndex,string sceneName)=>Loc.SceneLoaded(sceneName)"),"language: the boot default locale is ignored until a real scene, two polls agree");
        string mapHost=File.ReadAllText(Path.Combine(root,"src","slimemap","SlimeMapHost.cs")),fpsHost=File.ReadAllText(Path.Combine(root,"src","slimefps","SlimeFpsHost.cs"));
        C(mapHost.Contains("Loc.Tick();")&&fpsHost.Contains("Loc.Tick();"),"language: standalone hosts tick the locale poll every frame");
        C(loc.Contains("internal static string MapLocale(")&&loc.Contains("private static string TableName(")&&loc.Contains("\"CreatorToolkit.\" + TableName(lang) + \".tsv\"")&&loc.Contains("internal static string Language"),
          "language: per-locale tables with English fallback");
        foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(project,"LogicalName=\"CreatorToolkit\\.(Loc[A-Za-z]+\\.tsv)\""))
        {
            string name=m.Groups[1].Value;if(name=="LocEnglish.tsv")continue;
            LangTable(C,File.ReadAllText(Path.Combine(dir,name)),R("LocEnglish.tsv"),name);
        }
        // Every visible label goes through the table; typed text stays raw.
        foreach(var f in Directory.GetFiles(dir,"*.cs"))
        {
            string name=Path.GetFileName(f);if(name=="Loc.cs")continue;
            string text=File.ReadAllText(f);
            int raw=text.Split("GUI.Label(").Length-1+text.Split("GUI.Box(").Length-1;
            int allowed=name=="ToolkitUi.cs"?1:0;
            if(raw>allowed)C(false,"language: raw GUI.Label/Box bypasses the table in "+name);
        }
        C(ui.Contains("GUI.Label(new Rect(-scroll,0,Mathf.Max(inside.width,textWidth+4),inside.height),display,_label); // typed text: never translated"),
          "language: only typed text is drawn untranslated");
        C(ui.Contains("internal void SyncLanguage()")&&mod.Contains("SyncLanguage()"),"language: cached button/label texts are rebuilt on a switch");
        C(R("AtlasUi.cs").Contains("row.Title=Loc.T(map.DisplayName(point));")&&ui.Contains("text=Loc.T(text);\r\n        return text.Length>90")&&
          ui.Contains("string status=Loc.T(raw);"),"language: rows, wiki summaries and the status line are translated before the length cut");
        // Hints.
        C(ui.Contains("private void Hint(Rect rect,string text,GUIStyle style=null){if(DetailedHelp&&!string.IsNullOrEmpty(text))"),"hints: long explanations only with «?»");
        // Player page toggles.
        C(ui.Contains("\"Бесконечный режим строителя\"")&&ui.Contains("QueueInfiniteBuilder(builder)")&&ui.Contains("_app.Gadgets.SetInfiniteBuilder(enabled)"),
          "player: infinite builder toggle");
        C(ui.Contains("\"Свободная установка\"")&&ui.Contains("QueueFreePlacement(freeNext)")&&ui.Contains("_app.FreePlacement.Set(enabled)"),"player: free placement toggle");
        // FPS card and the lighter map page.
        C(ui.Contains("cap.ShowFps?\"Счётчик FPS: ВКЛ\":\"Счётчик FPS: ВЫКЛ\""),"convenience: FPS counter switch");
        C(capture.Contains("FpsPeakMs")||R("CaptureTools.cs").Contains("FpsPeakMs"),"fps: frame time and peak for the styled counter");
        C(!ui.Contains("\"Открыть весь атлас?\"")&&!ui.Contains("\"Мини-карта: радар\"")&&explorer.Contains("nav.SetMode(mode)"),
          "map: duplicate reveal/minimap buttons removed (Открытия / Навигация keep them)");
        // Starlight: reflection only, never a hard reference.
        C(!project.Contains("Starlight.dll")&&!project.Contains("Include=\"Starlight\""),"starlight: no compile-time reference");
        C(bridge.Contains("\"isAnyMenuOpen\"")&&bridge.Contains("_app.CloseMenu()")&&input.Contains("StarlightBridge.MenuOpen"),
          "starlight: menus are mutually exclusive");
        C(bridge.Contains("Starlight.Managers.StarlightSaveManager")&&bridge.Contains("\"inGameData\"")&&bridge.Contains("Starlight.Components.NoClipComponent")&&
          player.Contains("StarlightBridge.NoClipActive"),"starlight: cheat flags shown, flight refuses while NoClip is on");
        C(bridge.Contains("Starlight.Buttons.CustomPauseMenuButton")&&bridge.Contains("\"AddTranslation\""),"starlight: pause-menu button through the public API");
        C(mod.Contains("StarlightBridge.Configure(this)")&&mod.Contains("AssemblyVersion(\"0.9.0.0\")"),"starlight: configured; release 0.9.0");
    }

    private static void Table(Action<bool,string> C,string tsv)
    {
        var keys=new HashSet<string>(StringComparer.Ordinal);int bad=0,dup=0,cyr=0,rows=0;
        foreach(string raw in tsv.Split('\n'))
        {
            string line=raw.TrimEnd('\r');
            if(line.Length==0||line[0]=='#')continue;
            rows++;
            int tab=line.IndexOf('\t');
            if(tab<=0||line.IndexOf('\t',tab+1)>=0){bad++;continue;}
            string key=line.Substring(0,tab),value=line.Substring(tab+1);
            if(!keys.Add(key))dup++;
            if(value.Any(ch=>ch>='\u0400'&&ch<='\u04FF'))cyr++;
        }
        C(rows>=1500,"table: "+rows+" rows");
        C(bad==0,"table: key<TAB>value on every row ("+bad+" bad)");
        C(dup==0,"table: unique keys ("+dup+" duplicates)");
        C(cyr<=2,"table: English values ("+cyr+" with Cyrillic)");
        C(tsv.Contains("\nУдобство\tUtility\r")&&tsv.Contains("\nРесурсы: ценные\tValuables\r"),"table: short labels for the narrow tab and filter buttons");
        foreach(string k in new[]{"Бесконечный режим строителя","Свободная установка","Счётчик FPS: ВКЛ","Подтвердить","Отмена"})
            C(keys.Contains(k),"table: has \""+k+"\"");
    }

    private static void LangTable(Action<bool,string> C,string tsv,string en,string name)
    {
        var enRows=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(string raw in en.Split('\n'))
        {
            string line=raw.TrimEnd('\r');
            if(line.Length==0||line[0]=='#')continue;
            int tab=line.IndexOf('\t');if(tab<=0)continue;
            enRows[line.Substring(0,tab)]=line.Substring(tab+1);
        }
        var keys=new HashSet<string>(StringComparer.Ordinal);int bad=0,cyr=0,rows=0,ws=0,ph=0;
        foreach(string raw in tsv.Split('\n'))
        {
            string line=raw.TrimEnd('\r');
            if(line.Length==0||line[0]=='#')continue;
            rows++;
            int tab=line.IndexOf('\t');
            if(tab<=0||line.IndexOf('\t',tab+1)>=0){bad++;continue;}
            string key=line.Substring(0,tab),value=line.Substring(tab+1);
            keys.Add(key);
            if(value.Any(ch=>ch>='Ѐ'&&ch<='ӿ'))cyr++;
            if(enRows.TryGetValue(key,out var ev))
            {
                if(Lead(value)!=Lead(ev)||Trail(value)!=Trail(ev))ws++;
                if(Placeholders(value)!=Placeholders(ev))ph++;
            }
        }
        int missing=enRows.Keys.Count(k=>!keys.Contains(k)),extra=keys.Count(k=>!enRows.ContainsKey(k));
        C(bad==0,name+": key<TAB>value on every row ("+bad+" bad)");
        C(missing==0&&extra==0,name+": same keys as English ("+missing+" missing, "+extra+" extra)");
        C(cyr<=2,name+": no Cyrillic left ("+cyr+")");
        C(ws==0,name+": leading/trailing spaces preserved ("+ws+" broken)");
        C(ph==0,name+": {N} placeholders preserved ("+ph+" broken)");
        C(rows>=1500,name+": "+rows+" rows");
    }
    private static string Lead(string s){int i=0;while(i<s.Length&&char.IsWhiteSpace(s[i]))i++;return s.Substring(0,i);}
    private static string Trail(string s){int i=s.Length;while(i>0&&char.IsWhiteSpace(s[i-1]))i--;return s.Substring(i);}
    private static string Placeholders(string s)
    {
        var sb=new System.Text.StringBuilder();
        foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(s, @"\{\d+\}"))sb.Append(m.Value).Append(',');
        return sb.ToString();
    }
}
