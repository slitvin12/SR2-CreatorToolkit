using System;
namespace CreatorToolkit;
internal static class M21WeatherPolicy
{
    internal const int MaxConfigZones=64;
    internal const float MappingCacheSeconds=30f;
    internal static bool Reuse(bool known,bool force,bool sameScene,double now,double expires,bool positive=false)=>
        known&&!force&&sameScene&&double.IsFinite(now)&&now>=0&&
        (positive||double.IsFinite(expires)&&now<expires);
    internal static bool CompleteRegistry(int configs,int distinctRegistered,int registered)=>
        configs>=0&&configs<=MaxConfigZones&&registered>0&&registered<=MaxConfigZones&&
        distinctRegistered>0&&distinctRegistered<=configs&&distinctRegistered==registered;
    internal struct UniqueZone
    {
        private long _zone;
        internal bool Ambiguous {get;private set;}
        internal long Zone=>Ambiguous?0:_zone;
        internal void Add(long zone)
        {
            if(zone==0)return;
            if(_zone!=0&&_zone!=zone)Ambiguous=true;
            else _zone=zone;
        }
    }
}

internal static class M21MapPolicy
{
    internal static bool ObserveDrones(bool menu,bool map)=>menu||map;
}
