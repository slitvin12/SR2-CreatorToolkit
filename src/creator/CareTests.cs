using System;
using System.IO;
using System.Linq;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Slime;
using Il2CppMonomiPark.SlimeRancher.World;
using Il2CppMonomiPark.SlimeRancher.Player.PlayerItems;
using UnityEngine;

namespace CreatorToolkit;

internal sealed partial class FeatureSuite
{
    private SlimeEat _careFixture;
    private string _careFavorite = "",_careSlime="";
    private int _careSlot, _careAccepted, _careBefore;
    private float _careHungerBefore;
    private GadgetTools.Entry _gadgetFixture;
    private Gadget _placedGadgetFixture;
    private int _gadgetModelsBefore, _gadgetInventoryBefore;
    private System.Collections.Generic.HashSet<IntPtr> _gadgetTokensBefore;
    private int _gadgetSelectedBefore;
    private IntPtr _gadgetLockedBefore;
    private Quaternion _gadgetCameraBefore;
    private bool _gadgetCameraAimed;
    private IntPtr _gadgetCameraSession;
    private Vector3 _gadgetAimPoint;
    private int _gadgetAimRetries;
    private float _gadgetAimNext;
    private string _gadgetAimReason="";
    private void CaptureCare(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory, "v060-" + name + ".png"));
    private Vector3 FindGadgetFixtureGround()
    {
        GuardCopy();
        var camera=Camera.main;var native=_app.Gadgets.NativeItem;
        Require(camera!=null&&native?.GadgetItemMetadata!=null,"Native fixture ray metadata/camera unavailable.");
        Vector3 origin=camera.transform.position,player=Controller.Motor.TransientPosition;
        Vector3 forward=Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up).normalized;
        Require(forward.sqrMagnitude>.9f,"Native fixture camera has no planar forward direction.");
        var metadata=native.GadgetItemMetadata;int examined=0;
        foreach(float radius in new[]{4.2f,5.2f,6.3f})
        {
            for(int i=0;i<16;i++)
            {
                examined++;
                int step=i==0?0:((i+1)/2)*(i%2==1?1:-1);
                float angle=step*22.5f;
                Vector3 direction=Quaternion.AngleAxis(angle,Vector3.up)*forward;
                Vector3 candidate=player+direction*radius;
                if(!Physics.Raycast(new Ray(candidate+Vector3.up*6f,Vector3.down),out var ground,12f,
                    GadgetItem.CAN_PLACE_CHECK_RAY_MASK,QueryTriggerInteraction.Ignore))continue;
                var collider=ground.collider;
                if(collider==null||collider.isTrigger||collider.attachedRigidbody!=null||
                    !collider.gameObject.scene.IsValid()||!collider.gameObject.scene.isLoaded||
                    collider.GetComponentInParent<IdentifiableActor>()!=null||ground.normal.y<.995f||
                    !CarePolicy.NativePlacementSurface(ground.normal.y,metadata.MaxValidPlacementSlope))continue;
                string surface=(collider.name+" "+collider.transform.parent?.name).ToLowerInvariant();
                if(surface.Contains("water")||surface.Contains("slimesea")||surface.Contains("lava"))continue;
                Vector3 delta=ground.point-origin;float distance=delta.magnitude;
                if(distance<metadata.MinPlacementFootprintVisibleDist+.2f||
                    distance>Mathf.Min(8f,metadata.MaxValidPlacementDist))continue;
                // The downward ray only searches. The real camera ray must
                // reach exactly that same first solid surface without a wall,
                // actor, preview or fabricated hit replacing its geometry.
                if(!Physics.Raycast(new Ray(origin,delta.normalized),out var aimed,distance+.05f,
                    GadgetItem.CAN_PLACE_CHECK_RAY_MASK,QueryTriggerInteraction.Ignore)||
                    aimed.collider!=collider||Vector3.Distance(aimed.point,ground.point)>.1f||
                    !CarePolicy.NativePlacementSurface(aimed.normal.y,metadata.MaxValidPlacementSlope))continue;
                _app.LoggerInstance.Msg("Gadget owned fixture actual open ground candidates="+examined+
                    " point="+aimed.point.ToString("F3")+" normal="+aimed.normal.ToString("F3")+
                    " surface="+collider.name+" distance="+distance+
                    " nativeMinimumNormalY="+Mathf.Sin(metadata.MaxValidPlacementSlope*Mathf.Deg2Rad));
                return aimed.point;
            }
        }
        throw new InvalidOperationException("No bounded real open flat fixture surface in the native camera range; candidates="+examined);
    }
    private bool AimGadgetFixtureReady()
    {
        GuardCopy();
        if(Time.unscaledTime<_gadgetAimNext)return false;
        _gadgetAimNext=Time.unscaledTime+.15f;
        var camera=Camera.main;var controller=Controller._cameraController;
        if(camera==null||controller==null)return false;
        bool ground=_app.Gadgets.TryGround(out var ray,out var hit,out _,out var reason);
        float error=ground?Vector3.Distance(hit.point,_gadgetAimPoint):float.PositiveInfinity;
        float aim=ground?Vector3.Angle(camera.transform.forward,ray.direction):float.PositiveInfinity;
        _gadgetAimReason="ready="+ground+" attempts="+_gadgetAimRetries+" pointError="+error+" aimError="+aim+
            " camera="+camera.transform.position.ToString("F3")+" forward="+camera.transform.forward.ToString("F3")+
            " actual="+(ground?hit.point.ToString("F3"):reason);
        if(ground&&hit.normal.y>=.995f&&error<=.15f&&aim<.1f)
        {
            _app.LoggerInstance.Msg("Gadget owned fixture actual native aim converged: "+_gadgetAimReason);
            return true;
        }
        // The camera is attached to a following rig. Turning it can move its
        // real origin; re-aim through its native API after actual native frames.
        // This is copy-only, bounded, and never disables the player blocker or
        // substitutes the earlier downward search hit for the current ray.
        if(_gadgetAimRetries<12)
        {
            _gadgetAimRetries++;
            controller.SetRotation(Quaternion.LookRotation(_gadgetAimPoint-camera.transform.position,Vector3.up));
        }
        if(_gadgetAimRetries==12)_app.LoggerInstance.Msg("Gadget owned fixture aim still waiting: "+_gadgetAimReason);
        return false;
    }
    private void RetireCareFixtures()
    {
        _careFixture = null; _careFavorite = _careSlime=""; _gadgetFixture = null; _placedGadgetFixture = null;_gadgetTokensBefore=null;
        if(_gadgetCameraAimed&&_app.Ready&&_app.Session==_gadgetCameraSession&&Controller?._cameraController!=null)
            Controller._cameraController.SetRotation(_gadgetCameraBefore);
        _gadgetCameraAimed=false;_gadgetCameraSession=IntPtr.Zero;
        _gadgetAimPoint=Vector3.zero;_gadgetAimRetries=0;_gadgetAimNext=0;_gadgetAimReason="";
    }
    private void BuildCareSteps()
    {
        Add("Care / defaults and value-only actor snapshots", () =>
        {
            Require(!_app.Care.Inspect && _app.Care.NativeActorRoots == 0 && _app.Gadgets.PreviewRoots == 0,
                "Care enabled itself or retained a native actor/preview.");
            Require(typeof(SlimeCareTools.Hit).GetFields(System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).All(f => !typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).IsAssignableFrom(f.FieldType)),
                "Inspector value snapshot holds a native wrapper.");
        });
        Add("Care / owned native favorite fixture", () =>
        {
            GuardCopy(); _app.CloseMenu(); _app.Convenience.Reset(); _app.Farm.Reset(); _app.World.SetFreezeMobs(false);
            var chosen = _app.Actors.Catalog.First(e => e.Category == "Слаймы" && !e.IsPrototype && e.CanSpawn &&
                e.Type.TryCast<SlimeDefinition>()?.Diet?.FavoriteIdents?.Length > 0);
            var definition = chosen.Type.Cast<SlimeDefinition>();
            _careSlime=chosen.Reference;
            var food = _app.Actors.Catalog.First(e => e.Category == "Еда" && e.CanSpawn &&
                definition.Diet.FavoriteIdents.Any(f => f == e.Type));
            _careFavorite = food.Reference;
            var forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            var position = Controller.Motor.TransientPosition + forward * 2.4f + Vector3.up * .7f;
            var root = _app.Actors.SpawnAtForTest(chosen, position);
            _careFixture = root.GetComponent<SlimeEat>();
            Require(_careFixture != null, "Native slime fixture lacks SlimeEat.");
            // The native chomp owns rotation/animation/joints. Do not lock a
            // spawned fixture's Rigidbody and then mistake that blockage for
            // a production feeding failure.
            var ammo = _app.Context.PlayerState.Ammo;
            _careSlot = Enumerable.Range(0, ammo.Slots.Length).First(i => ammo.Slots[i].IsUnlocked);
            _app.Actors.FillAmmoForTest(food, _careSlot);
            Require(ammo.GetSlotCount(_careSlot) > 0 && ammo.GetSlotName(_careSlot) == food.Type, "Favorite fixture ammo initialization failed.");
            ammo.SetSelectedSlot(_careSlot); ammo.Slots[_careSlot].Count = 12;
        }, .8f);
        Add("Care / native inspector reports favorite and emotions", () =>
        {
            GuardCopy();
            _careFixture._emotions.Set(SlimeEmotions.Emotion.HUNGER, 1f);
            _careFixture._emotions.Set(SlimeEmotions.Emotion.AGITATION, .5f);
            _app.Care.SetInspect(true); _app.Care.ScanNow();
            var actor = _careFixture.GetComponent<IdentifiableActor>();
            var hit = _app.Care.Hits.FirstOrDefault(h => h.ActorToken == actor.Pointer);
            Require(hit != null && hit.Hungry && hit.Hunger > .95f && Math.Abs(hit.Agitation - .5f) < .05f &&
                hit.Favorite.Length > 0 && hit.Distance <= 25, "Inspector failed actual native favorite/emotion fields.");
            _app.UiForTests.ShowCreationForTest(1); _app.Input.Open();
        }, .3f);
        Add("Care / inspector native capture", () => CaptureCare("slime-inspector"), .3f);
        Add("Care / feeding confirmation does not spend", () =>
        {
            int before = _app.Context.PlayerState.Ammo.GetSlotCount(_careSlot);
            _app.Care.RequestFeed();
            Require(_app.Pending != null && _app.Context.PlayerState.Ammo.GetSlotCount(_careSlot) == before,
                "Unapproved care request spent inventory.");
        }, .3f);
        Add("Care / feeding confirmation capture", () => CaptureCare("feed-confirmation"), .3f);
        Add("Care / cancelled feeding remains unchanged", () =>
        {
            int before = _app.Context.PlayerState.Ammo.GetSlotCount(_careSlot);
            _app.CloseMenu();
            Require(_app.Pending == null && _app.Context.PlayerState.Ammo.GetSlotCount(_careSlot) == before,
                "Cancelled care request spent inventory.");
        });
        Add("Care / selected favorite portions accepted natively", () =>
        {
            GuardCopy(); _careBefore = _app.Context.PlayerState.Ammo.GetSlotCount(_careSlot);
            _careHungerBefore = _careFixture._emotions.Get(SlimeEmotions.Emotion.HUNGER);
            _app.LoggerInstance.Msg("Care owned fixture actor="+_careFixture.GetComponent<IdentifiableActor>().Pointer+
                " hunger="+_careHungerBefore+" canOffer="+_app.Care.CanOffer(_careFixture,_app.Context.PlayerState.Ammo.GetSlotName(_careSlot))+
                " canChomp="+_careFixture._chomper.CanChomp()+" nextChomp="+_careFixture._chomper._nextChompTime+
                " worldTime="+_app.Context.TimeDirector.WorldTime());
            _careAccepted = _app.Care.FeedNow();
            Require(_careAccepted > 0 && _careAccepted <= CarePolicy.MaxMeals &&
                _app.Context.PlayerState.Ammo.GetSlotCount(_careSlot) == _careBefore - _careAccepted,
                "Favorite handoff did not conserve real selected-slot food.");
            _app.LoggerInstance.Msg("Care native portions accepted=" + _careAccepted +
                " source=" + _careFavorite + " before=" + _careBefore + " after=" + _app.Context.PlayerState.Ammo.GetSlotCount(_careSlot));
        }, 5f,()=>_careFixture!=null&&_app.Care.CanOffer(_careFixture,_app.Context.PlayerState.Ammo.GetSlotName(_careSlot)),15f);
        Add("Care / normal chomp reduces hunger without force writes", () =>
        {
            float hunger=_careFixture._emotions.Get(SlimeEmotions.Emotion.HUNGER);
            _app.LoggerInstance.Msg("Care completion hunger="+hunger+" chomping="+_careFixture.IsChomping()+
                " sleeping="+_careFixture.IsSleeping()+" bite="+_careFixture._maxBiteDistance+
                " nativeDigest="+SlimeEat.DIGEST_TIME+" biteDelay="+Chomper.COMPLETE_BITE_DELAY);
            Require(hunger < _careHungerBefore - .05f,"Normal native chomp did not reduce the owned fixture hunger: "+hunger);
            Require(_app.Care.NativeActorRoots == 0, "Meals rooted a scene actor.");
            _app.Care.ScanNow(); _app.UiForTests.ShowCreationForTest(1); _app.Input.Open();
        }, .3f);
        Add("Care / fed native inspector capture", () => CaptureCare("slime-fed"), .3f);
        Add("Care / empty selected slot spends nothing", () =>
        {
            GuardCopy(); _app.CloseMenu();
            var slot = _app.Context.PlayerState.Ammo.Slots[_careSlot]; slot.Count = 0;
            Require(_app.Care.FeedNow() == 0 && slot.Count == 0, "Empty selected slot manufactured a portion.");
        }, 2.2f);
        Add("Care / non-food selected slot spends nothing", () =>
        {
            GuardCopy(); var ammo = _app.Context.PlayerState.Ammo;
            var resource = _app.Actors.Catalog.First(e => e.Category == "Плорты" && e.CanSpawn);
            _app.Actors.FillAmmoForTest(resource, _careSlot);
            Require(ammo.GetSlotName(_careSlot) == resource.Type, "Non-food fixture initialization failed.");
            ammo.Slots[_careSlot].Count = 4;
            Require(_app.Care.FeedNow() == 0 && ammo.GetSlotCount(_careSlot) == 4,
                "Non-food slot was fed or transformed a slime.");
        }, 2.2f);
        Add("Care / full sleeping slime and fresh resolution", () =>
        {
            GuardCopy(); var ammo = _app.Context.PlayerState.Ammo;
            // The feeding fixture is fully mobile and may despawn/transform
            // after its verified meal. Never dereference its old component for
            // a later sleep test: create a separate owned native fixture.
            var slime=_app.Actors.Catalog.Single(e=>e.Reference==_careSlime);
            var ahead=Vector3.ProjectOnPlane(Camera.main.transform.forward,Vector3.up).normalized;
            var restingRoot=_app.Actors.SpawnAtForTest(slime,Controller.Motor.TransientPosition+ahead*2.4f+Vector3.up*.5f);
            _careFixture=restingRoot.GetComponent<SlimeEat>();
            Require(_careFixture!=null,"Separate full/sleep fixture has no native SlimeEat.");
            var token=restingRoot.GetComponent<IdentifiableActor>().Pointer;
            var favorite = _app.Actors.Catalog.Single(e => e.Reference == _careFavorite);
            _app.Actors.FillAmmoForTest(favorite, _careSlot);
            Require(ammo.GetSlotName(_careSlot) == favorite.Type, "Favorite fixture reset failed.");
            ammo.Slots[_careSlot].Count = 3;
            _careFixture._emotions.Set(SlimeEmotions.Emotion.HUNGER, 0);
            _careFixture._emotions.SetAsleep(false); _app.Care.ScanNow();
            var target = _app.Care.Hits.Single(h => h.ActorToken == token);
            Require(!target.Hungry, "Native full fixture was reported hungry.");
            Require(!_app.Care.CanOffer(_careFixture,favorite.Type)&&ammo.GetSlotCount(_careSlot)==3,
                "Production full/sleeping eligibility accepted the full fixture or spent a portion.");
            _careFixture._emotions.Set(SlimeEmotions.Emotion.HUNGER,1);
            _careFixture._emotions.SetAsleep(true);_app.Care.ScanNow();
            Require(_careFixture.IsSleeping()&&!_app.Care.CanOffer(_careFixture,favorite.Type)&&ammo.GetSlotCount(_careSlot)==3,
                "Production full/sleeping eligibility accepted the genuinely hungry sleeping fixture.");
            // A zero budget is checked independently; neighboring test-copy
            // slimes can legitimately consume the same favorite.
            ammo.Slots[_careSlot].Count = 0;
            Require(_app.Care.FeedNow() == 0 && ammo.GetSlotCount(_careSlot) == 0,
                "Sleeping/full or empty fixture produced free food.");
            _careFixture._emotions.SetAsleep(false);
        });
        Add("Care / cleanup retires value snapshots and native fixtures", () =>
        {
            _app.Care.Reset(); _app.Actors.Cleanup(); _careFixture = null;
            Require(!_app.Care.Inspect && _app.Care.Hits.Count == 0 && _app.Care.NativeActorRoots == 0,
                "Care reset retained old-world targets.");
        }, .3f);
        Add("Gadgets / native catalog excludes locked and secondary definitions", () =>
        {
            GuardCopy(); _app.Gadgets.RefreshCatalog();
            foreach (var entry in _app.Gadgets.Catalog)
            {
                var definition = entry.Definition;
                Require(definition != null && !definition.IsSecondary && definition.prefab != null &&
                    (_app.Context.GadgetDirector.HasBlueprint(definition.BaseGadget ?? definition) ||
                     _app.Context.GadgetDirector.HasBlueprint(definition)), "Catalog exposed a locked/secondary gadget.");
            }
            Require(_app.Gadgets.PreviewRoots == 0, "Catalog instantiated a preview.");
        });
        Add("Gadgets / owned crafted decoration fixture", () =>
        {
            GuardCopy();
            var director = _app.Context.GadgetDirector;
            var definition = NativeEnumerable.Items(director.GetAllGadgets()).Select(t => t.TryCast<GadgetDefinition>())
                .First(d => d != null && d.Type == GadgetDefinition.Types.DEFAULT && !d.IsSecondary && !d.IsVariant &&
                    d.prefab != null && director.IsDecor(d) && d.prefab.GetComponent<Gadget>() != null &&
                    d.prefab.GetComponent<Gadget>().GetGadgetRadius() <= 1.5f);
            if (!director.HasBlueprint(definition)) Require(director.AddBlueprint(definition, false), "Owned blueprint fixture setup failed.");
            // This inventory seeding is test-copy-only. Production placement
            // cannot unlock blueprints, craft, or add inventory.
            director._model.SetCount(definition.BaseGadget ?? definition, 3);
            _app.Gadgets.RefreshCatalog();
            _gadgetFixture = _app.Gadgets.Catalog.Single(e => e.Reference == definition.ReferenceId);
            _app.UiForTests.ShowCreationForTest(2); _app.Input.Open();
        }, .3f);
        Add("Gadgets / native catalog capture", () => CaptureCare("gadget-catalog"), .3f);
        Add("Gadgets / owned native camera targets verified ground",()=>{
            GuardCopy();_app.CloseMenu();
            Vector3 point=FindGadgetFixtureGround();
            var camera=Controller._cameraController;
            Require(camera!=null&&Camera.main!=null,"Native camera controller unavailable.");
            _gadgetCameraBefore=camera.Rotation;_gadgetCameraAimed=true;_gadgetCameraSession=_app.Session;
            _gadgetAimPoint=point;_gadgetAimRetries=0;_gadgetAimNext=Time.unscaledTime+.15f;
            // The native item validates the player's real aim, not merely a
            // downward fallback probe. Aim this owned test copy through the
            // actual camera API; production never moves the user's camera.
            camera.SetRotation(Quaternion.LookRotation(point-Camera.main.transform.position,Vector3.up));
        },.3f);
        Add("Gadgets / verified native static ground", () =>
        {
            _app.CloseMenu();
            Require(_app.Gadgets.TryGround(out var ray, out var hit, out var rotation, out var reason),
                "No placement ground: " + reason+"; "+_gadgetAimReason);
            Require(hit.collider != null && hit.collider.attachedRigidbody == null && Vector3.Dot(rotation * Vector3.up, hit.normal) > .999f,
                "Gadget rotation does not follow the verified surface normal.");
            Require(Vector3.Angle(Camera.main.transform.forward,ray.direction)<.1f,
                "The fixture is not actually targeted by the native camera.");
        },.1f,AimGadgetFixtureReady,12f);
        Add("Gadgets / request requires confirmation before inventory changes", () =>
        {
            var director = _app.Context.GadgetDirector;
            _gadgetInventoryBefore = director.GetItemCount(_gadgetFixture.Definition.BaseGadget ?? _gadgetFixture.Definition);
            _gadgetSelectedBefore=director._model.SelectedSlotIndex.Value;
            _gadgetLockedBefore=director._model.LockedSlot.Value?.Pointer??IntPtr.Zero;
            _gadgetModelsBefore = _app.Context.GameModel.AllGadgets().Count;
            _app.Gadgets.RequestPlace(_gadgetFixture);
            Require(_app.Pending != null && _app.Context.GameModel.AllGadgets().Count == _gadgetModelsBefore &&
                director.GetItemCount(_gadgetFixture.Definition.BaseGadget ?? _gadgetFixture.Definition) == _gadgetInventoryBefore,
                "Unapproved placement changed native inventory/models.");
            _app.UiForTests.ShowCreationForTest(2); _app.Input.Open();
        }, .3f);
        Add("Gadgets / native placement confirmation capture", () => CaptureCare("gadget-confirmation"), .3f);
        Add("Gadgets / cancel does not place or spend", () =>
        {
            _app.CloseMenu();
            Require(_app.Pending == null && _app.Context.GameModel.AllGadgets().Count == _gadgetModelsBefore &&
                _app.Context.GadgetDirector.GetItemCount(_gadgetFixture.Definition.BaseGadget ?? _gadgetFixture.Definition) == _gadgetInventoryBefore,
                "Cancelled placement spent/registered a gadget.");
        });
        Add("Gadgets / confirmed request waits for native item initialization", () =>
        {
            GuardCopy();
            var previous = _app.Context.GameModel.AllGadgets();
            _gadgetTokensBefore = new System.Collections.Generic.HashSet<IntPtr>();
            for (int i = 0; i < previous.Count; i++) _gadgetTokensBefore.Add(previous[i].Pointer);
            Require(_app.Gadgets.PlaceNow(_gadgetFixture), "Native placement rejected its fixture: " + _app.Gadgets.Status);
            Require(_app.Gadgets.PlacementPending&&_app.Context.GameModel.AllGadgets().Count==_gadgetModelsBefore,
                "Native event was invoked before the newly enabled item initialized.");
        },.1f);
        Add("Gadgets / single native event creates registered GadgetModel", () =>
        {
            GuardCopy();
            Require(_app.Gadgets.LastPlacementSucceeded&&!_app.Gadgets.PlacementPending,
                "Native placement did not finish: "+_app.Gadgets.Status);
            var after = _app.Context.GameModel.AllGadgets();
            Require(after.Count == _gadgetModelsBefore + 1, "Single event created wrong number of GadgetModels.");
            for (int i = 0; i < after.Count; i++)
                if (!_gadgetTokensBefore.Contains(after[i].Pointer)) _placedGadgetFixture = after[i].GetGameObject()?.GetComponent<Gadget>();
            Require(_placedGadgetFixture != null && _placedGadgetFixture.GetModel() != null &&
                _app.Context.GadgetDirector.GetItemCount(_gadgetFixture.Definition.BaseGadget ?? _gadgetFixture.Definition) == _gadgetInventoryBefore - 1,
                "Native placement did not conserve one crafted inventory unit.");
            Require(Vector3.Distance(_placedGadgetFixture.GetModel().GetPos(),_app.Gadgets.LastPlacementPoint)<=.25f&&
                Vector3.Dot(_placedGadgetFixture.GetModel().GetRot()*Vector3.up,_app.Gadgets.LastPlacementNormal)>.99f,
                "Registered native gadget does not match its confirmed solid-ground point and normal.");
            Require(_app.Gadgets.PreviewRoots == 0 &&
                _app.Gadgets.NativeItem._gadgetPlaceholderInstance == null&&
                !_app.Context.PlayerState.GadgetModeActive.Value,
                "Native temporary preview remained after placement.");
            Require(_app.Context.GadgetDirector._model.SelectedSlotIndex.Value==_gadgetSelectedBefore&&
                (_app.Context.GadgetDirector._model.LockedSlot.Value?.Pointer??IntPtr.Zero)==_gadgetLockedBefore,
                "Temporary native gadget selection was not restored exactly.");
        }, .8f,()=>!_app.Gadgets.PlacementPending,3f);
        Add("Gadgets / placed decoration native capture", () => CaptureCare("gadget-placed"), .3f);
        Add("Gadgets / empty crafted stock is refused", () =>
        {
            GuardCopy();
            var basis = _gadgetFixture.Definition.BaseGadget ?? _gadgetFixture.Definition;
            var director = _app.Context.GadgetDirector;
            int previous = director.GetItemCount(basis), models = _app.Context.GameModel.AllGadgets().Count;
            director._model.SetCount(basis, 0);
            bool refused = false;
            try { _app.Gadgets.PlaceNow(_gadgetFixture); } catch (InvalidOperationException) { refused = true; }
            Require(refused && director.GetItemCount(basis) == 0 && _app.Context.GameModel.AllGadgets().Count == models,
                "Empty stock produced a gadget.");
            director._model.SetCount(basis, previous);
        });
        Add("Gadgets / native storage returns placed inventory unit", () =>
        {
            GuardCopy(); Require(_placedGadgetFixture != null, "Placed fixture missing before store.");
            _app.Gadgets.NativeItem.StoreTargettedGadget(_placedGadgetFixture);
        }, .5f);
        Add("Gadgets / stored fixture and catalog roots retired", () =>
        {
            Require(_app.Context.GameModel.AllGadgets().Count == _gadgetModelsBefore &&
                _app.Context.GadgetDirector.GetItemCount(_gadgetFixture.Definition.BaseGadget ?? _gadgetFixture.Definition) == _gadgetInventoryBefore,
                "Native storage did not return the crafted unit/remove its model.");
            _app.Gadgets.Reset(); RetireCareFixtures(); _app.UiForTests.ShowCreationForTest(0);
            Require(_app.Gadgets.Catalog.Count == 0 && _app.Gadgets.PreviewRoots == 0,
                "Gadget reset retained old-world catalog/preview.");
        });
    }
}