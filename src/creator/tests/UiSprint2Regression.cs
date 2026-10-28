using CreatorToolkit;

internal static class UiSprint2Regression
{
    // UI3 additions are offline pure-policy fixtures and static wiring guards only.
    // They do not certify loaded DLL identity, native rendering, input, saves or FPS.
    private static void Ui3(Action<bool,string> check,string root)
    {
        bool Near(float a,float b)=>Math.Abs(a-b)<.0001f;
        bool Fit(float sw,float sh,float bw,float bh,float ex,float ey,float ew,float eh)
        {
            bool ok=UiThemePolicy.TryFitSprite(sw,sh,bw,bh,out float x,out float y,out float w,out float h);
            return ok&&Near(x,ex)&&Near(y,ey)&&Near(w,ew)&&Near(h,eh)&&
                Near(w/sw,h/sh)&&x>=0&&y>=0&&x+w<=bw+.0001f&&y+h<=bh+.0001f;
        }
        check(Fit(200,100,40,40,0,10,40,20),"ui3 landscape native sprite keeps aspect and centers vertically");
        check(Fit(100,200,40,40,10,0,20,40),"ui3 portrait native sprite keeps aspect and centers horizontally");
        check(Fit(100,100,40,40,0,0,40,40),"ui3 square native sprite fills a square slot without distortion");
        check(Fit(80,40,120,40,20,0,80,40),"ui3 wide slot contains native sprite without stretching to slot width");
        check(Fit(40,80,40,120,0,20,40,80),"ui3 tall slot contains native sprite without stretching to slot height");
        check(Fit(16,8,64,64,0,16,64,32),"ui3 native sprite upscaling remains uniform and centered");
        check(Fit(320,180,32,18,0,0,32,18),"ui3 matching landscape aspect uses full slot");
        bool Invalid(float sw,float sh,float bw,float bh)
        {
            bool ok=UiThemePolicy.TryFitSprite(sw,sh,bw,bh,out float x,out float y,out float w,out float h);
            return !ok&&x==0&&y==0&&w==0&&h==0;
        }
        foreach(float value in new[]{0f,-1f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
        {
            check(Invalid(value,10,40,40),"ui3 invalid sprite width rejects and zeroes all outputs: "+value);
            check(Invalid(10,value,40,40),"ui3 invalid sprite height rejects and zeroes all outputs: "+value);
            check(Invalid(10,10,value,40),"ui3 invalid slot width rejects and zeroes all outputs: "+value);
            check(Invalid(10,10,40,value),"ui3 invalid slot height rejects and zeroes all outputs: "+value);
        }
        bool finiteFits=true;
        foreach(float sw in new[]{1f,7f,100f,1024f})foreach(float sh in new[]{1f,13f,200f})
        {
            bool ok=UiThemePolicy.TryFitSprite(sw,sh,53,37,out float x,out float y,out float w,out float h);
            finiteFits&=ok&&float.IsFinite(x)&&float.IsFinite(y)&&float.IsFinite(w)&&float.IsFinite(h)&&
                w>0&&h>0&&x>=0&&y>=0&&x+w<=53.0001f&&y+h<=37.0001f&&
                Near(w/sw,h/sh)&&Near(x*2+w,53)&&Near(y*2+h,37);
        }
        check(finiteFits,"ui3 seeded aspect grid preserves uniform positive finite centered contain geometry");
        check(Invalid(float.MaxValue,float.Epsilon,float.Epsilon,float.MaxValue),
            "ui3 representational underflow cannot publish a zero-size sprite fit");
        string Compact(string s)=>string.Concat(s.Where(c=>!char.IsWhiteSpace(c)));
        string map=Compact(File.ReadAllText(Path.Combine(root,"src","creator","MapTools.cs")));
        string candidates=map.Split("foreach(varcandidatein_uiCandidates)")[1].Split("DisplayStatus=")[0];
        check(candidates.Contains("NativeUiPriority.IsFrontDisplay(candidate)"),
            "ui3 static map candidate selection consults actual native front ownership, not active pooled order alone");
        string complete=map.Split("privatevoidCompleteMapClick()")[1].Split("privatevoidUnsubscribe()")[0];
        string commitGate=complete.Split("ClickRequests++")[0];
        check(commitGate.Contains("NativeMapHasInput")&&commitGate.Contains("_clickPage")&&
              commitGate.Contains("Ui.CurrentMapDefinition?.Pointer")&&commitGate.Contains("_clickDragged")&&
              commitGate.Contains("_clickExpires")&&commitGate.Contains("_clickPoint"),
            "ui3 static shared final map commit retains gesture guards and revalidates owner plus page before effects");
        check(map.Split("CompleteMapClick();").Length>=3,
            "ui3 static native-navigation and pointer-release paths retain the shared final map commit gate");
        string ui=Compact(File.ReadAllText(Path.Combine(root,"src","creator","ToolkitUi.cs")));
        string sprite=ui.Split("privatevoidDrawSprite(Rectrect,Spritesprite)")[1].Split("privatevoidCatalog(")[0];
        check(sprite.Contains("UiThemePolicy.TryFitSprite(")&&sprite.Contains("BeginGroup(")&&
              !sprite.Contains("sx=rect.width/r.width,sy=rect.height/r.height"),
            "ui3 static native sprite adapter consumes contain fit instead of independent slot-axis stretch");
        check(ui.Contains("Fill(newRect(iconRect.x+1,iconRect.y,Mathf.Max(2,icon*.12f),icon),MarkerColor(point.ColorIndex));"),
            "ui3 static map marker stripe shares marker hover geometry");
        string marker=ui.Split("floatmarkerOffset=HoverOffset(hit,")[1].Split("if(hit.Contains(Pointer()))")[0];
        check(marker.Contains("RecticonRect=HoverChildRect(hit,newRect(p2.x-half,p2.y-half,icon,icon),markerOffset)")&&
              !marker.Contains("GUI.Label(count,")&&!marker.Contains("cluster.Items.Count"),
            "ui3 static marker icon shares its hover transform and no count badge merges nearby objects");
        string explorer=Compact(File.ReadAllText(Path.Combine(root,"src","creator","ExplorerUi.cs")));
        check(explorer.Contains("Fill(HoverChildRect(row,newRect(row.x+5,row.y+10,4,24),")&&
              !explorer.Contains("Fill(newRect(row.x+5,row.y+10,4,24),MarkerColor(record.Color))"),
            "ui3 static custom waypoint row stripe follows row hover instead of remaining detached");
        string choices=explorer.Split("privateintColorChoices(Rectrect,intselected,stringprefix)")[1]
            .Split("privatevoidCustomWaypointPage(floatwidth)")[0];
        check(choices.Contains("boolhovered=InputEnabled&&r.Contains(Pointer())")&&
              choices.Contains("floatoffset=HoverOffset(r,hovered,hovered&&")&&
              choices.Contains("Fill(HoverChildRect(r,newRect(r.x+12,r.yMax-6,r.width-24,3),offset),MarkerColor(i))")&&
              !choices.Contains("Fill(newRect(r.x+12,r.yMax-6,r.width-24,3),MarkerColor(i))"),
            "ui3 static ColorChoices palette stripe shares its own button hover transform and native marker color");
    }
    internal static void Run(Action<bool,string> check,string root)
    {
        Ui3(check,root);
        check(NativeFocusPolicy.SelectActiveRoot(false,false)==NativeFocusPolicy.RootChoice.None &&
              NativeFocusPolicy.SelectActiveRoot(true,false)==NativeFocusPolicy.RootChoice.Display &&
              NativeFocusPolicy.SelectActiveRoot(false,true)==NativeFocusPolicy.RootChoice.Root &&
              NativeFocusPolicy.SelectActiveRoot(true,true)==NativeFocusPolicy.RootChoice.Display,
              "sprint2 active-root truth table preserves display priority and inactive-display fallback");
        string Dead()=>"collected";
        bool Live(string s)=>s!=null&&s!="collected";
        string Forbidden()=>throw new InvalidOperationException("lower-priority weather getter was eagerly invoked");
        var chosen=NativeIconPolicy.SelectWeatherIcon(()=>"metadata",Forbidden,Forbidden,Live,out var src);
        check(chosen=="metadata"&&src==NativeIconPolicy.WeatherIconSource.MetadataIcon,"sprint2 live metadata icon short-circuits all Pedia getters");
        chosen=NativeIconPolicy.SelectWeatherIcon(Dead,()=>"small",Forbidden,Live,out src);
        check(chosen=="small"&&src==NativeIconPolicy.WeatherIconSource.PediaSmallIcon,"sprint2 collected metadata falls back lazily to live Pedia small icon");
        chosen=NativeIconPolicy.SelectWeatherIcon(()=>null,Dead,()=>"large",Live,out src);
        check(chosen=="large"&&src==NativeIconPolicy.WeatherIconSource.PediaIcon,"sprint2 null metadata and collected small icon fall back to live Pedia icon");
        chosen=NativeIconPolicy.SelectWeatherIcon(Dead,()=>null,Dead,Live,out src);
        check(chosen==null&&src==NativeIconPolicy.WeatherIconSource.Missing,"sprint2 all unavailable native candidates remain explicitly missing without substitute");
        var a=new IntPtr(11);var b=new IntPtr(12);
        check(NativeIconPolicy.MergeWeatherPreset(IntPtr.Zero,a,false)==NativeIconPolicy.WeatherMerge.Replace&&
              NativeIconPolicy.MergeWeatherPreset(a,a,false)==NativeIconPolicy.WeatherMerge.Replace&&
              NativeIconPolicy.MergeWeatherPreset(a,IntPtr.Zero,false)==NativeIconPolicy.WeatherMerge.Keep&&
              NativeIconPolicy.MergeWeatherPreset(a,b,false)==NativeIconPolicy.WeatherMerge.Ambiguous,
              "sprint2 preset merge accepts same native sprite, retains missing candidate and rejects conflict");
        check(new[]{IntPtr.Zero,a,b}.All(p=>NativeIconPolicy.MergeWeatherPreset(p,a,true)==NativeIconPolicy.WeatherMerge.Ambiguous),
              "sprint2 ambiguity remains sticky even after existing sprite unload or same candidate retry");
        var state=new NativeIconPolicy.WeatherLookupState();
        check(NativeIconPolicy.TryBeginWeatherLookup(ref state,1,0)&&state.Attempts==1&&state.NextAttempt==1,"sprint2 first weather lookup begins immediately and schedules one-second retry");
        check(!NativeIconPolicy.TryBeginWeatherLookup(ref state,1,.999f)&&state.Attempts==1,"sprint2 early metadata retries do not consume attempts");
        bool budget=true;
        for(int i=1;i<=3;i++)budget&=NativeIconPolicy.TryBeginWeatherLookup(ref state,1,i)&&state.Attempts==i+1;
        check(budget&&!NativeIconPolicy.TryBeginWeatherLookup(ref state,1,100)&&state.Attempts==4,"sprint2 exactly four metadata attempts allowed per epoch, never a fifth after time passes");
        var prior=state;
        bool invalid=true;
        foreach(float t in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,-1f})invalid&=!NativeIconPolicy.TryBeginWeatherLookup(ref state,2,t);
        check(invalid&&state.Epoch==prior.Epoch&&state.Attempts==prior.Attempts&&state.NextAttempt==prior.NextAttempt,"sprint2 invalid clocks cannot consume attempts or rearm another epoch");
        check(NativeIconPolicy.TryBeginWeatherLookup(ref state,2,100)&&state.Epoch==2&&state.Attempts==1,"sprint2 new positive focus epoch rearms exhausted metadata attempts");
        state=default;
        check(NativeIconPolicy.TryBeginWeatherLookup(ref state,0,0)&&state.Attempts==1,"sprint2 reset clears weather lookup exhaustion including initial zero epoch");
        state=default;string late=null;
        bool began=NativeIconPolicy.TryBeginWeatherLookup(ref state,1,0);
        chosen=NativeIconPolicy.SelectWeatherIcon(()=>late,()=>null,()=>null,Live,out src);
        bool missing=began&&chosen==null;
        late="loaded-native";
        began=NativeIconPolicy.TryBeginWeatherLookup(ref state,1,1);
        chosen=NativeIconPolicy.SelectWeatherIcon(()=>late,Forbidden,Forbidden,Live,out src);
        check(missing&&began&&chosen==late&&src==NativeIconPolicy.WeatherIconSource.MetadataIcon,"sprint2 previously unresolved metadata can become live on a bounded same-epoch retry");

        var viewport=new MapShieldPolicy.Box(0,0,32,24);
        var sources=new[]{new MapShieldPolicy.Box(-3,2,22,18),new MapShieldPolicy.Box(23,1,7,20)};
        var cuts=new[]{new MapShieldPolicy.Box(4,4,9,8),new MapShieldPolicy.Box(8,8,9,10),new MapShieldPolicy.Box(5,5,2,2),new MapShieldPolicy.Box(23,0,2,24),new MapShieldPolicy.Box(-5,17,12,10)};
        bool Has(MapShieldPolicy.Box box,float x,float y)=>x>=box.X&&x<box.Right&&y>=box.Y&&y<box.Bottom;
        var pieces=MapShieldPolicy.Build(sources,cuts,viewport);
        bool exact=pieces.Count>0;int expectedPixels=0,actualPixels=0;
        for(int y=-5;y<30;y++)for(int x=-5;x<36;x++)
        {
            bool expected=Has(viewport,x+.5f,y+.5f)&&sources.Any(z=>Has(z,x+.5f,y+.5f))&&!cuts.Any(z=>Has(z,x+.5f,y+.5f));
            bool actual=pieces.Any(z=>Has(z,x+.5f,y+.5f));
            exact&=expected==actual;if(expected)expectedPixels++;if(actual)actualPixels++;
        }
        check(exact&&expectedPixels>0&&actualPixels==expectedPixels,"sprint2 independent pixel oracle proves complete multi-hole card coverage, not vacuous subset-only coverage");
        var source=new MapShieldPolicy.Box(0,0,10,10);
        var split4=new MapShieldPolicy.Box(2,2,2,2);
        var many=Enumerable.Repeat(source,512).ToArray();
        check(MapShieldPolicy.Build(many,new[]{split4},new(0,0,10,10)).Count==2048,"sprint2 exact source and piece budgets retain all 2048 valid shield pieces");
        check(MapShieldPolicy.Build(many,new[]{split4,new MapShieldPolicy.Box(6,6,2,2)},new(0,0,10,10)).Count==0,"sprint2 piece-budget overflow discards the entire publication rather than partial shields");
        var holes=Enumerable.Repeat(new MapShieldPolicy.Box(20,20,1,1),64).ToArray();
        check(MapShieldPolicy.Build(new[]{source},holes,new(0,0,10,10)).Count==1,"sprint2 exactly 64 exclusions are accepted without off-by-one fail-open");
        var hole=new MapShieldPolicy.Box(2,3,4,5);
        check(!MapShieldPolicy.AllowModPointer(true,new[]{hole},2,3)&&!MapShieldPolicy.AllowModPointer(true,new[]{hole},5.999f,7.999f)&&
              MapShieldPolicy.AllowModPointer(true,new[]{hole},6,4)&&MapShieldPolicy.AllowModPointer(true,new[]{hole},3,8),"sprint2 pointer holes use inclusive left/top and exclusive right/bottom boundaries");
        var motion=new UiThemePolicy.HoverAnimation();
        UiThemePolicy.StepHover(ref motion,-4,.03f);
        check(Math.Abs(motion.Position+.625f)<.00001f,"sprint2 smoothstep quarter-time position rejects a linear tween");
        UiThemePolicy.StepHover(ref motion,-4,.06f);
        check(Math.Abs(motion.Position+3.375f)<.00001f,"sprint2 smoothstep three-quarter position follows fixed-duration cubic easing");
        var direct=new UiThemePolicy.HoverAnimation();UiThemePolicy.StepHover(ref direct,-4,.09f);
        check(Math.Abs(direct.Position-motion.Position)<.00001f,"sprint2 elapsed-time partitioning preserves the same hover trajectory");
        float before=motion.Position;UiThemePolicy.StepHover(ref motion,3,0);
        bool continuous=motion.Position==before;UiThemePolicy.StepHover(ref motion,3,.12f);
        check(continuous&&motion.Position==3&&UiThemePolicy.HoverScale(motion.Position)==1,"sprint2 hover-to-held reversal stays continuous and finishes at exact held geometry");
        check(UiThemePolicy.NativeFontRank("OpenSans-Semibold",false)>UiThemePolicy.NativeFontRank("Mikado Bold",true)&&
              UiThemePolicy.NativeFontRank("OpenSans-Semibold",false)>UiThemePolicy.NativeFontRank("OpenSans-Bold",true),"sprint2 requested static Semibold beats dynamic Mikado and Bold");
        string[] absentFallback={"Mikado Bold","OpenSans-Bold","Generic Game Font"};
        check(absentFallback.OrderByDescending(n=>UiThemePolicy.NativeFontRank(n,false)).First()=="Mikado Bold"&&UiThemePolicy.NativeFontRank("OpenSans-Bold",false)>0,"sprint2 absent Semibold leaves known native fallback faces eligible");
        string icons=File.ReadAllText(Path.Combine(root,"src","creator","NativeIconTools.cs"));
        check(icons.Contains("NativeIconPolicy.SelectWeatherIcon")&&icons.Contains("NativeIconPolicy.MergeWeatherPreset")&&icons.Contains("NativeIconPolicy.TryBeginWeatherLookup")&&icons.Contains("if(Role(lookup.Role)==null)ReadWeatherMetadata(lookup)"),"sprint2 adapter wiring guard consumes tested weather policies and retries unavailable roles only (static, not runtime proof)");
        string map=File.ReadAllText(Path.Combine(root,"src","creator","MapTools.cs"));
        string pointer=map.Split("internal bool AllowsOverlayPointer(Vector2 screen)")[1].Split("private bool CollectNativeControlExclusions")[0];
        check(pointer.Contains("foreach(var rect in NativeChromeRects)")&&pointer.Contains("screen.x<box.Right")&&pointer.Contains("Screen.height-screen.y<box.Bottom"),"sprint2 static wiring guard rejects mod actions in the same native chrome holes as shields");
    }
}