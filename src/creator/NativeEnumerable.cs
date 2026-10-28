using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes;

namespace CreatorToolkit;

// IL2CPP's generic IEnumerator wrapper does not expose inherited non-generic MoveNext.
// Cast the native iterator to its base interface; never reinterpret it as a CLR enumerable.
internal static class NativeEnumerable
{
    internal static IEnumerable<T> Items<T>(Il2CppSystem.Collections.Generic.IEnumerable<T> source) where T : Il2CppObjectBase
    {
        if (source == null) yield break;
        var iterator = source.GetEnumerator();
        var control = iterator.Cast<Il2CppSystem.Collections.IEnumerator>();
        try { while (control.MoveNext()) yield return iterator.Current; }
        finally { iterator.Cast<Il2CppSystem.IDisposable>().Dispose(); }
    }
}
