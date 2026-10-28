using UnityEngine;
namespace CreatorToolkit;
internal sealed partial class ToolkitUi
{
    private static readonly int[] GraphicsLodPresets={100,75,50,25,10};
    private static readonly int[] GraphicsDistancePresets={0,90,180,300,600};
    private static readonly string[] GraphicsDistanceLabels={"Игра","90 м","180 м","300 м","600 м"};
    private static readonly string[] GraphicsLodLabels={"Игра","75%","50%","25%","10%"};
    private static readonly string[] GraphicsEffectLabels={"SSAO · объёмные тени","SSR · отражения","SSGI · непрямой свет","Объёмные облака","Контактные тени","Микротени","Размытие движения","Глубина резкости","Bloom · свечение"};
    private static readonly M19Effects[] GraphicsEffectBits={M19Effects.AO,M19Effects.Reflections,M19Effects.IndirectLight,M19Effects.Clouds,M19Effects.ContactShadows,M19Effects.MicroShadows,M19Effects.MotionBlur,M19Effects.DepthOfField,M19Effects.Bloom};
    private void GraphicsGeometry(Rect area,bool geometrySupported)
    {
        float col=(area.width-24)/2,x=col+24;
        GUI.Box(new Rect(0,116,col,200),GUIContent.none,_card);
        GUI.Label(new Rect(20,128,col-40,34),"Детализация LOD · "+_renderDraft.LodPercent+"%",_tableHeading);
        GUI.Label(new Rect(20,168,col-40,26),"Доля исходного LOD · ниже = проще вдали",_small);
        bool geometryEnabled=GUI.enabled;GUI.enabled=geometryEnabled&&geometrySupported;
        int lod=OptimizationPolicy.Rounded(Slider(74,new Rect(20,204,col-40,32),_renderDraft.LodPercent,10,100),_renderDraft.LodPercent,10,100);
        if(lod!=_renderDraft.LodPercent){_renderDraft=_renderDraft with{LodPercent=lod};_renderChanged=true;}
        float pw=(col-40-32)/5;
        for(int i=0;i<5;i++)if(Button(new Rect(20+i*(pw+8),256,pw,42),GraphicsLodLabels[i],_renderDraft.LodPercent==GraphicsLodPresets[i]?_selectedButton:_button)){_renderDraft=_renderDraft with{LodPercent=GraphicsLodPresets[i]};_renderChanged=true;}
        GUI.enabled=geometryEnabled;
        GUI.Box(new Rect(x,116,col,200),GUIContent.none,_card);
        GUI.Label(new Rect(x+20,128,col-40,34),_renderDraft.Distance>0?"Дальность · "+_renderDraft.Distance+" м":"Дальность · настройка игры",_tableHeading);
        GUI.Label(new Rect(x+20,168,col-40,26),"40–1000 м · не выше исходной дальности",_small);
        int distance=OptimizationPolicy.Rounded(Slider(75,new Rect(x+20,204,col-40,32),_renderDraft.Distance>0?_renderDraft.Distance:1000,40,1000),_renderDraft.Distance,40,1000);
        if(distance!=(_renderDraft.Distance>0?_renderDraft.Distance:1000)){_renderDraft=_renderDraft with{Distance=distance};_renderChanged=true;}
        for(int i=0;i<5;i++)if(Button(new Rect(x+20+i*(pw+8),256,pw,42),GraphicsDistanceLabels[i],_renderDraft.Distance==GraphicsDistancePresets[i]?_selectedButton:_button)){_renderDraft=_renderDraft with{Distance=GraphicsDistancePresets[i]};_renderChanged=true;}
        GUI.Box(new Rect(0,334,col,118),GUIContent.none,_card);
        GUI.Label(new Rect(20,346,col-174,54),"Инстанс-декор",_tableHeading);
        if(Button(new Rect(col-142,350,122,42),_renderDraft.HideDecor?"ВЫКЛ":"Игра",_renderDraft.HideDecor?_selectedButton:_button)){_renderDraft=_renderDraft with{HideDecor=!_renderDraft.HideDecor};_renderChanged=true;}
        GUI.Label(new Rect(20,402,col-40,40),"Только отрисовка декора; предметы и коллизии сохранены",_small);
        GUI.Box(new Rect(x,334,col,118),GUIContent.none,_card);
        GUI.Label(new Rect(x+20,346,col-174,54),"Тени источников",_tableHeading);
        if(Button(new Rect(x+col-142,350,122,42),_renderDraft.DisableShadows?"ВЫКЛ":"Игра",_renderDraft.DisableShadows?_selectedButton:_button)){_renderDraft=_renderDraft with{DisableShadows=!_renderDraft.DisableShadows};_renderChanged=true;}
        GUI.Label(new Rect(x+20,402,col-40,40),"Контактные и микротени настраиваются отдельно",_small);
    }
    private void GraphicsEffects(Rect area)
    {
        float col=(area.width-24)/3;
        GUI.Label(new Rect(0,112,area.width,28),"«Игра» = исходное состояние. «ВЫКЛ» = отключить только этот эффект.",_small);
        for(int i=0;i<GraphicsEffectBits.Length;i++)
        {
            float x=(i%3)*(col+12),y=154+(i/3)*94;var bit=GraphicsEffectBits[i];bool off=(_renderDraft.DisabledEffects&bit)!=0;
            GUI.Box(new Rect(x,y,col,82),GUIContent.none,_card);
            GUI.Label(new Rect(x+14,y+12,col-136,58),GraphicsEffectLabels[i],_atlasDetail);
            if(Button(new Rect(x+col-112,y+18,98,42),off?"ВЫКЛ":"Игра",off?_selectedButton:_button))
            {_renderDraft=_renderDraft with{DisabledEffects=off?_renderDraft.DisabledEffects&~bit:_renderDraft.DisabledEffects|bit};_renderChanged=true;}
        }
        GUI.Label(new Rect(0,442,area.width,28),"Если эффект отсутствует в текущей зоне, запрос сохранён до его появления. Небо, туман и экспозиция не меняются.",_small);
    }
}
