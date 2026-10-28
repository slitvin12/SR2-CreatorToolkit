using System;
using Il2CppInterop.Runtime;
using Il2CppMonomiPark.SlimeRancher;

namespace CreatorToolkit;

// The generated field getter calls il2cpp_value_box(Nullable<T>): IL2CPP unwraps
// Nullable and returns a boxed T, which must NOT be interpreted as Nullable<T>.
// Copy the native value into a genuine Nullable box without nullable boxing.
internal static class NativeSaveMetadata
{
    private static IntPtr Field => IL2CPP.GetIl2CppField(Il2CppClassPointerStore<AutoSaveDirector>.NativeClassPtr, "_currentGameMetadata");
    internal static unsafe Il2CppSystem.Nullable<GameMetadata> Read(AutoSaveDirector save)
    {
        if(save==null || save.WasCollected) return null;
        var boxed=new Il2CppSystem.Nullable<GameMetadata>();
        IL2CPP.il2cpp_field_get_value(save.Pointer,Field,(void*)IL2CPP.il2cpp_object_unbox(boxed.Pointer));
        return boxed;
    }
    internal static unsafe void Write(AutoSaveDirector save,Il2CppSystem.Nullable<GameMetadata> value)
    {
        if(save==null || save.WasCollected || value==null) throw new InvalidOperationException("Native save metadata unavailable.");
        IL2CPP.il2cpp_field_set_value(save.Pointer,Field,(void*)IL2CPP.il2cpp_object_unbox(value.Pointer));
    }
}
