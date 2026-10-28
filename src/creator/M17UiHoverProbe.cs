using System;
using System.IO;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace CreatorToolkit;
// Explicit UI-only hover: no button-down events, no OS cursor or gameplay calls.
internal static class M17UiHoverProbe
{
 private static Mouse _mouse;private static Vector2 _original;private static bool _enabled;
 private static IntPtr _session;private static float _until;private static int _phase;
 internal static void Start()
 {
  var app=ToolkitMod.I;
  if(_phase!=0||!app.Ready||!app.MenuOpen||app.Map.NativeMapOpen||NativeUiPriority.HasNativeFocus)throw new InvalidOperationException("Hover probe requires the open toolkit and idle native UI");
  _mouse=Mouse.current;
  if(_mouse==null||_mouse.leftButton.isPressed||_mouse.rightButton.isPressed)throw new InvalidOperationException("Mouse is unavailable or held");
  _original=_mouse.position.ReadValue();_enabled=_mouse.enabled;_session=app.Session;
  if(!_enabled)InputSystem.EnableDevice(_mouse);
  app.UiForTests.SliderTestInput=true;_phase=1;_until=Time.unscaledTime+.4f;
  float scale=Mathf.Max(.35f,Mathf.Min(1.2f,Mathf.Min((Screen.width-48f)/1160f,(Screen.height-48f)/820f)));
  Vector2 pixel=new((Screen.width-1160*scale)/2+100*scale,Screen.height-((Screen.height-820*scale)/2+296*scale));
  Queue(pixel);
 }
 private static unsafe void Queue(Vector2 position)
 {
  if(_mouse==null||_mouse.WasCollected)return;
  var data=StateEvent.From(_mouse,out InputEventPtr input,Allocator.Temp);
  try{InputControlExtensions.WriteValueIntoEvent<Vector2>(_mouse.position,position,input);InputControlExtensions.WriteValueIntoEvent<float>(_mouse.leftButton,0,input);InputSystem.QueueEvent(input);}
  finally{data.Dispose();}
 }
 internal static void Tick()
 {
  if(_phase==0)return;var app=ToolkitMod.I;
  if(app.Session!=_session||!app.MenuOpen){Cancel();return;}
  if(Time.unscaledTime<_until)return;
  if(_phase==1){ScreenCapture.CaptureScreenshot(Path.Combine(app.DataDirectory,"m17-hover.png"));_phase=2;_until=Time.unscaledTime+.4f;return;}
  Cancel();
 }
 internal static void Cancel()
 {
  if(_phase==0)return;
  try{Queue(_original);if(_mouse!=null&&!_mouse.WasCollected&&!_enabled)InputSystem.DisableDevice(_mouse);}
  finally{if(ToolkitMod.I?.UiForTests!=null)ToolkitMod.I.UiForTests.SliderTestInput=false;_mouse=null;_phase=0;}
 }
}
