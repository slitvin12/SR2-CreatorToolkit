using UnityEngine;
namespace CreatorToolkit;
internal sealed partial class ToolkitUi
{
    private void GraphicsBasics(Rect area)
    {
        var tools=_app.Optimization;float col=(area.width-24)/2,x=col+24;
        L.Box(new Rect(0,116,col,172),GUIContent.none,_card);
        L.Label(new Rect(20,128,col-40,34),"Текстуры · "+OptimizationPolicy.MipLabel(_optDraft.Mip),_tableHeading);
        bool enabled=GUI.enabled;GUI.enabled=enabled&&tools.MipSupported;
        int mip=OptimizationPolicy.Rounded(Slider(70,new Rect(20,170,col-40,32),_optDraft.Mip,0,4),_optDraft.Mip,0,4);
        if(mip!=_optDraft.Mip){_optDraft=_optDraft with{Mip=mip};_optDraftChanged=true;}
        float mw=(col-40-32)/5;
        for(int i=0;i<5;i++)if(Button(new Rect(20+i*(mw+8),226,mw,42),MipNames[i],mip==i?_selectedButton:_button)){_optDraft=_optDraft with{Mip=i};_optDraftChanged=true;}
        GUI.enabled=enabled;
        L.Box(new Rect(x,116,col,172),GUIContent.none,_card);
        L.Label(new Rect(x+20,128,col-40,34),"Частицы декора · "+_optDraft.ParticlePercent+"%",_tableHeading);
        GUI.enabled=enabled&&tools.ParticlesSupported;
        int particles=OptimizationPolicy.Rounded(Slider(73,new Rect(x+20,174,col-40,32),_optDraft.ParticlePercent,10,100),_optDraft.ParticlePercent,10,100);
        if(particles!=_optDraft.ParticlePercent){_optDraft=_optDraft with{ParticlePercent=particles};_optDraftChanged=true;}
        GUI.enabled=enabled;
        L.Label(new Rect(x+20,230,col-40,44),tools.ParticlesSupported?"Только декоративные; опасные эффекты сохранены":"Недоступно в этой сборке игры",_small);
        L.Box(new Rect(0,306,col,146),GUIContent.none,_card);
        L.Label(new Rect(20,318,col-40,34),"Фоновая загрузка",_tableHeading);
        Hint(new Rect(20,358,col-40,28),"Щадящие бюджеты работы за кадр",_small);
        if(Button(new Rect(20,396,col-40,40),_optDraft.Smooth?"Щадящие бюджеты: ВКЛ":"Щадящие бюджеты: ВЫКЛ",_optDraft.Smooth?_selectedButton:_button)){_optDraft=_optDraft with{Smooth=!_optDraft.Smooth};_optDraftChanged=true;}
        L.Box(new Rect(x,306,col,146),GUIContent.none,_card);
        L.Label(new Rect(x+20,318,col-40,34),"Защита изображения",_tableHeading);
        Hint(new Rect(x+20,360,col-40,76),"Интерфейс и шрифты не размываются.\nМасштаб рендера и запись HDRP-структур отключены.\nТекстуры и частицы — после отпускания.",_small);
    }
}
