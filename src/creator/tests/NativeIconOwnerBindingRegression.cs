using System;
using System.IO;
using CreatorToolkit;

// Parent must explicitly include/invoke this separate fixture. Not run by author.
// Pure selection fixtures + static wiring guards, NOT native/visual acceptance.
internal static class NativeIconOwnerBindingRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        var state=new NativeIconPolicy.OwnerBindingState();
        check(NativeIconPolicy.OwnerAvailability(state)==NativeIconPolicy.Availability.MissingNativeAsset,
            "owner binding distinguishes no observed owner from unloaded art");
        NativeIconPolicy.AddOwnerIcon(ref state,IntPtr.Zero);
        check(NativeIconPolicy.OwnerAvailability(state)==NativeIconPolicy.Availability.NotLoaded,
            "observed exact owner without live art is NotLoaded");
        NativeIconPolicy.AddOwnerIcon(ref state,new IntPtr(101));
        check(NativeIconPolicy.OwnerAvailability(state)==NativeIconPolicy.Availability.Resolved&&state.Sprite==new IntPtr(101),
            "late exact-owner art resolves without involving global alias ambiguity");
        NativeIconPolicy.AddOwnerIcon(ref state,new IntPtr(101));
        check(NativeIconPolicy.OwnerAvailability(state)==NativeIconPolicy.Availability.Resolved&&state.Owners==3,
            "repeated bindings to the identical native sprite pointer are not conflicting artwork");
        NativeIconPolicy.AddOwnerIcon(ref state,IntPtr.Zero);
        check(NativeIconPolicy.OwnerAvailability(state)==NativeIconPolicy.Availability.Resolved,
            "missing art on another validated owner copy does not replace live exact-owner art");
        NativeIconPolicy.AddOwnerIcon(ref state,new IntPtr(202));
        check(NativeIconPolicy.OwnerAvailability(state)==NativeIconPolicy.Availability.Ambiguous,
            "different live sprites within the chosen owner identity fail closed");
        NativeIconPolicy.AddOwnerIcon(ref state,new IntPtr(101));
        NativeIconPolicy.AddOwnerIcon(ref state,IntPtr.Zero);
        check(NativeIconPolicy.OwnerAvailability(state)==NativeIconPolicy.Availability.Ambiguous,
            "owner ambiguity is sticky within the census, never last-match wins");
        state=default;
        NativeIconPolicy.AddOwnerIcon(ref state,new IntPtr(202));
        NativeIconPolicy.AddOwnerIcon(ref state,new IntPtr(101));
        check(NativeIconPolicy.OwnerAvailability(state)==NativeIconPolicy.Availability.Ambiguous,
            "owner conflict is independent of enumeration order");
        state=default;
        check(NativeIconPolicy.OwnerAvailability(state)==NativeIconPolicy.Availability.MissingNativeAsset,
            "a new census starts with no stale owner state or sprite identity");
        check(NativeIconPolicy.WeatherBudget==24&&NativeIconPolicy.WeatherAttemptsPerEpoch==4&&
            NativeIconPolicy.CensusAttemptsPerEvent==2&&NativeIconPolicy.WeatherDiagnosticBudget>24&&
            NativeIconPolicy.WeatherDiagnosticBudget<=256,
            "text coverage can exceed retained weather lookups without raising sprite/retry budgets");

        string source=File.ReadAllText(Path.Combine(root,"src","creator","NativeIconTools.cs"));
        int begin=source.IndexOf("string[] ownerNames=",StringComparison.Ordinal);
        int end=begin>=0?source.IndexOf("// Every lookup now uses",begin,StringComparison.Ordinal):-1;
        string owners=begin>=0&&end>begin?source.Substring(begin,end-begin):"";
        check(owners.Contains("entry.PersistenceSuffix")&&owners.Contains("TryCast<IdentifiablePediaEntry>()")&&
            owners.Contains("_identifiableTypeByRefId.TryGetValue(reference")&&owners.Contains("registered.Pointer!=type.Pointer")&&
            owners.Contains("pediaIndex.TryGetValue(registered")&&owners.Contains("selected.Pointer!=entry.Pointer"),
            "tab owner wiring checks exact pedia suffix, registry reference/pointer and director-selected entry");
        check(!owners.Contains("Pick(")&&!owners.Contains("ambiguousNative")&&!owners.Contains("ambiguousSprites"),
            "unrelated raw sprite-name ambiguity cannot suppress validated tab owner art");
        check(source.Contains("if(aliases.Any(ambiguousNative.Contains))return null;")&&
            source.Contains("NativeIconPolicy.ExactNames(role).Any(ambiguousSprites.Contains)"),
            "unscoped raw alias resolution remains failclosed");
        check(source.Contains("WeatherMarkerDefinition\",StringComparison.Ordinal))clearOwner.Add(sprite)")&&
            source.Contains("DroneStationMarkerDefinition\",StringComparison.Ordinal))droneOwner.Add(sprite)")&&
            source.Contains("StoreRole(\"WeatherClear\",clearOwner.Icon"),
            "clear and drones bind typed exact marker owners rather than arbitrary sprite aliases");
        check(source.Contains("WEATHER_OBSERVED name=")&&source.Contains("diagnosticTruncated=")&&
            source.Contains("lookupCoverageTruncated=")&&source.Contains("NativeIconPolicy.WeatherDiagnosticBudget"),
            "weather report exposes exact observed identities and separate diagnostic/lookup truncation");
        check(source.Contains("CurrentInfo(_tabInfo[index],Tab(index))")&&source.Contains("Array.Clear(_tabInfo)"),
            "tab status checks borrowed-sprite liveness and resets its census metadata");
    }
}
