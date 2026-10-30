using System;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppKinematicCharacterController;
using Il2CppMonomiPark.KFC;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController.Abilities;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace CreatorToolkit;

internal sealed partial class PlayerTools
{
    private readonly ToolkitMod _app;
    internal bool GodMode, InfiniteEnergy, HighJump, InfiniteItems;
    internal float SpeedMultiplier = 1f;
    internal float JumpHeightMultiplier=2f;
    internal int PreservedSpends {get;private set;}
    private JumpAbilityBehavior _jumpBehavior;
    private JumpAbilityData _jumpOriginal,_jumpClone;
    private IntPtr _abilityArray;
    private float _jumpBaseSpeed,_jumpBaseHold;
    internal bool PrivateJumpForTest=>_jumpClone!=null&&_jumpBehavior!=null&&!_jumpBehavior.WasCollected&&
        _jumpBehavior.AbilityData==_jumpClone&&_jumpOriginal!=_jumpClone;
    internal float JumpLaunchSpeedForTest=>_jumpBehavior?.AbilityData?.JumpUpSpeed??0;
    internal float JumpOriginalLaunchForTest=>_jumpBaseSpeed;
    internal int JumpAssetRoots=>_jumpClone==null?0:2;
    internal bool Flight { get; private set; }
    private SRCharacterController _controller;
    private KinematicCharacterMotor _motor;
    private CharacterControllerParameters _original, _clone;
    private HideFlags _originalFlags,_originalExpectedFlags,_jumpOriginalFlags,_jumpExpectedFlags;
    private bool _originalFlagLease,_jumpFlagLease;
    internal int BorrowedAssetLeaseChangesForTest=>(_originalFlagLease?1:0)+(_jumpFlagLease?1:0);
    internal int AssetLeaseRestoreConflictsForTest {get;private set;}
    private float _groundSpeed, _airSpeed, _airAcceleration;
    private bool _oldGravity, _oldCapsule, _oldMovement, _oldGround, _oldMotorEnabled;
    private Vector3 _flyVelocity, _lastSafe;
    private Il2CppReferenceArray<Collider> _overlaps;
    private IntPtr _playerPointer;
    private float _nextFlightStreaming;
    private SceneGroup _flightDestination;private float _flightDeadline;
    internal string FlightStreamingStatus {get;private set;}="Ожидание полёта";
    private bool _transitHold, _holdMotorEnabled, _holdGravity;
    private SRCharacterController _holdController;
    private KinematicCharacterMotor _holdMotor;
    internal bool TransitHeld => _transitHold;
    internal SRCharacterController Controller=>_controller!=null&&!_controller.WasCollected?_controller:null;

    internal PlayerTools(ToolkitMod app) => _app = app;

