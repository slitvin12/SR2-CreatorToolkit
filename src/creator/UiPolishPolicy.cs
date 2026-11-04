using System;

namespace CreatorToolkit;

// 0.8.5: pure policies behind the world tracker icons, the eased switches, the sliding tab marker,
// the page fade and the procedural pixel logo of the menu header. No Unity types: unit-tested.
internal static class TrackerRowPolicy
{
    internal const int BadgeNone=0,BadgeDone=1,BadgePending=2,BadgeUnvisited=3;
    // Status badge in the corner of the row icon: done, not done, never loaded, unknown (no badge).
    internal static int Badge(bool? done,bool unvisited)=>done==true?BadgeDone:done==false?(unvisited?BadgeUnvisited:BadgePending):BadgeNone;
    // The atlas layer whose canon / native icon stands for a row (and for the list button).
    internal static string Category(int kind)=>M27TrackingPolicy.Kinds[M27TrackingPolicy.ClampKind(kind)];
    internal const int MaxRewards=3;
    // Pod rows show up to three reward icons; other lists show none.
    internal static int RewardSlots(int kind,int rewards)=>M27TrackingPolicy.ClampKind(kind)==M27TrackingPolicy.Pods?Math.Clamp(rewards,0,MaxRewards):0;
    internal const float Row=48f,Face=44f,Icon=34f,IconX=8f,TextX=54f,Reward=28f,RewardStep=32f,Dial=20f,DistanceW=80f,Badge14=14f;
    // Text width left of the right-hand cluster (distance + direction dial, reward icons).
    internal static float TextWidth(float rowWidth,bool distance,int rewards)=>
        Math.Max(40f,rowWidth-TextX-10f-(distance?DistanceW+Dial+16f:0f)-Math.Clamp(rewards,0,MaxRewards)*RewardStep);
    // Share of done entries for the summary bar; -1 hides the bar (teleports, empty list).
    internal static float Progress(int kind,int total,int done)=>
        M27TrackingPolicy.ClampKind(kind)==M27TrackingPolicy.Teleports||total<=0?-1f:Math.Clamp(done/(float)total,0f,1f);
    internal static string Percent(float progress)=>progress<0?"":((int)Math.Floor(progress*100f+.0001f)).ToString(System.Globalization.CultureInfo.InvariantCulture)+"%";
}

// Eased UI motion with fixed durations in unscaled seconds; every step is allocation-free.
internal static class UiMotionPolicy
{
    internal const float SwitchSeconds=.14f,MarkerSeconds=.22f,PageFadeSeconds=.18f,LogoPeriod=2.6f,LogoSquash=.035f;
    // Linear progress toward 0/1 (a reversal midway continues from where it is), shown through Smooth.
    internal static float Toward(float value,float target,float seconds,float duration)
    {
        if(!float.IsFinite(target))target=0;
        target=Math.Clamp(target,0f,1f);
        if(!float.IsFinite(value))return target;
        value=Math.Clamp(value,0f,1f);
        if(!float.IsFinite(seconds)||seconds<=0)return value;
        if(!float.IsFinite(duration)||duration<=0)return target;
        float step=Math.Min(1f,seconds/duration);
        return value<target?Math.Min(target,value+step):Math.Max(target,value-step);
    }
    internal static float Smooth(float t){t=float.IsFinite(t)?Math.Clamp(t,0f,1f):1f;return t*t*(3f-2f*t);}
    internal static float EaseOut(float t){t=float.IsFinite(t)?Math.Clamp(t,0f,1f):1f;float u=1f-t;return 1f-u*u*u;}
    // A value that glides to a new target: From→To over MarkerSeconds with ease-out.
    internal struct Glide{internal float From,To,T;internal bool Initialized;}
    internal static float Step(ref Glide glide,float target,float seconds,float duration=MarkerSeconds)
    {
        if(!float.IsFinite(target))target=glide.Initialized?glide.To:0f;
        if(!glide.Initialized||!float.IsFinite(glide.From)||!float.IsFinite(glide.To)||!float.IsFinite(glide.T))
        {glide=new Glide{From=target,To=target,T=1f,Initialized=true};return target;}
        if(target!=glide.To){glide.From=Value(glide);glide.To=target;glide.T=0f;}
        if(float.IsFinite(seconds)&&seconds>0)glide.T=duration>0&&float.IsFinite(duration)?Math.Min(1f,glide.T+seconds/duration):1f;
        return Value(glide);
    }
    internal static float Value(Glide glide)=>glide.T>=1f?glide.To:glide.From+(glide.To-glide.From)*EaseOut(glide.T);
    // Opacity of the parchment veil over a freshly opened page: 1 → 0 with ease-out.
    internal static float PageVeil(float elapsed)=>!float.IsFinite(elapsed)||elapsed<0?0f:1f-EaseOut(elapsed/PageFadeSeconds);
    // 0.8.5 entrance: the menu window pops from 92 % with a slight overshoot while its pages develop
    // from the parchment; a confirmation pops from 94 %; button paints cross-fade; bars fill smoothly.
    internal const float MenuPopSeconds=.26f,MenuVeilSeconds=.22f,DialogSeconds=.2f,SurfaceFadeSeconds=.12f,
        StatusFadeSeconds=.25f,BarSeconds=.35f;
    internal static float EaseOutBack(float t)
    {
        t=float.IsFinite(t)?Math.Clamp(t,0f,1f):1f;
        const float c1=1.70158f,c3=c1+1f;float u=t-1f;
        return 1f+c3*u*u*u+c1*u*u;
    }
    internal static float MenuPop(float elapsed)=>Pop(elapsed,MenuPopSeconds,.92f);
    internal static float DialogPop(float elapsed)=>Pop(elapsed,DialogSeconds,.94f);
    private static float Pop(float elapsed,float seconds,float from)
    {
        if(!float.IsFinite(elapsed)||elapsed>=seconds)return 1f;
        if(elapsed<=0)return from;
        return from+(1f-from)*EaseOutBack(elapsed/seconds);
    }
    internal static float MenuVeil(float elapsed)=>!float.IsFinite(elapsed)||elapsed<0?0f:1f-EaseOut(elapsed/MenuVeilSeconds);
    // 0 → 1 with ease-out over the given seconds; an invalid clock shows the settled state.
    internal static float Fade(float elapsed,float seconds)=>
        !float.IsFinite(elapsed)||!(seconds>0)||elapsed>=seconds?1f:elapsed<=0?0f:EaseOut(elapsed/seconds);
    // The header slime breathes: a slow squash and stretch, bottom anchored, never more than 3.5 %.
    internal static void LogoBreath(float time,out float scaleX,out float scaleY)
    {
        float s=float.IsFinite(time)?MathF.Sin(time*(2f*MathF.PI/LogoPeriod)):0f;
        scaleX=1f+LogoSquash*s;scaleY=1f-LogoSquash*s;
    }
}

