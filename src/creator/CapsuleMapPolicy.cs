using System;
using System.Collections.Generic;
namespace CreatorToolkit;
// Existence is separate from OPEN/LOCKED and from reward verification.
internal static class CapsuleMapPolicy
{
    internal static bool StaticAllowed(string scene,ISet<string> scenes,bool authoredActive)=>
        authoredActive&&!string.IsNullOrWhiteSpace(scene)&&scenes!=null&&scenes.Contains(scene.Replace('\\','/'));
    internal static bool NativeAllowed(bool sceneValid,bool sceneLoaded,bool active,bool enabled,string id)=>
        sceneValid&&sceneLoaded&&active&&enabled&&!string.IsNullOrWhiteSpace(id);
    // M29: a ComponentAcqDroneSpawner keeps the reward TreasurePod on the spawner object itself,
    // i.e. on the anchor of its phantom-drone route (GhostDroneIndex.tsv). Nothing is visible
    // there: the phantom drone flying that route hands the reward out. Same object = same
    // authored position, so a capsule on a route anchor is that route's reward, not a capsule.
    internal const double DroneRewardTolerance=.05;
    internal static string DroneRewardRoute(double x,double y,double z,IEnumerable<(string Id,double X,double Y,double Z)> anchors)
    {
        if(anchors==null||!double.IsFinite(x)||!double.IsFinite(y)||!double.IsFinite(z))return null;
        foreach(var a in anchors)
            if(!string.IsNullOrEmpty(a.Id)&&Math.Abs(a.X-x)<=DroneRewardTolerance&&Math.Abs(a.Y-y)<=DroneRewardTolerance&&
               Math.Abs(a.Z-z)<=DroneRewardTolerance)return a.Id;
        return null;
    }
}
