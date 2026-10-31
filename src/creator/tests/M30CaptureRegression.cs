using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace CreatorToolkit;
// 0.7.0 offline regression: camera FOV / smooth zoom, FPS counter, background FPS limit,
// capture.cfg, own save names (save-names.cfg, main menu captions) and free gadget placement.
// Pure policies + source wiring; no Unity, no game process, no saves.
internal static class M30CaptureRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool b,string s)=>check(b,"0.7 / "+s);
        Camera(C);Fps(C);Settings(C);Names(C);Placement(C);Sources(C,root);
    }

    private static bool Near(float a,float b,float e=.01f)=>MathF.Abs(a-b)<=e;

    private static void Camera(Action<bool,string> C)
    {
        C(CapturePolicy.Fov(10)==CapturePolicy.MinFov&&CapturePolicy.Fov(500)==CapturePolicy.MaxFov&&CapturePolicy.Fov(float.NaN)==CapturePolicy.DefaultFov&&CapturePolicy.Fov(90.4f)==90,
          "fov: clamped to 30..120, whole degrees, NaN falls back to 75");
        C(CapturePolicy.Zoom(1)==CapturePolicy.MinZoom&&CapturePolicy.Zoom(99)==CapturePolicy.MaxZoom&&CapturePolicy.Zoom(3.3f)==3.5f&&CapturePolicy.Zoom(float.PositiveInfinity)==CapturePolicy.DefaultZoom,
          "zoom: clamped to x1.5..x8 in half steps, invalid falls back to x4");
        C(CapturePolicy.ZoomKey(-1)==0&&CapturePolicy.ZoomKey(CapturePolicy.ZoomKeys.Length)==0&&CapturePolicy.ZoomKey(3)==3,"zoom key: out of range is the first key");
        C(CapturePolicy.ZoomedFov(75,4,0)==75&&Near(CapturePolicy.ZoomedFov(90,2,1),53.13f,.05f)&&CapturePolicy.ZoomedFov(75,8,1)<CapturePolicy.ZoomedFov(75,4,1),
          "zoom: optical (tangent) zoom, none at t=0, a larger factor narrows more");
        C(CapturePolicy.ZoomedFov(float.NaN,4,0)==CapturePolicy.DefaultFov&&CapturePolicy.ZoomedFov(75,4,float.NaN)==75&&CapturePolicy.ZoomedFov(75,4,5)==CapturePolicy.ZoomedFov(75,4,1),
          "zoom: invalid base FOV and progress are safe");
        float v=0;for(int i=0;i<120;i++)v=CapturePolicy.Step(v,1,1/60f);
        C(v==1f&&CapturePolicy.Step(.5f,1,0)==.5f&&CapturePolicy.Step(float.NaN,1,.1f)==1f,"zoom: the smooth approach settles exactly and ignores a zero frame");
        float a=CapturePolicy.Step(0,1,1/30f),b=CapturePolicy.Step(CapturePolicy.Step(0,1,1/60f),1,1/60f);
        C(Near(a,b,.001f),"zoom: frame-rate independent (one 33 ms step = two 16 ms steps)");
        C(CapturePolicy.ExternalChange(80,75)&&!CapturePolicy.ExternalChange(75.01f,75)&&!CapturePolicy.ExternalChange(80,float.NaN),
          "fov: a foreign FOV write (settings, effects, other mod) is followed, not fought");
    }

    private static void Fps(Action<bool,string> C)
    {
        C(CapturePolicy.FpsLabel(0,1,0)=="FPS —"&&CapturePolicy.FpsLabel(30,0,0)=="FPS —","fps: no frames yet");
        string s=CapturePolicy.FpsLabel(72,.5f,.0075f);
        C(s.StartsWith("FPS 144 · 6.9 мс")&&!s.Contains("пик"),"fps: rate and frame time, no spike note for a steady window: "+s);
        C(CapturePolicy.FpsLabel(30,.5f,.1f).Contains("пик 100 мс"),"fps: a hitch is reported as the worst frame");
        C(CapturePolicy.Corner(9)==1&&CapturePolicy.Corner(2)==2,"fps: corner defaults to top right");
        C(CapturePolicy.BackgroundFps(15)==15&&CapturePolicy.BackgroundFps(7)==CapturePolicy.DefaultBackgroundFps,"background: only the offered rates");
        C(CapturePolicy.LimitBackground(true,false,false)&&!CapturePolicy.LimitBackground(true,true,false)&&!CapturePolicy.LimitBackground(false,false,false)&&
          !CapturePolicy.LimitBackground(true,false,true),"background: only unfocused, never during an FPS measurement");
    }

    private static void Settings(Action<bool,string> C)
    {
        var s=CapturePolicy.ParseSettings("# c\nfov.enabled=1\r\nfov.value = 95\nzoom.factor=abc\nunknown=1\n=2\nfps.show=true\n");
        C(s.Count==4&&CapturePolicy.Flag(s,"fov.enabled")&&CapturePolicy.Flag(s,"fps.show")&&!CapturePolicy.Flag(s,"zoom.enabled")&&
          CapturePolicy.Number(s,"fov.value",0)==95&&CapturePolicy.Number(s,"zoom.factor",4)==4,"capture.cfg: known keys only, bad numbers fall back");
        C(CapturePolicy.Line("fov.value",87.5f)=="fov.value=87.5"&&CapturePolicy.Line("zoom.enabled",true)=="zoom.enabled=1","capture.cfg: invariant lines");
        C(CapturePolicy.ParseSettings(null).Count==0,"capture.cfg: missing file");
    }

    private static void Names(Action<bool,string> C)
    {
        string clean=CapturePolicy.CleanName("  Моя   ферма\t<b>!</b> ");
        C(clean.StartsWith("Моя ферма ")&&clean.IndexOfAny(new[]{'<','>','\t'})<0&&!clean.Contains("  ")&&CapturePolicy.CleanName(new string('x',60)).Length==CapturePolicy.MaxSaveName&&CapturePolicy.CleanName(null)=="",
          "names: whitespace collapsed, no rich-text tags, length limit");
        C(CapturePolicy.SlotOfGame("20261013155400_5")==5&&CapturePolicy.SlotOfGame("20261013155400")==0&&CapturePolicy.SlotOfGame("x_1")==0&&CapturePolicy.SlotOfGame(null)==0,
          "names: slot from the native GameName");
        C(CapturePolicy.GameOfSaveFile("20261013155400_5_12")=="20261013155400_5"&&CapturePolicy.GameOfSaveFile("profile")==null&&CapturePolicy.GameOfSaveFile("20261013155400_5")==null,
          "names: GameName from a save file name");
        C(CapturePolicy.SlotOfCaption("Файл 3")==3&&CapturePolicy.SlotOfCaption(" File 12 ")==12&&CapturePolicy.SlotOfCaption("Файл")==0&&
          CapturePolicy.SlotOfCaption("12:30 3")==0&&CapturePolicy.SlotOfCaption("<b>Файл</b> 3")==0,"names: slot captions in any language, no times or tags");
        C(CapturePolicy.CaptionObject("SlotName/Header")&&!CapturePolicy.CaptionObject("DayCounter/Info")&&!CapturePolicy.CaptionObject("PlayTime/Info")&&!CapturePolicy.CaptionObject("Currency/Info"),
          "names: day, play time and money counters are never captions");
        C(CapturePolicy.SlotButtonObject("SaveGameSlotButton(Clone)")&&!CapturePolicy.SlotButtonObject("TextButton_NoneSelectable")&&!CapturePolicy.SlotButtonObject(null)&&
          CapturePolicy.CaptionObject("ButtonLabel",true)&&!CapturePolicy.CaptionObject("ButtonLabel")&&!CapturePolicy.CaptionObject("DayLabel",true),
          "names: slot buttons of the save list; their label may be called Button*, counters still excluded");
        C(CapturePolicy.ZoomKeys[4].Length<=3,"zoom: the side mouse button label fits its chip");
        var t0=new DateTime(2026,10,1,0,0,0,DateTimeKind.Utc);
        var slots=CapturePolicy.SlotGames(new[]{("20261001000000_1_3",t0),("20261001000000_1_4",t0.AddMinutes(5)),("20250101000000_1_9",t0.AddMinutes(-9)),
            ("20261002000000_2_1",t0),("profile",t0.AddDays(1)),("settings",t0)});
        C(slots.Count==2&&slots[1]=="20261001000000_1"&&slots[2]=="20261002000000_2","names: the newest game of a slot wins, other files ignored");
        var names=new Dictionary<string,string>{["20261001000000_1"]="Ферма",["bad\tname"]="x",["20261002000000_2"]="  "};
        string text=CapturePolicy.SerializeNames(names);
        var back=CapturePolicy.ParseNames(text);
        C(back.Count==1&&back["20261001000000_1"]=="Ферма"&&text.StartsWith("#"),"save-names.cfg: round trip, invalid and empty entries dropped");
        var tolerant=CapturePolicy.ParseNames("x\n\tname\n20261001000000_1\t<i>A</i>\r\n");
        C(tolerant.Count==1&&tolerant.Single().Value.Contains('A')&&tolerant.Single().Value.IndexOfAny(new[]{'<','>','\r'})<0,"save-names.cfg: tolerant parse");
    }

    private static void Placement(Action<bool,string> C)
    {
        C(CapturePolicy.FreeRange(8)==32&&CapturePolicy.FreeRange(20)==60&&CapturePolicy.FreeRange(90)==90&&CapturePolicy.FreeRange(float.NaN)==CapturePolicy.FreeRangeMax,
          "free placement: reach x4 up to 60 m, never shorter than vanilla");
        C(CapturePolicy.FreePlacementLabel(true).EndsWith("ВКЛ")&&CapturePolicy.FreePlacementLabel(false).EndsWith("ВЫКЛ"),"free placement: label");
        C(MultiplayerPolicy.Classify("free placement")==CoopKind.Cheat&&MultiplayerPolicy.Known("camera fov")&&MultiplayerPolicy.Known("smooth zoom")&&
          MultiplayerPolicy.Known("smooth physics")&&MultiplayerPolicy.Known("save name")&&MultiplayerPolicy.Classify("camera fov")==CoopKind.Free,
          "coop: free placement is a cheat, camera/physics/names are local");
    }

    private static void Sources(Action<bool,string> C,string root)
    {
        string dir=Path.Combine(root,"src","creator");
        string R(string f)=>File.ReadAllText(Path.Combine(dir,f));
        string mod=R("ToolkitMod.cs"),bridge=R("MultiplayerBridge.cs"),tools=R("CaptureTools.cs"),free=R("FreePlacementTools.cs"),names=R("SaveNameTools.cs"),ui=R("CaptureUi.cs");
        C(mod.Contains("(\"capture\",Capture.Tick)")&&mod.Contains("(\"save names\",SaveNames.Tick)")&&mod.Contains("(\"camera fov\",Capture.LateTick)"),
          "wiring: capture and save names tick every frame, the FOV after the game's update");
        C(mod.Contains("FreePlacement?.Reset()")&&mod.Contains("Capture?.ResetSession()")&&mod.Contains("Capture?.Shutdown()"),"wiring: restored on save change and quit");
        C(mod.Contains("_ui.DrawFpsCounter()")&&ui.Contains("_app.Cinema.HideHud"),"wiring: FPS counter drawn with and without the menu, hidden in clean frame");
        C(bridge.Contains("_app.FreePlacement.Set(false)"),"coop: free placement switched off without cheats");
        C(tools.Contains("HarmonyLib.Priority.Last")&&tools.Contains("HookCoexistence.Track")&&tools.Contains("interpolation != RigidbodyInterpolation.None"),
          "smooth physics: cooperative hook, other choices kept");
        C(!free.Contains("harmony.Patch")&&!free.Contains("HarmonyLib")&&free.Contains("Restore()"),"free placement: native metadata leased, no patches");
        C(!names.Contains(".sav\", \"")&&!names.Contains("File.Delete")&&!names.Contains("StoreGame")&&names.Contains("label.Native"),
          "save names: save files never written, native captions handed back");
        C(mod.Contains("\"0.8.1\"")&&mod.Contains("AssemblyVersion(\"0.8.1.0\")"),"version 0.8.1");
    }
}
