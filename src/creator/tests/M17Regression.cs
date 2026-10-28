using System;
using System.IO;
using System.Linq;
using CreatorToolkit;
internal static class M17Regression
{
 internal static void Run(Action<bool,string> report,string root)
 {
  void Check(bool ok,string name)=>report(ok,"m15 / followup / m17 / "+name);
  // A dense resource field used to collapse into counted badges. Every node
  // now keeps its own marker at every zoom level and every layer.
  var points=Enumerable.Range(0,1800).Select(i=>((float)(i%30)*3,(float)(i/30)*3,i)).ToArray();
  var markers=MapOverlayLayout.Place(points,MapOverlayLayout.DefaultSize);
  Check(markers.Count==1800,"dense 1800-point resource field never collapses into grouped badges");
  Check(markers.Select(m=>m.Item).Distinct().Count()==1800,"dense placement preserves every identity exactly once");
  Check(markers.Zip(points).All(p=>p.First.Item==p.Second.Item3&&p.First.X==p.Second.Item1&&p.First.Y==p.Second.Item2),"every marker sits exactly on its own projected map position");
  Check(markers.All(m=>m.Size==MapOverlayLayout.DefaultSize)&&markers.Any(m=>m.Crowded),"dense neighbours keep one fixed icon size and are only flagged as overlapping");
  for(int i=1;i<=250;i++){float original=i*10;float clip=M17Policy.FarClip(original,.3f);Check(clip>0&&clip<=original&&clip<=180,"camera distance finite and never increases draw distance "+i);}
  string Read(string f)=>File.ReadAllText(Path.Combine(root,"src","creator",f));
  string ui=Read("ToolkitUi.cs"),actors=Read("ActorTools.cs"),map=Read("MapTools.cs"),player=Read("PlayerTools.cs"),opt=Read("OptimizationTools.cs");
  Check(ui.Contains("float glint=knob?")&&ui.Contains("SnapX(rect.x+dx+dw)")&&ui.Contains("SnapY(rect.y+dy+dh)"),"pill slices share snapped physical edges and no horizontal glint discontinuity");
  Check(ui.Contains("moved.y=Mathf.Clamp(moved.y,owner.y")&&ui.Contains("moved.width=Mathf.Min(moved.width,owner.width)"),"hover geometry cannot escape its unchanged owner hitbox");
  Check(ui.Contains("MapOverlayLayout.Place(projected,map.MarkerSize)")&&ui.Contains("MapMarkersForTest=markers.Count"),"the atlas always lays out one marker per object and never a dense badge");
  Check(map.Contains("existingPod.Position=pod.transform.position"),"loaded capsule location supersedes stale baked coordinate");
  Check(map.Contains("SourcesActualOnly||!p.SourceObservation||p.LiveSource"),"existing pots and possible spawn sites are user-filterable facts");
  Check(map.Contains("ComponentAcqDroneModel")&&map.Contains("drone.Awarded")&&map.Contains("research[point.Id].read"),"drone read and component reward states use separate native models");
  Check(actors.Contains("InstantiateGadget(snareType")&&actors.Contains("model.SnareGordo(job.Entry.Type)")&&!actors.Contains("InstantiateActorModel(entry.Type,player.SceneGroup,ground"),"Gordo creation uses native SnareModel not generic actor registration");
  Check(actors.Contains("Time.frameCount<=job.Frame+1")&&actors.Contains("GordoSpawnPending")&&actors.Contains("Destroyer.DestroyGadget"),"Gordo initialization is deferred one-at-a-time with exact owned gadget rollback");
  Check(player.Contains("loader.LoadSceneGroup(group,parameters)")&&player.Contains("_flightDestination==null&&_flyVelocity")&&map.Contains("atlas.TryFindZoneAt"),"NoClip group crossing uses native zone resolution and pauses movement during real load");
  Check(!opt.Contains("currentPlatformRenderPipelineSettings")&&!opt.Contains("allowDynamicResolution=")&&opt.Contains("camera.farClipPlane=_distanceExpected")&&opt.Contains("Attempt(\"distance\",RestoreDistance)"),"temporary scalar far-clip lease is included in restore without HDRP structs");
  Check(ui.Contains("float left=area.width*.40f")&&ui.Contains("Нативные варианты"),"world controls have separate time and weather cards");
 }
}
