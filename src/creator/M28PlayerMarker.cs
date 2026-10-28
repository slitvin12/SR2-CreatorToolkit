using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using PlayerDisplayOnMap = Il2Cpp.PlayerDisplayOnMap;
using PlayerMapMarker = Il2CppMonomiPark.SlimeRancher.UI.Map.PlayerMapMarker;
using UiGraphic = UnityEngine.UI.Graphic;
using UiImage = UnityEngine.UI.Image;
using UiMask = UnityEngine.UI.Mask;
using UiMaskable = UnityEngine.UI.MaskableGraphic;
using UiRawImage = UnityEngine.UI.RawImage;
using UiRectMask = UnityEngine.UI.RectMask2D;

namespace CreatorToolkit;

// M28: the native map's player marker (Beatrix portrait in a dark ring, pin tail and the
// translucent view cone) on the IMGUI minimap. While the game's atlas is open, every
// visible Image/RawImage under MapUI._playerMarker and its below-markers part (the game
// re-parents it to a lower layer) is read back once per map opening: the sprite rect via
// one GPU blit + readback into an owned texture (Image fill and parent Mask/RectMask2D
// applied on the CPU) and its screen quad. Parts under the rotating transforms are turned
// so the facing points up (the minimap is heading-up). The copy is persisted in
// UserData\CreatorToolkit\player-marker.txt + player-marker-N.png, so the minimap has it
// from the next launch on; until the first capture the old arrow is drawn.
internal sealed class PlayerMarkerTools
{
    private const string FileName="player-marker.txt",Format="ct-player-marker 1";
    private const float CaptureDelay=.6f,RetryEvery=.5f;
    private const int MaxTries=8,MaxCopy=512;
    private sealed class Part
    {
        internal string Name="",Info="";
        internal Texture2D Texture;
        internal int Width,Height;
        internal float X,Y,W,H,Angle;   // GUI px around the marker pivot (capture scale), quad centre/size, clockwise degrees
        internal Color Tint=Color.white;
        internal bool Below,Rotating;
    }
    // GUI px around the marker pivot (y down): bottom-left, top-left, top-right, bottom-right.
    private struct Quad{internal Vector2 C0,C1,C2,C3;}
    private sealed class Clip
    {
        internal Quad Quad;
        internal uint[] Alpha;
        internal int Width,Height;
        internal string Kind="";
    }
    private readonly ToolkitMod _app;
    private readonly List<Part> _parts=new();
    private float _body,_extent,_heading,_arrowAngle;
    private string _source="",_captured="";
    private uint _hash;
    private bool _loaded,_capturedOpen;
    private float _openedAt=-1f,_nextTry;
    private int _tries;
    internal string Status{get;private set;}="Метка игрока: откройте карту игры один раз — значок будет скопирован";
    internal int Parts=>_parts.Count;
    internal int Captures{get;private set;}
    internal int Failures{get;private set;}
    internal string LastNote{get;private set;}="";
    internal PlayerMarkerTools(ToolkitMod app)=>_app=app;

    internal void LateTick()
    {
        if(!_loaded&&!string.IsNullOrEmpty(_app.DataDirectory)){_loaded=true;Load();}
        var map=_app.Map;
        if(!_app.Ready||map==null||!map.NativeMapOpen||map.Busy){_openedAt=-1f;_capturedOpen=false;_tries=0;return;}
        float now=Time.unscaledTime;
        if(_openedAt<0f){_openedAt=now;_nextTry=now+CaptureDelay;}
        if(_capturedOpen||now<_nextTry)return;
        _nextTry=now+RetryEvery;
        bool ok;string note;
        try{ok=TryCapture(out note);}
        catch(Exception e){ok=false;note="ошибка: "+e.Message;}
        if(ok){_capturedOpen=true;_tries=0;LastNote=note;return;}
        LastNote=note;
        if(++_tries>=MaxTries)
        {
            _capturedOpen=true;Failures++;
            Status="Метка игрока: не удалось скопировать с карты игры — "+note+(_parts.Count>0?" (показана прежняя копия)":"");
            _app.LoggerInstance.Warning("[M28] player marker capture failed: "+note);
        }
    }
    internal void RequestCapture(){_capturedOpen=false;_tries=0;_nextTry=0f;}
    internal void Reset(){_openedAt=-1f;_capturedOpen=false;_tries=0;}
    internal void Forget()
    {
        DestroyAll(_parts);_parts.Clear();_hash=0;_body=_extent=0;_captured="";
        try
        {
            string dir=_app.DataDirectory;
            if(!string.IsNullOrEmpty(dir))
            {
                string file=Path.Combine(dir,FileName);if(File.Exists(file))File.Delete(file);
                foreach(var png in Directory.GetFiles(dir,"player-marker-*.png"))File.Delete(png);
            }
        }
        catch(Exception e){_app.LoggerInstance.Warning("[M28] player marker forget: "+e.Message);}
        Status="Метка игрока: копия удалена — откройте карту игры, чтобы снять заново";
        RequestCapture();
    }

