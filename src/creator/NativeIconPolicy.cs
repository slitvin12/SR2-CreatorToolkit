using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CreatorToolkit;

// Pure declaration/selection policy. Resolved is a lookup snapshot, NOT a game,
// visual-QA or asset-load PASS. The caller must still check borrowed Sprite life.
internal static class NativeIconPolicy
{
    internal const int WeatherBudget=24, WeatherAttemptsPerEpoch=4;
    internal const float WeatherRetrySeconds=1f;
    // Text-only discovery coverage is separate from retained sprite/lookups.
    internal const int WeatherDiagnosticBudget=128;
    internal struct OwnerBindingState
    {
        internal int Owners;
        internal IntPtr Sprite;
        internal bool Conflict;
    }
    // Call only after validating the chosen typed native owner identity.
    // Global same-name sprite candidates never participate in this decision.
    internal static void AddOwnerIcon(ref OwnerBindingState state,IntPtr liveSprite)
    {
        state.Owners++;
        if(liveSprite==IntPtr.Zero)return;
        if(state.Sprite!=IntPtr.Zero&&state.Sprite!=liveSprite)state.Conflict=true;
        else state.Sprite=liveSprite;
    }
    internal static Availability OwnerAvailability(OwnerBindingState state)=>
        state.Conflict?Availability.Ambiguous:state.Sprite!=IntPtr.Zero?Availability.Resolved:
        state.Owners>0?Availability.NotLoaded:Availability.MissingNativeAsset;
    internal enum WeatherIconSource { Missing, MetadataIcon, PediaSmallIcon, PediaIcon }
    internal enum WeatherMerge { Keep, Replace, Ambiguous }
    internal struct WeatherLookupState
    {
        internal int Epoch,Attempts;
        internal float NextAttempt;
    }
    // A late-loading icon gets bounded direct retries, then another bounded
    // window at the next genuine native opening. No repaint or resource scan.
    internal static bool TryBeginWeatherLookup(ref WeatherLookupState state,int epoch,float now)
    {
        if(!float.IsFinite(now)||now<0)return false;
        if(state.Epoch!=epoch)state=new WeatherLookupState{Epoch=epoch};
        if(state.Attempts>=WeatherAttemptsPerEpoch||now<state.NextAttempt)return false;
        state.Attempts++;state.NextAttempt=now+WeatherRetrySeconds;
        return true;
    }
    internal static T SelectWeatherIcon<T>(Func<T> metadata,Func<T> small,Func<T> icon,
        Func<T,bool> live,out WeatherIconSource source) where T:class
    {
        T candidate=metadata();
        if(live(candidate)){source=WeatherIconSource.MetadataIcon;return candidate;}
        candidate=small();
        if(live(candidate)){source=WeatherIconSource.PediaSmallIcon;return candidate;}
        candidate=icon();
        if(live(candidate)){source=WeatherIconSource.PediaIcon;return candidate;}
        source=WeatherIconSource.Missing;return null;
    }
    internal static WeatherMerge MergeWeatherPreset(IntPtr existing,IntPtr candidate,bool ambiguous)=>
        ambiguous||existing!=IntPtr.Zero&&candidate!=IntPtr.Zero&&existing!=candidate?WeatherMerge.Ambiguous:
        candidate!=IntPtr.Zero?WeatherMerge.Replace:WeatherMerge.Keep;
    internal static bool SameWeatherMetadata(IntPtr chosen,IntPtr candidate)=>
        chosen!=IntPtr.Zero&&chosen==candidate;
    internal const string WeatherUnavailable="WeatherUnavailable";
    // MissingNativeAsset means absent from the last loaded-object census, not
    // proof that this game version ships no such asset. NotLoaded is observed
    // metadata with no live art (or a borrowed sprite that has since unloaded).
    internal enum Availability { UnknownRole, DeclaredUnresolved, Resolved, Ambiguous,
        NotDiscovered, MissingNativeAsset, NotLoaded, BudgetExceeded }
    internal const int CensusAttemptsPerEvent=2;
    internal const float CensusRetrySeconds=1f;
    internal struct CensusState
    {
        internal int Attempts;
        internal float NextAttempt;
    }
    internal static bool TryBeginCensus(ref CensusState state,float now)
    {
        if(!float.IsFinite(now)||now<0||state.Attempts>=CensusAttemptsPerEvent||now<state.NextAttempt)return false;
        state.Attempts++;state.NextAttempt=now+CensusRetrySeconds;return true;
    }
    internal readonly record struct Resolution(string Role,Availability Availability,string AssetName,string Source)
    {
        internal bool Declared=>Availability!=NativeIconPolicy.Availability.UnknownRole;
        internal bool Resolved=>Availability==NativeIconPolicy.Availability.Resolved;
    }
    private static readonly Dictionary<string,string[]> Names=new(StringComparer.Ordinal)
    {
        ["PinkPlort"]=new[]{"iconPlortPink_small"},
        ["Feeder"]=new[]{"iconPatchCorralFeeder"},
        ["Silo"]=new[]{"iconPatchSiloBase"},
        // This is the game's compass facing indicator, not a fabricated rose.
        ["Compass"]=new[]{"frameHudCompassFacing"},
        ["Atlas"]=new[]{"iconPediaHomeWorld"},
        ["MyMarkers"]=new[]{"marker_flag"},
        ["Radar"]=new[]{"iconMapDetectorActive"},
        // 0.8.1: the «Навигация» sub-tab uses the game's waypoint diamond (the facing frame is a thin bar).
        ["Navigation"]=new[]{"iconWaypoint","marker_flag"},
        // Phantom route drones: the native map's own explorer drone artwork.
        ["GhostDrone"]=new[]{"iconMapExplorerDrone","iconMapRancherDrone"},
        ["Farm"]=new[]{"iconPatchSiloBase"},
        ["WeatherClear"]=new[]{"iconWeatherClear"},
        ["WeatherRain"]=new[]{"iconWeatherRain","iconWeatherRain_small"},
        ["WeatherThunder"]=new[]{"iconWeatherThunderstorm","iconWeatherThunderstorm_small"},
        ["WeatherSlime"]=new[]{"iconWeatherSlimerain","iconWeatherSlimerain_small"},
        ["WeatherPollen"]=new[]{"iconWeatherPollen","iconWeatherPollen_small"},
        ["WeatherWind"]=new[]{"iconWeatherWind","iconWeatherWind_small"},
        // No dedicated names were observed. Only native weather metadata may
        // resolve these; never lava dust, lava lamp, silo solar shield or time_sun.
        ["WeatherLava"]=Array.Empty<string>(),
        ["WeatherSolar"]=Array.Empty<string>(),
        [WeatherUnavailable]=Array.Empty<string>()
    };
    // Phantom route drones carry the component-route/component-drone identity
    // and the mod's own ghost label; a static research drone never does.
    internal static bool GhostDrone(string id,string name)
    {
        id??="";
        if(id.StartsWith("component-route",StringComparison.Ordinal)||
           id.StartsWith("component-drone",StringComparison.Ordinal))return true;
        return !string.IsNullOrEmpty(name)&&name.Contains("Фантом",StringComparison.Ordinal);
    }
    internal static IEnumerable<string> Roles=>Names.Keys;
    internal static IReadOnlyList<string> ExactNames(string role)=>
        Array.AsReadOnly(Names.TryGetValue(role??"",out var names)?names:Array.Empty<string>());
    internal static Resolution ResolveExact(string role,Func<string,bool> available)
    {
        role??="";
        if(!Names.TryGetValue(role,out var names))return new(role,Availability.UnknownRole,"","");
        foreach(string name in names)
            if(available!=null&&available(name))return new(role,Availability.Resolved,name,"Sprite exact name");
        return new(role,Availability.DeclaredUnresolved,"","");
    }
    // Call ONLY with identities belonging to an actual WeatherPatternDefinition
    // or its Metadata/PediaEntry. Tokens identify a role, never a substitute icon.
    internal static string WeatherPresetRole(string identity)
    {
        string spaced=Regex.Replace(identity??"","([a-z])([A-Z])","$1 $2");
        var tokens=new HashSet<string>(Regex.Split(spaced.ToLowerInvariant(),"[^a-z0-9]+"),StringComparer.Ordinal);
        bool lava=tokens.Contains("lava");
        bool solar=tokens.Contains("solar")||tokens.Contains("sunflare")||
            (tokens.Contains("sun")&&tokens.Contains("flare"));
        // Hazard identity always wins over a generic wind/clear token. Mixed
        // lava+solar stays unresolved instead of borrowing an unrelated icon.
        if(lava||solar)return lava==solar?"":lava?"WeatherLava":"WeatherSolar";
        bool clear=tokens.Contains("clear");
        bool wind=tokens.Contains("wind");
        return clear==wind?"":clear?"WeatherClear":"WeatherWind";
    }
}