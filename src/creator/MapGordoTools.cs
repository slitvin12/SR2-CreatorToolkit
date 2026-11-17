using System;
using System.Collections.Generic;
using System.Globalization;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using Il2CppMonomiPark.SlimeRancher.UI.Map;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace CreatorToolkit;

internal sealed partial class MapTools
{
    private readonly Dictionary<string,Point> _gordoPoints=new(StringComparer.Ordinal);
    private readonly Dictionary<string,HashSet<string>> _gordoScenes=new(StringComparer.Ordinal);
    private readonly HashSet<string> _gordoSceneComplete=new(StringComparer.Ordinal);
    private readonly HashSet<string> _gordoConflicts=new(StringComparer.Ordinal);
    private readonly Dictionary<string,GordoMapPolicy.State> _gordoStates=new(StringComparer.Ordinal);
    private readonly HashSet<string> _nativeGordoIds=new(StringComparer.Ordinal);
    // M28: indexed Gordo without any GameModel entry in a readable census = its
    // area never streamed in for this save, so it cannot have been fed or burst.
    private readonly HashSet<string> _gordoNeverLoaded=new(StringComparer.Ordinal);
    internal int GordoNeverLoadedCount=>_gordoNeverLoaded.Count;
    internal bool GordoNeverLoaded(Point point)=>point!=null&&point.Category=="Гордо"&&point.Id!=null&&_gordoNeverLoaded.Contains(point.Id);
    private readonly List<string> _gordoRejected=new();
    internal IReadOnlyList<string> GordoRejected=>_gordoRejected;
    internal int GordoDuplicateRows {get;private set;}
    internal int GordoPointCount=>_gordoPoints.Count;
    internal int GordoNativeVisible=>_nativeGordoIds.Count;
    internal int GordoConflicts=>_gordoConflicts.Count;
    private static string ScenePath(string path)=>(path??"").Replace('\\','/');
    private void ResetGordos()
    {
        _gordoPoints.Clear();_gordoScenes.Clear();_gordoSceneComplete.Clear();
        _gordoConflicts.Clear();_gordoStates.Clear();_nativeGordoIds.Clear();_gordoRejected.Clear();_gordoNeverLoaded.Clear();GordoDuplicateRows=0;
    }
    private void BuildGordoSceneMembership()
    {
        ResetGordos();
        // Snapshot native locator objects by index: boxed IL2CPP generic
        // enumerators throw Collection modified on this Unity runtime.
        var locators=new List<UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator>();
        var nativeLocators=Addressables.Instance.m_ResourceLocators;
        for(int i=0;nativeLocators!=null&&i<nativeLocators.Count;i++)
        {var locator=nativeLocators[i]?.Locator;if(locator!=null&&!locator.WasCollected)locators.Add(locator);}
        // Membership comes from actual current SceneGroup AssetReferences via
        // Addressables Locate only. No asset/scene loading or fuzzy name match.
        foreach(var pair in _groups)
        {
            var group=pair.Value;
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool complete=true;int references=0;string phase="start";
            void Add(AssetReference reference)
            {
                phase="validate reference";
                if(reference==null||reference.WasCollected||!reference.RuntimeKeyIsValid()){complete=false;return;}
                references++;bool found=false;phase="enumerate locators";
                foreach(var locator in locators)
                {
                    phase="locator Locate";
                    if(locator==null||locator.WasCollected||!locator.Locate(reference.RuntimeKey,null,out var locations))continue;
                    phase="enumerate locations";
                    if(locations==null)continue;
                    int count=locations.Cast<Il2CppSystem.Collections.Generic.ICollection<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>>().Count;
                    for(int i=0;i<count;i++)
                    {
                        var location=locations[i];
                        if(location==null||location.WasCollected)continue;
                        phase="read InternalId";
                        string path=ScenePath(location.InternalId);
                        if(!path.EndsWith(".unity",StringComparison.OrdinalIgnoreCase))continue;
                        paths.Add(path);found=true;
                    }
                }
                if(!found)complete=false;
            }
            try
            {
                Add(group.CoreSceneReference);
                var refs=group.SceneReferences;
                for(int i=0;refs!=null&&i<refs.Length;i++)Add(refs[i]);
                _gordoScenes[pair.Key]=paths;
                if(complete&&references>0)_gordoSceneComplete.Add(pair.Key);
            }
            catch(Exception e){_app.LoggerInstance.Warning("Gordo scene membership "+pair.Key+": "+phase+" : "+e.ToString());}
        }
    }
    private void AddGordoIndex(Point point)
    {
        string id=GordoMapPolicy.Key(point.Id);if(id==null)return;
        _gordoScenes.TryGetValue(point.GroupName??"",out var scenes);
        bool complete=_gordoSceneComplete.Contains(point.GroupName??"");
        if(!GordoMapPolicy.SceneAllowed(ScenePath(point.SourceScene),scenes,complete))
        {
            _gordoRejected.Add(id+" | "+point.GroupName+" | "+point.SourceScene+" | not in current SceneGroup references");
            return;
        }
        point.GordoRank=complete&&!string.IsNullOrEmpty(point.SourceScene)?1:0;
        if(_gordoPoints.TryGetValue(id,out var existing))
        {
            GordoDuplicateRows++;
            var merge=GordoMapPolicy.Merge(existing.GordoRank,point.GordoRank,
                string.Equals(existing.GroupName,point.GroupName,StringComparison.Ordinal),
                (existing.Position-point.Position).sqrMagnitude<=.0001f);
            if(merge==GordoMapPolicy.Duplicate.Conflict)_gordoConflicts.Add(id);
            else if(merge==GordoMapPolicy.Duplicate.Replace)
            {Points.Remove(existing);_gordoPoints[id]=point;Points.Add(point);_gordoConflicts.Remove(id);}
            return;
        }
        _gordoPoints[id]=point;Points.Add(point);
    }
    private void SupplementNativeGordos()
    {
        // Native markers may add a loaded/snared instance not baked into the
        // index. Require its exact GameModel ID, never a Gordo-looking prefab.
        var models=_app.Context.GameModel.AllGordos();
        var markers=_app.Context.MapDirector?.Markers;
        if(models==null)return;
        bool added=false;
        if(markers!=null)
        {
            foreach(var pair in models)
            {
                if(!markers.TryGetValue(pair.Key,out var marker)||marker==null||marker.WasCollected)continue;
                var source=marker.Source;
                var group=source?.SceneGroup;
                if(source==null||source.WasCollected||group==null||group.WasCollected||!group.IsGameplay)continue;
                var model=pair.Value;if(model==null||model.WasCollected)continue;
                var position=source.MapPosition;
                if(!float.IsFinite(position.x)||!float.IsFinite(position.y)||!float.IsFinite(position.z))continue;
                if(_gordoPoints.TryGetValue(pair.Key,out var existing))
                {
                    // Same persistent instance: update one row, never append it again.
                    existing.Group=group;existing.GroupName=group.name;existing.Position=position;existing.GordoRank=2;
                    _gordoConflicts.Remove(pair.Key);continue;
                }
                var type=model.identifiableType;
                string name=type?.LocalizedName?.GetLocalizedString();
                if(string.IsNullOrWhiteSpace(name)||name.StartsWith("!!",StringComparison.Ordinal))name=type?.name??"Гордо";
                var point=new Point{Id=pair.Key,Category="Гордо",Name=name,Group=group,GroupName=group.name,
                    Position=position,ExactHeight=true,GordoTarget=model.targetCount,GordoRank=2,Detail="Native MapDirector marker / GameModel persistent ID"};
                _gordoPoints[pair.Key]=point;Points.Add(point);added=true;
            }
        }
        bool needActorScan=false;
        foreach(var pair in models)
        {
            string key=GordoMapPolicy.Key(pair.Key);
            if(key!=null&&!_gordoPoints.ContainsKey(key)){needActorScan=true;break;}
        }
        if(needActorScan)
        {
            Dictionary<string,GordoEat> eats=null;
            foreach(var pair in models)
            {
                string id=GordoMapPolicy.Key(pair.Key);
                if(id==null||_gordoPoints.ContainsKey(id))continue;
                var model=pair.Value;if(model==null||model.WasCollected)continue;
                var go=model.gameObj;
                GordoEat eat=null;
                if(go==null||go.WasCollected)
                {
                    if(eats==null)
                    {
                        eats=new Dictionary<string,GordoEat>(StringComparer.Ordinal);
                        foreach(var candidate in UnityEngine.Object.FindObjectsOfType<GordoEat>())
                        {
                            if(candidate==null||candidate.WasCollected)continue;
                            string cid=GordoMapPolicy.Key(candidate.Id);
                            if(cid!=null&&!eats.ContainsKey(cid))eats[cid]=candidate;
                        }
                    }
                    eats.TryGetValue(id,out eat);
                    go=eat?.gameObject;
                }
                if(go==null||go.WasCollected)continue;
                var position=go.transform.position;
                if(!float.IsFinite(position.x)||!float.IsFinite(position.y)||!float.IsFinite(position.z))continue;
                SceneGroup group=null;
                var gScene=go.scene;
                if(gScene.IsValid()&&gScene.isLoaded)
                {
                    string path=ScenePath(gScene.path);
                    foreach(var sp in _gordoScenes)
                        if(_gordoSceneComplete.Contains(sp.Key)&&sp.Value.Contains(path)&&_groups.TryGetValue(sp.Key,out group))
                            break;
                }
                if(group==null)
                {
                    var loaderGroup=SystemContext.Instance?.SceneLoader?.CurrentSceneGroup;
                    if(loaderGroup!=null&&!loaderGroup.WasCollected&&loaderGroup.IsGameplay)group=loaderGroup;
                }
                if(group==null||group.WasCollected||!group.IsGameplay)continue;
                var type=model.identifiableType;
                string name=type?.LocalizedName?.GetLocalizedString();
                if(string.IsNullOrWhiteSpace(name)||name.StartsWith("!!",StringComparison.Ordinal))name=type?.name??"Гордо";
                int target=model.targetCount>0?model.targetCount:(eat??go.GetComponent<GordoEat>())?.TargetCount??0;
                var point=new Point{Id=id,Category="Гордо",Name=name,Group=group,GroupName=group.name,
                    Position=position,ExactHeight=true,GordoTarget=target,GordoRank=2,Detail="Native GordoEat / GameModel persistent ID"};
                _gordoPoints[id]=point;Points.Add(point);added=true;
            }
        }
        if(added){_filterResult=null;RefreshGordos();}
    }
    // 0.8.1 radar: the atlas row of a persistent gordo model (null when not indexed).
    internal Point GordoPoint(string id)=>id!=null&&_gordoPoints.TryGetValue(id,out var point)?point:null;
    internal GordoMapPolicy.State GordoState(Point point)=>point!=null&&point.Category=="Гордо"&&
        _gordoStates.TryGetValue(point.Id,out var state)?state:GordoMapPolicy.State.Unknown;
    internal string GordoStateLabel(Point point)=>GordoState(point)==GordoMapPolicy.State.Unknown?GordoNeverLoaded(point)?M28TrackingStatePolicy.GordoNeverLoaded:"Состояние Гордо неизвестно":point.GordoStateText??"Состояние Гордо неизвестно";
    private void RefreshGordos()
    {
        _gordoStates.Clear();_nativeGordoIds.Clear();_gordoNeverLoaded.Clear();
        try
        {
            var models=_app.Context.GameModel.AllGordos();
            int flag=GordoEat.ALREADY_BURST_FLAG;
            foreach(var pair in _gordoPoints)
            {
                var point=pair.Value;bool present=models!=null&&models.TryGetValue(pair.Key,out var unused);
                var model=present?models[pair.Key]:null;bool found=model!=null&&!model.WasCollected;
                bool never=models!=null&&M28TrackingStatePolicy.Classify(found,true,present,_gordoConflicts.Contains(pair.Key))==M28TrackingStatePolicy.Missing.NeverLoaded;
                if(never)_gordoNeverLoaded.Add(pair.Key);
                int eaten=found?model.gordoEatCount:0;
                int target=found&&model.targetCount>0?model.targetCount:point.GordoTarget;
                var state=GordoMapPolicy.Observe(found,eaten,target,flag,_gordoConflicts.Contains(pair.Key));
                _gordoStates[pair.Key]=state;
                if(point.GordoStateText==null||point.GordoState!=state||point.GordoEaten!=eaten||point.GordoTargetDisplay!=target||point.GordoUnvisited!=never)
                {
                    point.GordoState=state;point.GordoEaten=eaten;point.GordoTargetDisplay=target;point.GordoUnvisited=never;
                    point.GordoStateText=state switch
                    {
                        GordoMapPolicy.State.Burst=>"Лопнул",
                        GordoMapPolicy.State.Fed=>"Накормлен · "+eaten+" / "+target,
                        GordoMapPolicy.State.Hungry=>target>0?"Не лопнул · "+eaten+" / "+target:"Не лопнул · цель кормления неизвестна",
                        _=>never?M28TrackingStatePolicy.GordoNeverLoaded:"Состояние неизвестно · модель не загружена или конфликт ID"
                    };
                    point.GordoDisplayName=point.Name+" · "+point.GordoStateText;
                }
            }
            // Only currently visible, real scene UI counts. Pooled/prefab and
            // inactive markers do not suppress our single marker for that ID.
            if(NativeMapOpen&&Ui._map!=null)
            {
                var markers=Ui._map.GetComponentsInChildren<GordoMarkerUI>(false);
                for(int i=0;i<markers.Length&&i<256;i++)
                {
                    var marker=markers[i];
                    if(marker==null||marker.WasCollected||string.IsNullOrEmpty(marker._gordoIdentifier))continue;
                    var image=marker._iconImage;
                    if(!marker.gameObject.activeInHierarchy||image==null||image.WasCollected||!image.enabled||image.color.a<=0)continue;
                    _nativeGordoIds.Add(marker._gordoIdentifier);
                }
            }
        }
        catch(Exception e){_gordoStates.Clear();_nativeGordoIds.Clear();_gordoNeverLoaded.Clear();_app.Fail("read-only gordo markers",e);}
    }
    private bool DrawGordoOverlay(Point point)=>point.Category!="Гордо"||GordoMapPolicy.DrawOverlay(point.Id,_nativeGordoIds);
}
