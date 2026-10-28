using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace CreatorToolkit;

internal sealed class CapsuleTools
{
    internal sealed class View
    {
        internal string Title, Description, Search, RewardSummary="", NativeRewardSummary="", ValidationMessage="";
        internal int Lines, EvidenceGeneration;
        internal bool Confirmed, Baked, NativeObserved, NativeVerified, Mismatch;
        internal CapsuleTruthPolicy.RewardStatus Verification;
        internal CapsuleTruthPolicy.OpenState State;
        internal readonly List<Row> Rewards=new();
        internal readonly List<Row> NativeRewards=new();
    }
    internal sealed class Row
    { internal string Key,Title,Kind,Detail,AssetName; internal int Count; }
    private sealed class Evidence
    {
        internal bool Observed;
        internal CapsuleTruthPolicy.RewardStatus Status;
        internal string Message;
        internal readonly List<CapsuleTruthPolicy.RewardAmount> Rewards=new();
    }
    private readonly Dictionary<string,(string Title,string Detail)> _localized = new(StringComparer.Ordinal);
    private readonly Dictionary<string,View> _views = new(StringComparer.Ordinal);
    private readonly Queue<CapsuleDatabase.Reward> _pending = new();
    private readonly Dictionary<string,float> _retryAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string,string> _pediaDetails = new(StringComparer.Ordinal);
    private readonly HashSet<string> _nativeTitles = new(StringComparer.Ordinal);
    private readonly Dictionary<string,Evidence> _evidence = new(StringComparer.Ordinal);
    private readonly Dictionary<string,CapsuleTruthPolicy.OpenState> _states = new(StringComparer.Ordinal);
    private float _nextPediaScan;
    private int _pediaCursor;
    private bool _loaded;
    private string _installed;
    private const int TextPerTick=4, PediaEntriesPerScan=256;
    private const float TextRetrySeconds=5f, PediaRetrySeconds=10f;
    private const string MissingTitle="Название награды временно недоступно";
    private static readonly Regex Tags = new("<[^>]+>",RegexOptions.Compiled);
    internal CapsuleDatabase Database {get;private set;} = new();
    internal string Status {get;private set;} = "База содержимого ещё не загружена";
    internal bool TextReady => _pending.Count==0;
    internal int LocalizedCount => _localized.Count;
    internal int NativeTitleCount => _nativeTitles.Count;
    internal int EvidenceGeneration {get;private set;}

