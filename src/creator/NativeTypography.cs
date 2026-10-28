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
        _font=candidate;_ownsFont=false;_fontDisplayName=candidate.name;
        _fontSource="native-loaded Font";_fontCyrillic=true;
        _fontRoundRank=UiThemePolicy.NativeFontRank(candidate.name,candidate.dynamic);
        GUIStyle[] styles={_label,_muted,_heading,_title,_button,_accentButton,_selectedButton,
            _field,_card,_tabStyle,_wrap,_badge,_zoneLabel,_hudFrame,_hudLabel,_hudEffects,
            _atlasDetail,_atlasHelp,_small,_iconTab,_iconTabCompact,_iconTabSmall,
            _goldButton,_violetButton,_shadowBox,_glowBox,_trackStyle,_fillStyle,_tableHeading,
            _buttonText17,_buttonText16,_buttonText15,_buttonText14,_selectedRowText,_frame};
        foreach(var style in styles)if(style!=null)style.font=candidate;
        foreach(var style in _hoverStyles.Values)style.font=candidate;
        foreach(var style in _pressedStyles.Values)style.font=candidate;
        foreach(var style in _textShadows.Values)style.font=candidate;
        if(_titleGradient!=null)foreach(var style in _titleGradient)style.font=candidate;
        if(owned&&previous!=null&&previous!=candidate&&!previous.WasCollected)UnityEngine.Object.Destroy(previous);
        if(candidate.dynamic)foreach(int size in new[]{14,15,16,17,18,20,26,30})
            candidate.RequestCharactersInTexture(UiThemePolicy.CyrillicProbe,size,FontStyle.Bold);
    }
}