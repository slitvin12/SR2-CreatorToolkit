using UnityEngine;
namespace CreatorToolkit;
internal sealed partial class ToolkitUi
{
    private bool _timePage;
    private void CinematicRatePage(Rect area)
    {
        var tools=_app.CinematicRate;
        GUI.Label(new Rect(0,0,area.width-220,38),"Кинематограф · скорость времени",_heading);
        if(Button(new Rect(area.width-200,0,200,40),"Назад",_button)){_timePage=false;return;}
        GUI.Box(new Rect(0,76,area.width,340),GUIContent.none,_card);
        GUI.Label(new Rect(24,100,area.width-48,62),"Замедление для кадров и ускорение симуляции. Выключено по умолчанию; это Unity TimeScale, не скрытая способность разработчиков.",_muted);
        float w=(area.width-80)/3;float[] rates={.25f,1,5};string[] names={"×0.25 · Slow motion","×1 · Восстановить","×5 · Ускорить…"};
        for(int i=0;i<3;i++)
        {
            float rate=rates[i];
            if(Button(new Rect(24+i*(w+16),190,w,52),names[i],tools.Rate==rate?_selectedButton:_button))
                _app.Queue("cinematic time rate",()=>tools.Request(rate));
        }
        GUI.Label(new Rect(24,270,area.width-48,108),"Пауза игры имеет приоритет. При нативном меню, карте, загрузке или смене сейва режим отключается и сам не возобновляется. ×5 повышает нагрузку и влияет на игровой прогресс. fixedDeltaTime не изменяется.",_muted);
        GUI.Label(new Rect(0,444,area.width,72),tools.Status,_tableHeading);
    }
}
