using System;
using System.Collections.Generic;
using System.Text.Json;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.World;
using Il2CppMonomiPark.SlimeRancher.Weather;
using UnityEngine;
namespace CreatorToolkit;
// Explicit read-only diagnostic only. Legacy global searches never run in Tick.
internal static class M21WeatherAudit
{
    internal static string Snapshot(ToolkitMod app)
    {
        if(!app.Ready)throw new InvalidOperationException("Weather audit needs loaded gameplay");
        var registry=app.Context.WeatherRegistry;
        var current=app.Context.PlayerZoneTracker?.GetCurrentZone()??app.Context.PlayerZoneTracker?.GetLatestZone();
        ZoneDefinition legacy=null;
        if(current!=null)
        {
            foreach(var map in Resources.FindObjectsOfTypeAll<ZoneMapData>())
                if(map!=null&&map.PrimaryZone==current&&map.WeatherZone!=null&&registry._zones.ContainsKey(map.WeatherZone))
                {legacy=map.WeatherZone;break;}
            if(legacy==null&&registry._zones.ContainsKey(current))legacy=current;
        }
        var sceneZones=new HashSet<long>();ZoneDefinition sceneCandidate=null;
        foreach(var director in UnityEngine.Object.FindObjectsOfType<WeatherDirector>())
        {
            if(director==null||!director.enabled||!director.gameObject.activeInHierarchy||director.Zone==null||!registry._zones.ContainsKey(director.Zone))continue;
            sceneZones.Add(director.Zone.Pointer.ToInt64());
            sceneCandidate=director.Zone;
        }
        if(legacy==null&&sceneZones.Count==1)legacy=sceneCandidate;
        var resolved=app.World.WeatherZoneForAudit(true);
        var fallback=app.World.WeatherFallbackForAudit();
        var legacyFallback=sceneZones.Count==1?sceneCandidate:null;
        long Pointer(ZoneDefinition zone)=>zone==null?0:zone.Pointer.ToInt64();
        var row=new{Utc=DateTime.UtcNow,Current=current?.DebugNameKey,Legacy=legacy?.DebugNameKey,Resolved=resolved?.DebugNameKey,
            Matches=Pointer(legacy)==Pointer(resolved),FallbackMatches=Pointer(fallback)==Pointer(legacyFallback),
            RegistryFallback=fallback?.DebugNameKey,LegacyFallback=legacyFallback?.DebugNameKey,LegacyPointer=Pointer(legacy),ResolvedPointer=Pointer(resolved),
            GlobalLiveZones=sceneZones.Count,RegistryZones=registry._zones.Count,ConfigCount=registry.ZoneConfigList?.Count,
            app.World.WeatherZoneDiscoveryScans,app.World.WeatherNegativeCacheHits,app.World.WeatherRegistryFallbackReads,app.World.WeatherRegistryCoverageFailures};
        return "M21_WEATHER "+JsonSerializer.Serialize(row)+"\n";
    }
}
