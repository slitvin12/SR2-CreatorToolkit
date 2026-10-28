using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher.Map;
using UnityEngine;
using NativeMap = Il2CppMonomiPark.SlimeRancher.UI.Map.Map;
using FogMapElement = Il2CppMonomiPark.SlimeRancher.UI.Map.FogMapElement;
using PlayerMapMarker = Il2CppMonomiPark.SlimeRancher.UI.Map.PlayerMapMarker;
using UiImage = UnityEngine.UI.Image;

namespace CreatorToolkit;

// M27 game-style minimap terrain: a north-up circular crop of the real atlas art
// around the player, rotated heading-up by MiniMapUi under GUI.matrix.
// Art layers: the page background picture where a page has one, otherwise (Rainbow
// Island: MapBackground is empty) one Image per zone under the map content, painted
// in the atlas draw order. Each layer's world -> texture mapping is calibrated from
// the live native projection (MapUI._map.GetMapPosition + that Image's layout) while
// the atlas is open, and persisted per layer in UserData\CreatorToolkit\minimap-pages.txt.
// Cost model: only layers under the disc are read back (one GPU readback per frame,
// <=1024 px, at most six kept); the small composite (<=200x200) is rebuilt at <=15 Hz
// and only after the player moved at least MinMoveTexels. Nothing runs while the
// feature is off.
internal sealed class MiniMapTerrainTools
{
    private sealed class Capture
    {
        internal string Key="",Texture="";
        internal uint[] Pixels;
        internal int Width,Height;
        internal float UsedAt;
    }
    // A calibrated art layer: draw order and world box (culling).
    private sealed class Layer
    {
        internal string Key="",Name="";
        internal int Order;
        internal M27MiniMapPolicy.Calibration Calibration;
        internal double MinX,MinZ,MaxX,MaxZ;
    }
    // An art Image shown by the open atlas page.
    private sealed class Art
    {
        internal string Key="",Name="";
        internal Texture2D Texture;
        internal Layout Layout;
    }
    private sealed class Candidate
    {
        internal M27MiniMapPolicy.Affine Map;
        internal IntPtr Texture;
        internal float At;
    }
    private const float CalibrateSeconds=.5f,PageSeconds=2f,RetrySeconds=10f,CommitGap=.4f;
    private const double SampleSpan=100,FineSpan=20,CheckSpan=500;
    // Readback budget: six layers of at most 1024 px (about 4 MB each).
    private const int MaxCaptures=6,LayerCapture=1024;
    private const string BackgroundLayer="Background";
    // M29: the disc diameter is M29MiniMapLayout.Diameter, shared with DrawMiniMap (tests\M27Regression.cs checks both).
    internal static readonly uint Background=M27MiniMapPolicy.Pack(13,31,36,255);
    private readonly ToolkitMod _app;
    private readonly Dictionary<string,M27MiniMapPolicy.Calibration> _calibrations=new(StringComparer.Ordinal);
    private readonly List<Capture> _captures=new();
    private readonly Dictionary<string,float> _captureRetry=new(StringComparer.Ordinal);
    private readonly Dictionary<string,Candidate> _candidates=new(StringComparer.Ordinal);
    // Art the atlas actually displayed per layer (session only): the readback uses exactly the calibrated texture.
    private readonly Dictionary<string,Texture2D> _layerTextures=new(StringComparer.Ordinal);
    private readonly HashSet<string> _searchedPages=new(StringComparer.Ordinal);
    private readonly Dictionary<string,int> _noteCounts=new(StringComparer.Ordinal);
    private readonly List<Layer> _layers=new();
    private readonly List<M27MiniMapPolicy.Source> _sources=new();
    private string _layersPage;
    private bool _loaded,_composed,_dirty,_atlasSeen,_zonePageLogged,_mapOpen;
    private float _nextCalibrate,_nextPage,_composedAt;
    private MapDefinition _page;
    private string _pageName="",_composedPage="",_settledPage="";
    private IntPtr _settledUi;
    private uint[] _composite;
    private Texture2D _texture;
    private int _size;
    private double _composedX,_composedZ;
    internal bool Enabled{get;private set;}
    internal bool HasPlayer{get;private set;}
    internal Vector3 Player{get;private set;}
    internal float CenterX{get;private set;}
    internal float CenterZ{get;private set;}
    internal bool HasTerrain=>_composed&&_texture!=null&&_pageName.Length>0&&_composedPage==_pageName;
    internal Texture2D TerrainTexture=>HasTerrain?_texture:null;
    internal int CalibratedLayers=>_calibrations.Count;
    internal int CalibratedPages
    {
        get{var pages=new HashSet<string>(StringComparer.Ordinal);foreach(var key in _calibrations.Keys)pages.Add(PageOf(key));return pages.Count;}
    }
    internal int CachedLayers=>_captures.Count;
    // Layers painted into the last composite.
    internal int Layers{get;private set;}
    internal int Composes{get;private set;}
    internal float LastComposeMs{get;private set;}
    internal string PageName=>_pageName;
    internal string Status{get;private set;}="Рельеф мини-карты выключен";
    // Last calibration outcome (diagnostics): why the open atlas was (not) adopted.
    internal string CalibrationNote{get;private set;}="";
    internal MiniMapTerrainTools(ToolkitMod app)=>_app=app;
    // Each reason is logged at most three times, at most 40 reasons per game run.
    private void Note(string key,string detail="")
    {
        CalibrationNote=detail.Length>0?key+": "+detail:key;
        _noteCounts.TryGetValue(key,out int count);
        if(count>=3||count==0&&_noteCounts.Count>=40)return;
        _noteCounts[key]=count+1;
        _app.LoggerInstance.Msg("Minimap calibration: "+CalibrationNote);
    }
    private static string F(double value)=>value.ToString("0.######",CultureInfo.InvariantCulture);
    private static string Describe(M27MiniMapPolicy.Affine a)=>"u="+F(a.A)+"x+"+F(a.B)+"z+"+F(a.C)+" v="+F(a.D)+"x+"+F(a.E)+"z+"+F(a.F);
    private static string PageOf(string key)=>M27MiniMapPolicy.TryLayerKey(key,out string page,out _,out _)?page:key;
    private int CountFor(string page){int n=0;foreach(var key in _calibrations.Keys)if(PageOf(key)==page)n++;return n;}

