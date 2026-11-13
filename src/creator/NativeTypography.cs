using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2CppTMPro;
using UnityEngine;

namespace CreatorToolkit;

internal sealed partial class ToolkitUi
{
    internal bool NativePediaTypographyCheckedForTest {get;private set;}
    internal string NativePediaFontForTest {get;private set;}="";
    internal string NativePediaTypographyResultForTest {get;private set;}="Не проверено";

    internal void InspectNativePediaTypography(GameObject root)
    {
        _app.Audit.GuardStressCopy();
        if(root==null||!root.activeInHierarchy)throw new InvalidOperationException("Actual native Pedia root is not visible.");
        var rows=new List<object>();
        var votes=new Dictionary<IntPtr,(Font Font,int Count,string Asset)>();
        var seen=new HashSet<IntPtr>();
        void Observe(TMP_FontAsset asset,string origin,int depth)
        {
            if(asset==null||asset.WasCollected||depth>4||seen.Count>=48)return;
            bool first=seen.Add(asset.Pointer);
            Font source=asset.sourceFontFile;
            bool cyrillic=source!=null&&FontHasCyrillic(source);
            if(cyrillic)
            {
                var previous=votes.TryGetValue(source.Pointer,out var value)?value:(source,0,asset.name);
                votes[source.Pointer]=(source,previous.Item2+1,asset.name);
            }
            if(!first)return;
            bool ownCyrillic=true;
            foreach(char c in UiThemePolicy.CyrillicProbe)
                if(!char.IsWhiteSpace(c)&&!asset.HasCharacter(c,false,false)){ownCyrillic=false;break;}
            rows.Add(new{
                Origin=origin,TmpAsset=asset.name,SourceFont=source?.name??"",
                SourceHasCyrillic=cyrillic,TmpOwnProbeCoverage=ownCyrillic,
                SourceRounded=cyrillic&&UiThemePolicy.RoundedFace(source.name),
                AtlasTextures=asset.atlasTextures?.Length??0
            });
            var fallbacks=asset.fallbackFontAssetTable;
            if(fallbacks!=null)for(int i=0;i<fallbacks.Count&&i<16;i++)
                Observe(fallbacks[i],asset.name+" native fallback",depth+1);
        }
        var text=root.GetComponentsInChildren<TMP_Text>(true);
        int activeTexts=0;
        for(int i=0;i<text.Length&&activeTexts<128;i++)
            if(text[i]!=null&&text[i].gameObject.activeInHierarchy)
            {Observe(text[i].font,"active Pedia text "+text[i].name,0);activeTexts++;}
        _fontResourceScans++; // One explicit extra census on the owned copy.
        foreach(var asset in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            if(asset!=null&&!asset.WasCollected&&UiThemePolicy.RoundedFace(asset.name))
                Observe(asset,"loaded native rounded TMP asset",0);
        NativePediaTypographyCheckedForTest=seen.Count>0;
        var best=votes.Values.OrderByDescending(v=>UiThemePolicy.RoundedFace(v.Font.name))
            .ThenByDescending(v=>v.Count).ThenByDescending(v=>UiThemePolicy.NativeFontRank(v.Font.name,v.Font.dynamic))
            .FirstOrDefault();
        if(best.Font!=null)
        {
            NativePediaFontForTest=best.Asset;
            AdoptNativeFont(best.Font);
            NativePediaTypographyResultForTest="Подлинный источник Pedia: "+best.Asset+" → "+best.Font.name+
                (UiThemePolicy.RoundedFace(best.Font.name)?" · округлый":" · кириллическая локализация; не Mikado");
        }
        else NativePediaTypographyResultForTest=
            "TMP-шрифты Pedia прочитаны; пригодный Unity Font с кириллицей не экспортирован. Подмена не выполнялась.";
        File.WriteAllText(Path.Combine(_app.DataDirectory,"native-typography.json"),JsonSerializer.Serialize(new{
            Version=ToolkitMod.Version,Scope="Visible original Pedia and native TMP/Font resources on the owned copy",
            ActualPediaRoot=root.name,NativePediaAssetsRead=seen.Count,ActiveTextsObserved=activeTexts,
            SelectedUnityFont=_fontDisplayName,SelectedTmpFont=NativePediaFontForTest,
            NativeCyrillic=FontNativeForTest,RoundedCyrillic=FontRoundedForTest,
            Result=NativePediaTypographyResultForTest,Rows=rows
        },new JsonSerializerOptions{WriteIndented=true}));
    }
    private void AdoptNativeFont(Font candidate)
    {
        if(candidate==null||!FontHasCyrillic(candidate))return;
        var previous=_font;bool owned=_ownsFont;
        _font=_baseFont=candidate;_ownsFont=false;_fontDisplayName=candidate.name;
        _fontSource="native-loaded Font";_fontCyrillic=true;
        _fontRoundRank=UiThemePolicy.NativeFontRank(candidate.name,candidate.dynamic);
        GUIStyle[] styles={_label,_muted,_heading,_title,_button,_accentButton,_selectedButton,
            _field,_card,_tabStyle,_wrap,_badge,_zoneLabel,_hudFrame,_hudLabel,_hudEffects,
            _atlasDetail,_atlasHelp,_small,_iconTab,_iconTabCompact,_iconTabSmall,
            _goldButton,_violetButton,_shadowBox,_glowBox,_trackStyle,_fillStyle,_tableHeading,
            _buttonText17,_buttonText16,_buttonText15,_buttonText14,_selectedRowText,_frame};
        foreach(var style in styles)if(style!=null)style.font=candidate;
        foreach(var style in _textShadows.Values)style.font=candidate;
        if(_titleGradient!=null)foreach(var style in _titleGradient)style.font=candidate;
        if(owned&&previous!=null&&previous!=candidate&&!previous.WasCollected)UnityEngine.Object.Destroy(previous);
        if(candidate.dynamic)foreach(int size in new[]{14,15,16,17,18,20,26,30})
            candidate.RequestCharactersInTexture(UiThemePolicy.CyrillicProbe,size,FontStyle.Bold);
    }

    private Font _baseFont;
    private string _activeFontLang="";
    private readonly Dictionary<string,(Font Font,bool Owned)> _localeFonts=new(StringComparer.Ordinal);
    private void DisposeLocaleFonts(){foreach(var kv in _localeFonts.Values)if(kv.Owned&&kv.Font!=null&&!kv.Font.WasCollected)UnityEngine.Object.Destroy(kv.Font);_localeFonts.Clear();_baseFont=null;_activeFontLang="";}
    private void SyncLocaleFont(){
        if(_label==null||_font==null||_font.WasCollected)return;
        if(_baseFont==null||_baseFont.WasCollected)_baseFont=_font;
        string lang=Loc.Language??"ru";
        string group=(lang=="zh"||lang=="ja"||lang=="ko")?lang:"default";
        if(group==_activeFontLang)return;
        _activeFontLang=group;
        if(group=="default"){ApplyStyleFont(_baseFont);return;}
        char probe=lang switch{"zh"=>'\u53F2',"ja"=>'\u30B9',_=>'\uC2AC'};
        string sample=lang switch{"zh"=>"\u53F2\u83B1\u59C6\u7267\u573A\u5730\u56FE\u8BBE\u7F6E\u786E\u8BA4\u53D6\u6D88","ja"=>"\u30B9\u30E9\u30A4\u30E0\u30E9\u30F3\u30C1\u30E3\u30FC\u8A2D\u5B9A\u78BA\u8A8D\u30DE\u30C3\u30D7",_=>"\uC2AC\uB77C\uC784\uD53C\uB514\uC544\uC124\uC815\uD655\uC778\uCDE8\uC18C\uBBF8\uB2C8\uB9F5"};
        if(FontSupportsProbe(_baseFont,probe,sample)){ApplyStyleFont(_baseFont);return;}
        if(!_localeFonts.TryGetValue(lang,out var entry)||entry.Font==null||entry.Font.WasCollected){entry=ResolveCjkFont(lang,probe,sample);if(entry.Font!=null)_localeFonts[lang]=entry;}
        ApplyStyleFont(entry.Font??_baseFont);
    }
    private static bool FontSupportsProbe(Font font,char probe,string sample){if(font==null||font.WasCollected)return false;try{if(font.dynamic)font.RequestCharactersInTexture(sample,20,FontStyle.Bold);return font.HasCharacter(probe);}catch{return false;}}
    private (Font Font,bool Owned) ResolveCjkFont(string lang,char probe,string sample){
        try{foreach(var c in Resources.FindObjectsOfTypeAll<Font>())if(c!=null&&!c.WasCollected&&FontSupportsProbe(c,probe,sample))return (c,false);}catch{}
        string[] fams=lang switch{"ja"=>new[]{"Yu Gothic UI","Meiryo UI","Meiryo","MS UI Gothic","Microsoft YaHei UI"},"ko"=>new[]{"Malgun Gothic","Gulim","Dotum","Batang","Microsoft YaHei UI"},_=>new[]{"Microsoft YaHei UI","Microsoft YaHei","DengXian","SimHei","SimSun"}};
        var inst=new HashSet<string>(StringComparer.OrdinalIgnoreCase);try{foreach(string n in Font.GetOSInstalledFontNames())inst.Add(n);}catch{}
        foreach(string fam in fams){if(inst.Count>0&&!inst.Contains(fam))continue;Font df=null;try{df=Font.CreateDynamicFontFromOSFont(fam,20);if(FontSupportsProbe(df,probe,sample)){foreach(int sz in new[]{12,13,14,15,16,17,18,20,23,26,30}){df.RequestCharactersInTexture(sample,sz,FontStyle.Bold);df.RequestCharactersInTexture(sample,sz,FontStyle.Normal);}_app.LoggerInstance.Msg("Toolkit CJK font ("+lang+"): "+fam);return (df,true);}}catch{}if(df!=null&&!df.WasCollected)UnityEngine.Object.Destroy(df);}
        return (null,false);
    }
    private void ApplyStyleFont(Font target){
        if(target==null||target.WasCollected)return;
        GUIStyle[] styles={_label,_muted,_heading,_title,_button,_accentButton,_selectedButton,_field,_card,_tabStyle,_wrap,_badge,_zoneLabel,_hudFrame,_hudLabel,_hudEffects,_atlasDetail,_atlasHelp,_atlasChipHead,_atlasChip,_small,_iconTab,_iconTabCompact,_iconTabSmall,_goldButton,_violetButton,_shadowBox,_glowBox,_trackStyle,_fillStyle,_tableHeading,_buttonText17,_buttonText16,_buttonText15,_buttonText14,_selectedRowText,_frame,_atlasGroupText,_atlasChipTitle,_atlasRowTitle,_atlasRowMore,_atlasRowSub,_atlasCaps,_atlasRight,_atlasBadge,_atlasPill,_atlasCardTitle,_atlasCount,_mapBarTitle,_mapLegend,_mapTipTitle,_mapTipSub,_mapGroupHead,_mapGroupSub,_mapChipText,_mapSegment,_explorerLabel,_explorerRowLabel,_explorerTile,_explorerRightMuted,_explorerCenter,_explorerLink,_explorerPillText,_explorerPillTextWhite};
        foreach(var st in styles)if(st!=null)st.font=target;
        foreach(var st in _textShadows.Values)if(st!=null)st.font=target;
        if(_titleGradient!=null)foreach(var st in _titleGradient)if(st!=null)st.font=target;
        _tabWidthsFor=-1;_atlasWidthLanguage=-1;
    }
}
