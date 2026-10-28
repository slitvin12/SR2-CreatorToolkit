using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CreatorToolkit;

internal sealed class InputCapture
{
    private readonly List<InputAction> _baseline = new();
    private CursorLockMode _previousLock;
    private bool _previousVisible, _resumeUiMode;
    internal bool Active { get; private set; }
    internal int DisabledCount
    {
        get { int n=0; foreach(var a in _baseline) { try { if(a != null && !a.WasCollected && !a.enabled) n++; } catch { } } return n; }
    }
    internal int BaselineCount => _baseline.Count;
    internal int MissingAfterRestore { get; private set; }
    internal int NativeHandoffs { get; private set; }
    internal int NativeMouseModeReturns { get; private set; }
    private bool _nativeHandoff,_nativeHandoffHasFocused;
    private float _nativeHandoffUntil;
    private float _openAfterNativeCloseUntil;

    internal void Open()
    {
        if (Active || NativeUiPriority.HasNativeFocus) return;
        _nativeHandoff=false;
        _openAfterNativeCloseUntil=0;
        _previousLock = Cursor.lockState;
        _previousVisible = Cursor.visible;
        _resumeUiMode = _previousLock != CursorLockMode.Locked;
        // Cursor None also occurs when SR2 loses focus; it does NOT mean native UI maps were active.
        // Capture the real movement-action mode before the menu's prefix starts forcing UI mode.
        var app=ToolkitMod.I;
        var director=GameContext.Instance?.InputDirector;
        if(app != null && app.Ready && director != null)
        {
            var input=app.Context.Player.GetComponentInChildren<Il2CppMonomiPark.SlimeRancher.Player.CharacterController.SRCharacterInput>(true);
            if(input != null && input._moveVertical != null)
            {
                var movement=new Il2CppSystem.Collections.Generic.List<InputAction>();
                director.InputActionController.GetInputActionFromEvent(input._moveVertical,movement);
                if(movement.Count > 0)
                {
                    _resumeUiMode=true;
                    for(int i=0;i<movement.Count;i++) if(movement[i].enabled) { _resumeUiMode=false; break; }
                }
            }
        }
        _baseline.Clear();
        var enabled = InputSystem.ListEnabledActions();
        for (int i=0;i<enabled.Count;i++) if(enabled[i] != null) _baseline.Add(enabled[i]);
        Active = true;
        app?.UiForTests?.BeginOpenAnimation();
        try
        {
            if (director != null && !director.WasCollected) director.ForceUpdateInputMaps(true);
            Enforce();
        }
        catch { Close(); throw; }
    }

    internal void Enforce()
    {
        if (!Active) return;
        if(NativeUiPriority.HasNativeFocus)
        {
            ToolkitMod.I.YieldToNativeUi();
            return;
        }
        // Let SR2's own director manage gameplay/paused maps. Never disable arbitrary actions
        // individually: that bypasses native map ownership and can strand movement bindings.
        // Native UpdateInputMaps/ForceUpdateInputMaps prefixes retain UI mode without
        // tearing down and rebuilding action maps on every frame.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    internal void Close()
    {
        _openAfterNativeCloseUntil=0;
        _nativeHandoff=false;
        if (!Active) return;
        if(NativeUiPriority.HasNativeFocus){YieldToNative();return;}
        Active = false; // Drop the UpdateInputMaps guard BEFORE native map restoration.
        try
        {
            var director = GameContext.Instance?.InputDirector;
            if (director != null && !director.WasCollected) director.ForceUpdateInputMaps(_resumeUiMode);
            MissingAfterRestore = 0;
            foreach(var a in _baseline)
                if(a != null && !a.WasCollected && !a.enabled) MissingAfterRestore++;
        }
        finally
        {
            // Native focus may have arrived during ForceUpdateInputMaps. Never
            // overwrite it with the hidden/locked gameplay snapshot from Open.
            if(NativeUiPriority.HasNativeFocus)TickNativeHandoff();
            else
            {
                Cursor.lockState = _previousLock;
                Cursor.visible = _previousVisible;
            }
            _baseline.Clear();
        }
    }

    internal void YieldToNative()
    {
        if(!Active)return;
        Active=false;
        _openAfterNativeCloseUntil=0;
        _baseline.Clear();
        MissingAfterRestore=0;
        NativeHandoffs++;
        _nativeHandoff=_nativeHandoffHasFocused=true;
        TickNativeHandoff();
        // Native maps and pause state remain authoritative. The focus tick
        // restores pointer/navigation immediately, not after a mouse gesture.
    }
    internal void TickNativeHandoff()
    {
        // UpdateFrame calls this even with the mod closed. Native opening is
        // independent of an optional mod handoff/ExpectNativeWindow lease.
        if(NativeUiPriority.TickNativeFocus())NativeMouseModeReturns++;
        if(!_nativeHandoff)return;
        if(!NativeUiPriority.HasNativeFocus)
        {
            if((_nativeHandoffHasFocused&&Cursor.lockState==CursorLockMode.Locked&&Time.timeScale>0)||
                (!_nativeHandoffHasFocused&&Time.unscaledTime>_nativeHandoffUntil))_nativeHandoff=false;
            return;
        }
        _nativeHandoffHasFocused=true;
        // Initial MOUSE acquisition is handled above on native-owner changes.
        // Afterwards leave native keyboard/gamepad device selection alone; only
        // genuine mouse activity may return an existing owner to MOUSE mode.
        var mouse=Mouse.current;
        if(mouse==null||!mouse.enabled)return;
        bool pointerActivity=mouse.delta.ReadValue().sqrMagnitude>=1f||
            mouse.leftButton.wasPressedThisFrame||mouse.rightButton.wasPressedThisFrame||
            mouse.scroll.ReadValue().sqrMagnitude>=1f;
        if(pointerActivity&&NativeUiPriority.ReturnActiveMouseDevice())NativeMouseModeReturns++;
    }
    internal void ExpectNativeWindow()
    {
        _nativeHandoff=true;_nativeHandoffHasFocused=false;
        _nativeHandoffUntil=Time.unscaledTime+3f;
    }
    internal void OpenAfterNativeClose()
    {
        // Explicit "map settings" action only. Never reopen automatically after
        // a native window displaced the mod. Waiting also covers fade-out.
        _openAfterNativeCloseUntil=Time.unscaledTime+2f;
    }
    internal void TickDeferredOpen()
    {
        if(_openAfterNativeCloseUntil<=0)return;
        if(Time.unscaledTime>_openAfterNativeCloseUntil){_openAfterNativeCloseUntil=0;return;}
        if(!NativeUiPriority.HasNativeFocus)Open();
    }

    internal void RecoverGameplay()
    {
        Close();
        var app=ToolkitMod.I;
        if(app == null || !app.Ready) throw new InvalidOperationException("Сначала загрузите сейв.");
        var characterInput=app.Context.Player.GetComponentInChildren<Il2CppMonomiPark.SlimeRancher.Player.CharacterController.SRCharacterInput>(true);
        if(NativeUiPriority.HasNativeFocus || Time.timeScale <= 0f || characterInput == null || !characterInput._inputEnabled)
            throw new InvalidOperationException("Игрок сейчас заморожен или игра на паузе.");
        GameContext.Instance.InputDirector.ForceUpdateInputMaps(false);
        Cursor.lockState=CursorLockMode.Locked;
        Cursor.visible=false;
        app.Notify("Нативные карты управления восстановлены.");
    }
}
