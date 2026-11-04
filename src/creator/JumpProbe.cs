using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace CreatorToolkit;

// 0.8.3 explicit diagnostic only (`ct tk jump-probe` / `jump-probe-legacy`): real native Space jumps
// before and after Creator Toolkit flight in eight exit situations. Never starts by itself, never
// saves, always leaves flight off and releases its single synthetic key. Result: jump-probe.txt.
internal static class JumpProbe
{
    private static ToolkitMod _app;
    private static IEnumerator<float> _run;
    private static float _resume;
    private static IntPtr _session;
    private static readonly StringBuilder _log=new();
    private static Keyboard _keyboard;
    private static ButtonControl _held;
    private static int _passed,_failed;
    internal static bool Active=>_run!=null;

    internal static void Start(ToolkitMod app,bool legacy)
    {
        if(_run!=null)throw new InvalidOperationException("Jump probe already running.");
        if(!app.Ready||app.Map.Busy||app.Player.TransitHeld||app.Player.Controller==null)
            throw new InvalidOperationException("Jump probe requires idle gameplay with a native player.");
        if(Keyboard.current==null)throw new InvalidOperationException("Native keyboard unavailable.");
        _app=app;_session=app.Session;_log.Clear();_passed=_failed=0;
        PlayerTools.LegacyFlightExitForTest=legacy;
        Line("jump probe mode="+(legacy?"legacy":"repaired")+" at "+DateTime.Now.ToString("HH:mm:ss"));
        _run=Run();_resume=0;
    }

    internal static void Tick()
    {
        if(_run==null||Time.unscaledTime<_resume)return;
        try
        {
            if(!_app.Ready||_app.Session!=_session)throw new InvalidOperationException("Session changed during jump probe.");
            if(_run.MoveNext())_resume=Time.unscaledTime+Math.Max(0f,_run.Current);
            else Finish(null);
        }
        catch(Exception e){Finish(e);}
    }

    private static void Finish(Exception error)
    {
        var app=_app;
        try{Release();}catch(Exception e){Line("release: "+e.Message);}
        try{if(app.Player.Flight)app.Player.SetFlight(false);}catch(Exception e){Line("cleanup flight: "+e.Message);}
        if(error!=null)Line("ERROR "+error);
        Line("done passed="+_passed+" failed="+_failed+" exitRefreshes="+app.Player.FlightExitRefreshes+" lastIssue="+(app.Player.LastFlightExitIssue??"-"));
        PlayerTools.LegacyFlightExitForTest=false;
        _run=null;_app=null;
        try{File.WriteAllText(Path.Combine(app.DataDirectory,"jump-probe.txt"),_log.ToString());}catch{}
    }

    private static void Line(string text)
    {
        _log.AppendLine(text);
        MelonLoader.MelonLogger.Msg("[JumpProbe] "+text);
    }

    private static readonly string[] Scenarios={"baseline","toggle-on-ground","exit-in-air","enter-mid-jump",
        "space-held-in-flight","exit-inside-ground","menu-toggle-ground","menu-exit-in-air"};

    private static IEnumerator<float> Run()
    {
        _app.CloseMenu();_app.Input.RecoverGameplay();
        yield return .5f;
        for(int s=0;s<Scenarios.Length;s++)
        {
            foreach(var w in WaitGrounded(5f))yield return w;
            yield return .4f;
            switch(s)
            {
                case 1: Flight(true);yield return .4f;Flight(false);yield return .3f;break;
                case 2: Flight(true);yield return .1f;Move(4f);yield return .3f;Flight(false);
                    foreach(var w in WaitGrounded(6f))yield return w;yield return .3f;break;
                case 3: Press(Keyboard.current.spaceKey);yield return .12f;Release();yield return .05f;Flight(true);yield return .4f;Flight(false);
                    foreach(var w in WaitGrounded(6f))yield return w;yield return .3f;break;
                case 4: Flight(true);yield return .1f;Press(Keyboard.current.spaceKey);yield return .5f;Release();yield return .1f;Flight(false);
                    foreach(var w in WaitGrounded(6f))yield return w;yield return .3f;break;
                case 5: Flight(true);yield return .1f;Move(-2.5f);yield return .3f;Flight(false);
                    foreach(var w in WaitGrounded(6f))yield return w;yield return .3f;break;
                case 6: Flight(true);yield return .2f;_app.Input.Open();yield return .3f;Flight(false);yield return .3f;_app.CloseMenu();yield return .5f;break;
                case 7: Flight(true);yield return .1f;Move(4f);yield return .2f;_app.Input.Open();yield return .3f;Flight(false);yield return .3f;_app.CloseMenu();
                    foreach(var w in WaitGrounded(6f))yield return w;yield return .3f;break;
            }
            foreach(var w in JumpTest(Scenarios[s]))yield return w;
        }
    }