    // Detached managed cache copies only. MAIN THREAD: no Get/Build, refresh,
    // native wrappers, localization, scene census or writes to production caches.
    internal const int CachedPodLimit=2048,CachedRewardLimit=64,CachedTotalRewardLimit=8192;
    internal sealed class CachedCapsuleRow
    {
        internal string Id,Message="",Error="";
        internal bool Baked,HasEvidence,Observed,HasState,Partial;
        internal CapsuleTruthPolicy.OpenState State=CapsuleTruthPolicy.OpenState.Unknown;
        internal CapsuleTruthPolicy.RewardStatus Status=CapsuleTruthPolicy.RewardStatus.Unknown;
        internal int BakedEntries,NativeEntries;
        internal readonly List<CapsuleTruthPolicy.RewardAmount> BakedRewards=new();
        internal readonly List<CapsuleTruthPolicy.RewardAmount> NativeRewards=new();
    }
    internal sealed class CachedCapsuleSnapshot
    {
        internal int GenerationBefore,GenerationAfter,BakedIds,StateIds,EvidenceIds;
        internal bool VersionMatched,Complete=true;
        internal string CatalogHash="",Error="";
        internal readonly List<CachedCapsuleRow> Rows=new();
    }
    internal CachedCapsuleSnapshot SnapshotCachedEvidence()
    {
        var result=new CachedCapsuleSnapshot {GenerationBefore=EvidenceGeneration};
        int remaining=CachedTotalRewardLimit;
        try
        {
            var database=Database;
            result.VersionMatched=database.VersionMatched;result.CatalogHash=database.CatalogHash;
            result.BakedIds=database.Pods.Count;result.StateIds=_states.Count;result.EvidenceIds=_evidence.Count;
            var ids=new HashSet<string>(StringComparer.Ordinal);
            void AddIds(IEnumerable<string> keys)
            {
                int examined=0;
                foreach(string id in keys)
                {
                    if(examined++>=CachedPodLimit){result.Complete=false;break;}
                    if(string.IsNullOrWhiteSpace(id)||id.Length>512){result.Complete=false;continue;}
                    if(ids.Count>=CachedPodLimit&&!ids.Contains(id)){result.Complete=false;break;}
                    ids.Add(id);
                }
            }
            // Evidence first: a large baked catalogue must not conceal cached mismatches.
            AddIds(_evidence.Keys);AddIds(_states.Keys);AddIds(database.Pods.Keys);
            foreach(string id in ids)
            {
                var row=new CachedCapsuleRow {Id=id};
                try
                {
                    row.HasState=_states.TryGetValue(id,out var state);
                    if(row.HasState)row.State=state;
                    row.HasEvidence=_evidence.TryGetValue(id,out var evidence)&&evidence!=null;
                    bool hasPod=database.Pods.TryGetValue(id,out var pod)&&pod!=null;
                    row.Baked=result.VersionMatched&&hasPod;
                    row.Status=row.HasEvidence?evidence.Status:row.Baked?CapsuleTruthPolicy.RewardStatus.Baked:CapsuleTruthPolicy.RewardStatus.Unknown;
                    if(row.HasEvidence)
                    {
                        row.Observed=evidence.Observed;row.Message=evidence.Message??"";
                        row.NativeEntries=evidence.Rewards.Count;
                        int take=Math.Min(CachedRewardLimit,Math.Min(remaining,row.NativeEntries));
                        for(int i=0;i<take;i++)row.NativeRewards.Add(evidence.Rewards[i]);
                        remaining-=take;if(take!=row.NativeEntries)row.Partial=true;
                    }
                    if(row.Baked)
                    {
                        if(pod.Rewards==null){row.Partial=true;row.Error="Missing baked reward list";}
                        else
                        {
                            row.BakedEntries=pod.Rewards.Count;
                            int take=Math.Min(CachedRewardLimit,Math.Min(remaining,row.BakedEntries));
                            for(int i=0;i<take;i++)
                            {
                                var amount=pod.Rewards[i];
                                if(amount==null||amount.Key==null||!database.Assets.TryGetValue(amount.Key,out var reward)||reward==null)
                                {row.Partial=true;row.Error="Unresolved baked identity";continue;}
                                row.BakedRewards.Add(new(reward.Kind,reward.AssetName,amount.Count));
                            }
                            remaining-=take;if(take!=row.BakedEntries)row.Partial=true;
                        }
                    }
                }
                catch(Exception e){row.Partial=true;row.Error=e.GetType().Name;}
                if(row.Partial)result.Complete=false;
                result.Rows.Add(row);
            }
        }
        catch(Exception e){result.Complete=false;result.Error=e.GetType().Name;}
        result.GenerationAfter=EvidenceGeneration;
        if(result.GenerationAfter!=result.GenerationBefore)result.Complete=false;
        return result;
    }

    internal void Load(string installed)
    {
        if(_loaded&&string.Equals(_installed,installed,StringComparison.Ordinal))return;
        _loaded=false;Database=new CapsuleDatabase();
        ClearNativeEvidence();InvalidateLocalization();
        // Commit the version cache only after a successful read, not before it.
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("CreatorToolkit.CapsuleContents.json");
        Database=stream!=null?CapsuleDatabase.Read(stream,installed):new CapsuleDatabase();
        _installed=installed;_loaded=true;
        InvalidateLocalization();UpdateStatus();
    }

