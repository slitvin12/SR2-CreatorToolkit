using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppMonomiPark.SlimeRancher.UI.HUD;
using UnityEngine;
using UnityEngine.UI;

namespace CreatorToolkit;

// M27 native-style markers on the top compass bar. A few nearby targets are
// registered through the game's own RadarRegistry (ManualRadarEntry), so the
// compass draws them with its native marker prefab: the selected atlas target,
// creator radar hits and the nearest not-yet-done atlas points of the current
// zone. Anchors are our own empty GameObjects.
// Selection: M27CompassPolicy (at most six, within 250 m except the selection).
// M28: the compass shows every ManualRadarEntry as its default yellow star, so the
// real icon (the atlas/minimap one; the native flag tinted like the marker for own
// markers) is put on OUR marker instances only (CompassBarUI._radarInstances whose
// entry targets one of our anchors); everything is reverted before unregistering.
// The custom marker chosen as compass goal is always offered (M28CompassIconPolicy).
internal sealed class CompassMarkerTools
{
    private sealed class Anchor
    {
        internal string Key;
        internal GameObject Object;
        internal Sprite Sprite,Display;
        internal Color Tint=Color.white;
        internal SceneGroup Group;
        internal bool Verified,Seen;
        // Styling of the native instance (M28).
        internal IntPtr StyledInstance,StyledSprite;
        internal Color StyledTint;
        internal string StyledMode="";
        internal Image Icon,Primary;
        internal bool PrimaryEnabled,Replaced,PrimaryAspect;
        internal Sprite PrimarySprite;
        internal Color PrimaryColor;
    }
    private sealed class Target
    {
        internal Vector3 Position;
        internal MapTools.Point Point;
        internal string Reference;
    }
    private const float VerifySeconds=5f,StyleSeconds=1f,StyleRetrySeconds=.1f,BarSearchSeconds=5f;
    private const int MaxRadarHits=32,MaxReRegistrations=40,MaxStyleRetries=60,MaxInstances=256;
    private const string IconName="CreatorToolkit.CompassIcon";
    private readonly ToolkitMod _app;
    private readonly Dictionary<string,Anchor> _anchors=new(StringComparer.Ordinal);
    private readonly Dictionary<string,Target> _targets=new(StringComparer.Ordinal);
    private readonly Dictionary<string,Sprite> _catalogIcons=new(StringComparer.Ordinal);
    private readonly Dictionary<int,Sprite> _cellSprites=new();
    private readonly List<M29CompassPolicy.Candidate> _candidates=new();
    private readonly HashSet<string> _keep=new(StringComparer.Ordinal);
    private readonly List<string> _remove=new();
    private readonly List<string> _spriteNames=new();
    private readonly List<float> _spriteAreas=new();
    private IntPtr _session,_group,_cellAtlas;
    private float _nextTick,_nextVerify,_nextStyle,_nextBarSearch;
    private int _catalogCount=-1,_reRegistrations,_styleRetries;
    private bool _styleDirty,_cellSpritesBroken;
    private CompassBarUI _bar;
    internal bool Enabled{get;private set;}=true;
    // M29: the user's choice (navigation.cfg); Enabled also drops after an error.
    internal bool Wanted{get;private set;}=true;
    internal int Active=>_anchors.Count;
    internal IEnumerable<string> AnchorKeys=>_anchors.Keys;
    internal int Registrations{get;private set;}
    internal string Status{get;private set;}="Метки на компасе: ожидание мира";
    internal string Style{get;private set;}=M28CompassIconPolicy.Overlay;
    internal int Styled{get;private set;}
    internal int StyleFailures{get;private set;}
    internal string StyleStatus{get;private set;}="значки: ожидание";
    internal CompassMarkerTools(ToolkitMod app)=>_app=app;

