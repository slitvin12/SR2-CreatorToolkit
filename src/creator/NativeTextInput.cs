using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace CreatorToolkit;

internal sealed partial class ToolkitUi
{
    private readonly TextEditBuffer _textBuffer=new();
    private readonly Dictionary<string,string> _pendingTextWrites=new(StringComparer.Ordinal);
    private string _lastReturnedText="";
    private Keyboard _textKeyboard;
    private Il2CppSystem.Action<InputEventPtr,InputDevice> _imeEventCallback;
    private bool _imePreviouslySelected,_imeSubscribed,_imeOwned;
    private float _textHorizontalOffset,_nextKeyRepeat;
    private int _repeatKey=-1;
    private Vector2 _imeCursor=new(-10000,-10000);
    internal int TextListenerRootsForTest=>_textKeyboard==null?0:1;
    internal int TextCommitsForTest {get;private set;}
    internal int TextCallbacksForTest {get;private set;}
    internal int TextSpaceCallbacksForTest {get;private set;}
    internal int TextRejectedFocusForTest {get;private set;}
    internal int TextRejectedCharacterForTest {get;private set;}
    internal int ImePreviewsForTest {get;private set;}
    internal int RejectedImeEventsForTest {get;private set;}
    internal int RejectedTextEventsForTest {get;private set;}
    internal int LastTextEventFrameForTest {get;private set;}=-1;
    internal int LastTextBindFrameForTest {get;private set;}=-1;
    internal double LastTextCallbackMsForTest {get;private set;}
    internal string FocusedTextForTest=>_editingField.Length==0?"":_textBuffer.Value;
    internal string CompositionForTest=>_textBuffer.Composition;
    internal bool TextInputAbiForTest=>InputEvent.kBaseEventSize==20&&
        Marshal.SizeOf<InputEventPtr>()==IntPtr.Size&&Marshal.SizeOf<TextEvent>()==24&&
        Marshal.OffsetOf<TextEvent>(nameof(TextEvent.character)).ToInt32()==20;
    private bool AcceptText=>_textKeyboard!=null&&_editingField.Length>0&&_app.MenuOpen&&
        _app.Pending==null&&!_app.Map.Busy&&!_bodyInteractionBlocked;