    // Parent calls on actual session/scene transitions, not every Repaint.
    // No saved-state inference, no scene roots retained, no progress mutations.
    internal void InvalidateSession()
    {
        _loaded=false;_installed=null;Database=new CapsuleDatabase();
        ClearNativeEvidence();InvalidateLocalization();UpdateStatus();
    }
    internal void InvalidateScene()
    { ClearNativeEvidence();InvalidateLocalization();UpdateStatus(); }
    internal void InvalidateLocalization()
    {
        _pending.Clear();_retryAt.Clear();_localized.Clear();_nativeTitles.Clear();
        _pediaDetails.Clear();_views.Clear();_nextPediaScan=0f;_pediaCursor=0;
        foreach(var reward in Database.Assets.Values)_pending.Enqueue(reward);
        UpdateStatus();
    }
    private void ClearNativeEvidence()
    { _evidence.Clear();_states.Clear();_views.Clear();EvidenceGeneration++; }

    // The parent adapts the actual native model state; missing models stay Unknown.
    internal void ObserveState(string id,string nativeId,bool hasState,bool locked,bool open)
    {
        if(string.IsNullOrWhiteSpace(id))return;
        var state=CapsuleTruthPolicy.State(id,nativeId,hasState,locked,open);
        if(!_states.TryGetValue(id,out var previous)||previous!=state)
        { _states[id]=state;_views.Remove(id); }
    }
    internal CapsuleTruthPolicy.OpenState GetState(string id) =>
        id!=null&&_states.TryGetValue(id,out var state)?state:CapsuleTruthPolicy.OpenState.Unknown;

