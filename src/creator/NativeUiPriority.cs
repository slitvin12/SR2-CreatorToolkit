using System;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppMonomiPark.SlimeRancher.UI.Framework.Displays;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CreatorToolkit;

// Read the game's published UI state, not Time.timeScale, cursor appearance,
// a guessed display name or a global object scan. Never call Gather/Apply UI
// state or ForceUpdateInputMaps from inside the input-map Harmony prefix.
internal static class NativeUiPriority
{
    internal static string LastReadError="";
    private static bool WantsFocus(UIRuntimeDisplay display)=>
        display!=null&&!display.WasCollected&&display.DisplayConfig!=null&&
        display.DisplayConfig.FocusMode!=UIDisplayFocusMode.NO_FOCUS;
    internal static bool HasNativeFocus
    {
        get
        {
            try
            {
                LastReadError="";
                var manager=UIDisplayManager._instance;
                var state=manager==null||manager.WasCollected?null:manager._lastState;
                // HUD/tutorial popups are native displays too, but NO_FOCUS
                // explicitly permits gameplay. Merely existing in a runtime
                // layer or pending list is NOT a modal ownership claim.
                if(state!=null&&(WantsFocus(state.TopFocus)||WantsFocus(state.TopPossiblyFocusable)))return true;
                var top=GameContext.Instance?.UIDirector?.TopStackableUI;
                if(top==null||top.WasCollected)return false;
                var fake=top.TryCast<UIDisplayManager.FakeStackable>();
                if(fake!=null)return fake.IsFocused;
                // HudUI implements IStackableUI and is always the bottom of
                // the native stack. It is not a BaseUI modal and deliberately
                // keeps gameplay input; never classify it as an unknown UI.
                if(top.TryCast<HudUI>()!=null)return false;
                var legacy=top.TryCast<BaseUI>();
                return legacy==null||legacy.gameObject.activeInHierarchy&&legacy._closeInput!=null;
            }
            catch(Exception e){LastReadError=e.Message;return true;} // Fail closed with diagnostic evidence.
        }
    }
    internal static bool IsFrontDisplay(Component component)
    {
        if(component==null||component.WasCollected||!component.gameObject.activeInHierarchy)return false;
        try
        {
            var manager=UIDisplayManager._instance;
            if(manager==null||manager.WasCollected)return false;
            var state=manager._lastState;
            var top=WantsFocus(state?.TopFocus)?state.TopFocus:state?.TopPossiblyFocusable;
            if(!WantsFocus(top))return false;
            if(top==null||top.WasCollected||top.IsClosing)return false;
            var owner=manager.FindDisplayByObjectInHierarchy(component);
            return owner!=null&&!owner.WasCollected&&owner.Pointer==top.Pointer;
        }
        catch { return false; }
    }
    private static IntPtr _pointerOwner,_pointerManager,_pointerRoot;
    internal static int FocusGeneration {get;private set;}
    private static bool ActiveRoot(GameObject root)=>root!=null&&!root.WasCollected&&root.activeInHierarchy;

