using System;
using System.Collections.Generic;

namespace CreatorToolkit;

// Pure truth rules. No Unity objects, recipes, scene loading or save access.
internal static class CapsuleTruthPolicy
{
    internal enum OpenState { Unknown, Locked, Open }
    internal enum RewardStatus { Unknown, Baked, NativeMatch, NativeMismatch, Unverifiable }
    internal readonly record struct RewardIdentity(string Kind, string AssetName);
    internal readonly record struct RewardAmount(string Kind, string AssetName, long Count);

    internal static bool SameId(string expected, string observed) =>
        !string.IsNullOrWhiteSpace(expected)&&!string.IsNullOrWhiteSpace(observed)&&
        string.Equals(expected,observed,StringComparison.Ordinal);

    // Missing/unloaded state and contradictory observations are never "Locked".
    internal static OpenState State(string expectedId,string observedId,bool hasState,bool locked,bool open)
    {
        if(!SameId(expectedId,observedId)||!hasState||locked==open)return OpenState.Unknown;
        return open?OpenState.Open:OpenState.Locked;
    }

    internal static bool TryMultiset(IEnumerable<RewardAmount> rewards,out Dictionary<RewardIdentity,long> result)
    {
        result=new Dictionary<RewardIdentity,long>();
        if(rewards==null)return false;
        foreach(var reward in rewards)
        {
            if(reward.Kind is not ("Blueprint" or "Component" or "Item")||
                string.IsNullOrWhiteSpace(reward.AssetName)||reward.Count<=0)return false;
            var identity=new RewardIdentity(reward.Kind,reward.AssetName);
            result.TryGetValue(identity,out long count);
            try { result[identity]=checked(count+reward.Count); }
            catch(OverflowException) { return false; }
        }
        return true;
    }

    internal static bool SameRewards(IEnumerable<RewardAmount> expected,IEnumerable<RewardAmount> observed)
    {
        if(!TryMultiset(expected,out var left)||!TryMultiset(observed,out var right)||left.Count!=right.Count)return false;
        foreach(var pair in left)
            if(!right.TryGetValue(pair.Key,out long count)||count!=pair.Value)return false;
        return true;
    }

    internal static RewardStatus Verification(bool baked,bool sameId,bool observed,bool supported,
        IEnumerable<RewardAmount> expected,IEnumerable<RewardAmount> actual)
    {
        if(!sameId||!observed||!supported)return RewardStatus.Unverifiable;
        if(!baked)return RewardStatus.Unknown; // Native observed, but no mapping to verify.
        return SameRewards(expected,actual)?RewardStatus.NativeMatch:RewardStatus.NativeMismatch;
    }

    internal static bool Confirmed(RewardStatus status) => status==RewardStatus.NativeMatch;
    internal static bool ShouldHideOpened(OpenState state) => state==OpenState.Open;
}
