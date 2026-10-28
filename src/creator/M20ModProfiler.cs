using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;
namespace CreatorToolkit;

// Explicit 20-second, observe-only instrumentation. Measures this managed mod,
// not whole-engine CPU/GPU cost. No forced GC, input, caps, settings or saves.
internal sealed class M20ModProfiler
{
    private sealed class Counter
    {
        internal long Ticks,MaxTicks,Bytes;
        internal int Calls;
        internal void Add(long ticks,long bytes){Ticks+=ticks;MaxTicks=Math.Max(MaxTicks,ticks);Bytes+=Math.Max(0,bytes);Calls++;}
        internal object Snapshot(string name)=>new{Name=name,Calls,MeanMs=Calls>0?1000.0*Ticks/Stopwatch.Frequency/Calls:0,
            MaxMs=1000.0*MaxTicks/Stopwatch.Frequency,Bytes,BytesPerCall=Calls>0?(double)Bytes/Calls:0};
    }
    private readonly ToolkitMod _app;
    private readonly Dictionary<string,Counter> _sections=new();
    private Counter _update=new(),_gui=new();
    private IntPtr _session;
    private long _started;
    private int _failures,_focused,_menuFrames;
    private bool _capturing;
    private Task _writer;
    internal bool Active {get;private set;}
    internal bool Capturing=>Active&&_capturing;
    internal string Status {get;private set;}="M20Profiler: idle\n";
    internal M20ModProfiler(ToolkitMod app)=>_app=app;
    internal void Start()
    {
        if(Active||_writer?.IsCompleted==false)throw new InvalidOperationException("Profiler already active/writing");
        if(!_app.Ready||_app.Benchmark.Active||_app.Stress.Active||_app.Audit.Active||_app.BenchmarkPendingWork)
            throw new InvalidOperationException("Profiler needs stable gameplay and no benchmark/audit/pending work");
        _sections.Clear();_update=new();_gui=new();_focused=_menuFrames=0;
        _session=_app.Session;_failures=_app.FailureSerial;_started=Stopwatch.GetTimestamp();
        _capturing=false;Active=true;Status="M20Profiler: sampling 20s\n";
    }
    internal bool BeginUpdate()
    {
        if(!Active)return false;
        if(_session!=_app.Session||!_app.Ready){Finish("invalid-session-or-loading");return false;}
        if(_app.FailureSerial!=_failures){Finish("invalid-mod-error");return false;}
        if((Stopwatch.GetTimestamp()-_started)/(double)Stopwatch.Frequency>=20){Finish("completed");return false;}
        _capturing=true;if(Application.isFocused)_focused++;if(_app.MenuOpen)_menuFrames++;return true;
    }
    internal void RecordUpdate(long ticks,long bytes){if(Capturing)_update.Add(ticks,bytes);}
    internal void RecordGui(long ticks,long bytes){if(Capturing)_gui.Add(ticks,bytes);}
    internal void RecordSection(string name,long ticks,long bytes)
    {
        if(!Capturing)return;
        if(!_sections.TryGetValue(name,out var counter))
        {if(_sections.Count>=48)return;_sections.Add(name,counter=new());}
        counter.Add(ticks,bytes);
    }
    internal void Cancel(string reason){if(Active)Finish("cancelled-"+reason);}
    private void Finish(string reason)
    {
        Active=false;_capturing=false;
        double elapsed=(Stopwatch.GetTimestamp()-_started)/(double)Stopwatch.Frequency;
        var sections=new List<object>();foreach(var pair in _sections)sections.Add(pair.Value.Snapshot(pair.Key));
        string path=Path.Combine(_app.DataDirectory,"m20-profile-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json");
        var report=new{Reason=reason,ElapsedSeconds=elapsed,FocusedUpdates=_focused,MenuUpdates=_menuFrames,
            Update=_update.Snapshot("OnUpdate"),Gui=_gui.Snapshot("OnGUI"),Sections=sections,
            Scope="Managed mod main-thread only. Sections may nest; do not sum. GUI calls are not frames. Includes instrumentation overhead. No before/after FPS claim.",
            Settings=_app.Optimization.MaximumDiagnostics()};
        // Serialize immutable primitives before handing off filesystem work.
        string json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
        Status="M20Profiler: "+reason+"\nM20ProfilerFile: "+path+"\n";
        _writer=Task.Run(()=>{try{File.WriteAllText(path,json);}catch(Exception e){Status="M20Profiler: write-failed "+e.Message+"\n";}});
    }
}