    private static void Flight(bool on)=>_app.Player.SetFlight(on);
    private static void Move(float dy)
    {
        var motor=_app.Player.Controller.Motor;
        motor.SetPosition(motor.TransientPosition+Vector3.up*dy,true);
    }

    private static IEnumerable<float> WaitGrounded(float timeout)
    {
        var controller=_app.Player.Controller;
        float deadline=Time.unscaledTime+timeout;
        while(controller!=null&&!controller.IsGrounded&&Time.unscaledTime<deadline)yield return 0f;
        if(controller==null||!controller.IsGrounded)Line("  (not grounded after "+timeout+" s) "+Snap());
    }

    private static IEnumerable<float> JumpTest(string name)
    {
        var controller=_app.Player.Controller;
        if(controller==null)throw new InvalidOperationException("Native controller lost.");
        string before=Snap();
        float y0=controller.Motor.TransientPosition.y,max=y0;
        Press(Keyboard.current.spaceKey);
        yield return 0f;
        string pressed=Snap();
        float until=Time.unscaledTime+.15f;
        while(Time.unscaledTime<until){max=Math.Max(max,controller.Motor.TransientPosition.y);yield return 0f;}
        Release();
        until=Time.unscaledTime+.8f;
        while(Time.unscaledTime<until){max=Math.Max(max,controller.Motor.TransientPosition.y);yield return 0f;}
        float rise=max-y0;bool ok=rise>.5f;
        if(ok)_passed++;else _failed++;
        Line((ok?"PASS ":"FAIL ")+name+" rise="+rise.ToString("F2")+"\n  before:  "+before+"\n  pressed: "+pressed);
    }

    internal static string Snap()
    {
        var c=_app?.Player?.Controller;if(c==null)return "no controller";
        var text=new StringBuilder();
        try
        {
            var m=c.Motor;var g=m.GroundingStatus;
            text.Append("grounded=").Append(c.IsGrounded).Append(" stable=").Append(g.IsStableOnGround).Append(" any=").Append(g.FoundAnyGround)
                .Append(" motor=").Append(m.enabled).Append(" solveG=").Append(m._solveGrounding).Append(" solveM=").Append(m._solveMovementCollisions)
                .Append(" capsule=").Append(m.Capsule!=null&&m.Capsule.enabled).Append("/trigger=").Append(m.Capsule!=null&&m.Capsule.isTrigger)
                .Append(" bypassG=").Append(c._BypassGravity_k__BackingField).Append(" mustUnground=").Append(m.MustUnground())
                .Append(" vy=").Append(c.Velocity.y.ToString("F2")).Append(" y=").Append(m.TransientPosition.y.ToString("F2"))
                .Append(" t=").Append(c.CurrentTime.ToString("F2"));
        }
        catch(Exception e){text.Append(" motorErr=").Append(e.GetType().Name);}
        try
        {
            var j=_app.Player.NativeJump();
            if(j==null)text.Append(" jump=null");
            else
            {
                text.Append(" jump[active=").Append(j.IsActive).Append(" req=").Append(j._jumpRequested).Append(" startEv=").Append(j.StartAbilityEventReceived)
                    .Append(" stopEv=").Append(j.StopAbilityEventReceived).Append(" input=").Append(j._activeStartInputEvent!=null)
                    .Append(" land=").Append(N(j._landingTime)).Append(" air=").Append(N(j._inAirTime));
                try{text.Append(" can=").Append(j.CanStart());}catch(Exception e){text.Append(" canErr=").Append(e.GetType().Name);}
                text.Append(']');
            }
        }
        catch(Exception e){text.Append(" jumpErr=").Append(e.GetType().Name);}
        text.Append(" space=").Append(Keyboard.current!=null&&Keyboard.current.spaceKey.isPressed).Append(" lock=").Append(Cursor.lockState)
            .Append(" menu=").Append(_app.MenuOpen).Append(" flight=").Append(_app.Player.Flight);
        return text.ToString();
    }
    private static string N(Il2CppSystem.Nullable<double> value)=>value!=null&&value.hasValue?value.value.ToString("F2"):"-";

    private static void Press(ButtonControl key)
    {
        if(_held!=null||key==null||key.isPressed)throw new InvalidOperationException("Key already held; refusing to override user input.");
        _keyboard=Keyboard.current;_held=key;
        Queue(_keyboard,key,true);
    }
    private static void Release()
    {
        var key=_held;var keyboard=_keyboard;
        try{if(key!=null&&keyboard!=null&&!keyboard.WasCollected)Queue(keyboard,key,false);}
        finally{_held=null;_keyboard=null;}
    }
    private static unsafe void Queue(Keyboard keyboard,ButtonControl key,bool down)
    {
        var buffer=StateEvent.From(keyboard,out InputEventPtr eventPtr,Allocator.Temp);
        try{InputControlExtensions.WriteValueIntoEvent<float>(key,down?1f:0f,eventPtr);InputSystem.QueueEvent(eventPtr);}
        finally{buffer.Dispose();}
    }
}
