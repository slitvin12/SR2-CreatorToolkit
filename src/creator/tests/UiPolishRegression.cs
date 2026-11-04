using System;
using System.IO;
using System.Linq;

namespace CreatorToolkit;

// 0.8.5: world tracker icons, the gadget page layout, jelly hover in «Слои карты», eased motion
// (switch knobs, segment pills, tab marker, page veil) and the pixel logo of the header.
internal static class UiPolishRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool ok,string name)=>check(ok,"0.8.5 polish: "+name);
        string dir=Path.Combine(root,"src","creator");
        string R(string name){string p=Path.Combine(dir,name);return File.Exists(p)?File.ReadAllText(p).Replace("\r\n","\n"):"";}
        string Section(string text,string start,string end)
        {
            int a=text.IndexOf(start,StringComparison.Ordinal);
            if(a<0)return "";
            int b=text.IndexOf(end,a+start.Length,StringComparison.Ordinal);
            return b<0?text.Substring(a):text.Substring(a,b-a);
        }

        // Tracker rows.
        C(TrackerRowPolicy.Badge(true,false)==TrackerRowPolicy.BadgeDone&&TrackerRowPolicy.Badge(true,true)==TrackerRowPolicy.BadgeDone&&
          TrackerRowPolicy.Badge(false,false)==TrackerRowPolicy.BadgePending&&TrackerRowPolicy.Badge(false,true)==TrackerRowPolicy.BadgeUnvisited&&
          TrackerRowPolicy.Badge(null,true)==TrackerRowPolicy.BadgeNone&&TrackerRowPolicy.Badge(null,false)==TrackerRowPolicy.BadgeNone,
          "badge: done, pending, never loaded, unknown");
        C(Enumerable.Range(-2,8).All(k=>M27TrackingPolicy.KindOf(TrackerRowPolicy.Category(k))==M27TrackingPolicy.ClampKind(k))&&
          TrackerRowPolicy.Category(M27TrackingPolicy.Pods)=="Капсулы"&&TrackerRowPolicy.Category(M27TrackingPolicy.Teleports)=="Телепорты",
          "every list maps to its own atlas layer icon");
        C(TrackerRowPolicy.RewardSlots(M27TrackingPolicy.Pods,5)==3&&TrackerRowPolicy.RewardSlots(M27TrackingPolicy.Pods,2)==2&&
          TrackerRowPolicy.RewardSlots(M27TrackingPolicy.Pods,-1)==0&&TrackerRowPolicy.RewardSlots(M27TrackingPolicy.Gordos,3)==0&&
          TrackerRowPolicy.RewardSlots(M27TrackingPolicy.Doors,3)==0&&TrackerRowPolicy.RewardSlots(M27TrackingPolicy.Teleports,3)==0,
          "rewards: up to three on pod rows only");
        float free=TrackerRowPolicy.TextWidth(1028,false,0),busy=TrackerRowPolicy.TextWidth(1028,true,3);
        C(free>busy&&busy>=600&&TrackerRowPolicy.TextWidth(60,true,3)==40&&TrackerRowPolicy.TextWidth(1028,true,9)==busy&&
          TrackerRowPolicy.TextX>=TrackerRowPolicy.IconX+TrackerRowPolicy.Icon+8&&TrackerRowPolicy.Icon+8<=TrackerRowPolicy.Face&&TrackerRowPolicy.Face<TrackerRowPolicy.Row,
          "row: icon inside the face, text clear of the icon and of distance, dial and rewards");
        C(TrackerRowPolicy.Progress(M27TrackingPolicy.Pods,150,75)==.5f&&TrackerRowPolicy.Progress(M27TrackingPolicy.Teleports,10,0)<0&&
          TrackerRowPolicy.Progress(M27TrackingPolicy.Gordos,0,0)<0&&TrackerRowPolicy.Progress(M27TrackingPolicy.Doors,10,12)==1f&&
          TrackerRowPolicy.Percent(.5f)=="50%"&&TrackerRowPolicy.Percent(.999f)=="99%"&&TrackerRowPolicy.Percent(1f)=="100%"&&TrackerRowPolicy.Percent(-1)=="",
          "summary bar: share and whole percent, hidden for teleports and empty lists");

        // Motion.
        float s=UiMotionPolicy.SwitchSeconds;
        C(UiMotionPolicy.Toward(0,1,s*.5f,s)==.5f&&UiMotionPolicy.Toward(.5f,1,1,s)==1f&&UiMotionPolicy.Toward(.75f,0,s*.25f,s)==.5f&&
          UiMotionPolicy.Toward(float.NaN,1,0,s)==1f&&UiMotionPolicy.Toward(.3f,1,0,s)==.3f&&UiMotionPolicy.Toward(.3f,1,.01f,0)==1f&&
          UiMotionPolicy.Toward(.3f,float.NaN,s,s)==0f&&UiMotionPolicy.Toward(2f,1,0,s)==1f,
          "switch knob: linear progress, reversal midway, NaN and range safe, zero time holds");
        C(UiMotionPolicy.Smooth(0)==0&&UiMotionPolicy.Smooth(1)==1&&UiMotionPolicy.Smooth(.5f)==.5f&&UiMotionPolicy.Smooth(.25f)<.25f&&
          UiMotionPolicy.Smooth(float.NaN)==1&&UiMotionPolicy.EaseOut(0)==0&&UiMotionPolicy.EaseOut(1)==1&&UiMotionPolicy.EaseOut(.5f)==.875f,
          "easing: smoothstep and cubic ease-out end exactly");
        var glide=new UiMotionPolicy.Glide();
        float g0=UiMotionPolicy.Step(ref glide,100,.016f),g1=UiMotionPolicy.Step(ref glide,300,0);
        float g2=UiMotionPolicy.Step(ref glide,300,UiMotionPolicy.MarkerSeconds*.5f),g3=UiMotionPolicy.Step(ref glide,300,UiMotionPolicy.MarkerSeconds);
        C(g0==100&&g1==100&&g2==275&&g3==300,"tab marker: starts in place, glides with ease-out and lands exactly");
        var back=new UiMotionPolicy.Glide();UiMotionPolicy.Step(ref back,0,0);UiMotionPolicy.Step(ref back,100,UiMotionPolicy.MarkerSeconds*.5f);
        float mid=UiMotionPolicy.Value(back),rev=UiMotionPolicy.Step(ref back,0,0);
        C(mid==87.5f&&rev==mid&&UiMotionPolicy.Step(ref back,0,UiMotionPolicy.MarkerSeconds)==0&&UiMotionPolicy.Step(ref back,float.NaN,1)==0,
          "tab marker: a new target midway continues from where the marker is");
        C(UiMotionPolicy.PageVeil(0)==1&&UiMotionPolicy.PageVeil(UiMotionPolicy.PageFadeSeconds)==0&&UiMotionPolicy.PageVeil(5)==0&&
          UiMotionPolicy.PageVeil(-1)==0&&UiMotionPolicy.PageVeil(float.NaN)==0&&UiMotionPolicy.PageVeil(UiMotionPolicy.PageFadeSeconds*.5f) is >0f and <.5f,
          "page veil: opaque at the switch, gone after the fade, never stuck");
        bool breath=true;
        for(float t=0;t<10;t+=.05f)
        {
            UiMotionPolicy.LogoBreath(t,out float sx,out float sy);
            breath&=Math.Abs(sx-1)<=UiMotionPolicy.LogoSquash+1e-5f&&Math.Abs(sx+sy-2)<1e-4f;
        }
        UiMotionPolicy.LogoBreath(float.NaN,out float nx,out float ny);
        C(breath&&nx==1&&ny==1&&UiMotionPolicy.LogoSquash<=.05f,"logo: a gentle squash and stretch, area kept, NaN-safe");

        // Pixel logo.
        var rows=CreatorLogoPolicy.Rows;int n=CreatorLogoPolicy.Size;
        C(rows.Length==n&&rows.All(r=>r.Length==n)&&rows.All(r=>r.All(c=>c=='.'||CreatorLogoPolicy.Keys.IndexOf(c)>=0))&&
          CreatorLogoPolicy.Keys.Distinct().Count()==CreatorLogoPolicy.Keys.Length&&CreatorLogoPolicy.Keys.All(k=>CreatorLogoPolicy.TryColor(k,out _)),
          "logo: 32 x 32, every pixel from a palette with unique keys");
        int[] dx={1,-1,0,0},dy={0,0,1,-1};bool closed=true;
        for(int y=0;y<n;y++)for(int x=0;x<n;x++)
        {
            char c=rows[y][x];if(c=='.'||c=='K'||c=='S')continue;
            for(int k=0;k<4;k++){int px=x+dx[k],py=y+dy[k];if(px<0||py<0||px>=n||py>=n||rows[py][px]=='.')closed=false;}
        }
        C(closed,"logo: the slime and its hat sit inside a closed dark outline");
        int Count(char c)=>rows.Sum(r=>r.Count(ch=>ch==c));
        C(rows[0].All(c=>c=='.')&&rows[n-1].All(c=>c=='.')&&rows.All(r=>r[0]=='.'&&r[n-1]=='.')&&Count('P')>250&&Count('E')>=30&&Count('Y')>60&&
          Count('C')>=4&&Count('S')>=8,"logo: transparent border, a pink body with eyes, smile and blush, a hard hat, mint sparkles");
        int baseRow=(int)Math.Round(CreatorLogoPolicy.Base*n)-1;
        C(baseRow==28&&rows[baseRow].Count(c=>c=='K')>=15&&rows.Skip(baseRow+1).All(r=>r.All(c=>c=='.')),"logo: the breathing base is the bottom outline");
        var pixels=CreatorLogoPolicy.Pixels();int size=CreatorLogoPolicy.TextureSize;
        int At(int x,int y)=>((size-1-y)*size+x)*4;
        bool block=pixels!=null;
        if(block)for(int y=80;y<84;y++)for(int x=60;x<64;x++)block&=pixels[At(x,y)]==0xFF&&pixels[At(x,y)+1]==0x8F&&pixels[At(x,y)+2]==0xB4&&pixels[At(x,y)+3]==255;
        C(block&&pixels.Length==size*size*4&&size==128&&pixels[At(0,0)+3]==0&&pixels[At(127,127)+3]==0&&
          CreatorLogoPolicy.TryColor('P',out uint pink)&&pink==0xFF8FB4&&!CreatorLogoPolicy.TryColor('.',out _),
          "logo: 4x nearest upscale in bottom-up rows, opaque palette colours, clear background");

        // Wiring.
        string ui=R("ToolkitUi.cs"),unlock=R("UnlockUi.cs"),care=R("CareUi.cs"),chrome=R("MapChromeUi.cs"),tsv=R("LocEnglish.tsv").Replace("\r","");
        string tests=R(Path.Combine("tests","CreatorToolkit.Tests.csproj"));
        string tracking=Section(unlock,"private void TrackingPage(float width)","\n    private bool TrackKindButton(");
        C(tracking.Contains("TrackKindButton(new Rect(i*198,52,190,40),i,_trackKind==i)")&&tracking.Contains("if(point!=null)AtlasPointIcon(icon,point);else TrackKindIcon(icon,row.Kind);")&&
          tracking.Contains("TrackBadge(new Rect(icon.xMax-11,icon.yMax-11")&&!tracking.Contains("HoverChildRect(face,")&&tracking.Contains("AtlasDirection(RowChild(")&&tracking.Contains("_app.Map.Icons.Reward(key)")&&
          tracking.Contains("TrackerRowPolicy.TextWidth(rowWidth,hasDistance,slots)")&&tracking.Contains("float offset=JellyOffset(face);")&&!unlock.Contains("TrackColor(")&&
          tracking.Contains("Hint(new Rect(800,52,width-800,40),\"Отметки — из сейва; расстояние — от игрока\",_small);")&&
          tracking.Contains("DrawRoundedSurface(bar,_trackStyle.normal.background,5f);")&&unlock.Contains("_trackTally=default;_trackPercent=\"\";_trackRewards.Clear();"),
          "tracker: icons, badges, rewards, direction, progress bar; the source note needs «?»");
        C(unlock.Contains("if(CanonIcon(rect,CanonMapIconPolicy.Cell(category,\"\",null)))return;")&&unlock.Contains("MapGlyph(new Rect(rect.x+2,rect.y+2,rect.width-4,rect.height-4),badge==TrackerRowPolicy.BadgeDone?MapChromePolicy.Check:MapChromePolicy.Ring,Color.white);")&&
          unlock.Contains("var rewards=view.Mismatch&&view.NativeObserved?view.NativeRewards:view.Rewards;")&&unlock.Contains("if(_trackRewards.Count<1024)"),
          "tracker: canon / native / Fandom list icons, check or ring badge, bounded reward cache");
        string gadgets=Section(care,"private void GadgetPage(float width)","\n    private void GadgetFact(");
        C(gadgets.Contains("new Rect(x + 20, top + cardH - 20 - 52, right - 40, 52), \"Разместить 1 экземпляр…\"")&&gadgets.Contains("float bw = (width - 24) / 3f, by = top + cardH + 14;")&&
          gadgets.Contains("new Rect(0, by, bw, 42), InfiniteBuilderPolicy.Label(infinite)")&&gadgets.Contains("new Rect(bw + 12, by, bw, 42), CapturePolicy.FreePlacementLabel(free)")&&
          gadgets.Contains("new Rect(2 * (bw + 12), by, bw, 42), \"Ресурсы Преобразователя…\"")&&gadgets.Contains("GadgetFact(new Rect(x + 20, fy,")&&
          gadgets.Contains("\"В наличии: \" + tools.DisplayCount(chosen)")&&!gadgets.Contains("width - 205 - 15 - 410")&&!gadgets.Contains("new Rect(x + 18, 342"),
          "gadgets: name and stock on top, modes as two lines, the button at the foot, modes and refinery in one row");
        string mini=Section(chrome,"private bool MiniSwitch(Rect rect,bool on)","\n    }"),segmented=Section(chrome,"private int Segmented(","\n    }\n");
        string group=Section(chrome,"private void MapGroupRow(","\n    }"),chip=Section(chrome,"private void MapChipRow(","\n    }");
        C(mini.Contains("UiMotionPolicy.Smooth(SwitchProgress(rect,on))")&&mini.Contains("HoverOffset(rect,hover,held)")&&segmented.Contains("SegmentGlide(rect,selected)")&&
          segmented.Contains("HoverText(new Rect(seg.x,seg.y+offset*.5f,seg.width,seg.height),titles[i],_mapSegment,offset);")&&
          group.Contains("HoverOffset(head,hover,held)")&&group.Contains("AtlasChipIcon(HoverGrow(")&&chip.Contains("HoverOffset(row,hover,held)")&&chip.Contains("AtlasChipIcon(HoverGrow(")&&
          chrome.Contains("if(MapSettingRow(new Rect(10,sy,w-20,38),\"Скрывать открытое и собранное\",map.HideOpened))")&&
          !chrome.Contains("L.Box(head,GUIContent.none,_glowBox)")&&!chrome.Contains("if(CanHover(row))L.Box(row,GUIContent.none,_glowBox);"),
          "«Слои карты»: jelly hover on groups, chips, settings rows, segments and switches");
        string slime=Section(ui,"private bool SlimeSwitch(Rect rect,bool enabled)","\n    }");
        C(slime.Contains("float t=UiMotionPolicy.Smooth(SwitchProgress(rect,enabled));bool on=t>=.5f;")&&slime.Contains("Mathf.Lerp(rect.x+6,rect.xMax-38,t)")&&
          ui.Contains("if(_switchMotion.Count>=256)_switchMotion.Clear();")&&ui.Contains("if(_glideMotion.Count>=256)_glideMotion.Clear();")&&
          ui.Contains("_hoverMotion.Clear();_scrollMotion.Clear();_switchMotion.Clear();_glideMotion.Clear();"),
          "switches: the knob slides, motion state is bounded and dropped on a tab change");
        C(ui.Contains("TabMarker(markX,markW);")&&ui.Contains("if(i==_tab){markX=tabX;markW=tabWidths[i];}")&&ui.Contains("float dt=_tabMarkFrame!=Time.frameCount?Time.unscaledDeltaTime:0f;")&&
          ui.Contains("PageVeil(body);\n                AllocationMark(4,ref allocationStamp);")&&ui.Contains("int page=_tab*8+(_tab==1?_creationSection:_tab==4?_unlockSection:0);"),
          "tabs: a gliding marker once per frame, pages fade in");
        string dispose=Section(ui,"internal void Dispose()","\n    }");
        C(ui.Contains("if(!DrawLogo(new Rect(28,12,66,66)))")&&ui.Contains("var data=CreatorLogoPolicy.Pixels();")&&ui.Contains("BuildLogo();")&&
          ui.Contains("texture.name=\"CreatorToolkit_UI_Logo\"")&&dispose.Contains("UnityEngine.Object.Destroy(_logoTexture)")&&ui.Contains("ResetCreationUi();_headerSlime=null")&&
          ui.Contains("Fill(new Rect(rect.x+i*15,rect.yMax-1,12,4),Color.Lerp(_pink,_accent,i/25f))")&&
          Section(ui,"private void Styles()","\n    private void LoadIconAtlas()").IndexOf("BuildLogo();",StringComparison.Ordinal)>
          Section(ui,"private void Styles()","\n    private void LoadIconAtlas()").IndexOf("_textures.Count!=ThemeTextureBudget",StringComparison.Ordinal),
          "header: the pixel logo (outside the 16-texture theme budget, destroyed on dispose), pixel underline");
        // 0.8.5 motion and menu speed-ups.
        float popPeak=0f;for(int i=1;i<26;i++)popPeak=Math.Max(popPeak,UiMotionPolicy.MenuPop(i*.01f));
        C(UiMotionPolicy.MenuPop(0)==.92f&&UiMotionPolicy.MenuPop(UiMotionPolicy.MenuPopSeconds)==1f&&UiMotionPolicy.MenuPop(float.NaN)==1f&&
          popPeak>1f&&popPeak<1.02f,"menu pop: from 92 %, a small overshoot, settles at 1");
        C(UiMotionPolicy.DialogPop(0)==.94f&&UiMotionPolicy.DialogPop(1)==1f&&UiMotionPolicy.MenuVeil(0)==1f&&
          UiMotionPolicy.MenuVeil(UiMotionPolicy.MenuVeilSeconds)==0f&&UiMotionPolicy.MenuVeil(float.NaN)==0f&&
          UiMotionPolicy.Fade(0,.2f)==0f&&UiMotionPolicy.Fade(.2f,.2f)==1f&&UiMotionPolicy.Fade(float.NaN,.2f)==1f&&UiMotionPolicy.Fade(.1f,.2f)>.5f&&
          UiMotionPolicy.EaseOutBack(0)==0f&&Math.Abs(UiMotionPolicy.EaseOutBack(1)-1f)<1e-6f,"dialog pop, entrance veil, fades");
        string round=Section(ui,"private void DrawRoundedSurface(","\n    }");
        C(round.Contains("float bleed=radius>0?1f/pixelScale:0f;")&&round.Contains("BeginGroup(new Rect(cl,ct,cr-cl,cb-ct));")&&
          round.Contains("left-cl-sx*scaleX")&&round.Contains("SnapX(rect.x+dx+dw)"),"pill slices overlap one physical pixel (no light seam at a cap)");
        C(ui.Contains("PaintFace(rect,face,surface);")&&ui.Contains("if(_surfaceMotion.Count>=512)_surfaceMotion.Clear();")&&
          ui.Contains("float glow=held?1f:Mathf.Clamp01(offset/UiThemePolicy.HoverLift);")&&ui.Contains("GlideTo(ref _tabRise[index]"),
          "button paints cross-fade, the glow fades with the lift, the open tab rises");
        C(ui.Contains("if(pop!=1f)GUIUtility.ScaleAroundPivot(new Vector2(pop,pop),window.center);")&&ui.Contains("UiMotionPolicy.MenuVeil(opened)")&&
          ui.Contains("UiMotionPolicy.DialogPop(since)")&&ui.Contains("finally{GUI.matrix=beforePop;_bodyInteractionBlocked=wasBlocked;}")&&
          ui.Contains("FooterStatus(_app.Audit?.Active")&&unlock.Contains("GlideTo(ref _trackBar,progress,UiMotionPolicy.BarSeconds)"),
          "menu entrance pop, dialog pop, footer fade, tracker bar glide");
        C(ui.Contains("_ellipsized.TryGetValue(key,out var cached)")&&!ui.Contains("_ellipsizeSource")&&care.Contains("var entries = GadgetView(tools);")&&
          !care.Contains("tools.Catalog.Where(")&&!ui.Contains("Any(r=>r.")&&!ui.Contains("new[]{0,-1,1}")&&
          R("MapTools.cs").Contains("internal static bool AnyContains(List<Rect> rects,Vector2 p)")&&R("MapDistanceText.cs").Contains("Metres[whole]??="),
          "menu speed-ups: cached ellipsis and gadget view, allocation-free map tests, cached distance texts");
        C(MapDistanceText.Format(999.6f)=="1000 м"&&MapDistanceText.Format(1234.4f)==MapDistanceText.Format(1234f)&&
          ReferenceEquals(MapDistanceText.Format(42f),MapDistanceText.Format(42.2f)),"distance cache keeps the texts");
        C(tests.Contains("..\\UiPolishPolicy.cs")&&new[]{"Поиск чертежа…","В наличии","Экземпляр не списывается","Списывается 1 экземпляр",
          "Без проверки пересечений и уклона","Штатные проверки места","Слева — полученные чертежи"}.All(k=>tsv.Contains("\n"+k+"\t")),
          "tests link the policy, English rows present");
    }
}