    internal void Tick()
    {
        SceneContext ctx = _app.Context;
        if (ctx == null) return;
        var state = ctx.PlayerState;
        if (GodMode && state.GetCurrHealth() < state.GetMaxHealth()) state.SetHealth(state.GetMaxHealth());
        if (InfiniteEnergy && state.GetCurrEnergy() < state.GetMaxEnergy()) state.SetEnergy(state.GetMaxEnergy());
        // Controller hierarchy is stable for one player; invalidate on a new
        // player or a destroyed native component instead of searching each frame.
        SRCharacterController current = _controller;
        if(ctx.Player.Pointer!=_playerPointer||current==null||current.WasCollected)
        {
            _playerPointer=ctx.Player.Pointer;
            current=ctx.Player.GetComponentInChildren<SRCharacterController>(true);
        }
        if (current != _controller)
        {
            ReleaseHoldController();
            RestoreController();
            _controller = current;
            _motor = current != null ? current.Motor : null;
        }
        UpdateSpeed();
        if (_transitHold) { MaintainTransitHold(); return; }
        if (!Flight || _controller == null || _motor == null) return;
        // Suspend native physics only while flying; do not detour the ref-Vector3 velocity callback.
        _motor.enabled = false;
        _controller._BypassGravity_k__BackingField = true;
        _motor.SetMovementCollisionsSolvingActivation(false);
        _motor.SetGroundSolvingActivation(false);
        _motor.SetCapsuleCollisionsActivation(false);
        _flyVelocity = Vector3.zero;
        var kb = Keyboard.current;
        Camera cam = Camera.main;
        if (kb != null && cam != null && !_app.MenuOpen && !NativeUiPriority.HasNativeFocus && !(_app.Map?.Busy ?? false) && Cursor.lockState == CursorLockMode.Locked && Time.timeScale > 0f)
        {
            float forward = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            float right = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float up = (kb.spaceKey.isPressed ? 1f : 0f) - (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? 1f : 0f);
            Vector3 direction = cam.transform.forward * forward + cam.transform.right * right + Vector3.up * up;
            _flyVelocity = Vector3.ClampMagnitude(direction, 1f) * (10f * SafetyPolicy.Speed(SpeedMultiplier));
        }
        if (_flightDestination==null&&_flyVelocity.sqrMagnitude > 0f && Time.timeScale > 0f && !_app.MenuOpen)
        {
            Vector3 next = _motor.TransientPosition + _flyVelocity * Mathf.Min(Time.deltaTime, 0.05f);
            if (!float.IsFinite(next.x) || !float.IsFinite(next.y) || !float.IsFinite(next.z))
                throw new InvalidOperationException("Невалидные координаты полёта; перемещение отменено.");
            _motor.SetPosition(next, true);
        }
        TickFlightStreaming();
        _controller.ResetVelocity(false);
        if (ClearCapsule(_motor.TransientPosition)) _lastSafe = _motor.TransientPosition;
    }

