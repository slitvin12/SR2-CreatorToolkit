using UnityEngine;
namespace CreatorToolkit;
internal sealed partial class ToolkitUi
{
    private void GraphicsRuntime(Rect area)
    {
        float col=(area.width-24)/2,x=col+24;
        L.Box(new Rect(0,112,col,154),GUIContent.none,_card);
        L.Label(new Rect(16,120,col-32,30),"Тени · "+(_runtimeDraft.ShadowDistance==0?"Игра":_runtimeDraft.ShadowDistance+" м"),_tableHeading);
        int shadow=OptimizationPolicy.Rounded(Slider(76,new Rect(16,154,col-32,28),_runtimeDraft.ShadowDistance,0,500),0,0,500);
        if(shadow!=_runtimeDraft.ShadowDistance){_runtimeDraft=_runtimeDraft with{ShadowDistance=shadow};_renderChanged=true;}
        float sw=(col-48)/3;
        string[] labels={"Игра","Low","ВЫКЛ"};
        for(int i=0;i<3;i++)if(Button(new Rect(16+i*(sw+8),208,sw,40),labels[i],(int)_runtimeDraft.Shadows==i?_selectedButton:_button)){_runtimeDraft=_runtimeDraft with{Shadows=(M25ShadowMode)i};_renderChanged=true;}
        L.Box(new Rect(x,112,col,154),GUIContent.none,_card);
        L.Label(new Rect(x+16,120,col-32,30),"Мелкий декор / трава",_tableHeading);
        int distance=OptimizationPolicy.Rounded(Slider(77,new Rect(x+16,154,col-32,28),_runtimeDraft.DetailDistance,0,500),0,0,500);
        if(distance!=_runtimeDraft.DetailDistance){_runtimeDraft=_runtimeDraft with{DetailDistance=distance};_renderChanged=true;}
        L.Label(new Rect(x+16,202,col-32,48),distance==0?"Игра · LOD Bias — вкладка «Дальность и декор»":distance+" м · батчи декора и Terrain, не предметы/слаймы",_small);
        L.Box(new Rect(0,278,col,132),GUIContent.none,_card);
        L.Label(new Rect(16,286,col-32,28),"Particle Raycast Budget · "+(_runtimeDraft.Raycasts==0?"Игра":_runtimeDraft.Raycasts.ToString()),_tableHeading);
        int rays=OptimizationPolicy.Rounded(Slider(78,new Rect(16,324,col-32,28),_runtimeDraft.Raycasts,0,4096),0,0,4096);
        if(rays!=_runtimeDraft.Raycasts){_runtimeDraft=_runtimeDraft with{Raycasts=rays};_renderChanged=true;}
        Hint(new Rect(16,370,col-32,28),"0 = исходный бюджет · никогда не повышается",_small);
        L.Box(new Rect(x,278,col,132),GUIContent.none,_card);
        L.Label(new Rect(x+16,286,col-32,28),"Max Particles · "+(_optDraft.ParticleMax==0?"Игра":_optDraft.ParticleMax+" / система"),_tableHeading);
        bool enabled=GUI.enabled;GUI.enabled=enabled&&_app.Optimization.ParticlesSupported;
        int cap=OptimizationPolicy.Rounded(Slider(79,new Rect(x+16,324,col-32,28),_optDraft.ParticleMax,0,4096),0,0,4096);
        if(cap!=_optDraft.ParticleMax){_optDraft=_optDraft with{ParticleMax=cap};_renderChanged=true;}
        GUI.enabled=enabled;
        Hint(new Rect(x+16,370,col-32,28),"Только безопасные декоративные частицы, не VFX Graph",_small);
        if(Button(new Rect(0,424,250,40),_runtimeDraft.Sleep?"Сон слаймов: ВКЛ":"Сон слаймов: ВЫКЛ",_runtimeDraft.Sleep?_selectedButton:_button)){_runtimeDraft=_runtimeDraft with{Sleep=!_runtimeDraft.Sleep};_renderChanged=true;}
        Hint(new Rect(266,420,area.width-266,48),"Осторожный сон: почти неподвижные, за кадром, дальше 40 м.\nИИ/коллизии сохранены; видимые просыпаются. Меняет симуляцию · по умолчанию ВЫКЛ.",_small);
    }
}