    internal void Tick()
    {
        float now=UnityEngine.Time.unscaledTime;
        if(now>=_nextPediaScan)
        {
            _nextPediaScan=now+PediaRetrySeconds;
            try { ScanPedia(); } catch { /* Retry later; never mark an early miss permanently complete. */ }
        }
        bool changed=false;
        // One queue entry per unresolved reward. Failed text is NOT cached as a
        // successful translation; retry is throttled and never performed by Get.
        int work=Math.Min(TextPerTick,_pending.Count);
        for(int i=0;i<work;i++)
        {
            var reward=_pending.Dequeue();
            if(_retryAt.TryGetValue(reward.Key,out float due)&&now<due)
            { _pending.Enqueue(reward);continue; }
            bool hadText=_localized.TryGetValue(reward.Key,out var before);
            string title=Resolve(reward.Title)??before.Title;
            string detail=Resolve(reward.Description)??before.Detail??"";
            if(detail.Length==0&&!string.IsNullOrEmpty(reward.PediaSuffix)&&_pediaDetails.TryGetValue(reward.PediaSuffix,out var pedia))detail=pedia;
            if(!hadText||before.Title!=title||before.Detail!=detail)changed=true;
            _localized[reward.Key]=(title,detail);
            if(!string.IsNullOrEmpty(title))_nativeTitles.Add(reward.Key);else _nativeTitles.Remove(reward.Key);
            bool unresolved=string.IsNullOrEmpty(title)&&HasKey(reward.Title)||
                detail.Length==0&&(HasKey(reward.Description)||!string.IsNullOrEmpty(reward.PediaSuffix));
            if(unresolved) { _retryAt[reward.Key]=now+TextRetrySeconds;_pending.Enqueue(reward); }
            else _retryAt.Remove(reward.Key);
        }
        if(changed) { _views.Clear();UpdateStatus(); }
    }
    private void ScanPedia()
    {
        var missing=new HashSet<string>(Database.Assets.Values.Where(r=>!string.IsNullOrEmpty(r.PediaSuffix)&&
            !_pediaDetails.ContainsKey(r.PediaSuffix)&&(!_localized.TryGetValue(r.Key,out var text)||string.IsNullOrEmpty(text.Detail)))
            .Select(r=>r.PediaSuffix),StringComparer.Ordinal);
        if(missing.Count==0)return;
        var entries=UnityEngine.Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Pedia.PediaEntry>();
        if(entries==null||entries.Length==0)return;
        int examined=Math.Min(PediaEntriesPerScan,entries.Length);
        int start=_pediaCursor%entries.Length;
        for(int i=0;i<examined;i++)
        {
            var entry=entries[(start+i)%entries.Length];
            try
            {
                if(entry==null||!missing.Contains(entry.PersistenceSuffix))continue;
                string detail=Clean(entry.Description?.GetLocalizedString());
                if(!string.IsNullOrEmpty(detail))_pediaDetails[entry.PersistenceSuffix]=detail;
            }
            catch { /* Optional description remains retryable. */ }
        }
        _pediaCursor=(start+examined)%entries.Length;
    }
    private static bool HasKey(CapsuleDatabase.LocalizedKey key) =>
        key!=null&&!string.IsNullOrEmpty(key.Table)&&(key.Key!=0||!string.IsNullOrEmpty(key.Name));
    private static string Clean(string value)
    {
        if(string.IsNullOrWhiteSpace(value)||value.StartsWith("!!")||value.StartsWith("No translation",StringComparison.OrdinalIgnoreCase))return null;
        string text=Tags.Replace(value,"").Replace("\r","").Trim();
        return text.Length==0?null:text;
    }
    private static string Resolve(CapsuleDatabase.LocalizedKey key)
    {
        if(!HasKey(key))return null;
        try
        {
            TableReference table;
            if(key.Table.StartsWith("GUID:",StringComparison.Ordinal))table=TableReference.GuidFromString(key.Table);
            else table=key.Table;
            var localized=key.Key!=0?new LocalizedString(table,key.Key):new LocalizedString(table,key.Name);
            return Clean(localized.GetLocalizedString());
        }
        catch { return null; }
    }
    private static string KindTitle(string kind) => kind=="Blueprint"?"Чертёж":kind=="Component"?"Компонент улучшения":"Предмет";
    private Row RewardRow(CapsuleDatabase.Reward reward,int count)
    {
        _localized.TryGetValue(reward.Key,out var text);
        return new Row {Key=reward.Key,Title=text.Title??MissingTitle,Kind=KindTitle(reward.Kind),
            Detail=text.Detail??"",AssetName=reward.AssetName,Count=count};
    }
    private static string RowText(Row row) => row.Kind+" · "+row.Title+(row.Count!=1?" × "+row.Count:"");
    private View Build(string id)
    {
        Database.Pods.TryGetValue(id,out var pod);
        _evidence.TryGetValue(id,out var evidence);
        var view=new View {Title=pod?.ShadowCache==true?"Теневой тайник":"Капсула с сокровищами",
            State=GetState(id),EvidenceGeneration=EvidenceGeneration,Baked=Database.VersionMatched&&pod!=null,
            NativeObserved=evidence?.Observed==true,Verification=evidence?.Status??(pod!=null?CapsuleTruthPolicy.RewardStatus.Baked:CapsuleTruthPolicy.RewardStatus.Unknown),
            ValidationMessage=evidence?.Message??""};
        view.Confirmed=CapsuleTruthPolicy.Confirmed(view.Verification);
        view.NativeVerified=view.Confirmed;
        view.Mismatch=view.Verification==CapsuleTruthPolicy.RewardStatus.NativeMismatch;
        if(pod!=null)
            foreach(var amount in pod.Rewards)view.Rewards.Add(RewardRow(Database.Assets[amount.Key],amount.Count));
        if(evidence?.Observed==true&&CapsuleTruthPolicy.TryMultiset(evidence.Rewards,out var actual))
            foreach(var amount in actual)
            {
                var reward=Database.Assets.Values.FirstOrDefault(r=>r.Kind==amount.Key.Kind&&r.AssetName==amount.Key.AssetName);
                // Native array counts are bounded by array Length (int); never invent artwork/names.
                var row=reward!=null?RewardRow(reward,checked((int)amount.Value)):
                    new Row {Key="",Title=MissingTitle,Kind=KindTitle(amount.Key.Kind),Detail="",AssetName=amount.Key.AssetName,Count=checked((int)amount.Value)};
                view.NativeRewards.Add(row);
            }
        string baked=string.Join("\n",view.Rewards.Select(RowText));
        string native=string.Join("\n",view.NativeRewards.Select(RowText));
        string header=view.Verification switch
        {
            CapsuleTruthPolicy.RewardStatus.NativeMatch=>"Награды сверены с загруженной капсулой: вид, имя asset и количество.",
            CapsuleTruthPolicy.RewardStatus.NativeMismatch=>"Расхождение с загруженной капсулой. Ниже отдельно база и нативный снимок; база не исправлялась.",
            CapsuleTruthPolicy.RewardStatus.Unverifiable=>"Нативная сверка не подтверждена. "+view.ValidationMessage,
            CapsuleTruthPolicy.RewardStatus.Baked=>"Данные базы для совпавшей версии; нативная капсула ещё не сверена.",
            _=>view.NativeObserved?"Нативный снимок прочитан; сопоставление базы для этого ID отсутствует.":"Для точного ID содержимое неизвестно."
        };
        var details=view.Rewards.Select(r=>r.Detail).Where(s=>!string.IsNullOrWhiteSpace(s)).Distinct();
        view.Description=header+(view.Baked?"\n\nБаза:\n"+(baked.Length>0?baked:"Статические награды не указаны."):"")+
            (view.NativeObserved?"\n\nНативный снимок:\n"+(native.Length>0?native:"Поддерживаемые награды не указаны."):"");
        string description=string.Join("\n\n",details);if(description.Length>0)view.Description+="\n\nОписание наград базы:\n"+description;
        view.Lines=view.Rewards.Count;
        view.RewardSummary=view.Mismatch?"Награды расходятся с базой":string.Join(" · ",view.Rewards.Select(r=>r.Title+(r.Count!=1?" × "+r.Count:"")));
        view.NativeRewardSummary=string.Join(" · ",view.NativeRewards.Select(RowText));
        view.Search=(view.Title+" "+baked+" "+native).ToLowerInvariant();
        return view;
    }
    internal View Get(string id)
    {
        if(string.IsNullOrWhiteSpace(id))return new View {Title="Капсула с сокровищами",Description="Точный ID не указан.",Search="",State=CapsuleTruthPolicy.OpenState.Unknown};
        if(!_views.TryGetValue(id,out var view)) { view=Build(id);_views[id]=view; }
        return view;
    }

