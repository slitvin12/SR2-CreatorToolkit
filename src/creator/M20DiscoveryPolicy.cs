namespace CreatorToolkit;

// Pure request routing: unrelated native systems must not be enumerated.
internal static class M20DiscoveryPolicy
{
    internal const int PhaseCount=4;
    internal static bool PhaseNeeded(M19GraphicsRequest r,int phase)=>phase switch
    {
        0=>r.HideDecor,
        1=>r.DisableShadows,
        2=>r.DisabledEffects!=M19Effects.None,
        3=>r.Distance>0,
        _=>false
    };
    internal static bool NeedsPeriodic(M19GraphicsRequest r)=>
        r.HideDecor||r.DisableShadows||r.DisabledEffects!=M19Effects.None||r.Distance>0;
    internal static bool CloneNeeded(bool instantiated,bool requestedActiveEffect)=>
        !instantiated&&requestedActiveEffect;
    internal static ulong AppendScene(ulong hash,int handle)=>
        unchecked((hash^(uint)handle)*1099511628211UL);
}
