using System;
using System.Globalization;
using System.Text;

namespace CreatorToolkit;

// Pure Unicode editing state. Exactly one focused field owns its caret;
// rendering another empty text field can never reset that caret.
internal sealed class TextEditBuffer
{
    internal string Value {get;private set;}="";
    internal string Composition {get;private set;}="";
    internal int Caret {get;private set;}
    internal bool SelectedAll {get;private set;}
    internal int Maximum {get;private set;}=80;
    private char _highSurrogate;
    private int[] _boundaries=Array.Empty<int>();
    internal void Focus(string value,int maximum)
    {
        Maximum=Math.Clamp(maximum,1,120);
        SetValue(Sanitize(value,Maximum));Caret=Value.Length;
        SelectedAll=false;Composition="";_highSurrogate='\0';
    }
    private void SetValue(string value)
    {Value=value;_boundaries=StringInfo.ParseCombiningCharacters(Value);}
    internal void SetCaret(int position)
    {
        position=Math.Clamp(position,0,Value.Length);
        Caret=position==Value.Length?position:PreviousBoundary(position+1);
        SelectedAll=false;_highSurrogate='\0';
    }
    private int PreviousBoundary(int position)
    {
        int answer=0;
        foreach(int boundary in _boundaries){if(boundary>=position)break;answer=boundary;}
        return answer;
    }
    private int NextBoundary(int position)
    {foreach(int boundary in _boundaries)if(boundary>position)return boundary;return Value.Length;}
    internal void SelectAll(){SelectedAll=true;Caret=Value.Length;_highSurrogate='\0';}
    internal void Move(int direction)
    {
        Caret=SelectedAll?(direction<0?0:Value.Length):
            direction<0?PreviousBoundary(Caret):NextBoundary(Caret);
        SelectedAll=false;_highSurrogate='\0';
    }
    internal void Home(){Caret=0;SelectedAll=false;_highSurrogate='\0';}
    internal void End(){Caret=Value.Length;SelectedAll=false;_highSurrogate='\0';}
    internal void Backspace()
    {
        if(SelectedAll){SetValue("");Caret=0;}
        else if(Caret>0)
        {
            int previous=PreviousBoundary(Caret);
            SetValue(Value.Remove(previous,Caret-previous));Caret=previous;
        }
        SelectedAll=false;_highSurrogate='\0';Composition="";
    }
    internal void Delete()
    {
        if(SelectedAll){SetValue("");Caret=0;}
        else if(Caret<Value.Length)SetValue(Value.Remove(Caret,NextBoundary(Caret)-Caret));
        SelectedAll=false;_highSurrogate='\0';Composition="";
    }
    internal bool Type(char character)
    {
        if(char.IsControl(character)||character=='\0')return false;
        if(char.IsHighSurrogate(character)){_highSurrogate=character;return false;}
        if(char.IsLowSurrogate(character))
        {
            if(_highSurrogate=='\0')return false;
            string pair=new(new[]{_highSurrogate,character});_highSurrogate='\0';
            return Insert(pair);
        }
        _highSurrogate='\0';return Insert(character.ToString());
    }
    internal bool Insert(string text)
    {
        string clean=Sanitize(text,Maximum);
        int remaining=Maximum-(SelectedAll?0:Value.Length);
        clean=Sanitize(clean,remaining);
        Composition="";_highSurrogate='\0';
        if(clean.Length==0)return false;
        if(SelectedAll){SetValue("");Caret=0;SelectedAll=false;}
        SetValue(Value.Insert(Caret,clean));Caret+=clean.Length;return true;
    }
    internal void Preview(string composition)
    {Composition=Sanitize(composition,64);_highSurrogate='\0';}
    internal void Blur(){Composition="";SelectedAll=false;_highSurrogate='\0';}
    internal string PreviewValue=>Composition.Length==0?Value:
        SelectedAll?Composition:Value.Insert(Caret,Composition);
    internal string CaretPrefix=>SelectedAll&&Composition.Length>0?Composition:
        Value.Substring(0,Caret)+Composition;
    internal static string Sanitize(string value,int maximum)
    {
        if(string.IsNullOrEmpty(value)||maximum<=0)return "";
        var result=new StringBuilder(Math.Min(value.Length,maximum));
        for(int i=0;i<value.Length&&result.Length<maximum;i++)
        {
            char c=value[i];
            if(char.IsHighSurrogate(c))
            {
                if(i+1<value.Length&&char.IsLowSurrogate(value[i+1])&&result.Length+2<=maximum)
                {result.Append(c).Append(value[++i]);}
                continue;
            }
            if(char.IsLowSurrogate(c)||char.IsControl(c))continue;
            result.Append(c);
        }
        return result.ToString();
    }
    // Actual native IME layout: 20-byte InputEvent + int length + 64 UTF-16
    // code units. The generated IMECompositionString struct has a truncated
    // fixed buffer, so it must never be passed through a managed value bridge.
    internal static bool ImePayloadValid(uint bytes,int length,int header,int capacity)=>
        header==20&&capacity==64&&length>=0&&length<=capacity&&
        bytes>=(uint)(header+4)&&bytes>=(uint)(header+4+length*2);
    internal static bool ScalarValid(int scalar)=>
        scalar>=0&&scalar<=0x10ffff&&(scalar<0xd800||scalar>0xdfff);
}