    internal void SetEnabled(bool enabled)
    {
        Enabled=Wanted=enabled;_reRegistrations=0;
        if(!enabled){Clear();Status="Метки на компасе выключены";}
        else{_nextTick=0;Status="Метки на компасе включены";}
    }
    // M29: settings changed (range, limit, phantom drones): rebuild on the next tick.
    internal void RefreshSoon()=>_nextTick=0;
    // M28: overlay (our icon over the hidden star), replace (the star's sprite swapped)
    // or off (native stars). Markers are rebuilt so no instance keeps an old style.
    internal void SetStyle(string style)
    {
        Clear();Style=M28CompassIconPolicy.Style(style);_nextTick=0;_styleRetries=0;
        StyleStatus="значки: "+M28CompassIconPolicy.StyleTitle(Style);
    }
    internal void Reset()
    {
        Clear();_session=_group=IntPtr.Zero;_catalogIcons.Clear();_catalogCount=-1;_bar=null;_nextBarSearch=0;
        foreach(var sprite in _cellSprites.Values)if(sprite!=null&&!sprite.WasCollected)UnityEngine.Object.Destroy(sprite);
        _cellSprites.Clear();_cellAtlas=IntPtr.Zero;
    }
    internal void Clear()
    {
        foreach(var anchor in _anchors.Values)Drop(anchor);
        _anchors.Clear();_targets.Clear();_candidates.Clear();Styled=0;_styleDirty=false;
    }
    private void Drop(Anchor anchor)
    {
        Unstyle(anchor);
        var go=anchor.Object;anchor.Object=null;
        if(go==null)return;
        try{RadarRegistry.UnregisterTrackedGameObjectAdvanced(go);}
        catch(Exception e){_app.LoggerInstance.Warning("Compass marker unregister failed: "+e.Message);}
        UnityEngine.Object.Destroy(go);
    }

    internal void Tick()
    {
        if(!Enabled)return;
        if(_session!=_app.Session){Clear();_session=_app.Session;_group=IntPtr.Zero;}
        float now=Time.unscaledTime;
        if(now<_nextTick)return;
        _nextTick=now+M27CompassPolicy.TickSeconds;
        var map=_app.Map;
        if(map==null||map.Busy){Clear();return;}
        try{Refresh(map,now);}
        catch(Exception e)
        {
            Clear();Enabled=false;
            Status="Метки на компасе отключены из-за ошибки: "+e.Message;
            _app.Notify(Status);
            throw;
        }
    }

