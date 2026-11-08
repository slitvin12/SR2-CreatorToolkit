using System;
using System.IO;
namespace CreatorToolkit;
// 0.8.7 offline regression: teleport landing (enclosure probe, transit grounding refresh), the
// slider / pinned time rate with smooth slow-motion physics and the spawn placement mode.
// Source wiring only; no Unity, no game.
internal static class M32Regression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool b,string s)=>check(b,"0.8.7 / "+s);
        string dir=Path.Combine(root,"src","creator");
        string R(string name){string p=Path.Combine(dir,name);return File.Exists(p)?File.ReadAllText(p).Replace("\r\n","\n"):"";}
        string player=R("PlayerTools.cs"),landing=R("PlayerSafeLanding.cs"),m18=R("M18PlayerTools.cs");
        string rate=R("CinematicRateTools.cs"),rateUi=R("CinematicRateUi.cs"),policy=R("MultiplayerPolicy.cs"),bridge=R("MultiplayerBridge.cs");
        string placement=R("SpawnPlacementTools.cs"),placementUi=R("SpawnPlacementUi.cs"),actors=R("ActorTools.cs"),ui=R("ToolkitUi.cs"),mod=R("ToolkitMod.cs");
        int stop=player.IndexOf("_controller.StopAbilitiesForTeleport();\n            _holdController = _controller;",StringComparison.Ordinal);
        int capture=player.IndexOf("_holdGravity = _controller._BypassGravity_k__BackingField;",StringComparison.Ordinal);
        C(stop>=0&&capture>stop,"transit hold ends native abilities before capturing motor and gravity");
        C(player.Contains("finally { _transitHold = false; if (held) RefreshGroundingAfterTransit(); }")&&landing.Contains("grounding.IsStableOnGround=false;")&&
          landing.Contains("jump._jumpRequested=false;")&&m18.Contains("PlaceForMap(ground);RefreshGroundingAfterTransit();"),"a teleport landing discards the stale grounding report and the buffered jump");
        C(player.Contains("_holdMotor.enabled = _holdMotorEnabled || !Flight;")&&player.Contains("_BypassGravity_k__BackingField = _holdGravity && Flight;"),"the transit release never leaves the motor asleep or gravity bypassed");
        C(player.Contains("if(!ClearCapsule(candidate)||Enclosed(candidate))continue;")&&player.Contains("&&ClearCapsule(feet)&&!Enclosed(feet);")&&
          player.Contains("if (!ClearCapsule(here) || Enclosed(here))"),"landing search, clear-position checks and the NoClip exit reject spots inside geometry");
        C(landing.Contains("Physics.queriesHitBackfaces=true;")&&landing.Contains("finally{try{Physics.queriesHitBackfaces=before;}catch{}}")&&landing.Contains("_enclosureUnavailable=true;"),
          "the enclosure probe restores the global back-face flag and falls back to the capsule check");
        C(player.Contains("Array.Sort(distances,order);")&&player.Contains("nearest=hit.distance;landing=candidate;break;")&&player.Contains("return TryColumnGround(target,ceiling,range,out landing);"),
          "the ground search takes the highest valid surface of the column first");
        C(rate.Contains("internal bool Pinned")&&rate.Contains("if(wanted||_orphanUntil>0f)TryResume(wanted);")&&rate.Contains("try{Lease(want,false);Reapplies++;}")&&rate.Contains("\"cinematic-rate.txt\""),
          "a pinned rate waits out the pause, menu, map and loading, then comes back; persisted between launches");
        C(rate.Contains("try{Lease(want,true);Adoptions++;}"),"a rate written back by a save/restore writer is re-owned, never orphaned");
        C(rate.Contains("if(!SmoothPhysics||rate>=1f)return;")&&rate.Contains("v=>Time.fixedDeltaTime=v")&&rate.Contains("try{_fixedLease?.Restore();}"),"slow motion shortens the physics step under its own restored lease");
        C(rateUi.Contains("Slider(RateSliderId,")&&rateUi.Contains("_app.Queue(\"cinematic time pin\",")&&rateUi.Contains("_app.Queue(\"cinematic smooth step\",")&&!ui.Contains("Slider(66,"),"time page: own slider id, pin and smooth-physics toggles");
        C(policy.Contains("\"cinematic time pin\", \"cinematic smooth step\", \"Разрешить ускорение симуляции?\"")&&bridge.Contains("\"cinematic time pin\" => _app.CinematicRate?.Pinned == true,"),
          "the pin, smooth step and acceleration prompt are solo-only in co-op; unpinning stays allowed");
        C(placement.Contains("_app.Coop?.Deny(\"spawn\")")&&placement.Contains("_app.Actors.RequireSpawnable(entry);")&&
          placement.Contains("_app.Queue(\"spawn\",()=>_app.Actors.SpawnPlaced(entry,positions,yaw));")&&placement.Contains("_app.Queue(\"spawn\",()=>_app.Actors.SpawnGordoAt(entry,ground,yaw));"),
          "placement mode spawns only through the ordinary queued, co-op-gated spawn");
        C(actors.Contains("internal void SpawnPlaced(Entry entry,Vector3[] positions,float yaw)")&&actors.Contains("private void BeginGordoSpawn(Entry entry)=>BeginGordoSpawn(entry,null,0f);")&&
          actors.Contains("BeginGordoSpawn(entry,ground,yaw);")&&actors.Contains("chosen.HasValue?Quaternion.Euler(0f,yaw,0f):Quaternion.identity,false,out go,out model);"),
          "placed actors and the chosen Gordo snare carry the chosen facing");
        C(mod.Contains("(\"spawn placement\",Placement.Tick)")&&mod.Contains("SafeTick(\"cancel spawn placement\",()=>Placement?.Reset());")&&ui.Contains("if(_app.Placement?.Showing==true){DrawPlacement();return;}"),
          "placement ticks with the world, ends on a session change and replaces the panel while picking");
        C(ui.Contains("\"Выбрать место…\"")&&ui.Contains("BeginPlacement(_selected,1)")&&ui.Contains("BeginPlacement(_selected,_quantity)"),"the spawn page offers placement for Gordos and every other entry");
        C(placementUi.Contains("Fill(")&&!placementUi.Contains("GUI.Label(")&&!placementUi.Contains("GUI.Box("),"the placement overlay uses only the shared IMGUI wrappers");
    }
}
