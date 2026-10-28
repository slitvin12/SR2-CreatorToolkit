using System;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using Il2Cpp;
namespace CreatorToolkit;
internal static class M18Diagnostics
{
    internal static string Snapshot(ToolkitMod app)
    {
        var camera=Camera.main;
        var lookup=LookupDirector.GetIfReady();
        var native=lookup?._identifiableTypeByRefId;
        var types=new System.Collections.Generic.List<object>();
        if(native!=null)foreach(var pair in native)
        {
            string id=pair.Key??"";
            if(id.IndexOf("Drone",StringComparison.OrdinalIgnoreCase)<0&&id.IndexOf("Shadow",StringComparison.OrdinalIgnoreCase)<0&&id.IndexOf("Ghost",StringComparison.OrdinalIgnoreCase)<0)continue;
            var type=pair.Value;if(type==null)continue;
            types.Add(new{Id=id,Name=type.name,Localized=type.LocalizedName?.GetLocalizedString(),Prefab=type.prefab?.name});
        }
        var row=new{Utc=DateTime.UtcNow,app.Ready,app.Player.HasCheckpoint,app.Player.CheckpointStatus,
            Maximum=app.Optimization.MaximumActive,Graphics=app.Optimization.MaximumStatus,
            FarClip=camera?.farClipPlane,Lod=QualitySettings.lodBias,Width=Screen.width,Height=Screen.height,
            RegistryDiscoveries=types,
            GhostRoutes=app.Map.Points.Where(p=>p.Id.StartsWith("component-route|",StringComparison.Ordinal)).Select(p=>new{p.Id,p.Name,p.GroupName,p.SourceScene,Position=p.Position.ToString("F2"),p.ExactHeight,p.Detail}).ToArray(),
            GordoPending=app.Actors.GordoSpawnPending,GordoStatus=app.Actors.GordoSpawnStatus};
        return "M18_SNAPSHOT "+JsonSerializer.Serialize(row)+"\n"+app.Optimization.MaximumDiagnostics()+"\n"+M17RuntimeDiagnostics.Snapshot(app);
    }
}
