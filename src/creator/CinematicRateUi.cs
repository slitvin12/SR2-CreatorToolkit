using UnityEngine;
namespace CreatorToolkit;
internal sealed partial class ToolkitUi
{
    private bool _timePage;
    // 0.8.7: log-scale slider ×0.05…×5 plus presets; while dragging the rate is sent at most every 0.25 s.
    private const int RateSliderId=66;
    private static readonly float[] RatePresets={.1f,.25f,.5f,1f,2f,5f};
    private static readonly string[] RatePresetNames={"×0.1","×0.25","×0.5","×1","×2","×5"};
    private float _rateDraft=-1f,_rateDraftShown=-1f,_rateSentAt,_badgeRate=-1f;
    private bool _rateDirty;
    private string _rateDraftText,_badgeText;
    private void CinematicRatePage(Rect area)
    {
        var tools=_app.CinematicRate;
        if(_rateDraft<=0f||!_rateDirty&&_dragSlider!=RateSliderId)_rateDraft=tools.Shown;
        L.Label(new Rect(0,0,area.width-220,38),"Кинематограф · скорость времени",_heading);
        if(Button(new Rect(area.width-200,0,200,40),"Назад",_button)){SendRate(tools,false);_timePage=false;return;}
        L.Box(new Rect(0,56,area.width,268),GUIContent.none,_card);
        L.Label(new Rect(24,70,area.width-300,34),"Скорость симуляции",_label);
        if(_rateDraftText==null||!M26RatePolicy.Matches(_rateDraftShown,_rateDraft)){_rateDraftShown=_rateDraft;_rateDraftText="×"+M26RatePolicy.Label(_rateDraft);}
        L.Label(new Rect(area.width-244,64,220,42),_rateDraftText,_heading);
        float slid=M26RatePolicy.Snap(M26RatePolicy.FromSlider(Slider(RateSliderId,new Rect(24,114,area.width-48,32),M26RatePolicy.ToSlider(_rateDraft),0f,1f)));
        if(!M26RatePolicy.Matches(slid,_rateDraft)){_rateDraft=slid;_rateDirty=true;}
        if(_rateDirty&&(_dragSlider!=RateSliderId||Time.unscaledTime-_rateSentAt>.25f))SendRate(tools,false);
        float pw=(area.width-48-10f*(RatePresets.Length-1))/RatePresets.Length;
        for(int i=0;i<RatePresets.Length;i++)
        {
            float rate=RatePresets[i];
            if(Button(new Rect(24+i*(pw+10),162,pw,46),RatePresetNames[i],M26RatePolicy.Matches(tools.Shown,rate)?_selectedButton:_button))
            {_rateDraft=rate;_rateDirty=true;SendRate(tools,true);}
        }
        Hint(new Rect(24,222,area.width-48,92),"Ниже ×1 — замедление для кадров, выше — ускорение симуляции (спросит подтверждение). Это Unity TimeScale, не скрытая способность разработчиков; пауза игры всегда главнее.",_muted);
        bool pinned=Toggle(new Rect(0,338,area.width,64),"Закрепить скорость",tools.Pinned,"Не сбрасывается после Esc и паузы, карты, загрузки и смены сейва — возвращается сама. Запоминается между запусками.");
        if(pinned!=tools.Pinned)_app.Queue("cinematic time pin",()=>tools.SetPinned(pinned));
        bool smooth=Toggle(new Rect(0,414,area.width,64),"Плавная физика замедления",tools.SmoothPhysics,"Шаг физики уменьшается вместе со скоростью: слаймы, и из Classic Slimes тоже, движутся без рывков.");
        if(smooth!=tools.SmoothPhysics)_app.Queue("cinematic smooth step",()=>tools.SetSmoothPhysics(smooth));
        L.Label(new Rect(0,494,area.width,60),tools.Status,_tableHeading);
    }
    private void SendRate(CinematicRateTools tools,bool loud)
    {
        if(!_rateDirty)return;
        _rateDirty=false;_rateSentAt=Time.unscaledTime;
        float rate=_rateDraft;
        _app.Queue("cinematic time rate",()=>tools.Request(rate,!loud));
    }
    // 0.8.7: a small reminder while a non-ordinary rate runs with the menu closed (never in a clean frame).
    internal void DrawTimeRateBadge()
    {
        var tools=_app.CinematicRate;
        if(tools==null||!tools.Active||_app.Cinema?.HideHud==true||_app.Map?.Busy==true||Event.current?.type!=EventType.Repaint)return;
        if(_badgeText==null||!M26RatePolicy.Matches(_badgeRate,tools.Rate)){_badgeRate=tools.Rate;_badgeText="×"+M26RatePolicy.Label(tools.Rate);}
        Styles();
        var matrix=GUI.matrix;var color=GUI.color;
        try
        {
            _scale=1;_groupOrigin=Vector2.zero;_groupOrigins.Clear();_groupRects.Clear();
            GUI.matrix=Matrix4x4.identity;GUI.color=Color.white;
            float width=tools.Pinned?250f:120f;Rect rect=new((Screen.width-width)/2,10,width,40);
            L.Box(rect,GUIContent.none,_hudFrame);
            L.Label(new Rect(rect.x+14,rect.y+4,96,32),_badgeText,_label);
            if(tools.Pinned)L.Label(new Rect(rect.x+104,rect.y+6,width-118,30),"закреплено",_muted);
        }
        finally{GUI.matrix=matrix;GUI.color=color;}
    }
}
