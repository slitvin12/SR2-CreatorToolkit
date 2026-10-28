using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CreatorToolkit;

// Copy-only native device events. No OS key injection or platform IME claim.
internal static class NativeTextProbe
{
    private static Keyboard _chordKeyboard;
    internal static int QueuedTextFrame {get;private set;}=-1;
    internal static bool HoldsDevice=>_chordKeyboard!=null;
    private static Keyboard Guard()
    {
        ToolkitMod.I.Audit.GuardStressCopy();
        return Keyboard.current??throw new InvalidOperationException("Native text probe keyboard unavailable.");
    }
    internal static void Type(string value)
    {
        var keyboard=Guard();QueuedTextFrame=Time.frameCount;
        foreach(char character in value)InputSystem.QueueTextEvent(keyboard,character);
    }
    internal static unsafe void SelectAll()
    {
        var keyboard=Guard();
        if(_chordKeyboard!=null||keyboard.leftCtrlKey.isPressed||keyboard.rightCtrlKey.isPressed||keyboard.aKey.isPressed)
            throw new InvalidOperationException("Ctrl/A held; text probe refuses to override user input.");
        var state=StateEvent.From(keyboard,out InputEventPtr ptr,Allocator.Temp);
        try
        {
            InputControlExtensions.WriteValueIntoEvent<float>(keyboard.leftCtrlKey,1,ptr);
            InputControlExtensions.WriteValueIntoEvent<float>(keyboard.aKey,1,ptr);
            InputSystem.QueueEvent(ptr);_chordKeyboard=keyboard;
        }
        finally{state.Dispose();}
    }
    internal static unsafe void Release()
    {
        var keyboard=_chordKeyboard;
        try
        {
            if(keyboard==null||keyboard.WasCollected)return;
            var state=StateEvent.From(keyboard,out InputEventPtr ptr,Allocator.Temp);
            try
            {
                InputControlExtensions.WriteValueIntoEvent<float>(keyboard.leftCtrlKey,0,ptr);
                InputControlExtensions.WriteValueIntoEvent<float>(keyboard.aKey,0,ptr);
                InputSystem.QueueEvent(ptr);
            }
            finally{state.Dispose();}
        }
        finally{_chordKeyboard=null;}
    }
    internal static unsafe void Ime(string composition)
    {
        var keyboard=Guard();
        const int bytes=152,headerBytes=20,characters=64;
        if(InputEvent.kBaseEventSize!=headerBytes||IMECompositionEvent.kIMECharBufferSize!=characters||
            composition.Length>characters)throw new InvalidOperationException("Native IME layout/capacity differs.");
        // IMECompositionEvent.Create would return the truncated generated
        // fixed-buffer value. Construct a correctly sized *native event*
        // instead, with a real StateEvent header/time/device snapshot.
        byte* raw=stackalloc byte[bytes];new Span<byte>(raw,bytes).Clear();
        var state=StateEvent.From(keyboard,out InputEventPtr snapshot,Allocator.Temp);
        try
        {
            Buffer.MemoryCopy(snapshot.data,raw,bytes,headerBytes);
            var header=(InputEvent*)raw;
            header->type=IMECompositionEvent.Type;header->sizeInBytes=bytes;header->eventId=0;
            *(int*)(raw+headerBytes)=composition.Length;
            for(int i=0;i<composition.Length;i++)((char*)(raw+headerBytes+4))[i]=composition[i];
            var ptr=new InputEventPtr(header);
            if(ptr.type!=IMECompositionEvent.Type||ptr.sizeInBytes!=bytes||ptr.deviceId!=keyboard.deviceId)
                throw new InvalidOperationException("Native IME event header readback differs.");
            InputSystem.QueueEvent(ptr);
        }
        finally{state.Dispose();}
    }
}