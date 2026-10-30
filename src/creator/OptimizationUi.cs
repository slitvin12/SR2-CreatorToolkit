using System;
using UnityEngine;
using UnityEngine.InputSystem;
namespace CreatorToolkit;
internal sealed partial class ToolkitUi
{
    private bool _optimizationPage,_optDraftReady,_optDraftChanged,_optQueued,_renderDraftReady,_renderChanged;
    private OptimizationRequest _optDraft=OptimizationRequest.Full;
    private M19GraphicsRequest _renderDraft=M19GraphicsRequest.Full;
    private M25RuntimeRequest _runtimeDraft=M25RuntimeRequest.Full;
    private int _optRevision=-1,_renderRevision=-1,_optSubpage;
    private static readonly string[] MipNames={"Полные","½","¼","⅛","1/16"};
    private void ResetOptimizationUi()
    {
        _optimizationPage=_optDraftReady=_optDraftChanged=_optQueued=_renderDraftReady=_renderChanged=false;
        _optDraft=OptimizationRequest.Full;_renderDraft=M19GraphicsRequest.Full;_optSubpage=0;_runtimeDraft=M25RuntimeRequest.Full;
    }
    private void OpenOptimizationUi(){ClearInteraction();_infoPage=false;_optimizationPage=true;_optDraftReady=_renderDraftReady=false;}
    internal void ShowOptimizationForTest(){ShowTabForTest(7);OpenOptimizationUi();}
    internal void ShowGraphicsTabForTest(int tab){ShowOptimizationForTest();_optSubpage=Math.Clamp(tab,0,3);}
    internal M19GraphicsRequest GraphicsDraftForTest=>_renderDraft;
    internal int GraphicsSubpageForTest=>_optSubpage;
    internal bool OptimizationVisibleForTest=>_app.MenuOpen&&_tab==7&&_optimizationPage;
    internal bool OptimizationDragForTest=>_dragSlider>=70&&_dragSlider<=79;
    internal bool CareHeadersFitForTest=>_tableHeading!=null&&!_tableHeading.wordWrap&&_tableHeading.CalcSize(ButtonContent("Голод / возбуждение")).x<=1100*.25f;
    private void QueueOptimization(OptimizationRequest request)
    {
        if(_optQueued)return;_optQueued=true;_optDraftChanged=false;
        _app.Queue("graphics optimization",()=>{try{_app.Optimization.Apply(request);}finally{_optQueued=false;_optDraftReady=false;}});
    }
    private void ApplyCustomGraphics()
    {
        if(_optQueued)return;var basic=_optDraft;var details=_renderDraft;var runtime=_runtimeDraft;
        _optQueued=true;_optDraftChanged=_renderChanged=false;
        _app.Queue("detailed graphics",()=>{try{_app.Optimization.Apply(basic);_app.Optimization.ApplyDetailed(details);_app.Optimization.ApplyRuntime(runtime);}finally{_optQueued=false;_optDraftReady=_renderDraftReady=false;}});
    }
    private void GraphicsPreset(int kind)
    {
        if(_optQueued)return;_optQueued=true;
        _app.Queue("graphics preset",()=>{try{var tools=_app.Optimization;if(kind==0)tools.ApplyPotato();else if(kind==1)tools.ApplyMaximum();else tools.Restore();}finally{_optQueued=false;_optDraftReady=_renderDraftReady=_optDraftChanged=_renderChanged=false;}});
    }
    private void OptimizationPage(Rect area)
    {
        BeginGroup(area);
        try
        {
            var tools=_app.Optimization;
            if(!_optDraftReady||(!_optDraftChanged&&!_optQueued&&_dragSlider<0&&_optRevision!=tools.UiRevision))
            {_optDraft=tools.ForUi();_optRevision=tools.UiRevision;_optDraftReady=true;_optDraftChanged=false;}
            if(!_renderDraftReady||(!_renderChanged&&!_optQueued&&_dragSlider<0&&_renderRevision!=tools.UiRevision))
            {_runtimeDraft=tools.Runtime.Request;_renderDraft=tools.DetailedSettings;_renderRevision=tools.UiRevision;_renderDraftReady=true;_renderChanged=false;}
            L.Label(new Rect(0,0,area.width-250,38),"Графика и производительность",_heading);
            if(Button(new Rect(area.width-226,0,226,42),"Назад",_button)){_optimizationPage=false;ClearInteraction();return;}
            float tw=(area.width-30)/4;
            if(Button(new Rect(0,56,tw,40),"Текстуры и загрузка",_optSubpage==0?_selectedButton:_button)){_optSubpage=0;ClearInteraction();}
            if(Button(new Rect(tw+10,56,tw,40),"Дальность и декор",_optSubpage==1?_selectedButton:_button)){_optSubpage=1;ClearInteraction();}
            if(Button(new Rect(2*(tw+10),56,tw,40),"Постэффекты",_optSubpage==2?_selectedButton:_button)){_optSubpage=2;ClearInteraction();}
            if(Button(new Rect(3*(tw+10),56,tw,40),"Runtime FPS",_optSubpage==3?_selectedButton:_button)){_optSubpage=3;ClearInteraction();}
            bool controlsEnabled=GUI.enabled;GUI.enabled=controlsEnabled&&!_optQueued;
            if(_optSubpage==0)GraphicsBasics(area);else if(_optSubpage==1)GraphicsGeometry(area,tools.GeometrySupported);else if(_optSubpage==2)GraphicsEffects(area);else GraphicsRuntime(area);
            GUI.enabled=controlsEnabled;
            float bw=(area.width-36)/4;
            GUI.enabled=controlsEnabled&&!_optQueued;
            if(Button(new Rect(0,478,bw,44),"Применить своё",_renderChanged||_optDraftChanged?_accentButton:_button))ApplyCustomGraphics();
            if(Button(new Rect(bw+12,478,bw,44),"Сбалансированный",_goldButton))GraphicsPreset(0);
            if(Button(new Rect(2*(bw+12),478,bw,44),"Максимум FPS",tools.MaximumPresetActive?_selectedButton:_button))GraphicsPreset(1);
            GUI.enabled=controlsEnabled&&!_optQueued&&tools.Active;
            if(Button(new Rect(3*(bw+12),478,bw,44),"Восстановить всё",_button))GraphicsPreset(2);
            GUI.enabled=controlsEnabled;
            L.Label(new Rect(0,538,area.width,42),_optQueued?"Применение…":_renderChanged?"Есть изменения · нажмите «Применить своё»":tools.Runtime.Active?tools.Runtime.Status:tools.MaximumActive?tools.MaximumStatus:"Настройки временные · «Игра» возвращает исходное состояние",_small);
            if(_optDraftChanged&&!_optQueued&&_dragSlider<0&&(Mouse.current==null||!Mouse.current.leftButton.isPressed)&&Event.current.type==EventType.Repaint)QueueOptimization(_optDraft);
        }
        finally{EndGroup();}
    }
}
