using System;
using System.Collections.Generic;

namespace CreatorToolkit;

// Pure evidence rules. A source is never promoted to a currently existing actor.
internal static class WorldSourceObservationPolicy
{
    internal enum Kind { ActualGold, PotentialGold, PotentialSlime, ActualContainer, PotentialContainer }
    internal enum PositionRole { ObservedActor, SourceOrigin, ConfiguredSpawnLocation }
    internal static readonly IReadOnlyList<string> GoldNames=Array.AsReadOnly(new[]
    {
        "ContainerFieldsGold01","ContainerStrandGold01","ContainerGorgeGold01","ContainerBluffsGold01",
        "ContainerLabyHubGold01","ContainerLabyWaterworksGold01","ContainerLabyLavaDepthsGold01",
        "ContainerLabyDreamlandsGold01","ContainerLabyTerrariumGold01","ContainerGoldSlime01"
    });
    internal static bool IsGoldName(string value)
    { foreach(string name in GoldNames)if(string.Equals(name,value,StringComparison.Ordinal))return true;return false; }
    internal static bool ValidKey(string value) => !string.IsNullOrWhiteSpace(value)&&value.Length<=256&&NoControls(value);
    private static bool NoControls(string value)
    { foreach(char c in value)if(char.IsControl(c))return false;return true; }
    internal static string Text(string value)
    {
        if(value==null)return "";
        if(value.Length>256)value=value.Substring(0,256);
        return value.Replace('\r',' ').Replace('\n',' ').Replace('\t',' ');
    }
    internal static bool FinitePosition(float x,float y,float z) => float.IsFinite(x)&&float.IsFinite(y)&&float.IsFinite(z);
    internal static bool Weight(float value) => float.IsFinite(value)&&value>=0;
    internal static bool ActualAllowed(Kind kind,bool exactType,bool liveObject,bool validScene,bool loadedScene,bool active,bool finitePosition) =>
        kind==Kind.ActualGold&&exactType&&liveObject&&validScene&&loadedScene&&active&&finitePosition;
    internal static bool ActualContainerAllowed(bool exactType,bool liveObject,bool validScene,bool loadedScene,bool active,bool finitePosition)=>
        exactType&&liveObject&&validScene&&loadedScene&&active&&finitePosition;
    internal static string PhysicalPointKey(Kind kind,string physical,string location)=>
        (kind is Kind.ActualGold or Kind.ActualContainer?"actual|":kind==Kind.PotentialSlime?"slime|":"crate|")+physical+"|"+location;
    internal static bool Fresh(float observedAt,float now,float ttl) => float.IsFinite(observedAt)&&float.IsFinite(now)&&
        float.IsFinite(ttl)&&ttl>0&&now>=observedAt&&now-observedAt<=ttl;
}