    private static string Key(MapTools.Point point)=>point.Category==CustomWaypointTools.Category?
        M28CompassIconPolicy.CustomKey(point.Id):"pt|"+point.Category+"|"+point.Id;
    private void Refresh(MapTools map,float now)
    {
        var group=SystemContext.Instance?.SceneLoader?.CurrentSceneGroup;
        var motor=_app.Player?.Controller?.Motor;
        if(group==null||motor==null){Clear();Status="Метки на компасе: игрок не готов";return;}
        if(group.Pointer!=_group){Clear();_group=group.Pointer;}
        var origin=motor.TransientPosition;
        _targets.Clear();_candidates.Clear();
        // M29 task 4: cut-off distance, marker limit and phantom-drone priority from navigation.cfg.
        var nav=_app.Navigation?.Settings;
        float range=nav!=null?nav.CompassRange:M27CompassPolicy.Range;
        int max=nav!=null?M29NavigationSettings.ClampMarkers(nav.CompassMarkers):M27CompassPolicy.MaxMarkers,ghosts=nav?.Ghosts??0;
        // M28: the custom marker shown as compass goal ignores the range like the selection.
        string goal=_app.Custom?.GoalId??"";
        var goalPoint=goal.Length>0?_app.Custom.FindPoint(goal):null;
        if(goalPoint!=null&&SameGroup(goalPoint,group))
            Offer(Key(goalPoint),M28CompassIconPolicy.GoalPriority,goalPoint.Position,origin,goalPoint,null,goalPoint.Category);
        var selected=map.Selected;
        if(selected!=null&&SameGroup(selected,group))
            Offer(Key(selected),0,selected.Position,origin,selected,null,selected.Category);
        var radar=_app.Radar;
        // 0.8.1: «Показывать на компасе и мини-карте» on the radar page.
        if(radar!=null&&radar.Enabled&&radar.ShowOnHud)
        {
            var hits=radar.Hits;
            for(int i=0;i<hits.Count&&i<MaxRadarHits;i++)
            {
                var hit=hits[i];
                if(hit==null||string.IsNullOrEmpty(hit.Reference))continue;
                Offer("radar|"+hit.Reference+"|"+Mathf.RoundToInt(hit.Position.x)+","+Mathf.RoundToInt(hit.Position.z),1,hit.Position,origin,null,hit.Reference,M29CompassPolicy.RadarLayer);
            }
        }
        float range2=range*range;
        // M29: "always" pins the zone's nearest phantom route drone that still carries its reward.
        MapTools.Point pinned=null;
        if(ghosts>=2)
        {
            float best=float.MaxValue;
            foreach(var point in map.Points)
            {
                if(point==null||point==selected||point==goalPoint||!Ghost(point)||!map.LayerVisible(point)||!SameGroup(point,group)||map.Opened(point))continue;
                float gx=point.Position.x-origin.x,gz=point.Position.z-origin.z;
                if(gx*gx+gz*gz<best){best=gx*gx+gz*gz;pinned=point;}
            }
        }
        foreach(var point in map.Points)
        {
            if(point==null||point==selected||point==goalPoint||!map.LayerVisible(point)||!SameGroup(point,group))continue;
            float dx=point.Position.x-origin.x,dz=point.Position.z-origin.z;
            if(dx*dx+dz*dz>range2&&point!=pinned||map.Opened(point))continue;
            // M29 task 5: the navigator list rule - potential sources stay hidden while "only existing" is on.
            if(map.SourcesActualOnly&&point.SourceObservation&&!point.LiveSource)continue;
            bool own=point.Category==CustomWaypointTools.Category,ghost=Ghost(point);
            int priority=ghost?M29CompassPolicy.GhostPriority(ghosts,point==pinned):own?M28CompassIconPolicy.OwnPriority:Important(point.Category)?2:3;
            Offer(Key(point),priority,point.Position,origin,point,null,ghost?M29CompassPolicy.GhostLayer:point.Category);
        }
        // Every layer with a target in range gets a marker before any layer gets a second one.
        var picked=M29CompassPolicy.Pick(_candidates,max,range);
        _keep.Clear();
        foreach(var candidate in picked)_keep.Add(candidate.Key);
        _remove.Clear();
        foreach(var key in _anchors.Keys)if(!_keep.Contains(key))_remove.Add(key);
        foreach(var key in _remove){Drop(_anchors[key]);_anchors.Remove(key);}
        foreach(var candidate in picked)
        {
            var target=_targets[candidate.Key];
            Display(map,target,out var display,out var tint);
            if(_anchors.TryGetValue(candidate.Key,out var existing)&&existing.Object!=null)
            {
                existing.Object.transform.position=target.Position;
                if(display!=null&&(existing.Display==null||existing.Display.Pointer!=display.Pointer||existing.Tint!=tint))
                {existing.Display=display;existing.Tint=tint;_styleDirty=true;}
                continue;
            }
            var sprite=display??(target.Point!=null?map.Icons.Point(target.Point)??map.Icons.Category(target.Point.Category):CatalogIcon(target.Reference));
            if(sprite==null)continue;
            var go=new GameObject("CreatorToolkit.CompassAnchor");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.transform.position=target.Position;
            RadarRegistry.RegisterTrackedGameObjectAdvanced(go,group,sprite,false);
            _anchors[candidate.Key]=new Anchor{Key=candidate.Key,Object=go,Sprite=sprite,Display=display??sprite,Tint=tint,Group=group};
            Registrations++;_styleDirty=true;_styleRetries=0;
        }
        if(now>=_nextVerify){_nextVerify=now+VerifySeconds;Verify();}
        if(Enabled)
        {
            int layers=M29CompassPolicy.Layers(_candidates,range),starved=M29CompassPolicy.Starved(_candidates,picked,range);
            Status="Метки на компасе: "+_anchors.Count+" из "+max+" · до "+Mathf.RoundToInt(range)+" м · кандидатов "+_candidates.Count+" · слоёв "+layers+
                (starved>0?" · без места: "+starved:"")+(Style==M28CompassIconPolicy.Off?"":" · значков "+Styled);
            LayersInRange=layers;StarvedLayers=starved;
        }
    }
    private void Offer(string key,int priority,Vector3 position,Vector3 origin,MapTools.Point point,string reference,string layer)
    {
        if(string.IsNullOrEmpty(key)||_targets.ContainsKey(key))return;
        float dx=position.x-origin.x,dz=position.z-origin.z;
        float distance=Mathf.Sqrt(dx*dx+dz*dz);
        _targets[key]=new Target{Position=position,Point=point,Reference=reference};
        _candidates.Add(new M29CompassPolicy.Candidate(key,layer,priority,distance));
    }
    // Phantom route drones (route anchors and live component drones) form their own compass layer.
    private static bool Ghost(MapTools.Point point)=>point.Category=="Дроны"&&NativeIconPolicy.GhostDrone(point.Id,null);
    internal int LayersInRange{get;private set;}
    internal int StarvedLayers{get;private set;}
    private static bool Important(string category)=>
        category=="Капсулы"||category=="Гордо"||category=="Дроны"||category=="Ноды карты"||category=="Двери";
    private static bool SameGroup(SceneGroup a,SceneGroup b)=>a!=null&&b!=null&&(a.Pointer==b.Pointer||a.name==b.name);
    // Own markers may carry only the group name when the SceneGroup was not resolved.
    internal static bool SameGroup(MapTools.Point point,SceneGroup group)=>point!=null&&group!=null&&
        (point.Group!=null?SameGroup(point.Group,group):!string.IsNullOrEmpty(point.GroupName)&&point.GroupName==group.name);
    private Sprite CatalogIcon(string reference)
    {
        if(string.IsNullOrEmpty(reference))return null;
        var catalog=_app.Actors?.Catalog;
        int count=catalog?.Count??0;
        if(count!=_catalogCount)
        {
            _catalogCount=count;_catalogIcons.Clear();
            if(catalog!=null)
                foreach(var entry in catalog)
                    if(entry?.Reference!=null&&!_catalogIcons.ContainsKey(entry.Reference))_catalogIcons[entry.Reference]=entry.Icon;
        }
        return _catalogIcons.TryGetValue(reference,out var sprite)&&sprite!=null?sprite:null;
    }
    // Same icon as the atlas list and the minimap: canon Fandom badge (pods, map
    // nodes, drones), else the native map sprite, else the wiki cell; own markers use
    // the native waypoint flag tinted with the marker colour (like the HUD beacon).
    private void Display(MapTools map,Target target,out Sprite sprite,out Color tint)
    {
        sprite=null;tint=Color.white;
        var point=target.Point;
        if(point==null){sprite=CatalogIcon(target.Reference);return;}
        if(point.Category==CustomWaypointTools.Category)
        {
            sprite=map.Icons.Category(CustomWaypointTools.Category);
            tint=ToolkitUi.MarkerColor(point.ColorIndex);
            return;
        }
        sprite=CellSprite(CanonMapIconPolicy.Cell(point.Category,point.Id,point.Name))??
            map.Icons.Point(point)??CellSprite(FandomMapMarkers.IconFor(point))??map.Icons.Category(point.Category);
    }
    private Sprite CellSprite(int cell)
    {
        if(cell<0||_cellSpritesBroken)return null;
        var atlas=FandomMapMarkers.Atlas;
        if(atlas==null)return null;
        if(atlas.Pointer!=_cellAtlas)
        {
            foreach(var old in _cellSprites.Values)if(old!=null&&!old.WasCollected)UnityEngine.Object.Destroy(old);
            _cellSprites.Clear();_cellAtlas=atlas.Pointer;
        }
        if(_cellSprites.TryGetValue(cell,out var cached)&&cached!=null&&!cached.WasCollected)return cached;
        if(!M28CompassIconPolicy.CellRect(cell,FandomMapMarkers.AtlasColumns,FandomMapMarkers.AtlasRows,FandomMapMarkers.CellSize,
            out float x,out float y,out float w,out float h))return null;
        try
        {
            var sprite=Sprite.Create(atlas,new Rect(x,y,w,h),new Vector2(.5f,.5f),100f,0,SpriteMeshType.FullRect);
            if(sprite==null)return null;
            sprite.name="CreatorToolkit_CompassCell_"+cell.ToString(CultureInfo.InvariantCulture);
            sprite.hideFlags=HideFlags.HideAndDontSave;
            _cellSprites[cell]=sprite;
            return sprite;
        }
        catch(Exception e)
        {
            _cellSpritesBroken=true;
            _app.LoggerInstance.Warning("Compass icon sprites unavailable, native sprites are used: "+e.Message);
            return null;
        }
    }