    // Minimap drawing (Repaint only, GUI.matrix identity on entry). below: the view cone
    // layer the game draws under the other markers. turn (M29 north-up minimap): clockwise
    // screen degrees for the rotating parts (facing arrow, view cone) around the centre.
    internal bool Draw(Vector2 centre,float radius,bool below,float turn=0f)
    {
        if(_parts.Count==0||Event.current==null||Event.current.type!=EventType.Repaint)return false;
        float scale=M28PlayerMarkerPolicy.FitScale(_body,_body,M28PlayerMarkerPolicy.TargetSize);
        if(_extent>0&&radius>12f)scale=Math.Min(scale,(radius-6f)/_extent);
        if(!float.IsFinite(scale)||scale<=0)return false;
        bool drew=false;
        var matrix=GUI.matrix;var color=GUI.color;
        try
        {
            foreach(var part in _parts)
            {
                if(part.Below!=below)continue;
                if(part.Texture==null||part.Texture.WasCollected)continue;
                float w=part.W*scale,h=part.H*scale;
                if(!(w>.5f)||!(h>.5f))continue;
                float px=part.X*scale,py=part.Y*scale,angle=part.Angle;
                if(part.Rotating&&turn!=0f&&float.IsFinite(turn)){M28PlayerMarkerPolicy.Rotate(px,py,turn,out px,out py);angle+=turn;}
                var at=new Vector3(centre.x+px,centre.y+py,0f);
                GUI.matrix=matrix*Matrix4x4.TRS(at,Quaternion.Euler(0f,0f,angle),Vector3.one);
                GUI.color=part.Tint;
                GUI.DrawTexture(new Rect(-w*.5f,-h*.5f,w,h),part.Texture);
                drew=true;
            }
        }
        finally{GUI.matrix=matrix;GUI.color=color;}
        return drew;
    }

