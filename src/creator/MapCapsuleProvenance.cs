using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Il2Cpp;
using UnityEngine;
namespace CreatorToolkit;
internal sealed partial class MapTools
{
    private readonly List<object> _capsuleSourceAudit=new();
    private void AddCapsuleIndex(Point point,bool authoredActive)
    {
        _gordoScenes.TryGetValue(point.GroupName??"",out var scenes);
        // M29: a capsule on a phantom-drone route anchor is that drone's reward, not a capsule.
        if(TakeGhostReward(point.Id,null,point.Position) is string route)
        {
            _capsuleSourceAudit.Add(new{point.Id,point.GroupName,point.SourceScene,AuthoredActive=authoredActive,
                MembershipComplete=_gordoSceneComplete.Contains(point.GroupName??""),Allowed=false,
                Reason="component-drone reward of route "+route+": handed out by the phantom drone, not a world capsule"});
            return;
        }
        bool allowed=CapsuleMapPolicy.StaticAllowed(point.SourceScene,scenes,authoredActive);
        _capsuleSourceAudit.Add(new{point.Id,point.GroupName,point.SourceScene,AuthoredActive=authoredActive,
            MembershipComplete=_gordoSceneComplete.Contains(point.GroupName??""),Allowed=allowed,
            Reason=allowed?"exact current SceneGroup reference":!authoredActive?"authored inactive or disabled":"source scene not positively confirmed in current SceneGroup"});
        if(allowed)Points.Add(point);
    }
    internal string CapsuleProvenanceAudit()
    {
        RefreshIndex();
        var loaded=new List<object>();
        foreach(var pod in UnityEngine.Object.FindObjectsOfType<TreasurePod>())
        {
            if(pod==null||pod.WasCollected)continue;
            var go=pod.gameObject;var position=pod.transform.position;
            var spawner=pod.GetComponent<Il2CppMonomiPark.SlimeRancher.Drone.ComponentAcqDroneSpawner>();
            loaded.Add(new{pod.Id,Scene=go.scene.path,Active=go.activeInHierarchy,Enabled=pod.enabled,
                X=position.x,Y=position.y,Z=position.z,Indexed=Points.Any(p=>p.Category=="Капсулы"&&p.Id==pod.Id),
                DroneRoute=spawner!=null?spawner.Id:null});
        }
        return "M22_CAPSULES "+JsonSerializer.Serialize(new{Utc=DateTime.UtcNow,Rows=_capsuleSourceAudit,DroneRewards=_ghostRewardPods,
            Membership=_gordoScenes.Select(p=>new{Group=p.Key,Complete=_gordoSceneComplete.Contains(p.Key),Scenes=p.Value}).ToArray(),
            Loaded=loaded,Points=Points.Where(p=>p.Category=="Капсулы").Select(p=>new{p.Id,p.GroupName,p.SourceScene,X=p.Position.x,Y=p.Position.y,Z=p.Position.z}).ToArray()})+"\n";
    }
}
