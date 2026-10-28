using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Il2CppMonomiPark.SlimeRancher.UI.Framework.Displays;

namespace CreatorToolkit;

// Opt-in native InputSystem events only; never moves the OS pointer or clicks another app.
internal sealed class NativeUiProbe
{
    private readonly ToolkitMod _app;
    private readonly List<string> _rows=new();
    private readonly List<object> _hotkeyRows=new();
    private Mouse _mouse;
    private Vector2 _original;
    private bool _oldEnabled,_pressed;
    private float _oldSpeed,_deadline;
    private float _oldJumpHeight;
    private float _nativeCloseDeadline;
    private string _nativeF1Name="";
    private bool _nativeF1Requested;
    private bool _oldJump,_oldItems;
    private string _oldDraft="";
    private int _textCommitsBefore,_imePreviewsBefore,_textQueuedFrame,_textEventFrame,_textBoundFrame;
    private int _stage=-1,_hoverBaseline,_heldBaseline;
    private int _latencyIndex,_clickRowsBefore;
    private float TabX=>30+(1100f-7*8)/8/2+(_latencyIndex%8)*((1100f-7*8)/8+8);
    internal string Status="idle";
    internal bool Active=>_stage>=0;
    internal NativeUiProbe(ToolkitMod app)=>_app=app;
    internal void Start()
    {
        _app.Audit.GuardStressCopy();
        if(Active||_app.Convenience.Profile!=0)throw new InvalidOperationException("Native UI probe/profile already running.");
        _mouse=Mouse.current;
        if(_mouse==null||_mouse.leftButton.isPressed||_mouse.rightButton.isPressed)throw new InvalidOperationException("Mouse held/unavailable; probe refuses to override user input.");
        _rows.Clear();_hotkeyRows.Clear();_nativeF1Name="";_nativeF1Requested=false;
        _original=_mouse.position.ReadValue();_oldEnabled=_mouse.enabled;_oldSpeed=_app.Player.SpeedMultiplier;
        _oldJump=_app.Player.HighJump;_oldJumpHeight=_app.Player.JumpHeightMultiplier;_oldItems=_app.Player.InfiniteItems;
        _oldDraft=_app.Custom.DraftName;
        _textCommitsBefore=_app.UiForTests.TextCommitsForTest;_imePreviewsBefore=_app.UiForTests.ImePreviewsForTest;
        if(!_mouse.enabled)InputSystem.EnableDevice(_mouse);
        _stage=0;_deadline=Time.unscaledTime;Status="running";
    }
    private Vector2 Window(float x,float y)
    {
        float s=Mathf.Max(.35f,Mathf.Min(1.2f,Mathf.Min((Screen.width-48f)/1160f,(Screen.height-48f)/820f)));
        return new Vector2((Screen.width-1160*s)/2+x*s,Screen.height-((Screen.height-820*s)/2+y*s));
    }
    private unsafe void Pointer(float x,float y,bool down)
    {
        var buffer=StateEvent.From(_mouse,out InputEventPtr ptr,Allocator.Temp);
        try
        {
            InputControlExtensions.WriteValueIntoEvent<Vector2>(_mouse.position,Window(x,y),ptr);
            InputControlExtensions.WriteValueIntoEvent<float>(_mouse.leftButton,down?1f:0f,ptr);
            InputSystem.QueueEvent(ptr);_pressed=down;
        }
        finally{buffer.Dispose();}
    }
    private void Check(bool condition,string text)
    {
        _rows.Add((condition?"PASS ":"FAIL ")+text);
        if(!condition)throw new InvalidOperationException(text);
    }
    private void Capture(string name)=>ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"ui-"+name+".png"));
    private bool NativeF1PediaFocused()
    {
        var state=UIDisplayManager._instance?._lastState;
        return state?.TopFocus?.Name==_nativeF1Name&&!state.TopFocus.IsClosing||
            state?.TopPossiblyFocusable?.Name==_nativeF1Name&&!state.TopPossiblyFocusable.IsClosing;
    }
    private void HotkeyRow(string phase)
    {
        _app.Audit.GuardStressCopy();
        var keyboard=Keyboard.current;
        _hotkeyRows.Add(new{Phase=phase,Frame=Time.frameCount,Stage=_stage,
            ModOpen=_app.MenuOpen,InputActive=_app.Input.Active,TimeScale=Time.timeScale,
            CursorLock=Cursor.lockState.ToString(),Cursor.visible,
            KeyboardEnabled=keyboard?.enabled,F1Held=keyboard?.f1Key.isPressed,
            F1Pressed=keyboard?.f1Key.wasPressedThisFrame,InsertHeld=keyboard?.insertKey.isPressed,
            InsertPressed=keyboard?.insertKey.wasPressedThisFrame,
            NativeF1Name=_nativeF1Name,NativeF1Focused=NativeF1PediaFocused(),
            NativeOwnership=NativeUiPriority.Diagnostics(),RecordedUtc=DateTime.UtcNow.ToString("o")});
        File.WriteAllText(Path.Combine(_app.DataDirectory,"native-keyboard-window-proof.json"),
            JsonSerializer.Serialize(new{CopyOnly=true,
                Scope="Actual InputSystem F1/Insert events and bounded native display lifecycle; no OS-input certificate or forced pause/cursor.",
                Rows=_hotkeyRows},new JsonSerializerOptions{WriteIndented=true}));
    }
    internal void Tick()
    {
        if(!Active||Time.unscaledTime<_deadline)return;
        try
        {
            if(!_app.Ready)throw new InvalidOperationException("Save/scene changed during UI probe.");
            switch(_stage++)
            {
                case 0:_app.ShowTabForTest(0);_app.Input.Open();break;
                case 1:Pointer(662,311,true);break;
                case 2:Pointer(1010,311,true);break;
                case 3:Check(_mouse.leftButton.isPressed&&Math.Abs(_app.Player.SpeedMultiplier-8.2f)<.08f,"speed slider followed held native LMB to 80% of the 1–10 range");Pointer(1010,311,false);break;
                case 4:Capture("drag-player");break;
                case 5:_app.UiForTest(1,0,1);break;
                case 6:Pointer(752,496,true);break;
                case 7:Pointer(1074,496,true);break;
                case 8:Check(_app.UiQuantityForTest>=22&&_app.UiQuantityForTest<=24,"spawn quantity followed native LMB drag to 90%");Pointer(1074,496,false);break;
                case 9:Capture("drag-quantity");break;
                case 10:Pointer(646,346,true);break;
                case 11:Pointer(646,686,true);break;
                case 12:Check(_app.UiScrollForTest>1000,"catalog scrollbar followed held native LMB");Pointer(646,686,false);break;
                case 13:Check(Math.Abs(_app.UiScrollForTest/48f-Mathf.Round(_app.UiScrollForTest/48f))<.001f,"scrollbar release snapped to complete rows");Capture("drag-scroll");break;
                case 14:_app.UiForTest(1,2,1);break;
                case 15:Capture("largos");break;
                case 16:_app.UiForTest(1,3,1);break;
                case 17:Capture("experiments");break;
                case 18:_app.UiForTest(7,0,1);break;
                case 19:Capture("convenience-default");break;
                case 20:Pointer(174,324,true);break;
                case 21:Pointer(174,324,false);break;
                case 22:Check(_app.Convenience.Profile==1&&_app.Player.GodMode&&_app.Player.InfiniteEnergy&&Math.Abs(_app.Player.SpeedMultiplier-2f)<.01f&&_app.Convenience.ShowHud,"native LMB enabled explorer profile");Capture("convenience-explorer");break;
                case 23:Pointer(424,324,true);break;
                case 24:Pointer(424,324,false);break;
                case 25:Check(_app.Convenience.Profile==2&&_app.Player.Flight&&_app.Cinema.HideHud&&_app.World.FreezeTime&&_app.World.FreezeMobs,"native LMB switched to clean frame profile");Capture("convenience-frame");break;
                case 26:Pointer(299,440,true);break;
                case 27:Pointer(299,440,false);break;
                case 28:Check(_app.Convenience.Profile==0&&!_app.Player.Flight&&!_app.Cinema.HideHud&&!_app.World.FreezeTime&&!_app.World.FreezeMobs&&Math.Abs(_app.Player.SpeedMultiplier-8.2f)<.08f,"native LMB restored exact pre-profile settings");break;
                case 29:_app.ShowTabForTest(0);_hoverBaseline=_app.UiForTests.JellyHoverRepaintsForTest;Pointer(98,126,false);break;
                case 30:
                    Check(_app.UiForTests.JellyHoverRepaintsForTest>_hoverBaseline&&
                        _app.UiForTests.HoverLiftForTest>2.5f&&_app.UiForTests.HoverLiftForTest<=4f,
                        "native hovered bookmark painted a bounded upward spring lift");
                    Capture("jelly-tab-hover");break;
                case 31:_heldBaseline=_app.UiForTests.JellyHeldRepaintsForTest;Pointer(98,126,true);break;
                case 32:Check(_mouse.leftButton.isPressed&&_app.UiForTests.JellyHeldRepaintsForTest>_heldBaseline,"native held bookmark painted compressed one-pixel jelly shadow");Capture("jelly-tab-pressed");break;
                case 33:Pointer(98,126,false);break;
                case 34:_app.ShowTabForTest(1);break;
                case 35:Pointer(895,183,true);break;
                case 36:Pointer(895,183,false);break;
                case 37:Check(_app.UiForTests.CreationSectionVisibleForTest(1)&&_app.Care.Inspect,"native LMB opened slime care and enabled value-only inspection");Capture("care-tab");break;
                case 38:Pointer(150,239,true);break;
                case 39:Pointer(150,239,false);break;
                case 40:Check(!_app.Care.Inspect,"native LMB disabled slime inspection");break;
                case 41:Pointer(1048,183,true);break;
                case 42:Pointer(1048,183,false);break;
                case 43:Check(_app.UiForTests.CreationSectionVisibleForTest(2),"native LMB opened gadget catalog");Capture("gadgets-tab");break;
                case 44:Pointer(740,183,true);break;
                case 45:Pointer(740,183,false);break;
                case 46:Check(_app.UiForTests.CreationSectionVisibleForTest(0),"native LMB returned to unchanged actor catalog");break;
                case 47:
                    _app.UiForTests.ShowTabForTest(7);_latencyIndex=0;
                    _clickRowsBefore=_app.UiForTests.PaintTelemetry.Rows.FindAll(r=>r.NativeClick).Count;break;
                case 48:Pointer(TabX,126,true);break;
                case 49:Pointer(TabX,126,false);break;
                case 50:
                    Check(_app.UiForTests.CurrentTabForTest==_latencyIndex%8,"native tab round "+(_latencyIndex/8+1)+" index "+(_latencyIndex%8));
                    _latencyIndex++;if(_latencyIndex<16)_stage=48;break;
                case 51:
                    var timings=_app.UiForTests.PaintTelemetry.Rows;
                    Check(timings.FindAll(r=>r.NativeClick).Count-_clickRowsBefore==16,
                        "all sixteen real native tab clicks have bounded CPU repaint evidence");
                    for(int i=0;i<8;i++)
                        Check(timings.Exists(r=>r.Tab==i&&r.FirstInSession)&&timings.Exists(r=>r.Tab==i&&!r.FirstInSession),
                            "first and warmed native body repaint measured for tab "+i);
                    _app.UiForTests.PaintTelemetry.Write(_app.DataDirectory);break;
                case 52:
                    _app.UiForTests.ShowTabForTest(7);
                    Pointer(1000,738,true);break;
                case 53:Pointer(1000,738,false);break;
                case 54:
                    Check(_app.UiForTests.OptimizationVisibleForTest,"native LMB opened nested optimization without a ninth crowded tab");
                    Capture("optimization-default");break;
                case 55:_app.ShowTabForTest(0);Pointer(1062,371,true);break;
                case 56:Pointer(1062,371,false);break;
                case 57:Check(_app.Player.SpeedMultiplier==10,"native LMB selected the requested speed times ten");break;
                case 58:Pointer(1058,449,true);break;
                case 59:Pointer(1058,449,false);break;
                case 60:Check(_app.Player.HighJump&&_app.Player.PrivateJumpForTest,"native LMB enabled high jump through a private native ability clone");break;
                case 61:Pointer(1058,572,true);break;
                case 62:Pointer(1058,572,false);break;
                case 63:Check(_app.Player.InfiniteItems,"native LMB enabled explicitly opt-in infinite vacpack");Capture("lo-player-on");break;
                case 64:
                    _app.Player.HighJump=_app.Player.InfiniteItems=false;_app.Player.Tick();
                    _app.ShowTabForTest(2);break;
                case 65:Capture("lo-vacpack-row");break;
                case 66:_app.ShowTabForTest(7);Pointer(711,738,true);break;
                case 67:Pointer(711,738,false);break;
                case 68:Check(_app.UiForTests.InfoVisibleForTest,"native LMB opened version/status in nested Console Info");Capture("lo-info");break;
                case 69:
                    Check(!NativeUiPriority.HasNativeFocus,"native F1 fixture starts from gameplay without another modal");
                    _app.CloseMenu();_nativeF1Name=_app.Context.PediaDirector._pediaDisplay.Name;
                    _nativeF1Requested=true;HotkeyRow("before real native F1 press");
                    NativeKeyboardProbe.PressAuxiliary(Keyboard.current.f1Key);break;
                case 70:
                    HotkeyRow("after real native F1 press");
                    Check(!_app.MenuOpen,"native F1 state does not open the mod");NativeKeyboardProbe.ReleaseAuxiliary();
                    _nativeCloseDeadline=Time.unscaledTime+15f;
                    break;
                case 71:
                    // The real F1 handler can enqueue its display after our
                    // first post-key update. Never issue CloseDisplay before
                    // observing that exact native Pedia, then mistake its
                    // delayed opening for a broken Insert shortcut.
                    if(!NativeF1PediaFocused())
                    {
                        if(Time.unscaledTime>=_nativeCloseDeadline)HotkeyRow("native F1 open deadline");
                        Check(Time.unscaledTime<_nativeCloseDeadline,"real F1 opened the configured native Slimepedia within its bounded wait");
                        _stage=71;break;
                    }
                    HotkeyRow("configured native F1 Slimepedia observed");
                    Check(!_app.MenuOpen,"real F1 opened native Slimepedia without mod ownership");
                    Il2Cpp.GameContext.Instance.UIDirector.CloseDisplay(_nativeF1Name);
                    _nativeCloseDeadline=Time.unscaledTime+15f;_stage=171;break;
                case 171:
                    if(NativeUiPriority.HasNativeFocus||Time.timeScale<=0||Cursor.lockState!=CursorLockMode.Locked)
                    {
                        if(Time.unscaledTime>=_nativeCloseDeadline)HotkeyRow("native F1 close deadline");
                        Check(Time.unscaledTime<_nativeCloseDeadline,"native F1 display closed and gameplay resumed within its bounded wait");
                        _stage=171;break;
                    }
                    _nativeF1Requested=false;HotkeyRow("native F1 closed before Insert");
                    NativeKeyboardProbe.PressAuxiliary(Keyboard.current.insertKey);_stage=72;break;
                case 72:
                    HotkeyRow("after actual Insert press");
                    Check(_app.MenuOpen,"native Insert state opens the mod");NativeKeyboardProbe.ReleaseAuxiliary();break;
                case 73:NativeKeyboardProbe.PressAuxiliary(Keyboard.current.escapeKey);break;
                case 74:Check(!_app.MenuOpen,"native Escape state closes the mod");NativeKeyboardProbe.ReleaseAuxiliary();break;
                case 75:
                    _app.UiForTests.ShowExplorerForTest(1);_app.Custom.DraftName="";
                    _app.Input.Open();break;
                case 76:Pointer(200,331,true);break;
                case 77:Pointer(200,331,false);break;
                case 78:
                    Check(_app.UiForTests.TextInputAbiForTest&&_app.UiForTests.TextListenerRootsForTest==1,
                        "native mouse focused exactly one safe text listener");
                    NativeTextProbe.SelectAll();break;
                case 79:NativeTextProbe.Release();break;
                case 80:NativeTextProbe.Type("Мятная тропа");_textQueuedFrame=NativeTextProbe.QueuedTextFrame;break;
                case 81:
                    var ui=_app.UiForTests;
                    _textEventFrame=ui.LastTextEventFrameForTest;_textBoundFrame=ui.LastTextBindFrameForTest;
                    WriteTextFailureDiagnostics("Мятная тропа");
                    Check(_app.Custom.DraftName=="Мятная тропа"&&ui.FocusedTextForTest=="Мятная тропа",
                        "native Cyrillic text commits once into the real waypoint name field");
                    Check(_textEventFrame>=_textQueuedFrame&&_textEventFrame-_textQueuedFrame<=2&&
                        _textBoundFrame>=_textEventFrame&&_textBoundFrame-_textEventFrame<=2,
                        "native text event and real field binding complete within two actual frames");
                    Capture("lo-text-cyrillic");break;
                case 82:NativeTextProbe.Ime("Ёж");break;
                case 83:
                    Check(_app.UiForTests.CompositionForTest=="Ёж"&&_app.Custom.DraftName=="Мятная тропа",
                        "native IME preview does not change committed waypoint text");
                    Capture("lo-text-ime-preview");break;
                case 84:NativeTextProbe.Ime("");NativeTextProbe.Type("Ёж");break;
                case 85:Check(_app.Custom.DraftName=="Мятная тропаЁж","native IME commit does not duplicate its preview");break;
                case 86:NativeKeyboardProbe.PressAuxiliary(Keyboard.current.backspaceKey);break;
                case 87:NativeKeyboardProbe.ReleaseAuxiliary();break;
                case 88:Check(_app.Custom.DraftName=="Мятная тропаЁ","native Backspace edits without polling character names or delayed repeats");break;
                case 89:NativeTextProbe.Type("!");NativeKeyboardProbe.PressAuxiliary(Keyboard.current.enterKey);break;
                case 90:
                    Check(_app.Custom.DraftName=="Мятная тропаЁ!"&&_app.UiForTests.TextListenerRootsForTest==0,
                        "Enter retains a same-update text commit and releases native text listeners");
                    NativeKeyboardProbe.ReleaseAuxiliary();break;
                case 91:
                    Check(_app.UiForTests.RejectedImeEventsForTest==0,"valid full native IME events pass exact bounds checks");
                    Check(_app.UiForTests.RejectedTextEventsForTest==0,"valid native UTF-32 events pass exact scalar and byte bounds checks");
                    File.WriteAllText(Path.Combine(_app.DataDirectory,"text-input-proof.json"),JsonSerializer.Serialize(new{
                        Version=ToolkitMod.Version,
                        Scope="Owned-copy full native InputSystem events; not physical OS keyboard or platform IME-window certification",
                        TextQueuedFrame=_textQueuedFrame,TextEventFrame=_textEventFrame,RealFieldBindFrame=_textBoundFrame,
                        Commits=_app.UiForTests.TextCommitsForTest-_textCommitsBefore,
                        ImePreviews=_app.UiForTests.ImePreviewsForTest-_imePreviewsBefore,
                        CallbackCpuMs=_app.UiForTests.LastTextCallbackMsForTest,
                        ListenerRootsAfterEnter=_app.UiForTests.TextListenerRootsForTest,
                        TruncatedGeneratedImeBridgeUsed=false,
                        NativeUtf32Events=true,UnmanagedCharDelegateUsed=false,
                        RejectedTextEvents=_app.UiForTests.RejectedTextEventsForTest
                    },new JsonSerializerOptions{WriteIndented=true}));
                    Capture("lo-text-committed");break;
                default:Check(!_mouse.leftButton.isPressed,"native LMB released");Finish(true);return;
            }
            _deadline=Time.unscaledTime+.24f;
        }
        catch(Exception e){_rows.Add("FAIL "+e);Finish(false);}
    }
    private unsafe void Finish(bool pass)
    {
        try
        {
            if(_mouse!=null&&!_mouse.WasCollected)
            {
                var buffer=StateEvent.From(_mouse,out InputEventPtr ptr,Allocator.Temp);
                try
                {
                    InputControlExtensions.WriteValueIntoEvent<float>(_mouse.leftButton,0f,ptr);
                    InputControlExtensions.WriteValueIntoEvent<Vector2>(_mouse.position,_original,ptr);
                    InputSystem.QueueEvent(ptr);_pressed=false;
                }
                finally{buffer.Dispose();}
                if(!_oldEnabled&&!Application.isFocused&&_mouse.enabled)InputSystem.DisableDevice(_mouse);
            }
        }
        finally
        {
            NativeTextProbe.Release();NativeKeyboardProbe.ReleaseAuxiliary();
            try
            {
                if(_nativeF1Requested&&_app.Ready&&NativeF1PediaFocused())
                    Il2Cpp.GameContext.Instance.UIDirector.CloseDisplay(_nativeF1Name);
            }
            catch(Exception e){_rows.Add("FAIL owned F1 display retirement: "+e.Message);pass=false;}
            finally{_nativeF1Requested=false;}
            _app.Convenience.RestoreProfile();_app.Player.SpeedMultiplier=_oldSpeed;
            _app.Player.HighJump=_oldJump;_app.Player.JumpHeightMultiplier=_oldJumpHeight;_app.Player.InfiniteItems=_oldItems;
            _app.CloseMenu();_app.Custom.DraftName=_oldDraft;_stage=-1;_mouse=null;
            Status=pass?"PASS":"FAIL";
            File.WriteAllText(Path.Combine(_app.DataDirectory,"ui-interaction.txt"),"CreatorToolkit "+ToolkitMod.Version+" native InputSystem pointer probe\n"+Status+"\n"+string.Join("\n",_rows)+"\nScope: full native mouse device events, not physical OS mouse certification.\n");
        }
    }
    internal void Cancel(){if(Active){_rows.Add("CANCELLED");Finish(false);}}
    private void WriteTextFailureDiagnostics(string expected)
    {
        _app.Audit.GuardStressCopy();
        var ui=_app.UiForTests;string draft=_app.Custom.DraftName,focused=ui.FocusedTextForTest;
        int MissingIndex(string actual)
        {
            for(int i=0;i<expected.Length;i++)if(expected.Remove(i,1)==actual)return i;
            return -1;
        }
        // Never log actual typed text, clipboard or unknown characters.
        // Compare only our public synthetic fixture and numeric counters.
        File.WriteAllText(Path.Combine(_app.DataDirectory,"text-input-diagnostic.json"),JsonSerializer.Serialize(new{
            Version=ToolkitMod.Version,CopyOnly=true,ExpectedFixture="Мятная тропа",
            ExpectedLength=expected.Length,DraftLength=draft.Length,FocusedLength=focused.Length,
            DraftEqualsExpected=draft==expected,FocusedEqualsExpected=focused==expected,
            DraftEqualsFocused=draft==focused,
            DraftEqualsExpectedWithoutSpace=draft==expected.Replace(" ",""),
            FocusedEqualsExpectedWithoutSpace=focused==expected.Replace(" ",""),
            MissingExpectedDraftIndex=MissingIndex(draft),MissingExpectedFocusedIndex=MissingIndex(focused),
            ui.TextCallbacksForTest,ui.TextSpaceCallbacksForTest,ui.TextCommitsForTest,
            ui.TextRejectedFocusForTest,ui.TextRejectedCharacterForTest,
            ui.RejectedTextEventsForTest,
            QueuedFrame=_textQueuedFrame,EventFrame=_textEventFrame,BindFrame=_textBoundFrame,
            Frame=Time.frameCount
        },new JsonSerializerOptions{WriteIndented=true}));
    }
    internal bool HoldsDevice=>_mouse!=null;
}
