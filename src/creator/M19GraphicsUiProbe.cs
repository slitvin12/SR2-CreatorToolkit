using System;
using System.IO;
using System.Text.Json;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace CreatorToolkit;
// Explicit short native-app UI test, not OS input. Starts only with no graphics lease.
internal static class M19GraphicsUiProbe
{
    private static ToolkitMod _app;private static Mouse _mouse;private static Vector2 _original;private static bool _enabled;
    private static IntPtr _session;private static int _stage=-1,_lodDraft;private static float _next,_beforeLod,_beforeFar,_appliedLod;
    internal static void Start(ToolkitMod app)
    {
        if(_stage>=0||!app.Ready||app.Optimization.Active||app.Map.NativeMapOpen||NativeUiPriority.HasNativeFocus)throw new InvalidOperationException("Graphics UI probe requires idle gameplay and original graphics.");
        var mouse=Mouse.current;if(mouse==null||mouse.leftButton.isPressed||mouse.rightButton.isPressed)throw new InvalidOperationException("Mouse unavailable or held.");
        _app=app;_session=app.Session;_mouse=mouse;_original=mouse.position.ReadValue();_enabled=mouse.enabled;
        _beforeLod=QualitySettings.lodBias;_beforeFar=Camera.main.farClipPlane;
        if(!_enabled)InputSystem.EnableDevice(mouse);
        app.UiForTests.SliderTestInput=true;app.UiForTests.ShowGraphicsTabForTest(1);if(!app.MenuOpen)app.Input.Open();
        _stage=0;_next=Time.unscaledTime+.6f;
    }
    private static unsafe void Queue(Vector2 point,bool held)
    {
        var buffer=StateEvent.From(_mouse,out InputEventPtr input,Allocator.Temp);
        try{InputControlExtensions.WriteValueIntoEvent<Vector2>(_mouse.position,point,input);InputControlExtensions.WriteValueIntoEvent<float>(_mouse.leftButton,held?1:0,input);InputSystem.QueueEvent(input);}finally{buffer.Dispose();}
    }
    private static void Pointer(float x,float y,bool held)
    {
        float scale=Mathf.Max(.35f,Mathf.Min(1.2f,Mathf.Min((Screen.width-48f)/1160f,(Screen.height-48f)/820f)));
        Queue(new Vector2((Screen.width-1160*scale)/2+x*scale,Screen.height-((Screen.height-820*scale)/2+y*scale)),held);
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    internal static void Tick()
    {
        if(_stage<0||Time.unscaledTime<_next)return;
        try
        {
            Check(_app.Ready&&_app.Session==_session,"Session changed");var ui=_app.UiForTests;
            switch(_stage++)
            {
                case 0:Check(ui.OptimizationVisibleForTest,"Graphics menu not visible");Pointer(100,386,true);break;
                case 1:Pointer(290,386,true);break;
                case 2:_lodDraft=ui.GraphicsDraftForTest.LodPercent;Check(_lodDraft>40&&_lodDraft<65,"Native LOD drag did not update draft");Check(!_app.Optimization.Active&&QualitySettings.lodBias==_beforeLod,"Draft slider wrote quality before explicit Apply");Pointer(290,386,false);break;
                case 3:Pointer(950,242,true);break;
                case 4:Pointer(950,242,false);break;
                case 5:Check(ui.GraphicsSubpageForTest==2,"Post-effects tab click failed");Pointer(326,547,true);break;
                case 6:Pointer(326,547,false);break;
                case 7:Check(ui.GraphicsDraftForTest.DisabledEffects==M19Effects.MotionBlur,"Independent motion blur button changed wrong mask");Pointer(130,666,true);break;
                case 8:Pointer(130,666,false);break;
                case 9:
                    _appliedLod=QualitySettings.lodBias;var applied=_app.Optimization.DetailedSettings;
                    Check(applied.LodPercent==_lodDraft&&applied.DisabledEffects==M19Effects.MotionBlur&&!applied.HideDecor&&!applied.DisableShadows,"Custom Apply did not commit exact draft");
                    Check(Mathf.Abs(_appliedLod-M19GraphicsPolicy.Lod(_beforeLod,_lodDraft))<.0001f,"Native LOD readback differs");
                    Check(Mathf.Abs(Camera.main.farClipPlane-_beforeFar)<.1f,"LOD/effect-only Apply modified camera range");Finish(true,null);return;
            }
            _next=Time.unscaledTime+.3f;
        }
        catch(Exception e){Finish(false,e.Message);}
    }
    internal static void Cancel(){if(_stage>=0)Finish(false,"cancelled");}
    private static void Finish(bool success,string error)
    {
        try
        {
            if(_app.Ready&&_app.Session==_session)_app.Optimization.Restore(false);
            if(_mouse!=null){Queue(_original,false);if(!_enabled)InputSystem.DisableDevice(_mouse);}
            _app.UiForTests.SliderTestInput=false;_app.UiForTests.ClearInteraction();
            File.WriteAllText(Path.Combine(_app.DataDirectory,"m19-ui-proof.json"),JsonSerializer.Serialize(new{Success=success,Error=error,LodDraft=_lodDraft,BeforeLod=_beforeLod,AppliedLod=_appliedLod,RestoredLod=QualitySettings.lodBias,BeforeFar=_beforeFar,RestoredFar=Camera.main?.farClipPlane,Scope="Native InputSystem LOD drag, deferred apply, actual post-effect tab/button, custom Apply, native scalar readback and restore. No OS automation or save writes."}));
        }
        finally{_stage=-1;_mouse=null;_app=null;}
    }
}
