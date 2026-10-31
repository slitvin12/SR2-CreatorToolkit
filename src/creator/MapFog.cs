using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using FogMapElement = Il2CppMonomiPark.SlimeRancher.UI.Map.FogMapElement;
using UiImage = UnityEngine.UI.Image;
using EventDirector = Il2CppMonomiPark.SlimeRancher.Event.EventDirector;
using SceneContext = Il2Cpp.SceneContext;

namespace CreatorToolkit;

// 0.8.1 (mockups 05/06): no atlas icons under unexplored fog. The native atlas paints its fog
// as FogMapElement pictures (zone_fog_areas/map_fog_*) whose CanvasGroup alpha drops to 0 once
// the area is revealed. A point is fogged when a still-visible fog picture covers its map
// position with an opaque pixel. The heavy parts run rarely: a world -> picture affine map per
// picture (three native projections; invariant under pan and zoom, both live under the same map
// content), one GPU readback of each fog texture (<=256 px alpha mask, kept for the process) and
// a per-point answer cached until a fog picture changes state. Nothing runs while the native
// atlas is closed; a failed readback leaves that picture transparent (icons stay visible).
internal sealed class MapFogMask
{
    private sealed class Mask{internal byte[] Alpha;internal int Width,Height;}
    private sealed class Piece
    {
        internal FogMapElement Fog;internal CanvasGroup Group;internal UiImage Image;internal string Name="";
        internal float Ox,Oy,Xx,Xy,Zx,Zy;      // picture-local = O + X*x + Z*z (world metres)
        internal Rect Local;                    // the picture rect in its own local space
        internal float U0,V0,Us,Vs;             // the sprite window inside its texture (0..1)
        internal Mask Mask;internal bool Fogged,Revealed;internal int Hits;
    }
    internal const byte Opaque=128;
    private readonly List<Piece> _pieces=new();
    private static readonly Dictionary<IntPtr,Mask> Masks=new();
    private readonly Dictionary<MapTools.Point,bool> _answers=new();
    private IntPtr _ui,_page,_content;
    private float _nextCheck,_nextScan;
    internal int Version{get;private set;}
    internal int FoggedPieces{get;private set;}
    internal int MaskFailures{get;private set;}
    internal int PieceCount=>_pieces.Count;
    internal string Status{get;private set;}="туман: атлас не открыт";

