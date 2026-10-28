using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CreatorToolkit;

// Bounded test evidence: CPU time of IMGUI tab-body handling, NOT GPU/display
// presentation or physical-input latency. No game objects or native wrappers.
internal sealed class UiPaintTelemetry
{
    internal sealed class Row
    {
        public int Tab {get;set;}
        public string Name {get;set;}
        public bool FirstInSession {get;set;}
        public bool NativeClick {get;set;}
        public double BodyCpuMs {get;set;}
        public double? ProcessedClickToRepaintMs {get;set;}
        public int Frame {get;set;}
    }
    private readonly bool[] _first=new bool[8];
    private readonly int[] _warm=new int[8];
    internal readonly List<Row> Rows=new(96);
    internal bool Wants(int tab,bool clicked)=>tab>=0&&tab<8&&(!_first[tab]||_warm[tab]<8||clicked)&&Rows.Count<96;
    internal void Record(int tab,string name,double cpu,double? click,int frame)
    {
        if(!Wants(tab,click.HasValue)||!double.IsFinite(cpu)||cpu<0||
            (click.HasValue&&(!double.IsFinite(click.Value)||click.Value<0)))return;
        bool first=!_first[tab];_first[tab]=true;
        if(!first&&!click.HasValue)_warm[tab]++;
        Rows.Add(new Row{Tab=tab,Name=name,FirstInSession=first,NativeClick=click.HasValue,
            BodyCpuMs=cpu,ProcessedClickToRepaintMs=click,Frame=frame});
    }
    internal void Reset()
    {
        Rows.Clear();Array.Clear(_first,0,_first.Length);Array.Clear(_warm,0,_warm.Length);
    }
    internal void Write(string directory)=>File.WriteAllText(Path.Combine(directory,"tab-paint-profile.json"),
        JsonSerializer.Serialize(new{Scope="Native-copy IMGUI tab-body CPU handling. FirstInSession is the first measured body repaint after session bind. ProcessedClickToRepaintMs begins when the mod handles the native InputSystem click, not at physical OS input, and ends in CPU repaint handling, not GPU presentation. Eight early warm samples per tab plus bounded native clicks. Styles/font warmup outside the body is not included.",
            Rows},new JsonSerializerOptions{WriteIndented=true}));
}