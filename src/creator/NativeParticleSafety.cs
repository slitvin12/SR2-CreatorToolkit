using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace CreatorToolkit;

// The game's stripped interop assembly omits CollisionModule.enabled and
// TriggerModule.enabled. Use only the engine's registered Unity 6000.0
// property bindings, with the native boxed module layout verified against the
// actual module wrapper. Missing registration => no native particle reduction.
// No field offsets, private engine memory reads or guessed disabled states.
internal static class NativeParticleSafety
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return:MarshalAs(UnmanagedType.I1)]
    private delegate bool EnabledGetter(IntPtr module);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EnabledSetter(IntPtr module,[MarshalAs(UnmanagedType.I1)]bool value);
    private static T Resolve<T>(string type,string method) where T:Delegate
    {
        IntPtr pointer=IL2CPP.il2cpp_resolve_icall("UnityEngine.ParticleSystem/"+type+"::"+method);
        return pointer==IntPtr.Zero?null:Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }
    // Exact registered names read from this installed UnityPlayer.dll. This
    // player registers the module getter/setter without "_Injected"; the
    // previous names correctly failed closed instead of touching particles.
    private static readonly EnabledGetter Collision=Resolve<EnabledGetter>("CollisionModule","get_enabled");
    private static readonly EnabledGetter Trigger=Resolve<EnabledGetter>("TriggerModule","get_enabled");
    private static readonly EnabledSetter CollisionSet=Resolve<EnabledSetter>("CollisionModule","set_enabled");
    private static readonly EnabledSetter TriggerSet=Resolve<EnabledSetter>("TriggerModule","set_enabled");
    internal static bool Supported=>Collision!=null&&Trigger!=null;
    internal static bool TryRead(ParticleSystem system,out bool collision,out bool trigger)
    {
        collision=trigger=true; // Fail closed, not an invented disabled module.
        if(!Supported||system==null||system.WasCollected)return false;
        var col=system.collision;var tri=system.trigger;
        collision=Collision(IL2CPP.il2cpp_object_unbox(col.Pointer));
        trigger=Trigger(IL2CPP.il2cpp_object_unbox(tri.Pointer));
        GC.KeepAlive(col);GC.KeepAlive(tri);
        return true;
    }
    internal static void SetOwnedFixture(ParticleSystem system,bool collision,bool trigger)
    {
        ToolkitMod.I.Audit.GuardStressCopy();
        if(system==null||system.WasCollected||system.name!="CreatorToolkitAmbientDustAudit"||
            !Supported||CollisionSet==null||TriggerSet==null)
            throw new InvalidOperationException("Owned native ParticleSystem safety fixture/bindings unavailable.");
        var col=system.collision;var tri=system.trigger;
        CollisionSet(IL2CPP.il2cpp_object_unbox(col.Pointer),collision);
        TriggerSet(IL2CPP.il2cpp_object_unbox(tri.Pointer),trigger);
        GC.KeepAlive(col);GC.KeepAlive(tri);
    }
}