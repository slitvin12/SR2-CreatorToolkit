using System;
using System.Collections.Generic;
using System.Text;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CreatorToolkit;

// Opt-in, session-scoped conveniences. No loot collection, quest mutation,
// arbitrary save IDs or edits to shared assets. Profiles restore their snapshot.
internal sealed class ConvenienceTools
{
    private sealed class Snapshot
    {
        internal IntPtr Session;
        internal bool God, Energy, Flight, Hud, Clock, Mobs, Info, Jump, Items;
        internal float Speed,JumpHeight;
    }
    private readonly ToolkitMod _app;
    private Snapshot _before;
    private CinemaTools.Waypoint _safe;
    private IntPtr _refillSession;
    private int _refillSlot=-1;
    private string _refillId="";
    private float _nextSafe, _nextRefill, _nextHud;
    private float _nextZoneText;
    private IntPtr _hudZone;
    private int _hudFlags=-1, _hudSpeed=-1, _hudMinute=-1;
    private readonly StringBuilder _effects=new(192);
    internal int Profile {get;private set;}
    internal string ProfileName => Profile==1?"Исследователь":Profile==2?"Кадр":"Нет";
    internal bool ShowHud {get;private set;}
    internal bool TrackSafe {get;private set;}
    internal int HudPlacement;
    internal string HudZone="—", HudCoordinates="—", HudEffects="Все читы выключены", HudTime="00:00";
    internal string HudShortZone="—";
    internal bool Refilling => _refillSlot>=0;
    internal string RefillStatus {get;private set;}="Автопополнение выключено";
    internal bool HasSafe => _safe!=null&&_safe.Session==_app.Session;
    internal Vector3 SafePosition => HasSafe?_safe.Position:Vector3.zero;
    internal string SafeStatus => HasSafe?"Безопасная точка: "+_safe.Position.ToString("F1"):"Встаньте на безопасную поверхность";
    internal int RefillOperations {get;private set;}
    internal ConvenienceTools(ToolkitMod app)=>_app=app;

    internal void SetHud(bool visible) { ShowHud=visible;_nextHud=0; }
    internal void SetProfile(int profile)
    {
        if(!_app.Ready||_app.Map.Busy)throw new InvalidOperationException("Дождитесь загрузки сцены или завершения телепортации.");
        if(profile<0||profile>2)throw new ArgumentOutOfRangeException(nameof(profile));
        if(profile==Profile)profile=0;
        RestoreProfile();
        if(profile==0){_app.Notify("Профиль выключен. Предыдущие настройки восстановлены.");return;}
        _before=new Snapshot {Session=_app.Session,God=_app.Player.GodMode,Energy=_app.Player.InfiniteEnergy,
            Flight=_app.Player.Flight,Speed=_app.Player.SpeedMultiplier,Hud=_app.Cinema.HideHud,
            Clock=_app.World.FreezeTime,Mobs=_app.World.FreezeMobs,Info=ShowHud,
            Jump=_app.Player.HighJump,JumpHeight=_app.Player.JumpHeightMultiplier,Items=_app.Player.InfiniteItems};
        Profile=profile;
        try
        {
            _app.Player.GodMode=_app.Player.InfiniteEnergy=true;
            _app.Player.SpeedMultiplier=profile==1?2f:1f;
            if(profile==1) { _app.Cinema.SetHideHud(false);SetHud(true); }
            else
            {
                SetHud(false);_app.World.SetFreezeTime(true);_app.World.SetFreezeMobs(true);
                _app.Player.SetFlight(true);_app.Cinema.SetHideHud(true);
            }
            _app.Player.Tick();
        }
        catch {RestoreProfile();throw;}
        _app.Notify("Профиль «"+ProfileName+"» включён. Повторное нажатие восстановит прежние настройки.");
    }
    internal void RestoreProfile()
    {
        var before=_before;
        try
        {
            if(before==null||before.Session!=_app.Session)return;
            // Restore all independent settings even if one native controller vanished.
            try { _app.Player.SetFlight(before.Flight); }
            finally
            {
                _app.Player.GodMode=before.God;_app.Player.InfiniteEnergy=before.Energy;_app.Player.SpeedMultiplier=before.Speed;
                _app.Player.HighJump=before.Jump;_app.Player.JumpHeightMultiplier=before.JumpHeight;_app.Player.InfiniteItems=before.Items;
                try { _app.World.SetFreezeTime(before.Clock); }
                finally
                {
                    try { _app.World.SetFreezeMobs(before.Mobs); }
                    finally
                    {
                        try { _app.Cinema.SetHideHud(before.Hud); }
                        finally {SetHud(before.Info);}
                    }
                }
            }
        }
        finally {_before=null;Profile=0;}
    }

