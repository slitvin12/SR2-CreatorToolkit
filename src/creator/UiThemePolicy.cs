using System;

namespace CreatorToolkit;

// Pure, testable visual policy. Geometry keeps each visual face and its hitbox
// aligned; it never changes the native input stream, save data or physics.
internal static class UiThemePolicy
{
    internal const string Theme="native-parchment-v3";
    internal const uint Canvas=0xF8F4EA,Ink=0x2C2621,Rim=0xE8E0D0,Card=0xFFFDF9;
    internal const uint Body=0x4A4540,Heading=0x2C3E50,Mint=0x1DCFB0,Coral=0xFF5E7E;
    internal const uint Amber=0xFFAA33;
    internal const int TextureBudget=16;
    internal const int RestDepth=4,HoverDepth=3,HeldDepth=1;
    internal static float JellyShadeFloor(uint color)=>color is 0xFF6B7A or Coral ? .95f : .86f;
    internal static float PillWeight(string label)=>label switch
    {
        "Эксперименты"=>1.25f,
        "Слаймодождь"=>1.20f,
        "Ноды карты" or "Механизмы"=>1.18f,
        _=>1f
    };
    internal static float PillRowWidth(float total,float gap,string[] labels,int index)
    {
        if(!float.IsFinite(total)||!float.IsFinite(gap)||total<=0||gap<0||
            labels==null||labels.Length==0||index<0||index>=labels.Length||
            total<=gap*(labels.Length-1))throw new ArgumentOutOfRangeException(nameof(total));
        float weight=0;foreach(string label in labels)weight+=PillWeight(label);
        return (total-gap*(labels.Length-1))*PillWeight(labels[index])/weight;
    }
    internal const string CyrillicProbe="CREATOR TOOLKIT Розовый слайм Ёё Йй 0123456789 ×";
    internal static int ButtonDepth(bool hovered,bool held)=>
        held&&hovered?HeldDepth:hovered?HoverDepth:RestDepth;
    internal const float HoverSeconds=.120f,HoverLift=-4f,HoverMaximumScale=1.05f;
    internal static int FaceOffset(bool hovered,bool held)=>hovered?(held?3:-4):0;
    internal struct HoverAnimation
    {
        internal float Position,From,Target,Elapsed;
        internal bool Initialized;
    }
    // Fixed-duration smoothstep, not an exponential spring: every transition,
    // including reversal midway through a hover, settles in 120 unscaled ms.
    internal static void StepHover(ref HoverAnimation motion,float target,float seconds)
    {
        if(!float.IsFinite(target))target=0;
        target=Math.Clamp(target,HoverLift,3f);
        if(!motion.Initialized||!float.IsFinite(motion.Position)||!float.IsFinite(motion.Elapsed)||
            !float.IsFinite(motion.From)||!float.IsFinite(motion.Target))
            motion=new HoverAnimation{Initialized=true};
        if(motion.Target!=target)
        {
            motion.From=motion.Position;motion.Target=target;motion.Elapsed=0;
        }
        if(float.IsFinite(seconds)&&seconds>0)motion.Elapsed=Math.Min(HoverSeconds,motion.Elapsed+seconds);
        float t=Math.Clamp(motion.Elapsed/HoverSeconds,0,1),smooth=t*t*(3f-2f*t);
        motion.Position=motion.From+(motion.Target-motion.From)*smooth;
        if(t>=1)motion.Position=motion.Target;
    }
    internal static float HoverScale(float offset)=>float.IsFinite(offset)?
        1f+(HoverMaximumScale-1f)*Math.Clamp(-offset/-HoverLift,0,1):1f;
    // Contain one atlas cell without distorting its native aspect ratio.
    internal static bool TryFitSprite(float sourceWidth,float sourceHeight,float slotWidth,float slotHeight,
        out float x,out float y,out float width,out float height)
    {
        x=y=width=height=0;
        if(!float.IsFinite(sourceWidth)||!float.IsFinite(sourceHeight)||
            !float.IsFinite(slotWidth)||!float.IsFinite(slotHeight)||
            sourceWidth<=0||sourceHeight<=0||slotWidth<=0||slotHeight<=0)return false;
        double scale=Math.Min((double)slotWidth/sourceWidth,(double)slotHeight/sourceHeight);
        float w=(float)(sourceWidth*scale),h=(float)(sourceHeight*scale);
        if(!float.IsFinite(w)||!float.IsFinite(h)||w<=0||h<=0)return false;
        width=w;height=h;x=(slotWidth-w)*.5f;y=(slotHeight-h)*.5f;
        return true;
    }
    // Value-only structs: fixed storage in ToolkitUi, no delegate/list/string work.
    internal struct SliderAnimation
    {
        internal float Value;
        internal bool Initialized;
        internal HoverAnimation Jelly;
    }
    internal const float OpeningSeconds=.18f;
    internal static float OpeningProgress(float elapsed)
    {
        if(!float.IsFinite(elapsed))return 1f;
        float t=Math.Clamp(elapsed/OpeningSeconds,0f,1f);
        return 1f-(1f-t)*(1f-t)*(1f-t);
    }
    internal const float SliderResponseSeconds=.045f;
    internal static void StepSlider(ref SliderAnimation motion,float target,bool hover,bool held,float seconds)
    {
        target=float.IsFinite(target)?Math.Clamp(target,0f,1f):motion.Initialized?motion.Value:0f;
        if(!motion.Initialized||!float.IsFinite(motion.Value))
            motion=new SliderAnimation{Initialized=true,Value=target};
        if(float.IsFinite(seconds)&&seconds>0)
        {
            float alpha=1f-MathF.Exp(-Math.Min(seconds,.1f)/SliderResponseSeconds);
            motion.Value+=(target-motion.Value)*alpha;
            if(Math.Abs(target-motion.Value)<.0001f)motion.Value=target;
        }
        StepHover(ref motion.Jelly,held?3f:hover?HoverLift:0f,seconds);
    }
    internal static bool SliderCanContinue(bool enabled,bool focused,bool held,bool withinGroups)=>
        enabled&&focused&&held&&withinGroups;
    internal static float SliderPointerValue(float pointer,float x,float width)=>
        float.IsFinite(pointer)&&float.IsFinite(x)&&float.IsFinite(width)&&width>0?
        Math.Clamp((pointer-x)/width,0f,1f):0f;
    internal static float SliderKnobCenter(float x,float width,float t,float diameter)
    {
        float radius=Math.Min(Math.Max(0,width)*.5f,Math.Max(0,diameter)*.5f);
        return Math.Clamp(x+Math.Max(0,width)*Math.Clamp(t,0,1),x+radius,x+Math.Max(0,width)-radius);
    }
    internal static float CapsuleRadius(float width,float height,float requested)=>
        float.IsFinite(width)&&float.IsFinite(height)&&float.IsFinite(requested)?
        Math.Clamp(requested,0,Math.Max(0,Math.Min(width,height)*.5f)):0;
    internal static float Coverage(float x,float y,float size,float radius)
    {
        float qx=Math.Abs(x+.5f-size*.5f)-(size*.5f-radius);
        float qy=Math.Abs(y+.5f-size*.5f)-(size*.5f-radius);
        float distance=MathF.Sqrt(Math.Max(qx,0)*Math.Max(qx,0)+Math.Max(qy,0)*Math.Max(qy,0))+
            Math.Min(Math.Max(qx,qy),0)-radius;
        return Math.Clamp(.5f-distance,0,1);
    }
    internal static int NativeFontRank(string name,bool dynamic)
    {
        string n=(name??"").ToLowerInvariant();
        if(n.Contains("creatortoolkit")||n.Contains("legacyruntime")||n.Contains("segoe")||n.Contains("times new")||
            n.Contains("liberation")||(n.Contains("arial")&&!n.Contains("round")))return -1;
        // The current catalog's native Cyrillic font bundle contains these
        // exact OpenSans faces. Prefer the game's actual localization font to
        // an OS imitation. Do not describe an OpenSans face as "rounded".
        // Exact requested localization face outranks every rounded/Bold
        // candidate, even when that other face is dynamic. Glyph validation
        // remains mandatory in ResolveGameFont before this ranking is used.
        int native=n.Contains("opensans-semibold")||n.Contains("open sans semibold")?1000:
            n.Contains("opensans-bold")||n.Contains("open sans bold")?470:0;
        int round=n.Contains("mikado")?500:n.Contains("fredoka")?450:n.Contains("baloo")?430:
            n.Contains("nunito")?420:n.Contains("rounded")?410:n.Contains("quicksand")?400:
            n.Contains("comfortaa")?390:n.Contains("varela")?380:n.Contains("comic")?370:
            n.Contains("trebuchet")?360:0;
        if(round==0&&native==0)return -1;
        return Math.Max(round,native)+(dynamic?30:0);
    }
    internal static bool RoundedFace(string name)
    {
        string n=(name??"").ToLowerInvariant();
        return n.Contains("mikado")||n.Contains("fredoka")||n.Contains("baloo")||n.Contains("nunito")||
            n.Contains("rounded")||n.Contains("quicksand")||n.Contains("comfortaa")||n.Contains("varela")||
            n.Contains("comic")||n.Contains("trebuchet");
    }
}