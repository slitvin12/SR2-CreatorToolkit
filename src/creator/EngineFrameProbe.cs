using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace CreatorToolkit;

// Opt-in stress telemetry only. No global Profiler.enabled/quality/GC changes.
// Recorders refer to engine counters, never to a player/world. Dispose before
// lifecycle memory comparisons, and on every failed/cancelled test exit.
internal sealed class EngineFrameProbe : IDisposable
{
    internal sealed class Value
    {
        public string Name {get;set;}
        public string Unit {get;set;}
        public long Raw {get;set;}
    }
    internal sealed class Summary
    {
        public string Name {get;set;}
        public string Unit {get;set;}
        public int Samples {get;set;}
        public double MeanRaw {get;set;}
        public long MaxRaw {get;set;}
    }
    internal sealed class Hitch
    {
        public string Window {get;set;}
        public double ElapsedSeconds {get;set;}
        public double FrameMs {get;set;}
        public List<Value> LastCompletedNativeSamples {get;set;}
        public bool FrameTimingAvailable {get;set;}
        public double NativeCpuFrameMs {get;set;}
        public double NativeMainThreadMs {get;set;}
        public double NativeRenderThreadMs {get;set;}
        public double NativeGpuMs {get;set;}
    }
    private sealed class Counter
    {
        internal string Name,Unit;
        internal ProfilerRecorder Recorder;
        internal long Last,Max;
        internal double Sum;
        internal int Samples;
        internal bool HasValue;
    }
    private readonly List<Counter> _counters=new(8);
    private readonly List<string> _notes;
    private Il2CppStructArray<FrameTiming> _timings;
    private FrameTiming _lastTiming;
    private bool _timingEnabled,_hasTiming;
    internal bool ProfilerSupported {get;private set;}
    internal bool TimingFeatureEnabled {get;private set;}
    internal int ActiveRecorders=>_counters.Count;

    internal EngineFrameProbe(List<string> notes)
    {
        _notes=notes;
        try{ProfilerSupported=Profiler.supported;}
        catch(Exception e){Note("Profiler.supported unavailable: "+e.Message);}
        foreach(string name in new[]{"Main Thread","Render Thread","GC.Collect","Gfx.WaitForPresentOnGfxThread","Gfx.WaitForGfxCommandsFromMainThread","GC Allocated In Frame"})
            AddCounter(name);
        try
        {
            TimingFeatureEnabled=FrameTimingManager.IsFeatureEnabled();
            if(TimingFeatureEnabled){_timings=new Il2CppStructArray<FrameTiming>(1);_timingEnabled=true;}
            else Note("Native FrameTimingManager feature is disabled; GPU/CPU frame attribution is unavailable.");
        }
        catch(Exception e){Note("Native FrameTimingManager unavailable: "+e.Message);}
    }
    private void Note(string text){if(_notes.Count<64)_notes.Add(text);}
    private unsafe void AddCounter(string name)
    {
        ProfilerRecorder recorder=default;bool owns=false;
        try
        {
            // Use the actual native UTF-8 lookup, not an unstripped managed
            // string-pointer helper with runtime-specific string layout.
            byte[] utf8=Encoding.UTF8.GetBytes(name);
            ProfilerRecorderHandle handle;
            fixed(byte* p=utf8)handle=ProfilerRecorderHandle.GetByName__Unmanaged(ProfilerCategory.Any,p,utf8.Length);
            if(!handle.Valid){Note("Native recorder not registered: "+name);return;}
            recorder=new ProfilerRecorder(handle,1,ProfilerRecorderOptions.Default|ProfilerRecorderOptions.StartImmediately);
            owns=recorder.Valid;
            if(!owns){Note("Native recorder unavailable: "+name);return;}
            string unit=ProfilerRecorderHandle.GetDescription(handle).UnitType.ToString();
            _counters.Add(new Counter{Name=name,Unit=unit,Recorder=recorder});
            owns=false; // ownership moved into the bounded list
        }
        catch(Exception e){Note("Native recorder "+name+" unavailable: "+e.Message);}
        finally{if(owns)try{recorder.Dispose();}catch(Exception e){Note("Recorder cleanup "+name+": "+e.Message);}}
    }
    internal void BeginWindow()
    {
        foreach(var c in _counters){c.Last=c.Max=0;c.Sum=0;c.Samples=0;c.HasValue=false;}
        _hasTiming=false;
    }
    internal void SamplePreviousFrame()
    {
        foreach(var c in _counters)
        {
            try
            {
                if(!c.Recorder.Valid||c.Recorder.Count==0)continue;
                long value=c.Recorder.LastValue;
                c.Last=value;c.HasValue=true;c.Max=Math.Max(c.Max,value);c.Sum+=value;c.Samples++;
            }
            catch(Exception e)
            {
                Note("Recorder sampling "+c.Name+" stopped: "+e.Message);
                try{c.Recorder.Dispose();}catch(Exception cleanup){Note("Recorder disposal: "+cleanup.Message);}
                c.Recorder=default;
            }
        }
        if(!_timingEnabled)return;
        try
        {
            if(FrameTimingManager.GetLatestTimings(1,_timings)>0)
            {_lastTiming=_timings[0];_hasTiming=_lastTiming.frameStartTimestamp!=0;}
            FrameTimingManager.CaptureFrameTimings();
        }
        catch(Exception e){_timingEnabled=false;Note("Frame timing capture stopped: "+e.Message);}
    }
    internal List<Summary> Summaries()=>_counters.Select(c=>new Summary{Name=c.Name,Unit=c.Unit,Samples=c.Samples,
        MeanRaw=c.Samples==0?0:c.Sum/c.Samples,MaxRaw=c.Max}).ToList();
    internal Hitch CaptureHitch(string window,double elapsed,double frameMs)=>new()
    {
        Window=window,ElapsedSeconds=elapsed,FrameMs=frameMs,
        LastCompletedNativeSamples=_counters.Where(c=>c.HasValue).Select(c=>new Value{Name=c.Name,Unit=c.Unit,Raw=c.Last}).ToList(),
        FrameTimingAvailable=_hasTiming,NativeCpuFrameMs=_lastTiming.cpuFrameTime,NativeMainThreadMs=_lastTiming.cpuMainThreadFrameTime,
        NativeRenderThreadMs=_lastTiming.cpuRenderThreadFrameTime,NativeGpuMs=_lastTiming.gpuFrameTime
    };
    public void Dispose()
    {
        foreach(var c in _counters)
            try{if(c.Recorder.Valid)c.Recorder.Dispose();}catch(Exception e){Note("Native recorder cleanup "+c.Name+": "+e.Message);}
        _counters.Clear();_timings=null;_timingEnabled=_hasTiming=false;
    }
}