using System;
using Unity.Collections;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CreatorToolkit;

// Opt-in tests only. A full native device event preserves every key except the test's W.
// No global OS input, bitfield InputState.Change, controller/velocity patch, or transform movement.
internal static class NativeKeyboardProbe
{
    private static Keyboard _device;
    private static UnityEngine.InputSystem.Controls.ButtonControl _aux;
    private static Keyboard _auxDevice;

    internal static void PressW()
    {
        ToolkitMod.I.Audit.GuardStressCopy();
        if(_device != null) throw new InvalidOperationException("A native keyboard probe is already active.");
        var keyboard=Keyboard.current;
        if(keyboard == null) throw new InvalidOperationException("Native Keyboard unavailable.");
        if(keyboard.wKey.isPressed)
            throw new InvalidOperationException("W is already held; the test will not override user input.");
        QueueW(keyboard,true);
        _device=keyboard;
    }

    internal static void ReleaseW()
    {
        var keyboard=_device;
        if(keyboard == null) return;
        try { if(!keyboard.WasCollected) QueueW(keyboard,false); }
        finally { _device=null; }
    }

    internal static void PressAuxiliary(UnityEngine.InputSystem.Controls.ButtonControl key)
    {
        ToolkitMod.I.Audit.GuardStressCopy();
        if(_aux != null || key == null || key.isPressed) throw new InvalidOperationException("Auxiliary key already held; refusing to override user input.");
        _auxDevice=Keyboard.current;_aux=key;
        QueueKey(_auxDevice,key,true);
    }
    internal static void ReleaseAuxiliary()
    {
        var key=_aux;var keyboard=_auxDevice;
        try { if(key != null && keyboard != null && !keyboard.WasCollected) QueueKey(keyboard,key,false); }
        finally {_aux=null;_auxDevice=null;}
    }
    private static unsafe void QueueKey(Keyboard keyboard,UnityEngine.InputSystem.Controls.ButtonControl key,bool down)
    {
        var buffer=StateEvent.From(keyboard,out InputEventPtr eventPtr,Allocator.Temp);
        try { InputControlExtensions.WriteValueIntoEvent<float>(key,down?1f:0f,eventPtr);InputSystem.QueueEvent(eventPtr); }
        finally {buffer.Dispose();}
    }

    private static unsafe void QueueW(Keyboard keyboard,bool down)
    {
        // StateEvent.From allocates and snapshots the real device's correctly sized native state.
        // Do not construct the generated KeyboardState: its fixed-buffer wrapper has an incomplete managed layout.
        var buffer=StateEvent.From(keyboard,out InputEventPtr eventPtr,Allocator.Temp);
        try
        {
            InputControlExtensions.WriteValueIntoEvent<float>(keyboard.wKey,down?1f:0f,eventPtr);
            InputSystem.QueueEvent(eventPtr);
        }
        finally { buffer.Dispose(); }
    }
}