    // The registry may be reset by the game (scene change); re-register what is missing.
    private void Verify()
    {
        if(_anchors.Count==0)return;
        var entries=RadarRegistry.TrackedEntries;
        if(entries==null)return;
        foreach(var anchor in _anchors.Values)anchor.Verified=false;
        for(int i=0;i<entries.Count;i++)
        {
            var manual=entries[i]?.Entry?.TryCast<ManualRadarEntry>();
            if(manual==null)continue;
            foreach(var anchor in _anchors.Values)
                if(!anchor.Verified&&anchor.Object!=null&&manual.TargetIs(anchor.Object)){anchor.Verified=true;break;}
        }
        foreach(var anchor in _anchors.Values)
        {
            if(anchor.Verified||anchor.Object==null)continue;
            if(++_reRegistrations>MaxReRegistrations)
            {
                Clear();Enabled=false;
                Status="Компас игры не принимает метки — функция отключена";
                return;
            }
            Unstyle(anchor);
            RadarRegistry.RegisterTrackedGameObjectAdvanced(anchor.Object,anchor.Group,anchor.Sprite,false);
            Registrations++;_styleDirty=true;
        }
    }

    // ---- M28: icons on our own compass instances -------------------------------
    internal void LateTick()
    {
        if(!Enabled||Style==M28CompassIconPolicy.Off||_anchors.Count==0)return;
        float now=Time.unscaledTime;
        if(now<_nextStyle)return;
        StyleInstances();
        _nextStyle=now+(_styleDirty?StyleRetrySeconds:StyleSeconds);
    }
    private CompassBarUI Bar()
    {
        if(_bar!=null&&!_bar.WasCollected)return _bar;
        _bar=null;
        float now=Time.unscaledTime;
        if(now<_nextBarSearch)return null;
        _nextBarSearch=now+BarSearchSeconds;
        _bar=UnityEngine.Object.FindObjectOfType<CompassBarUI>();
        return _bar;
    }
    private void StyleInstances()
    {
        var bar=Bar();
        var instances=bar!=null?bar._radarInstances:null;
        if(instances==null){StyleStatus="значки: компас игры не найден";Styled=0;return;}
        foreach(var anchor in _anchors.Values)anchor.Seen=false;
        int styled=0,seen=0,visited=0;
        foreach(var pair in instances)
        {
            if(++visited>MaxInstances)break;
            var manual=pair.Key?.TryCast<ManualRadarEntry>();
            if(manual==null)continue;
            var instance=pair.Value;
            if(instance==null||instance.WasCollected)continue;
            foreach(var anchor in _anchors.Values)
            {
                if(anchor.Seen||anchor.Object==null||!manual.TargetIs(anchor.Object))continue;
                anchor.Seen=true;seen++;
                if(Apply(anchor,instance))styled++;
                break;
            }
        }
        Styled=styled;
        bool complete=styled==_anchors.Count;
        _styleDirty=!complete&&++_styleRetries<MaxStyleRetries;
        if(complete)_styleRetries=0;
        StyleStatus="значки: "+M28CompassIconPolicy.StyleTitle(Style)+" · "+styled+" из "+_anchors.Count+
            (seen<_anchors.Count?" · компас ещё не создал "+(_anchors.Count-seen):"");
    }
    private bool Apply(Anchor anchor,RectTransform instance)
    {
        var display=anchor.Display;
        if(display==null||display.WasCollected)return false;
        if(anchor.StyledInstance==instance.Pointer&&anchor.StyledSprite==display.Pointer&&anchor.StyledTint==anchor.Tint&&
           anchor.StyledMode==Style&&StillStyled(anchor))return true;
        Unstyle(anchor);
        try
        {
            var images=instance.GetComponentsInChildren<Image>(true);
            _spriteNames.Clear();_spriteAreas.Clear();
            var native=new List<Image>();
            for(int i=0;images!=null&&i<images.Length&&i<32;i++)
            {
                var image=images[i];
                if(image==null||image.WasCollected||image.gameObject.name==IconName)continue;
                var r=image.rectTransform.rect;
                native.Add(image);_spriteNames.Add(image.sprite!=null?image.sprite.name:"");_spriteAreas.Add(Mathf.Abs(r.width*r.height));
            }
            int index=M28CompassIconPolicy.PrimaryImage(_spriteNames,_spriteAreas);
            if(index<0){StyleFailures++;return false;}
            var primary=native[index];
            anchor.Primary=primary;anchor.PrimaryEnabled=primary.enabled;
            if(Style==M28CompassIconPolicy.Replace)
            {
                anchor.Replaced=true;anchor.PrimarySprite=primary.sprite;anchor.PrimaryColor=primary.color;anchor.PrimaryAspect=primary.preserveAspect;
                primary.sprite=display;primary.color=anchor.Tint;primary.preserveAspect=true;
            }
            else
            {
                var icon=CreateIcon(instance,primary);
                icon.sprite=display;icon.color=anchor.Tint;
                anchor.Icon=icon;
                primary.enabled=false;
            }
            anchor.StyledInstance=instance.Pointer;anchor.StyledSprite=display.Pointer;anchor.StyledTint=anchor.Tint;anchor.StyledMode=Style;
            return true;
        }
        catch(Exception e)
        {
            StyleFailures++;Unstyle(anchor);
            if(StyleFailures<=3)_app.LoggerInstance.Warning("Compass icon styling failed: "+e.Message);
            return false;
        }
    }
    private static bool StillStyled(Anchor anchor)
    {
        var primary=anchor.Primary;
        if(primary==null||primary.WasCollected)return false;
        if(anchor.Replaced)return primary.sprite!=null&&primary.sprite.Pointer==anchor.StyledSprite;
        var icon=anchor.Icon;
        return icon!=null&&!icon.WasCollected&&!primary.enabled&&icon.sprite!=null&&icon.sprite.Pointer==anchor.StyledSprite;
    }
    // Our child image centred on the star, same layer, a little larger than the star.
    private static Image CreateIcon(RectTransform instance,Image primary)
    {
        var types=new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Type>(1);
        types[0]=Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>();
        var go=new GameObject(IconName,types);
        go.layer=instance.gameObject.layer;
        var rt=go.GetComponent<RectTransform>();
        rt.SetParent(instance,false);
        rt.SetAsLastSibling();
        go.AddComponent<CanvasRenderer>();
        var image=go.AddComponent<Image>();
        image.raycastTarget=false;image.preserveAspect=true;
        var source=primary.rectTransform;
        var box=source.rect;
        Vector3 center=instance.InverseTransformPoint(source.TransformPoint(new Vector3(box.center.x,box.center.y,0f)));
        float sx=Ratio(source.lossyScale.x,instance.lossyScale.x),sy=Ratio(source.lossyScale.y,instance.lossyScale.y);
        float size=M28CompassIconPolicy.IconSize(Mathf.Abs(box.width*sx),Mathf.Abs(box.height*sy));
        rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(.5f,.5f);
        rt.localRotation=Quaternion.identity;rt.localScale=Vector3.one;
        rt.sizeDelta=new Vector2(size,size);
        rt.anchoredPosition=new Vector2(center.x,center.y)-instance.rect.center;
        return image;
    }
    private static float Ratio(float a,float b)=>Mathf.Abs(b)>1e-6f&&float.IsFinite(a/b)?a/b:1f;
    private static void Unstyle(Anchor anchor)
    {
        try
        {
            var icon=anchor.Icon;
            if(icon!=null&&!icon.WasCollected)UnityEngine.Object.Destroy(icon.gameObject);
            var primary=anchor.Primary;
            if(primary!=null&&!primary.WasCollected)
            {
                if(anchor.Replaced){primary.sprite=anchor.PrimarySprite;primary.color=anchor.PrimaryColor;primary.preserveAspect=anchor.PrimaryAspect;}
                primary.enabled=anchor.PrimaryEnabled;
            }
        }
        catch{}
        anchor.Icon=anchor.Primary=null;anchor.PrimarySprite=null;anchor.Replaced=false;
        anchor.StyledInstance=anchor.StyledSprite=IntPtr.Zero;anchor.StyledMode="";
    }

