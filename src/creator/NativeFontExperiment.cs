using System;
using System.IO;
using System.Linq;
using CreatorToolkit.NativeFontResearch;
using Il2CppTMPro;
using UnityEngine;

namespace CreatorToolkit;

// Opt-in audit on an owned save only. This does not replace the mod's font
// until actual PNGs prove both native glyphs and IMGUI/Canvas layering.
internal sealed partial class FeatureSuite
{
    private NativeTmpLabelProbe _nativeFontProbe;
    private Rect _nativeFontRect;
    private bool _nativeFontSwatch;

    private void RetireNativeFontExperiment()
    {
        _nativeFontSwatch=false;
        _nativeFontProbe?.Dispose();_nativeFontProbe=null;
    }
    internal void DrawNativeFontSwatch()
    {
        if(_nativeFontProbe==null)return;
        bool valid=_phase==2&&_recovery!=null&&_app.Ready&&
            _app.Session==_isolatedSession&&_mainModel==null&&
            _nativePediaReference!=null&&_nativePediaReference.activeInHierarchy&&
            !_app.MenuOpen&&_app.Pending==null;
        if(valid){try{GuardCopy();}catch{valid=false;}}
        if(!_nativeFontProbe.Maintain(valid)||!_nativeFontSwatch||
            Event.current?.type!=EventType.Repaint)return;
        GuardCopy();
        var matrix=GUI.matrix;var color=GUI.color;int depth=GUI.depth;
        try
        {
            GUI.matrix=Matrix4x4.identity;GUI.depth=0;
            GUI.color=new Color(248/255f,244/255f,234/255f,1);
            var r=_nativeFontRect;
            GUI.DrawTexture(new Rect(r.x-12,r.y-8,r.width+24,r.height+16),Texture2D.whiteTexture);
        }
        finally{GUI.matrix=matrix;GUI.color=color;GUI.depth=depth;}
    }
    private void BuildNativeFontExperimentSteps()
    {
        Add("NativeFont / create one genuine native TMP specimen on owned canvas",()=>{
            GuardCopy();RetireNativeFontExperiment();
            Require(NativePediaReady(),"Actual native Pedia must remain visible for the font experiment.");
            var donor=_nativePediaReference.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(t=>t!=null&&!t.WasCollected&&t.isActiveAndEnabled&&
                    t.font!=null&&t.font.name=="NotoSans-Medium (Cyrillic)");
            Require(donor!=null,"No visible genuine native Medium Cyrillic donor.");
            Require(NativeTmpLabelProbe.TryCreate(donor,_nativePediaReference,1200,
                out _nativeFontProbe,out var setupFailure),setupFailure);
            float width=Mathf.Min(760,Screen.width-64);
            _nativeFontRect=new Rect((Screen.width-width)/2,96,width,56);
            Require(_nativeFontProbe.TryPresent(NativeTmpLabelProbe.ProbeText,
                _nativeFontRect,26,out var textFailure),textFailure);
            File.WriteAllText(Path.Combine(_app.DataDirectory,"native-tmp-specimen-proof.json"),
                _nativeFontProbe.SnapshotJson());
        },.4f);
        Add("NativeFont / capture actual native TMP specimen without IMGUI swatch",()=>{
            GuardCopy();Require(_nativeFontProbe!=null&&_nativeFontProbe.Maintain(true),
                "Owned TMP specimen or native donor was lost.");
            ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"native-tmp-specimen-clear.png"));
        },.3f);
        Add("NativeFont / overlap owned TMP specimen with opaque IMGUI swatch",()=>{
            GuardCopy();Require(_nativeFontProbe!=null&&_nativeFontProbe.Maintain(true),
                "Owned TMP specimen is unavailable for layering experiment.");
            _nativeFontSwatch=true;
        },.3f);
        Add("NativeFont / capture layering and retire owned specimen",()=>{
            GuardCopy();Require(_nativeFontProbe!=null&&_nativeFontProbe.Maintain(true),
                "Owned TMP specimen is unavailable for layering capture.");
            ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"native-tmp-specimen-imgui.png"));
            File.WriteAllText(Path.Combine(_app.DataDirectory,"native-tmp-layering-proof.json"),
                _nativeFontProbe.SnapshotJson());
        },.3f);
        Add("NativeFont / dispose only owned experiment resources",()=>{
            GuardCopy();RetireNativeFontExperiment();
        },.3f);
        Add("NativeFont / deferred owned canvas cleanup",()=>{
            GuardCopy();
            Require(!Resources.FindObjectsOfTypeAll<Canvas>().Any(c=>c!=null&&
                c.name=="CreatorToolkit.NativeFontResearch.OwnedCanvas"),
                "Owned native-font experiment canvas survived disposal.");
        });
    }
}