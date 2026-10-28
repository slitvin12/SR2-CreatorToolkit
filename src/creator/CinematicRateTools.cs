using System;
using UnityEngine;
namespace CreatorToolkit;
// Explicit Unity TimeScale tool, not a claim of an unused developer prototype.
// Does not patch TimeDirector, saved worldTime, fixedDeltaTime, abilities or collision.
internal sealed class CinematicRateTools
{
    private readonly ToolkitMod _app;
    private M25ScalarLease<float> _lease;
    internal float Rate {get;private set;}=1;
    internal bool Active=>_lease?.Owned==true;
    internal string Status {get;private set;}="Скорость времени: штатная";
    internal int SavedScaleRepairs {get;private set;}
    internal string PauseDiagnostics {get;private set;}="none";
    internal CinematicRateTools(ToolkitMod app)=>_app=app;
    internal void Request(float rate)
    {
        if(!M26RatePolicy.Allowed(rate))throw new ArgumentOutOfRangeException(nameof(rate));
        if(rate==5)
            _app.Confirm("Ускорить симуляцию в 5 раз?","Ускоряются физика и игровые события. Это не оптимизация FPS. Прогресс мира может сохраниться; тестируйте на копии сейва. Нативная пауза или загрузка отключает режим.",()=>Apply(rate));
        else Apply(rate);
    }
    private void Apply(float rate)
    {
        if(!M26RatePolicy.Allowed(rate))throw new ArgumentOutOfRangeException(nameof(rate));
        if(rate==1){Reset();_app.Notify(Status);return;}
        if(!_app.Ready||_app.Map.Busy||_app.Map.NativeMapOpen||NativeUiPriority.HasNativeFocus||_app.Audit.MainAutosaveHeld||_app.Benchmark.Active||_app.Context.TimeDirector.IsFastForwarding())
            throw new InvalidOperationException("Завершите загрузку, нативное меню, ускоренную перемотку или замер FPS.");
        if(Active&&!M26RatePolicy.Matches(Time.timeScale,Rate))throw new InvalidOperationException("Скорость уже изменена игрой; сначала восстановите режим.");
        Reset();
        if(!M26RatePolicy.Matches(Time.timeScale,1))throw new InvalidOperationException("Игра не в обычном темпе. Пауза и сторонние изменения не переопределяются.");
        _lease=new M25ScalarLease<float>(()=>Time.timeScale,v=>Time.timeScale=v,rate);
        try{_lease.Apply();Rate=rate;Status="Скорость симуляции ×"+rate.ToString("0.##")+" · временно";}
        catch{_lease.Restore();throw;}
        _app.Notify(Status);
    }
    internal void Tick()
    {
        if(!Active)return;
        if(!_app.Ready||_app.Map.Busy||_app.Map.NativeMapOpen||NativeUiPriority.HasNativeFocus||_app.Context.TimeDirector.IsFastForwarding()||!M26RatePolicy.Matches(Time.timeScale,Rate))
        {Reset();Status="Режим времени отключён: приоритет нативной паузы, загрузки или внешнего изменения";}
    }
    internal void Reset()
    {
        RepairNativePauseMemory();_lease?.Restore();_lease=null;Rate=1;
        Status="Скорость времени: штатная; внешняя пауза не снимается";
    }
    // A native pause that started while our rate was active remembered that rate in
    // TimeDirector._savedTimeScale, so its own resume would silently bring back x0.25/x5
    // after our lease released ownership. Only the remembered value is corrected; the
    // current (paused) Unity scale is left untouched, so the pause is never lifted here.
    private void RepairNativePauseMemory()
    {
        if(_lease?.Owned!=true)return;
        try
        {
            var director=_app.Context?.TimeDirector;
            if(director==null||director.WasCollected)return;
            float saved=director._savedTimeScale,current=Time.timeScale;
            if(!M26RatePolicy.ShouldRepairSavedScale(current,Rate,director.isPaused(),saved))return;
            director._savedTimeScale=_lease.Before;
            SavedScaleRepairs++;
            PauseDiagnostics=$"native pause remembered {saved:0.###}; replaced with {director._savedTimeScale:0.###}; current {current:0.###}";
        }
        catch(Exception e){PauseDiagnostics="repair unavailable: "+e.GetType().Name;}
    }
}