    private void FocusTextInput(string id,string value,int maximum)
    {
        BlurTextInput();
        var keyboard=Keyboard.current;
        if(keyboard==null||!TextInputAbiForTest)
            throw new InvalidOperationException("Нативная клавиатура или безопасный формат текстовых событий недоступны.");
        _textBuffer.Focus(value,maximum);_editingField=id;_textHorizontalOffset=0;_lastReturnedText=_textBuffer.Value;
        _textKeyboard=keyboard;
        _imePreviouslySelected=keyboard.imeSelected.isPressed;
        try
        {
            _imeEventCallback=(Il2CppSystem.Action<InputEventPtr,InputDevice>)
                new Action<InputEventPtr,InputDevice>(OnNativeInputEvent);
            // The installed IL2CPP char-delegate trampoline did not preserve
            // our Cyrillic fixture. Read the native UTF-32 TextEvent instead;
            // never marshal a char through an unmanaged delegate.
            InputSystem.onEvent+=_imeEventCallback;_imeSubscribed=true;
            keyboard.SetIMEEnabled(true);_imeOwned=true;
        }
        catch{BlurTextInput();throw;}
    }
    private void BlurTextInput()
    {
        var keyboard=_textKeyboard;
        if(_editingField.Length>0&&_textBuffer.Value!=_lastReturnedText)
            _pendingTextWrites[_editingField]=_textBuffer.Value;
        try
        {
            if(_imeSubscribed&&_imeEventCallback!=null)InputSystem.onEvent-=_imeEventCallback;
        }
        finally
        {
            try
            {
                if(keyboard!=null&&!keyboard.WasCollected)
                {
                    if(_imeOwned)keyboard.SetIMEEnabled(_imePreviouslySelected);
                }
            }
            finally
            {
                _textKeyboard=null;_imeEventCallback=null;
                _imeSubscribed=_imeOwned=false;
                _editingField="";_textBuffer.Blur();_repeatKey=-1;_nextKeyRepeat=0;
                _textHorizontalOffset=0;_imeCursor=new Vector2(-10000,-10000);
            }
        }
    }
    private void CommitNativeScalar(int scalar)
    {
        if(scalar==' ')TextSpaceCallbacksForTest++;
        long started=Stopwatch.GetTimestamp();
        bool inserted=scalar<=char.MaxValue?_textBuffer.Type((char)scalar):
            _textBuffer.Insert(char.ConvertFromUtf32(scalar));
        if(inserted){TextCommitsForTest++;LastTextEventFrameForTest=Time.frameCount;}
        else TextRejectedCharacterForTest++;
        LastTextCallbackMsForTest=1000d*(Stopwatch.GetTimestamp()-started)/Stopwatch.Frequency;
    }
    private unsafe void OnNativeInputEvent(InputEventPtr ptr,InputDevice device)
    {
        if(_textKeyboard==null||device==null||device.deviceId!=_textKeyboard.deviceId||!ptr.valid)return;
        byte* raw=(byte*)ptr.data;
        if(ptr.type==TextEvent.Type)
        {
            TextCallbacksForTest++;
            if(!AcceptText){TextRejectedFocusForTest++;return;}
            if(raw==null||ptr.sizeInBytes<24){RejectedTextEventsForTest++;return;}
            int scalar=*(int*)(raw+20);
            if(!TextEditBuffer.ScalarValid(scalar)){RejectedTextEventsForTest++;return;}
            CommitNativeScalar(scalar);return;
        }
        if(!AcceptText||ptr.type!=IMECompositionEvent.Type)return;
        // Read the real native event buffer, not the generated by-value
        // IMECompositionString (whose fixed-buffer managed size is wrong).
        // This callback never handles, modifies or replays the input event.
        if(raw==null||ptr.sizeInBytes<24){RejectedImeEventsForTest++;return;}
        int count=*(int*)(raw+20);
        if(!TextEditBuffer.ImePayloadValid(ptr.sizeInBytes,count,InputEvent.kBaseEventSize,
            IMECompositionEvent.kIMECharBufferSize)){RejectedImeEventsForTest++;return;}
        _textBuffer.Preview(new string((char*)(raw+24),0,count));ImePreviewsForTest++;
    }
    internal void TickTextInput()
    {
        if(_textKeyboard==null)return;
        if(!AcceptText||_textKeyboard.WasCollected||Keyboard.current!=_textKeyboard)
        {BlurTextInput();return;}
        var kb=_textKeyboard;
        // The platform owns editing keys while an IME composition is active.
        if(_textBuffer.Composition.Length>0)return;
        if(kb.enterKey.wasPressedThisFrame||kb.numpadEnterKey.wasPressedThisFrame)
        {BlurTextInput();return;}
        bool control=kb.ctrlKey.isPressed;
        if(control&&kb.aKey.wasPressedThisFrame){_textBuffer.SelectAll();return;}
        if(control&&kb.vKey.wasPressedThisFrame){_textBuffer.Insert(GUIUtility.systemCopyBuffer);return;}
        if(control&&(kb.cKey.wasPressedThisFrame||kb.xKey.wasPressedThisFrame)&&_textBuffer.SelectedAll)
        {
            GUIUtility.systemCopyBuffer=_textBuffer.Value;
            if(kb.xKey.wasPressedThisFrame)_textBuffer.Delete();
            return;
        }
        KeyControl key=null;int command=-1;
        if(kb.backspaceKey.isPressed){key=kb.backspaceKey;command=0;}
        else if(kb.deleteKey.isPressed){key=kb.deleteKey;command=1;}
        else if(kb.leftArrowKey.isPressed){key=kb.leftArrowKey;command=2;}
        else if(kb.rightArrowKey.isPressed){key=kb.rightArrowKey;command=3;}
        else if(kb.homeKey.isPressed){key=kb.homeKey;command=4;}
        else if(kb.endKey.isPressed){key=kb.endKey;command=5;}
        if(key==null){_repeatKey=-1;return;}
        bool fresh=_repeatKey!=command||key.wasPressedThisFrame;
        if(!fresh&&Time.unscaledTime<_nextKeyRepeat)return;
        _repeatKey=command;_nextKeyRepeat=Time.unscaledTime+(fresh?.35f:.035f);
        switch(command)
        {
            case 0:_textBuffer.Backspace();break;case 1:_textBuffer.Delete();break;
            case 2:_textBuffer.Move(-1);break;case 3:_textBuffer.Move(1);break;
            case 4:_textBuffer.Home();break;case 5:_textBuffer.End();break;
        }
    }
    private void PlaceTextCaret(float x)
    {
        string value=_textBuffer.Value;int best=0;float error=Math.Abs(x);
        for(int i=1;i<=value.Length;i++)
        {
            if(i<value.Length&&char.IsLowSurrogate(value[i]))continue;
            float width=_label.CalcSize(ButtonContent(value.Substring(0,i))).x;
            float next=Math.Abs(width-x);
            if(next<error){error=next;best=i;}
        }
        _textBuffer.SetCaret(best);
    }
    private void PositionImeCursor(Rect inside,float caret)
    {
        if(_textKeyboard==null)return;
        var position=new Vector2((_groupOrigin.x+inside.x+caret-_textHorizontalOffset)*_scale,
            Screen.height-(_groupOrigin.y+inside.yMax)*_scale);
        if((position-_imeCursor).sqrMagnitude<1f)return;
        _textKeyboard.SetIMECursorPosition(position);_imeCursor=position;
    }
}