// The menu header logo: a 32 x 32 pink slime in a builder's hard hat with two mint sparkles, drawn by
// the mod itself (no game art needed, so it is there before a save is loaded).
internal static class CreatorLogoPolicy
{
    internal const int Size=32,Upscale=4,TextureSize=Size*Upscale;
    // Bottom of the body outline (row 28 of 32): the breathing squash keeps this line still.
    internal const float Base=29f/32f;
    internal static readonly string[] Rows={
        "................................",
        "...........................S....",
        "...........................S....",
        ".........................SSWSS..",
        "............KKKKKKKK.......S....",
        "..........KKYYYHHYYYKK.....S....",
        "...S.....KYYHYYHHYYYYYK.........",
        "..SSS...KYYHYYYHHYYYYYYK........",
        "...S...KYYYYYYYHHYYYYYYYK.......",
        ".....KKKYYYYYYYHHYYYYYYYKKK.....",
        "....KYYYYYYYYYYYYYYYYYYYYYYK....",
        "....KyyyyyyyyyyyyyyyyyyyyyyK....",
        "...KPPWWLLLLPPPPPPPPPPPPPPDK....",
        "...KPPWLLLLPPPPPPPPPPPPPPPDK....",
        "..KPPPPLLLPPPPPPPPPPPPPPPPPDK...",
        "..KPPPPPPPEEPPPPPPPEEPPPPPPDK...",
        ".KPPPPPPPEWWEPPPPPEWWEPPPPPDDK..",
        ".KPPPPPPPEWEEPPPPPEWEEPPPPPDDK..",
        ".KPPPPPPPEEEEPPPPPEEEEPPPPPPDK..",
        ".KPPPPPPPEEEEPPPPPEEEEPPPPPDDK..",
        ".KPPPPPPPPEEPPPPPPPEEPPPPPPDDK..",
        ".KPPPPCCPPPPPPPPPPPPPPPPCCPDDK..",
        ".KPPPPPPPPPPPEPPPPEPPPPPPPDDDK..",
        "..KPPPPPPPPPPPEEEEPPPPPPPPDDK...",
        "..KDPPPPPPPPPPPPPPPPPPPPPDDDK...",
        "...KDPPPPPPPPPPPPPPPPPPPDDDK....",
        "....KDDPPPPPPPPPPPPPPPDDDDK.....",
        ".....KDDDPPPPPPPPPPDDDDDDK......",
        "......KKKKKKKKKKKKKKKKKKK.......",
        "................................",
        "................................",
        "................................"};
    internal const string Keys="KPDLWECYyHS";
    // 0xRRGGBB per key: outline, pink body, shade, gloss, white, eyes and mouth, blush, hat, brim, hat shine, mint sparkle.
    private static readonly uint[] Colors={0x3B2133,0xFF8FB4,0xE55F8A,0xFFC9DB,0xFFFFFF,0x2B1B2A,0xFF5E7E,0xFFB83D,0xD98A1E,0xFFE28A,0x1DCFB0};
    internal static bool TryColor(char key,out uint rgb)
    {
        int i=Keys.IndexOf(key);
        rgb=i<0?0u:Colors[i];
        return i>=0;
    }
    // RGBA32 pixels of the upscaled texture in Unity's bottom-up row order (null when the map is broken).
    internal static byte[] Pixels()
    {
        if(Rows.Length!=Size)return null;
        var data=new byte[TextureSize*TextureSize*4];
        for(int y=0;y<Size;y++)
        {
            string row=Rows[y];
            if(row==null||row.Length!=Size)return null;
            for(int x=0;x<Size;x++)
            {
                char c=row[x];
                if(c=='.')continue;
                if(!TryColor(c,out uint rgb))return null;
                for(int dy=0;dy<Upscale;dy++)
                    for(int dx=0;dx<Upscale;dx++)
                    {
                        int ty=TextureSize-1-(y*Upscale+dy),tx=x*Upscale+dx,at=(ty*TextureSize+tx)*4;
                        data[at]=(byte)(rgb>>16);data[at+1]=(byte)(rgb>>8);data[at+2]=(byte)rgb;data[at+3]=255;
                    }
            }
        }
        return data;
    }
}