    internal void SetEnabled(bool enabled)
    {
        if(Enabled==enabled)return;
        Enabled=enabled;
        if(enabled){_nextPage=_nextCalibrate=0;Status="Рельеф мини-карты включён";}
        else{Release();Status="Рельеф мини-карты выключен";}
    }
    // Scene change / quit: drop textures and page copies, keep calibrations.
    internal void Reset()
    {
        Release();
        _captureRetry.Clear();_candidates.Clear();_layerTextures.Clear();_searchedPages.Clear();
        _settledPage="";_settledUi=IntPtr.Zero;
        HasPlayer=false;_nextPage=_nextCalibrate=0;
    }
    private void Release()
    {
        if(_texture!=null)UnityEngine.Object.Destroy(_texture);
        _texture=null;_composite=null;_size=0;_composed=false;_dirty=false;_composedPage="";
        _captures.Clear();_sources.Clear();_page=null;_pageName="";_layersPage=null;Layers=0;
    }

    internal void Tick()
    {
        if(!Enabled)return;
        var map=_app.Map;
        if(map==null||!_app.Ready){HasPlayer=false;return;}
        if(!_loaded)Load();
        float now=Time.unscaledTime;
        if(map.NativeMapOpen)
        {
            // Every atlas visit re-reads the shown art: its textures are reloaded per visit.
            if(!_mapOpen){_mapOpen=true;_settledPage="";_settledUi=IntPtr.Zero;_searchedPages.Clear();}
            _atlasSeen=true;
            if(now>=_nextCalibrate){_nextCalibrate=now+CalibrateSeconds;Calibrate(map,now);}
            return;
        }
        // Live: the art is unloaded some time after the map closes and a standing player never
        // recomposed, so layers still missing are read back on the first frames after closing.
        if(_mapOpen){_mapOpen=false;_captureRetry.Clear();_dirty=true;}
        if(!map.ShowMiniMap||map.Busy)return;
        SamplePlayer();
        if(!HasPlayer)return;
        if(now>=_nextPage){_nextPage=now+PageSeconds;ResolvePage(map);}
        if(_page==null||_pageName.Length==0)
        {
            Status=_atlasSeen?"Рельеф: у этой зоны нет страницы атласа":"Рельеф: откройте карту игры один раз, чтобы определить страницу";
            return;
        }
        var layers=LayersFor(_pageName);
        if(layers.Count==0)
        {
            Status="Рельеф: откройте карту игры на этой странице один раз для калибровки";
            return;
        }
        ComposeView(layers,now);
    }

    private void SamplePlayer()
    {
        var motor=_app.Player?.Controller?.Motor;
        if(motor==null){HasPlayer=false;return;}
        var position=motor.TransientPosition;
        HasPlayer=float.IsFinite(position.x)&&float.IsFinite(position.y)&&float.IsFinite(position.z);
        if(HasPlayer)Player=position;
    }
    private void ResolvePage(MapTools map)
    {
        var group=SystemContext.Instance?.SceneLoader?.CurrentSceneGroup;
        var page=group!=null?map.MapForGroup(group):null;
        // ZoneSceneGroupMapping is loaded only with the atlas (live: after a restart the
        // terrain waited until the map was opened). The game's current-zone lookup names
        // the page without it.
        if(page==null)
        {
            try
            {
                var zone=Il2CppMonomiPark.SlimeRancher.UI.Map.MapUIUtilities.GetPlayerCurrentZone();
                if(zone!=null)page=_app.Context.MapDirector?.GetMapForZone(zone);
                if(page!=null&&!_zonePageLogged){_zonePageLogged=true;_app.LoggerInstance.Msg("Minimap page from the player zone: "+zone.name+" -> "+page.name);}
            }
            catch(Exception e)
            {
                if(!_zonePageLogged){_zonePageLogged=true;_app.LoggerInstance.Msg("Minimap player zone unavailable: "+e.GetType().Name);}
                page=null;
            }
        }
        string name=page!=null?page.name??"":"";
        _page=page;
        if(name!=_pageName){_pageName=name;_composed=false;}
    }
    // Calibrated layers of a page in atlas draw order (cached until calibrations change).
    private List<Layer> LayersFor(string page)
    {
        if(_layersPage==page)return _layers;
        _layers.Clear();_layersPage=page;
        foreach(var calibration in _calibrations.Values)
        {
            if(!M27MiniMapPolicy.TryLayerKey(calibration.Page,out string owner,out int order,out string layer)||owner!=page)continue;
            if(!M27MiniMapPolicy.WorldBounds(calibration.Map,out double minX,out double minZ,out double maxX,out double maxZ))continue;
            _layers.Add(new Layer{Key=calibration.Page,Name=layer,Order=order,Calibration=calibration,MinX=minX,MinZ=minZ,MaxX=maxX,MaxZ=maxZ});
        }
        _layers.Sort((a,b)=>a.Order!=b.Order?a.Order.CompareTo(b.Order):string.CompareOrdinal(a.Key,b.Key));
        return _layers;
    }