    // Parent supplies an actually loaded component. Reads only; no global scan,
    // activation/unlock, model writes, or inference from recipes/object absence.
    internal View ValidateLoadedPod(string id,Il2Cpp.TreasurePod pod)
    {
        if(string.IsNullOrWhiteSpace(id))return Get(id);
        var evidence=new Evidence {Status=CapsuleTruthPolicy.RewardStatus.Unverifiable,Message="Загруженная капсула недоступна."};
        try
        {
            if(pod!=null)
            {
                if(!CapsuleTruthPolicy.SameId(id,pod._id))evidence.Message="Точный нативный ID не совпадает; награды к этому ID не привязаны.";
                else
                {
                    bool supported=true;
                    var blueprint=pod.Blueprint;if(blueprint!=null)evidence.Rewards.Add(new("Blueprint",blueprint.name,1));
                    var component=pod.UpgradeComponent;if(component!=null)evidence.Rewards.Add(new("Component",component.name,1));
                    var native=pod.SpawnObjs;
                    for(int i=0;native!=null&&i<native.Length;i++)
                    {
                        if(native[i]==null) { supported=false;continue; }
                        evidence.Rewards.Add(new("Item",native[i].name,1));
                    }
                    if(pod.UnlockedSlimeAppearanceDefinition!=null||pod.UnlockedSlimeAppearance!=null)supported=false;
                    CompleteNativeRewards(id,evidence,supported);
                }
            }
        }
        catch(Exception e) { evidence.Observed=false;evidence.Status=CapsuleTruthPolicy.RewardStatus.Unverifiable;evidence.Message="Чтение нативных наград недоступно: "+e.Message; }
        _evidence[id]=evidence;_views.Remove(id);UpdateStatus();
        return Get(id);
    }
    private void CompleteNativeRewards(string id,Evidence evidence,bool supported)
    {
        if(!CapsuleTruthPolicy.TryMultiset(evidence.Rewards,out _))supported=false;
        evidence.Observed=true;
        bool baked=Database.VersionMatched&&Database.Pods.ContainsKey(id);
        evidence.Status=CapsuleTruthPolicy.Verification(baked,true,true,supported,Database.ExpectedRewards(id),evidence.Rewards);
        evidence.Message=!supported?"Есть неподдерживаемые appearance-награды, null-элементы или неполные данные; полное совпадение не заявляется.":
            evidence.Status==CapsuleTruthPolicy.RewardStatus.NativeMismatch?"Вид, имя asset или количество наград расходятся с базой.":
            evidence.Status==CapsuleTruthPolicy.RewardStatus.NativeMatch?"Совпал multiset нативных наград; это не состояние открытия.":"Для точного ID нет version-gated baked-сопоставления.";
    }