    private void TickFlightStreaming()
    {
        var loader=SystemContext.Instance?.SceneLoader;if(loader==null)return;
        if(_flightDestination!=null)
        {
            if(_app.Map.SceneAssetsReady(_flightDestination))
            {FlightStreamingStatus="Остров загружен: "+_flightDestination.name;_flightDestination=null;}
            else if(Time.unscaledTime>_flightDeadline)
            {_flightDestination=null;FlightStreamingStatus="Прогрузка не подтверждена; повторная попытка через 5 с";_nextFlightStreaming=Time.unscaledTime+5f;}
            return;
        }
        if(Time.unscaledTime<_nextFlightStreaming||loader.IsSceneLoadInProgress||_app.MenuOpen||NativeUiPriority.HasNativeFocus)return;
        _nextFlightStreaming=Time.unscaledTime+.5f;
        // The motor is disabled during NoClip, but native region sources must
        // still receive its real transform and wake/check streamed sectors.
        var members=_app.Context.Player.GetComponentsInChildren<Il2CppMonomiPark.SlimeRancher.Regions.RegionMember>(true);
        foreach(var member in members)if(member!=null&&member.enabled)member.UpdateRegionMembershipFromPositionImpl(_motor.TransientPosition);
        var loaders=_app.Context.Player.GetComponentsInChildren<SECTR_RegionLoader>(true);
        foreach(var regionLoader in loaders)if(regionLoader!=null&&regionLoader.enabled){regionLoader.Update();regionLoader.UpdateAwake();}
        var group=_app.Map.FlightGroupAt(_motor.TransientPosition);
        if(group==null){FlightStreamingStatus="Зона полёта не определена; чужая сцена не подставляется";return;}
        if(group==loader.CurrentSceneGroup){FlightStreamingStatus="Прогрузка текущего острова: "+group.name;return;}
        Vector3 position=_motor.TransientPosition;
        var parameters=new SceneLoadingParameters{TeleportPlayer=true,PauseGame=true,
            TeleportPosition=new Il2CppSystem.Nullable<Vector3>{hasValue=true,value=position},
            TeleportRotation=new Il2CppSystem.Nullable<Vector3>(),ReloadAllCoreScenes=false,ReloadAllNonCoreScenes=false};
        _app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true).PrepareForUnmanagedTeleport();
        _flightDestination=group;_flightDeadline=Time.unscaledTime+45f;_flyVelocity=Vector3.zero;
        FlightStreamingStatus="Загрузка острова в полёте: "+group.name;
        try{loader.LoadSceneGroup(group,parameters);}catch{_flightDestination=null;throw;}
    }

    internal void BeginTransitHold()
    {
        if (Flight) throw new InvalidOperationException("Сначала восстановите коллизии NoClip перед безопасным переходом.");
        if (_controller == null || _motor == null) Tick();
        if (_controller == null || _motor == null) throw new InvalidOperationException("Физика игрока ещё недоступна.");
        _transitHold = true;
        MaintainTransitHold();
    }

    internal void MaintainTransitHold()
    {
        if (!_transitHold || _controller == null || _controller.WasCollected || _motor == null || _motor.WasCollected) return;
        if (_holdMotor != _motor)
        {
            ReleaseHoldController();
            _holdController = _controller; _holdMotor = _motor;
            _holdMotorEnabled = _motor.enabled;
            _holdGravity = _controller._BypassGravity_k__BackingField;
            _controller.StopAbilitiesForTeleport();
        }
        // Unlike NoClip this never disables capsule, movement or grounding
        // collisions. SceneLoader remains free to apply its native placement.
        _holdMotor.enabled = false;
        _holdController._BypassGravity_k__BackingField = true;
        _holdMotor.BaseVelocity = Vector3.zero;
        _holdController.ResetVelocity(false);
    }

    private void ReleaseHoldController()
    {
        try
        {
            if (_holdMotor != null && !_holdMotor.WasCollected)
            { _holdMotor.BaseVelocity = Vector3.zero; _holdMotor.enabled = _holdMotorEnabled; }
        }
        finally
        {
            try
            {
                if (_holdController != null && !_holdController.WasCollected)
                { _holdController._BypassGravity_k__BackingField = _holdGravity; _holdController.ResetVelocity(false); }
            }
            finally { _holdMotor = null; _holdController = null; }
        }
    }

    internal void EndTransitHold()
    {
        try { ReleaseHoldController(); }
        finally { _transitHold = false; }
    }

    private void UpdateSpeed()
    {
        if (_controller == null || _controller.WasCollected) return;
        float speed = SafetyPolicy.Speed(SpeedMultiplier);
        float jump=HighJump?PlayerMovementPolicy.JumpHeight(JumpHeightMultiplier):1f;
        if (Math.Abs(speed - 1f) < 0.001f&&Math.Abs(jump-1f)<.001f)
        {
            RestoreParameters();
            return;
        }
        // Native upgrades may replace parameters while our private speed clone exists.
        // Do not keep multiplying an obsolete clone or overwrite the fresh upgrade asset.
        if (_clone != null && _controller._parameters != _clone) RestoreParameters();
        if (_clone == null)
        {
            _original = _controller._parameters;
            if (_original == null) throw new InvalidOperationException("Параметры нативного контроллера пока недоступны.");
            _originalFlagLease=HoldBorrowedAsset(_original,out _originalFlags,out _originalExpectedFlags);
            _groundSpeed = _original._maxGroundedMoveSpeed;
            _airSpeed = _original._maxAirMoveSpeed;
            _airAcceleration = _original._airAccelerationSpeed;
            _clone = Object.Instantiate(_original);
            _clone.name = "CreatorToolkit_PlayerParameters";
            _controller._parameters = _clone;
        }
        // A private clone avoids altering a shared game asset or multiplying velocity each frame.
        _clone._maxGroundedMoveSpeed = _groundSpeed * speed;
        _clone._maxAirMoveSpeed = _airSpeed * speed;
        _clone._airAccelerationSpeed = _airAcceleration * speed;
        UpdateJump(jump);
    }

    private void UpdateJump(float height)
    {
        if(height<=1.001f){RestoreJump();return;}
        var abilities=_controller.AbilityBehaviors;
        if(abilities==null)throw new InvalidOperationException("Нативная способность прыжка ещё не инициализирована.");
        if(_jumpClone!=null&&(_abilityArray!=abilities.Pointer||_jumpBehavior==null||
            _jumpBehavior.WasCollected||_jumpBehavior.AbilityData!=_jumpClone))RestoreJump();
        if(_jumpClone==null)
        {
            for(int i=0;i<abilities.Length;i++)
            {
                var found=abilities[i]?.TryCast<JumpAbilityBehavior>();
                if(found!=null&&found.CharacterController==_controller){_jumpBehavior=found;break;}
            }
            _jumpOriginal=_jumpBehavior?.AbilityData;
            if(_jumpOriginal==null)throw new InvalidOperationException("Живая нативная способность прыжка не найдена.");
            _jumpFlagLease=HoldBorrowedAsset(_jumpOriginal,out _jumpOriginalFlags,out _jumpExpectedFlags);
            _jumpBaseSpeed=_jumpOriginal.JumpUpSpeed;_jumpBaseHold=_jumpOriginal.InitialHoldVelocity;
            if(!float.IsFinite(_jumpBaseSpeed)||_jumpBaseSpeed<=0||!float.IsFinite(_jumpBaseHold))
                throw new InvalidOperationException("Нативные параметры прыжка невалидны.");
            _jumpClone=Object.Instantiate(_jumpOriginal);
            _jumpClone.name="CreatorToolkit_PrivateJumpAbility";
            // The behavior reads its own AbilityData, not just the controller
            // parameter array. Both references must point to our private clone.
            var source=_clone._abilityData;
            if(source==null)throw new InvalidOperationException("Массив личных способностей недоступен.");
            var owned=new Il2CppReferenceArray<AbilityData>(source.Length);
            for(int i=0;i<source.Length;i++)owned[i]=source[i]==_jumpOriginal?_jumpClone:source[i];
            _clone._abilityData=owned;
            _jumpBehavior.AbilityData=_jumpClone;_abilityArray=abilities.Pointer;
        }
        float factor=PlayerMovementPolicy.JumpVelocityFactor(height);
        _jumpClone._jumpUpSpeed=_jumpBaseSpeed*factor;
        _jumpClone._initialHoldVelocity=_jumpBaseHold*factor;
        // Gravity, ceiling checks, coyote time, input and native velocity
        // callbacks remain untouched. No ref-Vector3 Harmony detour is used.
    }

    private void RestoreJump()
    {
        try
        {
            if(_jumpClone!=null)
            {
                if(_jumpBehavior!=null&&!_jumpBehavior.WasCollected&&_jumpBehavior.AbilityData==_jumpClone)
                    _jumpBehavior.AbilityData=_jumpOriginal;
                if(_clone!=null&&!_clone.WasCollected)
                {
                    var data=_clone._abilityData;
                    if(data!=null)for(int i=0;i<data.Length;i++)if(data[i]==_jumpClone)data[i]=_jumpOriginal;
                }
            }
        }
        finally
        {
            if(_jumpClone!=null&&!_jumpClone.WasCollected)Object.Destroy(_jumpClone);
            ReleaseBorrowedAsset(_jumpOriginal,_jumpOriginalFlags,_jumpExpectedFlags,ref _jumpFlagLease);
            _jumpBehavior=null;_jumpOriginal=_jumpClone=null;_abilityArray=IntPtr.Zero;
            _jumpBaseSpeed=_jumpBaseHold=0;
        }
    }

    internal bool PreserveSlotSpend(AmmoSlot slot)
    {
        if(!InfiniteItems||!_app.Ready||slot==null||slot.WasCollected||!slot.IsUnlocked||
            slot.Id==null||!PlayerMovementPolicy.PreserveSpend(slot.Count,1))return false;
        var slots=_app.Context.PlayerState.Ammo?.Slots;
        if(slots==null)return false;
        for(int i=0;i<slots.Length;i++)
            if(slots[i]?.Pointer==slot.Pointer){PreservedSpends++;return true;}
        return false; // Never affect a drone, other inventory or a foreign slot.
    }
    internal bool PreserveManagerSpend(AmmoSlotManager manager,int index,int count)
    {
        if(!InfiniteItems||!_app.Ready||count<=0)return false;
        var own=_app.Context?.PlayerState?.Ammo;
        if(!InfiniteItems||!_app.Ready||manager==null||own==null||manager.Pointer!=own.Pointer||
            own.Slots==null||index<0||index>=own.Slots.Length)return false;
        var slot=own.Slots[index];
        if(slot==null||!slot.IsUnlocked||slot.Id==null||!PlayerMovementPolicy.PreserveSpend(slot.Count,count))return false;
        PreservedSpends++;return true;
    }
    internal bool PreserveManagerSpend(AmmoSlotManager manager,IdentifiableType id,int count)
    {
        if(!InfiniteItems||!_app.Ready||count<=0)return false;
        var own=_app.Context?.PlayerState?.Ammo;
        if(!InfiniteItems||!_app.Ready||manager==null||own==null||manager.Pointer!=own.Pointer||
            own.Slots==null||id==null||count<=0)return false;
        long available=0;
        for(int i=0;i<own.Slots.Length;i++)
        {
            var slot=own.Slots[i];
            if(slot!=null&&slot.IsUnlocked&&slot.Id==id&&slot.Count>0)available+=slot.Count;
        }
        if(available<count)return false;
        PreservedSpends++;return true;
    }

    internal void SetFlight(bool enabled)
    {
        if(enabled&&_transitHold)throw new InvalidOperationException("Безопасный переход удерживает физику. Дождитесь посадки или возврата.");
        if(enabled&&StarlightBridge.NoClipActive)throw new InvalidOperationException("Включён NoClip Starlight — выключите его в меню читов Starlight.");
        if (enabled == Flight) return;
        if (!enabled) { StopFlight(); _app.Notify("Полёт выключен; коллизии и гравитация восстановлены."); return; }
        if (_controller == null || _motor == null) { Tick(); }
        if (_controller == null || _motor == null) throw new InvalidOperationException("Нативный контроллер игрока недоступен.");
        Vector3 position = _motor.TransientPosition;
        if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))
            throw new InvalidOperationException("Мотор игрока имеет невалидные координаты; полёт запрещён.");
        _oldMotorEnabled = _motor.enabled;
        _oldGravity = _controller._BypassGravity_k__BackingField;
        _oldCapsule = _motor.Capsule != null && _motor.Capsule.enabled;
        _oldMovement = _motor._solveMovementCollisions;
        _oldGround = _motor._solveGrounding;
        _lastSafe = _motor.TransientPosition;
        _controller.StopAbilitiesForTeleport();
        _controller.ResetVelocity(false);
        Flight = true;
        _motor.enabled = false;
        _flyVelocity = Vector3.zero;
        _app.Notify("Полёт / NoClip включён. Закройте меню: WASD, Space вверх, Shift вниз.");
    }

    internal bool TryMapGround(Vector3 target, bool exactHeight, out Vector3 landing)
    {
        landing=default;
        if(_motor==null||_controller==null)return false;
        float ceiling=exactHeight?target.y+6f:Math.Max(900f,target.y+200f);
        float range=exactHeight?20f:1800f;
        var hits=Physics.RaycastAll(new Vector3(target.x,ceiling,target.z),Vector3.down,range,_motor.CollidableLayers,QueryTriggerInteraction.Ignore);
        float nearest=float.MaxValue;
        for(int i=0;i<hits.Length;i++)
        {
            var hit=hits[i];var collider=hit.collider;
            if(collider==null||hit.normal.y<.65f||hit.distance>=nearest||collider.transform.IsChildOf(_controller.transform))continue;
            string name=(collider.name+" "+collider.transform.parent?.name).ToLowerInvariant();
            if(name.Contains("water")||name.Contains("slimesea")||name.Contains("lava")||name.Contains("slime"))continue;
            if(collider.GetComponentInParent<IdentifiableActor>()!=null)continue;
            Vector3 candidate=hit.point+Vector3.up*.12f;
            if(!ClearCapsule(candidate))continue;
            nearest=hit.distance;landing=candidate;
        }
        return nearest<float.MaxValue;
    }
    internal bool TryNearbyMapGround(Vector3 target,out Vector3 landing)
    {
        // Saved positions can become occupied while their region is unloaded.
        // Only saved-point callers opt in; ordinary map clicks still refuse an
        // occupied target. Every candidate uses the same slope, hazard, actor
        // and unsaturated native capsule checks as a normal map landing.
        if(TryMapGround(target,true,out landing)&&
            SafetyPolicy.NearbyReturnOffset(landing.x-target.x,landing.y-target.y,landing.z-target.z))return true;
        for(int ring=1;ring<=2;ring++)
        {
            float radius=SafetyPolicy.MaxReturnOffset*ring*.5f;
            for(int direction=0;direction<8;direction++)
            {
                float angle=direction*Mathf.PI*.25f;
                var nearby=target+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
                if(TryMapGround(nearby,true,out landing)&&
                    SafetyPolicy.NearbyReturnOffset(landing.x-target.x,landing.y-target.y,landing.z-target.z))return true;
            }
        }
        landing=default;return false;
    }
    internal string DescribeMapGround(Vector3 target,bool exactHeight)
    {
        // Failure-only telemetry: never broaden a landing or bypass clearance.
        if(_motor==null||_controller==null)return "no native motor/controller";
        var text=new System.Text.StringBuilder();
        float ceiling=exactHeight?target.y+6f:Math.Max(900f,target.y+200f);
        float range=exactHeight?20f:1800f;
        var hits=Physics.RaycastAll(new Vector3(target.x,ceiling,target.z),Vector3.down,range,_motor.CollidableLayers,QueryTriggerInteraction.Ignore);
        text.Append("rayHits=").Append(hits.Length).Append(" motor=").Append(_motor.TransientPosition.ToString("F2"));
        for(int i=0;i<hits.Length&&i<12;i++)
        {
            var hit=hits[i];var collider=hit.collider;
            if(collider==null)continue;
            text.Append(" | ").Append(collider.name).Append("/").Append(collider.transform.parent?.name)
                .Append(" y=").Append(hit.point.y.ToString("F2")).Append(" normalY=").Append(hit.normal.y.ToString("F2"))
                .Append(" actor=").Append(collider.GetComponentInParent<IdentifiableActor>()!=null)
                .Append(" clear=").Append(ClearCapsule(hit.point+Vector3.up*.12f));
        }
        return text.ToString();
    }
    internal void PlaceForMap(Vector3 position)
    {
        if(_motor==null||!ClearCapsule(position))throw new InvalidOperationException("Место посадки занято.");
        _motor.SetPosition(position,true);_motor.BaseVelocity=Vector3.zero;_lastSafe=position;
        _controller.ResetVelocity(false);
    }

    private bool ClearCapsule(Vector3 feet)
    {
        if (_motor == null) return false;
        _overlaps ??= new Il2CppReferenceArray<Collider>(128);
        float radius = Math.Max(0.2f, _motor.CapsuleRadius) * 0.95f;
        Vector3 bottom = feet + _motor.CharacterTransformToCapsuleBottomHemi;
        Vector3 top = feet + _motor.CharacterTransformToCapsuleTopHemi;
        int hits=_overlaps.Length;
        try
        {
            hits = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, _overlaps, _motor.CollidableLayers, QueryTriggerInteraction.Ignore);
            if (hits >= _overlaps.Length) return false; // Saturated query is not proof of safety.
            for (int i = 0; i < hits; i++)
            {
                Collider hit = _overlaps[i];
                if (hit != null && (_controller == null || !hit.transform.IsChildOf(_controller.transform))) return false;
            }
            return true;
        }
        finally
        {
            // NonAlloc writes only its returned prefix; stale collider wrappers
            // otherwise keep previous streamed regions rooted in this buffer.
            for(int i=0;i<Math.Min(hits,_overlaps.Length);i++)_overlaps[i]=null;
        }
    }

    internal bool IsClearPosition(Vector3 feet) => float.IsFinite(feet.x)&&float.IsFinite(feet.y)&&float.IsFinite(feet.z)&&ClearCapsule(feet);

    private void StopFlight()
    {
        if (!Flight) return;
        Flight = false;
        _flyVelocity = Vector3.zero;
        try
        {
            if (_motor != null && !_motor.WasCollected)
            {
                try
                {
                    if (!ClearCapsule(_motor.TransientPosition)) _motor.SetPosition(_lastSafe, true);
                    _motor.BaseVelocity = Vector3.zero;
                }
                finally
                {
                    // A failed safety query must never leave native collisions disabled.
                    try { _motor.SetCapsuleCollisionsActivation(_oldCapsule); }
                    finally
                    {
                        try { _motor.SetMovementCollisionsSolvingActivation(_oldMovement); }
                        finally
                        {
                            try { _motor.SetGroundSolvingActivation(_oldGround); }
                            finally { _motor.enabled = _oldMotorEnabled; }
                        }
                    }
                }
            }
        }
        finally
        {
            if (_controller != null && !_controller.WasCollected)
            {
                _controller._BypassGravity_k__BackingField = _oldGravity;
                _controller.ResetVelocity(false);
            }
        }
    }

    private void RestoreParameters()
    {
        try
        {
            RestoreJump();
        }
        finally
        {
            try
            {
                if (_clone!=null&&_controller != null && !_controller.WasCollected && _controller._parameters == _clone)
                    _controller._parameters = _original;
            }
            finally
            {
                if(_clone!=null&&!_clone.WasCollected)Object.Destroy(_clone);
                ReleaseBorrowedAsset(_original,_originalFlags,_originalExpectedFlags,ref _originalFlagLease);
                _clone = null;_original = null;
            }
        }
    }

    private static bool HoldBorrowedAsset(Object asset,out HideFlags before,out HideFlags expected)
    {
        // A managed IL2CPP wrapper does not protect a temporarily unreferenced
        // Unity asset from native UnloadUnusedAssets (e.g. a Pedia transition).
        // Lease ONLY the unload flag while our private clone replaces it.
        before=asset.hideFlags;expected=before|HideFlags.DontUnloadUnusedAsset;
        if(before==expected)return false;
        asset.hideFlags=expected;return true;
    }
    private void ReleaseBorrowedAsset(Object asset,HideFlags before,HideFlags expected,ref bool owned)
    {
        if(!owned)return;
        owned=false;
        if(asset==null||asset.WasCollected)return;
        if(asset.hideFlags==expected)asset.hideFlags=before;
        else AssetLeaseRestoreConflictsForTest++; // Never overwrite external flags.
    }

    private void RestoreController()
    {
        try { StopFlight(); } finally { RestoreParameters(); }
    }

    internal void Reset()
    {
        ClearCheckpoint();
        _flightDestination=null;_nextFlightStreaming=0;FlightStreamingStatus="Ожидание полёта";
        GodMode = InfiniteEnergy = HighJump = InfiniteItems = false;
        SpeedMultiplier = 1f;
        JumpHeightMultiplier=2f;PreservedSpends=0;
        EndTransitHold();
        RestoreController();
        _controller = null;
        _motor = null;
        _playerPointer=IntPtr.Zero;_overlaps=null;
    }
}
