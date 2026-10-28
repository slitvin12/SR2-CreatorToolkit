using System;
using System.Linq;
using System.Text.Json;
using UnityEngine;
namespace CreatorToolkit;
internal static class M17RuntimeDiagnostics
{
 internal static string Snapshot(ToolkitMod app)
 {
  var camera=Camera.main;
  var row=new {Utc=DateTime.UtcNow,app.Ready,app.Player.Flight,app.Player.FlightStreamingStatus,app.Actors.GordoSpawnPending,app.Actors.GordoSpawnStatus,
   Gordos=app.Actors.Catalog.Where(e=>e.Category=="Гордо").Select(e=>new{e.Reference,e.Name,e.CanSpawn,e.UnavailableReason}).ToArray(),
   PrototypeOrIncomplete=app.Actors.Catalog.Where(e=>e.IsPrototype||!e.CanSpawn).Select(e=>new{e.Reference,e.Name,e.UnavailableReason}).ToArray(),
   FarClip=camera==null?0:camera.farClipPlane,app.Optimization.DistanceActive,app.Optimization.DistanceStatus,
   app.UiForTests.MapProjectedForTest,app.UiForTests.MapMarkersForTest,app.UiForTests.MapCrowdedForTest,app.UiForTests.MapMarkerSizeForTest,
   app.Map.SourcesActualOnly,SourcePoints=app.Map.Points.Count(p=>p.SourceObservation),LiveSources=app.Map.Points.Count(p=>p.LiveSource),
   Drones=app.Map.Points.Where(p=>p.Category=="Дроны").Select(p=>new{p.Id,p.Name,p.Detail}).ToArray(),
   TargetCapsule=app.Map.Points.Where(p=>p.Id=="pod1210187464").Select(p=>new{p.Id,Position=p.Position.ToString("F3"),p.SourceScene,p.GroupName,State=app.Map.CapsuleStateLabel(p)}).ToArray()};
  return "M17_SNAPSHOT "+JsonSerializer.Serialize(row)+"\n"+M16RuntimeDiagnostics.Snapshot(app);
 }
}
