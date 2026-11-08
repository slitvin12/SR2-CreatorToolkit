using System;
using System.Globalization;
using System.IO;
using UnityEngine;
namespace CreatorToolkit;
// Explicit Unity TimeScale tool, not a claim of an unused developer prototype.
// Does not patch TimeDirector, saved worldTime, abilities or collision.
// 0.8.7: any rate ×0.05…×5 (slider and presets). «Закрепить» keeps the chosen rate through
// Esc/pause, the map, loading and a save change: the native pause still wins (the lease is
// released exactly as before, the remembered pause scale repaired, never unpaused) and the rate
// comes back by itself once the game runs at its ordinary ×1 again.
// Classic Slimes: its rigs read the scaled Time/deltaTime/fixedTime and follow any rate. What
// stuttered was physics at the native 50 Hz step: ×0.25 left ~12 physics steps per real second,
// so rigidbody slimes jumped between poses. Slow motion now shortens fixedDeltaTime by the same
// factor under its own lease (restored exactly; acceleration keeps the native step). A writer
// that zeroes the scale and later writes our rate back (a save/restore capture) gets the rate
// re-owned instead of an orphaned slow motion that no reset would restore.
internal sealed class CinematicRateTools
{
    private const string AccelerationTitle="Разрешить ускорение симуляции?";
    private const string WaitFree="ждёт конца паузы, меню, карты или загрузки";
    private readonly ToolkitMod _app;
    private M25ScalarLease<float> _lease,_fixedLease;
    private IntPtr _accelerationSession;
    private float _orphan=1f,_orphanUntil,_retryAt;
    private bool _loaded;
    private string _waitReason,_savedText;
    internal float Rate {get;private set;}=1;
    internal float Target {get;private set;}=1;
    internal bool Pinned {get;private set;}
    internal bool SmoothPhysics {get;private set;}=true;
    internal bool Waiting {get;private set;}
    internal bool Active=>_lease?.Owned==true;
    internal bool SmoothActive=>_fixedLease?.Owned==true;
    // What the page shows: the running rate, else the pinned or pending target, else ×1.
    internal float Shown=>Active?Rate:Pinned||Waiting?Target:1f;
    internal bool AccelerationAllowed=>_accelerationSession!=IntPtr.Zero&&_accelerationSession==_app.Session;
    internal string Status {get;private set;}="Скорость времени: штатная";
    internal int SavedScaleRepairs {get;private set;}
    internal int Reapplies {get;private set;}
    internal int Adoptions {get;private set;}
    internal string PauseDiagnostics {get;private set;}="none";
    internal CinematicRateTools(ToolkitMod app)=>_app=app;
    internal void Request(float rate)=>Request(rate,false);
    // quiet: slider drags refresh the status line only, no notification per step.
    internal void Request(float rate,bool quiet)
    {
        if(!M26RatePolicy.Allowed(rate))throw new ArgumentOutOfRangeException(nameof(rate));
        EnsureLoaded();
        rate=M26RatePolicy.Snap(rate);
        Target=rate;Save();
        if(rate>1f&&!AccelerationAllowed)
        {
            Waiting=true;_waitReason=null;Status="Ускорение ×"+M26RatePolicy.Label(rate)+" ждёт подтверждения";
            if(_app.Pending?.Title!=AccelerationTitle)
                _app.Confirm("Разрешить ускорение симуляции?","Ускоряются физика и игровые события. Это не оптимизация FPS. Прогресс мира может сохраниться; тестируйте на копии сейва. Разрешение действует до смены сейва; пауза игры по-прежнему главнее.",
                    ()=>{_accelerationSession=_app.Session;Apply(Target>1f?Target:rate,false);});
            return;
        }
        Apply(rate,quiet);
    }
    internal void SetPinned(bool pinned)
    {
        EnsureLoaded();
        Pinned=pinned;_waitReason=null;
        if(!pinned&&!Active){Waiting=false;Target=1;Status="Скорость времени: штатная";}
        else if(Active)Status=ActiveStatus();
        Save();
        _app.Notify(pinned?"Скорость закреплена: вернётся сама после паузы, карты и загрузки":"Скорость больше не закреплена");
    }
    internal void SetSmoothPhysics(bool on)
    {
        EnsureLoaded();
        SmoothPhysics=on;Save();
        if(Active){LeaseFixedStep(Rate);Status=ActiveStatus();}
        _app.Notify(on?"Плавная физика замедления включена":"Плавная физика замедления выключена");
    }
    private void Apply(float rate,bool quiet)
    {
        if(!M26RatePolicy.Allowed(rate))throw new ArgumentOutOfRangeException(nameof(rate));
        Target=rate;_waitReason=null;
        if(M26RatePolicy.Matches(rate,1)){Reset();Waiting=false;if(!quiet)_app.Notify(Status);return;}
        string blocked=BlockReason(true);
        if(blocked==null&&Active&&!M26RatePolicy.Matches(Time.timeScale,Rate))blocked="Скорость уже изменена игрой; сначала восстановите режим.";
        if(blocked==null)
        {
            Reset();
            if(!M26RatePolicy.Matches(Time.timeScale,1))blocked="Игра не в обычном темпе. Пауза и сторонние изменения не переопределяются.";
        }
        if(blocked!=null)
        {
            if(!Pinned){Waiting=false;throw new InvalidOperationException(blocked);}
            Wait(WaitFree);if(!quiet)_app.Notify(Status);return;
        }
        Lease(rate,false);
        if(!quiet)_app.Notify(Status);
    }
    private void Lease(float rate,bool adopt)
    {
        var lease=adopt?new M25ScalarLease<float>(()=>Time.timeScale,v=>Time.timeScale=v,1f,rate)
                       :new M25ScalarLease<float>(()=>Time.timeScale,v=>Time.timeScale=v,rate);
        try{lease.Apply();}
        catch{lease.Restore();throw;}
        _lease=lease;Rate=rate;
        LeaseFixedStep(rate);
        Waiting=false;_waitReason=null;_orphanUntil=0f;
        Status=ActiveStatus();
    }
    // Slow motion only: the native step × rate, so physics keeps its real-time frequency.
    private void LeaseFixedStep(float rate)
    {
        try{_fixedLease?.Restore();}
        catch(Exception e){PauseDiagnostics="physics step restore: "+e.GetType().Name;}
        _fixedLease=null;
        if(!SmoothPhysics||rate>=1f)return;
        float native=Time.fixedDeltaTime,step=M26RatePolicy.FixedStep(native,rate);
        if(M26RatePolicy.Matches(step,native))return;
        var lease=new M25ScalarLease<float>(()=>Time.fixedDeltaTime,v=>Time.fixedDeltaTime=v,step);
        try{lease.Apply();_fixedLease=lease;}
        catch(Exception e){try{lease.Restore();}catch{}PauseDiagnostics="physics step unavailable: "+e.GetType().Name;}
    }
    private string ActiveStatus()=>"Скорость симуляции ×"+M26RatePolicy.Label(Rate)+(Pinned?" · закреплена":" · временно")+(SmoothActive?" · плавная физика":"");
    internal void Tick()
    {
        EnsureLoaded();
        bool wanted=Pinned&&!M26RatePolicy.Matches(Target,1);
        if(!Active)
        {
            // A dismissed acceleration prompt of an unpinned request: back to the ordinary tempo.
            if(Waiting&&!Pinned&&_app.Pending?.Title!=AccelerationTitle){Waiting=false;Target=1;Status="Скорость времени: штатная";}
            if(wanted||_orphanUntil>0f)TryResume(wanted);
            return;
        }
        string blocked=BlockReason(false);
        if(blocked==null&&M26RatePolicy.Matches(Time.timeScale,Rate))return;
        // The native pause/menu, the map, loading or another writer owns the clock now: release
        // exactly as before (remembered pause scale repaired, never unpaused).
        float was=Rate;
        Reset();
        if(wanted){Wait(blocked!=null?WaitFree:"ждёт обычного темпа игры");return;}
        Target=1;
        Status="Режим времени отключён: приоритет нативной паузы, загрузки или внешнего изменения";
        // A save/restore writer may hand our very rate back a moment later: re-own it then.
        if(blocked==null){_orphan=was;_orphanUntil=Time.unscaledTime+30f;}
    }
    private void TryResume(bool wanted)
    {
        if(!wanted&&Time.unscaledTime>_orphanUntil){_orphanUntil=0f;return;}
        if(wanted&&Time.unscaledTime<_retryAt)return;
        if(BlockReason(true)!=null){if(wanted)Wait(WaitFree);return;}
        if(_app.Coop?.Deny("cinematic time rate") is string denied){if(wanted)Wait(denied);else _orphanUntil=0f;return;}
        float want=wanted?Target:_orphan,scale=Time.timeScale;
        bool permitted=want<=1f||AccelerationAllowed;
        if(permitted&&!M26RatePolicy.Matches(want,1)&&M26RatePolicy.Matches(scale,want))
        {
            try{Lease(want,true);Adoptions++;}
            catch(Exception e){_retryAt=Time.unscaledTime+2f;if(wanted)Wait("повтор не удался: "+e.Message);}
            return;
        }
        if(!wanted)return;
        if(!M26RatePolicy.Matches(scale,1)){Wait(scale<=0f?"ждёт снятия паузы":"ждёт: скорость изменена игрой или другим модом");return;}
        if(!permitted){Wait("ускорение ждёт подтверждения в меню");return;}
        try{Lease(want,false);Reapplies++;}
        catch(Exception e){_retryAt=Time.unscaledTime+2f;Wait("повтор не удался: "+e.Message);}
    }
    private void Wait(string reason)
    {
        Waiting=true;
        if(reason==_waitReason)return; // no status rebuilt per frame
        _waitReason=reason;
        Status="Закреплено ×"+M26RatePolicy.Label(Target)+" · "+reason;
    }
    // null while the game runs freely; otherwise something else owns the clock right now.
    private string BlockReason(bool forApply)
    {
        if(!_app.Ready||_app.Map.Busy||_app.Map.NativeMapOpen||NativeUiPriority.HasNativeFocus||_app.Context.TimeDirector.IsFastForwarding()||_app.Benchmark.Active)
            return "Завершите загрузку, нативное меню, ускоренную перемотку или замер FPS.";
        if(forApply&&_app.Audit.MainAutosaveHeld)return "Дождитесь автосохранения.";
        return null;
    }
    internal void Reset()
    {
        try{RepairNativePauseMemory();_lease?.Restore();}
        finally
        {
            _lease=null;Rate=1;
            try{_fixedLease?.Restore();}
            catch(Exception e){PauseDiagnostics="physics step restore: "+e.GetType().Name;}
            finally{_fixedLease=null;}
        }
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
    // UserData/CreatorToolkit/cinematic-rate.txt: rate, pin and smooth physics between launches.
    private string SettingsPath()=>string.IsNullOrEmpty(_app.DataDirectory)?null:Path.Combine(_app.DataDirectory,"cinematic-rate.txt");
    private string Serialize()=>"rate="+Target.ToString("0.###",CultureInfo.InvariantCulture)+"\npinned="+(Pinned?"1":"0")+"\nsmooth="+(SmoothPhysics?"1":"0")+"\n";
    private void EnsureLoaded()
    {
        if(_loaded)return;
        string path=SettingsPath();if(path==null)return;
        _loaded=true;
        try
        {
            if(!File.Exists(path))return;
            float rate=1f;
            foreach(string line in File.ReadAllLines(path))
            {
                int eq=line.IndexOf('=');if(eq<=0)continue;
                string key=line.Substring(0,eq).Trim(),value=line.Substring(eq+1).Trim();
                if(key=="rate"&&float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out float parsed)&&M26RatePolicy.Allowed(parsed))rate=M26RatePolicy.Snap(parsed);
                else if(key=="pinned")Pinned=value=="1";
                else if(key=="smooth")SmoothPhysics=value!="0";
            }
            // Only a pinned rate comes back after a restart; acceleration asks again per save.
            if(Pinned)Target=rate;
            _savedText=Serialize();
        }
        catch(Exception e){MelonLoader.MelonLogger.Warning("Cinematic rate settings not loaded: "+e.Message);}
    }
    private void Save()
    {
        string path=SettingsPath();if(path==null)return;
        string text=Serialize();if(text==_savedText)return;
        try
        {
            string temp=path+".tmp";
            File.WriteAllText(temp,text);
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            _savedText=text;
        }
        catch(Exception e){MelonLoader.MelonLogger.Warning("Cinematic rate settings not saved: "+e.Message);}
    }
}