    internal void Tick(MapTools map)
    {
        var ui=map.Ui;
        if(ui==null||!map.NativeMapOpen||ui._map==null){Forget();return;}
        var view=ui._map;var content=view._rectTransform;
        if(content==null){Forget();return;}
        IntPtr page=ui.CurrentMapDefinition?.Pointer??IntPtr.Zero;
        float now=Time.unscaledTime;
        // A page switch swaps the map content; its fog pictures may arrive a few frames later.
        if(ui.Pointer!=_ui||page!=_page||content.Pointer!=_content||(_pieces.Count==0&&now>=_nextScan))
        {
            _ui=ui.Pointer;_page=page;_content=content.Pointer;_nextScan=now+1f;
            Rebuild(view,content);_nextCheck=0;
        }
        if(now<_nextCheck)return;
        _nextCheck=now+.5f;
        bool changed=false;int fogged=0;
        EventDirector director=null;
        try{director=ui._fogReveal?._eventDirector;}catch{}
        if(director==null)try{director=SceneContext.Instance?.eventDirector;}catch{}
        foreach(var piece in _pieces)
        {
            piece.Revealed=Revealed(piece,director);
            bool visible=!piece.Revealed&&Visible(piece);
            if(visible!=piece.Fogged){piece.Fogged=visible;changed=true;}
            if(visible)fogged++;
        }
        FoggedPieces=fogged;
        if(changed){_answers.Clear();Version++;}
        Status="туман: картинок "+_pieces.Count+" · под туманом "+fogged+" · масок "+CountMasks()+(MaskFailures>0?" · сбоев чтения "+MaskFailures:"");
    }
    private void Forget()
    {
        if(_pieces.Count>0||_answers.Count>0){_pieces.Clear();_answers.Clear();Version++;}
        _ui=_page=_content=IntPtr.Zero;FoggedPieces=0;
        Status="туман: атлас не открыт";
    }
    private int CountMasks(){int n=0;foreach(var p in _pieces)if(p.Mask!=null)n++;return n;}
    // The fog art stays opaque in some builds after its event fired; the save's reveal event is
    // the authority (the same check the native MapUIFogReveal makes when it opens a page).
    private static bool Revealed(Piece piece,EventDirector director)
    {
        if(director==null)return false;
        try{return piece.Fog!=null&&!piece.Fog.WasCollected&&piece.Fog.ShouldBeRevealed(director);}
        catch{return false;}
    }
    private static bool Visible(Piece piece)
    {
        try
        {
            if(piece.Fog==null||piece.Fog.WasCollected||piece.Image==null||piece.Image.WasCollected)return false;
            if(!piece.Image.isActiveAndEnabled||piece.Image.color.a<.5f)return false;
            return piece.Group==null||piece.Group.WasCollected||piece.Group.alpha>.5f;
        }
        catch{return false;}
    }
    private void Rebuild(Il2CppMonomiPark.SlimeRancher.UI.Map.Map view,RectTransform content)
    {
        _pieces.Clear();_answers.Clear();Version++;
        // Three world samples through the native projection, as world-space UI points.
        Vector3 W(float x,float z)=>content.TransformPoint(view.GetMapPosition(new Vector3(x,0f,z),content));
        Vector3 a,b,c;
        try{a=W(0,0);b=W(1000,0);c=W(0,1000);}
        catch(Exception e){Status="туман: проекция недоступна · "+e.GetType().Name;return;}
        foreach(var fog in content.GetComponentsInChildren<FogMapElement>(true))
        {
            if(fog==null)continue;
            CanvasGroup group=null;
            try{group=fog._canvasGroup;}catch{}
            foreach(var image in fog.GetComponentsInChildren<UiImage>(true))
            {
                var sprite=image!=null?image.sprite:null;var texture=sprite!=null?sprite.texture:null;
                if(texture==null)continue;
                var rt=image.rectTransform;
                Vector3 la=rt.InverseTransformPoint(a),lb=rt.InverseTransformPoint(b),lc=rt.InverseTransformPoint(c);
                var piece=new Piece{Fog=fog,Group=group,Image=image,Name=fog.gameObject.name??"",Local=rt.rect,
                    Ox=la.x,Oy=la.y,Xx=(lb.x-la.x)/1000f,Xy=(lb.y-la.y)/1000f,Zx=(lc.x-la.x)/1000f,Zy=(lc.y-la.y)/1000f};
                if(!float.IsFinite(piece.Ox+piece.Oy+piece.Xx+piece.Xy+piece.Zx+piece.Zy)||piece.Local.width<=0||piece.Local.height<=0)continue;
                Rect window;
                try{window=sprite.textureRect;}catch{window=sprite.rect;}
                float tw=Mathf.Max(1,texture.width),th=Mathf.Max(1,texture.height);
                piece.U0=window.x/tw;piece.V0=window.y/th;piece.Us=window.width/tw;piece.Vs=window.height/th;
                piece.Mask=MaskOf(texture);
                piece.Fogged=false;
                _pieces.Add(piece);
            }
        }
    }
    private Mask MaskOf(Texture texture)
    {
        if(Masks.TryGetValue(texture.Pointer,out var known))return known;
        Mask mask=null;
        var previous=RenderTexture.active;
        int width=Mathf.Clamp(texture.width,1,256),height=Mathf.Clamp(texture.height,1,256);
        var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        Texture2D readable=null;
        try
        {
            // Fog art is not CPU-readable: one GPU blit + readback of the alpha channel.
            Graphics.Blit(texture,target);
            RenderTexture.active=target;
            readable=new Texture2D(width,height,TextureFormat.RGBA32,false);
            readable.ReadPixels(new Rect(0,0,width,height),0,0,false);
            var pixels=readable.GetPixels32();
            var alpha=new byte[width*height];
            for(int i=0;i<alpha.Length&&i<pixels.Length;i++)alpha[i]=pixels[i].a;
            mask=new Mask{Alpha=alpha,Width=width,Height=height};
        }
        catch{MaskFailures++;}
        finally
        {
            RenderTexture.active=previous;
            RenderTexture.ReleaseTemporary(target);
            if(readable!=null)UnityEngine.Object.Destroy(readable);
        }
        Masks[texture.Pointer]=mask;
        return mask;
    }
    private static bool Covers(Piece piece,float x,float z)
    {
        var mask=piece.Mask;if(mask==null)return false;
        float lx=piece.Ox+piece.Xx*x+piece.Zx*z,ly=piece.Oy+piece.Xy*x+piece.Zy*z;
        var r=piece.Local;
        if(lx<r.xMin||lx>r.xMax||ly<r.yMin||ly>r.yMax)return false;
        float u=piece.U0+(lx-r.xMin)/r.width*piece.Us,v=piece.V0+(ly-r.yMin)/r.height*piece.Vs;
        int px=Mathf.Clamp((int)(u*mask.Width),0,mask.Width-1),py=Mathf.Clamp((int)(v*mask.Height),0,mask.Height-1);
        return mask.Alpha[py*mask.Width+px]>=Opaque;
    }
    // Under a still-visible fog picture of the open atlas page.
    internal bool Fogged(MapTools.Point point)
    {
        if(point==null||FoggedPieces==0)return false;
        if(_answers.TryGetValue(point,out bool known))return known;
        bool fogged=false;
        foreach(var piece in _pieces)
            if(piece.Fogged&&Covers(piece,point.Position.x,point.Position.z)){fogged=true;piece.Hits++;break;}
        if(_answers.Count<50000)_answers[point]=fogged;
        return fogged;
    }
    private static string Safe(Func<string> f){try{return f();}catch(Exception e){return e.GetType().Name;}}
    private static float ParentAlpha(UiImage image)
    {
        float a=1f;
        foreach(var g in image.GetComponentsInParent<CanvasGroup>(true))if(g!=null&&g.enabled)a*=g.alpha;
        return a;
    }
    // The world metres (x,z) under the picture rect corners (inverse of the affine map).
    private static string WorldBox(Piece p)
    {
        float det=p.Xx*p.Zy-p.Zx*p.Xy;
        if(Mathf.Abs(det)<1e-12f)return "degenerate";
        float minX=float.MaxValue,minZ=float.MaxValue,maxX=float.MinValue,maxZ=float.MinValue;
        var r=p.Local;
        foreach(var c in new[]{new Vector2(r.xMin,r.yMin),new Vector2(r.xMax,r.yMin),new Vector2(r.xMin,r.yMax),new Vector2(r.xMax,r.yMax)})
        {
            float dx=c.x-p.Ox,dy=c.y-p.Oy;
            float x=(dx*p.Zy-p.Zx*dy)/det,z=(p.Xx*dy-dx*p.Xy)/det;
            minX=Mathf.Min(minX,x);maxX=Mathf.Max(maxX,x);minZ=Mathf.Min(minZ,z);maxZ=Mathf.Max(maxZ,z);
        }
        return "x "+minX.ToString("0")+".."+maxX.ToString("0")+" z "+minZ.ToString("0")+".."+maxZ.ToString("0");
    }
    internal string Describe()
    {
        var sb=new StringBuilder(2048);
        sb.Append(Status).Append(" · version ").Append(Version).Append(" · answers ").Append(_answers.Count).Append('\n');
        foreach(var p in _pieces)
            sb.Append(p.Name).Append(" fogged=").Append(p.Fogged).Append(" alpha=").Append(p.Group!=null?p.Group.alpha.ToString("0.00"):"none")
              .Append(" rect=").Append(p.Local.ToString()).Append(" sprite=").Append(p.Image?.sprite?.name)
              .Append(" mask=").Append(p.Mask!=null?p.Mask.Width+"x"+p.Mask.Height:"none")
              .Append(" uv=").Append(p.U0.ToString("0.000")).Append(',').Append(p.V0.ToString("0.000")).Append(' ').Append(p.Us.ToString("0.000")).Append('x').Append(p.Vs.ToString("0.000"))
              .Append(" world0=").Append(p.Ox.ToString("0")).Append(',').Append(p.Oy.ToString("0"))
              .Append(" revealed=").Append(p.Revealed).Append(" hits=").Append(p.Hits)
              .Append(" active=").Append(Safe(()=>p.Image.isActiveAndEnabled.ToString()))
              .Append(" crAlpha=").Append(Safe(()=>p.Image.canvasRenderer.GetAlpha().ToString("0.00")))
              .Append(" parents=").Append(Safe(()=>ParentAlpha(p.Image).ToString("0.00")))
              .Append(" mat=").Append(Safe(()=>p.Image.materialForRendering?.name??"-"))
              .Append(" world=").Append(WorldBox(p)).Append('\n');
        return sb.ToString();
    }
}
