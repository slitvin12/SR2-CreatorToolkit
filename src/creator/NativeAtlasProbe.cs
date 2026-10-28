using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;

namespace CreatorToolkit;

// Runs only on explicit diagnostics/the isolated feature audit. Uses the real
// native map, InputSystem pointer, native navigation event and native teleport.
internal sealed class NativeAtlasProbe
{
    private readonly ToolkitMod _app;
    private readonly List<string> _rows=new();
    private Mouse _mouse;
    private Vector2 _oldPointer,_screen;
    private Vector3 _origin,_target;
    private string[] _visible;
    private bool _oldMouseEnabled,_oldClick,_oldCustom,_exerciseCustom;
    private string _oldDraft="",_newCustomId="";
    private int _oldColor,_customBefore;
    private int _stage=-1,_before;
    private float _next,_deadline;
    internal bool Active=>_stage>=0;
    internal string Status="idle";
    internal NativeAtlasProbe(ToolkitMod app)=>_app=app;
    internal void Start()
    {
        _app.Audit.GuardStressCopy();
        if(Active||_app.Map.Busy)throw new InvalidOperationException("Map probe/transit already running.");
        _mouse=Mouse.current;if(_mouse==null||_mouse.leftButton.isPressed||_mouse.rightButton.isPressed)throw new InvalidOperationException("Native pointer held/unavailable.");
        _rows.Clear();_oldPointer=_mouse.position.ReadValue();_oldMouseEnabled=_mouse.enabled;
        if(!_mouse.enabled)InputSystem.EnableDevice(_mouse);
        _visible=new List<string>(_app.Map.Visible).ToArray();_oldClick=_app.Map.ClickTeleport;_oldCustom=_app.Map.CustomPointMode;
        _exerciseCustom=_app.Audit.Active;_newCustomId="";
        _oldDraft=_app.Custom.DraftName;_oldColor=_app.Custom.DraftColor;
        _app.Map.Visible.Clear();_app.Map.ClickTeleport=true;_app.Map.CustomPointMode=false;
        _origin=Controller.Motor.TransientPosition;_before=_app.Map.ClickRequests;
        _app.Map.Open();_stage=0;_next=Time.unscaledTime+.8f;_deadline=Time.unscaledTime+35f;Status="running";
    }
    private SRCharacterController Controller=>_app.Context.Player.GetComponentInChildren<SRCharacterController>(true);
    private unsafe void MouseEvent(Vector2 position,bool down)
    {
        var buffer=StateEvent.From(_mouse,out InputEventPtr ptr,Allocator.Temp);
        try
        {
            InputControlExtensions.WriteValueIntoEvent<Vector2>(_mouse.position,position,ptr);
            InputControlExtensions.WriteValueIntoEvent<Vector2>(_mouse.delta,position-_mouse.position.ReadValue(),ptr);
            InputControlExtensions.WriteValueIntoEvent<float>(_mouse.leftButton,down?1f:0f,ptr);
            InputSystem.QueueEvent(ptr);
        }
        finally{buffer.Dispose();}
    }
    private void Check(bool condition,string text)
    { _rows.Add((condition?"PASS ":"FAIL ")+text);if(!condition)throw new InvalidOperationException(text); }
    internal void Tick()
    {
        if(!Active||Time.unscaledTime<_next)return;
        try
        {
            if(Time.unscaledTime>_deadline)throw new InvalidOperationException("Native map click/transit probe timed out; stage="+_stage+" display="+_app.Map.DisplayStatus+" clicks="+(_app.Map.ClickRequests-_before));
            if(!_app.Ready)return;
            if(_stage==0)
            {
                if(!_app.Map.NativeMapOpen||_app.Map.Ui._viewModel==null||_app.Map.Ui._map==null)
                {Status="waiting native map display: "+_app.Map.DisplayStatus;return;}
                _app.Map.Ui._viewModel.CenterOnWorldPosition(new Vector2(_origin.x,_origin.z));
                _app.Map.Ui._viewModel.SetMapZoom(2.5f);
                _stage=1;_next=Time.unscaledTime+.8f;return;
            }
            if(_stage==1)
            {
                Check(_app.Map.ProjectionError>=0&&_app.Map.ProjectionError<10,"native map projection matches player marker");
                Vector2 originScreen=_app.Map.ScreenPoint(_origin);bool found=false;
                int checkedPoints=0,groundRejected=0,zoneRejected=0,screenRejected=0;
                float minimumPixels=Mathf.Max(34f,46f*Mathf.Min(1f,Screen.height/1080f));
                foreach(float radius in new[]{8f,12f,20f,28f,40f,56f,80f,96f})
                {
                    for(int i=0;i<16;i++)
                    {
                        checkedPoints++;
                        float angle=i*Mathf.PI/8;Vector3 candidate=_origin+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
                        // A map click has X/Z, not an assumed nearby Y. Use the
                        // identical normal map-click ground query and all its
                        // unchanged native slope/hazard/capsule-clearance gates.
                        if(!_app.Player.TryMapGround(candidate,false,out var ground)){groundRejected++;continue;}
                        var ui=_app.Map.Ui;
                        if(MapTools.ZoneAreaAt(ui,ground)==null){zoneRejected++;continue;}
                        Vector2 screen=_app.Map.ScreenPoint(ground);
                        if(Vector2.Distance(screen,originScreen)<minimumPixels||screen.x<100||screen.y<120||screen.x>Screen.width-100||screen.y>Screen.height-120||_app.Map.OverlayContains(screen)){screenRejected++;continue;}
                        Check(_app.Map.TryScreenWorld(screen,out var inverse)&&new Vector2(inverse.x-ground.x,inverse.z-ground.z).magnitude<.1f,"native projection round-trip at click target");
                        _target=ground;_screen=screen;found=true;break;
                    }
                    if(found)break;
                }
                _rows.Add("INFO target search candidates="+checkedPoints+" groundRejected="+groundRejected+
                    " zoneRejected="+zoneRejected+" screenRejected="+screenRejected+
                    " minimumPixels="+minimumPixels+" resolution="+Screen.width+"x"+Screen.height);
                if(!found)
                {
                    _rows.Add("INFO origin ground="+_app.Player.DescribeMapGround(_origin,false));
                    ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"atlas-click-search-failed.png"));
                }
                Check(found,"found a validated nearby terrain point clear of map controls");
                ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"atlas-click-target.png"));
                MouseEvent(_screen,false);_stage=11;_next=Time.unscaledTime+.25f;return;
            }
            if(_stage==11){MouseEvent(_screen,true);_stage=2;_next=Time.unscaledTime+.16f;return;}
            if(_stage==2){MouseEvent(_screen,false);_stage=3;_next=Time.unscaledTime+.8f;return;}
            if(_stage==3)
            {
                if(_app.Map.ClickRequests<=_before){_next=Time.unscaledTime+.2f;return;}
                if(_app.Map.Busy)return;
                _rows.Add("INFO transit="+_app.Map.TransitDetails);
                _rows.Add("INFO clickResolver="+_app.Map.LastClickResolver);
                Check(_app.Map.LastTransitSucceeded&&_app.Map.HasReturn,"native LMB click reached a successful terrain landing and created return point");
                Vector3 pos=Controller.Motor.TransientPosition;
                _rows.Add("INFO target="+_target.ToString("F3")+" pointer="+_screen.ToString("F2")+" accepted="+_app.Map.LastClickWorld.ToString("F3")+" acceptedPointer="+_app.Map.LastClickScreen.ToString("F2"));
                Check(new Vector2(pos.x-_target.x,pos.z-_target.z).magnitude<2f,"native map click landed at requested X/Z: "+pos.ToString("F2"));
                Check(float.IsFinite(pos.y)&&Controller.Motor.enabled,"landing has finite height and restored native motor");
                Check(!_app.Player.TransitHeld&&_app.Map.LastSceneAssetsReady&&_app.Map.LastStableGroundSamples>=4&&
                    _app.Map.LastTransitHoldSeconds>=TransitSafety.CollisionWarmupSeconds+TransitSafety.PlacementSettleSeconds,
                    "native scene assets and repeated stable collision gate precede gravity release");
                ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"atlas-click-landing.png"));
                _app.Map.Return();_stage=4;_next=Time.unscaledTime+.8f;return;
            }
            if(_stage==4)
            {
                if(_app.Map.Busy)return;
                var delta=Controller.Motor.TransientPosition-_origin;
                Check(SafetyPolicy.NearbyReturnOffset(delta.x,delta.y,delta.z)&&_app.Player.IsClearPosition(Controller.Motor.TransientPosition),
                    "return restored a bounded collision-safe pre-click position");
                if(!_exerciseCustom){Finish(true);return;}
                _app.Audit.GuardStressCopy();_customBefore=_app.Custom.Records.Count;
                _app.Custom.DraftName="Лимонная метка";_app.Custom.DraftColor=2;
                _app.Map.ClickTeleport=false;_app.Map.CustomPointMode=true;_before=_app.Map.ClickRequests;
                _app.Map.Open();_stage=5;_next=Time.unscaledTime+.8f;return;
            }
            if(_stage==5)
            {
                if(!_app.Map.NativeMapOpen||_app.Map.Ui._viewModel==null)return;
                _app.Map.Ui._viewModel.CenterOnWorldPosition(new Vector2(_origin.x,_origin.z));
                _app.Map.Ui._viewModel.SetMapZoom(2.5f);
                _stage=6;_next=Time.unscaledTime+.8f;return;
            }
            if(_stage==6)
            {
                _screen=_app.Map.ScreenPoint(_target);
                Check(!_app.Map.OverlayContains(_screen),"custom placement pointer is outside map chrome/shields");
                MouseEvent(_screen,false);_stage=12;_next=Time.unscaledTime+.25f;return;
            }
            if(_stage==12){MouseEvent(_screen,true);_stage=7;_next=Time.unscaledTime+.16f;return;}
            if(_stage==7){MouseEvent(_screen,false);_stage=8;_next=Time.unscaledTime+.5f;return;}
            if(_stage==8)
            {
                _app.Audit.GuardStressCopy();
                if(_app.Custom.Records.Count<=_customBefore){_next=Time.unscaledTime+.2f;return;}
                Check(_app.Custom.Records.Count==_customBefore+1&&!_app.Map.Busy,
                    "native custom LMB gesture creates exactly one marker without teleporting the player");
                var record=_app.Custom.Records[_app.Custom.Records.Count-1];_newCustomId=record.Id;
                Check(record.Name=="Лимонная метка"&&record.Color==2&&!record.ExactHeight,
                    "native map marker preserves chosen name/color without inventing a ground height");
                Check(new Vector2(record.X-_target.x,record.Z-_target.z).magnitude<.2f,
                    "custom marker inverse projection matches requested X/Z");
                _app.Custom.ReloadForTest();
                Check(_app.Custom.Find(_newCustomId)?.Name==record.Name,"actual native-click marker survived its atomic file round-trip");
                _app.Map.Selected=_app.Custom.FindPoint(_newCustomId);_stage=9;_next=Time.unscaledTime+.3f;return;
            }
            if(_stage==9)
            {
                Check(_app.Map.OverlayCardRects.Count==1,"custom map popover blocks click-through into placement mode");
                ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"v050-waypoint-native-click.png"));
                // CaptureScreenshot samples at the end of the frame. Keep the
                // native map alive until that frame actually renders.
                _stage=10;_next=Time.unscaledTime+.25f;return;
            }
            if(_stage==10)Finish(true);
        }
        catch(Exception e){_rows.Add("FAIL "+e);if(_app.Map.Busy)_app.Map.AbortTeleport();Finish(false);}
    }
    private void Finish(bool pass)
    {
        try
        {
            MouseEvent(_oldPointer,false);
            if(!_oldMouseEnabled&&!Application.isFocused&&_mouse.enabled)InputSystem.DisableDevice(_mouse);
            _app.Map.Visible.Clear();foreach(string category in _visible)_app.Map.Visible.Add(category);
            if(_newCustomId.Length>0&&_exerciseCustom&&!_app.Map.Busy){_app.Audit.GuardStressCopy();_app.Custom.Delete(_newCustomId);}
            _app.Custom.DraftName=_oldDraft;_app.Custom.DraftColor=_oldColor;
            _app.Map.ClickTeleport=_oldClick;_app.Map.CustomPointMode=_oldCustom;_app.Map.CloseNative();
        }
        finally
        {
            _stage=-1;Status=pass?"PASS":"FAIL";
            File.WriteAllText(Path.Combine(_app.DataDirectory,"atlas-interaction.txt"),"CreatorToolkit "+ToolkitMod.Version+" native map interaction probe\n"+Status+"\n"+string.Join("\n",_rows)+"\nScope: native pointer on nearby current-region terrain; not every region/OS mouse certification.\n");
        }
    }
    internal void Cancel(){if(Active){if(_app.Map.Busy)_app.Map.AbortTeleport();_rows.Add("CANCELLED");Finish(false);}}
}