    // ---- calibration from the live native projection ----
    private void Calibrate(MapTools map,float now)
    {
        var ui=map.Ui;
        var page=ui.CurrentMapDefinition;
        var view=ui._map;
        var mapRect=view!=null?view._rectTransform:null;
        if(page==null||view==null||mapRect==null)
        {Note("no-ui","page="+(page!=null)+" view="+(view!=null)+" mapRect="+(mapRect!=null));return;}
        string name=page.name??"";
        if(name.Length==0){Note("page-name");return;}
        // Every layer of this atlas instance already agrees with the stored calibration.
        if(name==_settledPage&&ui.Pointer==_settledUi)return;
        var arts=CollectArt(ui._mapBackground,mapRect,name);
        if(arts.Count==0){Note("no-art",name+" background="+(ui._mapBackground!=null&&ui._mapBackground.sprite!=null));return;}
        Vector3 origin=map.HasPlayerPosition?map.PlayerPosition:Vector3.zero;
        double x0=origin.x,z0=origin.z;
        var solved=new M27MiniMapPolicy.Affine[arts.Count];var ok=new bool[arts.Count];
        string failure="";
        // 100 m first, 20 m when the far samples leave the page (native clamping).
        for(int attempt=0;attempt<2;attempt++)
        {
            double span=attempt==0?SampleSpan:FineSpan;
            if(!Canvas(view,mapRect,x0,z0,out var p0)||!Canvas(view,mapRect,x0+span,z0,out var p1)||
               !Canvas(view,mapRect,x0,z0+span,out var p2)||!Canvas(view,mapRect,x0+span,z0+span,out var p3))
            {failure="projection span="+F(span);continue;}
            for(int i=0;i<arts.Count;i++)
            {
                if(ok[i])continue;
                var layout=arts[i].Layout;
                if(!layout.Uv(p0,out double u0,out double v0)||!layout.Uv(p1,out double u1,out double v1)||
                   !layout.Uv(p2,out double u2,out double v2)||!layout.Uv(p3,out double u3,out double v3))
                {failure=arts[i].Name+": uv span="+F(span);continue;}
                if(!M27MiniMapPolicy.TrySolve(x0,z0,u0,v0,x0+span,z0,u1,v1,x0,z0+span,u2,v2,out var affine))
                {failure=arts[i].Name+": solve span="+F(span)+" uv="+F(u0)+","+F(v0)+" "+F(u1)+","+F(v1)+" "+F(u2)+","+F(v2);continue;}
                double residual=M27MiniMapPolicy.Residual(affine,x0+span,z0+span,u3,v3);
                if(residual>M27MiniMapPolicy.MaxResidual){failure=arts[i].Name+": residual="+F(residual)+" span="+F(span);continue;}
                solved[i]=affine;ok[i]=true;
            }
        }
        int committed=0,settled=0,failed=0;
        var keys=new HashSet<string>(StringComparer.Ordinal);
        var details=new System.Text.StringBuilder();
        for(int i=0;i<arts.Count;i++)
        {
            var art=arts[i];var texture=art.Texture;
            keys.Add(art.Key);
            if(!ok[i]){failed++;continue;}
            var affine=solved[i];
            _layerTextures[art.Key]=texture;
            // Two agreeing solutions some time apart on the same art: the native layout is settled.
            if(!_candidates.TryGetValue(art.Key,out var candidate)||candidate.Texture!=texture.Pointer||
               !M27MiniMapPolicy.Consistent(candidate.Map,affine,x0,z0,CheckSpan))
            {
                if(candidate!=null&&candidate.Texture==texture.Pointer)Note("unsettled",art.Key+" "+Describe(affine));
                _candidates[art.Key]=new Candidate{Map=affine,Texture=texture.Pointer,At=now};
                continue;
            }
            if(now-candidate.At<CommitGap)continue;
            string textureName=texture.name??"";
            if(_calibrations.TryGetValue(art.Key,out var known)&&known.Texture==textureName&&known.Width==texture.width&&
               known.Height==texture.height&&M27MiniMapPolicy.Consistent(known.Map,affine,x0,z0,CheckSpan)){settled++;continue;}
            _calibrations[art.Key]=new M27MiniMapPolicy.Calibration{Page=art.Key,Texture=textureName,Width=texture.width,Height=texture.height,Map=affine};
            _captureRetry.Remove(art.Key);
            string key=art.Key;
            _captures.RemoveAll(c=>c.Key==key);
            committed++;
            details.Append(' ').Append(art.Name).Append('(').Append(textureName).Append(' ')
                .Append(F(M27MiniMapPolicy.TexelsPerMetre(affine,texture.width,texture.height))).Append(" t/m ").Append(Describe(affine)).Append(')');
        }
        if(failed>0)Note("sample",name+" failed="+failed+"/"+arts.Count+" "+failure);
        // Layers this page no longer shows are dropped once every shown layer solved.
        int removed=0;
        if(failed==0)
            foreach(var key in new List<string>(_calibrations.Keys))
                if(PageOf(key)==name&&!keys.Contains(key)){_calibrations.Remove(key);removed++;}
        if(committed>0||removed>0)
        {
            Save();
            _layersPage=null;
            if(name==_pageName)_dirty=true;
            Note("committed",name+" layers="+committed+"/"+arts.Count+" removed="+removed+details);
            Status="Рельеф: страница «"+name+"» откалибрована по карте игры · слоёв "+CountFor(name);
        }
        else if(failed==0&&settled==arts.Count)
        {
            _settledPage=name;_settledUi=ui.Pointer;
            Note("calibrated",name+" layers="+settled);
        }
    }
    // Canvas (world-space UI) point of a world position through the native projection.
    private static bool Canvas(NativeMap view,RectTransform mapRect,double x,double z,out Vector3 point)
    {
        point=mapRect.TransformPoint(view.GetMapPosition(new Vector3((float)x,0f,(float)z),mapRect));
        return float.IsFinite(point.x)&&float.IsFinite(point.y)&&float.IsFinite(point.z);
    }
    // The page background picture (layer 0) and the zone pictures under the map content.
    private static List<Art> CollectArt(UiImage background,RectTransform mapRect,string page)
    {
        var arts=new List<Art>();
        IntPtr skip=IntPtr.Zero;
        if(background!=null)
        {
            skip=background.Pointer;
            if(background.isActiveAndEnabled&&background.sprite!=null)
            {
                var art=ArtFor(background,M27MiniMapPolicy.LayerKey(page,0,BackgroundLayer),BackgroundLayer);
                if(art!=null)arts.Add(art);
            }
        }
        var zones=new List<UiImage>();bool tagged=false;
        foreach(var image in mapRect.GetComponentsInChildren<UiImage>(false))
        {
            if(image==null||image.Pointer==skip||!IsZoneArt(image,mapRect))continue;
            zones.Add(image);
            tagged|=ZoneMaterial(image);
        }
        // Zone art uses the MapZone material: when present it alone decides.
        int order=1;
        foreach(var image in zones)
        {
            if(tagged&&!ZoneMaterial(image))continue;
            if(order>99)break;
            string layer=image.gameObject.name??"";
            var art=ArtFor(image,M27MiniMapPolicy.LayerKey(page,order++,layer),layer);
            if(art!=null)arts.Add(art);
        }
        return arts;
    }
    private static bool ZoneMaterial(UiImage image)
    {
        var material=image.material;
        string name=material!=null?material.name??"":"";
        return name.StartsWith("MapZone",StringComparison.Ordinal);
    }
    // Big enabled pictures; markers, icons and fog overlays (FogMapElement) are not terrain.
    private static bool IsZoneArt(UiImage image,RectTransform mapRect)
    {
        if(!image.enabled)return false;
        var sprite=image.sprite;var texture=sprite!=null?sprite.texture:null;
        if(texture==null||Math.Max(texture.width,texture.height)<256)return false;
        var rectTransform=image.rectTransform;
        if(rectTransform==null)return false;
        var rect=rectTransform.rect;var lossy=rectTransform.lossyScale;var parent=mapRect.lossyScale;
        double sx=Math.Abs(parent.x)>1e-6f?Math.Abs(lossy.x/parent.x):1,sy=Math.Abs(parent.y)>1e-6f?Math.Abs(lossy.y/parent.y):1;
        if(!(Math.Min(rect.width*sx,rect.height*sy)>=64))return false;
        return image.GetComponentInParent<FogMapElement>()==null;
    }
    private static Art ArtFor(UiImage image,string key,string name)
    {
        var sprite=image.sprite;var texture=sprite!=null?sprite.texture:null;
        var rectTransform=image.rectTransform;
        if(texture==null||texture.width<=0||texture.height<=0||rectTransform==null)return null;
        var rect=rectTransform.rect;
        if(!(rect.width>1f)||!(rect.height>1f))return null;
        var spriteRect=sprite.rect;var pivot=rectTransform.pivot;
        if(!(spriteRect.width>0f)||!(spriteRect.height>0f))return null;
        // Unity Image (Simple): the whole sprite rect maps onto the (fitted) rect; a tight
        // sprite draws only its trimmed textureRect, which sits at textureRectOffset.
        double originX=spriteRect.x,originY=spriteRect.y;
        try{var trimmed=sprite.textureRect;var offset=sprite.textureRectOffset;originX=trimmed.x-offset.x;originY=trimmed.y-offset.y;}
        catch(Exception){}
        M27MiniMapPolicy.FitRect(rect.width,rect.height,spriteRect.width,spriteRect.height,image.preserveAspect,
            pivot.x,pivot.y,out double fx,out double fy,out double fw,out double fh);
        return new Art{Key=key,Name=name,Texture=texture,Layout=new Layout{ImageRect=rectTransform,Rect=rect,OriginX=originX,OriginY=originY,
            SpriteWidth=spriteRect.width,SpriteHeight=spriteRect.height,TextureWidth=texture.width,TextureHeight=texture.height,X=fx,Y=fy,W=fw,H=fh}};
    }
    private sealed class Layout
    {
        internal RectTransform ImageRect;
        internal Rect Rect;
        internal double OriginX,OriginY,SpriteWidth,SpriteHeight;
        internal int TextureWidth,TextureHeight;
        internal double X,Y,W,H;
        // Canvas point -> normalised texture UV of this Image (v up).
        internal bool Uv(Vector3 canvas,out double u,out double v)
        {
            u=v=0;
            if(!(W>0)||!(H>0)||TextureWidth<=0||TextureHeight<=0)return false;
            Vector3 local=ImageRect.InverseTransformPoint(canvas);
            double nx=(local.x-Rect.x)/Rect.width,ny=(local.y-Rect.y)/Rect.height;
            double sx=(nx-X)/W,sy=(ny-Y)/H;
            u=(OriginX+sx*SpriteWidth)/TextureWidth;
            v=(OriginY+sy*SpriteHeight)/TextureHeight;
            return double.IsFinite(u)&&double.IsFinite(v);
        }
    }

