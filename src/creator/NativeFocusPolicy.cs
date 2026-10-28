using System;

namespace CreatorToolkit;

// Pure decision shared by the real native-owner tick and offline regression tests.
// Ownership must already be positively established; no cursor guesses or key hooks.
internal static class NativeFocusPolicy
{
    internal enum RootChoice { None, Display, Root }
    internal static RootChoice SelectActiveRoot(bool displayAlive,bool rootAlive)=>
        displayAlive?RootChoice.Display:rootAlive?RootChoice.Root:RootChoice.None;
    internal readonly record struct Identity(IntPtr Owner,IntPtr Manager,IntPtr Root);
    internal readonly record struct Decision(bool ReturnNavigation,bool AcquireMouse,bool UnlockCursor);
    internal static Decision Evaluate(Identity previous,Identity current,bool nativeMouseDevice)
    {
        if(current.Owner==IntPtr.Zero)return default;
        bool opening=previous!=current;
        return new Decision(true,opening,opening||current.Manager!=IntPtr.Zero&&nativeMouseDevice);
    }
}