    private bool TryCapture(out string note)
    {
        note="";
        var marker=_app.Map.Ui?._playerMarker;
        if(marker==null||marker.WasCollected||!marker.gameObject.activeInHierarchy){note="на этой странице карты нет маркера игрока";return false;}
        var canvas=marker.GetComponentInParent<Canvas>();
        var camera=canvas!=null&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
        Vector2 pivot=RectTransformUtility.WorldToScreenPoint(camera,marker.transform.position);
        if(!TryHeading(out float heading,out string source)){note="не удалось определить направление игрока на карте";return false;}
        Transform arrow=marker._arrowRect!=null&&!marker._arrowRect.WasCollected?marker._arrowRect.transform:null;
        Transform below=marker._moveBelowMarkersTransform!=null&&!marker._moveBelowMarkersTransform.WasCollected?marker._moveBelowMarkersTransform.transform:null;
        var roots=new List<Transform>();
        if(below!=null&&!IsUnder(below,marker.transform))roots.Add(below);
        roots.Add(marker.transform);
        var parts=new List<Part>();var seen=new HashSet<IntPtr>();var masks=new Dictionary<IntPtr,Clip>();
        var skipped=new StringBuilder();
        try
        {
            foreach(var root in roots)
            foreach(var graphic in root.GetComponentsInChildren<UiGraphic>(false))
            {
                if(graphic==null||graphic.WasCollected||!seen.Add(graphic.Pointer))continue;
                if(parts.Count>=M28PlayerMarkerPolicy.MaxParts){skipped.Append(graphic.name).Append(": больше ").Append(M28PlayerMarkerPolicy.MaxParts).Append(" частей; ");continue;}
                var part=Read(graphic,camera,pivot,arrow,below,heading,masks,out string why);
                if(part!=null)parts.Add(part);else if(why.Length>0)skipped.Append(why).Append("; ");
            }
        }
        catch(Exception e){DestroyAll(parts);note="ошибка чтения: "+e.Message;return false;}
        if(parts.Count==0){DestroyAll(parts);note="у маркера нет видимых картинок"+(skipped.Length>0?" ("+skipped.ToString().TrimEnd(' ',';')+")":"");return false;}
        float body=0;
        var rootRect=marker._rectTransform!=null&&!marker._rectTransform.WasCollected?marker._rectTransform:marker.transform.TryCast<RectTransform>();
        if(rootRect!=null)
        {
            var r=rootRect.rect;var q=QuadOf(rootRect,new Vector4(r.xMin,r.yMin,r.xMax,r.yMax),camera,pivot);
            body=Math.Max((q.C3-q.C0).magnitude,(q.C1-q.C0).magnitude);
        }
        float extent=0,front=0;
        foreach(var part in parts)
        {
            float reach=part.X*part.X+part.Y*part.Y;
            float half=(float)Math.Sqrt(part.W*part.W+part.H*part.H)*.5f;
            float distance=(float)Math.Sqrt(reach)+half;
            extent=Math.Max(extent,distance);
            if(!part.Below)front=Math.Max(front,Math.Max(part.W,part.H));
        }
        if(body<4f)body=front>0?front:extent*2f;
        DestroyAll(_parts);_parts.Clear();_parts.AddRange(parts);
        _body=body;_extent=extent;_heading=heading;_source=source;
        _arrowAngle=arrow!=null?ScreenAngle(arrow,camera):float.NaN;
        _captured=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture);
        Captures++;
        uint hash=Hash();
        bool saved=false;
        if(hash!=_hash){_hash=hash;saved=Save();}
        note=parts.Count+" частей"+(skipped.Length>0?"; пропущено: "+skipped.ToString().TrimEnd(' ',';'):"");
        Status="Метка игрока: "+parts.Count+" частей скопировано с карты игры "+_captured.Substring(11,5)+(saved?" и сохранено":"");
        _app.LoggerInstance.Msg("[M28] player marker captured: parts="+parts.Count+" body="+F(body)+" extent="+F(extent)+" heading="+F(heading)+" ("+source+") arrow="+F(_arrowAngle)+" saved="+saved+(skipped.Length>0?" skipped="+skipped:""));
        return true;
    }

    private Part Read(UiGraphic graphic,Camera camera,Vector2 pivot,Transform arrow,Transform below,float heading,Dictionary<IntPtr,Clip> masks,out string why)
    {
        why="";
        if(!graphic.enabled)return null;
        var own=graphic.GetComponent<UiMask>();
        if(own!=null&&own.enabled&&!own.showMaskGraphic)return null; // stencil only, never drawn
        float alpha=graphic.color.a*InheritedAlpha(graphic);
        if(alpha<.02f)return null;
        var image=graphic.TryCast<UiImage>();
        var raw=image==null?graphic.TryCast<UiRawImage>():null;
        var rect=graphic.rectTransform.rect;
        var local=new Vector4(rect.xMin,rect.yMin,rect.xMax,rect.yMax);
        Texture source;Rect uv;int width,height;string kind;
        var info=new StringBuilder();
        int type=-1;
        if(image!=null)
        {
            Sprite sprite;
            try{sprite=image.activeSprite;}catch{sprite=image.sprite;}
            if(sprite==null||sprite.WasCollected){why=graphic.name+": без спрайта";return null;}
            source=sprite.texture;
            if(source==null||source.WasCollected||source.width<=0||source.height<=0){why=graphic.name+": спрайт без текстуры";return null;}
            var tr=sprite.textureRect;
            uv=new Rect(tr.x/source.width,tr.y/source.height,tr.width/source.width,tr.height/source.height);
            width=Mathf.RoundToInt(tr.width);height=Mathf.RoundToInt(tr.height);
            type=(int)image.type;
            kind=type switch{0=>"simple",1=>"sliced",2=>"tiled",3=>"filled",_=>"type"+type};
            if(type==0||type==3)
            {
                try{local=image.GetDrawingDimensions(image.preserveAspect);}
                catch{if(image.preserveAspect){M28PlayerMarkerPolicy.FitAspect(rect.width,rect.height,tr.width,tr.height,out float fw,out float fh);
                    local=new Vector4(rect.center.x-fw*.5f,rect.center.y-fh*.5f,rect.center.x+fw*.5f,rect.center.y+fh*.5f);}}
            }
            info.Append(kind).Append(" sprite=").Append(sprite.name).Append(" tex=").Append(source.name).Append(' ').Append(source.width).Append('x').Append(source.height);
            if(type==1||type==2)info.Append(" (9-slice/tile растянут целиком)");
        }
        else if(raw!=null)
        {
            source=raw.texture;
            if(source==null||source.WasCollected||source.width<=0||source.height<=0){why=graphic.name+": RawImage без текстуры";return null;}
            uv=raw.uvRect;kind="raw";
            width=Mathf.Clamp(Mathf.RoundToInt(Math.Abs(uv.width)*source.width),1,MaxCopy);
            height=Mathf.Clamp(Mathf.RoundToInt(Math.Abs(uv.height)*source.height),1,MaxCopy);
            info.Append("raw tex=").Append(source.name).Append(' ').Append(source.width).Append('x').Append(source.height);
        }
        else{why=graphic.name+": "+graphic.GetIl2CppType().Name+" не копируется";return null;}
        if(width<=0||height<=0){why=graphic.name+": пустой спрайт";return null;}
        if(width>MaxCopy||height>MaxCopy)
        {
            float k=Math.Min((float)MaxCopy/width,(float)MaxCopy/height);
            width=Math.Max(1,Mathf.RoundToInt(width*k));height=Math.Max(1,Mathf.RoundToInt(height*k));
            info.Append(" уменьшено до ").Append(width).Append('x').Append(height);
        }
        var quad=QuadOf(graphic.rectTransform,local,camera,pivot);
        uint[] pixels=ReadTexture(source,uv,width,height);
        if(image!=null&&type==3&&image.fillAmount<.999f)
        {
            int method=(int)image.fillMethod,origin=image.fillOrigin;float amount=image.fillAmount;bool clockwise=image.fillClockwise;
            if(method==0||method==1||method==4)
            {
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    float u=(x+.5f)/width,v=(y+.5f)/height;
                    bool visible=method==4?M28PlayerMarkerPolicy.RadialVisible(u,v,amount,origin,clockwise):M28PlayerMarkerPolicy.LinearVisible(u,v,amount,method,origin);
                    if(!visible)pixels[y*width+x]&=0x00FFFFFFu;
                }
                info.Append(" fill=").Append(F(amount)).Append(" method=").Append(method).Append(" origin=").Append(origin);
            }
            else info.Append(" fill method ").Append(method).Append(" не применён");
        }
        var maskable=graphic.TryCast<UiMaskable>();
        if(maskable==null||maskable.maskable)
        {
            for(var t=graphic.transform.parent;t!=null;t=t.parent)
            {
                var mask=t.GetComponent<UiMask>();
                if(mask!=null&&mask.enabled&&mask.graphic!=null&&!mask.graphic.WasCollected)
                {
                    var clip=MaskClip(mask,camera,pivot,masks);
                    if(clip!=null){ApplyClip(pixels,width,height,quad,clip);info.Append(" mask=").Append(t.name).Append('/').Append(clip.Kind);}
                }
                var rectMask=t.GetComponent<UiRectMask>();
                if(rectMask!=null&&rectMask.enabled)
                {
                    var mr=rectMask.rectTransform.rect;
                    var clip=new Clip{Quad=QuadOf(rectMask.rectTransform,new Vector4(mr.xMin,mr.yMin,mr.xMax,mr.yMax),camera,pivot),Kind="rect"};
                    ApplyClip(pixels,width,height,quad,clip);info.Append(" rectmask=").Append(t.name);
                }
                var parentCanvas=t.GetComponent<Canvas>();
                if(parentCanvas!=null&&parentCanvas.isRootCanvas)break;
            }
        }
        // A mirrored quad (negative scale): flip the pixels so the stored quad is upright.
        var e=quad.C3-quad.C0;var f=quad.C1-quad.C0;
        if(e.x*f.y-e.y*f.x>0f)
        {
            for(int y=0;y<height;y++)Array.Reverse(pixels,y*width,width);
            quad=new Quad{C0=quad.C3,C1=quad.C2,C2=quad.C1,C3=quad.C0};
            info.Append(" mirrored");
        }
        bool rotating=arrow!=null&&IsUnder(graphic.transform,arrow)||below!=null&&IsUnder(graphic.transform,below);
        if(rotating)
        {
            var spin=arrow!=null&&IsUnder(graphic.transform,arrow)?arrow:below;
            var sp=RectTransformUtility.WorldToScreenPoint(camera,spin.position);
            var rp=new Vector2(sp.x-pivot.x,pivot.y-sp.y);
            quad=new Quad{C0=Turn(quad.C0,rp,-heading),C1=Turn(quad.C1,rp,-heading),C2=Turn(quad.C2,rp,-heading),C3=Turn(quad.C3,rp,-heading)};
        }
        var centre=(quad.C0+quad.C1+quad.C2+quad.C3)*.25f;
        var up=quad.C1-quad.C0;
        var color=graphic.color;color.a=alpha;
        if(graphic.material!=null&&!graphic.material.WasCollected)info.Append(" material=").Append(graphic.material.name);
        return new Part{Name=graphic.name,Info=info.ToString(),Texture=Upload(pixels,width,height),Width=width,Height=height,
            X=centre.x,Y=centre.y,W=(quad.C3-quad.C0).magnitude,H=up.magnitude,Angle=M28PlayerMarkerPolicy.Normalize(M28PlayerMarkerPolicy.Heading(up.x,up.y)),
            Tint=color,Below=below!=null&&IsUnder(graphic.transform,below),Rotating=rotating};
    }

    private Clip MaskClip(UiMask mask,Camera camera,Vector2 pivot,Dictionary<IntPtr,Clip> cache)
    {
        if(cache.TryGetValue(mask.Pointer,out var cached))return cached;
        Clip clip=null;
        try
        {
            var graphic=mask.graphic;var rect=graphic.rectTransform.rect;
            var local=new Vector4(rect.xMin,rect.yMin,rect.xMax,rect.yMax);
            var image=graphic.TryCast<UiImage>();
            Sprite sprite=null;
            if(image!=null){try{sprite=image.activeSprite;}catch{sprite=image.sprite;}}
            if(image!=null&&sprite!=null&&!sprite.WasCollected&&sprite.texture!=null)
            {
                int type=(int)image.type;
                if(type==0||type==3){try{local=image.GetDrawingDimensions(image.preserveAspect);}catch{}}
                var source=sprite.texture;var tr=sprite.textureRect;
                int w=Mathf.Clamp(Mathf.RoundToInt(tr.width),1,MaxCopy),h=Mathf.Clamp(Mathf.RoundToInt(tr.height),1,MaxCopy);
                clip=new Clip{Quad=QuadOf(graphic.rectTransform,local,camera,pivot),Width=w,Height=h,Kind="sprite "+sprite.name,
                    Alpha=ReadTexture(source,new Rect(tr.x/source.width,tr.y/source.height,tr.width/source.width,tr.height/source.height),w,h)};
            }
            else clip=new Clip{Quad=QuadOf(graphic.rectTransform,local,camera,pivot),Kind="rect"};
        }
        catch(Exception e){_app.LoggerInstance.Warning("[M28] player marker mask "+mask.name+": "+e.Message);}
        cache[mask.Pointer]=clip;
        return clip;
    }

    // Pixels outside the clip quad (or where the mask sprite is transparent) become transparent.
    private static void ApplyClip(uint[] pixels,int width,int height,Quad part,Clip clip)
    {
        Vector2 ex=clip.Quad.C3-clip.Quad.C0,ey=clip.Quad.C1-clip.Quad.C0;
        float det=ex.x*ey.y-ey.x*ex.y;
        if(!float.IsFinite(det)||Math.Abs(det)<1e-6f)return;
        Vector2 px=part.C3-part.C0,py=part.C1-part.C0;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int i=y*width+x;
            if((pixels[i]>>24)==0)continue;
            float u=(x+.5f)/width,v=(y+.5f)/height;
            Vector2 d=part.C0+px*u+py*v-clip.Quad.C0;
            float a=(d.x*ey.y-ey.x*d.y)/det,b=(ex.x*d.y-d.x*ex.y)/det;
            bool inside=a>=0f&&a<=1f&&b>=0f&&b<=1f;
            if(inside&&clip.Alpha!=null)
            {
                int mx=Math.Min(clip.Width-1,(int)(a*clip.Width)),my=Math.Min(clip.Height-1,(int)(b*clip.Height));
                inside=(clip.Alpha[my*clip.Width+mx]>>24)>2u;
            }
            if(!inside)pixels[i]&=0x00FFFFFFu;
        }
    }

    private bool TryHeading(out float degrees,out string source)
    {
        degrees=0;source="";
        var map=_app.Map;var player=_app.Context?.Player;
        if(map==null||player==null)return false;
        Quaternion rotation;Vector3 position;
        PlayerDisplayOnMap display=null;
        try{display=player.GetComponentInChildren<PlayerDisplayOnMap>(true);}catch{}
        if(display!=null&&!display.WasCollected){rotation=display.GetCurrentRotation();position=display.transform.position;source="display";}
        else
        {
            var cam=Camera.main;if(cam==null||cam.WasCollected)return false;
            rotation=cam.transform.rotation;position=player.transform.position;source="camera";
        }
        var forward=rotation*Vector3.forward;forward.y=0;
        if(forward.sqrMagnitude<1e-4f)return false;
        forward.Normalize();
        Vector2 a=map.ScreenPoint(position),b=map.ScreenPoint(position+forward*25f);
        if(a.x<-9999f||b.x<-9999f)return false;
        float dx=b.x-a.x,dy=a.y-b.y; // GUI y grows downwards
        if(dx*dx+dy*dy<1e-4f)return false;
        degrees=M28PlayerMarkerPolicy.Heading(dx,dy);
        return true;
    }

    private static float ScreenAngle(Transform transform,Camera camera)
    {
        var a=RectTransformUtility.WorldToScreenPoint(camera,transform.position);
        var b=RectTransformUtility.WorldToScreenPoint(camera,transform.position+transform.up*10f);
        return M28PlayerMarkerPolicy.Heading(b.x-a.x,a.y-b.y);
    }
    private static Quad QuadOf(RectTransform rect,Vector4 local,Camera camera,Vector2 pivot)
    {
        Vector2 G(float x,float y){var s=RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(new Vector3(x,y,0f)));return new Vector2(s.x-pivot.x,pivot.y-s.y);}
        return new Quad{C0=G(local.x,local.y),C1=G(local.x,local.w),C2=G(local.z,local.w),C3=G(local.z,local.y)};
    }
    private static Vector2 Turn(Vector2 point,Vector2 around,float degrees)
    {
        M28PlayerMarkerPolicy.Rotate(point.x-around.x,point.y-around.y,degrees,out float x,out float y);
        return new Vector2(around.x+x,around.y+y);
    }
    private static bool IsUnder(Transform transform,Transform ancestor)
    {
        if(transform==null||ancestor==null)return false;
        for(var t=transform;t!=null;t=t.parent)if(t.Pointer==ancestor.Pointer)return true;
        return false;
    }
    private static float InheritedAlpha(UiGraphic graphic)
    {
        try{var renderer=graphic.canvasRenderer;return renderer!=null?Mathf.Clamp01(renderer.GetInheritedAlpha()):1f;}
        catch{return 1f;}
    }

    // The UI art is not CPU-readable: one GPU blit of the sprite rect + readback.
    private static uint[] ReadTexture(Texture source,Rect uv,int width,int height)
    {
        var previous=RenderTexture.active;
        var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        Texture2D readable=null;
        try
        {
            Graphics.Blit(source,target,new Vector2(uv.width,uv.height),new Vector2(uv.x,uv.y));
            RenderTexture.active=target;
            readable=new Texture2D(width,height,TextureFormat.RGBA32,false);
            readable.ReadPixels(new Rect(0,0,width,height),0,0,false);
            var pixels=readable.GetPixels32();
            var managed=new uint[width*height];
            CopyIn(pixels,managed);
            return managed;
        }
        finally
        {
            RenderTexture.active=previous;
            RenderTexture.ReleaseTemporary(target);
            if(readable!=null)UnityEngine.Object.Destroy(readable);
        }
    }
    // Il2Cpp array: header (klass, monitor, bounds, max_length) then the elements.
    private static unsafe void CopyIn(Il2CppStructArray<Color32> pixels,uint[] target)
    {
        if(pixels==null||pixels.Length<target.Length)throw new InvalidOperationException("неполные пиксели");
        IntPtr native=pixels.Pointer;
        if(native!=IntPtr.Zero&&Marshal.ReadIntPtr(native,3*IntPtr.Size).ToInt64()==pixels.Length)
        {
            long bytes=(long)target.Length*4;
            fixed(uint* destination=target)Buffer.MemoryCopy((void*)IntPtr.Add(native,4*IntPtr.Size),destination,bytes,bytes);
            return;
        }
        for(int i=0;i<target.Length;i++){var c=pixels[i];target[i]=(uint)(c.r|c.g<<8|c.b<<16|c.a<<24);}
    }
    private static unsafe Texture2D Upload(uint[] pixels,int width,int height)
    {
        var colors=new Il2CppStructArray<Color32>(pixels.Length);
        IntPtr native=colors.Pointer;
        if(native!=IntPtr.Zero&&Marshal.ReadIntPtr(native,3*IntPtr.Size).ToInt64()==colors.Length)
        {
            long bytes=(long)pixels.Length*4;
            fixed(uint* source=pixels)Buffer.MemoryCopy(source,(void*)IntPtr.Add(native,4*IntPtr.Size),bytes,bytes);
        }
        else for(int i=0;i<pixels.Length;i++){uint p=pixels[i];colors[i]=new Color32((byte)p,(byte)(p>>8),(byte)(p>>16),(byte)(p>>24));}
        var texture=new Texture2D(width,height,TextureFormat.RGBA32,true);
        texture.name="CreatorToolkit.PlayerMarker";texture.hideFlags=HideFlags.HideAndDontSave;
        texture.filterMode=FilterMode.Trilinear;texture.wrapMode=TextureWrapMode.Clamp;
        texture.SetPixels32(colors);
        texture.Apply(true,false);
        return texture;
    }
    private static void DestroyAll(List<Part> parts)
    {
        foreach(var part in parts)
            if(part.Texture!=null&&!part.Texture.WasCollected)UnityEngine.Object.Destroy(part.Texture);
    }
    private uint Hash()
    {
        uint hash=2166136261u;
        void Mix(uint value){hash^=value;hash*=16777619u;}
        foreach(var part in _parts)
        {
            Mix((uint)part.Width);Mix((uint)part.Height);
            Mix((uint)Mathf.RoundToInt(part.X*4));Mix((uint)Mathf.RoundToInt(part.Y*4));Mix((uint)Mathf.RoundToInt(part.W*4));Mix((uint)Mathf.RoundToInt(part.H*4));
            Mix((uint)Mathf.RoundToInt(part.Angle*4));Mix(part.Below?1u:0u);Mix((uint)(part.Tint.GetHashCode()));
            foreach(char c in part.Name)Mix(c);
        }
        return hash;
    }

    private bool Save()
    {
        string dir=_app.DataDirectory;
        if(string.IsNullOrEmpty(dir))return false;
        try
        {
            Directory.CreateDirectory(dir);
            foreach(var old in Directory.GetFiles(dir,"player-marker-*.png"))File.Delete(old);
            var lines=new List<string>{Format,"captured "+_captured+" heading "+F(_heading)+" source "+_source+" arrow "+F(_arrowAngle)+" body "+F(_body)+" extent "+F(_extent)};
            for(int i=0;i<_parts.Count;i++)
            {
                var p=_parts[i];
                WritePng(Path.Combine(dir,"player-marker-"+i+".png"),p.Texture);
                lines.Add("part "+i+" "+p.Width+" "+p.Height+" "+F(p.X)+" "+F(p.Y)+" "+F(p.W)+" "+F(p.H)+" "+F(p.Angle)+" "+
                    F(p.Tint.r)+" "+F(p.Tint.g)+" "+F(p.Tint.b)+" "+F(p.Tint.a)+" "+(p.Below?1:0)+" "+(p.Rotating?1:0)+" "+Safe(p.Name));
                lines.Add("info "+i+" "+M28TestPolicy.OneLine(p.Info,400));
            }
            File.WriteAllLines(Path.Combine(dir,FileName),lines,new UTF8Encoding(false));
            return true;
        }
        catch(Exception e){_app.LoggerInstance.Warning("[M28] player marker save: "+e.Message);return false;}
    }
    private static void WritePng(string path,Texture2D texture)
    {
        var png=ImageConversion.EncodeToPNG(texture);
        var bytes=new byte[png.Length];
        IntPtr native=png.Pointer;
        if(native!=IntPtr.Zero&&Marshal.ReadIntPtr(native,3*IntPtr.Size).ToInt64()==png.Length)
            Marshal.Copy(IntPtr.Add(native,4*IntPtr.Size),bytes,0,bytes.Length);
        else for(int i=0;i<bytes.Length;i++)bytes[i]=png[i];
        File.WriteAllBytes(path,bytes);
    }
    private void Load()
    {
        var loaded=new List<Part>();
        try
        {
            string dir=_app.DataDirectory,file=Path.Combine(dir,FileName);
            if(!File.Exists(file))return;
            var lines=File.ReadAllLines(file);
            if(lines.Length<2||lines[0]!=Format)throw new InvalidDataException("другой формат");
            var head=lines[1].Split(' ');
            for(int i=0;i+1<head.Length;i++)
            {
                if(head[i]=="body")_body=P(head[i+1]);
                else if(head[i]=="extent")_extent=P(head[i+1]);
                else if(head[i]=="heading")_heading=P(head[i+1]);
                else if(head[i]=="source")_source=head[i+1];
                else if(head[i]=="captured"&&i+2<head.Length)_captured=head[i+1]+" "+head[i+2];
            }
            foreach(var line in lines)
            {
                if(line.StartsWith("info ",StringComparison.Ordinal))
                {
                    var bits=line.Split(new[]{' '},3);
                    if(bits.Length==3&&int.TryParse(bits[1],out int at)&&at>=0&&at<loaded.Count)loaded[at].Info=bits[2];
                    continue;
                }
                if(!line.StartsWith("part ",StringComparison.Ordinal))continue;
                var f=line.Split(new[]{' '},17);
                if(f.Length<16)throw new InvalidDataException("строка части: "+line);
                int index=int.Parse(f[1],CultureInfo.InvariantCulture);
                var bytes=File.ReadAllBytes(Path.Combine(dir,"player-marker-"+index+".png"));
                // M29: decoded in managed code and uploaded like a fresh copy; ImageConversion.LoadImage
                // throws MissingMethodException in this build, so the saved marker never loaded before.
                var pixels=M29PngDecoder.Decode(bytes,out int pngWidth,out int pngHeight);
                var texture=Upload(pixels,pngWidth,pngHeight);
                loaded.Add(new Part{Width=int.Parse(f[2],CultureInfo.InvariantCulture),Height=int.Parse(f[3],CultureInfo.InvariantCulture),
                    X=P(f[4]),Y=P(f[5]),W=P(f[6]),H=P(f[7]),Angle=P(f[8]),Tint=new Color(P(f[9]),P(f[10]),P(f[11]),P(f[12])),
                    Below=f[13]=="1",Rotating=f[14]=="1",Name=f.Length>15?f[15]:"",Texture=texture});
            }
            if(loaded.Count==0)throw new InvalidDataException("нет частей");
            DestroyAll(_parts);_parts.Clear();_parts.AddRange(loaded);_hash=Hash();
            Status="Метка игрока: "+_parts.Count+" частей загружено (копия с карты игры "+_captured+")";
        }
        catch(Exception e)
        {
            DestroyAll(loaded);
            Status="Метка игрока: сохранённая копия не прочитана ("+e.Message+") — откройте карту игры";
            _app.LoggerInstance.Warning("[M28] player marker load: "+e.Message);
        }
    }

    internal string Dump()
    {
        var sb=new StringBuilder();
        sb.AppendLine("Player marker (M28)");
        sb.AppendLine("status: "+Status);
        sb.AppendLine("captured: "+_captured+" captures="+Captures+" failures="+Failures+" last: "+LastNote);
        sb.AppendLine("heading="+F(_heading)+" source="+_source+" arrow="+F(_arrowAngle)+" body="+F(_body)+" extent="+F(_extent));
        for(int i=0;i<_parts.Count;i++)
        {
            var p=_parts[i];
            sb.Append("part ").Append(i).Append(' ').Append(p.Name).Append(" tex=").Append(p.Width).Append('x').Append(p.Height)
              .Append(" at=").Append(F(p.X)).Append(',').Append(F(p.Y)).Append(" size=").Append(F(p.W)).Append('x').Append(F(p.H))
              .Append(" angle=").Append(F(p.Angle)).Append(" tint=").Append(F(p.Tint.r)).Append(',').Append(F(p.Tint.g)).Append(',').Append(F(p.Tint.b)).Append(',').Append(F(p.Tint.a))
              .Append(p.Below?" below":"").Append(p.Rotating?" rotating":"").Append(" | ").AppendLine(p.Info);
        }
        return sb.ToString();
    }
    private static string Safe(string text)
    {
        var sb=new StringBuilder();
        foreach(char c in text??"")sb.Append(char.IsWhiteSpace(c)?'_':c);
        return sb.Length==0?"part":sb.ToString();
    }
    private static string F(float value)=>float.IsFinite(value)?value.ToString("0.###",CultureInfo.InvariantCulture):"nan";
    private static float P(string text)=>float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out float value)?value:0f;
}