    // ---- composite (<=15 Hz, only after movement) ----
    // M29: the range the current composite was made for (the minimap scales it while the zoom moves).
    internal float ComposedRange{get;private set;}
    private void ComposeView(List<Layer> layers,float now)
    {
        // M29 task 4: the disc size and the zoom chosen in navigation.cfg (shared with MiniMapUi).
        var nav=_app.Navigation;
        float radius=M29MiniMapLayout.Diameter(nav?.Settings.Size??1,Screen.height)*.5f;
        float terrainRadius=radius-6f,markerRadius=radius-14f;
        float range=nav?.Range??MapTools.MiniMapRange;
        int size=M27MiniMapPolicy.CompositeSize(terrainRadius);
        double metresPerTexel=M27MiniMapPolicy.MetresPerTexel(range,markerRadius,terrainRadius,size);
        if(!(metresPerTexel>0))return;
        if(size!=_size)
        {
            if(_texture!=null)UnityEngine.Object.Destroy(_texture);
            _texture=null;_composite=null;_size=size;_composed=false;
        }
        bool settled=_composed&&!_dirty&&_composedPage==_pageName,zoomed=M29MiniMapZoom.Stale(ComposedRange,range);
        // While the zoom moves the old composite is drawn scaled; a new one at most at the composite rate.
        if(settled&&zoomed&&now-_composedAt<M27MiniMapPolicy.ComposeInterval)return;
        bool fresh=settled&&!zoomed;
        if(!M27MiniMapPolicy.NeedsCompose(fresh,_composedX,_composedZ,Player.x,Player.z,metresPerTexel,now,_composedAt))return;
        // Only layers under the disc are read back and painted, one readback per frame.
        double half=size*.5*metresPerTexel;
        _sources.Clear();
        bool readBack=false;int missing=0;string missingName="";
        foreach(var layer in layers)
        {
            if(!M27MiniMapPolicy.Overlaps(layer.MinX,layer.MinZ,layer.MaxX,layer.MaxZ,Player.x,Player.z,half))continue;
            var capture=Cached(layer.Key,now);
            if(capture==null)
            {
                if(_captureRetry.TryGetValue(layer.Key,out float retry)&&now<retry){missing++;missingName=layer.Name;continue;}
                if(readBack){_dirty=true;_sources.Clear();return;}
                readBack=true;
                capture=CaptureFor(layer,layers,now);
                if(capture==null){missing++;missingName=layer.Name;continue;}
            }
            _sources.Add(new M27MiniMapPolicy.Source(capture.Pixels,capture.Width,capture.Height,layer.Calibration.Map));
        }
        if(_composite==null||_composite.Length!=size*size)_composite=new uint[size*size];
        var watch=Stopwatch.StartNew();
        M27MiniMapPolicy.ComposeLayers(_sources,Player.x,Player.z,metresPerTexel,_composite,size,Background);
        Upload(size);
        watch.Stop();
        Layers=_sources.Count;_sources.Clear();
        _composed=true;_dirty=false;_composedPage=_pageName;_composedX=Player.x;_composedZ=Player.z;_composedAt=now;ComposedRange=range;
        CenterX=Player.x;CenterZ=Player.z;
        Composes++;LastComposeMs=(float)watch.Elapsed.TotalMilliseconds;
        string ms=LastComposeMs.ToString("0.00",CultureInfo.InvariantCulture);
        Status=Layers==0?"Рельеф: здесь на странице «"+_pageName+"» нет рисунка":
            "Рельеф: страница «"+_pageName+"» · слоёв "+Layers+" · "+size+"×"+size+" · сборка "+ms+" мс";
        if(missing>0)Status+=" · слой «"+missingName+"» не загружен — откройте карту игры";
    }
    private Capture Cached(string key,float now)
    {
        foreach(var cached in _captures)
            if(cached.Key==key){cached.UsedAt=now;return cached;}
        return null;
    }

