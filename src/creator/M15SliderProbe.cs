using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CreatorToolkit;

// Explicit temporary UI-only native InputSystem probe. No OS events, actors,
// progression, native saves, or permanent player changes. Original speed restored.
internal sealed class M15SliderProbe
{
    private readonly ToolkitMod _app;
    private readonly List<object> _rows=new(24);
    private Mouse _mouse;
    private Vector2 _original;
    private float _oldSpeed,_deadline,_committed;
    private bool _wasEnabled,_sawOpening;
    private IntPtr _session;
    private int _stage=-1;
    private readonly long[] _allocationSections=new long[32];
    private long _bytes,_calls,_drawBytes,_drawCalls,_pageBytes,_pageCalls;
    internal string Status {get;private set;}="idle";
    internal bool Active=>_stage>=0;
    internal M15SliderProbe(ToolkitMod app)=>_app=app;
    internal void Start()
    {
        if(Active||!_app.Ready||_app.Map.NativeMapOpen||NativeUiPriority.HasNativeFocus)
            throw new InvalidOperationException("Slider probe requires idle gameplay with no native window.");
        _mouse=Mouse.current;
        if(_mouse==null||_mouse.leftButton.isPressed||_mouse.rightButton.isPressed)
            throw new InvalidOperationException("Mouse unavailable/held; refusing to override input.");
        _session=_app.Session;_oldSpeed=_app.Player.SpeedMultiplier;
        _original=_mouse.position.ReadValue();_wasEnabled=_mouse.enabled;
        if(!_wasEnabled)InputSystem.EnableDevice(_mouse);
        _rows.Clear();_app.UiForTests.SliderTestInput=true;
        _app.CloseMenu();_app.ShowTabForTest(0);_app.Input.Open();
        _sawOpening=false;
        Check(_app.UiForTests.OpeningProgress==0,"real input-open path begins 180ms entrance animation");
        _stage=0;_deadline=Time.unscaledTime+.5f;Status="running";
    }
    private unsafe void Pointer(float x,float y,bool held)
    {
        float scale=Mathf.Max(.35f,Mathf.Min(1.2f,Mathf.Min((Screen.width-48f)/1160f,(Screen.height-48f)/820f)));
        Vector2 position=new((Screen.width-1160*scale)/2+x*scale,
            Screen.height-((Screen.height-820*scale)/2+y*scale));
        Queue(position,held);
    }
    private unsafe void Queue(Vector2 position,bool held)
    {
        var buffer=StateEvent.From(_mouse,out InputEventPtr input,Allocator.Temp);
        try
        {
            InputControlExtensions.WriteValueIntoEvent<Vector2>(_mouse.position,position,input);
            InputControlExtensions.WriteValueIntoEvent<float>(_mouse.leftButton,held?1:0,input);
            InputSystem.QueueEvent(input);
        }
        finally{buffer.Dispose();}
    }
    private void Check(bool pass,string name)
    {
        _rows.Add(new{Name=name,Pass=pass,Frame=Time.frameCount,Speed=_app.Player.SpeedMultiplier,
            Drag=_app.UiForTests.SliderDragForTest});
        if(!pass)throw new InvalidOperationException(name);
    }
    private void BeginMeasure(){var ui=_app.UiForTests;Array.Copy(ui.UiAllocationSections,_allocationSections,32);_bytes=ui.SliderAllocatedBytesForTest;_calls=ui.SliderMeasuredCallsForTest;_drawBytes=ui.DrawAllocatedBytesForTest;_drawCalls=ui.DrawMeasuredCallsForTest;_pageBytes=ui.PlayerPageAllocatedBytesForTest;_pageCalls=ui.PlayerPageMeasuredCallsForTest;}
    private void EndMeasure(string name)
    {
        var ui=_app.UiForTests;
        long bytes=ui.SliderAllocatedBytesForTest-_bytes,calls=ui.SliderMeasuredCallsForTest-_calls;
        long[] sections=new long[32];for(int i=0;i<32;i++)sections[i]=ui.UiAllocationSections[i]-_allocationSections[i];
        _rows.Add(new{Name=name,AllocationSections=sections,MeasuredCalls=calls,ManagedBytes=bytes,FullDrawBytes=ui.DrawAllocatedBytesForTest-_drawBytes,FullDrawCalls=ui.DrawMeasuredCallsForTest-_drawCalls,PlayerPageBytes=ui.PlayerPageAllocatedBytesForTest-_pageBytes,PlayerPageCalls=ui.PlayerPageMeasuredCallsForTest-_pageCalls,
            Scope="Entire warmed production Slider method including Unity/IL2CPP wrappers, excludes other OnGUI methods"});
        Check(calls>0,name+" sampled real production calls");
        Check(bytes==0,name+" warmed production sliders allocate zero managed bytes");
        Check(ui.DrawMeasuredCallsForTest>_drawCalls&&ui.DrawAllocatedBytesForTest==_drawBytes,name+" warmed full toolkit Draw on Player tab allocates zero managed bytes");
        Check(ui.PlayerPageMeasuredCallsForTest>_pageCalls&&ui.PlayerPageAllocatedBytesForTest==_pageBytes,name+" warmed entire PlayerPage allocates zero managed bytes");
    }
    internal void Tick()
    {
        if(!Active)return;
        float opening=_app.UiForTests.OpeningProgress;
        if(!_sawOpening&&opening>0&&opening<1)
        {
            _sawOpening=true;
            _rows.Add(new{Name="visible intermediate opening frame",Progress=opening,Frame=Time.frameCount});
            ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"m16-opening.png"));
        }
        // Continuous native pointer movement exercises changing value readouts,
        // not just a stationary held thumb. This runs outside the measured GUI.
        if(_stage==6&&Time.unscaledTime<_deadline)Pointer(800+8*Mathf.Sin(Time.unscaledTime*18),311,true);
        if(Time.unscaledTime<_deadline)return;
        try
        {
            if(!_app.Ready||_app.Session!=_session)throw new InvalidOperationException("session changed");
            var ui=_app.UiForTests;
            switch(_stage++)
            {
                case 0:
                    Check(_sawOpening&&ui.OpeningProgress==1,"entrance produces intermediate frames and settles before interaction");
                    Pointer(640,250,false);break;
                case 1:
                    // Verify retained GUI wrappers survive an ordinary managed GC.
                    // Collection happens outside the measured OnGUI scopes.
                    GC.Collect();GC.WaitForPendingFinalizers();
                    BeginMeasure();break;
                case 2:EndMeasure("idle");Pointer(662,311,true);break;
                case 3:Pointer(1010,311,true);break;
                case 4:
                    Check(ui.SliderDragForTest==1&&Math.Abs(_app.Player.SpeedMultiplier-8.2f)<.08f,"native held drag follows 80 percent rail point");
                    ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"m15-slider-held.png"));
                    BeginMeasure();break;
                case 5:EndMeasure("held drag");BeginMeasure();Pointer(800,311,true);break;
                case 6:
                    EndMeasure("moving drag with changing cached value labels");
                    Check(_app.Player.SpeedMultiplier<5&&_app.Player.SpeedMultiplier>3,"drag reversal follows new pointer without latching old target");
                    Pointer(800,311,true);break;
                case 7:
                    // QueueEvent is consumed on the next InputSystem update.
                    // Drain the final moving sample before snapshotting release.
                    Check(Math.Abs(_app.Player.SpeedMultiplier-4.3975906f)<.03f,"stationary final pointer has been consumed before release");
                    Pointer(800,311,false);_committed=_app.Player.SpeedMultiplier;break;
                case 8:
                    Check(ui.SliderDragForTest==-1&&Math.Abs(_app.Player.SpeedMultiplier-_committed)<.0001f,"release retains exact committed value");
                    Pointer(800,311,true);break;
                case 9:Pointer(20,20,true);_committed=_app.Player.SpeedMultiplier;break;
                case 10:
                    Check(ui.SliderDragForTest==-1&&Math.Abs(_app.Player.SpeedMultiplier-_committed)<.0001f,"leaving ancestor clip cancels drag without value mutation");
                    Pointer(1010,311,true);break;
                case 11:
                    Check(ui.SliderDragForTest==-1&&Math.Abs(_app.Player.SpeedMultiplier-_committed)<.0001f,"returning while held cannot reacquire a cancelled drag");
                    Pointer(1010,311,false);break;
                case 12:Pointer(800,311,true);break;
                case 13:
                    Check(ui.SliderDragForTest==1,"fresh press reacquires drag");
                    _committed=_app.Player.SpeedMultiplier;
                    if(!Application.isFocused)ui.SliderTestInput=false;
                    else _rows.Add(new{Name="focus loss",Skipped="host currently focused; no OS focus manipulation"});
                    break;
                case 14:
                    if(!Application.isFocused)Check(ui.SliderDragForTest==-1&&Math.Abs(_app.Player.SpeedMultiplier-_committed)<.0001f,"unfocused production input gate cancels held drag without jumping value");
                    Queue(_original,false);break;
                case 15:Finish(true,null);return;
            }
            _deadline=Time.unscaledTime+.35f;
        }
        catch(Exception e){Finish(false,e.Message);}
    }
    internal void Cancel(){if(Active)Finish(false,"cancelled");}
    private void Finish(bool success,string error)
    {
        try
        {
            if(_mouse!=null){Queue(_original,false);if(!_wasEnabled)InputSystem.DisableDevice(_mouse);}
            if(_app.Session==_session&&_app.Ready)_app.Player.SpeedMultiplier=_oldSpeed;
            _app.UiForTests.SliderTestInput=false;_app.UiForTests.ClearInteraction();
            Status=success?"PASS":"FAIL: "+error;
            File.WriteAllText(Path.Combine(_app.DataDirectory,"m15-slider-proof.json"),
                JsonSerializer.Serialize(new{Success=success,Error=error,Scope="Native InputSystem UI-only test; warmed production Slider, entire PlayerPage and full Toolkit.Draw on Player tab. Other tabs, startup/cache misses and Unity external OnGUI dispatch not certified",
                    RestoredSpeed=_app.Player.SpeedMultiplier,OriginalSpeed=_oldSpeed,Rows=_rows},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{_stage=-1;_mouse=null;}
    }
}
