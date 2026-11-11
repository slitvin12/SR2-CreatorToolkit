using System;
using System.IO;
namespace CreatorToolkit;
// 0.8.8 offline regression: one jelly button for the whole toolkit. Every rounded surface is one
// seamless nine-slice draw scaled to its radius, a hover never swaps a paint, one halo serves every
// clickable, and no decorative bars sit beside a toggle or under the open tab. Source wiring and pure
// policy only; no Unity, no game.
internal static class M33JellyRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool b,string s)=>check(b,"0.8.8 / "+s);
        string dir=Path.Combine(root,"src","creator");
        string R(string name){string p=Path.Combine(dir,name);return File.Exists(p)?File.ReadAllText(p).Replace("\r\n","\n"):"";}
        string ui=R("ToolkitUi.cs"),atlas=R("AtlasUi.cs"),explorer=R("ExplorerUi.cs"),chrome=R("MapChromeUi.cs"),typography=R("NativeTypography.cs");
        C(UiThemePolicy.SurfaceBorder==20f&&Math.Abs(UiThemePolicy.SurfaceScale(20f)-1f)<1e-6f&&Math.Abs(UiThemePolicy.SurfaceScale(5f)-.25f)<1e-6f&&
          UiThemePolicy.SurfaceScale(0f)>0f&&UiThemePolicy.SurfaceScale(float.NaN)>0f&&UiThemePolicy.SurfaceScale(-3f)>0f,"surface scale is radius/20 and never zero");
        bool fits=true;
        foreach(float h in new[]{4f,5f,12f,22f,26f,28f,36f,40f,44f,48f,64f,120f})
        foreach(float w in new[]{8f,24f,40f,56f,104f,250f})
        foreach(float wanted in new[]{2.5f,5f,11f,12f,14f,16f,20f,h*.5f})
        {
            float r=UiThemePolicy.CapsuleRadius(w,h,wanted);
            int border=Math.Max(0,(int)Math.Floor(r));
            if(r>=.5f&&(w<2*border||h<2*border))fits=false;
        }
        C(fits,"the exact-rect nine-slice border never overlaps its corners, at any size");
        C(UiThemePolicy.JellyRadius(200,44)==20f&&UiThemePolicy.JellyRadius(60,28)==14f&&UiThemePolicy.JellyRadius(20,10)==5f&&
          UiThemePolicy.JellyRadius(float.NaN,40)==0f,"one radius rule for faces, shadows and halos");
        C(UiThemePolicy.HaloAmount(UiThemePolicy.HoverLift,false)==1f&&UiThemePolicy.HaloAmount(0,false)==0f&&UiThemePolicy.HaloAmount(3,true)==1f&&
          UiThemePolicy.HaloAmount(float.NaN,false)==0f&&UiThemePolicy.HaloAmount(UiThemePolicy.HoverLift*.5f,false)==.5f,"the halo fades in with the hover lift");
        string button=Section(ui,"private bool Button(Rect rect,GUIContent content,GUIStyle style,Rect? visualBounds=null)","\n    }");
        C(button.Length>0&&button.Contains("JellyBase(visual,glow);")&&button.Contains("var surface=FacePaint(style);")&&!button.Contains("L.Box(")&&
          !button.Contains("hover.background")&&!button.Contains("CopyStyle(")&&ui.Contains("_button.hover.background = deep;"),
          "buttons: the paint is the role; hover and press never swap it");
        string slime=Section(ui,"private bool SlimeSwitch(Rect rect,bool enabled)","\n    }");
        C(slime.Contains("JellyBase(rect,")&&slime.Contains("PaintFace(rect,")&&!slime.Contains("L.Box("),"slime switches: the jelly halo, shadow and cross-fading face");
        string toggle=Section(ui,"private bool Toggle(Rect rect,string title,bool enabled,string detail)","\n    }");
        C(toggle.Length>0&&!toggle.Contains("Fill(")&&!ui.Contains("TabMarker(")&&!ui.Contains("_tabMarkX"),"no decorative bars: none beside a toggle, none under the open tab");
        int boxGlows=0;
        foreach(var text in new[]{ui,atlas,explorer,chrome})
            for(int at=text.IndexOf("_glowBox)",StringComparison.Ordinal);at>=0;at=text.IndexOf("_glowBox)",at+1,StringComparison.Ordinal))boxGlows++;
        C(boxGlows==1&&ui.Contains("L.Box(new Rect(25,14,69,69),GUIContent.none,_glowBox);")&&!chrome.Contains("_glowBox")&&!explorer.Contains("_glowBox")&&!atlas.Contains("_glowBox"),
          "one halo: every hover glow is the seamless halo (the header logo keeps its static ring)");
        C(atlas.Contains("else HoverHalo(row,14f);")&&explorer.Contains("HoverHalo(tile,16f);")&&explorer.Contains("HoverHalo(close,14f);")&&
          chrome.Contains("UiThemePolicy.HaloAmount(offset,held)")&&ui.Contains("JellyHalo(rect,UiThemePolicy.HaloAmount(offset,held),radius,spread);"),
          "rows, tiles, links, chips, segments and map settings hover like buttons");
        C(!typography.Contains("_hoverStyles")&&!typography.Contains("_pressedStyles")&&!ui.Contains("_hoverStyles")&&ui.Contains("_surfacePaint.Clear();"),
          "no per-style hover copies; the paint styles are dropped on dispose");
    }
    private static string Section(string text,string start,string end)
    {
        int a=text.IndexOf(start,StringComparison.Ordinal);if(a<0)return "";
        int b=text.IndexOf(end,a+start.Length,StringComparison.Ordinal);
        return b<0?text.Substring(a):text.Substring(a,b-a);
    }
}
