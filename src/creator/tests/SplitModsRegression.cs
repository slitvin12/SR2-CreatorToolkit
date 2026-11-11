using System.Text.RegularExpressions;
using CreatorToolkit;
using P = CreatorToolkit.ModInteropPolicy;

// The SlimeMap / SlimeFPS split: Creator Toolkit keeps its minimap, compass, optimization and FPS
// counter, but steps aside when the matching standalone mod is loaded. Pure detection rules plus
// static wiring pins (the shared source is linked into all three assemblies, so a flavor mod must
// never disable itself). No Unity, MelonLoader or live multi-mod session.
internal static class SplitModsRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void Check(bool value,string name)=>check(value,"split / "+name);
        static string Compact(string s)=>Regex.Replace(s,"\\s+","");
        string dir=Path.Combine(root,"src","creator");
        string Read(string file)=>File.ReadAllText(Path.Combine(dir,file));

        // Detection: a SEPARATE assembly provides the feature; the executing assembly is excluded.
        Check(P.SplitPresent(new[]{"SlimeMap","CreatorToolkit"},"slimemap","CreatorToolkit"),"Creator Toolkit sees a separate SlimeMap");
        Check(!P.SplitPresent(new[]{"SlimeMap"},"slimemap","SlimeMap"),"SlimeMap never disables itself");
        Check(P.SplitPresent(new[]{"SlimeFPS","CreatorToolkit"},"slimefps","CreatorToolkit"),"Creator Toolkit sees a separate SlimeFPS");
        Check(!P.SplitPresent(new[]{"SlimeFPS"},"slimefps","SlimeFPS"),"SlimeFPS never disables itself");
        Check(!P.SplitPresent(new[]{"CreatorToolkit"},"slimemap","CreatorToolkit")&&!P.SplitPresent(new[]{"CreatorToolkit"},"slimefps","CreatorToolkit"),"Creator Toolkit alone has no split mod");
        Check(P.SplitPresent(new[]{"SlimeMap","SlimeFPS"},"slimemap","SlimeFPS"),"SlimeFPS sees SlimeMap as separate");
        Check(P.SplitPresent(new[]{"slime-map"},"slimemap","CreatorToolkit")&&P.SplitPresent(new[]{"SLIMEFPS"},"slimefps","CreatorToolkit"),"split names match case/punctuation-insensitively");
        Check(!P.SplitPresent(null,"slimemap","CreatorToolkit")&&!P.SplitPresent(new[]{null,""},"slimemap","CreatorToolkit"),"split detection tolerates no input");
        Check(!P.SplitPresent(new[]{"SlimeMap"},"","SlimeMap"),"an empty feature never matches");
        Check(P.Find("SlimeMap")==null&&P.Find("SlimeFPS")==null,"first-party split mods stay out of the third-party Nexus catalogue");

        // Interop wiring: presence properties computed from the executing assembly.
        string interop=Read("ModInterop.cs");
        Check(interop.Contains("internal static bool SlimeMapPresent")&&interop.Contains("internal static bool SlimeFpsPresent"),"interop exposes split-mod presence");
        Check(interop.Contains("Assembly.GetExecutingAssembly().GetName().Name")&&
            interop.Contains("ModInteropPolicy.SplitPresent(names, \"slimemap\", self)")&&
            interop.Contains("ModInteropPolicy.SplitPresent(names, \"slimefps\", self)"),"presence excludes the executing assembly via the pure policy");

        // Gates in the shared source (spacing-insensitive).
        string mini=Compact(Read("MiniMapUi.cs"));
        Check(mini.Contains("if(ModInterop.SlimeMapPresent)return;")&&mini.Contains("if(ModInterop.SlimeMapPresent){MiniMapPoints.Clear();return;}"),"minimap draw and tick step aside for SlimeMap");
        string compass=Compact(Read("M27CompassMarkers.cs"));
        Check(compass.Contains("if(ModInterop.SlimeMapPresent){if(_anchors.Count>0)Clear();return;}")&&compass.Contains("if(ModInterop.SlimeMapPresent)return;"),"compass markers and styling step aside for SlimeMap");
        Check(Compact(Read("CaptureUi.cs")).Contains("if(ModInterop.SlimeFpsPresent)return;"),"FPS counter steps aside for SlimeFPS");
        Check(Compact(Read("OptimizationTools.cs")).Contains("if(ModInterop.SlimeFpsPresent)return;"),"optimization tick steps aside for SlimeFPS");
        string optUi=Read("OptimizationUi.cs");
        Check(optUi.Contains("if(ModInterop.SlimeFpsPresent)")&&optUi.Contains("управляет мод SlimeFPS"),"optimization page shows the SlimeFPS note");
        string explorer=Read("ExplorerUi.cs");
        Check(explorer.Contains("if(ModInterop.SlimeMapPresent)")&&explorer.Contains("управляет мод SlimeMap"),"navigation page shows the SlimeMap note");

        // Flavor hosts: distinct melon names, each drawing only its own feature.
        string mapHost=File.ReadAllText(Path.Combine(root,"src","slimemap","SlimeMapHost.cs"));
        Check(mapHost.Contains("[assembly: MelonInfo(typeof(CreatorToolkit.ToolkitMod), \"SlimeMap\"")&&mapHost.Contains("DrawMiniMap()"),"SlimeMap host is named SlimeMap and draws the minimap");
        string fpsHost=File.ReadAllText(Path.Combine(root,"src","slimefps","SlimeFpsHost.cs"));
        Check(fpsHost.Contains("[assembly: MelonInfo(typeof(CreatorToolkit.ToolkitMod), \"SlimeFPS\"")&&fpsHost.Contains("DrawFpsCounter()"),"SlimeFPS host is named SlimeFPS and draws the FPS counter");
    }
}