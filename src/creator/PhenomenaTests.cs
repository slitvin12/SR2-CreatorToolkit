using System;
using System.IO;
using System.Linq;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Aurora;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using UnityEngine;

namespace CreatorToolkit;

internal sealed partial class FeatureSuite
{
    private MapTools.Point _phenomenaOrigin;
    private IntPtr _phenomenaToken,_phenomenaSession;
    private string _phenomenaId="";
    private double _phenomenaClockBefore;
    private bool _phenomenaFreezeBefore;
    private void RetirePhenomenaFixtures()
    {
        _phenomenaOrigin=null;_phenomenaToken=_phenomenaSession=IntPtr.Zero;_phenomenaId="";
    }
    private AuroraObject ActualAuroraFixture()=>_app.Ready&&_app.Session==_phenomenaSession?
        UnityEngine.Object.FindObjectsOfType<AuroraObject>(true).FirstOrDefault(a=>a!=null&&!a.WasCollected&&
            a.Pointer==_phenomenaToken&&a.gameObject.scene.IsValid()&&a.gameObject.scene.isLoaded&&a._isSetup&&a.isActiveAndEnabled):null;
    private PhenomenaTools.Row ActualAuroraRow()=>_app.Phenomena.Rows.Single(r=>r.Id==_phenomenaId&&
        r.Token==_phenomenaToken&&r.Session==_app.Session);
    private void BuildPhenomenaSteps()
    {
        Add("Phenomena / opt-in default and no actor roots",()=>Require(!_app.Phenomena.Inspect&&
            _app.Phenomena.Rows.Count==0&&_app.Phenomena.NativeActorRoots==0,"Native observatory enabled without opt-in."));
        Add("Phenomena / actual loaded scene census",()=>{
            GuardCopy();_app.Phenomena.SetInspect(true);
            Require(_app.Phenomena.Rows.Count<=PhenomenaTools.MaxRows&&_app.Phenomena.Examined<=PhenomenaTools.MaxExamined&&
                _app.Phenomena.NativeActorRoots==0,"Native phenomena scan or retained roots are unbounded.");
            foreach(var row in _app.Phenomena.Rows)
                Require(row.Session==_app.Session&&row.Token!=IntPtr.Zero&&
                    ExplorerPolicy.Coordinates(row.Position.x,row.Position.y,row.Position.z),"A fabricated/stale phenomenon entered the census.");
            _app.LoggerInstance.Msg("Phenomena native census rows="+_app.Phenomena.Rows.Count+
                " examined="+_app.Phenomena.Examined+" source="+SystemContext.Instance.SceneLoader.CurrentSceneGroup.name);
        });
        Add("Phenomena / absent and stale native source cannot mutate time",()=>{
            GuardCopy();double before=_app.Context.TimeDirector.WorldTime();
            bool refused=false;
            try{_app.Phenomena.RequestWait(new PhenomenaTools.Row{Id="stale",Kind="Aurora",Session=IntPtr.Zero});}
            catch(InvalidOperationException){refused=true;}
            Require(refused&&_app.Pending==null&&Math.Abs(_app.Context.TimeDirector.WorldTime()-before)<.001,
                "An unavailable native phenomenon accepted a mutation.");
            _app.UiForTests.ShowPhenomenaForTest();_app.Input.Open();
        },.3f);
        Add("Phenomena / native observatory capture",()=>{
            ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"v060-phenomena.png"));
        },.3f);
        Add("Phenomena / primitive snapshots retired",()=>{
            _app.CloseMenu();_app.Phenomena.SetInspect(false);
            Require(_app.Phenomena.Rows.Count==0&&_app.Phenomena.NativeActorRoots==0,"Native scene roots survived observatory retirement.");
        });
        Add("Phenomena / actual Powderfall scene preload",()=>{
            GuardCopy();_app.CloseMenu();_app.Map.CloseNative();
            var player=_app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true);
            _phenomenaOrigin=new MapTools.Point{Group=player.SceneGroup,GroupName=player.SceneGroup.name,
                Position=Controller.Motor.TransientPosition,ExactHeight=true,AllowNearbyLanding=true,Name="Native observatory origin"};
            _phenomenaFreezeBefore=_app.World.FreezeTime;
            var group=Resources.FindObjectsOfTypeAll<SceneGroup>().First(g=>g!=null&&g.IsGameplay&&g.name=="PowderfallBluffs");
            // This is the real registered group's spawn, not a fabricated
            // phenomenon or guessed bridge transform. Native streaming and
            // the common stable-collision gate stay authoritative.
            _app.Map.RequestTeleport(new MapTools.Point{Group=group,GroupName=group.name,Position=group.InitialPlayerPosition,
                ExactHeight=false,AllowNearbyLanding=true,Name="Actual native Aurora scene"});
        },.5f);
        Add("Phenomena / positive active native Aurora fixture",()=>{
            GuardCopy();Require(_app.Map.LastTransitSucceeded&&_app.Map.LastSceneAssetsReady&&
                _app.Map.LastStableGroundSamples>=4,"Actual Aurora scene did not safely load.");
            _app.Phenomena.SetInspect(true);
            var row=_app.Phenomena.Rows.First(r=>r.Kind=="Aurora"&&r.CanWait&&
                UnityEngine.Object.FindObjectsOfType<AuroraObject>(true).Any(a=>a!=null&&a.Pointer==r.Token&&
                    a.isActiveAndEnabled&&a._colliders?.Count>0));
            _phenomenaToken=row.Token;_phenomenaId=row.Id;_phenomenaSession=row.Session;
            var source=ActualAuroraFixture();
            Require(source!=null&&_app.Phenomena.NativeActorRoots==0,"Native positive source is unavailable or rooted.");
            _app.LoggerInstance.Msg("Phenomena positive source name="+source.gameObject.name+" token="+source.Pointer+
                " source="+SystemContext.Instance.SceneLoader.CurrentSceneGroup.name+
                " start="+source._triggerStartHour+" end="+source._triggerEndHour+
                " triggerState="+source._triggerState+" inTriggerWindow="+source.InTriggerWindow()+
                " activeStart="+row.StartHour+" activeEnd="+row.EndHour+
                " state="+source._currState+" colliders="+source._colliders.Count+" hour="+_app.World.Hour()+
                " position="+source.transform.position.ToString("F3"));
            // Establish the daytime baseline solely through the same forward
            // clock feature. The original game turns its own bridge off.
            _app.World.SetFreezeTime(false);_app.World.SetHour(PhenomenaWindowPolicy.After(row.EndHour));
        },.2f,()=>UnityEngine.Object.FindObjectsOfType<AuroraObject>(true).Any(a=>a!=null&&a._isSetup&&
            a.isActiveAndEnabled&&a._colliders?.Count>0),12f);
        Add("Phenomena / native daytime phase before confirmation",()=>{
            GuardCopy();var source=ActualAuroraFixture();
            Require(source!=null&&source._currState.ToString()=="OFF","Original Aurora did not reach its daytime OFF phase.");
            _app.World.SetFreezeTime(true);_phenomenaClockBefore=_app.Context.TimeDirector.WorldTime();
            _app.Phenomena.Refresh();_app.Phenomena.RequestWait(ActualAuroraRow());
            Require(_app.Pending!=null&&Math.Abs(_app.Context.TimeDirector.WorldTime()-_phenomenaClockBefore)<.001,
                "Native phase wait mutated the clock before approval.");
            _app.UiForTests.ShowPhenomenaForTest(_phenomenaId);_app.Input.Open();
        },.3f,()=>ActualAuroraFixture()?._currState.ToString()=="OFF",40f);
        Add("Phenomena / positive native phase confirmation capture",()=>
            ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"v060-phenomena-confirmation.png")),.3f);
        Add("Phenomena / cancellation leaves actual native phase and clock unchanged",()=>{
            GuardCopy();_app.CloseMenu();
            Require(_app.Pending==null&&Math.Abs(_app.Context.TimeDirector.WorldTime()-_phenomenaClockBefore)<.001&&
                ActualAuroraFixture()?._currState.ToString()=="OFF","Cancelled native phase wait changed the source/clock.");
            _app.Phenomena.Refresh();_app.Phenomena.RequestWait(ActualAuroraRow());
            Require(_app.Pending!=null,"Actual native phase has no confirmation.");_app.AcceptConfirmation();
        },.3f);
        Add("Phenomena / approval advances only forward into original window",()=>{
            GuardCopy();var source=ActualAuroraFixture();
            Require(source!=null&&_app.Pending==null&&_app.Context.TimeDirector.WorldTime()>_phenomenaClockBefore&&
                (source.InTriggerWindow()==(source._triggerState.ToString()=="ON")),
                "Approved wait did not enter the real native ON branch.");
            _app.World.SetFreezeTime(false);
            _app.LoggerInstance.Msg("Phenomena native approved clock before="+_phenomenaClockBefore+
                " after="+_app.Context.TimeDirector.WorldTime()+" hour="+_app.World.Hour()+
                " window="+source.InTriggerWindow()+" state="+source._currState);
        },.2f);
        Add("Phenomena / original native transition activates solid colliders",()=>{
            GuardCopy();var source=ActualAuroraFixture();int enabled=0;
            Require(source!=null&&source._currState.ToString()=="ON","Original native Aurora never reached ON.");
            for(int i=0;i<source._colliders.Count;i++)
                if(source._colliders[i]!=null&&source._colliders[i].enabled&&!source._colliders[i].isTrigger&&
                    source._colliders[i].gameObject.activeInHierarchy)enabled++;
            Require(enabled>0,"Original native phase did not expose any real solid collision support.");
            _app.LoggerInstance.Msg("Phenomena native ON source="+source.gameObject.name+" token="+source.Pointer+
                " enabledSolidColliders="+enabled+" hour="+_app.World.Hour());
            _app.Phenomena.Refresh();_app.UiForTests.ShowPhenomenaForTest(_phenomenaId);_app.Input.Open();
        },.3f,()=>ActualAuroraFixture()?._currState.ToString()=="ON",40f);
        Add("Phenomena / active native observatory capture",()=>
            ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"v060-phenomena-active.png")),.3f);
        Add("Phenomena / active source uses guarded native travel",()=>{
            GuardCopy();_app.CloseMenu();_app.Phenomena.Refresh();_app.Phenomena.RequestTravel(ActualAuroraRow());
        },.3f);
        Add("Phenomena / actual solid landing after streamed travel",()=>{
            GuardCopy();Require(_app.Map.LastTransitSucceeded&&_app.Map.LastSceneAssetsReady&&
                _app.Map.LastStableGroundSamples>=4&&!_app.Player.TransitHeld&&Controller.Motor.enabled&&
                _app.Player.IsClearPosition(Controller.Motor.TransientPosition),"Native phenomenon travel bypassed or failed solid-ground readiness.");
            _app.LoggerInstance.Msg("Phenomena native safe landing position="+Controller.Motor.TransientPosition.ToString("F3")+
                " stableSamples="+_app.Map.LastStableGroundSamples+" assetsReady="+_app.Map.LastSceneAssetsReady);
            ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"v060-phenomena-world.png"));
        },.3f);
        Add("Phenomena / native guarded return and fixture retirement",()=>{
            GuardCopy();_app.Phenomena.SetInspect(false);Require(_phenomenaOrigin!=null,"Native observatory origin missing.");
            _app.Map.RequestTeleport(_phenomenaOrigin);
        },.3f);
        Add("Phenomena / returned origin leaves no automation or scene actor roots",()=>{
            GuardCopy();Require(_app.Map.LastTransitSucceeded&&_app.Map.LastSceneAssetsReady&&
                _app.Map.LastStableGroundSamples>=4&&!_app.Player.TransitHeld&&Controller.Motor.enabled,
                "Native observatory return failed its collision gate.");
            _app.World.SetFreezeTime(_phenomenaFreezeBefore);RetirePhenomenaFixtures();
            Require(!_app.Phenomena.Inspect&&_app.Phenomena.Rows.Count==0&&_app.Phenomena.NativeActorRoots==0,
                "Positive native observatory test retained scene sources or enabled itself.");
        });
    }
}