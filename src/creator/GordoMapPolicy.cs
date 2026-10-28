using System;
using System.Collections.Generic;

namespace CreatorToolkit;

// Persistent instance identity, not species/translated name, is authoritative.
internal static class GordoMapPolicy
{
    internal enum State { Unknown, Hungry, Fed, Burst }
    internal enum Duplicate { Keep, Replace, Conflict }
    internal static string Key(string id)=>!string.IsNullOrWhiteSpace(id)&&id.Length<=512?id:null;
    internal static State Observe(bool modelPresent,int eaten,int target,int alreadyBurstFlag,bool conflict=false)
    {
        if(!modelPresent||conflict)return State.Unknown;
        if(eaten==alreadyBurstFlag)return State.Burst;
        if(eaten<0)return State.Unknown;
        if(target>0&&eaten>=target)return State.Fed;
        return State.Hungry;
    }
    internal static bool SceneAllowed(string source,ISet<string> canonicalScenes,bool complete)=>
        !complete||string.IsNullOrEmpty(source)||canonicalScenes==null||canonicalScenes.Contains(source);
    internal static Duplicate Merge(int currentRank,int candidateRank,bool sameGroup,bool samePosition)
    {
        if(candidateRank>currentRank)return Duplicate.Replace;
        if(candidateRank<currentRank)return Duplicate.Keep;
        return sameGroup&&samePosition?Duplicate.Keep:Duplicate.Conflict;
    }
    internal static bool DrawOverlay(string id,ISet<string> visibleNativeIds)=>
        visibleNativeIds==null||id==null||!visibleNativeIds.Contains(id);
}