    // Called from the unconditional update path, not from HasNativeFocus or
    // the input-map prefix. Read-only ownership checks must remain side-effect free.
    internal static bool TickNativeFocus()
    {
        try
        {
            var manager=UIDisplayManager._instance;
            var state=manager==null||manager.WasCollected?null:manager._lastState;
            var display=WantsFocus(state?.TopFocus)?state.TopFocus:state?.TopPossiblyFocusable;
            IntPtr owner=IntPtr.Zero,managerId=IntPtr.Zero,rootId=IntPtr.Zero;
            if(WantsFocus(display)&&!display.IsClosing)
            {
                // Authoritative manager ownership covers the actual PediaRoot
                // and Map prefabs without guessing a SlimepediaUI class/name.
                // A pooled DisplayGameObject may exist before activation or
                // after collection. Null-coalescing alone hides a live root.
                var displayRoot=display.DisplayGameObject;
                var fallbackRoot=display.RootGameObject;
                var choice=NativeFocusPolicy.SelectActiveRoot(ActiveRoot(displayRoot),ActiveRoot(fallbackRoot));
                var root=choice==NativeFocusPolicy.RootChoice.Display?displayRoot:
                    choice==NativeFocusPolicy.RootChoice.Root?fallbackRoot:null;
                if(root!=null)
                {
                    owner=display.Pointer;managerId=manager.Pointer;rootId=root.Pointer;
                }
            }
            if(owner==IntPtr.Zero)
            {
                var top=GameContext.Instance?.UIDirector?.TopStackableUI;
                if(top!=null&&!top.WasCollected&&top.TryCast<HudUI>()==null&&
                    top.TryCast<UIDisplayManager.FakeStackable>()==null)
                {
                    var legacy=top.TryCast<BaseUI>();
                    if(legacy!=null&&legacy.gameObject.activeInHierarchy&&legacy._closeInput!=null)
                    {
                        owner=legacy.Pointer;rootId=legacy.gameObject.Pointer;
                    }
                }
            }
            if(owner==IntPtr.Zero)
            {
                _pointerOwner=_pointerManager=_pointerRoot=IntPtr.Zero;
                return false; // Gameplay/NO_FOCUS/closing/unknown: never unlock.
            }
            var decision=NativeFocusPolicy.Evaluate(
                new NativeFocusPolicy.Identity(_pointerOwner,_pointerManager,_pointerRoot),
                new NativeFocusPolicy.Identity(owner,managerId,rootId),
                managerId!=IntPtr.Zero&&manager.FocusModeDevice==UIDisplayFocusModeDevice.MOUSE);
            bool changed=false;
            if(decision.ReturnNavigation)ReturnNavigation();
            if(decision.AcquireMouse)
            {
                unchecked {FocusGeneration++;}
                // Explicit policy: native modal opening starts in MOUSE even
                // with an idle/absent mouse. Subsequent controller input may win.
                changed=ReturnActiveMouseDevice();
                Cursor.lockState=CursorLockMode.None;
                Cursor.visible=true;
            }
            else if(decision.UnlockCursor)
            {
                // Repair late native cursor writes only while a real modal
                // owns mouse focus; never force MOUSE every idle frame.
                Cursor.lockState=CursorLockMode.None;
                Cursor.visible=true;
            }
            _pointerOwner=owner;_pointerManager=managerId;_pointerRoot=rootId;
            return changed;
        }
        catch(Exception e)
        {
            LastReadError=e.Message;
            return false; // No speculative cursor writes on a failed read.
        }
    }
    internal static string Diagnostics()
    {
        try
        {
            var manager=UIDisplayManager._instance;var state=manager?._lastState;
            var top=GameContext.Instance?.UIDirector?.TopStackableUI;
            var fake=top?.TryCast<UIDisplayManager.FakeStackable>();
            return $"NativeUiOwnsInput: {HasNativeFocus}\nNativeUiFocusedDisplay: {state?.TopFocus?.Name}\n"+
                $"NativeUiFocusedMode: {state?.TopFocus?.DisplayConfig?.FocusMode}\n"+
                $"NativeUiDeviceMode: {manager?.FocusModeDevice}\n"+
                $"NativeUiPossibleDisplay: {state?.TopPossiblyFocusable?.Name}\n"+
                $"NativeUiPossibleMode: {state?.TopPossiblyFocusable?.DisplayConfig?.FocusMode}\n"+
                $"NativeUiLegacyType: {top?.TryCast<Il2CppSystem.Object>()?.GetIl2CppType().FullName}\nNativeUiFakeFocused: {fake?.IsFocused}\n"+
                $"NativeUiPendingRequests: {manager?._pendingDisplays?.Count}\n"+
                $"NativeUiNavigationEvents: {EventSystem.current?.sendNavigationEvents}\n"+
                $"NativeUiFocusGeneration: {FocusGeneration}\nNativeUiReadError: {LastReadError}\n";
        }
        catch(Exception e){return "NativeUiDiagnosticError: "+e.Message+"\n";}
    }
    internal static void ReturnNavigation()
    {
        var events=EventSystem.current;
        if(events!=null&&!events.WasCollected)events.sendNavigationEvents=true;
        // Cursor repair is restricted to positively identified modal ownership
        // in TickNativeFocus; never restore the mod's gameplay snapshot here.
    }
    internal static bool ReturnActiveMouseDevice()
    {
        ReturnNavigation();
        var manager=UIDisplayManager._instance;
        if(manager==null||manager.WasCollected||manager.FocusModeDevice==UIDisplayFocusModeDevice.MOUSE)
            return false;
        manager.SetFocusModeDevice(UIDisplayFocusModeDevice.MOUSE);
        return true;
    }
}