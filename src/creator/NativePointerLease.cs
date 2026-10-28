using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CreatorToolkit;

// Isolated-copy InputSystem events only. No OS cursor movement, SendInput,
// EventSystem.ExecuteEvents or direct invocation of a button callback.
internal sealed class NativePointerLease:IDisposable
{
    private Mouse _mouse;
    private readonly Vector2 _original;
    private Vector2 _last;
    private readonly bool _wasEnabled;
    internal NativePointerLease()
    {
        ToolkitMod.I.Audit.GuardStressCopy();
        _mouse=Mouse.current;
        if(_mouse==null||_mouse.leftButton.isPressed||_mouse.rightButton.isPressed)
            throw new InvalidOperationException("Native mouse unavailable or held; input probe refused.");
        _original=_last=_mouse.position.ReadValue();_wasEnabled=_mouse.enabled;
        if(!_wasEnabled)InputSystem.EnableDevice(_mouse);
    }
    internal unsafe void Set(Vector2 pixel,bool down,Vector2 scroll=default)
    {
        ToolkitMod.I.Audit.GuardStressCopy();
        Queue(pixel,down,scroll);
    }
    private unsafe void Queue(Vector2 pixel,bool down,Vector2 scroll)
    {
        var buffer=StateEvent.From(_mouse,out InputEventPtr ptr,Allocator.Temp);
        try
        {
            InputControlExtensions.WriteValueIntoEvent<Vector2>(_mouse.position,pixel,ptr);
            InputControlExtensions.WriteValueIntoEvent<Vector2>(_mouse.delta,pixel-_last,ptr);
            InputControlExtensions.WriteValueIntoEvent<Vector2>(_mouse.scroll,scroll,ptr);
            InputControlExtensions.WriteValueIntoEvent<float>(_mouse.leftButton,down?1:0,ptr);
            InputSystem.QueueEvent(ptr);_last=pixel;
        }
        finally{buffer.Dispose();}
    }
    public void Dispose()
    {
        if(_mouse==null)return;
        try
        {
            if(!_mouse.WasCollected){Queue(_original,false,Vector2.zero);if(!_wasEnabled)InputSystem.DisableDevice(_mouse);}
        }
        finally{_mouse=null;}
    }
}