    // ---- layer texture copy (once per layer and session) ----
    private Capture CaptureFor(Layer layer,List<Layer> layers,float now)
    {
        var calibration=layer.Calibration;
        var texture=TextureFor(layer,layers);
        if(texture==null){_captureRetry[layer.Key]=now+RetrySeconds;return null;}
        if(texture.name!=calibration.Texture||texture.width!=calibration.Width||texture.height!=calibration.Height)
        {
            // Other art than the calibrated one (another game build or display variant):
            // never composed; the next atlas visit on this page recalibrates (overwrites).
            _captureRetry[layer.Key]=now+RetrySeconds;
            Status="Рельеф: страница изменилась, откройте карту для повторной калибровки";
            return null;
        }
        try
        {
            var capture=Read(texture,LayerCapture);
            capture.Key=layer.Key;capture.UsedAt=now;
            // Least recently used first; never a layer the current frame already uses.
            while(_captures.Count>=MaxCaptures)
            {
                int oldest=-1;
                for(int i=0;i<_captures.Count;i++)
                    if(_captures[i].UsedAt<now&&(oldest<0||_captures[i].UsedAt<_captures[oldest].UsedAt))oldest=i;
                if(oldest<0)break;
                _captures.RemoveAt(oldest);
            }
            _captures.Add(capture);
            return capture;
        }
        catch(Exception e)
        {
            _captureRetry[layer.Key]=now+RetrySeconds;
            Status="Рельеф: не удалось прочитать слой карты: "+e.Message;
            _app.LoggerInstance.Warning("Minimap layer read failed layer="+layer.Key+": "+e);
            return null;
        }
    }
    private static bool Alive(Texture2D texture)=>texture!=null&&!texture.WasCollected;
    // The art seen while calibrating first, then the page definition's background,
    // then (after a restart) already loaded textures: searched once per page and session.
    private Texture2D TextureFor(Layer layer,List<Layer> layers)
    {
        if(_layerTextures.TryGetValue(layer.Key,out var shown)&&Alive(shown))return shown;
        if(layer.Name.Length==0||layer.Name==BackgroundLayer)
        {
            var sprite=_page?.MapBackground?.Image;var own=sprite!=null?sprite.texture:null;
            if(Alive(own)&&own.name==layer.Calibration.Texture)return own;
        }
        if(_searchedPages.Add(_pageName))Search(layers);
        return _layerTextures.TryGetValue(layer.Key,out shown)&&Alive(shown)?shown:null;
    }
    private void Search(List<Layer> layers)
    {
        var wanted=new Dictionary<string,List<Layer>>(StringComparer.Ordinal);
        foreach(var layer in layers)
        {
            if(_layerTextures.TryGetValue(layer.Key,out var known)&&Alive(known))continue;
            string name=layer.Calibration.Texture??"";
            if(name.Length==0)continue;
            if(!wanted.TryGetValue(name,out var list))wanted[name]=list=new List<Layer>();
            list.Add(layer);
        }
        if(wanted.Count==0)return;
        int found=0;
        try
        {
            foreach(var texture in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if(texture==null)continue;
                string name=texture.name;
                if(string.IsNullOrEmpty(name)||!wanted.TryGetValue(name,out var list))continue;
                foreach(var layer in list)
                    if(texture.width==layer.Calibration.Width&&texture.height==layer.Calibration.Height){_layerTextures[layer.Key]=texture;found++;}
            }
        }
        catch(Exception e){_app.LoggerInstance.Warning("Minimap layer search failed: "+e.Message);}
        Note("search",_pageName+" found="+found+" wanted="+wanted.Count);
    }
    private static Capture Read(Texture2D texture,int max)
    {
        M27MiniMapPolicy.CaptureSize(texture.width,texture.height,Math.Min(max,M27MiniMapPolicy.MaxCapture),out int width,out int height);
        if(width<=0||height<=0)throw new InvalidOperationException("пустая текстура");
        var previous=RenderTexture.active;
        var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        Texture2D readable=null;
        try
        {
            // The page art is not CPU-readable: one GPU blit + readback.
            Graphics.Blit(texture,target);
            RenderTexture.active=target;
            readable=new Texture2D(width,height,TextureFormat.RGBA32,false);
            readable.ReadPixels(new Rect(0,0,width,height),0,0,false);
            var pixels=readable.GetPixels32();
            var managed=new uint[width*height];
            Copy(pixels,managed);
            return new Capture{Texture=texture.name??"",Pixels=managed,Width=width,Height=height};
        }
        finally
        {
            RenderTexture.active=previous;
            RenderTexture.ReleaseTemporary(target);
            if(readable!=null)UnityEngine.Object.Destroy(readable);
        }
    }
    // Il2Cpp array: header (klass, monitor, bounds, max_length) then the elements.
    private static unsafe void Copy(Il2CppStructArray<Color32> pixels,uint[] target)
    {
        if(pixels==null||pixels.Length<target.Length)throw new InvalidOperationException("неполные пиксели страницы");
        IntPtr native=pixels.Pointer;
        if(native!=IntPtr.Zero&&Marshal.ReadIntPtr(native,3*IntPtr.Size).ToInt64()==pixels.Length)
        {
            long bytes=(long)target.Length*4;
            fixed(uint* destination=target)Buffer.MemoryCopy((void*)IntPtr.Add(native,4*IntPtr.Size),destination,bytes,bytes);
            return;
        }
        for(int i=0;i<target.Length;i++){var c=pixels[i];target[i]=(uint)(c.r|c.g<<8|c.b<<16|c.a<<24);}
    }
    private unsafe void Upload(int size)
    {
        if(_texture==null)
        {
            _texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
            _texture.name="CreatorToolkit.MiniMapTerrain";
            _texture.hideFlags=HideFlags.HideAndDontSave;
            _texture.filterMode=FilterMode.Bilinear;
            _texture.wrapMode=TextureWrapMode.Clamp;
        }
        fixed(uint* data=_composite)_texture.LoadRawTextureData((IntPtr)data,size*size*4);
        _texture.Apply(false);
    }

