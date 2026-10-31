using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Map;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using AtlasUi = Il2CppMonomiPark.SlimeRancher.UI.Map.MapUI;

namespace CreatorToolkit;

// Native map stays authoritative for projection, zoom, panning and page switching.
// The baked read-only index is gated by the installed Addressables catalog hash.
internal sealed partial class MapTools
{
    internal sealed class Point
    {
        internal string Id, Name, Category, GroupName, Detail, Contents="";
        internal string SourceScene,GordoStateText,GordoDisplayName,WikiHint,StateKey,FilterKind;
        internal string ZoneSource; // M29: how a phantom route got its zone (M29GhostZonePolicy)
        internal int GordoTarget,GordoRank,GordoEaten,GordoTargetDisplay;
        internal GordoMapPolicy.State GordoState;
        internal Vector3 Position;
        internal SceneGroup Group;
        internal bool ExactHeight,AllowNearbyLanding,SourceObservation,LiveSource,DroneObservation,GordoUnvisited;
        internal MapDefinition SourceMap;
        internal Sprite SourceIcon;
        internal float ObservedAt;
        internal int ColorIndex;
        internal string Chip; // AtlasGroupPolicy chip, resolved once per point
        internal string Search => (Name+" "+Id+" "+GroupName+" "+Detail+" "+Contents+" "+WikiHint).ToLowerInvariant();
    }
    // Food and plort spawns were split out of «Ресурсы» (MapResourceLayerPolicy);
    // appended so the indices of the original twelve layers stay stable.
    internal static readonly string[] Categories={"Капсулы","Гордо","Телепорты","Ноды карты","Дроны","Ресурсы","Двери","Механизмы","Горшочки","Спавнеры",FandomMapMarkers.GatherCategory,FandomMapMarkers.RareCategory,MapResourceLayerPolicy.Food,MapResourceLayerPolicy.Plorts};
    internal readonly HashSet<string> Visible = new(Categories.Take(5));
    // 0.8.1: find kinds switched off inside a visible shared layer (AtlasGroupPolicy chips).
    internal readonly HashSet<string> HiddenChips = new(StringComparer.Ordinal);
    internal static string ChipOf(Point p)=>p==null?"":p.Chip??=AtlasGroupPolicy.ChipOf(p.Category,p.FilterKind,
        p.Category==AtlasGroupPolicy.Drones&&(p.DroneObservation||NativeIconPolicy.GhostDrone(p.Id,p.Name)));
    // The one visibility rule of the atlas list, the native map, the minimap and the compass.
    internal bool LayerVisible(Point p)=>p!=null&&AtlasGroupPolicy.Visible(p.Category,ChipOf(p),Visible,HiddenChips);
    internal void ToggleChip(AtlasGroupPolicy.Chip chip){AtlasGroupPolicy.Toggle(chip,Visible,HiddenChips);_filterResult=null;}
    internal void SetLayer(string layer,bool on){AtlasGroupPolicy.SetLayer(layer,on,Visible,HiddenChips);_filterResult=null;}
    internal bool ChipActive(AtlasGroupPolicy.Chip chip)=>AtlasGroupPolicy.Active(chip,Visible,HiddenChips);
    // Native map «Слои карты» panel: whole groups and one-click presets over the same chips.
    internal void SetGroup(AtlasGroupPolicy.Group group,bool on){AtlasGroupPolicy.SetGroup(group,on,Visible,HiddenChips);_filterResult=null;}
    internal int GroupActive(AtlasGroupPolicy.Group group)=>AtlasGroupPolicy.ActiveCount(group,Visible,HiddenChips);
    internal void ApplyPreset(int index)
    {
        if(index<0||index>=AtlasGroupPolicy.Presets.Length)return;
        AtlasGroupPolicy.ApplyPreset(AtlasGroupPolicy.Presets[index],Visible,HiddenChips);_filterResult=null;
    }
    internal int MatchingPreset()=>AtlasGroupPolicy.MatchingPreset(Visible,HiddenChips);
    // 0.8.1: no icons under unexplored fog of the open atlas page (MapFogMask); own markers and
    // the selected object stay. FogHiddenOnPage feeds the legend under the toolbar.
    internal readonly MapFogMask Fog=new();
    internal bool HideFogged=true;
    internal int FogHiddenOnPage {get;private set;}
    // Whole-world chip totals for the atlas chips («0/19»): every two seconds or on a save-state change.
    private readonly Dictionary<string,(int Total,int Done)> _chipCounts=new(StringComparer.Ordinal);
    private float _nextChipCounts;private int _chipCountVersion=-1,_chipCountPoints=-1;
    internal (int Total,int Done) ChipCount(string chip)
    {
        if(Time.unscaledTime>=_nextChipCounts||_chipCountVersion!=_stateVersion||_chipCountPoints!=Points.Count)
        {
            _nextChipCounts=Time.unscaledTime+2f;_chipCountVersion=_stateVersion;_chipCountPoints=Points.Count;
            _chipCounts.Clear();
            foreach(var point in Points)
            {
                if(point==null)continue;
                string id=ChipOf(point);if(id.Length==0)continue;
                var spec=AtlasGroupPolicy.Find(id);
                _chipCounts.TryGetValue(id,out var count);
                count.Total++;if(spec!=null&&spec.Progress&&Opened(point))count.Done++;
                _chipCounts[id]=count;
            }
        }
        return chip!=null&&_chipCounts.TryGetValue(chip,out var c)?c:default;
    }
    internal readonly List<Point> Points = new();
    internal readonly CapsuleTools Capsules = new();
    internal readonly NativeIconTools Icons;
    internal readonly WorldSourceObservations Sources=new();
    private WorldSourceObservations.Snapshot _sourceSnapshot;
    private readonly Dictionary<string,MapDefinition> _sourceMaps=new(StringComparer.Ordinal);
    private float _nextSourceMaps;
    internal string Search="", IndexStatus="Индекс ещё не загружен", TeleportStatus="ЛКМ ставит обычную метку";
    internal bool HideOpened=true, ClickTeleport, CustomPointMode, PanelExpanded=true;
    // One fixed icon size for every marker; the player sets it with the slider.
    internal float MarkerSize=MapOverlayLayout.DefaultSize;
    internal int ClickRequests {get;private set;}
    internal string LastClickResolver {get;private set;}="not requested";
    internal Vector3 LastClickWorld {get;private set;}
    internal Vector2 LastClickScreen {get;private set;}
    internal bool LastTransitSucceeded {get;private set;}
    internal string TransitDetails {get;private set;}="not requested";
    internal Point Selected;
    internal bool SourcesActualOnly;
    private float _nextDroneObservation;
    private bool _flightUiDiscoveryAttempted;
    private List<Point> _filterResult;private string _filterKey;private float _filterUntil;
    internal readonly List<Rect> MarkerHitRects=new();
    internal readonly List<Rect> OverlayCardRects=new();
    // Excludes native map chrome from our click-to-teleport path, but never
    // adds raycast shields over it: native title/counter/zoom controls must work.
    internal readonly List<Rect> NativeChromeRects=new();
    private Point _pendingFocus;
    internal AtlasUi Ui {get;private set;}
    internal bool NativeMapOpen => Ui != null && !Ui.WasCollected && Ui.gameObject.activeInHierarchy;
    internal bool NativeMapHasInput=>NativeMapOpen&&NativeUiPriority.IsFrontDisplay(Ui);
    internal bool Busy => _transit!=0;
    internal bool HasReturn => _return!=null && _return.Session==_app.Session;
    internal float ProjectionError {get;private set;}=-1f;
    internal string DisplayStatus {get;private set;}="display not requested";
    private readonly ToolkitMod _app;
    private readonly List<Image> _shields=new();
    private GameObject _shieldRoot;
    private Canvas _shieldCanvas;
    private IntPtr _shieldUi,_shieldSession,_shieldPage;
    private int _shieldWidth,_shieldHeight;
    private int _shieldFrameBegun=-1,_shieldLastFrame=-1;
    private readonly List<MapShieldPolicy.Box> _nativeControlRects=new();
    private bool _nativeControlsKnown;
    private int _nativeControlsFrame=-1;
    private IntPtr _nativeControlsUi,_nativeControlsPage;
    private IntPtr _labelMap;
    private readonly List<(string Name,Vector3 Position)> _zoneLabels=new();
    private float _nextUiScan, _nextPodScan;
    private float _nextUiDiscovery,_uiDiscoveryUntil;
    private readonly List<AtlasUi> _uiCandidates=new();
    private IntPtr _lastUiTop;
    private bool _discoverUi;
    internal int UiDiscoveryScans {get;private set;}
    internal int ShieldCount=>_shields.Count;
    internal int SubscriptionCount=>_navHandler!=null?1:0;
    internal int CachedUiCount=>_uiCandidates.Count;
    private IntPtr _indexedSession;
    private readonly Dictionary<string,SceneGroup> _groups=new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _openedPods=new();
    private readonly HashSet<string> _observedPodIds=new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadedPodIds=new(StringComparer.Ordinal);
    private readonly HashSet<string> _ambiguousPodIds=new(StringComparer.Ordinal);
    private bool _podCensusOk;
    // M28: the last pod census read the whole save (world ready, no native failure).
    // A pod outside a complete census has no save model: its area never streamed in.
    internal bool PodCensusComplete=>_podCensusOk;
    internal bool PodInCensus(string id)=>id!=null&&_observedPodIds.Contains(id);
    internal bool PodAmbiguous(string id)=>id!=null&&_ambiguousPodIds.Contains(id);
    internal bool PodNeverLoaded(Point point)=>point!=null&&point.Category=="Капсулы"&&point.Id!=null&&
        M28TrackingStatePolicy.Classify(false,_podCensusOk,_observedPodIds.Contains(point.Id),_ambiguousPodIds.Contains(point.Id))==M28TrackingStatePolicy.Missing.NeverLoaded;
    private float _nextRewardValidation;
    private readonly Dictionary<string,MapDefinition> _maps=new(StringComparer.OrdinalIgnoreCase);
    private MapDirector _director;
    private Il2CppSystem.Action<MapNavigationMarkerData> _navHandler;
    private bool _ignoreNav;
    private bool _clickDown,_clickDragged;
    private Vector2 _clickStart;
    private IntPtr _clickPage;
    private float _clickExpires;
    private Point _clickPoint;
    private int _transit;
    private float _transitStart,_deadline,_nextGroundQuery;
    private float _placedAt;
    private bool _returning;
    private Vector3 _settledLanding;
    private readonly TransitSafety _collisionGate=new();
    internal float LastTransitHoldSeconds {get;private set;}
    internal int LastStableGroundSamples {get;private set;}
    internal bool LastSceneAssetsReady {get;private set;}
    private Vector3 _destination;
    private SceneGroup _destinationGroup;
    private bool _exactHeight,_allowNearbyLanding,_oldGod,_oldFlight,_oldAutosave,_restoreAutosave;
    private IntPtr _transitSession;
    private CinemaTools.Waypoint _origin,_return;
    internal MapTools(ToolkitMod app){_app=app;Icons=new NativeIconTools(app);}

