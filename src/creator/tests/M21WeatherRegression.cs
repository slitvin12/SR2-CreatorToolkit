using System;
using System.IO;
using System.Linq;
using CreatorToolkit;
internal static class M21WeatherRegression
{
    internal static void Run(Action<bool,string> report,string root)
    {
        void Check(bool ok,string name)=>report(ok,"m15 / followup / m21 / "+name);
        foreach(bool known in new[]{false,true})foreach(bool force in new[]{false,true})foreach(bool same in new[]{false,true})
            Check(M21WeatherPolicy.Reuse(known,force,same,10,30)==(known&&!force&&same),"mapping scope and explicit force "+known+force+same);
        foreach(double now in new[]{double.NaN,double.NegativeInfinity,double.PositiveInfinity,-1,30,31})
            Check(!M21WeatherPolicy.Reuse(true,false,true,now,30),"invalid or expired mapping time "+now);
        foreach(double expiry in new[]{double.NaN,double.NegativeInfinity,double.PositiveInfinity,-1,0,10})
            Check(!M21WeatherPolicy.Reuse(true,false,true,10,expiry),"invalid or elapsed mapping expiry "+expiry);
        Check(M21WeatherPolicy.Reuse(true,false,true,29.999,30),"cache usable strictly before TTL");
        Check(M21WeatherPolicy.MappingCacheSeconds==30,"negative metadata cache has finite thirty-second rediscovery bound");
        Check(M21WeatherPolicy.Reuse(true,false,true,300,30,true),"positive live mapping does not regress into recurring global scans after TTL");
        Check(!M21WeatherPolicy.Reuse(true,true,true,300,30,true)&&!M21WeatherPolicy.Reuse(true,false,false,300,30,true),"positive mappings still honor explicit force and scene invalidation");
        foreach(bool menu in new[]{false,true})foreach(bool map in new[]{false,true})Check(M21MapPolicy.ObserveDrones(menu,map)==(menu||map),"drone census only for UI consumer "+menu+map);
        foreach(int configs in new[]{-1,0,1,6,64,65})foreach(int distinct in new[]{0,1,5,6,64})foreach(int registered in new[]{0,1,6,64,65})
            Check(M21WeatherPolicy.CompleteRegistry(configs,distinct,registered)==
                (configs>=0&&configs<=64&&registered>0&&registered<=64&&distinct>0&&distinct<=configs&&distinct==registered),
                "complete authoritative registry coverage "+configs+" / "+distinct+" / "+registered);
        long[] zones={0,17,29,-3,long.MaxValue};
        foreach(long a in zones)foreach(long b in zones)foreach(long c in zones)
        {
            M21WeatherPolicy.UniqueZone result=default;result.Add(a);result.Add(b);result.Add(c);
            var distinct=new[]{a,b,c}.Where(x=>x!=0).Distinct().ToArray();
            Check(result.Ambiguous==(distinct.Length>1)&&result.Zone==(distinct.Length==1?distinct[0]:0),"unique live zone / duplicates / ambiguity "+a+" / "+b+" / "+c);
        }
        string Read(string n)=>File.ReadAllText(Path.Combine(root,"src","creator",n));
        string world=Read("WorldTools.cs"),host=Read("ToolkitMod.cs"),audit=Read("M21WeatherAudit.cs");
        Check(!world.Contains("FindObjectsOfType<WeatherDirector>")&&world.Contains("registry.ZoneConfigList")&&world.Contains("registry._zones.TryGetValue(zone,out var data)"),"production fallback reads indexed native registry, never scene-wide director census");
        Check(world.Contains("_resolvedMappingKnown=true")&&world.Contains("else if(_resolvedWeatherZone==null)WeatherNegativeCacheHits++")&&world.Contains("M21WeatherPolicy.Reuse(_resolvedMappingKnown,force"),"missing direct mapping cached independently of fresh fallback");
        Check(world.Contains("_resolvedPrimaryZone==current.Pointer")&&world.Contains("_resolvedWeatherRegistry==registry.Pointer")&&world.Contains("stamp==_resolvedSceneStamp")&&host.Contains("World?.InvalidateWeatherResolution()"),"zone registry streaming/session invalidations preserve cache scope");
        Check(world.Contains("RefreshWeather(false)")&&world.Contains("internal void RefreshWeather()=>RefreshWeather(true)")&&world.Contains("entry.Zone != ResolveWeatherZone(true)"),"background reuses metadata; explicit refresh and SetWeather revalidate fresh target");
        int clear=world.IndexOf("internal void ClearWeather()");Check(clear>=0&&world[clear..].IndexOf("RefreshWeather();")<world[clear..].IndexOf("StopCurrentWeather();"),"clear weather forces current target resolution before native action");
        Check(world.Contains("M21WeatherPolicy.CompleteRegistry")&&world.Contains("return unique.Ambiguous?null:live")&&world.Contains("actual.Pointer!=zone.Pointer"),"partial registry ambiguity and mismatched native director fail closed");
        Check(world.Contains("director.gameObject.scene.isLoaded")&&world.Contains("director.WasCollected")&&world.Contains("_registeredWeatherZones.Clear()"),"unloaded/destroyed directors rejected and scratch IDs bounded/reset");
        Check(audit.Contains("FindObjectsOfType<WeatherDirector>")&&audit.Contains("Matches=Pointer(legacy)==Pointer(resolved)")&&host.Contains("case \"m21-weather-audit\""),"explicit read-only audit compares production target to legacy global result");
        string atlas=Read("MapTools.cs");
        Check(atlas.Contains("M21MapPolicy.ObserveDrones(_app.MenuOpen,NativeMapOpen)")&&atlas.Contains("_droneObservationRequested=false;_nextDroneObservation=0;return"),"closed UI skips actor census and next open is immediate");
        Check(atlas.Contains("Points.RemoveAll(p=>p.DroneObservation);ResetGhostRouteStates()")&&atlas.Contains("if(Selected?.DroneObservation==true)Selected=null"),"hidden moving observations retired and authored phantom anchors retained");
        Check(audit.Contains("FallbackMatches=Pointer(fallback)==Pointer(legacyFallback)"),"registry fallback independently compared even when primary mapping already warm");
        Check(!audit.Contains("DEBUG_SetZoneWeather(")&&!audit.Contains("RunState(")&&!audit.Contains("StopState(")&&!audit.Contains("File.Write")&&!audit.Contains("SetHealth("),"weather equivalence audit never changes weather saves actors or health");
    }
}
