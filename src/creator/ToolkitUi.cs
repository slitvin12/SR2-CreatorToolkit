using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CreatorToolkit;

internal sealed partial class ToolkitUi
{
    internal const int TabCount=9;
    private readonly ToolkitMod _app;
    private Vector2 _atlasScroll,_sourceDetailScroll;
    private string _sourceDetailId="";
    private Vector2 _capsuleScroll;
    private CapsuleLayout _capsuleLayout;
    private sealed class CapsuleLayout
    {
        internal CapsuleTools.View View;
        internal float Width,EvidenceHeight,DescriptionHeight,Extent;
        internal readonly List<(float Title,float Detail,float Row)> Rows=new();
    }
    private string _capsuleId="",_family="Все";
    private MapTools.Point _hoverCapsule;
    private int _tab, _category, _quantity = 1;
    private float _chickenArm, _tileArm;
    private ActorTools.Entry _tileArmEntry; // 0.8.2: «Все ×» needs a second click within 4 s
    private Vector2 _catalogScroll, _ammoScroll, _weatherScroll;
    private string _search = "", _ammoSearch = "", _waypointName = "", _currencyAmount = "";
    private float _hour = 12f;
    private int _dragSlider = -1;
    private float _menuOpenedAt=-10f;
    private bool _menuEntering;
    internal void BeginOpenAnimation(){_menuOpenedAt=Time.unscaledTime;_menuEntering=true;ClearInteraction();}
    internal float OpeningProgress=>UiThemePolicy.OpeningProgress(Time.unscaledTime-_menuOpenedAt);
    internal bool DetailedHelp;
    private Texture2D _sliderTrackPaint,_sliderFillPaint,_sliderGlowPaint;
    private struct SliderMotion
    {
        internal UiThemePolicy.SliderAnimation Animation;
        internal float Time;
        internal int Frame;
    }
    private readonly SliderMotion[] _sliderMotion=new SliderMotion[80];
    private static readonly float[] PlayerSpeeds={1,2,3,5,10};
    private static readonly string[] PlayerSpeedNames={"× 1","× 2","× 3","× 5","× 10"};
    private static readonly string[] SpeedReadouts=BuildReadouts("Скорость  × ",100,1000,100f,"F2");
    private static readonly string[] JumpReadouts=BuildReadouts("Усиление × ",10,50,10f,"F1");
    private string _healthReadout="Здоровье",_energyReadout="Энергия";
    private int _health=-1,_maxHealth=-1,_energy=-1,_maxEnergy=-1;
    internal bool SliderTestInput;
    internal int SliderDragForTest=>_dragSlider;
    internal long SliderAllocatedBytesForTest,SliderMeasuredCallsForTest;
    private static string[] BuildReadouts(string prefix,int first,int last,float divisor,string format)
    {
        var labels=new string[last-first+1];
        for(int i=first;i<=last;i++)labels[i-first]=prefix+(i/divisor).ToString(format,System.Globalization.CultureInfo.InvariantCulture);
        return labels;
    }
    internal void TickPlayerReadouts()
    {
        if(!_app.Ready)return;
        _uiContextRoot=_app.Context;
        _uiMouseRoot=Mouse.current;
        if(_uiMouseRoot!=null)
        {_mousePositionRoot=_uiMouseRoot.position;_mouseLeftRoot=_uiMouseRoot.leftButton;_mouseRightRoot=_uiMouseRoot.rightButton;}
        if(_paintSession!=_app.Session){_nativePaintTextures.Clear();_paintSession=_app.Session;}
        var st=_uiContextRoot.PlayerState;
        int health=st.GetCurrHealth(),maxHealth=st.GetMaxHealth(),energy=st.GetCurrEnergy(),maxEnergy=st.GetMaxEnergy();
        if(health!=_health||maxHealth!=_maxHealth){_health=health;_maxHealth=maxHealth;_healthReadout=$"Здоровье  {health} / {maxHealth}";}
        if(energy!=_energy||maxEnergy!=_maxEnergy){_energy=energy;_maxEnergy=maxEnergy;_energyReadout=$"Энергия  {energy} / {maxEnergy}";}
        if(!Application.isFocused&&!SliderTestInput&&!(_app.UiProbe?.Active??false))_dragSlider=-1;
    }
    private void QueuePlayerFlight(bool enabled)=>_app.Queue("flight",()=>_app.Player.SetFlight(enabled));
    // Same queue labels as «Спавн → Гаджеты»: identical co-op gating and confirmations.
    private void QueueInfiniteBuilder(bool enabled)=>_app.Queue("infinite builder",()=>_app.Gadgets.SetInfiniteBuilder(enabled));
    private void QueueFreePlacement(bool enabled)=>_app.Queue("free placement",()=>_app.FreePlacement.Set(enabled));
    private void ResetPlayerEffects(){_app.Player.Reset();_app.Notify("Эффекты игрока сброшены.");}
    internal readonly UiPaintTelemetry PaintTelemetry=new();
    private long _tabClickAt;
    private int _tabClickIndex=-1;
    internal int CurrentTabForTest=>_tab;
    private int _dragScroll = -1;
    // A slider or scroll drag owns the mouse until release, even outside its rect.
    internal bool PointerCaptured=>_dragSlider>=0||_dragScroll>=0;
    private float _dragScrollOffset, _scale = 1f;
    private int _pointerFrame = -1;
    private bool _pointerConsumed;
    private bool _bodyInteractionBlocked;
    private bool _nativeMapOverlayDrawing,_nativeMapPointerAllowed;
    private bool CanHover(Rect rect)=>InputEnabled&&PointerWithinGroups()&&rect.Contains(Pointer());
    private bool InputEnabled=>GUI.enabled&&!_bodyInteractionBlocked&&(!_menuEntering||OpeningProgress>=1f)&&
        (!_nativeMapOverlayDrawing||_nativeMapPointerAllowed);
    private Vector2 _groupOrigin;
    private readonly Stack<Vector2> _groupOrigins = new();
    private readonly Stack<Rect> _groupRects=new();
    private string _editingField = "";
    internal bool TextFieldFocused => _editingField.Length > 0; // 0.8.1: a typing menu key waits while a field has focus
    private ActorTools.Entry _selected, _ammoSelected;
    private GUIStyle _label, _muted, _heading, _title, _button, _accentButton, _selectedButton, _field, _card, _tabStyle, _wrap, _badge, _zoneLabel;
    private GUIStyle _hudFrame,_hudLabel,_hudEffects;
    private GUIStyle _atlasDetail,_atlasHelp,_atlasChipHead,_atlasChip;
    private GUIStyle _small,_iconTab,_iconTabCompact,_iconTabSmall,_goldButton,_violetButton,_shadowBox,_glowBox,_trackStyle,_fillStyle,_tableHeading;
    private GUIStyle _buttonText17,_buttonText16,_buttonText15,_buttonText14,_selectedRowText;
    private readonly Dictionary<IntPtr,GUIStyle> _textShadows=new();
    private GUIStyle[] _titleGradient;
    private Texture2D _slimeKnob,_badgeCircle,_headerGradient;
    // 0.8.5: the pixel slime of the header (CreatorLogoPolicy); outside the shared theme budget.
    private Texture2D _logoTexture;
    private int _fontResourceScans,_jellyHeldRepaints,_jellyHoverRepaints,_fontRoundRank;
    private string _fontSource="not initialized",_fontDisplayName="";
    private bool _fontCyrillic;
    private Sprite _headerSlime;
    private Texture2D _iconAtlas;
    private Font _font;
    private bool _ownsFont;
    private readonly List<Texture2D> _textures = new();
    internal readonly List<Rect> MapZoneLabelRects = new();
    private readonly Dictionary<IntPtr,GUIStyle> _hoverStyles=new(),_pressedStyles=new();
    private readonly Dictionary<string,GUIContent> _buttonLabels=new();
    private struct HoverMotion {internal UiThemePolicy.HoverAnimation Animation;internal float Time;internal int Frame;}
    private readonly Dictionary<(float X,float Y,float W,float H),HoverMotion> _hoverMotion=new();
    // 0.8.5: sliding switch knobs and gliding segment pills, keyed like hover motion; bounded.
    private struct SwitchMotion {internal float Value;internal int Frame;}
    private readonly Dictionary<(float X,float Y,float W,float H),SwitchMotion> _switchMotion=new(64);
    private struct GlideMotion {internal UiMotionPolicy.Glide Glide;internal int Frame;}
    private readonly Dictionary<(float X,float Y,float W,float H),GlideMotion> _glideMotion=new(32);
    private UiMotionPolicy.Glide _tabMarkX,_tabMarkW;
    private int _tabMarkFrame=-1,_veilPage=-1;
    private float _veilSince=-10f;
    private sealed class ScrollMotion { internal float Target,Returned,Time; internal int Frame=-1; }
    private readonly Dictionary<int,ScrollMotion> _scrollMotion=new();
    private readonly string[] _tabs = { "Игрок", "Спавн", "Ресурсы", "Мир", "Открытия", "Съёмка", "Карта", "Удобство", "Настройки" };
    private readonly string[] _categories = { "Все", "Слаймы", "Ларго", "Эксперименты", "Еда", "Плорты", "Ресурсы", "Гордо" };
    private static readonly string[] WeatherNames = { "Ясно", "Дождь", "Гроза", "Слаймодождь", "Пыльца", "Ветер", "Лава", "Блики" };
    private string _buttonLabelFitDiagnostics="";
    private static Color Hex(uint rgb)=>new(((rgb>>16)&255)/255f,((rgb>>8)&255)/255f,(rgb&255)/255f,1f);
    private readonly Color _accent = Hex(UiThemePolicy.Mint);
    private readonly Color _ink = Hex(UiThemePolicy.Ink);
    private readonly Color _pink = Hex(UiThemePolicy.Coral);
    private readonly Color _sun = Hex(UiThemePolicy.Amber);
    private readonly Color _violet = Hex(0xC2A9EB);
    private readonly Color _parchment = Hex(UiThemePolicy.Canvas);
    private readonly Color _parchmentRim = Hex(UiThemePolicy.Rim);
    private readonly Color _pastelCard = Hex(UiThemePolicy.Card);
    internal const int ThemeTextureBudget=UiThemePolicy.TextureBudget;
    private bool _iconsSupported = true;
    private GUIStyle _frame;
    private readonly Dictionary<string, List<ActorTools.Entry>> _filterCache = new();
    private int _filterGeneration = -1;
    internal ToolkitUi(ToolkitMod app) => _app = app;
    internal int OwnedTextureCount=>_textures.Count;
    internal int DynamicLabelCount=>_buttonLabels.Count;
    internal int FilterCacheCount=>_filterCache.Count;
    internal void InitializeReadyGui()=>Styles();
    internal string FontSourceForTest=>_fontSource;
    internal string FontNameForTest=>_fontDisplayName;
    internal bool FontCyrillicForTest=>_fontCyrillic&&_font!=null&&!_font.WasCollected;
    internal bool FontRoundedForTest=>UiThemePolicy.RoundedFace(_fontDisplayName)&&FontCyrillicForTest;
    internal bool FontNativeForTest=>_fontSource=="native-loaded Font"&&!_ownsFont&&FontCyrillicForTest;
    internal int FontResourceScansForTest=>_fontResourceScans;
    internal int JellyHeldRepaintsForTest=>_jellyHeldRepaints;
    internal int JellyHoverRepaintsForTest=>_jellyHoverRepaints;
    internal float HoverLiftForTest
    {
        get{float lift=0;foreach(var motion in _hoverMotion.Values)lift=Mathf.Max(lift,-motion.Animation.Position);return lift;}
    }
    internal bool ThemePaletteForTest=>_parchment==Hex(0xF8F4EA)&&_parchment.a==1f&&
        _ink==Hex(0x2C2621)&&_parchmentRim==Hex(UiThemePolicy.Rim)&&_pastelCard==Hex(UiThemePolicy.Card);
    internal bool ThemeTexturesForTest=>_textures.Count==ThemeTextureBudget&&_textures.All(t=>
        t!=null&&!t.WasCollected&&t.filterMode==FilterMode.Bilinear&&t.wrapMode==TextureWrapMode.Clamp);
    internal bool TabLabelsFitForTest
    {
        get
        {
            if(_iconTab==null||_iconTabCompact==null||_iconTabSmall==null)return false;
            // 0.8.3: nine measured bookmarks (TabWidths) instead of nine equal slices.
            var widths=TabWidths(1160f-60f);
            for(int i=0;i<_tabs.Length;i++)
            {
                float width=widths[i]-45f;var style=IconTextStyle(_tabs[i],width);
                if(style.wordWrap||style.fontSize<14||style.CalcSize(ButtonContent(_tabs[i])).x>width)return false;
            }
            return true;
        }
    }
    // 0.8.3: nine bookmarks share one row. Each gets its icon plus its measured label (the largest
    // font at which the whole row still fits); the spare width is spread evenly (UiThemePolicy).
    private readonly float[] _tabWidths=new float[TabCount],_tabNeed=new float[TabCount];
    private float _tabWidthsFor=-1;private int _tabWidthsLanguage=-1;
    private float[] TabWidths(float total)
    {
        if(Mathf.Approximately(_tabWidthsFor,total)&&_tabWidthsLanguage==Loc.Generation)return _tabWidths;
        foreach(var style in new[]{_iconTab,_iconTabCompact,_iconTabSmall})
        {
            for(int i=0;i<_tabs.Length;i++)_tabNeed[i]=50f+Mathf.Ceil(style.CalcSize(ButtonContent(_tabs[i])).x);
            if(UiThemePolicy.TabWidths(_tabNeed,total,8f,_tabWidths))break; // else: equal slices, smallest font
        }
        _tabWidthsFor=total;_tabWidthsLanguage=Loc.Generation;
        return _tabWidths;
    }
    internal float TabCenterForTest(int index)
    {
        var widths=TabWidths(1160f-60f);float x=30;
        for(int i=0;i<index&&i<widths.Length;i++)x+=widths[i]+8;
        return x+widths[Mathf.Clamp(index,0,widths.Length-1)]*.5f;
    }
    internal bool ButtonLabelsFitForTest
    {
        get
        {
            if(_buttonText14==null)return false;
            var labels=new (string Text,float Width)[]{
                ("Эксперименты",UiThemePolicy.PillRowWidth(1100f,8f,_categories,3)),
                ("Слаймодождь",(1100f-24f)/4f-38f),
                ("Ноды карты",MapLayerOrder.FilterWidth(1100f,MapTools.Categories.Length)),
                ("Механизмы",MapLayerOrder.FilterWidth(1100f,MapTools.Categories.Length)),
                ("Поиск · метки · настройки",292f),("Обновить сцену",242f)};
            bool allFit=true;var metrics=new List<string>();
            foreach(var pair in labels)
            {
                var content=ButtonContent(pair.Text);var style=ButtonTextStyle(content,pair.Width);
                float measured=style.CalcSize(content).x;
                bool fit=!style.wordWrap&&style.fontSize>=14&&float.IsFinite(measured)&&measured<=pair.Width-4f;
                allFit&=fit;
                metrics.Add(pair.Text+": "+(fit?"PASS":"FAIL")+" font="+style.fontSize+
                    " measuredWithPadding="+measured.ToString("F2")+" face="+pair.Width.ToString("F2"));
            }
            _buttonLabelFitDiagnostics=string.Join("; ",metrics);
            return allFit;
        }
    }
    internal string ButtonLabelFitDiagnosticsForTest=>_buttonLabelFitDiagnostics;
    internal void ResetSession()
    {
        ClearInteraction();_selected=_ammoSelected=null;_filterCache.Clear();_filterGeneration=-1;
        _pendingTextWrites.Clear();_lastReturnedText="";
        _buttonLabels.Clear();MapZoneLabelRects.Clear();
        _hoverCapsule=null;_capsuleId="";_capsuleScroll=Vector2.zero;_capsuleLayout=null;_family="Все";
        _mapCardShown=null;_mapCardSlot=null;_atlasChipSource=null;
        _phenomenaPage=false;_phenomenaScroll=Vector2.zero;_phenomenaSelection="";_timePage=false;
        _infoPage=false;_capturePage=false;_saveNamePage=false;_saveNameDraft="";_farmPage=false;
        ResetOptimizationUi();
        PaintTelemetry.Reset();_tabClickAt=0;_tabClickIndex=-1;
        ResetExplorerUi();ResetCreationUi();_headerSlime=null;ResetM27Ui();
    }
    internal void Dispose()
    {
        ResetSession();_hoverStyles.Clear();_pressedStyles.Clear();_textShadows.Clear();_headerSlime=null;
        foreach(var texture in _textures)if(texture!=null&&!texture.WasCollected)UnityEngine.Object.Destroy(texture);
        _textures.Clear();
        if(_logoTexture!=null&&!_logoTexture.WasCollected)UnityEngine.Object.Destroy(_logoTexture);
        _logoTexture=null;
        _iconAtlas=_slimeKnob=_badgeCircle=_headerGradient=null;
        if(_ownsFont&&_font!=null&&!_font.WasCollected)UnityEngine.Object.Destroy(_font);
        _font=null;_ownsFont=false;
    }

    // Native IMGUI font tint is decoded to linear; compensate text only.
    // Texture/rectangle colors already arrive correctly and must not be changed.
    private static Color TextTint(Color color)
    {
        float Gamma(float v)=>(float)(v<=.0031308f?12.92*v:1.055*Math.Pow(v,1.0/2.4)-.055);
        return new Color(Gamma(color.r),Gamma(color.g),Gamma(color.b),color.a);
    }
    private Texture2D Solid(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.name="CreatorToolkit_UI_"+_textures.Count;
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.SetPixel(0, 0, color);
        texture.Apply(false, true);
        _textures.Add(texture);
        return texture;
    }

    // The game's stripped IL2CPP GUIStyle exposes no managed copy constructor.
    private readonly List<GUIStyleState> _paintStyleStates=new();
    private readonly List<RectOffset> _paintStyleOffsets=new();
    private GUIStyle CopyStyle(GUIStyle source)
    {
        var style = new GUIStyle { font = source.font, fontSize = source.fontSize, fontStyle = source.fontStyle,
            alignment = source.alignment, wordWrap = source.wordWrap, richText = source.richText, clipping=source.clipping,
            padding = new RectOffset(source.padding.left, source.padding.right, source.padding.top, source.padding.bottom),
            border = new RectOffset(source.border.left, source.border.right, source.border.top, source.border.bottom) };
        style.normal.textColor = source.normal.textColor; style.normal.background = source.normal.background;
        style.hover.textColor = source.hover.textColor; style.hover.background = source.hover.background;
        style.active.textColor = source.active.textColor; style.active.background = source.active.background;
        style.focused.textColor = source.focused.textColor; style.focused.background = source.focused.background;
        if(_paintStyleStates.Count<2048)
        {_paintStyleStates.Add(style.normal);_paintStyleStates.Add(style.hover);_paintStyleStates.Add(style.active);_paintStyleStates.Add(style.focused);}
        if(_paintStyleOffsets.Count<2048)
        {_paintStyleOffsets.Add(style.padding);_paintStyleOffsets.Add(style.border);_paintStyleOffsets.Add(style.margin);_paintStyleOffsets.Add(style.overflow);}
        return style;
    }