    // m28-compass-dump: our instances and every image of them.
    internal string Dump()
    {
        var text=new StringBuilder();
        var bar=Bar();
        var instances=bar!=null?bar._radarInstances:null;
        text.AppendLine("enabled="+Enabled+" style="+Style+" anchors="+_anchors.Count+" styled="+Styled+" failures="+StyleFailures+
            " registrations="+Registrations+" bar="+(bar!=null)+" instances="+(instances!=null?instances.Count:-1));
        text.AppendLine(Status);text.AppendLine(StyleStatus);
        foreach(var anchor in _anchors.Values)
            text.AppendLine("anchor "+anchor.Key+" sprite="+Name(anchor.Sprite)+" display="+Name(anchor.Display)+" tint="+anchor.Tint+
                " styled="+(anchor.StyledInstance!=IntPtr.Zero)+" mode="+anchor.StyledMode);
        if(instances==null)return text.ToString();
        int index=0;
        foreach(var pair in instances)
        {
            if(index>=MaxInstances)break;
            var instance=pair.Value;
            var manual=pair.Key?.TryCast<ManualRadarEntry>();
            string owner="-";
            if(manual!=null)foreach(var anchor in _anchors.Values)if(anchor.Object!=null&&manual.TargetIs(anchor.Object)){owner=anchor.Key;break;}
            text.Append("#").Append(index++).Append(" entry=").Append(manual!=null?"ManualRadarEntry sprite="+Name(manual.Sprite):"native")
                .Append(" ours=").Append(owner);
            if(instance==null||instance.WasCollected){text.AppendLine(" instance=null");continue;}
            text.Append(" go=").Append(instance.gameObject.name).Append(" active=").Append(instance.gameObject.activeInHierarchy)
                .Append(" pos=").Append(instance.anchoredPosition).Append(" size=").Append(instance.rect.size).AppendLine();
            if(owner=="-"&&manual==null)continue;
            var images=instance.GetComponentsInChildren<Image>(true);
            for(int i=0;images!=null&&i<images.Length&&i<16;i++)
            {
                var image=images[i];if(image==null)continue;
                text.Append("   image ").Append(image.gameObject.name).Append(" sprite=").Append(Name(image.sprite))
                    .Append(" enabled=").Append(image.enabled).Append(" color=").Append(image.color)
                    .Append(" rect=").Append(image.rectTransform.rect.size).Append(" pos=").Append(image.rectTransform.anchoredPosition).AppendLine();
            }
        }
        return text.ToString();
    }
    private static string Name(UnityEngine.Object value)=>value==null||value.WasCollected?"null":value.name;
}