    // TreasurePodRewarder is a separate IdHandler, not a TreasurePod subclass.
    // Same exact-ID/multiset contract; parent supplies the actually loaded component.
    internal View ValidateLoadedRewarder(string id,Il2CppMonomiPark.SlimeRancher.World.TreasurePodRewarder rewarder)
    {
        if(string.IsNullOrWhiteSpace(id))return Get(id);
        var evidence=new Evidence {Status=CapsuleTruthPolicy.RewardStatus.Unverifiable,Message="Загруженный тайник недоступен."};
        try
        {
            if(rewarder!=null)
            {
                if(!CapsuleTruthPolicy.SameId(id,rewarder._id))evidence.Message="Точный нативный ID не совпадает; награды к этому ID не привязаны.";
                else
                {
                    bool supported=true;
                    var blueprint=rewarder.Blueprint;if(blueprint!=null)evidence.Rewards.Add(new("Blueprint",blueprint.name,1));
                    var component=rewarder.UpgradeComponent;if(component!=null)evidence.Rewards.Add(new("Component",component.name,1));
                    var native=rewarder.SpawnObjs;
                    for(int i=0;native!=null&&i<native.Length;i++)
                    {
                        if(native[i]==null) { supported=false;continue; }
                        evidence.Rewards.Add(new("Item",native[i].name,1));
                    }
                    if(rewarder.UnlockedSlimeAppearanceDefinition!=null||rewarder.UnlockedSlimeAppearance!=null)supported=false;
                    CompleteNativeRewards(id,evidence,supported);
                }
            }
        }
        catch(Exception e) { evidence.Observed=false;evidence.Status=CapsuleTruthPolicy.RewardStatus.Unverifiable;evidence.Message="Чтение нативных наград недоступно: "+e.Message; }
        _evidence[id]=evidence;_views.Remove(id);UpdateStatus();
        return Get(id);
    }

    internal bool MatchesLoadedPod(string id,Il2Cpp.TreasurePod pod) => ValidateLoadedPod(id,pod).Confirmed;
    internal void ForgetLoadedPod(string id)
    {
        if(id==null)return;
        _evidence.Remove(id);_views.Remove(id);UpdateStatus();
        // Unloading says nothing about open/locked; state observations are independent.
    }
    private void UpdateStatus()
    {
        Status=Database.VersionMatched?"База "+Database.Pods.Count+" капсул · версия совпала · нативных названий "+NativeTitleCount+"/"+Database.Assets.Count+
            " · сверено "+_evidence.Values.Count(e=>e.Status==CapsuleTruthPolicy.RewardStatus.NativeMatch)+
            " · расхождений "+_evidence.Values.Count(e=>e.Status==CapsuleTruthPolicy.RewardStatus.NativeMismatch):
            "Version-gated база недоступна; награды не подтверждены";
    }
}