    // ---- persistence ----
    private string FilePath=>Path.Combine(_app.DataDirectory??"","minimap-pages.txt");
    private void Load()
    {
        _loaded=true;
        try
        {
            if(string.IsNullOrEmpty(_app.DataDirectory)||!File.Exists(FilePath))return;
            foreach(var line in File.ReadAllLines(FilePath))
                if(M27MiniMapPolicy.TryParse(line,out var calibration))_calibrations[calibration.Page]=calibration;
            _layersPage=null;
        }
        catch(Exception e){_app.LoggerInstance.Warning("Minimap calibrations not loaded: "+e.Message);}
    }
    private void Save()
    {
        try
        {
            if(string.IsNullOrEmpty(_app.DataDirectory))return;
            Directory.CreateDirectory(_app.DataDirectory);
            var lines=new List<string>();
            foreach(var calibration in _calibrations.Values)lines.Add(M27MiniMapPolicy.Serialize(calibration));
            string temp=FilePath+".tmp";
            File.WriteAllLines(temp,lines);
            File.Copy(temp,FilePath,true);
            File.Delete(temp);
        }
        catch(Exception e){_app.LoggerInstance.Warning("Minimap calibrations not saved: "+e.Message);}
    }

    // ---- acceptance diagnostics (read-only: m27-mapprobe, m27-minimapdump) ----
    internal string Probe(MapTools map)
    {
        if(map==null||!map.NativeMapOpen)return "native map is not open";
        var ui=map.Ui;var sb=new System.Text.StringBuilder(4096);
        string Safe(Func<string> text){try{return text();}catch(Exception e){return "<"+e.GetType().Name+": "+e.Message+">";}}
        string SpriteText(Sprite s)=>s==null?"null":Safe(()=>s.name+" rect="+s.rect+" tex="+
            (s.texture!=null?s.texture.name+" "+s.texture.width+"x"+s.texture.height:"null")+
            " textureRect="+Safe(()=>s.textureRect.ToString())+" offset="+Safe(()=>s.textureRectOffset.ToString())+" packed="+Safe(()=>s.packed+"/"+s.packingMode));
        string ImageText(UiImage i)=>i==null?"null":Safe(()=>"active="+i.gameObject.activeInHierarchy+" enabled="+i.enabled+" type="+i.type+
            " preserve="+i.preserveAspect+" rect="+i.rectTransform.rect+" pivot="+i.rectTransform.pivot+" lossy="+i.rectTransform.lossyScale+" color="+i.color+
            " material="+(i.material!=null?i.material.name+"/"+(i.material.shader!=null?i.material.shader.name:"null"):"null")+" sprite="+SpriteText(i.sprite));
        string PathOf(Transform t){var parts=new List<string>();for(int n=0;t!=null&&n<12&&t!=ui.transform;n++,t=t.parent)parts.Insert(0,t.name);return string.Join("/",parts);}
        var page=ui.CurrentMapDefinition;
        sb.Append("page=").Append(page!=null?page.name:"null").Append(" pageSprite=").Append(Safe(()=>SpriteText(page?.MapBackground?.Image)))
          .Append(" pageOverlay=").Append(Safe(()=>SpriteText(page?.MapBackgroundOverlay?.Image))).Append('\n');
        sb.Append("background=").Append(ImageText(ui._mapBackground)).Append('\n');
        sb.Append("overlay=").Append(ImageText(ui._mapBackgroundOverlay)).Append('\n');
        var view=ui._map;var mapRect=view!=null?view._rectTransform:null;
        sb.Append("mapRect=").Append(Safe(()=>mapRect!=null?mapRect.rect+" lossy="+mapRect.lossyScale+" path="+PathOf(mapRect):"null")).Append('\n');
        var group=SystemContext.Instance?.SceneLoader?.CurrentSceneGroup;
        var playerPage=group!=null?map.MapForGroup(group):null;
        sb.Append("group=").Append(group!=null?group.name:"null").Append(" playerPage=").Append(playerPage!=null?playerPage.name:"null")
          .Append(" player=").Append(map.HasPlayerPosition?map.PlayerPosition.ToString("F1"):"none").Append('\n');
        // The native player marker must sit where the projection puts the player.
        if(view!=null&&mapRect!=null&&map.HasPlayerPosition)
        {
            var p=map.PlayerPosition;
            sb.Append("playerCanvas=").Append(Safe(()=>Canvas(view,mapRect,p.x,p.z,out var c)?c.ToString("F2"):"none"))
              .Append(" marker=").Append(Safe(()=>{var m=ui.GetComponentInChildren<PlayerMapMarker>(true);return m!=null?m.transform.position.ToString("F2")+" active="+m.gameObject.activeInHierarchy:"null";}))
              .Append('\n');
            List<Art> arts;
            try{arts=CollectArt(ui._mapBackground,mapRect,page!=null?page.name??"":"");}
            catch(Exception e){arts=new List<Art>();sb.Append("art-error ").Append(e.GetType().Name).Append(' ').Append(e.Message).Append('\n');}
            foreach(var art in arts)
            {
                var l=art.Layout;
                string uv=Safe(()=>Canvas(view,mapRect,p.x,p.z,out var c)&&l.Uv(c,out double u,out double v)?F(u)+","+F(v):"none");
                sb.Append("art ").Append(art.Key).Append(" tex=").Append(art.Texture.name).Append(' ').Append(l.TextureWidth).Append('x').Append(l.TextureHeight)
                  .Append(" fit=").Append(F(l.X)).Append(',').Append(F(l.Y)).Append(',').Append(F(l.W)).Append(',').Append(F(l.H))
                  .Append(" origin=").Append(F(l.OriginX)).Append(',').Append(F(l.OriginY)).Append(" sprite=").Append(F(l.SpriteWidth)).Append('x').Append(F(l.SpriteHeight))
                  .Append(" playerUv=").Append(uv).Append('\n');
            }
        }
        int images=0;
        foreach(var image in ui.GetComponentsInChildren<UiImage>(true))
        {
            if(image==null)continue;
            var sprite=image.sprite;var texture=sprite!=null?sprite.texture:null;
            if(texture==null||Math.Max(texture.width,texture.height)<1024)continue;
            sb.Append("image ").Append(Safe(()=>PathOf(image.transform))).Append(' ').Append(ImageText(image)).Append('\n');
            if(++images>=40)break;
        }
        int fogs=0;
        foreach(var fog in ui.GetComponentsInChildren<FogMapElement>(true))
        {
            if(fog==null)continue;
            sb.Append("fog ").Append(Safe(()=>PathOf(fog.transform)+" active="+fog.gameObject.activeInHierarchy+" alpha="+(fog._canvasGroup!=null?F(fog._canvasGroup.alpha):"none"))).Append('\n');
            if(++fogs>=20)break;
        }
        sb.Append("calibrations=").Append(_calibrations.Count).Append(" layersInView=").Append(Layers).Append(" cached=").Append(_captures.Count)
          .Append(" note=").Append(CalibrationNote).Append('\n');
        foreach(var calibration in _calibrations.Values)sb.Append("calibration ").Append(M27MiniMapPolicy.Serialize(calibration)).Append('\n');
        return sb.ToString();
    }
    // PNG copies (north up) of the last composite and of every cached layer readback.
    internal string Dump(string directory)
    {
        Directory.CreateDirectory(directory);
        var written=new List<string>();
        if(_composite!=null&&_size>0&&_composite.Length>=_size*_size)
        {WritePng(Path.Combine(directory,"m27-minimap.png"),_composite,_size,_size);written.Add("m27-minimap.png");}
        foreach(var capture in _captures)
        {
            var name=new System.Text.StringBuilder("m27-layer-");
            foreach(char c in capture.Key)name.Append(char.IsLetterOrDigit(c)?c:'_');
            string file=name.Append(".png").ToString();
            WritePng(Path.Combine(directory,file),capture.Pixels,capture.Width,capture.Height);
            written.Add(file+" "+capture.Width+"x"+capture.Height);
        }
        return written.Count>0?string.Join("; ",written):"nothing composed or cached";
    }
    private static unsafe void WritePng(string path,uint[] pixels,int width,int height)
    {
        var texture=new Texture2D(width,height,TextureFormat.RGBA32,false);
        try
        {
            fixed(uint* data=pixels)texture.LoadRawTextureData((IntPtr)data,width*height*4);
            texture.Apply(false);
            var png=ImageConversion.EncodeToPNG(texture);
            var bytes=new byte[png.Length];
            IntPtr native=png.Pointer;
            if(native!=IntPtr.Zero&&Marshal.ReadIntPtr(native,3*IntPtr.Size).ToInt64()==png.Length)
                Marshal.Copy(IntPtr.Add(native,4*IntPtr.Size),bytes,0,bytes.Length);
            else for(int i=0;i<bytes.Length;i++)bytes[i]=png[i];
            File.WriteAllBytes(path,bytes);
        }
        finally{UnityEngine.Object.Destroy(texture);}
    }
}
