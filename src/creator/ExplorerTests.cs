using System;
using System.IO;
using System.Linq;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using UnityEngine;
using UObj=UnityEngine.Object;

namespace CreatorToolkit;

internal sealed partial class FeatureSuite
{
    private string _customFixtureId="";
    private MapTools.Point _explorerOrigin;
    private LandPlot _farmFixturePlot;
    private PlortCollector _farmFixtureCollector;
    private SlimeFeeder _farmFixtureFeeder;
    private int _collectorBefore;
    private string _fixturePlort;
    private string _radarFixtureSlime="",_radarFixtureFavorite="";

    private void CaptureExplorer(string name)=>ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"v050-"+name+".png"));
    private static int StorageCount(SiloStorage storage)
    {
        var ammo=storage?.GetRelevantAmmo();int n=0;
        for(int i=0;ammo?.Slots!=null&&i<ammo.Slots.Length;i++)n+=ammo.GetSlotCount(i);
        return n;
    }
    private Vector3 CollectorFixturePosition(Collider area)
    {
        Require(area!=null&&area.enabled&&area.gameObject.activeInHierarchy,
            "Native collector collision area is not active.");
        Physics.SyncTransforms();
        var bounds=area.bounds;
        Require(ExplorerPolicy.Coordinates(bounds.center.x,bounds.center.y,bounds.center.z)&&
            bounds.size.x>.1f&&bounds.size.y>.1f&&bounds.size.z>.1f&&
            bounds.size.x<100&&bounds.size.y<100&&bounds.size.z<100,
            "Native collection trigger has invalid or unbounded dimensions.");
        _app.LoggerInstance.Msg("Farm native collection area="+area.name+
            " center="+bounds.center.ToString("F3")+" size="+bounds.size.ToString("F3"));
        // A 2D map's ray from y=900 deliberately selects the highest safe
        // player surface. It is not an appropriate locator for a plort inside
        // a corral: the Conservatory roof can be above the collection trigger.
        // Start INSIDE the actual trigger and use a short native terrain ray.
        // The 0.6 m floor margin permits ground just below the trigger bottom;
        // the actual spawned position must still be contained by the trigger.
        var offsets=new[]{Vector2.zero,new Vector2(-.2f,0),new Vector2(.2f,0),
            new Vector2(0,-.2f),new Vector2(0,.2f),new Vector2(-.2f,-.2f),
            new Vector2(.2f,-.2f),new Vector2(-.2f,.2f),new Vector2(.2f,.2f)};
        foreach(var offset in offsets)
        {
            var origin=new Vector3(bounds.center.x+bounds.size.x*offset.x,
                bounds.max.y-.02f,bounds.center.z+bounds.size.z*offset.y);
            var hits=Physics.RaycastAll(origin,Vector3.down,bounds.size.y+.6f,
                Controller.Motor.CollidableLayers,QueryTriggerInteraction.Ignore);
            float lowest=float.MaxValue;Vector3 spawn=default;string surface="";
            for(int i=0;i<hits.Length;i++)
            {
                var hit=hits[i];var collider=hit.collider;
                if(i<12)_app.LoggerInstance.Msg("Farm trigger ray hit="+collider?.name+
                    " point="+hit.point.ToString("F3")+" normalY="+hit.normal.y.ToString("F3"));
                if(collider==null||hit.normal.y<.65f||hit.point.y>=lowest||
                    collider.transform.IsChildOf(Controller.transform)||
                    collider.GetComponentInParent<IdentifiableActor>()!=null)continue;
                string name=(collider.name+" "+collider.transform.parent?.name).ToLowerInvariant();
                if(name.Contains("water")||name.Contains("slimesea")||name.Contains("lava"))continue;
                var candidate=hit.point+Vector3.up*.6f;
                if(!bounds.Contains(candidate)||!ExplorerPolicy.Coordinates(candidate.x,candidate.y,candidate.z))continue;
                lowest=hit.point.y;spawn=candidate;surface=collider.name;
            }
            if(lowest<float.MaxValue)
            {
                _app.LoggerInstance.Msg("Farm registered plort ground="+surface+" spawn="+spawn.ToString("F3"));
                return spawn;
            }
        }
        throw new InvalidOperationException("No native ground inside the actual collection trigger; see bounded ray telemetry.");
    }
    private void RetireExplorerFixtures()
    {
        _explorerOrigin=null;_farmFixturePlot=null;_farmFixtureCollector=null;_farmFixtureFeeder=null;_fixturePlort="";
        _radarFixtureSlime=_radarFixtureFavorite="";
    }
    private void BuildExplorerSteps()
    {
        Add("Explorer / explicit opt-in defaults",()=>{
            Require(!_app.Custom.ShowCompass&&!_app.Map.CustomPointMode&&!_app.Radar.Enabled&&!_app.Farm.Enabled,
                "A new navigation or farm feature enabled without opt-in.");
            Require(_app.Farm.CollectorCount==0&&_app.Farm.FeederCount==0&&_app.Radar.TargetCount==0,
                "Disabled new modules retained world-device/actor roots.");
            var player=_app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true);
            _explorerOrigin=new MapTools.Point{Group=player.SceneGroup,GroupName=player.SceneGroup.name,
                Position=Controller.Motor.TransientPosition,ExactHeight=true,AllowNearbyLanding=true,Name="Explorer origin"};
        });
        Add("CustomWaypoints / native grounded creation",()=>{
            GuardCopy();Require(_app.Custom.Records.Count==0,"New owned save unexpectedly already has user markers.");
            _app.Custom.DraftName="Радужный привал";_app.Custom.DraftColor=3;_app.Custom.SaveCurrent();
            var point=_app.Custom.Records.Single();_customFixtureId=point.Id;
            Require(point.Name=="Радужный привал"&&point.Color==3&&point.ExactHeight,"Custom marker lost its label/palette/native height.");
            Require(_app.Map.Points.Any(p=>p.Category==CustomWaypointTools.Category&&p.Id==point.Id&&p.Group!=null),"Saved custom marker did not enter the native map index.");
            _app.Custom.SetGoal(point.Id);_app.CloseMenu();
        },.4f);
        Add("CustomWaypoints / compass native world capture",()=>{
            Require(_app.Custom.ShowCompass&&_app.Custom.Heading.Contains("Радужный привал"),"Native waypoint heading was not refreshed.");
            CaptureExplorer("waypoint-compass");
        },.25f);
        Add("CustomWaypoints / editor opening after world capture",()=>{
            _app.UiForTests.ShowExplorerForTest(1,_customFixtureId);_app.Input.Open();
        },.25f);
        Add("CustomWaypoints / editor capture",()=>CaptureExplorer("waypoint-editor"),.2f);
        Add("CustomWaypoints / atomic rename color and readback",()=>{
            _app.CloseMenu();_app.Custom.Rename(_customFixtureId,"Мятная тропа",1);_app.Custom.ReloadForTest();
            var record=_app.Custom.Find(_customFixtureId);
            Require(record?.Name=="Мятная тропа"&&record.Color==1,"Atomic marker file did not round-trip label and color.");
            bool rejected=false;try{_app.Custom.Rename(_customFixtureId,new string('x',49),1);}catch(InvalidOperationException){rejected=true;}
            Require(rejected&&_app.Custom.Find(_customFixtureId).Name=="Мятная тропа","Rejected marker name changed the stored label.");
            _app.Custom.HideGoal();
            _app.Map.RequestTeleport(_app.Custom.FindPoint(_customFixtureId));
        },.2f);
        Add("CustomWaypoints / safe teleport same collision gate",()=>{
            var record=_app.Custom.Find(_customFixtureId);var actual=Controller.Motor.TransientPosition;
            Require(_app.Map.LastTransitSucceeded&&!_app.Player.TransitHeld&&Controller.Motor.enabled&&
                _app.Map.LastSceneAssetsReady&&_app.Map.LastStableGroundSamples>=4,"Custom teleport bypassed scene/collision readiness.");
            Require(SafetyPolicy.NearbyReturnOffset(actual.x-record.X,actual.y-record.Y,actual.z-record.Z)&&_app.Player.IsClearPosition(actual),
                "Custom teleport did not land inside its collision-safe tolerance.");
            _app.Map.OpenAt(_app.Custom.FindPoint(_customFixtureId));
        },.5f);
        Add("CustomWaypoints / colored native map popover",()=>{
            Require(_app.Map.Selected?.Id==_customFixtureId&&_app.Map.OverlayCardRects.Count==1,"Custom marker popover has no click-through shield.");
            CaptureExplorer("waypoint-map");
        },.25f,()=>_app.Map.NativeMapOpen&&_app.Map.Ui._map!=null&&_app.Map.Ui._viewModel!=null&&
            _app.Map.Selected?.Id==_customFixtureId&&_app.Map.OverlayCardRects.Count==1);
        Add("CustomWaypoints / map closes after native card capture",()=>_app.Map.CloseNative(),.25f);
        Add("Radar / native favorite diet and fixtures",()=>{
            GuardCopy();_app.CloseMenu();_app.Map.CloseNative();
            var slime=_app.Actors.Catalog.First(e=>e.Category=="Слаймы"&&!e.IsPrototype&&
                e.Type.TryCast<SlimeDefinition>()?.Diet?.FavoriteIdents?.Length>0&&
                _app.Actors.Catalog.Any(f=>f.CanSpawn&&f.Category=="Еда"&&e.Type.TryCast<SlimeDefinition>().Diet.FavoriteIdents.Contains(f.Type)));
            _app.Radar.SetSlime(slime);
            _radarFixtureSlime=slime.Type.ReferenceId;
            var food=_app.Actors.Catalog.First(e=>e.CanSpawn&&e.Category=="Еда"&&slime.Type.TryCast<SlimeDefinition>().Diet.FavoriteIdents.Contains(e.Type));
            var rare=_app.Actors.Catalog.First(e=>e.CanSpawn&&ExplorerPolicy.RareMaterial(e.Type.name));
            _radarFixtureFavorite=food.Name;
            _app.Actors.Spawn(food,1,-1.2f,4.5f);_app.Actors.Spawn(rare,1,1.2f,4.5f);
            _app.Radar.SetModes(true,true);
            Require(_app.Radar.FavoriteNames.Contains(food.Name),"Native preferred-food name is not shown.");
        },.8f);
        Add("Radar / current-zone favorites and rare native actors",()=>{
            _app.Radar.ScanNow();
            var zone=_app.Context.PlayerZoneTracker.GetCurrentZone();
            Require(zone!=null&&_app.Radar.Hits.Any(h=>h.Food)&&_app.Radar.Hits.Any(h=>!h.Food),
                "Directional radar missed its actual favorite-food/rare-resource fixtures.");
            foreach(var hit in _app.Radar.Hits)
            {
                Require(ExplorerPolicy.Within((hit.Position-Controller.Motor.TransientPosition).sqrMagnitude,100),"Radar escaped its radius.");
                Require(UObj.FindObjectsOfType<IdentifiableActor>().Any(a=>a!=null&&a.identType?.ReferenceId==hit.Reference&&
                    a.RegionMember!=null&&a.RegionMember.IsInZone(zone)&&Vector3.Distance(a.transform.position,hit.Position)<.2f),
                    "Radar displayed an actor outside the native current zone or retained a stale actor.");
            }
            Require(_app.Radar.TargetCount<=32,"Radar exceeded the bounded target budget.");
            Require(!_app.Map.NativeMapOpen&&!_app.MenuOpen&&Time.timeScale>0,"Radar world proof was obscured by a native/menu display.");
            CaptureExplorer("radar-world");
        },.25f);
        Add("Radar / indexed native definitions and loaded actor discovery",()=>{
            GuardCopy();
            Require(_app.Radar.QueryDefinitionCount>0&&_app.Radar.QueryDefinitionCount<=_app.Actors.Catalog.Count,
                "Radar definition cache is missing or exceeds the actual catalog.");
            Require(_app.Radar.ExaminedModelCount>0&&_app.Context.GameModel.identifiablesByIdent!=null,
                "Radar did not exercise the authoritative per-definition native index.");
        });
        Add("Radar / settings opening after world capture",()=>{_app.UiForTests.ShowExplorerForTest(2);_app.Input.Open();},.25f);
        Add("Radar / canonical slime selection capture",()=>{
            Require(_app.Radar.SlimeReference==_radarFixtureSlime&&_app.Radar.FavoriteNames.Contains(_radarFixtureFavorite),
                "Opening radar settings replaced the chosen native slime or its favorite diet.");
            CaptureExplorer("radar-settings");
        },.25f);
        Add("Radar / separate filters and zero roots when off",()=>{
            _app.CloseMenu();_app.Radar.SetModes(false,true);_app.Radar.ScanNow();
            Require(_app.Radar.Hits.All(h=>!h.Food),"Food filter remained enabled.");
            _app.Radar.SetModes(false,false);
            Require(_app.Radar.TargetCount==0&&_app.Radar.QueryDefinitionCount==0&&_app.Radar.ExaminedModelCount==0&&!_app.Radar.Enabled,
                "Disabled radar retains targets or native definition query roots.");
            _app.Actors.Cleanup();
        });
        Add("Farm / actual native ranch sector preload",()=>{
            GuardCopy();_app.CloseMenu();_app.Map.CloseNative();_app.World.SetFreezeTime(true);
            var group=Resources.FindObjectsOfTypeAll<SceneGroup>().First(g=>g!=null&&g.IsGameplay&&g.name=="ConservatoryFields");
            // The previous actor matrix ends in fields, where streaming has
            // unloaded ranch plots. Use the real native spawn, not guessed
            // plot coordinates or a raw model edit, to load its ranch sector.
            _app.Map.RequestTeleport(new MapTools.Point{Group=group,GroupName=group.name,
                Position=group.InitialPlayerPosition,ExactHeight=false,AllowNearbyLanding=true,Name="Native ranch preload"});
        },.5f);
        Add("Farm / native owned corral purchase fixture",()=>{
            GuardCopy();Require(_app.Map.LastTransitSucceeded&&_app.Map.LastSceneAssetsReady,"Native ranch sector did not safely load.");
            var plots=UObj.FindObjectsOfType<LandPlot>();
            _app.LoggerInstance.Msg("Farm native fixture loaded plots="+plots.Length);
            var plot=plots.Where(p=>p!=null&&(p.GetPlotId()==LandPlot.Id.EMPTY||p.GetPlotId()==LandPlot.Id.NONE)&&
                p.GetComponentInParent<LandPlotLocation>()!=null).OrderBy(p=>(p.transform.position-Controller.Motor.TransientPosition).sqrMagnitude).First();
            var prefab=LookupDirector.GetIfReady()?.GetPlotPrefab(LandPlot.Id.CORRAL);
            Require(prefab!=null,"Actual native corral purchase prefab is unavailable.");
            var go=plot.GetComponentInParent<LandPlotLocation>().Replace(plot,prefab);
            _farmFixturePlot=go?.GetComponent<LandPlot>()??go?.GetComponentInChildren<LandPlot>(true);
            Require(_farmFixturePlot?.GetPlotId()==LandPlot.Id.CORRAL&&_farmFixturePlot._model!=null,"Native empty-plot replacement did not register a corral model.");
            _farmFixturePlot.AddUpgrade(LandPlot.Upgrade.FEEDER);
            _farmFixturePlot.AddUpgrade(LandPlot.Upgrade.PLORT_COLLECTOR);
        },1.2f,()=>UObj.FindObjectsOfType<LandPlot>().Any(p=>p!=null&&
            (p.GetPlotId()==LandPlot.Id.EMPTY||p.GetPlotId()==LandPlot.Id.NONE)&&p.GetComponentInParent<LandPlotLocation>()!=null));
        Add("Farm / installed feeder collector and safe approach",()=>{
            Require(_farmFixturePlot!=null,"Owned corral fixture missing.");
            _farmFixtureCollector=_farmFixturePlot.GetComponentInChildren<PlortCollector>(true);
            _farmFixtureFeeder=_farmFixturePlot.GetComponentInChildren<SlimeFeeder>(true);
            Require(_farmFixtureCollector?._storage!=null&&_farmFixtureFeeder?._storage!=null,
                "Native corral upgrades did not instantiate authoritative devices.");
            var group=_farmFixturePlot._region?.SceneGroup;
            Require(group!=null,"Native corral region has no scene group.");
            _app.Map.RequestTeleport(new MapTools.Point{Group=group,GroupName=group.name,
                Position=_farmFixturePlot.transform.position+new Vector3(11,0,0),Name="Native corral fixture",ExactHeight=false});
        },.25f);
        Add("Farm / confirmation is cancellable and no free food",()=>{
            Require(_app.Map.LastTransitSucceeded,"Safe approach to the actual corral failed.");
            _app.UiForTests.ShowExplorerForTest(3);_app.Input.Open();_app.Farm.Request(true,true);
            Require(_app.Pending!=null&&!_app.Farm.Enabled,"Farm mutations began before confirmation.");
            CaptureExplorer("farm-confirmation");
        },.25f);
        Add("Farm / native confirmation cancellation after capture",()=>{
            _app.CloseMenu();
            Require(!_app.Farm.Enabled&&_app.Pending==null,"Farm cancellation left automation armed.");
        },.25f);
        Add("Farm / indexed loaded native corral device discovery",()=>{
            GuardCopy();_app.Farm.ScanNow();
            Require(_app.Farm.ScannedPlotCount>0&&_app.Farm.CollectorCount>0&&_app.Farm.FeederCount>0,
                "Native plot index missed installed loaded corral devices.");
            _app.Farm.SetModes(false,false);
            Require(_app.Farm.ScannedPlotCount==0&&_app.Farm.CollectorCount==0&&_app.Farm.FeederCount==0,
                "Disabled farm retains discovery state or native device roots.");
        });
        Add("Farm / exact one-unit native food conservation",()=>{
            GuardCopy();var food=_app.Actors.Catalog.First(e=>e.Category=="Еда"&&e.Family=="Фрукты"&&e.CanSpawn);
            var ammo=_app.Context.PlayerState.Ammo;int slot=ammo._selectedAmmoIdx;
            _app.Actors.FillAmmoForTest(food,slot);_app.Farm.SetModes(false,true);_app.Farm.ScanNow();
            int source=ammo.GetSlotCount(slot),destination=StorageCount(_farmFixtureFeeder._storage);
            Require(_app.Farm.TransferOne(_farmFixtureFeeder),"Native feeder refused a nearby compatible food portion.");
            Require(ExplorerPolicy.Conserved(source,ammo.GetSlotCount(slot),destination,StorageCount(_farmFixtureFeeder._storage)),
                "Feeder transfer created or lost food.");
            _app.Farm.SetModes(false,false);
        });
        Add("Farm / bounded native four-unit work budget",()=>{
            _app.Farm.SetModes(false,true);_app.Farm.ScanNow();
            var ammo=_app.Context.PlayerState.Ammo;int slot=ammo._selectedAmmoIdx,before=ammo.GetSlotCount(slot);
            _app.Farm.WorkNow();
            Require(before-ammo.GetSlotCount(slot)>0&&before-ammo.GetSlotCount(slot)<=4,"Feeder work did not respect the four-unit budget.");
            _app.Farm.SetModes(false,false);
        });
        Add("Farm / full native feeder rejects without source loss",()=>{
            var storage=_farmFixtureFeeder._storage;var ammo=storage.GetRelevantAmmo();
            var source=_app.Context.PlayerState.Ammo;var food=source.GetSlotName(source._selectedAmmoIdx);
            for(int i=0;i<ammo.Slots.Length;i++)
            {
                int maximum=storage.GetSlotMaxCount(i),count=storage.GetSlotCount(i);
                if(maximum>count)Require(storage.MaybeAddAsResource(food,i,maximum-count,false),"Native full-feeder fixture could not fill to the real capacity.");
            }
            _app.Farm.SetModes(false,true);int before=source.GetSlotCount(source._selectedAmmoIdx),stored=StorageCount(storage);
            Require(!_app.Farm.TransferOne(_farmFixtureFeeder)&&source.GetSlotCount(source._selectedAmmoIdx)==before&&StorageCount(storage)==stored,
                "Full feeder consumed, duplicated or overflowed a source portion.");
            _app.Farm.SetModes(false,false);
        });
        Add("Farm / native collector plort fixture",()=>{
            GuardCopy();
            var plort=_app.Actors.Catalog.First(e=>e.Category=="Плорты"&&e.CanSpawn);
            _fixturePlort=plort.Type.ReferenceId;_collectorBefore=StorageCount(_farmFixtureCollector._storage);
            var area=_farmFixtureCollector.CollectionArea.GetComponent<Collider>();
            var spawn=CollectorFixturePosition(area);
            Require(area.bounds.Contains(spawn),"Confirmed ground lies outside the native collector's actual trigger.");
            var actor=_app.Actors.SpawnAtForTest(plort,spawn);
            Require(actor!=null&&area.bounds.Contains(actor.transform.position),
                "Registered native plort fixture did not appear inside the collector trigger.");
        },1f);
        Add("Farm / accepted native collection request",()=>{
            _app.UiForTests.ShowExplorerForTest(3);_app.Input.Open();_app.Farm.Request(true,false);
            Require(_app.Pending!=null&&!_app.Farm.Collect,"Collector enabled without its explicit opt-in.");
            _app.AcceptConfirmation();_app.CloseMenu();
        },8f);
        Add("Farm / real collector delivers registered plort to storage",()=>{
            Require(_app.Farm.Collect&&_app.Farm.CollectionRequests>0,"Native corral collector was not invoked.");
            var storage=_farmFixtureCollector._storage;var ammo=storage.GetRelevantAmmo();
            Require(StorageCount(storage)>=_collectorBefore+1&&Enumerable.Range(0,ammo.Slots.Length).Any(i=>
                ammo.GetSlotCount(i)>0&&ammo.GetSlotName(i)?.ReferenceId==_fixturePlort),
                "Native collector did not deliver the actual registered plort to its own storage.");
            _app.UiForTests.ShowExplorerForTest(3);_app.Input.Open();
        },.3f);
        Add("Farm / native settings proof capture",()=>{
            Require(_app.UiForTests.ExplorerSectionVisibleForTest(3),"Farm proof requires its actual visible settings panel.");
            CaptureExplorer("farm-settings");
        },.25f,()=>_app.UiForTests.ExplorerSectionVisibleForTest(3));
        Add("Farm / off releases devices and return remains safe",()=>{
            _app.CloseMenu();_app.Farm.SetModes(false,false);
            Require(_app.Farm.CollectorCount==0&&_app.Farm.FeederCount==0&&!_app.Farm.Enabled,"Disabled farm tool retained native world devices.");
            Require(!_app.Farm.TransferOne(_farmFixtureFeeder),"Disabled feeder mode still mutates inventory.");
            _app.Actors.Cleanup();_app.World.Reset();_app.Map.RequestTeleport(_explorerOrigin);
        },.25f);
        Add("Explorer / returned original region and no extra textures",()=>{
            Require(_app.Map.LastTransitSucceeded&&!_app.Player.TransitHeld&&_app.Player.IsClearPosition(Controller.Motor.TransientPosition),
                "New feature fixture did not safely return to the original region.");
            Require(_app.UiOwnedTextures==ToolkitUi.ThemeTextureBudget,"Explorer panels allocated per-feature textures instead of reusing the shared jelly theme.");
            _app.Custom.HideGoal();_app.Radar.Reset();_app.Farm.Reset();RetireExplorerFixtures();
        });
    }
}