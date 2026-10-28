using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Il2Cpp;
using UnityEngine;

namespace CreatorToolkit;

// MAIN THREAD ONLY: caller captures this string, then hands only the string to
// DiagnosticIo/a file worker. No I/O, cache refresh, scene scans or world writes.
internal static class ReadOnlySprintDiagnostics
{
    private const int CatalogBudget=4096,PodBudget=2048,TextBudget=524288;
    private sealed class Report
    {
        private readonly StringBuilder _text=new();
        private bool _truncated;
        internal bool Truncated=>_truncated;
        internal void Line(string line)
        {
            if(_truncated)return;
            if(_text.Length+line.Length+96>TextBudget)
            {_text.AppendLine("ReportTruncated=true; remaining records UNKNOWN");_truncated=true;return;}
            _text.AppendLine(line);
        }
        public override string ToString()=>_text.ToString();
    }
    private static string Q(string value)
    {
        if(value!=null&&value.Length>512)value=value.Substring(0,512)+" [TRUNCATED]";
        return JsonSerializer.Serialize(value);
    }
    private static string Ptr(IntPtr pointer)=>"0x"+pointer.ToInt64().ToString("x");
    private static string Error(Exception error)=>error.GetType().Name;

    internal static string Snapshot(ToolkitMod app)
    {
        var report=new Report();
        report.Line("M14ReadOnlySnapshot UTC="+DateTime.UtcNow.ToString("O")+"; mainThreadCallerRequired=true");
        report.Line("Evidence=single observational snapshot; NOT visual acceptance, spawn validation or reward proof");
        if(app==null){report.Line("Context=UNKNOWN app missing");return report.ToString();}
        try
        {
            CachedCapsules(report,app.Map?.Capsules);
            IntPtr session=app.Session;
            var model=app.Context?.GameModel;
            var loader=SystemContext.Instance?.SceneLoader;
            report.Line("Ready="+app.Ready+"; SceneLoading="+(loader==null?"UNKNOWN":loader.IsSceneLoadInProgress.ToString())+"; Session="+Ptr(session)+"; GameModel="+(model==null?"missing":Ptr(model.Pointer)));
            // Never follow old wrappers across loading/menu/session boundaries.
            if(!app.Ready||model==null||model.WasCollected||session==IntPtr.Zero||model.Pointer!=session)
            {report.Line("NativeSections=UNKNOWN not-ready or model/session mismatch; no reads attempted");return report.ToString();}
            Catalog(report,app);
            Icons(report,app);
            var sources=app.Map.Sources.GetSnapshot(Time.unscaledTime);
            report.Line("WORLD_SOURCES actual="+sources.Actual.Count+"; potential="+sources.Potential.Count+
                "; generation="+sources.Generation+"; started="+sources.StartedAt+"; published="+sources.PublishedAt+
                "; cycleComplete="+sources.CycleComplete+"; identityComplete="+sources.IdentityCensusComplete+
                "; actualComplete="+sources.ActualCensusComplete+"; potentialComplete="+sources.PotentialCensusComplete+
                "; incomplete="+sources.Incomplete+"; unloadedScenes=UNKNOWN; source=managed-cached-only");
            foreach(var issue in sources.Issues)report.Line("WORLD_SOURCE_ISSUE "+Q(issue));
            foreach(var gold in sources.GoldCoverage)report.Line("GOLD_IDENTITY "+Q(gold.Key)+" = "+Q(gold.Value));
            int sourceRows=0;
            foreach(var side in new[]{sources.Actual,sources.Potential})
            foreach(var row in side)
            {
                if(sourceRows++>=768){report.Line("WorldSourceRowsTruncated=true");break;}
                report.Line("WORLD_SOURCE kind="+row.Kind+"; key="+Q(row.Key)+"; type="+Q(row.TypeReference)+
                    "; name="+Q(row.NativeName)+"; scene="+Q(row.Scene)+"; zone="+Q(row.ZoneReference)+
                    "; positionRole="+row.PositionRole+"; xyz="+row.Position.ToString("F2")+"; observedAt="+row.ObservedAt+
                    "; queryPresent="+row.QueryPresent+"; eligibilityUnknown="+row.EligibilityUnknown+
                    "; weight="+row.Weight+"; constraintWeight="+row.ConstraintWeight+"; time="+Q(row.TimeMode)+
                    "; hours="+row.StartHour+".."+row.EndHour+"; radius="+row.Radius+"; feral="+row.Feral+
                    "; active="+row.SourceActive+"; enabled="+row.SourceEnabled+"; queryEvaluated=false");
            }
            int visited=0,open=0,locked=0,unknown=0,objects=0,noObjects=0,objectUnknown=0,errors=0,conflicts=0;
            bool complete=false;
            var ids=new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var pods=model.AllPods();
                if(pods==null)report.Line("Pods=UNKNOWN AllPods returned null");
                else
                {
                    int declared=pods.Count;
                    report.Line("PodsDeclared="+declared+"; PodReadLimit="+PodBudget+"; source=current GameModel.AllPods");
                    foreach(var pair in pods)
                    {
                        if(visited>=PodBudget)break;
                        visited++;
                        try
                        {
                            string id=pair.Key;
                            // Do not silently turn a truncated ID into an apparent exact match.
                            bool validId=!string.IsNullOrEmpty(id)&&id.Length<=512;
                            bool conflict=validId&&!ids.Add(id);
                            if(conflict)conflicts++;
                            var pod=pair.Value;
                            string state="UNKNOWN",presence="UNKNOWN",modelPresence="missing";
                            if(pod!=null&&!pod.WasCollected)
                            {
                                modelPresence="present";
                                try
                                {
                                    var native=pod.state;
                                    if(native!=null&&!native.WasCollected)
                                    {
                                        var value=native.Value;
                                        state=value==TreasurePod.State.OPEN?"OPEN":value==TreasurePod.State.LOCKED?"LOCKED":"UNKNOWN_ENUM("+((int)value)+")";
                                    }
                                }
                                catch(Exception e){errors++;state="UNKNOWN:"+Error(e);}
                                try
                                {
                                    // Export-confirmed field getter, NOT DEBUG_GetGameObject,
                                    // GetComponent, Instantiate, a scene census or a reward read.
                                    var go=pod.gameObj;
                                    presence=go!=null&&!go.WasCollected?"present":"absent-or-unloaded";
                                }
                                catch(Exception e){errors++;presence="UNKNOWN:"+Error(e);}
                            }
                            if(!validId||conflict)state="UNKNOWN_ID_CONFLICT";
                            if(state=="OPEN")open++;else if(state=="LOCKED")locked++;else unknown++;
                            if(presence=="present")objects++;else if(presence=="absent-or-unloaded")noObjects++;else objectUnknown++;
                            report.Line("POD id="+(validId?Q(id):"INVALID_OR_OVERSIZE_NOT_EMITTED")+"; model="+modelPresence+"; state="+state+"; gameObj="+presence+"; conflict="+conflict);
                        }
                        catch(Exception e){errors++;unknown++;objectUnknown++;report.Line("POD record=UNKNOWN; error="+Error(e));}
                    }
                    complete=visited==declared&&declared<=PodBudget;
                }
            }
            catch(Exception e){errors++;report.Line("PodsEnumeration=UNKNOWN_PARTIAL; error="+Error(e));}
            report.Line("PodsSummary visited="+visited+"; open="+open+"; locked="+locked+"; unknown="+unknown+"; gameObjPresent="+objects+"; gameObjAbsent="+noObjects+"; gameObjUnknown="+objectUnknown+"; conflicts="+conflicts+"; errors="+errors+"; enumerationComplete="+complete);
            report.Line("Rewards=NOT_READ_FROM_NATIVE_BY_SNAPSHOT; managed cached evidence is not revalidation; absent model/object/state is never inferred from recipes or treated as closed");
            report.Line("SnapshotConsistency="+(app.Session==session&&app.Context?.GameModel?.Pointer==model.Pointer?"same-session-model":"CHANGED; preceding observations invalid"));
        }
        catch(Exception e){report.Line("Snapshot=UNKNOWN_PARTIAL; error="+Error(e));}
        return report.ToString();
    }

    // This section reads detached managed DTOs, not Get/Build or Unity/native objects.
    private static void CachedCapsules(Report report,CapsuleTools capsules)
    {
        report.Line("CachedCapsules source=managed production cache; freshNativeReads=NONE; revalidation=NONE; observationAge=UNKNOWN; nativeComponentIdAndCurrentLoadedPresence=UNKNOWN");
        report.Line("CachedCapsules semantics=status is previously cached, not newly confirmed; generation is cache-clear epoch, NOT observation timestamp; missing evidence is UNKNOWN, never an empty native reward set");
        report.Line("CachedCapsules rewardEncoding=raw entries; duplicate exact (Kind,AssetName) quantities sum; unsupported/partial evidence is not a complete native reward census");
        if(capsules==null){report.Line("CachedCapsules=UNKNOWN missing managed cache");return;}
        try
        {
            var snapshot=capsules.SnapshotCachedEvidence();
            report.Line("CachedCapsules versionMatched="+snapshot.VersionMatched+"; catalogHash="+Q(snapshot.CatalogHash)+"; bakedIds="+snapshot.BakedIds+"; stateIds="+snapshot.StateIds+"; evidenceIds="+snapshot.EvidenceIds+"; generationBefore="+snapshot.GenerationBefore+"; generationAfter="+snapshot.GenerationAfter+"; limits="+CapsuleTools.CachedPodLimit+" IDs/"+CapsuleTools.CachedRewardLimit+" entries per side/"+CapsuleTools.CachedTotalRewardLimit+" total entries");
            int emitted=0,matched=0,mismatched=0,unverifiable=0,missing=0;
            foreach(var row in snapshot.Rows)
            {
                if(report.Truncated)break;
                emitted++;
                if(!row.HasEvidence)missing++;
                else if(row.Status==CapsuleTruthPolicy.RewardStatus.NativeMatch)matched++;
                else if(row.Status==CapsuleTruthPolicy.RewardStatus.NativeMismatch)mismatched++;
                else if(row.Status==CapsuleTruthPolicy.RewardStatus.Unverifiable)unverifiable++;
                report.Line("CAPSULE_CACHE id="+Q(row.Id)+"; generation="+snapshot.GenerationBefore+"; statePresent="+row.HasState+"; state="+row.State+"; baked="+row.Baked+"; evidencePresent="+row.HasEvidence+"; nativeObservedCached="+row.Observed+"; cachedStatus="+row.Status+"; cachedConfirmed="+(row.HasEvidence&&row.Observed&&row.Status==CapsuleTruthPolicy.RewardStatus.NativeMatch)+"; exportComplete="+!row.Partial+"; bakedEntries="+row.BakedEntries+"; nativeEntries="+row.NativeEntries+"; nativeEntriesMeaning="+(row.HasEvidence&&row.Observed?"cached observation, see status":"UNKNOWN_OR_PARTIAL_NOT_EMPTY_PROOF")+"; reason="+Q(row.Message)+"; exportError="+Q(row.Error));
                void Rewards(string source,List<CapsuleTruthPolicy.RewardAmount> rewards)
                {
                    for(int i=0;i<rewards.Count&&!report.Truncated;i++)
                    {
                        var reward=rewards[i];
                        bool exact=!string.IsNullOrWhiteSpace(reward.Kind)&&reward.Kind.Length<=512&&!string.IsNullOrWhiteSpace(reward.AssetName)&&reward.AssetName.Length<=512;
                        report.Line("CAPSULE_CACHE_REWARD id="+Q(row.Id)+"; source="+source+"; entry="+i+"; identityExactInExport="+exact+"; kind="+Q(reward.Kind)+"; assetName="+Q(reward.AssetName)+"; count="+reward.Count);
                    }
                }
                Rewards("BAKED",row.BakedRewards);Rewards("NATIVE_CACHED",row.NativeRewards);
            }
            report.Line("CachedCapsulesSummary emitted="+emitted+"; cachedNativeMatch="+matched+"; cachedNativeMismatch="+mismatched+"; cachedUnverifiable="+unverifiable+"; withoutEvidence="+missing+"; countersScope=emitted cached rows only; exportComplete="+(snapshot.Complete&&emitted==snapshot.Rows.Count&&!report.Truncated)+"; error="+Q(snapshot.Error)+"; unexportedOrMissing=UNKNOWN; freshNativeConfirmation=NOT_PERFORMED");
        }
        catch(Exception e){report.Line("CachedCapsules=UNKNOWN_PARTIAL; error="+Error(e));}
    }

    private static void Catalog(Report report,ToolkitMod app)
    {
        var variants=new Dictionary<string,List<ActorTools.Entry>>(StringComparer.Ordinal);
        int scanned=0,ordinary=0,prototype=0,radiant=0,errors=0,total=0;
        bool complete=false;
        try
        {
            var catalog=app.Actors?.Catalog;
            if(catalog==null){report.Line("Catalog=UNKNOWN missing");return;}
            total=catalog.Count;
            for(int i=0;i<Math.Min(total,CatalogBudget);i++)
            {
                scanned++;
                try
                {
                    var entry=catalog[i];
                    if(entry==null){errors++;continue;}
                    if(entry.IsRadiant)
                    {
                        radiant++;string key=entry.CatalogKey;
                        if(key==null){errors++;continue;}
                        if(!variants.TryGetValue(key,out var entries))variants[key]=entries=new();
                        entries.Add(entry);
                    }
                    else if(entry.IsPrototype)prototype++;
                    else ordinary++;
                }
                catch{errors++;}
            }
            complete=scanned==total&&errors==0;
        }
        catch(Exception e){errors++;report.Line("CatalogEnumeration=UNKNOWN_PARTIAL; error="+Error(e));}
        report.Line("Catalog total="+total+"; scanned="+scanned+"; ordinary="+ordinary+"; prototype="+prototype+"; radiant="+radiant+"; errors="+errors+"; complete="+complete);
        for(int i=0;i<Math.Min(22,RadiantCatalogPolicy.Ordered.Count);i++)
        {
            var species=RadiantCatalogPolicy.Ordered[i];
            try
            {
                if(!variants.TryGetValue(species.Key,out var entries)||entries.Count!=1)
                {report.Line("VARIANT key="+Q(species.Key)+"; matches="+(entries?.Count??0)+"; availability="+(entries?.Count>1?"CONFLICT":complete?"MISSING":"UNKNOWN_PARTIAL"));continue;}
                var entry=entries[0];var type=entry.Type;var icon=entry.Icon;
                string iconLife=icon==null?"missing":icon.WasCollected?"collected":icon.texture==null?"texture-missing":"live";
                report.Line("VARIANT key="+Q(species.Key)+"; matches=1; CanSpawn="+entry.CanSpawn+"; reason="+Q(entry.UnavailableReason)+"; TypeRef="+Q(type==null||type.WasCollected?null:type.ReferenceId)+"; support=cached-catalog-preflight-not-reprobed; Icon="+(icon==null?"none":Ptr(icon.Pointer))+"; IconLife="+iconLife);
            }
            catch(Exception e){report.Line("VARIANT key="+Q(species.Key)+"; availability=UNKNOWN; error="+Error(e));}
        }
    }

    private static void Icons(Report report,ToolkitMod app)
    {
        try
        {
            var icons=app.Map?.Icons;
            if(icons==null){report.Line("Icons=UNKNOWN missing");return;}
            report.Line("Icons status="+Q(icons.Status)+"; categories="+icons.CategoryCount+"; rewards="+icons.RewardCount+"; DiscoveryScans="+icons.DiscoveryScans);
            foreach(string role in new[]{"WeatherClear","WeatherRain","WeatherThunder","WeatherSlime","WeatherPollen","WeatherWind","WeatherLava","WeatherSolar"})
            {
                try{report.Line("WEATHER role="+role+"; diagnostic="+Q(icons.RoleDiagnostic(role)));}
                catch(Exception e){report.Line("WEATHER role="+role+"; diagnostic=UNKNOWN; error="+Error(e));}
            }
        }
        catch(Exception e){report.Line("Icons=UNKNOWN_PARTIAL; error="+Error(e));}
    }
}