    internal void SetSafeTracking(bool enabled)
    {
        TrackSafe=enabled;_nextSafe=0;
        if(enabled)CaptureSafe();else _safe=null;
        _app.Notify(enabled?"SOS включён: записываются только проверенные наземные позиции. Ctrl + Home — возврат.":"SOS выключен. Наземная точка очищена.");
    }
    internal bool CaptureSafe()
    {
        if(!TrackSafe||!_app.Ready||_app.Player.Flight||_app.Map.Busy||_app.Context.PlayerState.GetCurrHealth()<=0)return false;
        var controller=_app.Context.Player.GetComponentInChildren<SRCharacterController>(true);
        var player=_app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true);
        if(controller?.Motor==null||!controller.Motor.enabled||!controller.IsGrounded||player?.SceneGroup==null)return false;
        var position=controller.Motor.TransientPosition;
        if(!_app.Player.IsClearPosition(position)||!_app.Player.TryMapGround(position,true,out var ground)||
            Math.Abs(ground.y-position.y)>1.5f)return false;
        _safe=new CinemaTools.Waypoint {Name="SOS — наземная точка",Position=position,Group=player.SceneGroup,Session=_app.Session};
        return true;
    }
    internal void Rescue()
    {
        if(!HasSafe)throw new InvalidOperationException("Наземная точка ещё не записана в этом сейве.");
        _app.Map.RequestTeleport(new MapTools.Point {Id="CreatorToolkit.SOS",Name=_safe.Name,Category="Телепорты",
            Group=_safe.Group,GroupName=_safe.Group.name,Position=_safe.Position,ExactHeight=true,AllowNearbyLanding=true});
    }
    internal void OpenNearestCapsule()
    {
        var point=_app.Map.NearestUnopenedCapsule();
        if(point==null)throw new InvalidOperationException("Доступная неоткрытая капсула не найдена. Проверьте индекс версии карты.");
        _app.Map.Visible.Add("Капсулы");_app.Map.HideOpened=true;_app.Map.Search="";
        _app.Map.OpenAt(point);
        _app.Notify("Ближайшая капсула: "+point.Name+". На карте можно выбрать безопасный телепорт. Автосбор не выполняется.");
    }

    internal void RequestRefill()
    {
        if(Refilling){StopRefill("Автопополнение выключено");return;}
        var ammo=_app.Context.PlayerState.Ammo;
        int index=ammo?._selectedAmmoIdx??-1;
        if(ammo?.Slots==null||index<0||index>=ammo.Slots.Length)throw new InvalidOperationException("Выберите открытый непустой слот.");
        string id=ammo.Slots[index]?.Id?.ReferenceId;
        if(string.IsNullOrEmpty(id)||ammo.Slots[index].Count<=0)throw new InvalidOperationException("Сначала поместите ресурс в выбранный слот.");
        _app.Confirm("Включить автопополнение?", "Слот "+(index+1)+" будет пополняться "+(ModInterop.CustomVacLimits?"до лимита слота (его задаёт мод вакпака), когда останется четверть или меньше":"до 100 шт., когда останется 25 или меньше")+". Количество сохраняется игрой. При смене выбранного слота, ресурса или опустошении чит отключится.",()=>ArmRefill(index,id));
    }
    private void ArmRefill(int index,string id)
    {
        var ammo=_app.Context.PlayerState.Ammo;
        if(ammo?.Slots==null||ammo._selectedAmmoIdx!=index||index<0||index>=ammo.Slots.Length)
            throw new InvalidOperationException("Выбранный слот изменился; автопополнение не включено.");
        var slot=ammo.Slots[index];
        if(slot==null||!slot.IsUnlocked||slot.Count<=0||slot.Id==null||slot.Id.ReferenceId!=id||
            slot.Definition==null||!slot.Definition.IsAllowed(slot.Id))
            throw new InvalidOperationException("Слот закрыт, пуст или его ресурс изменился; автопополнение не включено.");
        _refillSlot=index;_refillId=id;_refillSession=_app.Session;_nextRefill=0;
        RefillStatus="Слот "+(index+1)+": порог 25 → 100";
        _app.Notify("Автопополнение включено только для выбранного ресурса. Смена слота отключит чит.");
    }
    internal void ArmRefillForTest()
    {
        var ammo=_app.Context.PlayerState.Ammo;
        ArmRefill(ammo._selectedAmmoIdx,ammo.Slots[ammo._selectedAmmoIdx].Id?.ReferenceId);
    }
    internal void StopRefill(string reason)
    {
        _refillSlot=-1;_refillId="";_refillSession=IntPtr.Zero;RefillStatus=reason;
    }
    internal void TickRefill()
    {
        if(!Refilling)return;
        var ammo=_app.Context?.PlayerState?.Ammo;
        if(_refillSession!=_app.Session||ammo?.Slots==null||ammo._selectedAmmoIdx!=_refillSlot||_refillSlot>=ammo.Slots.Length)
        {StopRefill("Отключено: выбранный слот или сейв изменился");return;}
        var slot=ammo.Slots[_refillSlot];
        if(slot==null||!slot.IsUnlocked||slot.Id==null||slot.Id.ReferenceId!=_refillId||slot.Count<=0||
            slot.Definition==null||!slot.Definition.IsAllowed(slot.Id))
        {StopRefill("Отключено: слот пуст, закрыт или ресурс изменился");return;}
        if(_app.Map.Busy||_app.Map.NativeMapOpen||Time.timeScale<=0)return;
        int target=ModInterop.FillTarget(slot); // 100, or the slot's own limit with a slot-limit mod
        if(!SafetyPolicy.NeedsRefill(slot.Count,target))return;
        int before=slot.Count;
        try
        {
            // Native setter updates its HUD/model callbacks; never replace metadata.
            slot.Count=target;
            if(slot.Count!=target)throw new InvalidOperationException("Слот не принял пополнение.");
            RefillOperations++;
        }
        catch
        {
            string expected=_refillId;
            StopRefill("Отключено: ошибка нативного пополнения");
            if(slot.Id?.ReferenceId==expected)slot.Count=before;
            throw;
        }
    }

    internal void Tick()
    {
        if(!_app.Ready)return;
        if(TrackSafe&&!_app.MenuOpen&&!_app.Map.NativeMapOpen&&Time.unscaledTime>=_nextSafe)
        {_nextSafe=Time.unscaledTime+2f;CaptureSafe();}
        if(Refilling&&Time.unscaledTime>=_nextRefill){_nextRefill=Time.unscaledTime+.25f;TickRefill();}
        var kb=Keyboard.current;
        if(TrackSafe&&HasSafe&&kb!=null&&kb.homeKey.wasPressedThisFrame&&(kb.leftCtrlKey.isPressed||kb.rightCtrlKey.isPressed)&&
            !_app.MenuOpen&&!_app.Map.NativeMapOpen&&!_app.Map.Busy&&Cursor.lockState==CursorLockMode.Locked&&Time.timeScale>0)
            Rescue();
        if(ShowHud&&Time.unscaledTime>=_nextHud){_nextHud=Time.unscaledTime+.25f;RefreshHud();}
    }
    internal void RefreshHud()
    {
        var motor=_app.Player.Controller?.Motor;
        if(motor==null)return;
        var p=motor.TransientPosition;
        HudCoordinates=$"X {p.x:0.0}   Y {p.y:0.0}   Z {p.z:0.0}";
        var zone=_app.Context.PlayerZoneTracker?.GetCurrentZone()??_app.Context.PlayerZoneTracker?.GetLatestZone();
        var zoneId=zone?.Pointer??IntPtr.Zero;
        // Localization can involve native tables/handles. Resolve on zone change
        // and occasionally for a language change, not on every 4 Hz position tick.
        if(zoneId!=_hudZone||Time.unscaledTime>=_nextZoneText)
        {
            _hudZone=zoneId;_nextZoneText=Time.unscaledTime+5f;
            HudZone=zone?.Name?.GetLocalizedString();
            if(string.IsNullOrWhiteSpace(HudZone)||HudZone.StartsWith("!!"))HudZone=zone?.name??"Неизвестная зона";
            HudShortZone=HudZone.Length>28?HudZone.Substring(0,25)+"…":HudZone;
        }
        int minutes=(int)(_app.World.Hour()*60f)%1440;
        if(minutes!=_hudMinute){_hudMinute=minutes;HudTime=$"{minutes/60:00}:{minutes%60:00}";}
        int flags=Profile|(_app.Player.GodMode?4:0)|(_app.Player.InfiniteEnergy?8:0)|(_app.Player.Flight?16:0)|
            (Refilling?32:0)|(_app.World.FreezeTime?64:0)|(_app.World.FreezeMobs?128:0)|(_app.World.DisableTarr?256:0);
        int speed=(int)Math.Round(_app.Player.SpeedMultiplier*10f);
        if(flags==_hudFlags&&speed==_hudSpeed)return;
        _hudFlags=flags;_hudSpeed=speed;_effects.Clear();
        void Add(string text){if(_effects.Length>0)_effects.Append(" · ");_effects.Append(text);}
        if(Profile!=0)Add(ProfileName);
        if(_app.Player.GodMode)Add("Бессмертие");
        if(_app.Player.InfiniteEnergy)Add("Энергия ∞");
        if(_app.Player.Flight)Add("Полёт");
        if(Math.Abs(_app.Player.SpeedMultiplier-1f)>.01f)Add("×"+_app.Player.SpeedMultiplier.ToString("0.0"));
        if(Refilling)Add("Автослот");
        if(_app.World.FreezeTime)Add("Время ❚❚");
        if(_app.World.FreezeMobs)Add("Слаймы ❚❚");
        if(_app.World.DisableTarr)Add("Без варров");
        HudEffects=_effects.Length==0?"Все читы выключены":_effects.ToString();
    }
    internal void Reset()
    {
        try {RestoreProfile();}
        finally
        {
            StopRefill("Автопополнение выключено");TrackSafe=false;_safe=null;ShowHud=false;HudPlacement=0;
            _nextSafe=_nextRefill=_nextHud=0;
            _nextZoneText=0;_hudZone=IntPtr.Zero;_hudFlags=_hudSpeed=_hudMinute=-1;_effects.Clear();
        }
    }
}