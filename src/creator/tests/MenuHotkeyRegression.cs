using System;
using System.IO;
using System.Linq;

namespace CreatorToolkit;

// 0.8.1: rebindable menu key, crisp minimap player marker, «Навигация» preview fixes.
internal static class MenuHotkeyRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool ok,string name)=>check(ok,"0.8.1 hotkey/marker: "+name);
        string dir=Path.Combine(root,"src","creator");
        string R(string name){string p=Path.Combine(dir,name);return File.Exists(p)?File.ReadAllText(p):"";}
        // Menu key policy.
        C(MenuHotkeyPolicy.Default=="Insert"&&MenuHotkeyPolicy.Keys[0]=="Insert"&&MenuHotkeyPolicy.Keys.Distinct().Count()==MenuHotkeyPolicy.Keys.Length,"Insert is the default and first, names unique");
        C(new[]{"Escape","F1","Tab","Enter","Space","Backspace","LeftArrow","UpArrow","LeftShift","LeftCtrl","LeftAlt","CapsLock","NumLock","PrintScreen"}.All(k=>!MenuHotkeyPolicy.Allowed(k)),
          "Esc, F1, Tab, Enter, Space, arrows, modifiers and lock keys are never menu keys");
        C(MenuHotkeyPolicy.Allowed("F5")&&MenuHotkeyPolicy.Allowed("Home")&&MenuHotkeyPolicy.Allowed("M")&&MenuHotkeyPolicy.Allowed("Backquote")&&MenuHotkeyPolicy.Allowed("Numpad0"),"F-keys, navigation keys, letters and numpad allowed");
        C(MenuHotkeyPolicy.Parse("# x\r\nmenu.key=F5\r\n")=="F5"&&MenuHotkeyPolicy.Parse("menu.key= home ")=="Home"&&MenuHotkeyPolicy.Parse("menu.key=Escape")=="Insert"&&
          MenuHotkeyPolicy.Parse("")=="Insert"&&MenuHotkeyPolicy.Parse(null)=="Insert"&&MenuHotkeyPolicy.Parse("menu.key=")=="Insert","parse: case-insensitive, unknown or forbidden falls back to Insert");
        C(MenuHotkeyPolicy.Parse(MenuHotkeyPolicy.Serialize("PageDown"))=="PageDown"&&MenuHotkeyPolicy.Serialize("Space").Contains("menu.key=Insert"),"serialize round trip");
        C(MenuHotkeyPolicy.Display("PageUp")=="Page Up"&&MenuHotkeyPolicy.Display("Digit7")=="7"&&MenuHotkeyPolicy.Display("NumpadPlus")=="Num +"&&MenuHotkeyPolicy.Display("Numpad3")=="Num 3"&&
          MenuHotkeyPolicy.Display("Backquote")=="`"&&MenuHotkeyPolicy.Display("K")=="K"&&MenuHotkeyPolicy.Footer("F6")=="F6  ·  Esc"&&MenuHotkeyPolicy.Footer("Insert")=="Insert  ·  Esc","display names and footer");
        C(!MenuHotkeyPolicy.Typing("Insert")&&!MenuHotkeyPolicy.Typing("F5")&&!MenuHotkeyPolicy.Typing("F12")&&!MenuHotkeyPolicy.Typing("PageDown")&&
          MenuHotkeyPolicy.Typing("F")&&MenuHotkeyPolicy.Typing("Home")&&MenuHotkeyPolicy.Typing("Delete")&&MenuHotkeyPolicy.Typing("Digit1")&&MenuHotkeyPolicy.Typing("Numpad1"),
          "typing/editing keys wait while a text field has focus, F-keys and Insert never do");
        string hotkey=R("MenuHotkey.cs"),toolkit=R("ToolkitMod.cs"),ui=R("ToolkitUi.cs"),info=R("InfoUi.cs"),tests=R(Path.Combine("tests","CreatorToolkit.Tests.csproj"));
        C(MenuHotkeyPolicy.Keys.All(k=>hotkey.Contains("\""+k+"\"=>kb."+char.ToLowerInvariant(k[0])+k.Substring(1)+"Key")),"every allowed key maps to its Keyboard control");
        C(hotkey.Contains("kb.escapeKey.wasPressedThisFrame){Capturing=false;")&&hotkey.Contains("File.Replace(temp,file,null)")&&hotkey.Contains("kb.leftCtrlKey.isPressed"),"capture: Esc cancels, saved atomically, Ctrl/Alt + key ignored");
        C(toolkit.Contains("if (Hotkey.Capturing)")&&toolkit.Contains("Hotkey.Pressed(kb) && !(MenuOpen && Hotkey.Typing && (_ui?.TextFieldFocused ?? false))")&&
          toolkit.Contains("Hotkey?.CancelCapture();")&&!toolkit.Contains("kb.insertKey.wasPressedThisFrame"),"host: the menu toggles on the chosen key only, capture cancelled on close");
        C(ui.Contains("\"Клавиша меню\"")&&ui.Contains("hotkey.StartCapture()")&&ui.Contains("hotkey.Reset()")&&ui.Contains("_app.Hotkey?.Footer")&&
          ui.Contains("доступно по \"+(_app.Hotkey?.Display")&&info.Contains("Открыть меню: \"+(_app.Hotkey?.Display"),"UI: «Удобство» row, footer and hints follow the key");
        C(tests.Contains("..\\MenuHotkeyPolicy.cs"),"tests: the hotkey policy is linked");
        // Player marker: sized from the disc, resampled to the drawn size.
        C(Math.Abs(M28PlayerMarkerPolicy.MarkerScale(47.1f,50.1f,108f,118.8f)-118.8f*.4f/47.1f)<1e-4f,"marker: the portrait frame is 40% of the disc radius (M)");
        C(Math.Abs(M28PlayerMarkerPolicy.MarkerScale(47.1f,50.1f,108f,86.4f)-80.4f/108f)<1e-4f,"marker: the view cone still fits a small disc");
        C(Math.Abs(M28PlayerMarkerPolicy.MarkerScale(47.1f,50.1f,108f,400f)-52f/47.1f)<1e-4f&&Math.Abs(M28PlayerMarkerPolicy.MarkerScale(0,50.1f,0,118.8f)-M28PlayerMarkerPolicy.TargetSize/50.1f)<1e-4f,
          "marker: capped on huge discs, body fit without a front part");
        uint red=0xFF0000FFu;
        var flat=M28PlayerMarkerPolicy.Downsample(Enumerable.Repeat(red,16).ToArray(),4,4,2,2);
        C(flat.Length==4&&flat.All(p=>p==red),"downsample: a flat colour stays exact");
        var edge=M28PlayerMarkerPolicy.Downsample(new[]{0xFFFFFFFFu,0x00000000u},2,1,1,1);
        C(edge.Length==1&&(edge[0]&0xFFFFFFu)==0xFFFFFFu&&edge[0]>>24==128,"downsample: transparent texels do not darken the edge (alpha 50 %)");
        var third=M28PlayerMarkerPolicy.Downsample(new[]{0xFF000000u,0xFF0000FFu,0xFF0000FFu},3,1,2,1);
        C(third.Length==2&&(third[0]&255)==85&&(third[1]&255)==255&&third.All(p=>p>>24==255),"downsample: fractional footprints are area-weighted");
        C(M28PlayerMarkerPolicy.Downsample(new[]{red},1,1,4,4).Length==1&&M28PlayerMarkerPolicy.Downsample(null,4,4,2,2).Length==0&&
          M28PlayerMarkerPolicy.Downsample(new uint[3],2,2,1,1).Length==0,"downsample: never upsamples, rejects short input");
        string marker=R("M28PlayerMarker.cs"),mini=R("MiniMapUi.cs");
        C(marker.Contains("M28PlayerMarkerPolicy.MarkerScale(front,_body,_extent,radius)")&&marker.Contains("SizedTexture(part,w*pixels,h*pixels,out int tw,out int th)")&&
          marker.Contains("Upload(pixels,tw,th,false)")&&marker.Contains("GUI.DrawTexture(new Rect(left,top,w,h),texture)")&&
          marker.Contains("Matrix4x4.TRS(pivot,Quaternion.Euler(0f,0f,angle),Vector3.one)*Matrix4x4.Translate(-pivot)")&&marker.Contains("foreach(var sized in part.Resampled)if(sized.Texture!=null"),
          "marker: drawn at its real place (no quadrant clip in the preview), resampled without mips, resamples destroyed");
        // «Навигация» preview: S/M/L and an opaque world under the disc.
        C(Math.Abs(M29MiniMapLayout.PreviewFraction(2,1080)-1f)<1e-5f&&Math.Abs(M29MiniMapLayout.PreviewFraction(1,1080)-237.6f/324f)<1e-4f&&
          M29MiniMapLayout.PreviewFraction(0,1080)<M29MiniMapLayout.PreviewFraction(1,1080)&&M29MiniMapLayout.PreviewFraction(0,1080)>=.5f,"preview: the disc keeps the S/M/L proportions");
        C(mini.Contains("M29MiniMapLayout.PreviewFraction(look.Size,Screen.height)")&&mini.Contains("GUI.color=new Color(.30f,.46f,.42f,fade);GUI.DrawTexture(box,MiniDiscTexture(false));"),
          "preview: sized by S/M/L over an opaque world disc");
    }
}