    private Texture2D Rounded(Color color, Color stroke)
    {
        const int size = 48;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name="CreatorToolkit_UI_"+_textures.Count;
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.filterMode=FilterMode.Bilinear;texture.wrapMode=TextureWrapMode.Clamp;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float qx=Math.Abs(x+.5f-size*.5f)-(size*.5f-14f),qy=Math.Abs(y+.5f-size*.5f)-(size*.5f-14f);
            float distance=Mathf.Sqrt(Mathf.Max(qx,0)*Mathf.Max(qx,0)+Mathf.Max(qy,0)*Mathf.Max(qy,0))+Mathf.Min(Mathf.Max(qx,qy),0)-14f;
            float coverage=Mathf.Clamp01(.5f-distance),inside=Mathf.Clamp01(-.5f-distance);
            Color pixel=Color.Lerp(stroke,color,inside);pixel.a*=coverage;texture.SetPixel(x,y,pixel);
        }
        texture.Apply(false, true); _textures.Add(texture); return texture;
    }

    private bool FontHasCyrillic(Font font)
    {
        if(font==null||font.WasCollected)return false;
        try
        {
            if(font.dynamic)font.RequestCharactersInTexture(UiThemePolicy.CyrillicProbe,20,FontStyle.Bold);
            foreach(char c in UiThemePolicy.CyrillicProbe)if(c!=' '&&!font.HasCharacter(c))return false;
            return true;
        }
        catch{return false;}
    }

    private void ResolveGameFont()
    {
        // A one-time native resource census, never a per-frame FindObjects scan.
        // TMP SDF-only fonts without a usable Unity Font are not passed to IMGUI.
        var rows=new List<string>();int best=-1;
        _fontResourceScans++;
        foreach(var candidate in Resources.FindObjectsOfTypeAll<Font>())
        {
            if(candidate==null||candidate.WasCollected)continue;
            try
            {
                string name=candidate.name??"unnamed";
                var families=new List<string>();
                try{foreach(string family in candidate.fontNames)families.Add(family);}catch{}
                string familyNames=string.Join(", ",families);
                int rank=UiThemePolicy.NativeFontRank(name+" "+familyNames,candidate.dynamic);
                bool glyphs=rank>=0&&FontHasCyrillic(candidate);
                rows.Add("RESOURCE "+name+" families=["+familyNames+"] dynamic="+candidate.dynamic+" EligibleCyrillic="+glyphs+" rank="+rank);
                if(glyphs&&rank>best){_font=candidate;_fontDisplayName=name;_fontRoundRank=best=rank;}
            }
            catch(Exception e){rows.Add("RESOURCE unavailable: "+e.GetType().Name);}
        }
        if(_font!=null){_fontSource="native-loaded Font";_ownsFont=false;}
        else
        {
            // The rounded OS fallback is explicitly reported, not called a game font.
            var installed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try{foreach(string name in Font.GetOSInstalledFontNames())installed.Add(name);}catch{}
            foreach(string name in new[]{"Arial Rounded MT Bold","Comic Sans MS","Trebuchet MS"})
            {
                if(installed.Count>0&&!installed.Contains(name))continue;
                Font fallback=null;
                try
                {
                    fallback=Font.CreateDynamicFontFromOSFont(name,20);
                    if(FontHasCyrillic(fallback))
                    {_font=fallback;_ownsFont=true;_fontDisplayName=name;_fontRoundRank=UiThemePolicy.NativeFontRank(name,true);_fontSource="rounded OS fallback / Bold";break;}
                }
                catch(Exception e){rows.Add("FALLBACK "+name+": "+e.GetType().Name);}
                if(fallback!=null&&!fallback.WasCollected)UnityEngine.Object.Destroy(fallback);
            }
        }
        if(_font==null)throw new InvalidOperationException("Не найден игровой или округлый шрифт с кириллицей.");
        _fontCyrillic=FontHasCyrillic(_font);
        if(_font.dynamic)
            foreach(int size in new[]{14,15,16,17,18,20,26,30})
            {
                _font.RequestCharactersInTexture(UiThemePolicy.CyrillicProbe,size,FontStyle.Bold);
                if(!_ownsFont)_font.RequestCharactersInTexture(UiThemePolicy.CyrillicProbe,size,FontStyle.Normal);
            }
        rows.Insert(0,"Source="+_fontSource+"\nSelected="+_fontDisplayName+"\nCyrillic="+_fontCyrillic+
            "\nRounded="+FontRoundedForTest+"\nResourceScans="+_fontResourceScans+"\nOwnsFont="+_ownsFont);
        try{File.WriteAllText(Path.Combine(_app.DataDirectory,"ui-fonts.txt"),"CreatorToolkit "+ToolkitMod.Version+
            " typography census\n"+string.Join("\n",rows)+"\nNative fonts are borrowed, not destroyed. Rounded fallback is explicit.\n");}
        catch(Exception e){_app.LoggerInstance.Warning("Font census report: "+e.Message);}
        _app.LoggerInstance.Msg("Toolkit font: "+_fontDisplayName+"; "+_fontSource+"; Cyrillic="+_fontCyrillic);
    }

    private Texture2D Surface(Color color,Color stroke,bool jelly=false,bool circle=false,bool glow=false,bool knob=false)
    {
        const int size=64;float radius=circle?32:20;
        var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
        texture.name="CreatorToolkit_UI_"+_textures.Count;texture.hideFlags=HideFlags.HideAndDontSave;
        texture.filterMode=FilterMode.Bilinear;texture.wrapMode=TextureWrapMode.Clamp;
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            float coverage=UiThemePolicy.Coverage(x,y,size,radius);
            float inside=UiThemePolicy.Coverage(x-1.4f,y-1.4f,size-2.8f,radius-1.4f);
            Color body=color;
            if(jelly)
            {
                float height=(y+.5f)/size;
                // Saturated toy-candy face. Depth comes from the separate lower
                // shadow, not an almost black chrome gradient across the face.
                float shade=knob ? .68f : UiThemePolicy.JellyShadeFloor(color==_pink?0xFF6B7A:0u);
                body=Color.Lerp(color*shade,color,Mathf.Pow(height,1.4f));body.a=color.a;
                float highlight=Mathf.Exp(-Mathf.Pow((height-.84f)/.065f,2))*.48f;
                body=Color.Lerp(body,new Color(.94f,1f,1f,color.a),highlight);
                // Horizontal variation across a sliced pill caused visible vertical seams.
                float glint=knob?Mathf.Exp(-((x-20f)*(x-20f)/180f+(y-49f)*(y-49f)/26f))*.34f:0f;
                body=Color.Lerp(body,Color.white,glint);
            }
            if(knob&&coverage>0)
            {
                float eye=Mathf.Max(Mathf.Exp(-((x-23f)*(x-23f)+(y-29f)*(y-29f))/5f),
                    Mathf.Exp(-((x-40f)*(x-40f)+(y-29f)*(y-29f))/5f));
                body=Color.Lerp(body,new Color(.035f,.10f,.18f,1f),eye);
            }
            Color pixel=Color.Lerp(stroke,body,inside);
            pixel.a*=glow?coverage*.38f:coverage;
            texture.SetPixel(x,y,pixel);
        }
        texture.Apply(false,true);_textures.Add(texture);return texture;
    }

    private GUIStyle SurfaceStyle(Texture2D background,int border=20)
    {
        var style=CopyStyle(GUI.skin.box);style.border=new RectOffset(border,border,border,border);
        style.padding=new RectOffset(0,0,0,0);style.normal.background=background;return style;
    }

    private void Styles()
    {
        if (_label != null) return;
        ResolveGameFont();
        // Exactly fifteen shared surfaces + one existing icon atlas. No per-widget textures.
        var frame=Surface(_parchment,_parchmentRim);
        var card=Surface(_pastelCard,_parchmentRim);
        var field=Surface(Hex(0xFFFDF5),Hex(0xB8A990));
        var teal=Surface(_accent,Hex(0x159888),true);
        var pink=Surface(_pink,Hex(0xC94759),true);
        var gold=Surface(_sun,Hex(0xB98516),true);
        var violet=Surface(_violet,Hex(0x8B70AE),true);
        var deep=Surface(Hex(0xF5EEDB),Hex(0xB8A990),true);
        _slimeKnob=Surface(new Color(.36f,1f,.89f,1f),new Color(.70f,1f,.95f,1f),true,true,false,true);
        _badgeCircle=Surface(new Color(.78f,.98f,.98f,1f),new Color(.30f,.88f,.82f,1f),true,true);
        var track=Surface(Hex(0xDDD1B9),Hex(0xA59071));
        var fill=Surface(_accent,new Color(.52f,1f,.88f,1f),true);
        var shadow=Surface(new Color(.45f,.34f,.23f,.22f),new Color(.45f,.34f,.23f,.12f));
        var glow=Surface(new Color(.13f,.65f,.55f,.25f),new Color(.13f,.65f,.55f,.08f),false,false,true);
        _headerGradient=new Texture2D(128,2,TextureFormat.RGBA32,false);
        _headerGradient.name="CreatorToolkit_UI_HeaderGradient";_headerGradient.hideFlags=HideFlags.HideAndDontSave;
        _headerGradient.filterMode=FilterMode.Bilinear;_headerGradient.wrapMode=TextureWrapMode.Clamp;
        for(int x=0;x<128;x++)for(int y=0;y<2;y++)
        {_headerGradient.SetPixel(x,y,Color.Lerp(_pink,_accent,x/127f));}
        _headerGradient.Apply(false,true);_textures.Add(_headerGradient);
        _label = CopyStyle(GUI.skin.label);
        _label.font = _font; _label.fontSize = 20; _label.alignment = TextAnchor.MiddleLeft;
        _label.fontStyle=_ownsFont?FontStyle.Bold:FontStyle.Normal;
        _label.clipping=TextClipping.Clip;
        _label.richText = false; _label.normal.textColor = TextTint(Hex(UiThemePolicy.Body));
        _muted = CopyStyle(_label); _muted.fontSize = 18; _muted.wordWrap = true;
        _muted.fontSize=17;_muted.normal.textColor = TextTint(Hex(0x665749));
        _selectedRowText=CopyStyle(_muted);_selectedRowText.normal.textColor=TextTint(_ink);
        // The narrow atlas sidebar is not a normal 18 px menu paragraph.
        // Reserve exactly two explicit help lines; wrapping a long first line
        // used to push the safety warning outside its clipped 40 px rectangle.
        _atlasDetail=CopyStyle(_muted);_atlasDetail.fontSize=16;_atlasDetail.alignment=TextAnchor.UpperLeft;
        _atlasDetail.padding=new RectOffset(0,0,0,0);
        _atlasHelp=CopyStyle(_atlasDetail);_atlasHelp.wordWrap=false;
        _small=CopyStyle(_atlasDetail);_small.fontSize=14;_small.wordWrap=false;_small.alignment=TextAnchor.MiddleLeft;
        // M29: two-line search chip in the creator atlas header (dark ink on the teal pill).
        _atlasChipHead=CopyStyle(_small);_atlasChipHead.fontSize=13;_atlasChipHead.fontStyle=FontStyle.Bold;_atlasChipHead.normal.textColor=TextTint(_ink);
        _atlasChip=CopyStyle(_atlasChipHead);_atlasChip.fontSize=16;
        _wrap = CopyStyle(_label); _wrap.wordWrap = true;
        _badge=CopyStyle(_label);_badge.fontSize=14;_badge.alignment=TextAnchor.MiddleCenter;_badge.padding=new RectOffset(0,0,0,0);
        _zoneLabel=CopyStyle(_muted);_zoneLabel.fontStyle=FontStyle.Bold;_zoneLabel.alignment=TextAnchor.MiddleCenter;_zoneLabel.padding=new RectOffset(10,10,5,5);
        _zoneLabel.normal.background=frame;_zoneLabel.border=new RectOffset(20,20,20,20);_zoneLabel.normal.textColor=TextTint(_ink);
        _heading = CopyStyle(_label); _heading.fontSize = 26; _heading.fontStyle = FontStyle.Bold;
        _heading.normal.textColor=TextTint(Hex(UiThemePolicy.Heading));
        _tableHeading=CopyStyle(_heading);_tableHeading.fontSize=20;_tableHeading.wordWrap=false;
        _title = CopyStyle(_heading); _title.fontSize = 30; _title.normal.textColor = TextTint(_ink);
        _button = CopyStyle(GUI.skin.button);
        _button.font = _font; _button.fontSize = 17; _button.alignment = TextAnchor.MiddleCenter;
        _button.fontStyle=FontStyle.Bold;_button.clipping=TextClipping.Clip;_button.wordWrap=false;
        _button.richText = false; _button.border = new RectOffset(20,20,20,20);
        _button.padding = new RectOffset(10,10,4,4);
        _button.normal.background = deep;
        _button.hover.background = violet;
        _button.active.background = deep;
        _button.normal.textColor = _button.hover.textColor = _button.active.textColor = TextTint(_ink);
        _buttonText17=CopyStyle(_button);
        _buttonText17.normal.background=_buttonText17.hover.background=_buttonText17.active.background=_buttonText17.focused.background=null;
        _buttonText16=CopyStyle(_buttonText17);_buttonText16.fontSize=16;
        _buttonText15=CopyStyle(_buttonText17);_buttonText15.fontSize=15;
        _buttonText14=CopyStyle(_buttonText17);_buttonText14.fontSize=14;
        _selectedButton = CopyStyle(_button);
        _selectedButton.normal.background = _selectedButton.hover.background = _selectedButton.active.background = teal;
        _selectedButton.normal.textColor = TextTint(_ink);
        _accentButton = CopyStyle(_button);
        _accentButton.normal.background = _accentButton.hover.background = _accentButton.active.background = pink;
        _accentButton.normal.textColor = _accentButton.hover.textColor = TextTint(_ink);
        _field = CopyStyle(GUI.skin.box);
        _field.font = _font; _field.fontSize = 20; _field.alignment = TextAnchor.MiddleLeft;
        _field.border = new RectOffset(16,16,16,16); _field.padding = new RectOffset(14,14,4,4);
        _field.normal.background = _field.focused.background = field;
        _field.fontStyle=_label.fontStyle;
        _field.normal.textColor = _field.focused.textColor = TextTint(_ink);
        _card = SurfaceStyle(card);
        _frame = CopyStyle(_card);
        _frame.normal.background = frame;
        _tabStyle = CopyStyle(_button); _tabStyle.fontSize = 17;
        _iconTab=CopyStyle(_label);_iconTab.fontSize=16;_iconTab.fontStyle=FontStyle.Bold;_iconTab.alignment=TextAnchor.MiddleCenter;_iconTab.padding=new RectOffset(0,0,0,0);
        _iconTab.wordWrap=false;
        _iconTabCompact=CopyStyle(_iconTab);_iconTabCompact.fontSize=15;
        _iconTabSmall=CopyStyle(_iconTab);_iconTabSmall.fontSize=14;
        _hudFrame=CopyStyle(_card);
        _hudFrame.normal.background=frame;
        _hudLabel=CopyStyle(_label);_hudLabel.fontSize=18;
        _hudEffects=CopyStyle(_muted);_hudEffects.fontSize=16;_hudEffects.alignment=TextAnchor.UpperLeft;
        _hudEffects.normal.textColor=TextTint(Hex(0x665749));
        _goldButton=CopyStyle(_button);_goldButton.normal.background=_goldButton.hover.background=_goldButton.active.background=gold;
        _violetButton=CopyStyle(_button);_violetButton.normal.background=_violetButton.hover.background=_violetButton.active.background=violet;
        _shadowBox=SurfaceStyle(shadow);_glowBox=SurfaceStyle(glow);
        _trackStyle=SurfaceStyle(track,20);_fillStyle=SurfaceStyle(fill,20);
        _sliderTrackPaint=track;_sliderFillPaint=fill;_sliderGlowPaint=glow;
        _tabWidthsFor=-1;
        AtlasStyles();
        _titleGradient=new GUIStyle[8];
        for(int i=0;i<_titleGradient.Length;i++)
        {
            _titleGradient[i]=CopyStyle(_title);
            _titleGradient[i].normal.textColor=TextTint(_ink);
        }
        if(_app.Ready)_headerSlime=_app.Actors.Catalog.FirstOrDefault(e=>e.Category=="Слаймы"&&!e.IsPrototype)?.Type?.Icon;
        LoadIconAtlas();
        if(_textures.Count!=ThemeTextureBudget)throw new InvalidDataException("Shared jelly theme texture budget mismatch.");
        BuildLogo();
    }

    private void LoadIconAtlas()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("CreatorToolkit.CreatorAtlas.rgba");
        if(stream==null)throw new InvalidDataException("Creator icon atlas missing.");
        using var reader=new BinaryReader(stream);byte[] rgba=reader.ReadBytes(256*256*4);
        if(rgba.Length!=256*256*4)throw new InvalidDataException("Creator icon atlas truncated.");
        var texture=new Texture2D(256,256,TextureFormat.RGBA32,false);
        texture.name="CreatorToolkit_UI_IconAtlas";texture.hideFlags=HideFlags.HideAndDontSave;
        texture.filterMode=FilterMode.Bilinear;texture.wrapMode=TextureWrapMode.Clamp;_textures.Add(texture);
        for(int y=0;y<256;y++)for(int x=0;x<256;x++)
        {int i=(y*256+x)*4;texture.SetPixel(x,255-y,new Color(rgba[i]/255f,rgba[i+1]/255f,rgba[i+2]/255f,rgba[i+3]/255f));}
        texture.Apply(false,true);_iconAtlas=texture;
    }
    private void Icon(Rect rect,int index)
    {
        if(_iconAtlas==null)return;
        // Same validated clipped DrawTexture path as native sprite previews.
        BeginGroup(rect);
        try{GUI.DrawTexture(new Rect(-(index%4)*rect.width,-(index/4)*rect.height,rect.width*4,rect.height*4),_iconAtlas);}
        finally{EndGroup();}
    }
    private bool FandomIcon(Rect rect,int index)
    {
        var atlas=index<0?null:FandomMapMarkers.Atlas;
        if(atlas==null)return false;
        int columns=FandomMapMarkers.AtlasColumns,rows=FandomMapMarkers.AtlasRows;
        BeginGroup(rect);
        try{GUI.DrawTexture(new Rect(-(index%columns)*rect.width,-(index/columns)*rect.height,rect.width*columns,rect.height*rows),atlas);}
        finally{EndGroup();}
        return true;
    }
    // Capsule / map-node / drone badges are the exact Fandom wiki map markers
    // (CanonMapIconPolicy cells of FandomAtlas.rgba, nothing mod-drawn); false
    // lets the caller fall back to the native sprite.
    private bool _canonIconLogged;
    private bool CanonIcon(Rect rect,int index)
    {
        if(index<0)return false;
        bool drawn=FandomIcon(rect,index);
        if(!_canonIconLogged){_canonIconLogged=true;_app.LoggerInstance.Msg("Canon map icons: Fandom wiki markers "+(drawn?"loaded":"unavailable"));}
        return drawn;
    }
    private static string FandomSummary(MapTools.Point point)
    {
        string text=FandomMapMarkers.IsFandom(point?.Category)?point.Detail?.Split('\n')[0]:point?.WikiHint;
        if(string.IsNullOrEmpty(text)||text==FandomMapMarkers.SourceNote||text==FandomMapMarkers.ExactNote)return null;
        int wiki=text.IndexOf("Вики: ",StringComparison.Ordinal);
        if(wiki>0&&FandomMapMarkers.IsFandom(point.Category))text=text.Substring(wiki);
        text=Loc.T(text);
        return text.Length>90?text.Substring(0,87)+"…":text;
    }
    private static int MapIcon(string category)=>category switch
    {"Капсулы"=>0,"Дроны"=>1,"Телепорты"=>2,"Ресурсы"=>3,"Ноды карты"=>4,"Гордо"=>5,"Двери"=>6,"Механизмы"=>7,_=>15};
    private GUIStyle IconTextStyle(string text,float width)
    {
        // Native Cyrillic faces are wider than the former OS fallback. Keep
        // each bookmark on one line and its hit rectangle unchanged. Styles
        // and content are bounded cached objects, never recreated per repaint.
        var content=ButtonContent(text);
        if(_iconTab.CalcSize(content).x<=width)return _iconTab;
        if(_iconTabCompact.CalcSize(content).x<=width)return _iconTabCompact;
        return _iconTabSmall;
    }
    private bool IconButton(Rect rect,string text,int icon,bool selected,Rect? hitRect=null,GUIStyle flavor=null)
    {
        Rect hit=hitRect??rect;
        bool clicked=Button(hit,GUIContent.none,selected?_selectedButton:(flavor??_button),rect);
        float offset=HoverOffset(hit,CanHover(hit),
            InputEnabled&&Mouse.current!=null&&Mouse.current.leftButton.isPressed);
        Rect badge=HoverChildRect(rect,new Rect(rect.x+6,rect.y+(rect.height-32)/2-2,32,32),offset);
        GUI.DrawTexture(badge,_badgeCircle);
        Icon(HoverChildRect(rect,new Rect(rect.x+9,rect.y+(rect.height-32)/2+1,26,26),offset),icon);
        HoverText(HoverChildRect(rect,new Rect(rect.x+40,rect.y-2,rect.width-45,rect.height),offset),text,IconTextStyle(text,rect.width-45),offset);
        return clicked;
    }

    private bool TabButton(Rect rect,string text,int index,bool selected)
    {
        // The hit area stays fixed. Only genuine native Slimepedia sprites
        // appear here; unavailable art is not replaced with a schematic icon.
        // 0.8.5: the open tab rises smoothly instead of jumping 3 px.
        float rise=index>=0&&index<_tabRise.Length?GlideTo(ref _tabRise[index],selected?3f:0f,UiMotionPolicy.SwitchSeconds):selected?3f:0f;
        Rect visual=new(rect.x,rect.y-rise,rect.width,rect.height+rise);
        GUIStyle flavor=index switch {0 or 2=>_accentButton,1 or 3=>_goldButton,4 or 5=>_violetButton,_=>_button};
        bool clicked=Button(rect,GUIContent.none,selected?_selectedButton:flavor,visual);
        float offset=HoverOffset(rect,CanHover(rect),
            InputEnabled&&Mouse.current!=null&&Mouse.current.leftButton.isPressed&&rect.Contains(Pointer()));
        var sprite=_app.Map.Icons.Tab(index);
        if(sprite!=null)DrawSprite(HoverChildRect(visual,new Rect(visual.x+6,visual.y+6,32,32),offset),sprite);
        HoverText(HoverChildRect(visual,new Rect(visual.x+40,visual.y-2,visual.width-45,visual.height),offset),
            text,IconTextStyle(text,visual.width-45),offset);
        return clicked;
    }
    private bool NativeRoleButton(Rect rect,string text,string role,bool selected=false)
    {
        bool clicked=Button(rect,GUIContent.none,selected?_selectedButton:_button);
        float offset=HoverOffset(rect,CanHover(rect),
            CanHover(rect)&&Mouse.current!=null&&Mouse.current.leftButton.isPressed);
        var sprite=_app.Map.Icons.Role(role);
        string caption=sprite!=null?text:text+" · нет иконки";
        var content=ButtonContent(caption);content.tooltip=_app.Map.Icons.RoleDiagnostic(role);
        float iconWidth=sprite!=null?38:0;
        if(sprite!=null)DrawSprite(HoverChildRect(rect,new Rect(rect.x+6,rect.y+3,32,32),offset),sprite);
        HoverText(HoverChildRect(rect,new Rect(rect.x+iconWidth,rect.y-2,rect.width-iconWidth,rect.height),offset),
            content,IconTextStyle(caption,rect.width-iconWidth),offset);
        return clicked;
    }

    private GUIStyle TextShadowStyle(GUIStyle style)
    {
        if(!_textShadows.TryGetValue(style.Pointer,out var shadow))
        {
            shadow=CopyStyle(style);shadow.normal.background=null;shadow.hover.background=null;
            shadow.active.background=null;shadow.focused.background=null;
            shadow.normal.textColor=new Color(.006f,.015f,.04f,.78f);
            _textShadows[style.Pointer]=shadow;
        }
        return shadow;
    }
    private void ShadowText(Rect rect,string text,GUIStyle style)=>ShadowText(rect,ButtonContent(text),style);
    private void ShadowText(Rect rect,GUIContent content,GUIStyle style)
    {
        // Native parchment typography is crisp dark ink, not white lettering
        // with two dark ghost outlines. Jelly depth belongs to the button face.
        L.Label(rect,content,style);
    }

    private void GradientTitle(Rect rect)
    {
        const string text="CREATOR TOOLKIT";
        L.Label(rect,text,_title);
        // 0.8.5: a pixel-step pink-to-mint underline that matches the pixel logo.
        for(int i=0;i<26;i++)Fill(new Rect(rect.x+i*15,rect.yMax-1,12,4),Color.Lerp(_pink,_accent,i/25f));
    }

    private static Texture2D _fillWhite;
    private static GUIContent _emptyContent;
    private static GUIContent EmptyContent=>_emptyContent??=GUIContent.none;
    private Event _guiEventRoot;
    private GUISkin _guiSkinRoot;
    private GUIStyle _guiEmptyStyleRoot;
    private GUIContent _guiTempTextRoot,_guiTempImageRoot,_guiTempTextImageRoot;
    private Mouse _uiMouseRoot;
    private UnityEngine.InputSystem.InputControl _mousePositionRoot,_mouseLeftRoot,_mouseRightRoot;
    private readonly Dictionary<IntPtr,Texture2D> _roundedButtonPaint=new();
    private Il2Cpp.SceneContext _uiContextRoot;
    private const int NativePaintCacheBudget=1024;
    private readonly Dictionary<IntPtr,Texture2D> _nativePaintTextures=new();
    private IntPtr _paintSession;
    private static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, _fillWhite??=Texture2D.whiteTexture);
        GUI.color = previous;
    }

    // Nine non-overlapping clipped DrawTexture regions. The game's stripped
    // DrawTextureWithTexCoords wrapper is deliberately never used.
    // Source corners stay 20/64; destination corners shrink to the actual
    // capsule radius instead of overlapping fixed GUIStyle borders.
    private void DrawRoundedSurface(Rect rect,Texture2D surface,float wantedRadius)
    {
        if(surface==null||rect.width<=0||rect.height<=0||Event.current?.type!=EventType.Repaint)return;
        // Snap ALL shared slice edges to physical pixels, including nested groups.
        float pixelScale=Mathf.Max(.1f,_scale);
        float SnapX(float x)=>Mathf.Round((_groupOrigin.x+x)*pixelScale)/pixelScale-_groupOrigin.x;
        float SnapY(float y)=>Mathf.Round((_groupOrigin.y+y)*pixelScale)/pixelScale-_groupOrigin.y;
        rect=Rect.MinMaxRect(SnapX(rect.x),SnapY(rect.y),SnapX(rect.xMax),SnapY(rect.yMax));
        float radius=UiThemePolicy.CapsuleRadius(rect.width,rect.height,wantedRadius);
        // 0.8.5: neighbouring slices overlap by one physical pixel. IMGUI clips every group with a soft
        // edge, so two slices meeting exactly on one pixel line left a light seam (a pill's right cap).
        float bleed=radius>0?1f/pixelScale:0f;
        for(int row=0;row<3;row++)
        for(int col=0;col<3;col++)
        {
            float sx=col==0?0:col==1?20:44,sy=row==0?0:row==1?20:44;
            float sourceW=col==1?24:20,sourceH=row==1?24:20;
            float dx=col==0?0:col==1?radius:rect.width-radius;
            float dy=row==0?0:row==1?radius:rect.height-radius;
            float dw=col==1?rect.width-2*radius:radius;
            float dh=row==1?rect.height-2*radius:radius;
            if(dw<=0||dh<=0)continue;
            float scaleX=dw/sourceW,scaleY=dh/sourceH;
            float left=SnapX(rect.x+dx),top=SnapY(rect.y+dy);
            float right=SnapX(rect.x+dx+dw),bottom=SnapY(rect.y+dy+dh);
            scaleX=(right-left)/sourceW;scaleY=(bottom-top)/sourceH;
            float cl=col>0?left-bleed:left,ct=row>0?top-bleed:top,cr=col<2?right+bleed:right,cb=row<2?bottom+bleed:bottom;
            BeginGroup(new Rect(cl,ct,cr-cl,cb-ct));
            try{GUI.DrawTexture(new Rect(left-cl-sx*scaleX,top-ct-sy*scaleY,64*scaleX,64*scaleY),surface);}
            finally{EndGroup();}
        }
    }

    internal void ClearInteraction()
    {
        _dragSlider = _dragScroll = -1;
        _carouselDrag=_carouselTap=-1;_carouselDragMoved=false;
        Array.Clear(_sliderMotion);
        _hoverMotion.Clear();_scrollMotion.Clear();_switchMotion.Clear();_glideMotion.Clear();
        BlurTextInput();
        if(!_optQueued){_optDraftChanged=false;_optDraftReady=false;}
    }

    // IMGUI's native event stream can differ from InputSystem in this player.
    // Track group coordinates explicitly and read the same native mouse device
    // used by gameplay; dragging does not depend on a stripped GUI slider.
    private void BeginGroup(Rect rect)
    {
        _groupOrigins.Push(_groupOrigin);
        _groupOrigin += rect.position;
        _groupRects.Push(new Rect(_groupOrigin,rect.size));
        GUI.BeginGroup(rect);
    }
    private void EndGroup()
    {
        try { GUI.EndGroup(); }
        finally { _groupOrigin = _groupOrigins.Pop();_groupRects.Pop(); }
    }
    private Vector2 Pointer()
    {
        var mouse = Mouse.current;
        if (mouse == null) return new Vector2(-10000, -10000);
        var p = mouse.position.ReadValue();
        return new Vector2(p.x / _scale, (Screen.height - p.y) / _scale) - _groupOrigin;
    }
    private bool PointerWithinGroups()
    {
        var global=Pointer()+_groupOrigin;
        foreach(var clip in _groupRects)if(!clip.Contains(global))return false;
        return true;
    }
    private bool Pressed(Rect rect)
    {
        if (_pointerFrame != Time.frameCount)
        {
            _pointerFrame = Time.frameCount;
            _pointerConsumed = false;
        }
        if (!InputEnabled || _pointerConsumed || Mouse.current == null ||
            !Mouse.current.leftButton.wasPressedThisFrame || !PointerWithinGroups() || !rect.Contains(Pointer())) return false;
        _pointerConsumed = true;
        BlurTextInput();
        ConsumePointerEvent();
        return true;
    }
    private bool Button(Rect rect,string text,GUIStyle style)
        =>Button(rect,ButtonContent(text),style);
    // 0.8.0: cached content carries the translated text (Loc.T); the cache is keyed by the source
    // text and dropped on a language switch (SyncLanguage).
    private GUIContent ButtonContent(string text)
    {
        if(!_buttonLabels.TryGetValue(text,out var content))
        {
            if(_buttonLabels.Count>512)_buttonLabels.Clear();
            content=new GUIContent(Loc.T(text));_buttonLabels[text]=content;
        }
        return content;
    }
    // Typed text (text boxes): measured as is, never translated.
    private readonly Dictionary<string,GUIContent> _rawLabels=new();
    private GUIContent RawContent(string text)
    {
        if(!_rawLabels.TryGetValue(text,out var content))
        {
            if(_rawLabels.Count>256)_rawLabels.Clear();
            content=new GUIContent(text);_rawLabels[text]=content;
        }
        return content;
    }
    private int _languageGeneration=-1;
    // Called at the top of every IMGUI entry point: a game language switch rebuilds text caches.
    internal void SyncLanguage()
    {
        if(_languageGeneration==Loc.Generation)return;
        _languageGeneration=Loc.Generation;
        _buttonLabels.Clear();_ellipsized.Clear();_confirmText=null;_footerSource=null;
        OnLanguageChanged();
    }
    partial void OnLanguageChanged();
    private GUIStyle ButtonTextStyle(GUIContent content,float width)
    {
        // Four lifetime-cached native-font sizes, including the normal padding.
        // Keep the physical hitbox and jelly surface unchanged; do not create
        // styles/content per repaint or silently shrink below the 14 px floor.
        float available=Mathf.Max(0,width-4f);
        if(_buttonText17.CalcSize(content).x<=available)return _buttonText17;
        if(_buttonText16.CalcSize(content).x<=available)return _buttonText16;
        if(_buttonText15.CalcSize(content).x<=available)return _buttonText15;
        return _buttonText14;
    }
    // A status that shares a row with buttons must never be clipped by its rect: one 17 px line,
    // else one 14 px line, else head + detail on two 14 px lines (detail ellipsized only if still too wide).
    private void RowStatus(Rect rect,string head,string detail)
    {
        string full=string.IsNullOrEmpty(detail)?head:head+" · "+detail;
        var content=ButtonContent(full);
        if(_muted.CalcSize(content).x<=rect.width){L.Label(rect,content,_muted);return;}
        if(_small.CalcSize(content).x<=rect.width){L.Label(rect,content,_small);return;}
        float line=Mathf.Ceil(_small.lineHeight)+3f,top=rect.center.y-line;
        L.Label(new Rect(rect.x,top,rect.width,line),ButtonContent(head),_small);
        L.Label(new Rect(rect.x,top+line,rect.width,line),ButtonContent(Ellipsize(detail,rect.width,_small)),_small);
    }
    private readonly GUIContent _measureContent=new();
    // 0.8.5: one cached result per text, width and style (it was a single slot that every list row
    // missed, so each row re-measured every GUI event) and a binary search for the longest fitting
    // prefix instead of one measurement per dropped letter.
    private readonly Dictionary<(string Text,int Width,IntPtr Style),string> _ellipsized=new();
    private string Ellipsize(string text,float width,GUIStyle style)
    {
        if(string.IsNullOrEmpty(text))return text??"";
        text=Loc.T(text);
        var key=(text,Mathf.RoundToInt(width*4f),style.Pointer);
        if(_ellipsized.TryGetValue(key,out var cached))return cached;
        string result=text;_measureContent.text=text;
        if(style.CalcSize(_measureContent).x>width)
        {
            result="…";
            int lo=1,hi=text.Length-1;
            while(lo<=hi)
            {
                int n=(lo+hi)/2;
                string cut=text.Substring(0,n).TrimEnd()+"…";_measureContent.text=cut;
                if(style.CalcSize(_measureContent).x<=width){result=cut;lo=n+1;}else hi=n-1;
            }
        }
        if(_ellipsized.Count>=1024)_ellipsized.Clear();
        _ellipsized[key]=result;
        return result;
    }
    private bool Button(Rect rect,GUIContent content,GUIStyle style,Rect? visualBounds=null)
    {
        long allocationStamp=GC.GetAllocatedBytesForCurrentThread();
        GUIStyle rendered=style;
        bool hovered=CanHover(rect);
        bool held=hovered&&Mouse.current!=null&&Mouse.current.leftButton.isPressed;
        if(hovered)
        {
            var cache=held?_pressedStyles:_hoverStyles;
            if(!cache.TryGetValue(style.Pointer,out rendered))
            {
                rendered=CopyStyle(style);
                rendered.normal.background=held?style.active.background:style.hover.background;
                rendered.normal.textColor=held?style.active.textColor:style.hover.textColor;
                cache[style.Pointer]=rendered;
            }
        }
        // Use the very same native InputSystem pointer as sliders/scrollbars.
        // Native legacy controls ignore queued device state in this player.
        AllocationMark(20,ref allocationStamp);
        float offset=HoverOffset(rect,hovered,held);
        AllocationMark(21,ref allocationStamp);
        Rect visual=visualBounds??rect;
        // 0.8.5: the hover glow fades in and out with the lift instead of blinking.
        float glow=held?1f:Mathf.Clamp01(offset/UiThemePolicy.HoverLift);
        Color beforeGlow=GUI.color;
        if(glow<1f)GUI.color=new Color(beforeGlow.r,beforeGlow.g,beforeGlow.b,beforeGlow.a*glow);
        if(visual.height<40f)
        {
            // 0.8.3: below 40 px the fixed 20 px GUIStyle borders of glow and shadow overlap and leave a
            // contour above small pills (tile «Мои ×»); paint them with the same radius-clamped slices.
            if(glow>.01f)DrawRoundedSurface(new Rect(visual.x-2,visual.y-2,visual.width+4,visual.height+4),_glowBox.normal.background,Mathf.Min(20f,visual.height*.5f+2f));
            GUI.color=beforeGlow;
            DrawRoundedSurface(visual,_shadowBox.normal.background,Mathf.Min(20f,visual.height*.5f));
        }
        else
        {
            if(glow>.01f)L.Box(new Rect(visual.x-2,visual.y-2,visual.width+4,visual.height+4),GUIContent.none,_glowBox);
            GUI.color=beforeGlow;
            L.Box(visual,GUIContent.none,_shadowBox);
        }
        Rect face=HoverChildRect(visual,new Rect(visual.x,visual.y,visual.width,Mathf.Max(4,visual.height-UiThemePolicy.RestDepth)),offset);
        { // All pill sizes share the same seam-free radius-clamped paint path.
            if(!_roundedButtonPaint.TryGetValue(rendered.Pointer,out var surface)||surface==null||surface.WasCollected)
            {surface=rendered.normal.background;if(_roundedButtonPaint.Count<1024)_roundedButtonPaint[rendered.Pointer]=surface;}
            PaintFace(rect,face,surface);
        }

        AllocationMark(22,ref allocationStamp);
        HoverText(face,content,ButtonTextStyle(content,face.width/UiThemePolicy.HoverScale(offset)),offset);
        AllocationMark(23,ref allocationStamp);
        if(Event.current?.type==EventType.Repaint)
        {if(held)_jellyHeldRepaints++;else if(hovered)_jellyHoverRepaints++;}
        AllocationMark(24,ref allocationStamp);
        return Pressed(rect);
    }
    // 0.8.5: a face that changes its paint (hover, press, selection) cross-fades instead of switching.
    private struct SurfaceMotion{internal Texture2D Current,Previous;internal float Since;}
    private readonly Dictionary<(float X,float Y,float W,float H),SurfaceMotion> _surfaceMotion=new();
    private void PaintFace(Rect hit,Rect face,Texture2D surface)
    {
        float radius=Mathf.Min(20f,face.height*.5f);
        if(surface==null||Event.current?.type!=EventType.Repaint){DrawRoundedSurface(face,surface,radius);return;}
        var key=(_groupOrigin.x+hit.x,_groupOrigin.y+hit.y,hit.width,hit.height);
        float now=Time.unscaledTime;
        if(!_surfaceMotion.TryGetValue(key,out var motion))
        {
            if(_surfaceMotion.Count>=512)_surfaceMotion.Clear();
            motion=new SurfaceMotion{Current=surface,Since=-10f};_surfaceMotion[key]=motion;
        }
        else if(motion.Current is null||motion.Current.Pointer!=surface.Pointer)
        {motion.Previous=motion.Current;motion.Current=surface;motion.Since=now;_surfaceMotion[key]=motion;}
        float shown=UiMotionPolicy.Fade(now-motion.Since,UiMotionPolicy.SurfaceFadeSeconds);
        if(shown>=1f||motion.Previous is null||motion.Previous.WasCollected){DrawRoundedSurface(face,surface,radius);return;}
        DrawRoundedSurface(face,motion.Previous,radius);
        Color before=GUI.color;
        GUI.color=new Color(before.r,before.g,before.b,before.a*shown);
        try{DrawRoundedSurface(face,surface,radius);}
        finally{GUI.color=before;}
    }
    // 0.8.5: a value easing toward its target, advanced once per frame (OnGUI runs several events a frame).
    private struct FrameGlide{internal UiMotionPolicy.Glide Glide;internal int Frame;}
    private static float GlideTo(ref FrameGlide glide,float target,float duration)
    {
        float dt=glide.Frame!=Time.frameCount?Time.unscaledDeltaTime:0f;glide.Frame=Time.frameCount;
        return UiMotionPolicy.Step(ref glide.Glide,target,dt,duration);
    }
    private readonly FrameGlide[] _tabRise=new FrameGlide[16];
    // 0.8.5: the footer line is trimmed once per new status (not every frame) and fades in when a
    // status that stood still for a second changes; a status that changes every frame never blinks.
    private string _footerSource,_footerText="";private float _footerSince=-10f,_footerChanged=-10f;
    private string FooterStatus(string raw,out float shown)
    {
        raw??="";
        if(!ReferenceEquals(raw,_footerSource)&&!string.Equals(raw,_footerSource,StringComparison.Ordinal))
        {
            bool first=_footerSource==null;float now=Time.unscaledTime;
            _footerSource=raw;
            string status=Loc.T(raw);
            int newline=status.IndexOf('\n'); if(newline>=0)status=status.Substring(0,newline);
            if(status.Length>94)status=status.Substring(0,91)+"…";
            _footerText=status;
            if(first)_footerSince=-10f;else if(now-_footerChanged>=1f)_footerSince=now;
            _footerChanged=now;
        }
        shown=UiMotionPolicy.Fade(Time.unscaledTime-_footerSince,UiMotionPolicy.StatusFadeSeconds);
        return _footerText;
    }
    // 0.8.5: allocation-free rectangle tests of the map overlay (each LINQ lambda allocated a closure per marker).
    private static readonly int[] ZoneLabelColumns={0,-1,1};
    private static bool AnyContains(List<Rect> rects,Vector2 p)=>MapTools.AnyContains(rects,p);
    private static bool AnyOverlaps(List<Rect> rects,Rect r){for(int i=0;i<rects.Count;i++)if(rects[i].Overlaps(r))return true;return false;}
    private static bool AnyOverlaps(Rect[] rects,Rect r){for(int i=0;i<rects.Length;i++)if(rects[i].Overlaps(r))return true;return false;}
    private float HoverOffset(Rect rect,bool hovered,bool held)
    {
        var key=(_groupOrigin.x+rect.x,_groupOrigin.y+rect.y,rect.width,rect.height);
        if(!_hoverMotion.TryGetValue(key,out var motion))
        {
            if(!hovered)return 0;
            if(_hoverMotion.Count>=512)_hoverMotion.Clear();
            motion=new HoverMotion{Frame=-1,Time=Time.unscaledTime-Mathf.Min(.05f,Time.unscaledDeltaTime)};
        }
        if(motion.Frame!=Time.frameCount)
        {
            UiThemePolicy.StepHover(ref motion.Animation,
                UiThemePolicy.FaceOffset(hovered,held),Time.unscaledTime-motion.Time);
            motion.Time=Time.unscaledTime;motion.Frame=Time.frameCount;
            if(!hovered&&motion.Animation.Position==0&&motion.Animation.Elapsed>=UiThemePolicy.HoverSeconds)
            {_hoverMotion.Remove(key);return 0;}
            _hoverMotion[key]=motion;
        }
        return motion.Animation.Position;
    }
    private static Rect HoverChildRect(Rect owner,Rect child,float offset)
    {
        float scale=UiThemePolicy.HoverScale(offset);
        Vector2 center=new(owner.center.x,owner.y+Mathf.Max(4,owner.height-UiThemePolicy.RestDepth)*.5f);
        Rect moved=new(center.x+(child.x-center.x)*scale,center.y+(child.y-center.y)*scale+offset,child.width*scale,child.height*scale);
        // A hover never escapes its fixed owner: top scroll rows and bottom
        // buttons cannot fly through a parent clip or reveal sliced edges.
        moved.width=Mathf.Min(moved.width,owner.width);moved.height=Mathf.Min(moved.height,owner.height);
        moved.x=Mathf.Clamp(moved.x,owner.x,owner.xMax-moved.width);
        moved.y=Mathf.Clamp(moved.y,owner.y,owner.yMax-moved.height);
        return moved;
    }

    private void HoverText(Rect scaledRect,string text,GUIStyle style,float offset)=>
        HoverText(scaledRect,ButtonContent(text),style,offset);
    private void HoverText(Rect scaledRect,GUIContent content,GUIStyle style,float offset)
    {
        float scale=UiThemePolicy.HoverScale(offset);
        if(scale==1f){ShadowText(scaledRect,content,style);return;}
        // A bigger label rect alone does not scale native glyphs. Scale only
        // this draw, never the pointer/hit test or the surrounding GUI group.
        Matrix4x4 previous=GUI.matrix;Vector2 center=scaledRect.center;
        try
        {
            // Unity accounts for the GUIClip/BeginGroup offset of this local pivot.
            GUIUtility.ScaleAroundPivot(new Vector2(scale,scale),center);
            ShadowText(new Rect(center.x-scaledRect.width/(2*scale),center.y-scaledRect.height/(2*scale),
                scaledRect.width/scale,scaledRect.height/scale),content,style);
        }
        finally{GUI.matrix=previous;}
    }

    private static bool _imguiFocusUnavailable;
    private static void DropImguiKeyboardFocus()
    {
        if(_imguiFocusUnavailable)return;
        try{if(GUIUtility.keyboardControl!=0)GUIUtility.keyboardControl=0;}
        catch{_imguiFocusUnavailable=true;}
    }

    private static void ConsumePointerEvent()
    {
        var e = Event.current;
        if (e != null && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag ||
                          e.type == EventType.MouseUp)) e.Use();
    }

    internal readonly long[] UiAllocationSections=new long[32];
    private void AllocationMark(int slot,ref long stamp)
    {long now=GC.GetAllocatedBytesForCurrentThread();UiAllocationSections[slot]+=now-stamp;stamp=now;}
    internal long DrawAllocatedBytesForTest,DrawMeasuredCallsForTest,PlayerPageAllocatedBytesForTest,PlayerPageMeasuredCallsForTest;
    internal void Draw()
    {
        long bytes=GC.GetAllocatedBytesForCurrentThread();
        try{DrawMeasured();}
        finally{DrawAllocatedBytesForTest+=GC.GetAllocatedBytesForCurrentThread()-bytes;DrawMeasuredCallsForTest++;}
    }
    private void DrawMeasured()
    {
        long allocationStamp=GC.GetAllocatedBytesForCurrentThread();
        _guiEventRoot=Event.current;
        _=EmptyContent;
        _guiSkinRoot??=GUI.skin;_guiEmptyStyleRoot??=GUIStyle.none;
        _guiTempTextRoot??=GUIContent.s_Text;_guiTempImageRoot??=GUIContent.s_Image;_guiTempTextImageRoot??=GUIContent.s_TextImage;
        Styles();
        AllocationMark(0,ref allocationStamp);
        if(_headerSlime==null&&_app.Ready)
            _headerSlime=_app.Actors.Catalog.FirstOrDefault(e=>e.Category=="Слаймы"&&!e.IsPrototype)?.Type?.Icon;
        Matrix4x4 original = GUI.matrix;
        bool wasEnabled = GUI.enabled;
        bool wasBlocked = _bodyInteractionBlocked;
        Color originalColor = GUI.color, originalContent = GUI.contentColor, originalBackground = GUI.backgroundColor;
        try
        {
            GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            _bodyInteractionBlocked=false;
            float scale = Mathf.Max(.35f, Mathf.Min(1.2f, Mathf.Min((Screen.width - 48f) / 1160f, (Screen.height - 48f) / 820f)));
            _scale = scale;
            _groupOrigin = Vector2.zero;
            _groupOrigins.Clear();_groupRects.Clear();
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale,scale,1));
            float sw = Screen.width / scale, sh = Screen.height / scale;
            float entrance=OpeningProgress,opened=Time.unscaledTime-_menuOpenedAt;
            if(entrance>=1f&&!(opened<UiMotionPolicy.MenuPopSeconds))_menuEntering=false;
            const float width = 1160, height = 820;
            Rect window = new((sw-width)/2, (sh-height)/2+24f*(1f-entrance), width,height);
            Fill(new Rect(0,0,sw,sh),new Color(.19f,.15f,.10f,.22f*entrance));
            // 0.8.5: the window pops in with a slight overshoot (the backdrop stays put) and its pages
            // develop from the parchment; GUI.matrix is restored by the finally below.
            float pop=_menuEntering?UiMotionPolicy.MenuPop(opened):1f;
            if(pop!=1f)GUIUtility.ScaleAroundPivot(new Vector2(pop,pop),window.center);
            Fill(new Rect(window.x-2,window.y+8,width+4,height),new Color(.35f,.25f,.13f,.10f));
            L.Box(window,GUIContent.none,_frame);
            AllocationMark(1,ref allocationStamp);
            BeginGroup(window);
            try
            {
                L.Box(new Rect(25,14,69,69),GUIContent.none,_glowBox);
                GUI.DrawTexture(new Rect(30,14,62,62),_badgeCircle);
                AllocationMark(7,ref allocationStamp);
                if(!DrawLogo(new Rect(28,12,66,66)))
                {
                    if(_headerSlime!=null&&!_headerSlime.WasCollected)DrawSprite(new Rect(34,16,54,54),_headerSlime);
                    else Icon(new Rect(32,16,58,58),8);
                }
                AllocationMark(8,ref allocationStamp);
                GradientTitle(new Rect(102,25,650,44));
                AllocationMark(9,ref allocationStamp);
                if(Button(new Rect(width-182,25,44,44),"?",DetailedHelp?_selectedButton:_button)){DetailedHelp=!DetailedHelp;_app.Notify(DetailedHelp?"Подсказки включены · пояснения под кнопками":"Подсказки скрыты");}
                if(Button(new Rect(width-130,25,100,44),"Закрыть",_button)) _app.CloseMenu();
                Fill(new Rect(30,88,width-60,1),new Color(.15f,.85f,.78f,.35f));
                AllocationMark(2,ref allocationStamp);
                bool modal = _app.Pending != null;
                // The audit locks interaction, not the palette. Using
                // GUI.enabled for this also washed out every static label in
                // the labels and produced unreadable validation captures.
                _bodyInteractionBlocked=(_app.Audit?.Active??false)&&!(_app.UiProbe?.Active??false);
                GUI.enabled = !modal && !_app.Map.Busy;
                var tabWidths=TabWidths(width-60);float tabX=30,markX=30,markW=tabWidths[0];
                for(int i=0;i<_tabs.Length;tabX+=tabWidths[i]+8,i++)
                {
                    if(i==_tab){markX=tabX;markW=tabWidths[i];}
                    if(TabButton(new Rect(tabX,102,tabWidths[i],46),_tabs[i],i,i==_tab))
                    {
                        _tab=i;ClearInteraction();
                        _tabClickAt=System.Diagnostics.Stopwatch.GetTimestamp();_tabClickIndex=i;
                        if(_tab==3&&_app.Ready)_hour=_app.World.Hour();
                    }
                }
                TabMarker(markX,markW);
                AllocationMark(3,ref allocationStamp);
                Rect body = new(30,166,width-60,height-226);
                bool measurePaint=Event.current.type==EventType.Repaint&&_app.Ready&&!_app.Map.Busy&&!modal&&
                    PaintTelemetry.Wants(_tab,_tabClickAt!=0&&_tabClickIndex==_tab);
                long bodyStart=measurePaint?System.Diagnostics.Stopwatch.GetTimestamp():0;
                if(_app.Map.Busy)
                {
                    GUI.enabled=true;
                    _bodyInteractionBlocked=false; // cancellation remains available
                    L.Box(new Rect(30,185,width-60,260),GUIContent.none,_card);
                    Icon(new Rect(58,211,64,64),2);
                    L.Label(new Rect(140,208,width-220,44),"Безопасный переход между островами",_heading);
                    L.Label(new Rect(140,264,width-220,80),_app.Map.TeleportStatus,_wrap);
                    Hint(new Rect(58,346,width-116,40),"Гравитация удерживается. Посадка — только после прогрузки и повторных проверок коллизий.",_muted);
                    if(Button(new Rect(58,392,340,44),"Отменить / повторить возврат",_button))_app.Queue("cancel safe transit",_app.Map.AbortTeleport);
                }
                else if(!_app.Ready)
                {
                    L.Box(new Rect(30,175,width-60,220),GUIContent.none,_card);
                    L.Label(new Rect(58,200,width-116,45),"Сначала загрузите игровой мир",_heading);
                    L.Label(new Rect(58,265,width-116,90),"Функции станут доступны после загрузки игрока и нативных компонентов игры.",_wrap);
                }
                else switch(_tab)
                { case 0:PlayerPage(body);break; case 1:CreationPage(body);break; case 2:CurrencyPage(body);break; case 3:WorldPage(body);break; case 4:UnlockPage(body);break; case 5:CinemaPage(body);break; case 6:AtlasPage(body);break; case 7:ConveniencePage(body);break; case 8:SettingsPage(body);break; }
                PageVeil(body);
                AllocationMark(4,ref allocationStamp);
                if(measurePaint)
                {
                    long completed=System.Diagnostics.Stopwatch.GetTimestamp();
                    double? click=_tabClickAt!=0&&_tabClickIndex==_tab?
                        1000d*(completed-_tabClickAt)/System.Diagnostics.Stopwatch.Frequency:null;
                    PaintTelemetry.Record(_tab,_tabs[_tab],1000d*(completed-bodyStart)/System.Diagnostics.Stopwatch.Frequency,
                        click,Time.frameCount);
                    if(click.HasValue){_tabClickAt=0;_tabClickIndex=-1;}
                }
                _bodyInteractionBlocked=false;
                GUI.enabled = true;
                Fill(new Rect(30,height-52,width-60,1),new Color(.15f,.85f,.78f,.35f));
                string status=FooterStatus(_app.Audit?.Active == true ? _app.Audit.Status : _app.Status,out float statusShown);
                bool coop=_app.Coop?.InSession==true; // Ranching Together: role and server cheat rule instead of the key hint.
                Rect statusRect=new(30,height-46+4f*(1f-statusShown),width-(coop?370:250),38);
                if(statusShown>=1f)L.Label(statusRect,status,_muted);
                else{Color c=GUI.color;GUI.color=new Color(c.r,c.g,c.b,c.a*statusShown);L.Label(statusRect,status,_muted);GUI.color=c;}
                L.Label(coop?new Rect(width-330,height-46,300,38):new Rect(width-210,height-46,180,38),coop?_app.Coop.Footer:_app.Hotkey?.Footer??"Insert  ·  Esc",_muted);
                float veil=_menuEntering?UiMotionPolicy.MenuVeil(opened):0f;
                if(veil>.001f)Fill(new Rect(24,94,width-48,height-112),new Color(_parchment.r,_parchment.g,_parchment.b,veil));
                if(modal)DrawConfirmation(width,height);
                AllocationMark(5,ref allocationStamp);
            }
            finally { EndGroup(); }
        }
        finally { _bodyInteractionBlocked=wasBlocked;GUI.matrix=original; GUI.enabled=wasEnabled; GUI.color=originalColor; GUI.contentColor=originalContent; GUI.backgroundColor=originalBackground; }
        AllocationMark(6,ref allocationStamp);
    }

    // Native TextField enters stripped GUIStateObjects/TextEditor. Keep the
    // Unicode buffer managed, but receive commits from Keyboard.onTextInput.
    // IMGUI Event.character is deliberately not a second character source.
    private string TextBox(string id, Rect rect, string value, int maxLength = 80, string placeholder = "Поиск по названию или ID…")
    {
        value ??= "";
        if(_pendingTextWrites.TryGetValue(id,out var committed))
        {value=committed;_pendingTextWrites.Remove(id);}
        string current=_editingField==id?_textBuffer.Value:value;
        float previousScroll=_editingField==id?_textHorizontalOffset:0;
        if(Pressed(rect))
        {
            FocusTextInput(id,current,maxLength);
            PlaceTextCaret(Pointer().x-rect.x-9+previousScroll);
        }
        else if(_editingField==id&&(!InputEnabled||
            (Mouse.current!=null&&Mouse.current.leftButton.wasPressedThisFrame&&!rect.Contains(Pointer()))))
            BlurTextInput();
        bool focused=InputEnabled && _editingField == id;
        if(focused)
        {
            value=_textBuffer.Value;
            if(value!=_lastReturnedText)LastTextBindFrameForTest=Time.frameCount;
            _lastReturnedText=value;
        }
        L.Box(rect,GUIContent.none,_field);
        Rect inside=new(rect.x+9,rect.y+3,rect.width-18,rect.height-6);
        string display=focused?_textBuffer.PreviewValue:value;
        float textWidth=_label.CalcSize(RawContent(display)).x;
        float caret=focused?_label.CalcSize(RawContent(_textBuffer.CaretPrefix)).x:0;
        if(focused)
        {
            if(caret-_textHorizontalOffset>inside.width-4)_textHorizontalOffset=caret-inside.width+4;
            if(caret<_textHorizontalOffset)_textHorizontalOffset=caret;
            _textHorizontalOffset=Mathf.Clamp(_textHorizontalOffset,0,Mathf.Max(0,textWidth-inside.width+4));
            PositionImeCursor(inside,caret);
        }
        float scroll=focused?_textHorizontalOffset:0;
        BeginGroup(inside);
        try
        {
            if(focused&&_textBuffer.SelectedAll&&_textBuffer.Composition.Length==0)
                Fill(new Rect(-scroll,2,Mathf.Max(2,textWidth),inside.height-4),new Color(.10f,.75f,.66f,.20f));
            if(value.Length==0&&!focused)L.Label(new Rect(0,0,inside.width,inside.height),placeholder,_muted);
            GUI.Label(new Rect(-scroll,0,Mathf.Max(inside.width,textWidth+4),inside.height),display,_label); // typed text: never translated
            if(focused&&_textBuffer.Composition.Length>0)
            {
                float previewWidth=_label.CalcSize(RawContent(_textBuffer.Composition)).x;
                Fill(new Rect(caret-previewWidth-scroll,inside.height-3,previewWidth,2),_accent);
            }
            if(focused&&!_textBuffer.SelectedAll&&((int)(Time.unscaledTime*2f)&1)==0)
                Fill(new Rect(caret-scroll,4,2,Math.Max(4,inside.height-8)),_accent);
        }
        finally { EndGroup(); }
        if(focused) Fill(new Rect(rect.x,rect.yMax-2,rect.width,2),_accent);
        return value;
    }

    // GUI.HorizontalSlider calls stripped GUI.Slider in this IL2CPP player; draw/drag our own.
    private float Slider(int id, Rect rect, float value, float minimum, float maximum)
    {
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        try
        {
            if(id<0||id>=_sliderMotion.Length||maximum<=minimum)return value;
            var mouse = Mouse.current;
            bool focused=Application.isFocused||SliderTestInput||(_app.UiProbe?.Active??false);
            // Fixed, clipped interaction surface. Jelly deforms only within it;
            // pointer mapping never follows an animated/transformed knob.
            Rect hit = new(rect.x,rect.center.y-22f,rect.width,44f);
            bool within=PointerWithinGroups();
            if(focused&&Pressed(hit))
            {
                _dragSlider = id;
                _dragScroll = -1;
            }
            if (_dragSlider == id)
            {
                if (!InputEnabled || mouse == null ||
                    !UiThemePolicy.SliderCanContinue(InputEnabled,focused,mouse.leftButton.isPressed,within))
                    _dragSlider = -1;
                else value=Mathf.Lerp(minimum,maximum,
                    UiThemePolicy.SliderPointerValue(Pointer().x,rect.x,rect.width));
                ConsumePointerEvent();
            }
            value = Mathf.Clamp(value, minimum, maximum);
            float target = Mathf.InverseLerp(minimum, maximum, value);
            ref var motion=ref _sliderMotion[id];
            bool hover=focused&&CanHover(hit),held=_dragSlider==id;
            if(!motion.Animation.Initialized||motion.Frame!=Time.frameCount)
            {
                float now=Time.unscaledTime;
                UiThemePolicy.StepSlider(ref motion.Animation,target,hover,held,
                    motion.Animation.Initialized?now-motion.Time:0f);
                motion.Time=now;motion.Frame=Time.frameCount;
            }
            float t=motion.Animation.Value;
            Rect track = new(rect.x, rect.y + rect.height * .5f - 11f, rect.width,22f);
            DrawRoundedSurface(track,_sliderTrackPaint,track.height*.5f);
            if(t>0)
            {
                float knobCenter=UiThemePolicy.SliderKnobCenter(rect.x,rect.width,t,32f);
                Rect filled=new(track.x+2,track.y+2,Mathf.Max(0,knobCenter-track.x-2),track.height-4);
                DrawRoundedSurface(filled,_sliderFillPaint,filled.height*.5f);
            }
            float jelly=motion.Animation.Jelly.Position;
            const float diameter=32;
            float drawDiameter=diameter*(1f+.04f*Mathf.Clamp01(-jelly/4f));
            float height=drawDiameter*(1f-.09f*Mathf.Clamp01(jelly/3f));
            float center=UiThemePolicy.SliderKnobCenter(rect.x,rect.width,t,drawDiameter);
            Rect knob=new(center-drawDiameter*.5f,rect.center.y-height*.5f,drawDiameter,height);
            if(hover||held)DrawRoundedSurface(new Rect(knob.x-3,knob.y-3,knob.width+6,knob.height+6),_sliderGlowPaint,19f);
            GUI.DrawTexture(new Rect(knob.x,knob.y+2,knob.width,knob.height),_badgeCircle);
            GUI.DrawTexture(knob,_slimeKnob);
            return value;
        }
        finally
        {
            SliderAllocatedBytesForTest+=GC.GetAllocatedBytesForCurrentThread()-allocated;
            SliderMeasuredCallsForTest++;
        }
    }

    internal int QuantityForTest=>_quantity;
    internal float ScrollForTest=>_catalogScroll.y;
    internal void SetForTest(int tab,int category,int quantity){ShowTabForTest(tab);_category=Mathf.Clamp(category,0,_categories.Length-1);_family="Все";_quantity=quantity;_catalogScroll=Vector2.zero;_selected=null;}
    internal void SetFamilyForTest(int category,string family){SetForTest(1,category,1);_family=family;}
    internal int FilteredCountForTest=>Filter("",_categories[_category],false,_family).Count;

    internal void ShowPage(int tab) { _tab = Mathf.Clamp(tab, 0, TabCount-1); if(_tab==3&&_app.Ready)_hour=_app.World.Hour(); if(_tab==6)_explorerSection=0; if(_tab==7)_farmPage=false; if(_tab==8){_optimizationPage=false;_infoPage=false;} ClearInteraction(); }
    internal void ShowTabForTest(int tab)=>ShowPage(tab);
    // Read-only content navigation: the same filter/selection state as Atlas UI.
    internal void ShowCapsules()
    {
        ShowPage(6);_app.Map.Search="";_app.Map.HiddenChips.Clear();_app.Map.Visible.Clear();_app.Map.Visible.Add("Капсулы");_atlasGroup=0;
        _atlasScroll=Vector2.zero;_capsuleScroll=Vector2.zero;
        var points=_app.Map.Filter();
        _app.Map.Selected=points.FirstOrDefault(p=>_app.Map.Capsules.Get(p.Id).NativeVerified)??points.FirstOrDefault();
    }
    internal void ShowSources()
    {
        ShowPage(6);_app.Map.Search="";_app.Map.HiddenChips.Clear();_app.Map.Visible.Clear();
        _app.Map.Visible.Add("Горшочки");_app.Map.Visible.Add("Спавнеры");_atlasGroup=4;
        _atlasScroll=Vector2.zero;_app.Map.Selected=_app.Map.Filter().FirstOrDefault();
    }
    // UI-only navigation for explicit file IPC; never creates actors or changes progression.
    internal void ShowRadiantCatalog()
    {
        ShowPage(1);_category=1;_family="Лучистые";_search="";_catalogScroll=Vector2.zero;_selected=null;
    }

    private bool Toggle(Rect rect,string title,bool enabled,string detail)
    {
        // 0.8.0: the explanation is drawn only with «?» (DetailedHelp). Compact rows (< 70 px)
        // keep the switch centred and put the explanation on one 14 px line under the title.
        L.Box(rect,GUIContent.none,_card);
        Fill(new Rect(rect.x+1,rect.y+12,3,rect.height-24),enabled?_accent:new Color(.23f,.42f,.54f));
        bool compact=rect.height<70f,help=DetailedHelp&&!string.IsNullOrEmpty(detail);
        float titleY=help?(compact?rect.y+2:rect.y+10):rect.center.y-17;
        L.Label(new Rect(rect.x+18,titleY,rect.width-154,34),title,_label);
        if(help)L.Label(compact?new Rect(rect.x+18,rect.y+32,rect.width-154,rect.height-34):new Rect(rect.x+18,rect.y+48,rect.width-36,rect.height-54),
            detail,compact?_small:_muted);
        return SlimeSwitch(new Rect(rect.xMax-122,help&&!compact?rect.y+10:rect.center.y-22,104,44),enabled)?!enabled:enabled;
    }
    // 0.8.0: explanatory text that is useful once, not on every visit.
    private void Hint(Rect rect,string text,GUIStyle style=null){if(DetailedHelp&&!string.IsNullOrEmpty(text))L.Label(rect,text,style??_muted);}

    private bool SlimeSwitch(Rect rect,bool enabled)
    {
        bool hover=CanHover(rect),held=hover&&Mouse.current!=null&&Mouse.current.leftButton.isPressed;
        float offset=HoverOffset(rect,hover,held);
        // 0.8.5: the knob slides (UiMotionPolicy.SwitchSeconds); colour and caption flip as it passes the middle.
        float t=UiMotionPolicy.Smooth(SwitchProgress(rect,enabled));bool on=t>=.5f;
        if(on||hover)L.Box(new Rect(rect.x-3,rect.y-3,rect.width+6,rect.height+6),GUIContent.none,_glowBox);
        L.Box(rect,GUIContent.none,_shadowBox);
        L.Box(HoverChildRect(rect,new Rect(rect.x,rect.y,rect.width,rect.height-4),offset),GUIContent.none,on?_selectedButton:_button);
        float knobX=Mathf.Lerp(rect.x+6,rect.xMax-38,t);
        GUI.DrawTexture(HoverChildRect(rect,new Rect(knobX,rect.y+4,32,32),offset),_slimeKnob);
        HoverText(HoverChildRect(rect,new Rect(on?rect.x+6:rect.x+40,rect.y-2,58,44),offset),on?"ВКЛ":"ВЫКЛ",_iconTab,offset);
        return Pressed(rect);
    }
    // 0.8.5: 0..1 knob position of a switch, eased toward its state once per frame. A switch seen for
    // the first time (or after a tab change) starts at rest.
    private float SwitchProgress(Rect rect,bool on)
    {
        float target=on?1f:0f;
        var key=(_groupOrigin.x+rect.x,_groupOrigin.y+rect.y,rect.width,rect.height);
        if(!_switchMotion.TryGetValue(key,out var motion))
        {
            if(_switchMotion.Count>=256)_switchMotion.Clear();
            _switchMotion[key]=new SwitchMotion{Value=target,Frame=Time.frameCount};
            return target;
        }
        if(motion.Frame!=Time.frameCount)
        {
            motion.Value=UiMotionPolicy.Toward(motion.Value,target,Time.unscaledDeltaTime,UiMotionPolicy.SwitchSeconds);
            motion.Frame=Time.frameCount;_switchMotion[key]=motion;
        }
        return motion.Value;
    }
    // 0.8.5: the selection pill of a segmented control glides to the chosen segment.
    private float SegmentGlide(Rect rect,int selected)
    {
        var key=(_groupOrigin.x+rect.x,_groupOrigin.y+rect.y,rect.width,rect.height);
        if(!_glideMotion.TryGetValue(key,out var motion))
        {
            if(_glideMotion.Count>=256)_glideMotion.Clear();
            motion=new GlideMotion{Frame=Time.frameCount};
            UiMotionPolicy.Step(ref motion.Glide,selected,0f);
            _glideMotion[key]=motion;
            return selected;
        }
        float dt=motion.Frame!=Time.frameCount?Time.unscaledDeltaTime:0f;
        float value=UiMotionPolicy.Step(ref motion.Glide,selected,dt);
        motion.Frame=Time.frameCount;_glideMotion[key]=motion;
        return value;
    }
    // Hover / press offset of a jelly face drawn with Button(rect, GUIContent.none, ...): children follow it.
    private float JellyOffset(Rect face)
    {
        bool hover=CanHover(face);
        return HoverOffset(face,hover,hover&&Mouse.current!=null&&Mouse.current.leftButton.isPressed);
    }
    // A child drawn outside a jelly face: grows around its centre with the hover scale.
    private static Rect HoverGrow(Rect rect,float offset)
    {
        float s=UiThemePolicy.HoverScale(offset),w=rect.width*s,h=rect.height*s;
        return new Rect(rect.center.x-w*.5f,rect.center.y-h*.5f,w,h);
    }
    // 0.8.5: a child of a wide list row keeps its own place (the face cannot grow past the fixed row),
    // follows the lift and, for icons, grows around its own centre.
    private static Rect RowChild(Rect child,float offset,bool grow=false)
    {
        Rect r=grow?HoverGrow(child,offset):child;
        if(float.IsFinite(offset))r.y+=offset;
        return r;
    }
    // 0.8.5: a mint pill under the open tab glides to the next one.
    private void TabMarker(float x,float w)
    {
        float dt=_tabMarkFrame!=Time.frameCount?Time.unscaledDeltaTime:0f;_tabMarkFrame=Time.frameCount;
        float mx=UiMotionPolicy.Step(ref _tabMarkX,x,dt),mw=UiMotionPolicy.Step(ref _tabMarkW,w,dt);
        DrawRoundedSurface(new Rect(mx+18,153,Mathf.Max(8,mw-36),5),_fillStyle.normal.background,2.5f);
    }
    // 0.8.5: a freshly opened page (tab, «Спавн» and «Открытия» sections) fades in from the parchment.
    private void PageVeil(Rect body)
    {
        int page=_tab*8+(_tab==1?_creationSection:_tab==4?_unlockSection:0);
        if(page!=_veilPage){bool first=_veilPage<0;_veilPage=page;_veilSince=first?-10f:Time.unscaledTime;}
        float veil=UiMotionPolicy.PageVeil(Time.unscaledTime-_veilSince);
        if(veil<=.001f)return;
        Fill(new Rect(body.x-6,body.y-6,body.width+12,body.height+12),new Color(_parchment.r,_parchment.g,_parchment.b,veil));
    }
    // 0.8.5: the header logo, a 32 x 32 pixel slime upscaled 4x once (bilinear keeps edges even at any UI scale).
    private void BuildLogo()
    {
        if(_logoTexture!=null&&!_logoTexture.WasCollected)return;
        _logoTexture=null;
        var data=CreatorLogoPolicy.Pixels();
        if(data==null)return;
        int size=CreatorLogoPolicy.TextureSize;
        var pixels=new Color32[size*size];
        for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(data[i*4],data[i*4+1],data[i*4+2],data[i*4+3]);
        var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
        texture.name="CreatorToolkit_UI_Logo";texture.hideFlags=HideFlags.HideAndDontSave;
        texture.filterMode=FilterMode.Bilinear;texture.wrapMode=TextureWrapMode.Clamp;
        texture.SetPixels32(pixels);texture.Apply(false,true);
        _logoTexture=texture;
    }
    // The slime breathes (squash and stretch around the base of its body).
    private bool DrawLogo(Rect rect)
    {
        if(_logoTexture==null||_logoTexture.WasCollected)return false;
        UiMotionPolicy.LogoBreath(Time.unscaledTime,out float sx,out float sy);
        float w=rect.width*sx,h=rect.height*sy,baseY=rect.y+rect.height*CreatorLogoPolicy.Base;
        GUI.DrawTexture(new Rect(rect.center.x-w*.5f,baseY-h*CreatorLogoPolicy.Base,w,h),_logoTexture);
        return true;
    }

    private void PlayerPage(Rect area)
    {
        long bytes=GC.GetAllocatedBytesForCurrentThread();
        try{PlayerPageMeasured(area);}
        finally{PlayerPageAllocatedBytesForTest+=GC.GetAllocatedBytesForCurrentThread()-bytes;PlayerPageMeasuredCallsForTest++;}
    }
    private void PlayerPageMeasured(Rect area)
    {
        long allocationStamp=GC.GetAllocatedBytesForCurrentThread();
        BeginGroup(area);
        try
        {
            float col=(area.width-24)/2;
            L.Label(new Rect(0,0,area.width-220,36),"Игрок · защита, движение, строительство",_heading);
            AllocationMark(10,ref allocationStamp);
            var p=_app.Player;
            // 0.8.0: six compact rows; the builder switches are the same ones as in «Спавн → Гаджеты».
            const float rowH=56,stride=61,top=52;
            p.GodMode=Toggle(new Rect(0,top,col,rowH),"Бессмертие",p.GodMode,"Поддерживать максимум здоровья.");
            p.InfiniteEnergy=Toggle(new Rect(0,top+stride,col,rowH),"Бесконечная энергия",p.InfiniteEnergy,"Спринт и способности без расхода.");
            AllocationMark(11,ref allocationStamp);
            bool fly=Toggle(new Rect(0,top+2*stride,col,rowH),"Полёт / NoClip",p.Flight,"WASD · Space вверх · Shift вниз.");
            if(fly!=p.Flight)QueuePlayerFlight(fly);
            AllocationMark(12,ref allocationStamp);
            p.InfiniteItems=Toggle(new Rect(0,top+3*stride,col,rowH),"Бесконечный инвентарь",p.InfiniteItems,"Не тратить предметы из непустых слотов.");
            var gadgets=_app.Gadgets;bool infinite=gadgets!=null&&gadgets.InfiniteBuilder;
            bool builder=Toggle(new Rect(0,top+4*stride,col,rowH),"Бесконечный режим строителя",infinite,"Гаджеты и декор ×999 · установка без расхода.");
            if(gadgets!=null&&builder!=infinite)QueueInfiniteBuilder(builder);
            var placement=_app.FreePlacement;bool free=placement!=null&&placement.Enabled;
            bool freeNext=Toggle(new Rect(0,top+5*stride,col,rowH),"Свободная установка",free,"Без проверок пересечений и уклона · дальше.");
            if(placement!=null&&freeNext!=free)QueueFreePlacement(freeNext);
            float x=col+24;
            L.Box(new Rect(x,52,col,190),GUIContent.none,_card);
            L.Label(new Rect(x+20,70,col-40,42),SpeedReadouts[Mathf.Clamp(Mathf.RoundToInt(p.SpeedMultiplier*100)-100,0,SpeedReadouts.Length-1)],_heading);
            p.SpeedMultiplier=SafetyPolicy.Speed(Slider(1,new Rect(x+20,129,col-40,32),p.SpeedMultiplier,1,10));
            float bw=(col-56)/5;
            for(int i=0;i<PlayerSpeeds.Length;i++)if(Button(new Rect(x+20+i*(bw+4),183,bw,44),PlayerSpeedNames[i],Math.Abs(p.SpeedMultiplier-PlayerSpeeds[i])<.01f?_selectedButton:_button))p.SpeedMultiplier=PlayerSpeeds[i];
            AllocationMark(13,ref allocationStamp);
            L.Box(new Rect(x,254,col,162),GUIContent.none,_card);
            L.Label(new Rect(x+20,266,col-154,32),"Высокий прыжок",_tableHeading);
            if(SlimeSwitch(new Rect(x+col-124,263,104,44),p.HighJump))p.HighJump=!p.HighJump;
            L.Label(new Rect(x+20,308,col-40,30),JumpReadouts[Mathf.Clamp(Mathf.RoundToInt(p.JumpHeightMultiplier*10)-10,0,JumpReadouts.Length-1)],_muted);
            p.JumpHeightMultiplier=PlayerMovementPolicy.JumpHeight(Slider(2,new Rect(x+20,350,col-40,28),p.JumpHeightMultiplier,1,5));
            AllocationMark(14,ref allocationStamp);
            L.Box(new Rect(0,432,area.width,64),GUIContent.none,_card);
            L.Label(new Rect(20,439,col-30,30),_healthReadout,_label);
            L.Label(new Rect(x+20,439,col-30,30),_energyReadout,_label);
            Fill(new Rect(20,480,col-40,4),new Color(.30f,.30f,.30f));
            Fill(new Rect(20,480,(col-40)*Mathf.Clamp01((float)_health/Math.Max(1,_maxHealth)),4),new Color(.91f,.45f,.40f));
            Fill(new Rect(x+20,480,col-40,4),new Color(.30f,.30f,.30f));
            Fill(new Rect(x+20,480,(col-40)*Mathf.Clamp01((float)_energy/Math.Max(1,_maxEnergy)),4),new Color(.37f,.62f,.91f));
            AllocationMark(16,ref allocationStamp);
            float quick=(area.width-30)/4;
            if(Button(new Rect(0,510,quick,44),"Восстановить силы",_accentButton))_app.Queue("refill vitals",p.RefillVitals);
            if(Button(new Rect(quick+10,510,quick,44),"Запомнить место",_button))_app.Queue("remember checkpoint",p.RememberCheckpoint);
            bool canReturn=GUI.enabled;GUI.enabled=canReturn&&p.HasCheckpoint;
            if(Button(new Rect(2*(quick+10),510,quick,44),"К точке возврата",_button))_app.Queue("recall checkpoint",p.RecallCheckpoint);
            GUI.enabled=canReturn;
            if(Button(new Rect(3*(quick+10),510,quick,44),"Сбросить эффекты",_button))_app.Queue("reset player",ResetPlayerEffects);
            string starlight=StarlightBridge.PlayerNote;
            if(starlight.Length==0)L.Label(new Rect(0,566,area.width,30),p.CheckpointStatus,_small);
            else{L.Label(new Rect(0,566,col,30),p.CheckpointStatus,_small);L.Label(new Rect(x,566,col,30),starlight,_small);}
            AllocationMark(19,ref allocationStamp);
        }
        finally{EndGroup();}
        AllocationMark(17,ref allocationStamp);
    }

    // ScrollView's native scrollbars also call the stripped Slider method.
    private Vector2 BeginScroll(int id, Rect viewport, Vector2 scroll, float contentHeight, float rowHeight = 48f)
    {
        contentHeight=M26ListPolicy.Extent(contentHeight); // All custom lists retain a 16 logical-pixel bottom gutter.
        float limit = Mathf.Max(0f, contentHeight - viewport.height);
        if(!_scrollMotion.TryGetValue(id,out var motion))
        {
            if(_scrollMotion.Count>=64)_scrollMotion.Clear();
            motion=new ScrollMotion{Target=scroll.y,Returned=scroll.y,Time=Time.unscaledTime};
            _scrollMotion[id]=motion;
        }
        // An explicit filter/page reset wins over an old animation target.
        if(Mathf.Abs(scroll.y-motion.Returned)>.1f)motion.Target=scroll.y;
        motion.Target=ScrollMotionPolicy.Clamp(motion.Target,limit);
        var e = Event.current;
        if (InputEnabled && PointerWithinGroups() && e != null && e.type == EventType.ScrollWheel && viewport.Contains(e.mousePosition))
        {
            motion.Target = ScrollMotionPolicy.Wheel(motion.Target,e.delta.y,rowHeight,limit);
            e.Use();
        }
        if(motion.Frame!=Time.frameCount)
        {
            if(_dragScroll!=id)scroll.y=ScrollMotionPolicy.Step(scroll.y,motion.Target,Time.unscaledTime-motion.Time,limit);
            motion.Time=Time.unscaledTime;motion.Frame=Time.frameCount;
        }
        scroll.y = ScrollMotionPolicy.Clamp(scroll.y,limit);
        BeginGroup(viewport);
        if (limit > 0f)
        {
            const float gutter = 44;
            float x = viewport.width - gutter;
            Rect rail = new(x, 0, gutter, viewport.height);
            float thumb = Mathf.Clamp(rail.height * viewport.height / contentHeight, 44, rail.height);
            float travel = Mathf.Max(1, rail.height - thumb);
            float y = rail.y + travel * scroll.y / limit;
            Rect handle = new(x + 8, y, gutter - 16, thumb);
            if (Pressed(rail))
            {
                _dragScroll = id;
                _dragSlider = -1;
                _dragScrollOffset = handle.Contains(Pointer()) ? Pointer().y - y : thumb * .5f;
            }
            if (_dragScroll == id)
            {
                if (!InputEnabled || Mouse.current == null || !Mouse.current.leftButton.isPressed)
                { _dragScroll = -1; motion.Target=scroll.y; }
                else { scroll.y = Mathf.Clamp01((Pointer().y - rail.y - _dragScrollOffset) / travel) * limit; motion.Target=scroll.y; }
                ConsumePointerEvent();
                y = rail.y + travel * scroll.y / limit;
            }
            // Explicit nine-slice drawing keeps the rounded caps valid even
            // on a 16 px rail. GUIStyle's fixed 20 px borders overlapped on
            // narrow tracks and produced the asymmetric cutout/egg artifact.
            DrawRoundedSurface(new Rect(x+14,rail.y,16,rail.height),_trackStyle.normal.background,8f);
            DrawRoundedSurface(new Rect(x+10,y+2,gutter-20,thumb),_shadowBox.normal.background,12f);
            DrawRoundedSurface(new Rect(x+10,y,gutter-20,thumb),_selectedButton.normal.background,12f);
        }
        motion.Returned=scroll.y;
        return scroll;
    }

    private List<ActorTools.Entry> Filter(string search,string category,bool ammo,string family="Все")
    {
        if(_filterGeneration!=_app.Actors.Generation){_filterCache.Clear();_filterGeneration=_app.Actors.Generation;}
        int slot=ammo&&_app.Ready?_app.Context.PlayerState.Ammo._selectedAmmoIdx:-1;
        string needle=(search??"").Trim().ToLowerInvariant(),key=category+"|"+ammo+"|"+slot+"|"+family+"|"+needle;
        if(!_filterCache.TryGetValue(key,out var result))
        {
            result=_app.Actors.Catalog.Where(e=>(category=="Все"||e.Category==category)&&(family=="Все"||e.Family==family)&&(needle.Length==0||e.Search.Contains(needle))&&(!ammo||_app.Actors.CanStore(e,slot))).ToList();
            if(_filterCache.Count>40)_filterCache.Clear();_filterCache[key]=result;
        }
        return result;
    }

    private void SpritePreview(Rect rect,ActorTools.Entry entry)
    {
        if(!_iconsSupported||entry==null)return;
        try
        {
            // A radiant entry owns exact native variant art. Missing art must
            // never fall through to the ordinary base slime or a tinted prototype.
            if(entry.IsRadiant)
            {
                var radiantIcon=entry.Icon;
                if(radiantIcon==null||radiantIcon.WasCollected||radiantIcon.texture==null)L.Label(rect,"—",_badge);
                else DrawSprite(rect,radiantIcon);
                return;
            }
            if(entry.IsPrototype)
            {
                Color saved=GUI.color;GUI.color=entry.Prototype=="rad"?new Color(.45f,1f,.32f):new Color(.65f,.80f,1f);
                try{DrawSprite(rect,entry.Icon??entry.Type?.Icon);}finally{GUI.color=saved;}
                Rect badge=new(rect.xMax-18,rect.yMax-20,20,20);L.Box(badge,GUIContent.none,_selectedButton);
                L.Label(badge,entry.Prototype=="rad"?"Р":"М",_badge);
                return;
            }
            var slime = entry.Type?.TryCast<Il2Cpp.SlimeDefinition>();
            if (slime != null && slime.IsLargo)
            {
                // Largo badges combine the two real parent icons instead of
                // reusing a misleading single-species icon or leaving a blank.
                var bases = slime.BaseSlimes;
                L.Box(rect, GUIContent.none, _selectedButton);
                if (bases != null && bases.Length >= 2)
                {
                    DrawSprite(new Rect(rect.x + 1, rect.y + 1, rect.width * .73f, rect.height * .73f), bases[0]?.Icon ?? bases[0]?.icon);
                    DrawSprite(new Rect(rect.x + rect.width * .27f, rect.y + rect.height * .27f, rect.width * .73f, rect.height * .73f), bases[1]?.Icon ?? bases[1]?.icon);
                }
                else L.Label(rect, "Л", _label);
                return;
            }
            var sprite=entry.Icon??entry.Type?.Icon??entry.Type?.icon;
            DrawSprite(rect, sprite);
        }
        catch { /* One incomplete prototype must not disable all other icons. */ }
    }

    private void DrawSprite(Rect rect, Sprite sprite)
    {
        if(sprite==null||sprite.WasCollected)return;
        if(!_nativePaintTextures.TryGetValue(sprite.Pointer,out var t)||t==null||t.WasCollected)
        {
            t=sprite.texture;if(t==null||t.WasCollected)return;
            if(_nativePaintTextures.Count<NativePaintCacheBudget)_nativePaintTextures[sprite.Pointer]=t;
        }
        var r=sprite.textureRect;
            if(r.width<=0||r.height<=0)return;
            // DrawTextureWithTexCoords enters a broken Internal_DrawTextureArguments
            // managed wrapper in this player (confirmed native crash dump).
            // Use the already-tested native DrawTexture overload, clipped to the atlas cell.
            if(!UiThemePolicy.TryFitSprite(r.width,r.height,rect.width,rect.height,
                out float fitX,out float fitY,out float fitWidth,out float fitHeight))return;
            float sx=fitWidth/r.width,sy=sx;
            BeginGroup(new Rect(rect.x+fitX,rect.y+fitY,fitWidth,fitHeight));
            try{GUI.DrawTexture(new Rect(-r.x*sx,-(t.height-r.y-r.height)*sy,t.width*sx,t.height*sy),t);}
            finally{EndGroup();}
    }

    private void Catalog(Rect rect,List<ActorTools.Entry> entries,ref Vector2 scroll,ref ActorTools.Entry selection)
    {
        if(selection==null||!entries.Contains(selection))
        {
            string selectedKey=selection?.CatalogKey;
            selection=!string.IsNullOrEmpty(selectedKey)?entries.Find(e=>string.Equals(e.CatalogKey,selectedKey,StringComparison.Ordinal)):null;
            if(selection==null)selection=entries.Count>0?entries[0]:null;
        }
        L.Box(rect,GUIContent.none,_card);
        const float row=48;
        // Reserve the footer hint as well as the top/bottom inset. Shorter
        // inventory lists must not draw their final row underneath the hint.
        float rowsHeight = Mathf.Max(row, Mathf.Floor((rect.height - 52) / row) * row);
        Rect viewport=new(rect.x+12,rect.y+12,rect.width-24,rowsHeight);
        scroll=BeginScroll(rect.y < 200 ? 10 : 11,viewport,scroll,entries.Count*row);
        try
        {
            int first=Mathf.Max(0,Mathf.FloorToInt(scroll.y/row)-1),last=Mathf.Min(entries.Count,first+Mathf.CeilToInt(viewport.height/row)+3);
            for(int i=first;i<last;i++)
            {
                var e=entries[i];float y=i*row-scroll.y;
                Rect b=new(0,y,viewport.width-52,row-4);
                if(Button(b,GUIContent.none,e==selection?_selectedButton:_button))selection=e;
                bool hovered=CanHover(b);
                float offset=HoverOffset(b,hovered,hovered&&Mouse.current!=null&&Mouse.current.leftButton.isPressed);
                SpritePreview(HoverChildRect(b,new Rect(9,y+5,34,34),offset),e);
                string caption=e.IsRadiant?e.Name+" · нативный"+(e.Icon==null?" · нет иконки":""):e.Name;
                HoverText(HoverChildRect(b,new Rect(52,y+1,b.width-65,row-6),offset),caption,_label,offset);
            }
            if(entries.Count==0)L.Label(new Rect(18,18,viewport.width-44,100),"Ничего не найдено.\nИзмените название или категорию.",_muted);
        }
        finally{EndGroup();}
        Hint(new Rect(rect.x + 12, rect.yMax - 38, rect.width - 24, 26), "ЛКМ — перетаскивание полоски · колесо — прокрутка", _muted);
    }

    private void SpawnPage(Rect area)
    {
        BeginGroup(area);
        try
        {
            L.Label(new Rect(0,0,area.width-470,36),"Создание · каталог игры",_heading);
            CreationNavigation(area.width);
            _search=TextBox("spawn-search",new Rect(0,48,area.width-148,38),_search);
            if(Button(new Rect(area.width-136,48,136,38),"Обновить",_button))_app.Queue("catalog",_app.Actors.RefreshCatalog);
            const float top=102,sidebar=168,gap=14;
            float rightW=322,x=area.width-rightW,listX=sidebar+gap,listW=x-gap-listX,height=area.height-top;
            L.Box(new Rect(0,top,sidebar,height),GUIContent.none,_card);
            for(int i=0;i<_categories.Length;i++)
                if(Button(new Rect(10,top+12+i*53,sidebar-20,43),_categories[i],i==_category?_selectedButton:_button))
                {_category=i;_family="Все";_catalogScroll=Vector2.zero;_selected=null;ClearInteraction();}
            bool food=_categories[_category]=="Еда",materials=_categories[_category]=="Ресурсы",slimes=_categories[_category]=="Слаймы";
            float filterHeight=food||materials||slimes?52:0;
            if(filterHeight>0)
            {
                var families=food?new[]{"Все","Фрукты","Овощи","Мясо","Прочее питание"}:materials?new[]{"Все","Материалы","Прочее"}:new[]{"Все","Лучистые"};
                float fw=(listW-(families.Length-1)*6)/families.Length;
                for(int i=0;i<families.Length;i++)
                    if(Button(new Rect(listX+i*(fw+6),top,fw,40),families[i]=="Прочее питание"?"Прочее":families[i],_family==families[i]?_selectedButton:_button))
                    {_family=families[i];_catalogScroll=Vector2.zero;_selected=null;}
            }
            SpawnTiles(new Rect(listX,top+filterHeight,listW,height-filterHeight),Filter(_search,_categories[_category],false,_family));
            L.Box(new Rect(x,top,rightW,height),GUIContent.none,_card);
            SpritePreview(new Rect(x+18,top+18,60,60),_selected);
            L.Label(new Rect(x+92,top+18,rightW-110,60),_selected?.Category??"Выберите объект",_tableHeading);
            L.Label(new Rect(x+18,top+94,rightW-36,66),_selected?.Name??"Нажмите карточку слева",_wrap);
            bool gordo=_selected?.Category=="Гордо";
            if(gordo)
            {
                L.Label(new Rect(x+18,top+170,rightW-36,112),DetailedHelp?"1. Выберите вид Гордо.\n2. Наведите камеру на свободную землю в 9 м перед собой.\n3. Нажмите «Разместить Гордо».":"Наведите камеру на свободную землю в 9 м перед собой.",_atlasDetail);
                Hint(new Rect(x+18,top+284,rightW-36,38),"Один Гордо на нативной ловушке",_small);
            }
            else
            {
                L.Label(new Rect(x+18,top+174,rightW-36,28),"Количество · "+_quantity,_label);
                _quantity=SafetyPolicy.SpawnCount(Mathf.RoundToInt(Slider(2,new Rect(x+18,top+215,rightW-36,30),_quantity,1,25)));
                int[] counts={1,5,10,25};float width=(rightW-36-18)/4;
                for(int i=0;i<counts.Length;i++)if(Button(new Rect(x+18+i*(width+6),top+258,width,40),counts[i].ToString(),_quantity==counts[i]?_selectedButton:_button))_quantity=counts[i];
            }
            bool enabled=GUI.enabled;GUI.enabled=enabled&&_selected?.CanSpawn==true&&!_app.Actors.GordoSpawnPending;
            if(Button(new Rect(x+18,top+330,rightW-36,46),gordo?"Разместить Гордо":"Создать перед камерой",_accentButton))
            {var entry=_selected;int count=gordo?1:_quantity;_app.Queue("spawn",()=>_app.Actors.Spawn(entry,count));}
            GUI.enabled=enabled&&!_app.Actors.GordoSpawnPending;
            if(Button(new Rect(x+18,top+388,rightW-36,42),"Очистить созданное ("+_app.Actors.OwnedCount+")",_button))_app.Queue("cleanup owned",_app.Actors.Cleanup);
            float hintY=top+486;bool chickenArmed=_chickenArm>Time.unscaledTime;
            if(Button(new Rect(x+18,top+436,rightW-36,42),chickenArmed?"Нажмите ещё раз: удалить ВСЕХ куриц":"Удалить всех куриц",chickenArmed?_accentButton:_button))
            {
                if(chickenArmed){_chickenArm=0f;_app.Queue("cleanup chickens",_app.Actors.CleanupChickens);}
                else _chickenArm=Time.unscaledTime+4f;
            }
            GUI.enabled=enabled;
            string hint=gordo?_app.Actors.GordoSpawnStatus:_selected?.CanSpawn==false?_selected.UnavailableReason:_selected?.IsPrototype==true?"Только визуальный Pink-прототип, без уникальных способностей.":_selected?.IsRadiant==true?"Нативный лучистый вариант. Вакпак не поддерживается.":DetailedHelp?"В 2,5 м перед камерой. Очистка удаляет только созданное этим модом.":"";
            L.Label(new Rect(x+18,hintY,rightW-36,Mathf.Max(40,height-(hintY-top)-6)),hint,_atlasDetail);
        }
        finally{EndGroup();}
    }

    private float SmallTileButtonWidth(string text)=>UiThemePolicy.SmallButtonWidth(_buttonText14.CalcSize(ButtonContent(text)).x);
    private void SpawnTiles(Rect rect,List<ActorTools.Entry> entries)
    {
        if(_selected==null||!entries.Contains(_selected))_selected=entries.Count>0?entries[0]:null;
        L.Box(rect,GUIContent.none,_card);
        const float row=104;float width=(rect.width-90)/2;
        var viewport=new Rect(rect.x+10,rect.y+10,rect.width-20,rect.height-48);
        _catalogScroll=BeginScroll(10,viewport,_catalogScroll,((entries.Count+1)/2)*row);
        try
        {
            int first=Mathf.Max(0,Mathf.FloorToInt(_catalogScroll.y/row)-1)*2;
            int last=Mathf.Min(entries.Count,first+2*(Mathf.CeilToInt(viewport.height/row)+3));
            for(int i=first;i<last;i++)
            {
                var entry=entries[i];float tx=(i%2)*(width+10),ty=(i/2)*row-_catalogScroll.y;
                Rect tile=new(tx,ty,width,row-10);
                // 0.8.2: per-tile removal. «Мои ×» — only what the mod created from this tile,
                // «Все ×» — every actor of the type in the world (second click confirms).
                // The small buttons claim the click BEFORE the tile underneath them.
                bool tools=entry.Type!=null&&entry.Category!="Гордо";
                bool world=tools&&!entry.IsPrototype&&!entry.IsRadiant;
                // 0.8.3: sized by the measured (translated) label: «Mine ×» was clipped at a fixed 62 px.
                const float smallH=28;
                float ownW=SmallTileButtonWidth("Мои ×"),allW=Mathf.Max(SmallTileButtonWidth("Все ×"),SmallTileButtonWidth("Точно?"));
                Rect allRect=new Rect(tx+width-10-allW,ty+row-10-smallH-8,allW,smallH);
                Rect ownRect=world?new Rect(allRect.x-6-ownW,allRect.y,ownW,smallH):new Rect(tx+width-10-ownW,allRect.y,ownW,smallH);
                int own=tools?_app.Actors.OwnedCountFor(entry):0;
                bool armed=world&&_tileArmEntry==entry&&_tileArm>Time.unscaledTime;
                bool ownHit=tools&&own>0&&Pressed(ownRect),allHit=world&&Pressed(allRect);
                if(Button(tile,GUIContent.none,entry==_selected?_selectedButton:_button))_selected=entry;
                SpritePreview(new Rect(tx+10,ty+14,44,44),entry);
                L.Label(new Rect(tx+64,ty+8,width-76,54),entry.Name,_atlasDetail);
                float kindW=tools?ownRect.x-tx-18:width-24;
                // 0.8.3: never a clipped stub («Nati…»): the full type, else a short one for the notable
                // cases (prototype, radiant, unavailable), else nothing — an ordinary native tile needs no label.
                var kind=ButtonContent(entry.IsPrototype?"Визуальный прототип":entry.CanSpawn?(entry.IsRadiant?"Лучистый · нативный":"Нативный объект"):"Недоступен в этой версии");
                string kindShort=entry.IsPrototype?"Прототип":entry.CanSpawn?(entry.IsRadiant?"Лучистый":null):"Недоступен";
                if(kindShort!=null&&_small.CalcSize(kind).x>kindW)kind=ButtonContent(kindShort);
                if(kindW>=24&&_small.CalcSize(kind).x<=kindW)L.Label(new Rect(tx+12,ty+64,kindW,24),kind,_small);
                if(tools)
                {
                    bool enabled=GUI.enabled;GUI.enabled=enabled&&own>0;
                    Button(ownRect,"Мои ×",_button);
                    GUI.enabled=enabled;
                    if(world)Button(allRect,armed?"Точно?":"Все ×",armed?_accentButton:_button);
                }
                if(ownHit){var target=entry;_app.Queue("cleanup type owned",()=>_app.Actors.CleanupEntryOwned(target));}
                if(allHit)
                {
                    if(armed){_tileArm=0f;_tileArmEntry=null;var target=entry;_app.Queue("cleanup type all",()=>_app.Actors.CleanupEntryAll(target));}
                    else{_tileArmEntry=entry;_tileArm=Time.unscaledTime+4f;_app.Notify("Нажмите «Точно?»: удалить ВСЕХ «"+entry.Name+"» во всём мире, включая загоны.");}
                }
            }
            if(entries.Count==0)L.Label(new Rect(12,18,viewport.width-48,96),"Ничего не найдено. Измените поиск или категорию.",_muted);
        }
        finally{EndGroup();}
        Hint(new Rect(rect.x+12,rect.yMax-30,rect.width-24,24),"Выберите карточку · колесо — прокрутка",_small);
    }

    private void CurrencyPage(Rect area)
    {
        BeginGroup(area);
        try
        {
            // 0.8.3: the native Newbucks coin and the balance, then the amount field, add and native maximum right after it.
            const float maxW = 124, addW = 128, fieldW = 200;
            var coin = _app.Actors.CurrencyIcon();
            float balanceX = 0;
            if (coin != null) { DrawSprite(new Rect(0, 2, 38, 38), coin); balanceX = 46; }
            string balance = _app.Actors.Currency().ToString("N0");
            var balanceContent = RawContent(coin != null ? balance : Loc.T("Нью-баксы: ") + balance);
            float balanceW = Mathf.Ceil(_heading.CalcSize(balanceContent).x) + 4;
            L.Label(new Rect(balanceX, 1, balanceW, 40), balanceContent, _heading);
            float fieldX = Mathf.Min(balanceX + balanceW + 22, area.width - maxW - addW - fieldW - 20);
            _currencyAmount = TextBox("currency-amount", new Rect(fieldX, 2, fieldW, 38), _currencyAmount, 13, "Сумма…");
            if (Button(new Rect(fieldX + fieldW + 10, 2, addW, 38), "Добавить", _accentButton))
            {
                int amount = SafetyPolicy.ParseCurrency(_currencyAmount);
                if (amount <= 0) _app.Notify("Введите сумму — целое число больше нуля.");
                else _app.Queue("currency add", () => _app.Actors.AddCurrency(amount));
            }
            if (Button(new Rect(fieldX + fieldW + addW + 20, 2, maxW, 38), "Максимум", _button)) _app.Queue("currency max", () => _app.Actors.AddCurrency(0, true));
            L.Label(new Rect(0, 52, area.width, 32), "Заполнить выбранный слот вакпака", _heading);
            var ammo = _app.Context.PlayerState.Ammo;
            float sw = (area.width - (ammo.Slots.Length - 1) * 6) / Math.Max(1, ammo.Slots.Length);
            for (int i = 0; i < ammo.Slots.Length; i++)
            {
                bool enabled = GUI.enabled; GUI.enabled = enabled && ammo.Slots[i].IsUnlocked;
                // A 44 px hitbox / 40 px face fits both 20 px corner borders.
                // The previous 34 px box forced the top/bottom slices to overlap.
                if (Button(new Rect(i * (sw + 6), 92, sw, 44), "Слот " + (i + 1) + " · " + ammo.GetSlotCount(i), i == ammo._selectedAmmoIdx ? _selectedButton : _button)) { int slot = i; _app.Queue("select ammo slot", () => ammo.SetSelectedSlot(slot)); }
                GUI.enabled = enabled;
            }
            _ammoSearch = TextBox("ammo-search", new Rect(0, 143, area.width, 36), _ammoSearch);
            float leftW = Mathf.Round((area.width - 16) * .60f);
            const float panelTop = 191;
            float panelHeight = area.height - panelTop;
            Catalog(new Rect(0, panelTop, leftW, panelHeight), Filter(_ammoSearch, "Все", true), ref _ammoScroll, ref _ammoSelected);
            float x = leftW + 16, w = area.width - x;
            L.Box(new Rect(x, panelTop, w, panelHeight), GUIContent.none, _card);
            SpritePreview(new Rect(x + 16, panelTop + 16, 54, 54), _ammoSelected);
            L.Label(new Rect(x + 86, panelTop + 16, w - 102, 72), _ammoSelected != null ? _ammoSelected.Name : "Выберите совместимый ресурс", _wrap);
            bool allow = GUI.enabled; GUI.enabled = allow && _ammoSelected != null;
            if (Button(new Rect(x + 16, panelTop + 116, w - 32, 48), "Заполнить " + ModInterop.FillText, _accentButton)) { var entry = _ammoSelected; _app.Actors.RequestAmmo(entry); }
            GUI.enabled = allow;
            L.Label(new Rect(x + 16, panelTop + 190, w - 32, 106), (ModInterop.CustomVacLimits ? "Замена — с подтверждением. Лимит слота задаёт мод вакпака." : "Замена — с подтверждением. 100 шт. могут превысить ёмкость."), _muted);
        }
        finally { EndGroup(); }
    }

    private void WorldPage(Rect area)
    {
        BeginGroup(area);
        try
        {
            var world=_app.World;float left=area.width*.40f,rightX=left+20,right=area.width-rightX;
            int minutes=(int)(world.Hour()*60f)%1440;
            L.Box(new Rect(0,0,left,460),GUIContent.none,_card);
            L.Box(new Rect(rightX,0,right,460),GUIContent.none,_card);
            L.Label(new Rect(18,14,left-36,38),"Время · "+(minutes/60).ToString("00")+":"+(minutes%60).ToString("00"),_heading);
            float half=(left-44)/2;
            if(Button(new Rect(18,66,half,40),"+1 час",_button))_app.Queue("time +1",()=>world.AdvanceHours(1));
            if(Button(new Rect(26+half,66,half,40),"+6 часов",_button))_app.Queue("time +6",()=>world.AdvanceHours(6));
            if(Button(new Rect(18,118,left-36,44),world.FreezeTime?"Продолжить время":"Остановить время",world.FreezeTime?_selectedButton:_button))
            {bool freeze=!world.FreezeTime;_app.Queue("freeze time",()=>world.SetFreezeTime(freeze));}
            _hour=Slider(3,new Rect(18,180,left-36,32),_hour,0,23.99f);
            if(Button(new Rect(18,222,left-36,42),"Перемотать к "+((int)_hour).ToString("00")+":"+((int)((_hour%1)*60)).ToString("00"),_button))
            {float h=_hour;_app.Queue("set hour",()=>world.SetHour(h));}
            string[] times={"Утро · 06:00","День · 12:00","Закат · 18:00","Ночь · 00:00"};float[] hours={6,12,18,0};
            for(int i=0;i<4;i++)if(Button(new Rect(18+(i%2)*(half+8),280+(i/2)*52,half,42),times[i],_button))
            {_hour=hours[i];float h=_hour;_app.Queue("set time",()=>world.SetHour(h));}
            Hint(new Rect(18,396,left-36,44),"Только вперёд · таймеры сохраняются",_small);
            L.Label(new Rect(rightX+18,14,right-36,38),"Погода",_heading);
            L.Label(new Rect(rightX+18,56,right-36,42),world.ZoneName,_muted);
            string[] ids={"clear","rain","thunder","slime","pollen","wind","lava","solar"};
            string[] roles={"WeatherClear","WeatherRain","WeatherThunder","WeatherSlime","WeatherPollen","WeatherWind","WeatherLava","WeatherSolar"};
            float bw=(right-44)/2;
            for(int i=0;i<WeatherNames.Length;i++)
            {
                var choice=i==0?null:world.FindPreset(ids[i]);bool enabled=GUI.enabled;
                GUI.enabled=enabled&&world.WeatherZone!=null&&(i==0||choice!=null);
                string role=choice!=null?_app.Map.Icons.WeatherRole(choice.Pattern):roles[i];
                if(NativeRoleButton(new Rect(rightX+18+(i%2)*(bw+8),108+(i/2)*48,bw,40),WeatherNames[i],role))
                {if(i==0)_app.Queue("clear weather",world.ClearWeather);else{var c=choice;_app.Queue("weather",()=>world.SetWeather(c));}}
                GUI.enabled=enabled;
            }
            L.Label(new Rect(rightX+18,306,right-36,28),"Нативные варианты",_small);
            Rect view=new(rightX+18,340,right-36,98);
            _weatherScroll=BeginScroll(12,view,_weatherScroll,world.Weather.Count*44f,44f);
            try{for(int i=0;i<world.Weather.Count;i++){float y=i*44-_weatherScroll.y;if(y+40<=0||y>=view.height)continue;
                var c=world.Weather[i];if(NativeRoleButton(new Rect(4,y,view.width-56,40),c.Name,_app.Map.Icons.WeatherRole(c.Pattern)))_app.Queue("native weather",()=>world.SetWeather(c));}}
            finally{EndGroup();}
            float col=(area.width-16)/2;
            bool tarr=Toggle(new Rect(0,478,col,90),"Отключить Варров",world.DisableTarr,"Запрет появления; существующие Варры не удаляются.");
            if(tarr!=world.DisableTarr)_app.Queue("tarr",()=>world.SetDisableTarr(tarr));
            bool freezeMobs=Toggle(new Rect(col+16,478,col,90),"Заморозить слаймов",world.FreezeMobs,"Эффект обратим; только загруженные слаймы.");
            if(freezeMobs!=world.FreezeMobs)_app.Queue("freeze mobs",()=>world.SetFreezeMobs(freezeMobs));
        }finally{EndGroup();}
    }

    private void CinemaPage(Rect area)
    {
        BeginGroup(area);
        try
        {
            if(_timePage){CinematicRatePage(area);return;}
            if(_phenomenaPage){PhenomenaPage(area.width);return;}
            if(_capturePage){CapturePage(area);return;}
            var cinema = _app.Cinema;
            // 0.8.4: the three sub-pages in one row, the clean-frame switch, then the teleport points;
            // explanations (the empty-list text too) appear only with «?».
            float third=(area.width-24)/3;
            if(Button(new Rect(0,0,third,44),"Камера и запись…",_accentButton)){_capturePage=true;ClearInteraction();return;}
            if(Button(new Rect(third+12,0,third,44),"Скорость времени…",_violetButton)){_timePage=true;ClearInteraction();return;}
            if(Button(new Rect(2*(third+12),0,third,44),"Нативные феномены…",_violetButton))
            {_phenomenaPage=true;ClearInteraction();_app.Queue("native phenomena census",()=>_app.Phenomena.SetInspect(true));return;}
            bool hidden = Toggle(new Rect(0, 58, area.width, 64), "Чистый кадр / скрыть HUD", cinema.HideHud, "Убирает игровой интерфейс и вакпак. Меню остаётся доступно по "+(_app.Hotkey?.Display??"Insert")+".");
            if (hidden != cinema.HideHud) _app.Queue("hide HUD", () => cinema.SetHideHud(hidden));
            L.Label(new Rect(0, 140, area.width, 35), "Точки телепортации · " + cinema.Waypoints.Count + " / 5", _heading);
            _waypointName = TextBox("waypoint-name", new Rect(0, 184, area.width - 234, 38), _waypointName, 40, "Название новой точки…");
            bool previous = GUI.enabled; GUI.enabled = previous && cinema.Waypoints.Count < SafetyPolicy.MaxWaypoints;
            if (Button(new Rect(area.width - 218, 184, 218, 38), "Сохранить позицию", _accentButton)) { string name = _waypointName; _app.Queue("save waypoint", () => cinema.SaveWaypoint(name)); _waypointName = ""; }
            GUI.enabled = previous;
            if(cinema.Waypoints.Count==0)
            {
                Hint(new Rect(0,244,area.width,30),"Первая точка — ваш быстрый возврат",_label);
                Hint(new Rect(0,278,area.width,38),"Введите название и сохраните текущую позицию. Можно держать до пяти точек.");
            }
            for (int i = 0; i < cinema.Waypoints.Count; i++)
            {
                var point = cinema.Waypoints[i]; float y = 240 + i * 62;
                L.Box(new Rect(0, y, area.width, 54), GUIContent.none, _card);
                L.Label(new Rect(15, y + 7, area.width - 300, 40), point.Name + "   " + point.Position.ToString("F1"), _label);
                if (Button(new Rect(area.width - 269, y + 10, 186, 34), "Телепортироваться", _button)) _app.Queue("teleport waypoint", () => cinema.Teleport(point));
                if (Button(new Rect(area.width - 72, y + 10, 57, 34), "×", _button)) { var remove = point; _app.Queue("delete waypoint", () => cinema.Waypoints.Remove(remove)); }
            }
            Hint(new Rect(0, area.height - 40, area.width, 36), "Точки хранятся в текущем сеансе и не переносятся между сейвами.", _muted);
        }
        finally { EndGroup(); }
    }

    // 0.8.0: «Ферма…» (moved from «Карта»), FPS counter next to the info HUD, hints only with «?».
    private bool _farmPage;
    private static readonly string[] FpsCornerArrows={"↖","↗","↙","↘"};
    private static readonly string[] HudPlaces={"Справа","Слева","Внизу"};
    internal void ShowFarmForTest(){ShowTabForTest(7);_farmPage=true;}
    private void ConveniencePage(Rect area)
    {
        if(_saveNamePage){SaveNamePage(area);return;}
        BeginGroup(area);
        try
        {
            if(_farmPage)
            {
                L.Label(new Rect(0,0,area.width-220,38),"Фермерский помощник · установленные устройства",_heading);
                if(Button(new Rect(area.width-200,0,200,40),"Назад",_button)){_farmPage=false;ClearInteraction();return;}
                FarmPage(area.width,56);
                return;
            }
            var tools=_app.Convenience;
            // 0.8.4: every card holds two rows of controls; the explanations (and the room for them) appear
            // only with «?». «Имя сейва…» sits in the heading row, the farm helper next to the auto-refill.
            bool help=DetailedHelp;
            float col=(area.width-24)/2,x=col+24,bw=(col-48)/2;
            const float top=54,r1=56,r2=108,hintY=160;
            float cardH=help?214:166,row2=top+cardH+14;
            L.Label(new Rect(0,0,area.width-250,40),"Удобство · профили, HUD и автоматизация",_heading);
            if(Button(new Rect(area.width-230,0,230,40),"Имя сейва…",_accentButton))OpenSaveNameUi();

            L.Box(new Rect(0,top,col,cardH),GUIContent.none,_card);
            L.Label(new Rect(20,top+12,col-40,34),"Профили · "+tools.ProfileName,_heading);
            bool enabled=GUI.enabled;GUI.enabled=enabled&&!_app.Map.Busy;
            if(Button(new Rect(20,top+r1,bw,44),"Исследователь",tools.Profile==1?_selectedButton:_button))
                _app.Queue("explorer profile",()=>tools.SetProfile(1));
            if(Button(new Rect(28+bw,top+r1,bw,44),"Кадр",tools.Profile==2?_selectedButton:_button))
                _app.Queue("frame profile",()=>tools.SetProfile(2));
            GUI.enabled=enabled&&tools.Profile!=0&&!_app.Map.Busy;
            if(Button(new Rect(20,top+r2,col-40,44),"Восстановить прежние настройки",_button))
                _app.Queue("restore profile",()=>tools.SetProfile(0));
            GUI.enabled=enabled;
            Hint(new Rect(20,top+hintY,col-40,46),"Исследователь: защита, энергия, ×2.\nКадр: полёт, пауза, без HUD.");

            L.Box(new Rect(x,top,col,cardH),GUIContent.none,_card);
            L.Label(new Rect(x+20,top+12,col-40,34),"Исследование и SOS",_heading);
            if(Button(new Rect(x+20,top+r1,bw,44),tools.TrackSafe?"SOS: запись ВКЛ":"SOS: запись ВЫКЛ",tools.TrackSafe?_selectedButton:_button))
            {bool track=!tools.TrackSafe;_app.Queue("safe position tracking",()=>tools.SetSafeTracking(track));}
            GUI.enabled=enabled&&tools.HasSafe&&!_app.Map.Busy;
            if(Button(new Rect(x+28+bw,top+r1,bw,44),"Вернуться · Ctrl + Home",_button))
                _app.Queue("SOS rescue",tools.Rescue);
            GUI.enabled=enabled;
            if(Button(new Rect(x+20,top+r2,col-40,44),"Найти неоткрытую капсулу · без автосбора",_accentButton))
                _app.Queue("nearest unopened capsule",tools.OpenNearestCapsule);
            Hint(new Rect(x+20,top+hintY,col-40,46),tools.TrackSafe?tools.SafeStatus:"Записываются только проверенные наземные точки.");

            L.Box(new Rect(0,row2,col,cardH),GUIContent.none,_card);
            L.Label(new Rect(20,row2+12,col-40,34),"Автопополнение и ферма",_heading);
            if(Button(new Rect(20,row2+r1,col-40,44),tools.Refilling?"Отключить автопополнение":"Включить для выбранного слота…",tools.Refilling?_selectedButton:_button))
                _app.Queue("auto refill request",tools.RequestRefill);
            if(Button(new Rect(20,row2+r2,col-40,44),"Фермерский помощник…",_button)){_farmPage=true;ClearInteraction();return;}
            Hint(new Rect(20,row2+hintY,col-40,24),tools.Refilling?tools.RefillStatus:"Порог: "+ModInterop.RefillText,_small);
            Hint(new Rect(20,row2+hintY+24,col-40,24),"Сохраняется игрой · пустой или другой слот — стоп.",_small);

            // Info HUD (0.8.3: the FPS counter, keys, graphics and the console moved to «Настройки»).
            L.Box(new Rect(x,row2,col,cardH),GUIContent.none,_card);
            L.Label(new Rect(x+20,row2+12,col-40,34),"Инфо-HUD",_heading);
            float tw=Mathf.Round((col-40)*.46f),cx=x+20+tw+10,cw=col-40-tw-10;
            if(Button(new Rect(x+20,row2+r1,tw,44),tools.ShowHud?"Инфо-HUD: ВКЛ":"Инфо-HUD: ВЫКЛ",tools.ShowHud?_selectedButton:_button))
            {bool show=!tools.ShowHud;_app.Queue("info HUD",()=>tools.SetHud(show));}
            float pw=(cw-16)/3;
            for(int i=0;i<HudPlaces.Length;i++)
                if(Button(new Rect(cx+i*(pw+8),row2+r1,pw,44),HudPlaces[i],tools.HudPlacement==i?_selectedButton:_button))tools.HudPlacement=i;
            if(Button(new Rect(x+20,row2+r2,col-40,44),"Клавиши, FPS, графика и консоль →",_violetButton))ShowPage(8);
            Hint(new Rect(x+20,row2+hintY,col-40,46),"Зона, XYZ, время и активные эффекты поверх игры.");

            float qy=row2+cardH+14;
            Hint(new Rect(0,qy,area.width,26),"Все новые функции выключены по умолчанию. Включайте только то, что нужно сейчас.",_small);
            Hint(new Rect(0,qy+28,area.width,26),"SOS и инфо-HUD — на сеанс · клавиши, FPS и графика — во вкладке «Настройки».",_small);
        }
        finally {EndGroup();}
    }

    // 0.8.3 «Настройки»: keys (the menu key + optional action keys), smooth zoom, the FPS counter and the
    // graphics / console pages that used to sit at the bottom of «Удобство».
    private void SettingsPage(Rect area)
    {
        if(_optimizationPage){OptimizationPage(area);return;}
        if(_infoPage){InfoPage(area);return;}
        BeginGroup(area);
        try
        {
            bool enabled=GUI.enabled;
            float col=(area.width-24)/2,x=col+24,lw=col-40;
            L.Label(new Rect(0,0,area.width-220,36),"Настройки · клавиши, зум, FPS и графика",_heading);

            // Keys: the menu key first, then the action keys (every one unbound by default).
            // 0.8.4: the cards hug their controls; the explanations (and the room for them) appear only with «?».
            const float top=48,keyRow=104,stride=50,rowH=40,nameW=200,keyW=196;
            bool help=DetailedHelp;
            float bottom=help?area.height:keyRow+ActionHotkeyPolicy.Count*stride+rowH+20;
            var hotkey=_app.Hotkey;var keys=_app.Shortcuts;
            bool capturing=(hotkey?.Capturing??false)||(keys!=null&&keys.Capturing>=0);
            L.Box(new Rect(0,top,col,bottom-top),GUIContent.none,_card);
            L.Label(new Rect(20,top+12,lw,34),capturing?"Нажмите клавишу · Esc — отмена":"Горячие клавиши",_heading);
            float kx=20+nameW+8,rx=kx+keyW+8,rw=col-20-rx;
            if(hotkey!=null)
            {
                L.Label(new Rect(20,keyRow,nameW,rowH),"Клавиша меню",_muted);
                if(Button(new Rect(kx,keyRow,keyW,rowH),hotkey.Capturing?"Нажмите клавишу…":hotkey.Display+"  ·  изменить…",hotkey.Capturing?_selectedButton:_button))
                {if(hotkey.Capturing)hotkey.CancelCapture();else{keys?.CancelCapture();hotkey.StartCapture();}}
                GUI.enabled=enabled&&!hotkey.IsDefault&&!hotkey.Capturing;
                if(Button(new Rect(rx,keyRow,rw,rowH),"Insert",_button))hotkey.Reset();
                GUI.enabled=enabled;
            }
            if(keys!=null)
                for(int i=0;i<ActionHotkeyPolicy.Count;i++)
                {
                    float y=keyRow+(i+1)*stride;bool mine=keys.Capturing==i;string key=keys.Key(i);
                    L.Label(new Rect(20,y,nameW,rowH),ActionHotkeyPolicy.Titles[i],_muted);
                    string caption=mine?"Нажмите клавишу…":key==null?"Назначить…":keys.Blocked(i)?keys.Display(i)+" · занята меню":keys.Display(i)+"  ·  изменить…";
                    if(Button(new Rect(kx,y,keyW,rowH),caption,mine?_selectedButton:_button))
                    {if(mine)keys.CancelCapture();else keys.StartCapture(i);}
                    GUI.enabled=enabled&&key!=null&&!mine;
                    if(Button(new Rect(rx,y,rw,rowH),"×",_button))keys.Clear(i);
                    GUI.enabled=enabled;
                }
            float hy=keyRow+(ActionHotkeyPolicy.Count+1)*stride+4;
            Hint(new Rect(20,hy,lw,area.height-hy-10),capturing?"Esc — отмена · F1, Tab, Enter, пробел и стрелки не подходят":
                "Клавиши работают в игре и в меню · Ctrl/Alt + клавиша не срабатывает · сохраняются сразу.");

            var cap=_app.Capture;
            float zoomH=help?178:150,fpsH=help?150:112;
            // Smooth zoom: on/off and its key (C, Z, V, middle or side mouse button).
            L.Box(new Rect(x,top,col,zoomH),GUIContent.none,_card);
            L.Label(new Rect(x+20,top+12,lw,34),"Плавный зум",_heading);
            if(cap!=null)
            {
                if(Button(new Rect(x+20,top+56,lw,40),cap.ZoomEnabled?"Плавный зум: ВКЛ":"Плавный зум: ВЫКЛ",cap.ZoomEnabled?_selectedButton:_button))
                {bool on=!cap.ZoomEnabled;_app.Queue("smooth zoom",()=>cap.SetZoom(on));}
                L.Label(new Rect(x+20,top+104,120,36),"Клавиша",_muted);
                int zoomKey=NavigationChoice(new Rect(x+140,top+104,lw-120,36),CapturePolicy.ZoomKeys,cap.ZoomKey);
                if(zoomKey>=0&&zoomKey!=cap.ZoomKey)cap.SetZoomKey(zoomKey);
                Hint(new Rect(x+20,top+144,lw,28),cap.ZoomKey==4?"M4 — боковая кнопка мыши · сила зума — во вкладке «Съёмка».":"Держите клавишу — приближение · сила зума — во вкладке «Съёмка».",_small);
            }
            // FPS counter: on/off and the screen corner.
            float fy=top+zoomH+14;
            L.Box(new Rect(x,fy,col,fpsH),GUIContent.none,_card);
            L.Label(new Rect(x+20,fy+12,lw,34),"Счётчик FPS",_heading);
            if(cap!=null)
            {
                float half=Mathf.Round(lw*.5f);
                if(Button(new Rect(x+20,fy+56,half,40),cap.ShowFps?"Счётчик FPS: ВКЛ":"Счётчик FPS: ВЫКЛ",cap.ShowFps?_selectedButton:_button))
                    cap.SetShowFps(!cap.ShowFps);
                int corner=NavigationChoice(new Rect(x+30+half,fy+56,lw-half-10,40),FpsCornerArrows,cap.FpsCorner);
                if(corner>=0&&corner!=cap.FpsCorner)cap.SetFpsCorner(corner);
                Hint(new Rect(x+20,fy+104,lw,36),cap.ShowFps?cap.FpsText+" · виден и в меню игры":"Кадры в секунду и время кадра в выбранном углу.");
            }
            // Graphics (Potato Mode) and the console, formerly at the bottom of «Удобство».
            float gy=fy+fpsH+14;
            L.Box(new Rect(x,gy,col,bottom-gy),GUIContent.none,_card);
            L.Label(new Rect(x+20,gy+12,lw,34),"Графика и консоль",_heading);
            float bw=(lw-10)/2;
            if(Button(new Rect(x+20,gy+56,bw,44),"Оптимизация / Potato Mode",_goldButton))OpenOptimizationUi();
            if(Button(new Rect(x+30+bw,gy+56,bw,44),"Консоль / Инфо",_button)){_infoPage=true;ClearInteraction();}
            Hint(new Rect(x+20,gy+108,lw,bottom-gy-116),"Пресеты, дальность, текстуры и эффекты · версия, статус и последние ошибки.",_muted);
        }
        finally{EndGroup();}
    }

    internal void DrawTransitStatus()
    {
        if(!_app.Map.Busy)return;
        Styles();
        var matrix=GUI.matrix;var color=GUI.color;bool enabled=GUI.enabled;
        try
        {
            _scale=1;_groupOrigin=Vector2.zero;_groupOrigins.Clear();_groupRects.Clear();
            GUI.matrix=Matrix4x4.identity;GUI.color=Color.white;GUI.enabled=true;
            float width=Mathf.Min(660,Screen.width-32);Rect rect=new((Screen.width-width)/2,24,width,146);
            L.Box(rect,GUIContent.none,_hudFrame);Icon(new Rect(rect.x+16,rect.y+18,42,42),2);
            L.Label(new Rect(rect.x+70,rect.y+10,width-86,36),"Безопасная телепортация",_label);
            L.Label(new Rect(rect.x+16,rect.y+52,width-32,40),_app.Map.TeleportStatus,_muted);
            if(Button(new Rect(rect.x+16,rect.y+96,width-32,40),"Отменить переход / повторить возврат",_button))
                _app.Queue("safe transit return",_app.Map.AbortTeleport);
        }
        finally{GUI.matrix=matrix;GUI.color=color;GUI.enabled=enabled;}
    }

    internal void DrawSessionHud()
    {
        var tools=_app.Convenience;
        if(!_app.Ready||!tools.ShowHud||_app.MenuOpen||_app.Map.Busy||_app.Cinema.HideHud||_app.Map.NativeMapOpen||Time.timeScale<=0)return;
        if(Event.current==null||Event.current.type!=EventType.Repaint)return;
        Styles();
        var matrix=GUI.matrix;var color=GUI.color;var content=GUI.contentColor;var background=GUI.backgroundColor;
        try
        {
            GUI.matrix=Matrix4x4.identity;GUI.color=GUI.contentColor=GUI.backgroundColor=Color.white;
            float width=Mathf.Min(420,Screen.width-32),height=144;
            float x=tools.HudPlacement==1?24:tools.HudPlacement==2?(Screen.width-width)/2:Screen.width-width-24;
            float y=tools.HudPlacement==2?Mathf.Max(24,Screen.height-Mathf.Max(380,Screen.height*.36f)):72;
            L.Box(new Rect(x,y,width,height),GUIContent.none,_hudFrame);
            Icon(new Rect(x+12,y+12,28,28),15);
            L.Label(new Rect(x+50,y+8,width-136,30),tools.HudShortZone,_hudLabel);
            L.Label(new Rect(x+width-74,y+8,62,30),tools.HudTime,_hudLabel);
            L.Label(new Rect(x+16,y+40,width-32,30),tools.HudCoordinates,_hudLabel);
            L.Label(new Rect(x+16,y+78,width-32,54),tools.HudEffects,_hudEffects);
        }
        finally {GUI.matrix=matrix;GUI.color=color;GUI.contentColor=content;GUI.backgroundColor=background;}
    }

    private void CapsuleCard(Rect rect,MapTools.Point point,bool openMap)
    {
        var map=_app.Map;var view=map.Capsules.Get(point.Id);
        var rewards=view.Mismatch&&view.NativeObserved?view.NativeRewards:view.Rewards;
        if(_capsuleId!=point.Id){_capsuleId=point.Id;_capsuleScroll=Vector2.zero;}
        L.Box(rect,GUIContent.none,_frame);
        Rect podRect=new(rect.x+12,rect.y+12,38,38);
        if(!CanonIcon(podRect,CanonMapIconPolicy.TreasurePod))
        {
            var podIcon=map.Icons.Category("Капсулы");
            if(podIcon!=null)DrawSprite(podRect,podIcon);
        }
        L.Label(new Rect(rect.x+60,rect.y+8,rect.width-(openMap?76:120),42),view.Title,_label);
        if(!openMap&&Button(new Rect(rect.xMax-54,rect.y+8,44,44),"×",_button))
        {map.Selected=null;_hoverCapsule=null;_capsuleId="";return;}
        L.Label(new Rect(rect.x+16,rect.y+54,rect.width-32,20),map.CapsuleStateLabel(point),_small);
        string truth=view.Mismatch?"Расхождение · ниже нативные награды":view.Verification==CapsuleTruthPolicy.RewardStatus.Unverifiable?"Native-сверка неполная · награды базы":view.NativeVerified?"Награды сверены с загруженной капсулой":view.Baked?"База совпавшей версии · native-сверки нет":"Награды неизвестны";
        L.Label(new Rect(rect.x+16,rect.y+74,rect.width-32,18),truth,_small);
        // Native map: the footer carries «Цель / Телепорт / Отметить» (mockup 05).
        float footer=openMap?100:60;
        Rect viewport=new(rect.x+12,rect.y+94,rect.width-24,Mathf.Max(44,rect.height-94-footer));
        // Cached by managed view identity and viewport width. No repeated
        // text measurement/list allocation on each IMGUI event; native reads unchanged.
        if(_capsuleLayout==null||!ReferenceEquals(_capsuleLayout.View,view)||_capsuleLayout.Width!=viewport.width)
        {
            var layout=new CapsuleLayout{View=view,Width=viewport.width};
            layout.EvidenceHeight=view.Verification==CapsuleTruthPolicy.RewardStatus.Unverifiable?
                Mathf.Max(26,_atlasDetail.CalcHeight(ButtonContent(view.ValidationMessage),viewport.width-56)+12):0;
            layout.Extent=8+layout.EvidenceHeight;
            foreach(var reward in rewards)
            {
                float titleHeight=Mathf.Max(28,_wrap.CalcHeight(ButtonContent(reward.Title),viewport.width-92));
                float detailHeight=string.IsNullOrWhiteSpace(reward.Detail)?0:
                    _atlasDetail.CalcHeight(ButtonContent(reward.Detail),viewport.width-92);
                float rowHeight=Mathf.Max(64,titleHeight+detailHeight+42);
                layout.Rows.Add((titleHeight,detailHeight,rowHeight));layout.Extent+=rowHeight+12;
            }
            layout.DescriptionHeight=rewards.Count==0?Mathf.Max(72,_atlasDetail.CalcHeight(ButtonContent(view.Description),viewport.width-56)+16):0;
            layout.Extent+=layout.DescriptionHeight;_capsuleLayout=layout;
        }
        float evidenceHeight=_capsuleLayout.EvidenceHeight,descriptionHeight=_capsuleLayout.DescriptionHeight;
        float textWidth=viewport.width-92,extent=_capsuleLayout.Extent;
        var heights=_capsuleLayout.Rows;
        extent=Mathf.Max(viewport.height,extent);
        _capsuleScroll=BeginScroll(31,viewport,_capsuleScroll,extent,24);
        try
        {
            float y=8-_capsuleScroll.y;
            if(evidenceHeight>0)
            {
                L.Label(new Rect(4,y,viewport.width-56,evidenceHeight-8),view.ValidationMessage,_atlasDetail);
                y+=evidenceHeight;
            }
            for(int i=0;i<rewards.Count;i++)
            {
                var reward=rewards[i];var h=heights[i];
                var icon=map.Icons.Reward(reward.Key);
                if(icon!=null)DrawSprite(new Rect(4,y+4,48,48),icon);
                L.Label(new Rect(64,y,textWidth,h.Title),reward.Title,_wrap);
                L.Label(new Rect(64,y+h.Title+2,textWidth,24),reward.Kind+(reward.Kind=="Предмет"?" · × "+reward.Count:""),_small);
                if(h.Detail>0)L.Label(new Rect(64,y+h.Title+30,textWidth,h.Detail),reward.Detail,_atlasDetail);
                if(icon==null)L.Label(new Rect(4,y+52,54,20),map.Icons.RewardInfo(reward.Key).Availability==NativeIconPolicy.Availability.Ambiguous?"конфликт":"нет иконки",_small);
                y+=h.Row+12;
            }
            if(rewards.Count==0)L.Label(new Rect(4,y,viewport.width-56,descriptionHeight),view.Description,_atlasDetail);
        }
        finally {EndGroup();}
        if(openMap)L.Label(new Rect(rect.x+16,rect.yMax-footer+6,rect.width-32,24),"ID: "+point.Id,_small);
        else MapCardActions(new Rect(rect.x+12,rect.yMax-54,rect.width-24,42),point);
        if(openMap&&Button(new Rect(rect.x+16,rect.yMax-58,rect.width-32,44),"Показать на карте",_accentButton))
            _app.Queue("capsule on atlas",()=>map.OpenAt(point));
    }

    internal int MapProjectedForTest,MapMarkersForTest,MapCrowdedForTest;
    internal float MapMarkerSizeForTest;
    // M29: card slot memory (kept while the same object is shown and its slot stays clear)
    // and last-repaint facts for ct-atlas-state.
    private M29MapCardPolicy.Placement _mapCardPlacement;
    private M29MapCardPolicy.Slot? _mapCardSlot;
    private MapTools.Point _mapCardShown;
    private Vector2 _mapCardScreen;
    private readonly List<MapShieldPolicy.Box> _mapCardObstacles=new();
    private string _atlasChipSource,_atlasChipText;
    internal int MapOwnDrawnForTest,MapShownCoveredForTest,AtlasSearchClearsForTest;
    internal string MapCardForTest=>M29MapCardPolicy.Describe(_mapCardPlacement)+" shown="+
        (_mapCardShown==null?"none":_mapCardShown.Category+"|"+_mapCardShown.Id);
    private Rect MapCardRect(float sw,float sh,Rect panel,Rect[] chrome,MapTools.Point shown,Rect? anchor,bool commit)
    {
        bool same=shown!=null&&ReferenceEquals(shown,_mapCardShown)&&
            Mathf.Approximately(sw,_mapCardScreen.x)&&Mathf.Approximately(sh,_mapCardScreen.y);
        _mapCardObstacles.Clear();
        foreach(var r in chrome)_mapCardObstacles.Add(new MapShieldPolicy.Box(r.x,r.y,r.width,r.height));
        MapShieldPolicy.Box? box=anchor is Rect a?new MapShieldPolicy.Box(a.x,a.y,a.width,a.height):(MapShieldPolicy.Box?)null;
        var placement=M29MapCardPolicy.Place(sw,sh,new MapShieldPolicy.Box(panel.x,panel.y,panel.width,panel.height),
            box,_mapCardObstacles,same?_mapCardSlot:null);
        if(commit){_mapCardPlacement=placement;_mapCardSlot=placement.Slot;_mapCardShown=shown;_mapCardScreen=new Vector2(sw,sh);}
        var card=placement.Card;
        return new Rect(card.X,card.Y,card.Width,card.Height);
    }
    // Header chip of an active atlas search: hits on this map page, the query and a clear button.
    private void AtlasSearchChip(Rect chip,MapTools map)
    {
        DrawRoundedSurface(chip,_selectedButton.normal.background,Mathf.Min(20f,chip.height*.5f));
        float textW=chip.width-58;
        L.Label(new Rect(chip.x+12,chip.y+3,textW,18),ButtonContent(M29AtlasSearchPolicy.ChipHead(map.SearchHitsOnPage())),_atlasChipHead);
        if(!ReferenceEquals(map.Search,_atlasChipSource))
        {_atlasChipSource=map.Search;_atlasChipText=Ellipsize(M29AtlasSearchPolicy.ChipQuery(map.Search),textW,_atlasChip);}
        L.Label(new Rect(chip.x+12,chip.y+19,textW,22),ButtonContent(_atlasChipText),_atlasChip);
        if(Button(new Rect(chip.xMax-42,chip.y+5,38,34),"×",_button))ClearAtlasSearch();
    }
    // One clear path for the menu field, the native-map chip and ct-atlas-search clear.
    private void ClearAtlasSearch()=>SetAtlasSearch("");
    private void SetAtlasSearch(string text)
    {
        // A focused field writes its buffer back on blur; that write must not undo the change.
        if(_editingField=="atlas-search")BlurTextInput();
        _pendingTextWrites.Remove("atlas-search");
        _app.Map.Search=text??"";_atlasScroll.y=0;
        if(_app.Map.Search.Length==0)AtlasSearchClearsForTest++;
    }
    internal void SetAtlasSearchForTest(string text)=>SetAtlasSearch(text);
    internal void DrawMapOverlay()
    {
        var map=_app.Map;
        if(map==null)return;
        bool repaint=Event.current?.type==EventType.Repaint;
        // Retire the previous publication before any visibility or error exit.
        if(repaint)map.BeginMapOverlayFrame();
        if(!map.NativeMapHasInput||map.Ui._map==null||map.Ui._viewModel==null)
        {map.SuspendRaycastShields();return;}
        // Drag fix, IMGUI half: MouseDown/MouseDrag/MouseUp over the Atlas panel
        // or a card, and every event of a captured slider/scroll drag, are used
        // here, so no later OnGUI consumer acts on them; a press on the panel also
        // drops IMGUI keyboard focus through the guarded keyboardControl reset. UGUI never
        // sees IMGUI events: MapTools.TickNativeInputGate disables the native
        // pan/zoom and click-to-place handlers for the same gesture.
        var current=Event.current;
        if(current!=null)
        {
            var type=current.type;
            bool down=type==EventType.MouseDown,drag=type==EventType.MouseDrag,up=type==EventType.MouseUp;
            if((down||drag||up)&&MapInputGatePolicy.AbsorbImguiEvent(down,drag,up,map.PointerOverOverlayCard(),PointerCaptured))
            {
                if(down)DropImguiKeyboardFocus();
                current.Use();
                return;
            }
        }
        // Native pointer state drives buttons on Repaint; ScrollWheel still
        // reaches the capsule card. Layout/key events need no map projection,
        // marker layout, shields or label placement.
        if(Event.current==null||(Event.current.type!=EventType.Repaint&&Event.current.type!=EventType.ScrollWheel))return;
        Styles();
        Matrix4x4 original=GUI.matrix;
        Color old=GUI.color,oldContent=GUI.contentColor,oldBackground=GUI.backgroundColor;
        bool enabled=GUI.enabled;
        try
        {
            _nativeMapOverlayDrawing=true;
            GUI.color=GUI.contentColor=GUI.backgroundColor=Color.white;GUI.enabled=true;
            _scale=Mathf.Clamp(Screen.height/1080f,.6f,1.3f);_groupOrigin=Vector2.zero;_groupOrigins.Clear();_groupRects.Clear();
            GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(_scale,_scale,1));
            float sw=Screen.width/_scale,sh=Screen.height/_scale;
            // BeginMapOverlayFrame owns rect publication; ScrollWheel may draw
            // a card but must not mutate the last successful Repaint's lease.
            MapZoneLabelRects.Clear();
            // 0.8.1 (mockups 05/06): the atlas toolbar, the line under it and the «Слои карты»
            // drop-down replace the old 324 px panel. Geometry: MapChromePolicy. The line keeps
            // only what it drew on the previous frame, so markers left of the legend stay free.
            bool layersOpen=map.PanelExpanded;
            Rect toolbar=R(MapChromePolicy.Toolbar(sw));
            Rect lineFull=R(MapChromePolicy.Line(sw));
            Rect layersRect=layersOpen?R(MapChromePolicy.Layers(sw,sh)):default;
            float lineUsed=Mathf.Clamp(_mapLineUsed,0,lineFull.width);
            Rect lineRect=new(lineFull.xMax-lineUsed,lineFull.y,lineUsed,lineFull.height);
            var overlayChrome=layersOpen?new[]{toolbar,layersRect}:new[]{toolbar,lineRect};
            var overlayExclusions=new List<Rect>(overlayChrome.Length);
            foreach(var r in overlayChrome)if(r.width>0)overlayExclusions.Add(new Rect(r.x-8,r.y-8,r.width+16,r.height+16));
            // The card of the selected object; hovering only shows the plaque (mockup 05).
            bool hadCapsule=map.Selected!=null;
            var nativeChrome=new[]{new Rect(0,0,720,112),new Rect(0,120,260,100),new Rect(0,sh-400,116,320)};
            var cardChrome=new Rect[nativeChrome.Length+overlayChrome.Length];
            nativeChrome.CopyTo(cardChrome,0);overlayChrome.CopyTo(cardChrome,nativeChrome.Length);
            // With the drop-down closed the card has no panel to sit beside: an empty box at the
            // right edge puts the «beside» slot under the toolbar line.
            Rect cardPanel=layersOpen?layersRect:new Rect(sw,MapChromePolicy.Top,0,0);
            // M29: a provisional slot here, re-checked against the shown object's own marker
            // once the markers are laid out.
            var cardShown=map.Selected;
            Rect capsuleBounds=MapCardRect(sw,sh,cardPanel,cardChrome,cardShown,null,repaint&&map.ProjectionError>=20f);
            var nativeMarkerExclusions=new List<Rect>();
            if(repaint)map.NativeChromeRects.Clear();
            foreach(var chrome in nativeChrome)
            {
                nativeMarkerExclusions.Add(new Rect(chrome.x-28,chrome.y-28,chrome.width+56,chrome.height+56));
                if(repaint)map.NativeChromeRects.Add(new Rect(chrome.x*_scale,chrome.y*_scale,chrome.width*_scale,chrome.height*_scale));
            }
            // Publish actual native controls BEFORE any IMGUI click/scroll work.
            // Failed discovery disables mod actions, while native input stays free.
            map.PrepareNativeControlExclusions();
            _nativeMapPointerAllowed=Mouse.current!=null&&map.AllowsOverlayPointer(Mouse.current.position.ReadValue());
            var pagePoints=map.Filter(true);
            MapTools.Point hoverPoint=null;
            // Native projection is affine in world X/Z. Three native samples per
            // repaint avoid thousands of native calls for the resource layer.
            if(map.ProjectionError<20f)
            {
                Vector2 origin=map.ScreenPoint(Vector3.zero),dx=(map.ScreenPoint(Vector3.right*100)-origin)/100,dz=(map.ScreenPoint(Vector3.forward*100)-origin)/100;
                var projected=new List<(float X,float Y,MapTools.Point Item)>();
                foreach(var point in pagePoints)
                {
                    Vector2 pixel=origin+dx*point.Position.x+dz*point.Position.z;
                    Vector2 p=new(pixel.x/_scale,(Screen.height-pixel.y)/_scale);
                    if(p.x<28||p.x>sw-28||p.y<100||p.y>sh-100||AnyContains(overlayExclusions,p)||AnyContains(nativeMarkerExclusions,p))continue;
                    projected.Add((p.x,p.y,point));
                }
                // Every projected object keeps its own marker on its exact map
                // position: nothing is merged, clustered or displaced, and every
                // icon uses the same fixed player-chosen size.
                var markers=MapOverlayLayout.Place(projected,map.MarkerSize);
                MapProjectedForTest=projected.Count;MapMarkersForTest=markers.Count;
                MapCrowdedForTest=markers.Count(m=>m.Crowded);MapMarkerSizeForTest=map.MarkerSize;
                // Draw the selected marker last so it stays above its neighbours.
                int selectedMarker=markers.FindIndex(m=>m.Item==map.Selected);
                if(selectedMarker>=0&&selectedMarker!=markers.Count-1)
                {var top=markers[selectedMarker];markers[selectedMarker]=markers[markers.Count-1];markers[markers.Count-1]=top;}
                // M29: the card never covers the shown object's own marker.
                var shownMarker=cardShown==null?null:markers.Find(m=>m.Item==cardShown);
                capsuleBounds=MapCardRect(sw,sh,cardPanel,cardChrome,cardShown,shownMarker==null?(Rect?)null:
                    new Rect(shownMarker.X-shownMarker.HitBox*.5f,shownMarker.Y-shownMarker.HitBox*.5f,shownMarker.HitBox,shownMarker.HitBox),repaint);
                int ownDrawn=0,shownCovered=0;
                var markerBoxes=new List<Rect>();
                Vector2 hoverPos=default;MapTools.Point hoveredCapsule=null;
                foreach(var marker in markers)
                {
                    var point=marker.Item;
                    Vector2 p2=new(marker.X,marker.Y);
                    float icon=marker.Size,half=icon*.5f,box=marker.HitBox;
                    Rect hit=new(p2.x-box*.5f,p2.y-box*.5f,box,box);
                    markerBoxes.Add(new Rect(p2.x-half-1,p2.y-half-1,icon+2,icon+2));
                    bool covered=hadCapsule&&capsuleBounds.Overlaps(hit);
                    if(covered){if(point==cardShown)shownCovered++;continue;}
                    if(point.Category==CustomWaypointTools.Category)ownDrawn++;
                    if(repaint)map.MarkerHitRects.Add(new Rect(hit.x*_scale,hit.y*_scale,hit.width*_scale,hit.height*_scale));
                    // Markers are pure hit areas: a jelly button face would paint a
                    // square behind every icon. Hover and selection show as a ring
                    // around the icon itself, so one fixed icon size stays clean.
                    bool markerHover=CanHover(hit);
                    float markerOffset=HoverOffset(hit,markerHover,
                        markerHover&&Mouse.current!=null&&Mouse.current.leftButton.isPressed);
                    Rect iconRect=HoverChildRect(hit,new Rect(p2.x-half,p2.y-half,icon,icon),markerOffset);
                    if(_badgeCircle!=null&&(map.Selected==point||markerHover))
                    {
                        var tint=GUI.color;
                        GUI.color=map.Selected==point?new Color(1f,.84f,.36f,.9f):new Color(1f,1f,1f,.45f);
                        GUI.DrawTexture(new Rect(iconRect.x-3,iconRect.y-3,iconRect.width+6,iconRect.height+6),_badgeCircle);
                        GUI.color=tint;
                    }
                    if(Pressed(hit))map.Selected=point;
                    // Pods, map nodes and research/phantom drones draw the mod's own
                    // Fandom-style badges; every other layer keeps borrowed native art.
                    int canonMarker=CanonMapIconPolicy.Cell(point.Category,point.Id,point.Name);
                    bool canonDrawn=CanonIcon(iconRect,canonMarker);
                    var nativeIcon=map.Icons.Point(point);
                    if(canonDrawn){/* canon badge already drawn */}
                    else if(nativeIcon!=null)DrawSprite(iconRect,nativeIcon);
                    else if(!FandomIcon(iconRect,FandomMapMarkers.IconFor(point)))
                    {
                        // No object may be invisible: category artwork is the last
                        // honest fallback before the neutral atlas glyph.
                        var fallback=map.Icons.Category(point.Category)??map.Icons.Role("Atlas");
                        if(fallback!=null)DrawSprite(iconRect,fallback);
                    }
                    if(point.Category==CustomWaypointTools.Category)
                        Fill(new Rect(iconRect.x+1,iconRect.y,Mathf.Max(2,icon*.12f),icon),MarkerColor(point.ColorIndex));
                    if(hit.Contains(Pointer()))
                    {
                        hoverPoint=point;hoverPos=p2;
                        if(point.Category=="Капсулы")hoveredCapsule=point;
                    }
                }
                if(repaint){MapOwnDrawnForTest=ownDrawn;MapShownCoveredForTest=shownCovered;}
                // Place labels in nearby free space, not underneath the marker layer.
                var labelBoxes=new List<Rect>();
                foreach(var zone in map.ZoneLabels())
                {
                    Vector2 pixel=origin+dx*zone.Position.x+dz*zone.Position.z;
                    Vector2 at=new(pixel.x/_scale,(Screen.height-pixel.y)/_scale);
                    if(at.x<40||at.x>sw-40||at.y<80||at.y>sh-100)continue;
                    bool placed=false;
                    for(int ring=0;ring<=3&&!placed;ring++)
                    {
                        for(int zoneSide=0;zoneSide<(ring==0?1:2);zoneSide++)
                        {
                            int offset=ring==0?0:zoneSide==0?-ring:ring;
                            foreach(int horizontal in ZoneLabelColumns)
                            {
                                Rect label=new(Mathf.Clamp(at.x-145+horizontal*156,16,sw-306),at.y-24+offset*56,290,48);
                                if(label.y<80||label.yMax>sh-100||AnyOverlaps(overlayChrome,label)||hadCapsule&&capsuleBounds.Overlaps(label)||AnyOverlaps(nativeChrome,label)||AnyOverlaps(labelBoxes,label)||AnyOverlaps(markerBoxes,label))continue;
                                L.Label(label,zone.Name,_zoneLabel);labelBoxes.Add(label);
                                MapZoneLabelRects.Add(new Rect(label.x*_scale,label.y*_scale,label.width*_scale,label.height*_scale));
                                placed=true;break;
                            }
                            if(placed)break;
                        }
                    }
                }
                // The plaque is always drawn (an unknown player position is stated, not dropped)
                // and moves clear of the selected card, the toolbar and the native chrome.
                if(hoverPoint!=null)
                {
                    MapTipModel(hoverPoint);
                    _mapTipObstacles.Clear();
                    if(hadCapsule)_mapTipObstacles.Add(new MapShieldPolicy.Box(capsuleBounds.x,capsuleBounds.y,capsuleBounds.width,capsuleBounds.height));
                    foreach(var r in overlayChrome)_mapTipObstacles.Add(new MapShieldPolicy.Box(r.x,r.y,r.width,r.height));
                    foreach(var r in nativeChrome)_mapTipObstacles.Add(new MapShieldPolicy.Box(r.x,r.y,r.width,r.height));
                    Rect tip=R(MapChromePolicy.TipPlace(hoverPos.x,hoverPos.y,_mapTipWidth,MapChromePolicy.TipHeight,sw,sh,_mapTipObstacles));
                    MapTipDraw(tip,hoverPoint);
                }
                _hoverCapsule=hoveredCapsule;
            }
            if(map.HideOpened&&map.Selected?.Category=="Капсулы"&&map.Opened(map.Selected))map.Selected=null;
            var capsule=map.Selected?.Category=="Капсулы"?map.Selected:null;
            var custom=map.Selected?.Category==CustomWaypointTools.Category?map.Selected:null;
            if(map.Selected!=null)
            {
                if(repaint)map.OverlayCardRects.Add(new Rect(capsuleBounds.x*_scale,capsuleBounds.y*_scale,capsuleBounds.width*_scale,capsuleBounds.height*_scale));
                if(custom!=null)CustomMapCard(capsuleBounds,custom);
                else if(capsule!=null)CapsuleCard(capsuleBounds,capsule,false);
                else MapPointCard(capsuleBounds,map.Selected);
            }
            // Toolbar and its line or drop-down draw last, above markers and cards; every drawn
            // rectangle is a raycast shield (no click-through teleports or native waypoints).
            _mapRoundTip=-1;
            MapToolbar(toolbar,map);
            if(repaint)map.OverlayCardRects.Add(new Rect(toolbar.x*_scale,toolbar.y*_scale,toolbar.width*_scale,toolbar.height*_scale));
            if(layersOpen)
            {
                MapLayersPanel(layersRect,map);
                if(repaint)map.OverlayCardRects.Add(new Rect(layersRect.x*_scale,layersRect.y*_scale,layersRect.width*_scale,layersRect.height*_scale));
            }
            else
            {
                int shown=0;
                foreach(var point in pagePoints)if(!map.PinnedOnly(point))shown++;
                Rect used=MapLine(lineFull,map,shown,repaint);
                if(repaint)
                {
                    _mapLineUsed=used.width;
                    if(used.width>0)map.OverlayCardRects.Add(new Rect(used.x*_scale,used.y*_scale,used.width*_scale,used.height*_scale));
                }
            }
            // Publish only a complete render; never publish partial rectangles
            // from finally after a drawing exception.
            if(repaint)map.SyncRaycastShields();
        }
        catch
        {
            map.SuspendRaycastShields();
            throw;
        }
        finally{_nativeMapOverlayDrawing=_nativeMapPointerAllowed=false;GUI.matrix=original;GUI.enabled=enabled;GUI.color=old;GUI.contentColor=oldContent;GUI.backgroundColor=oldBackground;}
    }

    // The dialog grows with its text (M27 live test: the autofeed description
    // overflowed the fixed 170 px label); the height is measured once per text.
    private string _confirmText;
    private int _confirmFont = -1;
    private float _confirmTextHeight;
    // 0.8.5: the backdrop fades in and the dialog pops from 94 %; its buttons wait until it settles.
    private object _confirmShown;private float _confirmSince=-10f;
    private void DrawConfirmation(float width, float height)
    {
        if(!ReferenceEquals(_confirmShown,_app.Pending)){_confirmShown=_app.Pending;_confirmSince=Time.unscaledTime;}
        float since=Time.unscaledTime-_confirmSince;
        Fill(new Rect(0, 0, width, height), new Color(0, 0, 0, 0.8f*UiMotionPolicy.Fade(since,UiMotionPolicy.DialogSeconds)));
        string description = _app.Pending.Description ?? "";
        if (!ReferenceEquals(description, _confirmText) || _confirmFont != _wrap.fontSize)
        {
            _confirmText = description; _confirmFont = _wrap.fontSize;
            _confirmTextHeight = _wrap.CalcHeight(new GUIContent(Loc.T(description)), 652);
        }
        float dialogHeight = Mathf.Clamp(_confirmTextHeight + 200, 360, Mathf.Max(360, height - 40));
        Rect dialog = new((width - 700) / 2, (height - dialogHeight) / 2, 700, dialogHeight);
        float pop=UiMotionPolicy.DialogPop(since);
        Matrix4x4 beforePop=GUI.matrix;bool wasBlocked=_bodyInteractionBlocked;
        try
        {
        if(pop!=1f){GUIUtility.ScaleAroundPivot(new Vector2(pop,pop),dialog.center);_bodyInteractionBlocked=true;}
        L.Box(dialog, GUIContent.none, _card);
        L.Label(new Rect(dialog.x + 24, dialog.y + 20, dialog.width - 48, 58), _app.Pending.Title, _heading);
        L.Label(new Rect(dialog.x + 24, dialog.y + 90, dialog.width - 48, dialogHeight - 190), description, _wrap);
        if (Button(new Rect(dialog.x + 24, dialog.yMax - 74, 315, 48), "Подтвердить", _accentButton)) _app.AcceptConfirmation();
        if (Button(new Rect(dialog.x + 360, dialog.yMax - 74, 315, 48), "Отмена", _button)) _app.Pending = null;
        }
        finally{GUI.matrix=beforePop;_bodyInteractionBlocked=wasBlocked;}
    }
}