    internal void Open()
    {
        RefreshIndex();
        _discoverUi=true;_nextUiScan=_nextUiDiscovery=0;
        _uiDiscoveryUntil=Time.unscaledTime+3f;
        _app.CloseMenu();
        // MapDirector.OpenMap only emits a request. The native UI root owns
        // creation/initialization of the actual display and its view-model.
        var root=UnityEngine.Object.FindObjectOfType<Il2CppMonomiPark.SlimeRancher.UI.Map.MapUIRoot>();
        if(root!=null)root.OpenMap();else _app.Context.MapDirector.OpenMap();
        _app.Input.ExpectNativeWindow();
        _app.LoggerInstance.Msg("Atlas native open: root="+(root!=null)+" director="+_app.Context.MapDirector.Pointer);
    }
    internal void OpenAt(Point point)
    {
        if(point==null)throw new InvalidOperationException("Выберите метку.");
        if(point.SourceObservation&&MapForPoint(point)==null)throw new InvalidOperationException("Страница карты для этого источника не подтверждена.");
        Open();
        Selected=Points.FirstOrDefault(p=>p.Id==point.Id&&p.Category==point.Category)??point;
        _pendingFocus=Selected;_nextPodScan=0;
    }
    internal void CloseNative()
    {
        if(NativeMapOpen)GameContext.Instance.UIDirector.CloseDisplayByObjectInHierarchy(Ui);
        OpenNativeInputGate();ClearShields();Ui=null;_discoverUi=false;_uiDiscoveryUntil=0;
    }
    internal void Tick()
    {
        // Retire before transit/readiness/session early returns, not only during OnGUI.
        if(!ShieldOverlayReady||_shieldSession!=_app.Session||_shieldUi!=Ui.Pointer||
           _shieldPage!=(Ui.CurrentMapDefinition?.Pointer??IntPtr.Zero)||
           _shieldWidth!=Screen.width||_shieldHeight!=Screen.height||Time.frameCount>_shieldLastFrame+1)ClearShields();
        if(_transit!=0)TickTransit();
        if(!_app.Ready){OpenNativeInputGate();return;}
        Capsules.Tick();
        if(_indexedSession!=_app.Session)RefreshIndex();
        Icons.Tick(this);
        TickMiniMap();
        TickSourceObservations();TickDroneObservations();
        if(Time.unscaledTime>=_nextUiScan)
        {
            _nextUiScan=Time.unscaledTime+.25f;
            Ui=null;int loaded=0,active=0;
            var top=GameContext.Instance?.UIDirector?.TopStackableUI?.Pointer??IntPtr.Zero;
            if(top!=_lastUiTop)
            {
                _lastUiTop=top;
                if(top!=IntPtr.Zero){_discoverUi=true;_uiDiscoveryUntil=Time.unscaledTime+3f;_nextUiDiscovery=0;}
                else if(Time.unscaledTime>=_uiDiscoveryUntil)_discoverUi=false;
            }
            for(int i=_uiCandidates.Count-1;i>=0;i--)
            {
                var candidate=_uiCandidates[i];
                if(candidate==null||candidate.WasCollected||!candidate.gameObject.scene.IsValid()||!candidate.gameObject.scene.isLoaded)
                {_uiCandidates.RemoveAt(i);continue;}
            }
            // Most frames have no UI-stack change. Reuse live pooled displays;
            // Discovery is bounded to explicit open/UI-stack changes. An empty
            // cache is NOT a reason to scan all Unity objects in closed gameplay.
            if(_discoverUi&&Time.unscaledTime>=_nextUiDiscovery)
            {
                _nextUiDiscovery=Time.unscaledTime+1f;UiDiscoveryScans++;
                _uiCandidates.Clear();
                foreach(var candidate in Resources.FindObjectsOfTypeAll<AtlasUi>())
                    if(candidate!=null&&!candidate.WasCollected&&candidate.gameObject.scene.IsValid()&&candidate.gameObject.scene.isLoaded)
                        _uiCandidates.Add(candidate);
                _discoverUi=_uiCandidates.Count==0&&Time.unscaledTime<_uiDiscoveryUntil;
            }
            foreach(var candidate in _uiCandidates)
            {
                loaded++;if(!candidate.gameObject.activeInHierarchy)continue;active++;
                if(candidate._map!=null&&candidate._viewModel!=null&&NativeUiPriority.IsFrontDisplay(candidate))Ui=candidate;
            }
            DisplayStatus=$"loaded={loaded} active={active} map={Ui?._map!=null} model={Ui?._viewModel!=null}";
            if(Ui==null){ClearShields();_zoneHarvestPage=IntPtr.Zero;}
            else if(Time.unscaledTime>=_nextZoneHarvest){_nextZoneHarvest=Time.unscaledTime+2f;HarvestZoneTitles();}
            var director=_app.Context.MapDirector;
            if(_director!=director)
            {
                Unsubscribe();_director=director;
                _navHandler=new Action<MapNavigationMarkerData>(HandleNavigation);
                _director.add_OnPlayerNavMarkerChanged(_navHandler);
            }
        }
        Fog.Tick(this);
        PrepareNativeControlExclusions();
        TrackMapClick();
        if(!ShieldOverlayReady)SuspendRaycastShields();
        // Slider/panel gestures never reach the native pan/zoom or click-to-place handlers.
        TickNativeInputGate();
        if(NativeMapOpen||_app.MenuOpen)SamplePlayer();
        if((NativeMapOpen||_app.MenuOpen) && Time.unscaledTime>=_nextPodScan)
        {
            _nextPodScan=Time.unscaledTime+.5f;
            RefreshOpenedPods();RefreshGordos();RefreshProgressState();
            UpdateProjectionCheck();
            if(Ui!=null&&_pendingFocus!=null&&Ui._map!=null&&Ui._viewModel!=null){var point=_pendingFocus;_pendingFocus=null;Focus(point);}
        }
    }
    private void BeginMapClick(Vector2 screen)
    {
        _clickDown=true;_clickDragged=OverlayContains(screen);_clickStart=screen;
        _clickPage=Ui.CurrentMapDefinition?.Pointer??IntPtr.Zero;_clickExpires=0;_clickPoint=null;
    }
    private void TrackMapClick()
    {
        if(!NativeMapHasInput||Ui._map==null||Ui._viewModel==null||!(ClickTeleport||CustomPointMode)||Busy)
        {_clickDown=false;_clickPoint=null;_clickExpires=0;return;}
        var mouse=Mouse.current;if(mouse==null)return;
        var screen=mouse.position.ReadValue();
        if(mouse.leftButton.wasPressedThisFrame&&!_clickDown)BeginMapClick(screen);
        if(_clickDown&&Vector2.Distance(screen,_clickStart)>6f)_clickDragged=true;
        if(_clickDown&&mouse.leftButton.wasReleasedThisFrame)
        {
            _clickDown=false;
            _clickDragged|=OverlayContains(screen)||_clickPage!=(Ui.CurrentMapDefinition?.Pointer??IntPtr.Zero);
            _clickExpires=_clickDragged?0:Time.unscaledTime+.35f;
            // Vanilla may not emit a navigation marker under unrevealed fog.
            // A genuine non-drag native pointer release still uses the native
            // map inverse, zone data and an unambiguous scene-group mapping.
            if(!_clickDragged&&_clickPoint==null)ResolveMapClick(screen,null);
            if(_clickPoint!=null)CompleteMapClick();
        }
    }
    private void CompleteMapClick()
    {
        if(_clickDragged||_clickExpires<=Time.unscaledTime||_clickPoint==null)return;
        var mouse=Mouse.current;
        if(mouse==null||!NativeMapHasInput||
           _clickPage!=(Ui.CurrentMapDefinition?.Pointer??IntPtr.Zero)||
           OverlayContains(mouse.position.ReadValue()))
        {_clickPoint=null;_clickExpires=0;_clickDown=false;return;}
        var point=_clickPoint;_clickPoint=null;_clickExpires=0;_clickDown=false;
        ClickRequests++;
        if(CustomPointMode)
        {
            string name=_app.Custom.DraftName;int color=_app.Custom.DraftColor;
            _app.Queue("create custom map waypoint",()=>_app.Custom.Add(point,name,color));
        }
        else _app.Queue("map click teleport",()=>RequestTeleport(point));
    }
    private void Unsubscribe()
    {
        try{if(_director!=null && !_director.WasCollected && _navHandler!=null)_director.remove_OnPlayerNavMarkerChanged(_navHandler);}
        catch(Exception e){_app.Fail("map event cleanup",e);}
        finally{_director=null;_navHandler=null;}
    }
    internal void RefreshIndex()
    {
        if(!_app.Ready)return;
        Points.Clear();_filterResult=null;_maps.Clear();_groups.Clear();Selected=null;
        foreach(var group in Resources.FindObjectsOfTypeAll<SceneGroup>())
            if(group!=null && group.IsGameplay && !_groups.ContainsKey(group.name))_groups.Add(group.name,group);
        BuildGordoSceneMembership();
        _capsuleSourceAudit.Clear();
        string installed="";
        try{installed=File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"aa","catalog.hash")).Trim();}catch{}
        Capsules.Load(installed);
        // M29: route anchors before the capsule rows: a capsule on an anchor is that phantom's reward.
        LoadGhostRoutes(installed);
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("CreatorToolkit.MapIndex.tsv");
        if(stream!=null)
        {
            using var reader=new StreamReader(stream);
            string header=reader.ReadLine();
            if(header=="#catalog="+installed && installed.Length>0)
            {
                string line;
                while((line=reader.ReadLine())!=null)
                {
                    var c=line.Split('\t');if(c.Length<9)continue;
                    if(!float.TryParse(c[4],NumberStyles.Float,CultureInfo.InvariantCulture,out float x)||!float.TryParse(c[5],NumberStyles.Float,CultureInfo.InvariantCulture,out float y)||!float.TryParse(c[6],NumberStyles.Float,CultureInfo.InvariantCulture,out float z))continue;
                    _groups.TryGetValue(c[3],out var group);
                    var point=new Point{Id=c[0],Category=c[1],Name=c[2],GroupName=c[3],Group=group,Position=new Vector3(x,y,z),ExactHeight=c[7]=="1",Detail=c[8],SourceScene=c.Length>9?c[9]:null};
                    if(c.Length>10&&int.TryParse(c[10],NumberStyles.Integer,CultureInfo.InvariantCulture,out int target))point.GordoTarget=target;
                    if(point.Category=="Гордо")AddGordoIndex(point);
                    else if(point.Category=="Капсулы")AddCapsuleIndex(point,c.Length>11&&c[11]=="1");
                    else Points.Add(point);
                }
                IndexStatus="Индекс совпадает с версией игры";
            }
            else IndexStatus="Версия игры изменилась: статические метки отключены";
        }
        else IndexStatus="Статический индекс недоступен";
        AddFandomPoints(installed);
        // Resource node locations are native discoverable data, including unloaded groups.
        // Crafting materials, food and plort spawns land in separate layers.
        var seen=new HashSet<string>(Points.Select(p=>p.Id+"|"+p.GroupName));
        var lookup=LookupDirector.GetIfReady();
        _sourceSnapshot=null;
        foreach(var pair in _groups)
        {
            var group=pair.Value;
            var resources=group.DiscoverableResources?._resourceData;
            for(int i=0;resources!=null&&i<resources.Length;i++)
            {
                var resource=resources[i];if(resource==null||!seen.Add(resource.ID+"|"+group.name))continue;
                var ids=resource._identifiableTypeIDs;string id="";int types=ids?.Count??0;
                for(int k=0;k<types&&id.Length==0;k++)id=ids[k]??"";
                var entry=id.Length==0?null:_app.Actors.Catalog.FirstOrDefault(e=>!e.IsRadiant&&!e.IsPrototype&&e.Reference==id);
                string layer=MapResourceLayerPolicy.Classify(id,entry?.Category);
                if(layer==null)continue;
                string name=MapResourceLayerPolicy.TypeName(id);
                try{if(lookup!=null&&lookup._identifiableTypeByRefId.TryGetValue(id,out var type)){string n=type?.LocalizedName?.GetLocalizedString();if(!string.IsNullOrWhiteSpace(n)&&!n.StartsWith("!!"))name=n;}}catch{}
                var p=resource.Location;
                Points.Add(new Point{Id=resource.ID,Category=layer,Name=name,GroupName=group.name,Group=group,Position=new Vector3(p.x,p.y,p.z),ExactHeight=true,Detail=MapResourceLayerPolicy.Detail(layer)+"\nТип: "+id+(types>1?" (+"+(types-1)+")":""),Contents=id,SourceIcon=entry?.Icon});
            }
        }
        // Runtime pods supplement the baked index, never replace unloaded entries.
        _ambiguousPodIds.Clear();
        var nativePodIds=new Dictionary<string,IntPtr>(StringComparer.Ordinal);
        foreach(var pod in UnityEngine.Object.FindObjectsOfType<TreasurePod>())
        {
            if(pod==null||pod.WasCollected)continue;
            var podScene=pod.gameObject.scene;
            if(!CapsuleMapPolicy.NativeAllowed(podScene.IsValid(),podScene.isLoaded,pod.gameObject.activeInHierarchy,pod.enabled,pod.Id))continue;
            // M29: the reward pod of a component-drone spawner (route anchor, nothing visible there).
            var droneSpawner=pod.GetComponent<Il2CppMonomiPark.SlimeRancher.Drone.ComponentAcqDroneSpawner>();
            if(TakeGhostReward(pod.Id,droneSpawner!=null?droneSpawner.Id:null,pod.transform.position)!=null)continue;
            if(nativePodIds.TryGetValue(pod.Id,out var prior)&&prior!=pod.Pointer)_ambiguousPodIds.Add(pod.Id);
            else nativePodIds[pod.Id]=pod.Pointer;
            var existingPod=Points.FirstOrDefault(p=>p.Category=="Капсулы"&&p.Id==pod.Id);
            if(existingPod!=null){existingPod.Position=pod.transform.position;existingPod.ExactHeight=true;continue;}
            // An additive-loaded object's scene is not necessarily CurrentSceneGroup.
            // Keep owner unknown instead of attaching it to the player's region.
            string scene=pod.gameObject.scene.name;
            if(!seen.Add(pod.Id+"|"+scene))continue;
            var owners=_gordoScenes.Where(p=>_gordoSceneComplete.Contains(p.Key)&&p.Value.Contains(ScenePath(podScene.path))).Select(p=>p.Key).ToArray();
            string owner=owners.Length==1?owners[0]:scene;
            _groups.TryGetValue(owner,out var podGroup);
            Points.Add(new Point{Id=pod.Id,Name="Капсула",Category="Капсулы",Position=pod.transform.position,Group=podGroup,GroupName=owner,SourceScene=ScenePath(podScene.path),ExactHeight=true,Detail=podGroup!=null?"Загруженная активная капсула; точная группа сцены":"Загруженная капсула; группа сцены не подтверждена"});
        }
        SupplementNativeGordos();
        Points.AddRange(_app.Custom.MapPoints(_groups));
        Visible.Add(CustomWaypointTools.Category);
        ApplyFandomHints();
        Points.Sort((a,b)=>string.Compare(a.Name,b.Name,StringComparison.CurrentCultureIgnoreCase));
        foreach(var ids in Points.Where(p=>p.Category=="Капсулы").GroupBy(p=>p.Id,StringComparer.Ordinal))
            if(ids.Select(p=>p.GroupName).Distinct(StringComparer.Ordinal).Skip(1).Any())_ambiguousPodIds.Add(ids.Key);
        _indexedSession=_app.Session;
        RefreshOpenedPods();RefreshGordos();ResetGhostRouteStates();
    }
    // Extra layers: exact game-data positions (catalog-gated) plus a few wiki estimates.
    internal string FandomStatus {get;private set;}="";
    private void AddFandomPoints(string installed)
    {
        bool catalogMatches=installed.Length>0&&FandomMapMarkers.Catalog==installed;
        int skipped=0;
        foreach(var entry in FandomMapMarkers.Entries)
        {
            if(entry.Exact&&!catalogMatches){skipped++;continue;}
            _groups.TryGetValue(entry.Group,out var group);
            Points.Add(new Point{Id=entry.Id,Category=entry.Layer,Name=entry.Kind,GroupName=entry.Group,Group=group,
                Position=new Vector3(entry.X,entry.Y,entry.Z),ExactHeight=entry.Exact,AllowNearbyLanding=false,
                Contents=entry.FilterKind+" "+entry.Refs,StateKey=entry.StateKey,FilterKind=entry.FilterKind,
                Detail=(string.IsNullOrEmpty(entry.Description)?"":entry.Description+"\n")+(entry.Exact?FandomMapMarkers.ExactNote:FandomMapMarkers.SourceNote)});
        }
        FandomStatus=skipped>0?"Версия игры изменилась: точные доп. слои отключены ("+skipped+")":FandomMapMarkers.Status;
    }
    private void ApplyFandomHints()
    {
        var hints=FandomMapMarkers.Hints;
        foreach(var point in Points)
            if(point!=null&&!FandomMapMarkers.IsFandom(point.Category)&&point.Id!=null&&hints.TryGetValue(point.Id,out var hint))point.WikiHint=hint;
    }
    internal CapsuleTruthPolicy.OpenState CapsuleState(Point point)=>point!=null&&point.Category=="Капсулы"&&!_ambiguousPodIds.Contains(point.Id)
        ?Capsules.GetState(point.Id):CapsuleTruthPolicy.OpenState.Unknown;
    internal bool Opened(Point point)=>point!=null&&(Marked(point)||(point.Category=="Гордо"?GordoState(point)==GordoMapPolicy.State.Burst:
        point.Category=="Дроны"?point.Id!=null&&(_readDrones.Contains(point.Id)||GhostPaid(point)):
        !string.IsNullOrEmpty(point.StateKey)?StateDone(point):CapsuleTruthPolicy.ShouldHideOpened(CapsuleState(point))));
    internal string CapsuleStateLabel(Point point)=>CapsuleState(point) switch
    {
        CapsuleTruthPolicy.OpenState.Open=>"Открыта · состояние текущего сохранения",
        CapsuleTruthPolicy.OpenState.Locked=>"Закрыта · состояние текущего сохранения",
        _=>PodNeverLoaded(point)?M28TrackingStatePolicy.PodNeverLoaded:"Состояние неизвестно · отсутствует модель/state или конфликт ID"
    };
    internal string DisplayName(Point point)
    {
        if(point==null)return "Выберите метку";
        if(point.Category=="Гордо")return GordoState(point)==GordoMapPolicy.State.Unknown&&!GordoNeverLoaded(point)?point.Name+" · состояние неизвестно":point.GordoDisplayName??point.Name;
        if(point.Category=="Дроны")return GhostRouteReward(point) is string reward?point.Name+" · "+reward:point.Name;
        if(point.Category!="Капсулы")return point.Name;
        var view=Capsules.Get(point.Id);
        // Baked asset names remain searchable diagnostic data, never labels.
        return view.Baked?view.RewardSummary:view.Title;
    }
    // Read the actual save model; neither recipe knowledge nor absent scene objects imply opening.
    internal void RefreshOpenedPods()
    {
        _openedPods.Clear();
        var observed=new HashSet<string>(StringComparer.Ordinal);
        var loaded=new HashSet<string>(StringComparer.Ordinal);
        bool validate=Time.unscaledTime>=_nextRewardValidation;
        if(validate)_nextRewardValidation=Time.unscaledTime+2f;
        bool census=false;
        try
        {
        if(_app.Ready)
        {
            var pods=_app.Context.GameModel.AllPods();
            if(pods==null)throw new InvalidOperationException("Native pod census unavailable");
            if(pods!=null)foreach(var pair in pods)
            {
                string id=pair.Key;if(string.IsNullOrWhiteSpace(id))continue;
                observed.Add(id);
                if(observed.Count>2048)throw new InvalidOperationException("Pod census budget exceeded");
                try
                {
                    var model=pair.Value;
                    var state=model!=null&&!model.WasCollected?model.state:null;
                    bool known=state!=null&&!state.WasCollected&&!_ambiguousPodIds.Contains(id);
                    Capsules.ObserveState(id,id,known,known&&state.Value==TreasurePod.State.LOCKED,known&&state.Value==TreasurePod.State.OPEN);
                    if(CapsuleTruthPolicy.ShouldHideOpened(Capsules.GetState(id)))_openedPods.Add(id);
                    var go=model!=null&&!model.WasCollected?model.gameObj:null;
                    if(go!=null&&!go.WasCollected&&go.scene.IsValid()&&go.scene.isLoaded)
                    {
                        var pod=go.GetComponent<TreasurePod>();
                        if(!_ambiguousPodIds.Contains(id))
                        {
                            if(pod!=null&&!pod.WasCollected)
                            {loaded.Add(id);if(validate||!_loadedPodIds.Contains(id))Capsules.ValidateLoadedPod(id,pod);}
                            else
                            {
                                var rewarder=go.GetComponent<Il2CppMonomiPark.SlimeRancher.World.TreasurePodRewarder>();
                                if(rewarder!=null&&!rewarder.WasCollected)
                                {loaded.Add(id);if(validate||!_loadedPodIds.Contains(id))Capsules.ValidateLoadedRewarder(id,rewarder);}
                            }
                        }
                    }
                }
                catch{Capsules.ObserveState(id,id,false,false,false);}
            }
            census=true;
        }
        }
        catch
        {
            census=false;
            // Failed/partial native census must not retain old OPEN or native reward confirmation.
            foreach(string id in observed.Concat(_observedPodIds).Distinct(StringComparer.Ordinal))Capsules.ObserveState(id,id,false,false,false);
            foreach(string id in loaded.Concat(_loadedPodIds).Distinct(StringComparer.Ordinal))Capsules.ForgetLoadedPod(id);
            observed.Clear();loaded.Clear();_openedPods.Clear();
        }
        foreach(string id in _observedPodIds)if(!observed.Contains(id))Capsules.ObserveState(id,id,false,false,false);
        foreach(string id in _loadedPodIds)if(!loaded.Contains(id))Capsules.ForgetLoadedPod(id);
        _observedPodIds.Clear();_observedPodIds.UnionWith(observed);
        _loadedPodIds.Clear();_loadedPodIds.UnionWith(loaded);
        _podCensusOk=census;
        if(HideOpened&&Selected?.Category=="Капсулы"&&Opened(Selected))Selected=null;
    }
    internal void InvalidateScene()
    {
        Capsules.InvalidateScene();Icons.RequestRebind();Sources.InvalidateScene();
        ResetGordos();
        _sourceSnapshot=null;_sourceMaps.Clear();_nextSourceMaps=0;
        Points.RemoveAll(p=>p.SourceObservation);
        _indexedSession=IntPtr.Zero;_openedPods.Clear();_observedPodIds.Clear();_loadedPodIds.Clear();_ambiguousPodIds.Clear();_filledSlots.Clear();_readDrones.Clear();_podCensusOk=false;
        _nextPodScan=_nextRewardValidation=0;Selected=null;
    }
    internal Point NearestUnopenedCapsule()
    {
        if(_indexedSession!=_app.Session)RefreshIndex();
        RefreshOpenedPods();
        var motor=_app.Context.Player.GetComponentInChildren<SRCharacterController>(true)?.Motor;
        var group=_app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true)?.SceneGroup;
        if(motor==null||group==null)return null;
        Vector3 origin=motor.TransientPosition;
        // Prefer the current loaded region, then other resolvable native scenes.
        return Points.Where(p=>p.Category=="Капсулы"&&CapsuleState(p)==CapsuleTruthPolicy.OpenState.Locked&&p.Group!=null)
            .OrderBy(p=>p.Group==group?0:1)
            .ThenBy(p=>new Vector2(p.Position.x-origin.x,p.Position.z-origin.z).sqrMagnitude).FirstOrDefault();
    }
    // M29 task 5: the census also runs for the minimap and the compass while a source layer is on.
    internal bool SourcesRequested{get;private set;}
    private string _sourceGroupName="";
    private readonly HashSet<string> _loadedScenes=new(StringComparer.Ordinal);
    private void TickSourceObservations()
    {
        float now=Time.unscaledTime;
        bool atlas=(_app.MenuOpen&&_app.UiForTests.CurrentTabForTest==6)||NativeMapOpen&&(Visible.Contains("Горшочки")||Visible.Contains("Спавнеры"));
        bool hud=_app.Ready&&M29LayerPolicy.HudNeedsSources(ShowMiniMap,_app.Compass?.Enabled==true,Visible.Contains(M29LayerPolicy.Pots)||Visible.Contains(M29LayerPolicy.Spawners));
        bool requested=(atlas||hud)&&!Busy;
        SourcesRequested=requested;
        Sources.Tick(_app.Context,_app.Session,now,requested,M29LayerPolicy.CensusBudget(atlas));
        var snapshot=Sources.GetSnapshot(now);
        string currentGroup=SystemContext.Instance?.SceneLoader?.CurrentSceneGroup?.name??"";
        if(ReferenceEquals(snapshot,_sourceSnapshot)&&currentGroup==_sourceGroupName)return;
        _sourceSnapshot=snapshot;_sourceGroupName=currentGroup;
        _loadedScenes.Clear();
        for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
        {var scene=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);if(scene.isLoaded)_loadedScenes.Add(scene.name);}
        if((snapshot.Potential.Count>0||snapshot.Actual.Count>0)&&now>=_nextSourceMaps)
        {
            _nextSourceMaps=now+3f;_sourceMaps.Clear();
            var conflicts=new HashSet<string>(StringComparer.Ordinal);
            foreach(var zone in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.World.ZoneDefinition>())
            {
                if(zone==null||zone.WasCollected||string.IsNullOrWhiteSpace(zone.ReferenceId))continue;
                var map=_app.Context.MapDirector.GetMapForZone(zone);
                if(map==null||map.WasCollected)continue;
                if(_sourceMaps.TryGetValue(zone.ReferenceId,out var previous)&&previous.Pointer!=map.Pointer)conflicts.Add(zone.ReferenceId);
                else _sourceMaps[zone.ReferenceId]=map;
            }
            foreach(string key in conflicts)_sourceMaps.Remove(key);
        }
        string selectedKey=Selected?.SourceObservation==true?Selected.Id:null;
        Points.RemoveAll(p=>p.SourceObservation);_filterResult=null;
        // One physical source/location = one point. Table options and constraints
        // are card contents, never extra markers or invented live actors.
        foreach(var batch in snapshot.Actual.Concat(snapshot.Potential.Where(o=>o.Weight>0&&(o.Kind!=WorldSourceObservationPolicy.Kind.PotentialSlime||o.ConstraintWeight>0))).GroupBy(o=>
            string.IsNullOrEmpty(o.PhysicalKey)?o.Key:o.PhysicalKey,StringComparer.Ordinal))
        {
            var observation=batch.First();
            bool actual=observation.Kind is WorldSourceObservationPolicy.Kind.ActualGold or WorldSourceObservationPolicy.Kind.ActualContainer;
            bool slime=observation.Kind==WorldSourceObservationPolicy.Kind.PotentialSlime;
            // Director-registered spawners may deliberately disable MonoBehaviour.Update.
            // enabled=false is not evidence that the native director cannot use this source.
            _groups.TryGetValue(observation.SceneGroup,out var group);
            _sourceMaps.TryGetValue(observation.ZoneReference,out var sourceMap);
            var names=new List<string>();var refs=new HashSet<string>(StringComparer.Ordinal);Sprite icon=null;
            foreach(var item in batch)
            {
                if(!refs.Add(item.TypeReference))continue;
                var entry=_app.Actors.Catalog.FirstOrDefault(e=>!e.IsRadiant&&!e.IsPrototype&&e.Reference==item.TypeReference);
                names.Add(entry?.Name??item.NativeName);icon=refs.Count==1?entry?.Icon:null;
            }
            string name=actual?(names.FirstOrDefault()??"Горшочек"):
                slime?(names.Count==1?"Появление · "+names[0]:"Появление слаймов · видов: "+names.Count):"Место появления горшочков";
            string detail=(actual?"Горшочек существует сейчас.":"Место появления, не живой объект. Условия появления не вычислялись.")+"\n"+
                (actual?"":string.Join(", ",names)+"\n")+"Сцена: "+observation.Scene;
            if(slime&&observation.HasRadius)detail+=" · радиус "+observation.Radius.ToString("0.#",CultureInfo.InvariantCulture)+" м";
            var windows=batch.Where(o=>o.HasTimeWindow).Select(o=>o.StartHour.ToString("0.#",CultureInfo.InvariantCulture)+"–"+o.EndHour.ToString("0.#",CultureInfo.InvariantCulture)).Distinct().ToArray();
            if(windows.Length>0)detail+="\nВремя: "+string.Join(", ",windows);
            if(!actual&&!observation.SourceActive)detail+="\nИсточник сейчас неактивен.";
            if(snapshot.Incomplete)detail+="\nЧасть источников ещё не проверена.";
            // M29 task 5: a potential source has no native owner; seen in a loaded scene it is in the current scene group
            // (the compass and the minimap match points by group; the scene stays in the detail).
            string groupName=M29LayerPolicy.SourceGroup(observation.SceneGroup,currentGroup,_loadedScenes.Contains(observation.Scene));
            if(groupName.Length==0)groupName=observation.Scene;
            Points.Add(new Point{Id="source|"+batch.Key,Category=slime?"Спавнеры":"Горшочки",
                Name=name,Group=actual?group:null,GroupName=groupName,SourceMap=sourceMap,
                SourceIcon=icon??Icons.Role("Atlas"),SourceObservation=true,LiveSource=actual,ObservedAt=observation.ObservedAt,Position=observation.Position,
                ExactHeight=actual,AllowNearbyLanding=false,Detail=detail});
        }
        if(selectedKey!=null)Selected=Points.FirstOrDefault(p=>p.Id==selectedKey);
    }
    private bool _droneObservationRequested;
    internal int DroneObservationScans {get;private set;}
    private void TickDroneObservations()
    {
        if(!M21MapPolicy.ObserveDrones(_app.MenuOpen,NativeMapOpen))
        {
            if(_droneObservationRequested)
            {
                // A hidden map must not keep claiming a moving drone is live.
                // Revert to authored anchors once; next open refreshes at once.
                Points.RemoveAll(p=>p.DroneObservation);ResetGhostRouteStates();
                if(Selected?.DroneObservation==true)Selected=null;
                _filterResult=null;
            }
            _droneObservationRequested=false;_nextDroneObservation=0;return;
        }
        _droneObservationRequested=true;
        if(Time.unscaledTime<_nextDroneObservation||Busy)return;_nextDroneObservation=Time.unscaledTime+2f;
        var model=_app.Context?.GameModel;if(model==null)return;
        DroneObservationScans++;ResolveGhostZonesWithMaps();
        Points.RemoveAll(p=>p.DroneObservation);
        ResetGhostRouteStates();
        var research=model.AllResearchDrones();
        foreach(var point in Points)if(point.Category=="Дроны"&&!point.Id.StartsWith("component-route",StringComparison.Ordinal))
        {
            bool known=research!=null&&research.TryGetValue(point.Id,out var state);
            point.Name="Исследовательский дрон";
            point.Detail=known?(research[point.Id].read?"Запись прочитана":"Запись ещё не прочитана"):"Статическая точка исследовательского дрона; состояние неизвестно";
        }
        int inspected=0;
        foreach(var pair in model.identifiables)
        {
            if(++inspected>8192)break;var drone=pair.Value?.TryCast<Il2CppMonomiPark.SlimeRancher.DataModel.ComponentAcqDroneModel>();
            if(drone==null)continue;
            var route=Points.FirstOrDefault(p=>p.Id=="component-route|"+drone.SpawnerId);
            // M29: the model's flag or the reward pod OPEN in the save (the drone may be gone).
            bool paid=drone.Awarded||GhostRewardTaken(drone.SpawnerId);if(paid&&drone.SpawnerId!=null)_ghostAwarded.Add(drone.SpawnerId);
            string reward=GhostRewardText(drone.SpawnerId);
            if(route!=null){BindGhostLive(route,drone.SceneGroup);route.Name=paid?"Фантом · награда получена":"Фантом · награда не получена";route.Detail=route.Name+". Позиция начала маршрута; состояние нативной модели."+reward;}
            var go=drone.GetGameObject();
            if(go==null||go.WasCollected||!go.activeInHierarchy||!go.scene.IsValid()||!go.scene.isLoaded)continue;
            string state=paid?"Награда уже получена":"Награда ещё не получена";
            if(route!=null){route.Position=go.transform.position;route.ExactHeight=true;route.Detail=state+". Текущее положение загруженного дрона; маршрут "+drone.SpawnerId+reward;continue;}
            Points.Add(new Point{Id="component-drone|"+drone.actorId.Value,Category="Дроны",Name="Дрон-компонент · фантом",
                Position=go.transform.position,Group=drone.SceneGroup,GroupName=drone.SceneGroup?.name,ExactHeight=true,DroneObservation=true,
                Detail=state+"\nДвижущийся нативный объект · маршрут "+drone.SpawnerId+reward,Contents="ghost phantom компонент фантом "+drone.ident?.ReferenceId});
        }
        _filterResult=null;
    }

    internal string SourceStatus=>Sources.GetSnapshot(Time.unscaledTime).Status;
    internal MapDefinition MapForPoint(Point point)=>point?.SourceObservation==true&&point.SourceMap!=null&&!point.SourceMap.WasCollected?point.SourceMap:MapForGroup(point?.Group);

    internal List<Point> Filter(bool currentPage=false)
    {
        string needle=(Search??"").Trim().ToLowerInvariant();
        bool byDistance=SortByDistance&&!currentPage&&HasPlayerPosition;
        string key=needle+"|"+SourcesActualOnly+"|"+currentPage+"|"+HideOpened+"|"+string.Join(",",Visible)+"|"+string.Join(",",HiddenChips)+"|"+(Ui?.CurrentMapDefinition?.name??"")+"|"+byDistance+"|"+_stateVersion+
            "|"+HideFogged+"|"+Fog.Version+"|"+(currentPage&&Selected!=null?Selected.Category+"/"+Selected.Id:"");
        if(_filterResult!=null&&key==_filterKey&&Time.unscaledTime<_filterUntil)return _filterResult;
        _filterKey=key;_filterUntil=Time.unscaledTime+(byDistance?.5f:.15f);
        // M29: own markers stay pinned on the native atlas page whatever the search text or
        // the layer buttons say; the atlas list (currentPage=false) still filters them.
        int fogged=0;
        var result=Points.Where(p=>currentPage&&M29AtlasSearchPolicy.PinnedOnMap(p.Category)?OnCurrentMap(p):
            LayerVisible(p)&&(!SourcesActualOnly||!p.SourceObservation||p.LiveSource)&&(!HideOpened||!Opened(p))&&
            MatchesSearch(p,needle)&&(!currentPage||OnCurrentMap(p)&&DrawGordoOverlay(p)&&!FogHides(p,ref fogged))).ToList();
        if(currentPage)FogHiddenOnPage=fogged;
        if(byDistance)result=result.OrderBy(p=>Distance(p)).ToList();
        return _filterResult=result;
    }
    private bool FogHides(Point p,ref int count)
    {
        if(!HideFogged||p==Selected||!Fog.Fogged(p))return false;
        count++;return true;
    }
    private bool MatchesSearch(Point p,string needle)=>
        needle.Length==0||p.Search.Contains(needle)||p.Category=="Капсулы"&&Capsules.Get(p.Id).Search.Contains(needle);
    // An own marker on the atlas page that only the M29 pin keeps there (its layer is off or
    // the search does not match it): drawn, but neither a search hit nor a «ближайшая» target.
    internal bool PinnedOnly(Point p)=>p!=null&&M29AtlasSearchPolicy.PinnedOnMap(p.Category)&&
        (!LayerVisible(p)||!MatchesSearch(p,M29AtlasSearchPolicy.Needle(Search)));
    internal int SearchHitsOnPage()
    {
        int hits=0;
        foreach(var p in Filter(true))if(!PinnedOnly(p))hits++;
        return hits;
    }
    internal MapDefinition MapForGroup(SceneGroup group)
    {
        if(group==null)return null;
        if(_maps.TryGetValue(group.name,out var cached))return cached;
        var director=_app.Context.MapDirector;
        foreach(var mapping in Resources.FindObjectsOfTypeAll<ZoneSceneGroupMapping>())
        {
            if(mapping==null||!mapping.TryGetValue(group,out var zone)||zone==null)continue;
            var map=director.GetMapForZone(zone);if(map!=null){_maps[group.name]=map;return map;}
        }
        return null;
    }
    internal SceneGroup GroupNamed(string name)=>!string.IsNullOrEmpty(name)&&_groups.TryGetValue(name,out var group)?group:null;
    // Zone titles harvested while the native atlas is open: ZoneSceneGroupMapping and the
    // zone polygons are loaded only with the atlas UI. Kept for the game process (titles
    // do not depend on the save). The polygon under a point names its actual zone
    // (Conservatory vs Rainbow Fields share one scene group); the group title is the fallback.
    internal static readonly Dictionary<string,string> ZoneTitleByGroup=new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string,string> ZoneTitleByPoint=new(StringComparer.Ordinal);
    private readonly HashSet<string> _zoneHarvestTested=new(StringComparer.Ordinal);
    private IntPtr _zoneHarvestPage;
    private float _nextZoneHarvest;
    private bool _zoneHarvestFailed;
    // Titles of tracked points (pods, gordos, doors, teleports) and groups are also kept in
    // zone-titles.txt, so after a restart the tracking list is named before the atlas opens.
    // Order: live point, saved point, live group, saved group. Opening the atlas rewrites it.
    private static readonly Dictionary<string,string> SavedZoneByPoint=new(StringComparer.Ordinal);
    private static readonly Dictionary<string,string> SavedZoneByGroup=new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string,string> TrackedZoneByPoint=new(StringComparer.Ordinal);
    private static bool _zoneTitlesLoaded;
    internal static int ZoneTitleCount=>ZoneTitleByGroup.Count+ZoneTitleByPoint.Count;
    internal static int SavedZoneTitleCount=>SavedZoneByGroup.Count+SavedZoneByPoint.Count;
    internal string ZoneTitleFor(Point point)
    {
        if(point==null)return null;
        if(!_zoneTitlesLoaded)LoadZoneTitles();
        string title;
        if(!string.IsNullOrEmpty(point.Id)&&(ZoneTitleByPoint.TryGetValue(point.Id,out title)||SavedZoneByPoint.TryGetValue(point.Id,out title)))return title;
        if(string.IsNullOrEmpty(point.GroupName))return null;
        return ZoneTitleByGroup.TryGetValue(point.GroupName,out title)||SavedZoneByGroup.TryGetValue(point.GroupName,out title)?title:null;
    }
    private string ZoneTitlesPath=>Path.Combine(_app.DataDirectory??"","zone-titles.txt");
    private void LoadZoneTitles()
    {
        _zoneTitlesLoaded=true;
        try
        {
            if(string.IsNullOrEmpty(_app.DataDirectory)||!File.Exists(ZoneTitlesPath))return;
            int count=0;
            foreach(var line in File.ReadLines(ZoneTitlesPath))
            {
                if(count>=M27TrackingPolicy.MaxCachedZoneTitles)break;
                if(!M27TrackingPolicy.TryParseZoneTitle(line,out bool group,out string key,out string title))continue;
                (group?SavedZoneByGroup:SavedZoneByPoint)[key]=title;count++;
            }
        }
        catch(Exception e){_app.LoggerInstance.Warning("Zone titles not loaded: "+e.Message);}
    }
    private void SaveZoneTitles()
    {
        try
        {
            if(string.IsNullOrEmpty(_app.DataDirectory))return;
            var groups=new Dictionary<string,string>(SavedZoneByGroup,StringComparer.OrdinalIgnoreCase);
            foreach(var pair in ZoneTitleByGroup)groups[pair.Key]=pair.Value;
            var points=new Dictionary<string,string>(SavedZoneByPoint,StringComparer.Ordinal);
            foreach(var pair in TrackedZoneByPoint)points[pair.Key]=pair.Value;
            var lines=new List<string>(groups.Count+points.Count);
            foreach(var pair in groups)lines.Add(M27TrackingPolicy.SerializeZoneTitle(true,pair.Key,pair.Value));
            foreach(var pair in points)
            {
                if(lines.Count>=M27TrackingPolicy.MaxCachedZoneTitles)break;
                lines.Add(M27TrackingPolicy.SerializeZoneTitle(false,pair.Key,pair.Value));
            }
            Directory.CreateDirectory(_app.DataDirectory);
            string temp=ZoneTitlesPath+".tmp";
            File.WriteAllLines(temp,lines);
            File.Copy(temp,ZoneTitlesPath,true);
            File.Delete(temp);
        }
        catch(Exception e){_app.LoggerInstance.Warning("Zone titles not saved: "+e.Message);}
    }
    internal static string ZoneName(Il2CppMonomiPark.SlimeRancher.World.ZoneDefinition zone)
    {
        if(zone==null||zone.WasCollected)return null;
        string title=null;
        try{title=zone.Name?.GetLocalizedString();}catch{}
        return M27TrackingPolicy.UsableZoneTitle(title)?title.Trim():null;
    }
    private void HarvestZoneTitles()
    {
        int added=0;
        try
        {
            var mappings=Resources.FindObjectsOfTypeAll<ZoneSceneGroupMapping>();
            foreach(var pair in _groups)
            {
                if(pair.Value==null||ZoneTitleByGroup.ContainsKey(pair.Key))continue;
                foreach(var mapping in mappings)
                {
                    if(mapping==null||!mapping.TryGetValue(pair.Value,out var zone)||zone==null)continue;
                    string title=ZoneName(zone);
                    if(title!=null){ZoneTitleByGroup[pair.Key]=title;added++;}
                    break;
                }
            }
            var page=Ui.CurrentMapDefinition;
            var director=_app.Context.MapDirector;
            if(page!=null&&page.Pointer!=_zoneHarvestPage){_zoneHarvestPage=page.Pointer;_zoneHarvestTested.Clear();}
            for(int i=0;page!=null&&director!=null&&i<Points.Count;i++)
            {
                var point=Points[i];
                if(point==null||string.IsNullOrEmpty(point.Id)||ZoneTitleByPoint.ContainsKey(point.Id)||!_zoneHarvestTested.Add(point.Id))continue;
                // Only points of the displayed page: its projection also maps other pages' X/Z
                // into these polygons (live: Grey Labyrinth pods landed in Starlight Strand).
                if(MapForPoint(point)!=page)continue;
                var zone=ZoneAreaAt(Ui,point.Position)?.ZoneMapData?.PrimaryZone;
                if(zone==null||director.GetMapForZone(zone)!=page)continue;
                string title=ZoneName(zone);
                if(title==null)continue;
                ZoneTitleByPoint[point.Id]=title;added++;
                if(M27TrackingPolicy.KindOf(point.Category)>=0)TrackedZoneByPoint[point.Id]=title;
            }
        }
        catch(Exception e)
        {
            if(!_zoneHarvestFailed){_zoneHarvestFailed=true;_app.LoggerInstance.Warning("Zone titles unavailable: "+e.Message);}
            return;
        }
        if(added==0)return;
        _app.LoggerInstance.Msg("Zone titles from the atlas: +"+added+" (groups "+ZoneTitleByGroup.Count+", points "+ZoneTitleByPoint.Count+", tracked "+TrackedZoneByPoint.Count+")");
        SaveZoneTitles();
    }
    // The zone polygon under a point of the displayed atlas page; null while the atlas is
    // closed or shows another page (never a guess from another page's projection).
    internal Il2CppMonomiPark.SlimeRancher.World.ZoneDefinition ZoneAt(Point point)
    {
        if(point==null||!NativeMapOpen||Ui.CurrentMapDefinition==null||MapForPoint(point)!=Ui.CurrentMapDefinition)return null;
        return ZoneAreaAt(Ui,point.Position)?.ZoneMapData?.PrimaryZone;
    }
    // MapUI.TryFindZoneAt takes a UI world point: the zone polygons are children of the
    // zoomable map rect. Raw game X/Z gave zoom-dependent hits (live: the Conservatory and
    // Rainbow Fields resolved to Starlight Strand). Project with the native marker
    // projection instead; the hits are then identical at every zoom (probe 0.55/1/2).
    internal static Il2CppMonomiPark.SlimeRancher.UI.Map.ZoneDetectionArea ZoneAreaAt(AtlasUi atlas,Vector3 position)
    {
        if(atlas==null||atlas.WasCollected)return null;
        var map=atlas._map;var rect=map!=null?map._rectTransform:null;
        if(rect==null)return null;
        Vector3 ui=rect.TransformPoint(map.GetMapPosition(position,rect));
        return atlas.TryFindZoneAt(new Vector2(ui.x,ui.y));
    }
    internal bool OnCurrentMap(Point point)
    {
        if(!NativeMapOpen||Ui.CurrentMapDefinition==null)return false;
        var map=MapForPoint(point);
        return map!=null && map==Ui.CurrentMapDefinition;
    }
    internal Vector2 ScreenPoint(Vector3 position)
    {
        if(!NativeMapOpen||Ui._map==null)return new Vector2(-99999,-99999);
        var map=Ui._map;var rect=map._rectTransform;
        Vector3 local=map.GetMapPosition(position,rect);
        Vector3 world=rect.TransformPoint(local);
        var canvas=rect.GetComponentInParent<Canvas>();
        return RectTransformUtility.WorldToScreenPoint(canvas!=null&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null,world);
    }
    internal bool TryScreenWorld(Vector2 screen,out Vector3 world)
    {
        world=default;
        if(!NativeMapOpen||Ui._map==null)return false;
        var map=Ui._map;var rect=map._rectTransform;
        var canvas=rect.GetComponentInParent<Canvas>();
        var camera=canvas!=null&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
        if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,screen,camera,out Vector2 local))return false;
        // Invert the native forward projection, including its actual rect,
        // pivot and zoom. The vanilla nav-marker path may snap to a nearby
        // location; a teleport click must resolve the pointer itself.
        Vector3 origin=map.GetMapPosition(Vector3.zero,rect);
        Vector3 x=map.GetMapPosition(new Vector3(100f,0,0),rect)-origin;
        Vector3 z=map.GetMapPosition(new Vector3(0,0,100f),rect)-origin;
        float determinant=x.x*z.y-x.y*z.x;
        if(!float.IsFinite(determinant)||Mathf.Abs(determinant)<.00001f)return false;
        float a=local.x-origin.x,b=local.y-origin.y;
        world=new Vector3(100f*(a*z.y-b*z.x)/determinant,0,100f*(x.x*b-x.y*a)/determinant);
        return float.IsFinite(world.x)&&float.IsFinite(world.z)&&Vector2.Distance(ScreenPoint(world),screen)<1f;
    }
    private void UpdateProjectionCheck()
    {
        ProjectionError=-1;
        if(Ui?._playerMarker==null||Ui._map==null||!Ui._playerMarker.gameObject.activeInHierarchy)return;
        var motor=_app.Context.Player.GetComponentInChildren<SRCharacterController>(true)?.Motor;
        if(motor==null)return;
        var canvas=Ui._map._rectTransform.GetComponentInParent<Canvas>();
        var camera=canvas!=null&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
        Vector2 actual=RectTransformUtility.WorldToScreenPoint(camera,Ui._playerMarker.transform.position);
        ProjectionError=Vector2.Distance(ScreenPoint(motor.TransientPosition),actual);
    }
    internal void Focus(Point point)
    {
        if(point==null)return;
        Selected=point;
        if(!NativeMapOpen)return;
        var map=MapForPoint(point);
        if(map!=null&&Ui.CurrentMapDefinition!=map)Ui.OpenMap(map);
        Ui._viewModel.CenterOnWorldPosition(new Vector2(point.Position.x,point.Position.z));
        if(point.SourceObservation)return;
        _ignoreNav=true;
        try{_app.Context.MapDirector.SetPlayerNavigationMarker(point.Position,Ui.CurrentMapDefinition,0f);}
        finally{_ignoreNav=false;}
    }
    // Native map zone polygons resolve the projected X/Z even when its display is closed
    // (the hidden map keeps its last layout). Never guess an island from the nearest marker
    // or a coordinate radius, nor from another atlas page's projection.
    internal SceneGroup FlightGroupAt(Vector3 position)
    {
        var atlas=Ui??_uiCandidates.FirstOrDefault(p=>p!=null&&!p.WasCollected);
        if(atlas==null)
        {
            // A one-off explicit-flight discovery, not a permanent closed-game scan.
            if(_flightUiDiscoveryAttempted)return null;_flightUiDiscoveryAttempted=true;
            foreach(var candidate in Resources.FindObjectsOfTypeAll<AtlasUi>())
                if(candidate!=null&&!candidate.WasCollected&&candidate.gameObject.scene.IsValid()&&candidate.gameObject.scene.isLoaded)_uiCandidates.Add(candidate);
            atlas=_uiCandidates.FirstOrDefault();
        }
        if(atlas==null||atlas.WasCollected)return null;
        var page=atlas.CurrentMapDefinition;
        var current=_app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true)?.SceneGroup;
        if(page!=null&&current!=null&&_maps.TryGetValue(current.name,out var currentPage)&&currentPage!=page)return null;
        var zone=ZoneAreaAt(atlas,position)?.ZoneMapData?.PrimaryZone;
        if(zone==null)return null;SceneGroup found=null;
        foreach(var mapping in Resources.FindObjectsOfTypeAll<ZoneSceneGroupMapping>())
        for(int i=0;mapping?._mappings!=null&&i<mapping._mappings.Count;i++)
        {var row=mapping._mappings[i];if(row.Zone!=zone||row.SceneGroups==null)continue;
         foreach(var group in row.SceneGroups){if(group==null||!group.IsGameplay)continue;if(found!=null&&found!=group)return null;found=group;}}
        return found;
    }

    private void HandleNavigation(MapNavigationMarkerData marker)
    {
        if(_ignoreNav||!(ClickTeleport||CustomPointMode)||Busy||_app.MenuOpen||!NativeMapHasInput||marker==null||!marker.IsMapMarkerActive)return;
        // Accept native navigation on press OR release, but commit only after
        // a completed non-drag LMB gesture. Keyboard markers remain markers.
        var mouse=Mouse.current;
        if(mouse==null||OverlayContains(mouse.position.ReadValue()))return;
        if(!_clickDown&&mouse.leftButton.wasPressedThisFrame)BeginMapClick(mouse.position.ReadValue());
        if(!_clickDown&&_clickExpires<=Time.unscaledTime)return;
        ResolveMapClick(mouse.position.ReadValue(),marker.SceneGroup);
        if(mouse.leftButton.wasReleasedThisFrame)
        {
            _clickDown=false;_clickDragged|=Vector2.Distance(mouse.position.ReadValue(),_clickStart)>6f;
            _clickExpires=_clickDragged?0:Time.unscaledTime+.35f;
        }
        if(!_clickDown)CompleteMapClick();
    }
    private void ResolveMapClick(Vector2 screen,SceneGroup nativeHint)
    {
        if(_clickDragged||OverlayContains(screen)||!NativeMapOpen)return;
        try
        {
            if(!TryScreenWorld(screen,out Vector3 position))return;
            var area=ZoneAreaAt(Ui,position);
            if(area?.ZoneMapData?.PrimaryZone==null){_app.Notify("Телепорт отменён: выберите сушу внутри известной зоны.");return;}
            var group=nativeHint;
            // A zone may span several scene groups. Do not overwrite the
            // native resolved destination with the first group in an asset.
            if(group==null||!group.IsGameplay)
            {
                var candidates=new Dictionary<IntPtr,SceneGroup>();
                foreach(var mapping in Resources.FindObjectsOfTypeAll<ZoneSceneGroupMapping>())
                    for(int i=0;mapping?._mappings!=null&&i<mapping._mappings.Count;i++)
                    {
                        var entry=mapping._mappings[i];
                        if(entry.Zone!=area.ZoneMapData.PrimaryZone||entry.SceneGroups==null)continue;
                        foreach(var candidate in entry.SceneGroups)
                            if(candidate!=null&&candidate.IsGameplay)candidates[candidate.Pointer]=candidate;
                    }
                var current=_app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true)?.SceneGroup;
                if(current!=null&&candidates.ContainsKey(current.Pointer))group=current;
                else if(candidates.Count==1)group=candidates.Values.First();
            }
            if(group==null||!group.IsGameplay){_app.Notify("Для этой области не найдена игровая сцена. Телепорт отменён.");return;}
            LastClickWorld=position;LastClickScreen=screen;
            LastClickResolver=nativeHint!=null?"native navigation event":"native pointer release + native zone mapping";
            _clickPoint=new Point{Id="map-click",Name="Точка карты",Category="",Position=position,Group=group,GroupName=group.name,ExactHeight=false};
        }
        catch(Exception e){_app.Fail("map click",e);}
    }
    internal bool OverlayContains(Vector2 screen)
    {
        if(!ShieldOverlayReady)return false;
        if(!AllowsOverlayPointer(screen))return true;
        Vector2 p=new(screen.x,Screen.height-screen.y);
        return MarkerHitRects.Any(r=>r.Contains(p))||OverlayCardRects.Any(r=>r.Contains(p))||NativeChromeRects.Any(r=>r.Contains(p));
    }
    internal List<(string Name,Vector3 Position)> ZoneLabels()
    {
        if(!NativeMapOpen||Ui.CurrentMapDefinition==null)return _zoneLabels;
        if(_labelMap==Ui.CurrentMapDefinition.Pointer)return _zoneLabels;
        _labelMap=Ui.CurrentMapDefinition.Pointer;_zoneLabels.Clear();
        var seen=new HashSet<IntPtr>();var names=new HashSet<string>();
        var areas=Ui._zoneDetectionAreas;
        for(int i=0;areas!=null&&i<areas.Length;i++)
        {
            var zone=areas[i]?.ZoneMapData?.PrimaryZone;if(zone==null||!seen.Add(zone.Pointer))continue;
            string name=zone.Name?.GetLocalizedString();if(string.IsNullOrWhiteSpace(name)||name.StartsWith("!!"))name=zone.name;
            if(!names.Add(name))continue;
            _zoneLabels.Add((name,Ui.FindZoneWorldCenter(zone)));
        }
        return _zoneLabels;
    }
    private bool ShieldOverlayReady=>_app.Ready&&!_app.MenuOpen&&NativeMapHasInput&&Ui._map!=null&&Ui._viewModel!=null;
    private static bool ShieldAlive(UnityEngine.Object value)=>value!=null&&!value.WasCollected;
    private void ClearShields()
    {
        SuspendRaycastShields();
        if(ShieldAlive(_shieldRoot))UnityEngine.Object.Destroy(_shieldRoot);
        _shieldRoot=null;_shieldCanvas=null;_shields.Clear();_shieldUi=_shieldSession=_shieldPage=IntPtr.Zero;
        _shieldWidth=_shieldHeight=0;
        _shieldFrameBegun=_shieldLastFrame=-1;
        _nativeControlsKnown=false;_nativeControlsFrame=-1;_nativeControlRects.Clear();
    }
    // Call before the overlay's Repaint visibility gate. This is a render lease,
    // not a guessed geometry cache. An aborted render must never commit shields.
    internal void BeginMapOverlayFrame()
    {
        SuspendRaycastShields();
        MarkerHitRects.Clear();OverlayCardRects.Clear();NativeChromeRects.Clear();
        _nativeControlsKnown=false;_nativeControlsFrame=-1;_nativeControlRects.Clear();
        _shieldFrameBegun=Time.frameCount;
    }
    internal void SuspendRaycastShields()
    {
        _shieldFrameBegun=-1;
        foreach(var image in _shields)
            if(ShieldAlive(image)){image.raycastTarget=false;image.gameObject.SetActive(false);}
        if(ShieldAlive(_shieldRoot))_shieldRoot.SetActive(false);
    }
    private static GameObject ShieldRectObject(string name)
    {
        var types=new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Type>(1);
        types[0]=Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>();
        var go=new GameObject(name,types);
        go.SetActive(false);
        return go;
    }
    private static void ResetShieldTransform(RectTransform rt)
    {
        rt.anchorMin=rt.anchorMax=new Vector2(.5f,.5f);rt.pivot=new Vector2(.5f,.5f);
        rt.localRotation=Quaternion.identity;rt.localScale=Vector3.one;
        rt.anchoredPosition3D=Vector3.zero;rt.sizeDelta=Vector2.zero;
    }
    // Convert all four corners through the ACTUAL shield canvas. It is deliberately
    // an independent overlay canvas, not a child of the camera/world-space map.
    // Thus no camera fallback, donor scaleFactor division or native rotation leaks.
    private bool TryShieldLocalRect(Rect pixel,out Rect local)
    {
        local=default;
        var root=_shieldRoot.GetComponent<RectTransform>();
        if(_shieldCanvas.renderMode!=RenderMode.ScreenSpaceOverlay)return false;
        Vector2 bl,br,tl,tr;
        float bottom=Screen.height-pixel.yMax,top=Screen.height-pixel.yMin;
        if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(root,new Vector2(pixel.xMin,bottom),null,out bl)||
           !RectTransformUtility.ScreenPointToLocalPointInRectangle(root,new Vector2(pixel.xMax,bottom),null,out br)||
           !RectTransformUtility.ScreenPointToLocalPointInRectangle(root,new Vector2(pixel.xMin,top),null,out tl)||
           !RectTransformUtility.ScreenPointToLocalPointInRectangle(root,new Vector2(pixel.xMax,top),null,out tr))return false;
        if(!float.IsFinite(bl.x)||!float.IsFinite(bl.y)||!float.IsFinite(br.x)||!float.IsFinite(br.y)||
           !float.IsFinite(tl.x)||!float.IsFinite(tl.y)||!float.IsFinite(tr.x)||!float.IsFinite(tr.y))return false;
        // Reject skew/rotation instead of replacing it with an oversized AABB.
        const float epsilon=.01f;
        if(Math.Abs(bl.y-br.y)>epsilon||Math.Abs(tl.y-tr.y)>epsilon||Math.Abs(bl.x-tl.x)>epsilon||Math.Abs(br.x-tr.x)>epsilon)return false;
        local=new Rect(bl.x,bl.y,br.x-bl.x,tl.y-bl.y);
        return new MapShieldPolicy.Box(local.x,local.y,local.width,local.height).Valid;
    }
    internal bool PrepareNativeControlExclusions()
    {
        if(!ShieldOverlayReady)
        {_nativeControlsKnown=false;_nativeControlsFrame=-1;_nativeControlRects.Clear();return false;}
        IntPtr page=Ui.CurrentMapDefinition?.Pointer??IntPtr.Zero;
        if(_nativeControlsFrame==Time.frameCount&&_nativeControlsUi==Ui.Pointer&&_nativeControlsPage==page)
            return _nativeControlsKnown;
        _nativeControlsFrame=Time.frameCount;_nativeControlsUi=Ui.Pointer;_nativeControlsPage=page;
        _nativeControlRects.Clear();
        _nativeControlsKnown=CollectNativeControlExclusions(_nativeControlRects);
        if(!_nativeControlsKnown)_nativeControlRects.Clear();
        return _nativeControlsKnown;
    }
    internal bool AllowsOverlayPointer(Vector2 screen)
    {
        if(!PrepareNativeControlExclusions()||
           !MapShieldPolicy.AllowModPointer(true,_nativeControlRects,screen.x,Screen.height-screen.y))return false;
        // These same chrome holes are subtracted from raycast shields. Never
        // activate a mod button through a hole still owned by the native map.
        foreach(var rect in NativeChromeRects)
        {
            var box=new MapShieldPolicy.Box(rect.x,rect.y,rect.width,rect.height);
            if(!box.Valid)return false;
            if(screen.x>=box.X&&screen.x<box.Right&&Screen.height-screen.y>=box.Y&&Screen.height-screen.y<box.Bottom)return false;
        }
        return true;
    }

    // Exclude actual native controls, not only resolution-dependent decorative
    // reserves published by IMGUI. An unreadable control layout fails open.
    private bool CollectNativeControlExclusions(List<MapShieldPolicy.Box> exclusions)
    {
        int observed=0;
        bool AddControl(RectTransform rect)
        {
            if(rect==null||rect.WasCollected||!rect.gameObject.activeInHierarchy)return true;
            var canvas=rect.GetComponentInParent<Canvas>();
            if(!ShieldAlive(canvas))return false;
            var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
            if(canvas.renderMode!=RenderMode.ScreenSpaceOverlay&&camera==null)return false;
            var corners=new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            rect.GetWorldCorners(corners);
            float left=float.PositiveInfinity,top=float.PositiveInfinity,right=float.NegativeInfinity,bottom=float.NegativeInfinity;
            for(int i=0;i<4;i++)
            {
                Vector2 point=RectTransformUtility.WorldToScreenPoint(camera,corners[i]);
                float y=Screen.height-point.y;
                if(!float.IsFinite(point.x)||!float.IsFinite(y))return false;
                left=Math.Min(left,point.x);right=Math.Max(right,point.x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);
            }
            var box=new MapShieldPolicy.Box(left,top,right-left,bottom-top);
            if(!box.Valid)return false;
            if(exclusions.Count>=MapShieldPolicy.MaxExclusions)return false;
            exclusions.Add(box);observed++;return true;
        }
        try
        {
            var controls=Ui.GetComponentsInChildren<Selectable>(true);
            if(controls.Length>MapShieldPolicy.MaxExclusions)return false;
            foreach(var control in controls)
                // M28: the moved drone-menu button is covered by the shield, never a hole in it.
                if(control!=null&&!control.WasCollected&&control.enabled&&!(_app.DroneToggle?.Shielded(control)??false)&&
                    !AddControl(control.GetComponent<RectTransform>()))return false;
            var zooms=Ui.GetComponentsInChildren<Il2CppMonomiPark.SlimeRancher.UI.Map.MapZoomSlider>(true);
            if(zooms.Length>MapShieldPolicy.MaxExclusions)return false;
            foreach(var zoom in zooms)
                if(zoom!=null&&!zoom.WasCollected&&zoom.enabled&&!AddControl(zoom._rectTransform))return false;
            return observed>0;
        }
        catch{return false;}
    }
    internal void SyncRaycastShields()
    {
        // Only the SUCCESS path of a completed Repaint may publish these regions.
        bool begun=_shieldFrameBegun==Time.frameCount;
        SuspendRaycastShields();
        if(!begun||!ShieldOverlayReady||Screen.width<=0||Screen.height<=0)return;
        var donor=Ui.GetComponentInParent<Canvas>();
        if(!ShieldAlive(donor))return;
        donor=donor.rootCanvas;
        if(!ShieldAlive(donor)||donor.targetDisplay!=0||donor.sortingOrder>=32767||donor.sortingOrder< -32768)return;
        var sources=new List<MapShieldPolicy.Box>();var exclusions=new List<MapShieldPolicy.Box>();
        foreach(var rect in OverlayCardRects)sources.Add(new MapShieldPolicy.Box(rect.x,rect.y,rect.width,rect.height));
        foreach(var rect in MarkerHitRects)sources.Add(new MapShieldPolicy.Box(rect.x,rect.y,rect.width,rect.height));
        foreach(var rect in NativeChromeRects)exclusions.Add(new MapShieldPolicy.Box(rect.x,rect.y,rect.width,rect.height));
        if(!PrepareNativeControlExclusions())return;
        exclusions.AddRange(_nativeControlRects);
        var pieces=MapShieldPolicy.Build(sources,exclusions,new MapShieldPolicy.Box(0,0,Screen.width,Screen.height));
        if(pieces.Count==0)return;
        try
        {
            if(_shieldUi!=Ui.Pointer||_shieldSession!=_app.Session||!ShieldAlive(_shieldRoot))
            {
                ClearShields();
                _shieldUi=Ui.Pointer;_shieldSession=_app.Session;
                _shieldRoot=ShieldRectObject("CreatorToolkit_MapInputShields");
                _shieldCanvas=_shieldRoot.AddComponent<Canvas>();
                _shieldCanvas.renderMode=RenderMode.ScreenSpaceOverlay;
                _shieldCanvas.targetDisplay=0;_shieldCanvas.scaleFactor=1f;
                var raycaster=_shieldRoot.AddComponent<GraphicRaycaster>();
                raycaster.blockingObjects=GraphicRaycaster.BlockingObjects.None;
            }
            // Only our canvas is ordered. Never move a native ancestor or sibling.
            // No native ancestors means event bubbling cannot reach MapUI handlers.
            _shieldCanvas.sortingLayerID=donor.sortingLayerID;
            _shieldCanvas.sortingOrder=donor.sortingOrder+1;
            _shieldRoot.SetActive(true);
            while(_shields.Count<pieces.Count)
            {
                var go=ShieldRectObject("CreatorToolkit_MapInputShield");
                go.transform.SetParent(_shieldRoot.transform,false);
                go.AddComponent<CanvasRenderer>();
                var image=go.AddComponent<Image>();
                image.raycastTarget=false;image.sprite=null;image.color=Color.clear;
                _shields.Add(image);
            }
            // Configure everything before enabling ANY raycast target.
            for(int i=0;i<pieces.Count;i++)
            {
                var p=pieces[i];
                if(!TryShieldLocalRect(new Rect(p.X,p.Y,p.Width,p.Height),out var local))
                {SuspendRaycastShields();return;}
                var image=_shields[i];var rt=image.rectTransform;
                ResetShieldTransform(rt);
                var root=_shieldRoot.GetComponent<RectTransform>();
                rt.anchorMin=rt.anchorMax=root.pivot;
                rt.anchoredPosition=local.center;rt.sizeDelta=local.size;
            }
            if(!ShieldOverlayReady||_shieldSession!=_app.Session){SuspendRaycastShields();return;}
            for(int i=0;i<pieces.Count;i++)
            {_shields[i].raycastTarget=true;_shields[i].gameObject.SetActive(true);}
            _shieldPage=Ui.CurrentMapDefinition?.Pointer??IntPtr.Zero;
            _shieldWidth=Screen.width;_shieldHeight=Screen.height;
            _shieldLastFrame=Time.frameCount;
        }
        catch
        {
            // Destroy is deferred; disable synchronously first, even on partial setup.
            ClearShields();throw;
        }
    }
    internal void RequestTeleport(Point point)
    {
        if(Busy||point?.Group==null)throw new InvalidOperationException("Телепорт недоступен: нет целевой игровой сцены или переход уже выполняется.");
        if(!float.IsFinite(point.Position.x)||!float.IsFinite(point.Position.y)||!float.IsFinite(point.Position.z))throw new InvalidOperationException("Невалидные координаты карты.");
        var player=_app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true);
        var motor=_app.Context.Player.GetComponentInChildren<SRCharacterController>(true)?.Motor;
        if(player?.SceneGroup==null||motor==null)throw new InvalidOperationException("Игрок ещё не готов к телепортации.");
        if(!SceneAssetsReady(player.SceneGroup))
            throw new InvalidOperationException("Исходная сцена ещё не готова к безопасному возврату: "+SceneAssetStatus(player.SceneGroup));
        _origin=new CinemaTools.Waypoint{Name="До телепорта по карте",Position=motor.TransientPosition,Group=player.SceneGroup,Session=_app.Session};
        _destination=point.Position;_destinationGroup=point.Group;_exactHeight=point.ExactHeight;_allowNearbyLanding=point.AllowNearbyLanding;
        LastTransitSucceeded=false;
        TransitDetails="from="+_origin.Group.name+" to="+_destinationGroup.name+" target="+_destination.ToString("F3")+" exact="+_exactHeight+" nearby="+_allowNearbyLanding;
        _app.LoggerInstance.Msg("Atlas transit: "+TransitDetails);
        _transitSession=_app.Session;_oldGod=_app.Player.GodMode;_oldFlight=_app.Player.Flight;
        var save=GameContext.Instance.AutoSaveDirector;_oldAutosave=save._autosaveDisabled;save.SetAutosaveDisabled(true);_restoreAutosave=true;
        _app.Player.GodMode=true;
        if(_app.Player.Flight)_app.Player.SetFlight(false);
        _origin.Position=motor.TransientPosition; // Flight exit may have restored its last grounded location.
        CloseNative();_app.CloseMenu();
        _transit=1;_returning=false;_transitStart=Time.unscaledTime;
        _deadline=_transitStart+TransitSafety.LoadDeadlineSeconds;_nextGroundQuery=0;_collisionGate.Reset();
        LastTransitHoldSeconds=0;LastStableGroundSamples=0;LastSceneAssetsReady=false;
        // Native async group loading owns core/sector references and Addressables
        // handles. The player is only staged here, never released into that spot.
        var loadPosition=new Vector3(_destination.x,_exactHeight?_destination.y+3f:point.Group.InitialPlayerPosition.y+8f,_destination.z);
        try
        {
            if(!_app.Player.TryNearbyMapGround(_origin.Position,out var safeOrigin))
                throw new InvalidOperationException("У исходной позиции нет подтверждённой безопасной земли для возврата.");
            _origin.Position=safeOrigin;
            _app.Player.BeginTransitHold();
            NativeTeleport(loadPosition,_destinationGroup);
            _app.Player.MaintainTransitHold();
            TeleportStatus="Подготовка острова… Физика игрока удерживается.";_app.Notify(TeleportStatus);
        }
        catch{RestoreTransit();throw;}
    }
    private void NativeTeleport(Vector3 position,SceneGroup group)
    {
        if(IPlayerController.TryGet(out var controller)){controller.StopAbilitiesForTeleport();controller.ResetVelocity(false);}
        var loader=SystemContext.Instance.SceneLoader;
        _app.LoggerInstance.Msg("Atlas native travel: loaded="+loader.CurrentSceneGroup?.name+" requested="+group.name);
        if(loader.CurrentSceneGroup!=group)
        {
            // TeleportablePlayer.TeleportTo alone can update model membership
            // without loading the destination's colliders. The scene loader
            // must own a real cross-region transition and its player placement.
            var parameters=new SceneLoadingParameters{
                TeleportPlayer=true,PauseGame=true,
                TeleportPosition=new Il2CppSystem.Nullable<Vector3>{hasValue=true,value=position},
                TeleportRotation=new Il2CppSystem.Nullable<Vector3>(),
                ReloadAllCoreScenes=false,ReloadAllNonCoreScenes=false};
            _app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true).PrepareForUnmanagedTeleport();
            loader.LoadSceneGroup(group,parameters);
        }
        else _app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true).TeleportTo(position,group,new Il2CppSystem.Nullable<Vector3>(),false);
    }
    internal bool SceneAssetsReady(SceneGroup group)
    {
        var loader=SystemContext.Instance?.SceneLoader;
        if(group==null||loader==null||loader.IsSceneLoadInProgress||loader.CurrentSceneGroup!=group)return false;
        if(group.CoreSceneReference!=null&&!loader.IsAssetLoaded(group.CoreSceneReference))return false;
        var scenes=group.SceneReferences;
        for(int i=0;scenes!=null&&i<scenes.Length;i++)
            if(scenes[i]!=null&&!loader.IsAssetLoaded(scenes[i]))return false;
        return true;
    }
    internal string SceneAssetStatus(SceneGroup group)
    {
        var loader=SystemContext.Instance?.SceneLoader;
        if(loader==null||group==null)return "no scene loader/group";
        int loaded=0,total=0;var scenes=group.SceneReferences;
        for(int i=0;scenes!=null&&i<scenes.Length;i++)if(scenes[i]!=null){total++;if(loader.IsAssetLoaded(scenes[i]))loaded++;}
        return "group="+group.name+" current="+loader.CurrentSceneGroup?.name+" loading="+loader.IsSceneLoadInProgress+
            " core="+(group.CoreSceneReference==null||loader.IsAssetLoaded(group.CoreSceneReference))+" sectors="+loaded+"/"+total;
    }
    private void TickTransit()
    {
        if(_app.Session!=_transitSession){RestoreTransit();return;}
        try
        {
            _app.Player.MaintainTransitHold();
            if(!_app.Ready)return; // Never release gravity during the native async load.
            if(_transit==5)return; // Failed return: hold, no autosave, explicit retry.
            float now=Time.unscaledTime;
            var group=_returning?_origin?.Group:_destinationGroup;
            if(_transit==4)
            {
                if(now<_placedAt+TransitSafety.PlacementSettleSeconds)return;
                Physics.SyncTransforms();
                if(!SceneAssetsReady(group)||!_app.Player.TryMapGround(_settledLanding,true,out var check)||
                    Vector3.Distance(check,_settledLanding)>TransitSafety.MaximumGroundDrift)
                {
                    // Streaming transforms or a passing actor may invalidate a
                    // sample briefly. Stay held and re-run the bounded gate;
                    // do not release gravity or permanently freeze on one miss.
                    _collisionGate.Reset();_nextGroundQuery=0;_transit=_returning?3:1;
                    TeleportStatus="Повторная проверка земли после загрузки…";return;
                }
                LastTransitHoldSeconds=now-_transitStart;
                LastStableGroundSamples=_collisionGate.Samples;LastSceneAssetsReady=true;
                bool returned=_returning;
                if(!returned)
                {
                    _return=_origin;LastTransitSucceeded=true;
                    TransitDetails+=" landing="+_settledLanding.ToString("F3")+" stableSamples="+LastStableGroundSamples+" holdSeconds="+LastTransitHoldSeconds.ToString("F2");
                }
                RestoreTransit();
                TeleportStatus=returned?"Цель небезопасна. Игрок возвращён на проверенную землю.":"Остров готов. Безопасная посадка подтверждена; доступен возврат.";
                _app.Notify(TeleportStatus);return;
            }
            if(now>_deadline)
            {
                if(_returning)
                {
                    _transit=5;TeleportStatus="Возврат ещё небезопасен. Физика и автосохранение удерживаются; повторите возврат.";
                    _app.Fail("safe transit recovery",new InvalidOperationException(TeleportStatus));
                }
                else BeginRollback("Время подготовки земли истекло.");
                return;
            }
            if(now<_nextGroundQuery)return;
            _nextGroundQuery=now+TransitSafety.QueryIntervalSeconds;
            Physics.SyncTransforms();
            Vector3 landing=default;
            bool ready=SceneAssetsReady(group)&&(_returning?
                _app.Player.TryNearbyMapGround(_origin.Position,out landing):TryDestinationGround(out landing));
            if(!_collisionGate.Observe(ready,now,landing.x,landing.y,landing.z))
            {
                TeleportStatus=ready?"Проверка коллизий земли · "+_collisionGate.StableSeconds(now).ToString("F1")+" / 0,75 с":
                    "Подготовка сцены и безопасного сектора…";
                return;
            }
            Vector3 placed=landing;
            // Saved feet should remain exact when already above the same
            // stable, collision-free support. Do NOT overwrite the measured
            // ground used by the warmup gate with this preferred transform.
            if(!_returning&&_allowNearbyLanding&&_exactHeight&&
                new Vector2(landing.x-_destination.x,landing.z-_destination.z).sqrMagnitude<.0025f&&
                Math.Abs(landing.y-_destination.y)<TransitSafety.MaximumGroundDrift&&_app.Player.IsClearPosition(_destination))
                placed=_destination;
            _app.Player.PlaceForMap(placed);
            _settledLanding=placed;_placedAt=now;_transit=4;
            TeleportStatus="Посадка подтверждена. Стабилизация физики…";
        }
        catch(Exception e)
        {
            _app.Fail("safe transit",e);
            try { BeginRollback("Проверка перехода завершилась ошибкой."); }
            catch(Exception recovery)
            { _transit=5;TeleportStatus="Переход остановлен безопасно; требуется повторить возврат.";_app.Fail("safe transit rollback",recovery); }
        }
    }
    private void BeginRollback(string reason)
    {
        _collisionGate.Reset();
        if(_returning)
        {
            _transit=5;TeleportStatus=reason+" Физика удерживается до безопасного возврата.";return;
        }
        TransitDetails+=" rollback="+reason;
        _returning=true;_transit=3;_nextGroundQuery=0;
        _deadline=Time.unscaledTime+TransitSafety.LoadDeadlineSeconds;
        NativeTeleport(_origin.Position,_origin.Group);
        _app.Player.MaintainTransitHold();
        TeleportStatus=reason+" Возвращаемся на проверенную землю.";
    }
    private bool TryDestinationGround(out Vector3 landing) =>
        _allowNearbyLanding&&_exactHeight?_app.Player.TryNearbyMapGround(_destination,out landing):
        _app.Player.TryMapGround(_destination,_exactHeight,out landing);
    internal void AbortTeleport()
    {
        if(!Busy||_origin==null||_origin.Session!=_app.Session)return;
        if(!_app.Ready){TeleportStatus="Отмена запрошена. Дождитесь окончания нативной загрузки.";_deadline=0;return;}
        _returning=false;BeginRollback("Переход отменён.");
    }
    internal void Return()
    {
        if(Busy||!HasReturn)throw new InvalidOperationException("Нет точки возврата текущего сейва.");
        var point=_return;CloseNative();_app.CloseMenu();_app.Cinema.Teleport(point);_return=null;TeleportStatus="Возврат к позиции до телепорта.";
    }
    private void RestoreTransit()
    {
        try
        {
            _app.Player.EndTransitHold();
            if(_transitSession==_app.Session)
            {
                if(!_oldFlight&&_app.Player.Flight)_app.Player.SetFlight(false);
                if(_oldFlight&&!_app.Player.Flight&&_app.Ready)_app.Player.SetFlight(true);
                _app.Player.GodMode=_oldGod;
            }
        }
        finally
        {
            if(_restoreAutosave){GameContext.Instance?.AutoSaveDirector?.SetAutosaveDisabled(
                AuditSavePolicy.RestoreAutosaveDisabled(AuditSaveGuard.Required,_oldAutosave));_restoreAutosave=false;}
            _transit=0;_transitSession=IntPtr.Zero;_collisionGate.Reset();_returning=false;
        }
    }
    internal void Reset()
    {
        Sources.InvalidateSession();_sourceSnapshot=null;_sourceMaps.Clear();_nextSourceMaps=0;
        _flightUiDiscoveryAttempted=false;ResetGordos();
        Icons.Reset();
        if(Busy)RestoreTransit();Unsubscribe();ClearShields();_labelMap=IntPtr.Zero;_zoneLabels.Clear();Ui=null;Points.Clear();_filterResult=null;_groups.Clear();_maps.Clear();_openedPods.Clear();Selected=null;_return=null;_indexedSession=IntPtr.Zero;ClickTeleport=CustomPointMode=false;
        _clickDown=_clickDragged=false;_clickPoint=null;_clickExpires=0;
        _pendingFocus=null;_origin=null;_destinationGroup=null;_clickPage=IntPtr.Zero;_allowNearbyLanding=false;
        _uiCandidates.Clear();_lastUiTop=IntPtr.Zero;_discoverUi=false;_uiDiscoveryUntil=0;_nextUiScan=_nextUiDiscovery=_nextPodScan=0;
        MarkerHitRects.Clear();OverlayCardRects.Clear();NativeChromeRects.Clear();LastTransitSucceeded=false;
        Capsules.InvalidateSession();Icons.Reset();_observedPodIds.Clear();_loadedPodIds.Clear();_ambiguousPodIds.Clear();_nextRewardValidation=0;_podCensusOk=false;
    }
}
