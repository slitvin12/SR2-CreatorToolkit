using System;
using System.IO;
using System.Linq;

namespace CreatorToolkit;

// 0.8.3: the ninth «Настройки» tab, optional action keys, measured tab row, tile buttons, the Newbucks coin
// and press-and-drag map carousels.
internal static class SettingsRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool ok,string name)=>check(ok,"0.8.3 settings: "+name);
        string dir=Path.Combine(root,"src","creator");
        string R(string name){string p=Path.Combine(dir,name);return File.Exists(p)?File.ReadAllText(p).Replace("\r\n","\n"):"";}
        string Section(string text,string start,string end)
        {
            int a=text.IndexOf(start,StringComparison.Ordinal);
            if(a<0)return "";
            int b=text.IndexOf(end,a+start.Length,StringComparison.Ordinal);
            return b<0?text.Substring(a):text.Substring(a,b-a);
        }

        // Action key policy.
        C(ActionHotkeyPolicy.Ids.Length==6&&ActionHotkeyPolicy.Ids.Distinct().Count()==6&&ActionHotkeyPolicy.Titles.Length==ActionHotkeyPolicy.Ids.Length&&
          ActionHotkeyPolicy.Empty().All(k=>k==null)&&ActionHotkeyPolicy.Index(" GOD ")==1&&ActionHotkeyPolicy.Index("menu")<0,"six actions, unique ids, all unbound by default");
        C(ActionHotkeyPolicy.Key("f5")=="F5"&&ActionHotkeyPolicy.Key(" numpad1 ")=="Numpad1"&&ActionHotkeyPolicy.Key("Escape")==null&&ActionHotkeyPolicy.Key("Space")==null&&
          ActionHotkeyPolicy.Key("")==null&&ActionHotkeyPolicy.Key(null)==null,"keys: canonical names, menu-forbidden keys rejected");
        var parsed=ActionHotkeyPolicy.Parse("# x\r\nflight=F5\r\ngod=f5\nenergy=Escape\nhud= numpad1 \nbogus=F6\nfps=\n# frame=F7\nframe\n");
        C(parsed.Length==6&&parsed[0]=="F5"&&parsed[1]==null&&parsed[2]==null&&parsed[3]=="Numpad1"&&parsed[4]==null&&parsed[5]==null,
          "parse: duplicates keep the first line, forbidden/unknown/empty lines ignored");
        C(ActionHotkeyPolicy.Parse(null).Length==6&&ActionHotkeyPolicy.Parse("").All(k=>k==null),"parse: missing file means no keys");
        var round=new[]{"F5",null,"PageDown",null,"Numpad3","K"};
        C(ActionHotkeyPolicy.Parse(ActionHotkeyPolicy.Serialize(round)).SequenceEqual(round)&&ActionHotkeyPolicy.Serialize(null).Contains("flight=\n")&&
          ActionHotkeyPolicy.Serialize(new[]{"Escape"}).Contains("flight=\n"),"serialize: round trip, forbidden or short input writes empty lines");
        var keys=ActionHotkeyPolicy.Empty();
        C(ActionHotkeyPolicy.Assign(keys,0,"F5","Insert",out int m0)==null&&keys[0]=="F5"&&m0==-1,"assign: a free key");
        C(ActionHotkeyPolicy.Assign(keys,1,"f5","Insert",out int m1)==null&&keys[1]=="F5"&&keys[0]==null&&m1==0,"assign: a taken key moves and reports its previous owner");
        C(ActionHotkeyPolicy.Assign(keys,2,"insert","Insert",out int m2)!=null&&keys[2]==null&&m2==-1&&
          ActionHotkeyPolicy.Assign(keys,2,"Space","Insert",out _)!=null&&ActionHotkeyPolicy.Assign(keys,6,"F6","Insert",out _)!=null&&
          ActionHotkeyPolicy.Assign(null,0,"F6","Insert",out _)!=null&&ActionHotkeyPolicy.Assign(new string[2],0,"F6","Insert",out _)!=null,"assign: menu key, forbidden keys and bad actions refused");
        C(ActionHotkeyPolicy.Active("F5","Insert")&&!ActionHotkeyPolicy.Active("F5","F5")&&!ActionHotkeyPolicy.Active(null,"Insert")&&
          ActionHotkeyPolicy.Active("Insert","F5"),"active: the menu key always wins");
        C(ActionHotkeyPolicy.Display(null)=="—"&&ActionHotkeyPolicy.Display("Escape")=="—"&&ActionHotkeyPolicy.Display("PageUp")=="Page Up"&&
          ActionHotkeyPolicy.Display("Numpad3")=="Num 3","display");
        C(new[]{"flight","hotkey god mode","hotkey infinite energy","info HUD","hide HUD","smooth zoom"}.All(MultiplayerPolicy.Known)&&
          MultiplayerPolicy.Classify("hotkey god mode")==CoopKind.Cheat&&MultiplayerPolicy.Classify("hotkey infinite energy")==CoopKind.Cheat,
          "co-op: every queued hotkey label is known, god mode and energy are cheats");

        // Tab row: measured needs, spare width spread evenly.
        float[] need={90,95,110,70,120,105,90,115,125},w=new float[9];
        bool fits=UiThemePolicy.TabWidths(need,1100f,8f,w);
        float extra=w[0]-need[0];
        C(fits&&Math.Abs(w.Sum()+8*8-1100f)<.01f&&extra>0&&need.Select((n,i)=>Math.Abs(w[i]-n-extra)).Max()<.01f,"tabs: nine measured tabs fill one 1100 px row");
        C(!UiThemePolicy.TabWidths(need,900f,8f,w)&&w.All(x=>Math.Abs(x-(900f-64f)/9f)<.01f)&&!UiThemePolicy.TabWidths(need,1100f,8f,new float[3])&&
          !UiThemePolicy.TabWidths(new[]{float.NaN},100f,0f,new float[1])&&!UiThemePolicy.TabWidths(null,100f,0f,w),"tabs: overflow falls back to equal slices, bad input refused");
        C(UiThemePolicy.SmallButtonWidth(40f)==58f&&UiThemePolicy.SmallButtonWidth(20f)==56f&&UiThemePolicy.SmallButtonWidth(float.NaN)==56f&&
          UiThemePolicy.SmallButtonWidth(39.2f)==58f,"tile buttons: measured label plus padding, 56 px floor");
        // Carousel drag.
        C(AtlasGroupPolicy.DragThreshold==6f&&!AtlasGroupPolicy.IsDrag(6f)&&!AtlasGroupPolicy.IsDrag(-5f)&&AtlasGroupPolicy.IsDrag(-6.5f)&&AtlasGroupPolicy.IsDrag(7f),
          "carousel: a press becomes a drag after 6 px");
        C(AtlasGroupPolicy.CarouselDrag(100f,40f,1000f,300f)==60f&&AtlasGroupPolicy.CarouselDrag(100f,-900f,1000f,300f)==700f&&
          AtlasGroupPolicy.CarouselDrag(10f,50f,1000f,300f)==0f&&AtlasGroupPolicy.CarouselDrag(0f,-50f,200f,300f)==0f,"carousel: drag follows the pointer, clamped to the strip");

        // Wiring.
        string ui=R("ToolkitUi.cs"),info=R("InfoUi.cs"),opt=R("OptimizationUi.cs"),icons=R("NativeIconTools.cs"),toolkit=R("ToolkitMod.cs"),
            shortcuts=R("ActionHotkeys.cs"),actors=R("ActorTools.cs"),atlas=R("AtlasUi.cs"),explorer=R("ExplorerUi.cs"),probe=R("NativeUiProbe.cs"),
            tsv=R("LocEnglish.tsv"),tests=R(Path.Combine("tests","CreatorToolkit.Tests.csproj"));
        string settings=Section(ui,"private void SettingsPage(Rect area)","\n    private "),convenience=Section(ui,"private void ConveniencePage(Rect area)","\n    private ");
        C(ui.Contains("internal const int TabCount=9;")&&ui.Contains("\"Карта\", \"Удобство\", \"Настройки\" };")&&ui.Contains("case 8:SettingsPage(body);break;")&&
          ui.Contains("var tabWidths=TabWidths(width-60);")&&ui.Contains("UiThemePolicy.TabWidths(_tabNeed,total,8f,_tabWidths)"),"ui: nine tabs on one measured row");
        C(settings.Length>0&&new[]{"hotkey.StartCapture()","hotkey.Reset()","keys.StartCapture(i)","cap.SetZoomKey(zoomKey)","cap.SetShowFps(!cap.ShowFps)","OpenOptimizationUi()","_infoPage=true"}.All(settings.Contains),
          "«Настройки»: menu key, action keys, zoom key, FPS, graphics and console");
        C(convenience.Length>0&&convenience.Contains("ShowPage(8)")&&!convenience.Contains("OpenOptimizationUi()")&&!convenience.Contains("_infoPage=true")&&
          !convenience.Contains("hotkey.StartCapture()")&&!convenience.Contains("SetShowFps("),"«Удобство»: the moved controls are gone, a link leads to «Настройки»");
        C(ui.Contains("if(_tab==8){_optimizationPage=false;_infoPage=false;}")&&info.Contains("_tab==8&&_infoPage")&&opt.Contains("_tab==8&&_optimizationPage")&&
          info.Contains("\" / \"+ToolkitUi.TabCount+\" · игровые спрайты\"")&&!info.Contains("\" / 8 · "),"graphics and console are nested in tab 8");
        C(icons.Contains("\"Удобство\",\"Настройки\"};")&&icons.Contains("\"Yolky\",\"Rock\"};")&&icons.Contains("\"yolky_slime\",\"rock_slime\"};")&&!icons.Contains("\" / 8 · "),
          "tab icon: «Настройки» shows the rock slime");
        C(toolkit.Contains("Shortcuts = new ActionHotkeys(this);")&&toolkit.Contains("Shortcuts.Tick(kb, MenuOpen && (_ui?.TextFieldFocused ?? false));")&&
          toolkit.Contains("Shortcuts?.CancelCapture();")&&toolkit.Contains("if (!assigning)")&&toolkit.Contains("ActionKeys: {Shortcuts?.Summary}"),"host: keys tick outside capture, capture cancelled on close");
        C(shortcuts.Contains("File.Replace(temp,file,null)")&&shortcuts.Contains("kb.leftCtrlKey.isPressed")&&shortcuts.Contains("textFocused&&MenuHotkeyPolicy.Typing(key)")&&
          shortcuts.Contains("_app.Queue(\"hotkey god mode\"")&&shortcuts.Contains("_app.Queue(\"hotkey infinite energy\""),"runtime: atomic save, Ctrl/Alt ignored, typing keys wait, cheats queued");
        C(ui.Contains("float ownW=SmallTileButtonWidth(\"Мои ×\")")&&ui.Contains("JellyBase(visual,glow);")&&!ui.Contains("if(visual.height<40f)")&&ui.Contains("_app.Actors.CurrencyIcon()"),
          "tiles: measured small buttons, one seamless glow and shadow at every button size; currency coin");
        C(ui.Contains("if(kindW>=24&&_small.CalcSize(kind).x<=kindW)")&&!ui.Contains("Ellipsize(entry.IsPrototype"),"tiles: the type label is shown whole or not at all");
        string currency=Section(actors,"internal int Currency()","\n    internal ");
        C(actors.Contains("internal Sprite CurrencyIcon()")&&currency.Length>0&&!currency.Contains("catch"),"currency: read without an exception per frame");
        C(atlas.Contains("AtlasCarousel(0,strip,")&&atlas.Contains("AtlasCarousel(1,strip,")&&explorer.Contains("AtlasCarousel(2,strip,")&&atlas.Contains("CarouselTap(0,rect)")&&
          atlas.Contains("CarouselTap(1,rect)")&&explorer.Contains("CarouselTap(2,pill)")&&atlas.Contains("DrawChevron(")&&!atlas.Contains("\"<\",_button")&&!atlas.Contains("\">\",_button")&&
          ui.Contains("_carouselDrag=_carouselTap=-1;"),"carousels: round chevrons, press-and-drag, taps click on release");
        int fpsCalls=0;for(int at=toolkit.IndexOf("_ui.DrawFpsCounter()",StringComparison.Ordinal);at>=0;at=toolkit.IndexOf("_ui.DrawFpsCounter()",at+1,StringComparison.Ordinal))fpsCalls++;
        C(fpsCalls==1&&toolkit.Contains("else if (!StarlightBridge.MenuOpen)"),"host: one FPS counter call for menu, HUD and Starlight frames");
        // 0.8.4: one working «?»; the cards hug their controls and explanations need «?».
        string cinema=Section(ui,"private void CinemaPage(Rect area)","\n    private ");
        C(ui.Contains("DetailedHelp=!DetailedHelp;_app.Notify(DetailedHelp?\"Подсказки включены · пояснения под кнопками\":\"Подсказки скрыты\");")&&
          !ui.Contains("DetailedHelp?\"Скрыть подсказки\":\"Подсказки\""),"hints: the header «?» is the one switch and says what it did");
        C(convenience.Contains("float cardH=help?214:166")&&convenience.Contains("Hint(new Rect(x+20,row2+hintY,col-40,46),\"Зона, XYZ")&&
          convenience.Contains("Hint(new Rect(20,row2+hintY,col-40,24),tools.Refilling?tools.RefillStatus:\"Порог: \"+ModInterop.RefillText,_small);")&&
          convenience.Contains("\"Имя сейва…\",_accentButton))OpenSaveNameUi();")&&convenience.Contains("\"Фермерский помощник…\"")&&
          !convenience.Contains("L.Label(new Rect(20,row2+50")&&!convenience.Contains("string[] places="),"«Удобство»: compact cards, explanations only with «?»");
        C(cinema.Contains("new Rect(2*(third+12),0,third,44),\"Нативные феномены…\"")&&
          cinema.Contains("Hint(new Rect(0,244,area.width,30),\"Первая точка — ваш быстрый возврат\",_label);")&&!cinema.Contains("L.Label(new Rect(20,244"),
          "«Съёмка»: sub-pages in one row, the empty-list text only with «?»");
        C(settings.Contains("float bottom=help?area.height:keyRow+ActionHotkeyPolicy.Count*stride+rowH+20;")&&settings.Contains("Hint(new Rect(20,hy,lw,area.height-hy-10)")&&
          settings.Contains("Hint(new Rect(x+20,fy+104,lw,36)")&&settings.Contains("Hint(new Rect(x+20,gy+108,lw,bottom-gy-116)")&&probe.Contains("Pointer(734,582,true)"),
          "«Настройки»: compact cards, explanations only with «?», the probe follows");
        C(probe.Contains("TabCenterForTest(_latencyIndex%ToolkitUi.TabCount)")&&!probe.Contains("_latencyIndex%8"),"in-game probe follows the measured tab row");
        C(tests.Contains("..\\ActionHotkeyPolicy.cs")&&tsv.Contains("\nНастройки\tSettings\n")&&tsv.Contains("\nГорячие клавиши\tHotkeys\n"),"tests link the policy, English rows present");
    }
}
