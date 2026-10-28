using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CreatorToolkit;

// Plain data only: no Unity objects, localization subscriptions or scene roots.
internal sealed class CapsuleDatabase
{
    internal sealed class LocalizedKey
    { public string Table {get;set;} = ""; public long Key {get;set;} public string Name {get;set;} = ""; }
    internal sealed class Reward
    {
        public string Key {get;set;} public string Kind {get;set;} public string AssetName {get;set;}
        public string AssetPath {get;set;} public string File {get;set;} public string PathId {get;set;}
        public string PediaSuffix {get;set;} = "";
        public LocalizedKey Title {get;set;} public LocalizedKey Description {get;set;}
    }
    internal sealed class Amount
    { public string Key {get;set;} public int Count {get;set;} }
    internal sealed class Pod
    { public string Id {get;set;} public bool ShadowCache {get;set;} public List<Amount> Rewards {get;set;} = new(); }
    private sealed class Data
    {
        public string CatalogHash {get;set;} public string Source {get;set;}
        public List<Reward> Assets {get;set;} public List<Pod> Pods {get;set;}
    }
    internal readonly Dictionary<string,Reward> Assets = new(StringComparer.Ordinal);
    internal readonly Dictionary<string,Pod> Pods = new(StringComparer.Ordinal);
    internal string CatalogHash {get;private set;} = "";
    internal string Source {get;private set;} = "";
    internal bool VersionMatched {get;private set;}
    internal IEnumerable<CapsuleTruthPolicy.RewardAmount> ExpectedRewards(string id)
    {
        if(id==null||!Pods.TryGetValue(id,out var pod))yield break;
        foreach(var amount in pod.Rewards)
        {
            var reward=Assets[amount.Key];
            yield return new CapsuleTruthPolicy.RewardAmount(reward.Kind,reward.AssetName,amount.Count);
        }
    }
    internal static CapsuleDatabase Read(Stream stream, string installedHash)
    {
        var result = new CapsuleDatabase();
        var data = JsonSerializer.Deserialize<Data>(stream) ?? throw new InvalidDataException("Empty capsule database.");
        if(string.IsNullOrEmpty(installedHash)||!string.Equals(data.CatalogHash,installedHash,StringComparison.Ordinal))
            return result; // Different version: do not attach plausible but wrong rewards to an ID.
        if(data.Assets==null||data.Pods==null)throw new InvalidDataException("Missing capsule data arrays.");
        result.CatalogHash=data.CatalogHash;result.Source=data.Source??"";
        foreach(var reward in data.Assets)
        {
            if(reward==null||string.IsNullOrWhiteSpace(reward.Key)||string.IsNullOrWhiteSpace(reward.AssetName)||
                reward.Kind is not ("Blueprint" or "Component" or "Item")||!result.Assets.TryAdd(reward.Key,reward))
                throw new InvalidDataException("Invalid or duplicate capsule reward.");
        }
        foreach(var pod in data.Pods)
        {
            if(pod==null||!CapsuleTruthPolicy.SameId(pod.Id,pod.Id)||pod.Rewards==null||!result.Pods.TryAdd(pod.Id,pod))
                throw new InvalidDataException("Invalid or duplicate capsule ID.");
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var amount in pod.Rewards)
                if(amount==null||string.IsNullOrWhiteSpace(amount.Key)||amount.Count<1||!result.Assets.ContainsKey(amount.Key)||!seen.Add(amount.Key))
                    throw new InvalidDataException("Unresolved, duplicate or invalid capsule reward amount.");
        }
        result.VersionMatched=true; // Baked/version-gated only, NEVER native verification.
        return result;
    }
}
