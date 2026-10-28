using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2Cpp;
using Il2CppMonomiPark.KFC;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController.Abilities;
using UnityEngine;

namespace CreatorToolkit;

internal sealed partial class FeatureSuite
{
    private CharacterControllerParameters _loNativeParameters;
    private JumpAbilityData _loNativeJump;
    private float _loGroundSpeed,_loAirSpeed,_loGravity,_loJumpSpeed,_loJumpHold,_loJumpDelta,_loJumpVelocity;
    private Vector3 _loJumpOrigin;
    private ActorTools.Entry _loAmmoEntry;
    private int _loPreservedCount;
    private AsyncOperation _loUnusedUnload;
    private HideFlags _loParameterFlags,_loJumpFlags;
    private JumpAbilityBehavior ActualJump()
    {
        var behaviors=Controller.AbilityBehaviors;
        if(behaviors!=null)for(int i=0;i<behaviors.Length;i++)
        {
            var jump=behaviors[i]?.TryCast<JumpAbilityBehavior>();
            if(jump!=null&&jump.CharacterController==Controller)return jump;
        }
        throw new InvalidOperationException("Actual native jump behavior unavailable.");
    }
    private void RetireLoFixtures()
    {_loNativeParameters=null;_loNativeJump=null;_loAmmoEntry=null;_loJumpOrigin=default;_loUnusedUnload=null;}
    private void BuildLoPlayerSteps()
    {
        Add("LO Player / defaults off and original native ability snapshot",()=>{
            GuardCopy();var p=_app.Player;
            Require(!p.HighJump&&!p.InfiniteItems&&p.JumpAssetRoots==0,
                "A new player cheat or private jump asset remained armed without opt-in.");
            p.SpeedMultiplier=1;p.Tick();
            _loNativeParameters=Controller._parameters;_loNativeJump=ActualJump().AbilityData;
            Require(_loNativeParameters!=null&&_loNativeJump!=null,"Native player baseline missing.");
            _loGroundSpeed=_loNativeParameters._maxGroundedMoveSpeed;
            _loAirSpeed=_loNativeParameters._maxAirMoveSpeed;_loGravity=_loNativeParameters._gravityMagnitude;
            _loJumpSpeed=_loNativeJump.JumpUpSpeed;_loJumpHold=_loNativeJump.InitialHoldVelocity;
            _loParameterFlags=_loNativeParameters.hideFlags;_loJumpFlags=_loNativeJump.hideFlags;
        });
        Add("LO Player / speed ten uses only private controller parameters",()=>{
            GuardCopy();var p=_app.Player;p.SpeedMultiplier=10;p.Tick();
            Require(Controller._parameters!=_loNativeParameters&&
                Math.Abs(Controller._parameters._maxGroundedMoveSpeed-_loGroundSpeed*10)<.001f&&
                Math.Abs(Controller._parameters._maxAirMoveSpeed-_loAirSpeed*10)<.001f,
                "Native speed ten did not reach the privately cloned ground/air parameters.");
            Require(_loNativeParameters._maxGroundedMoveSpeed==_loGroundSpeed&&
                _loNativeParameters._maxAirMoveSpeed==_loAirSpeed&&
                Controller._parameters._gravityMagnitude==_loGravity,
                "Speed ten mutated the shared asset or global/player gravity.");
        });
        Add("LO Player / high jump owns native data without a velocity detour",()=>{
            GuardCopy();var p=_app.Player;p.SpeedMultiplier=1;p.HighJump=true;p.JumpHeightMultiplier=4;p.Tick();
            var actual=ActualJump().AbilityData;
            Require(p.PrivateJumpForTest&&actual!=_loNativeJump&&Math.Abs(actual.JumpUpSpeed-_loJumpSpeed*2)<.001f&&
                Math.Abs(actual.InitialHoldVelocity-_loJumpHold*2)<.001f,"Native jump behavior does not read its private doubled-velocity data.");
            Require(_loNativeJump.JumpUpSpeed==_loJumpSpeed&&_loNativeJump.InitialHoldVelocity==_loJumpHold&&
                Controller._parameters._gravityMagnitude==_loGravity&&Controller.Motor._solveMovementCollisions&&
                Controller.Motor._solveGrounding&&Controller.Motor.Capsule.enabled,
                "High jump mutated a shared ability/gravity or disabled collision/grounding.");
        });
        Add("LO Player / unload unused native assets with private jump active",()=>{
            GuardCopy();_loUnusedUnload=Resources.UnloadUnusedAssets();
        },.12f);
        Add("LO Player / borrowed originals survive native asset unload",()=>{
            GuardCopy();
            Require(_loNativeParameters!=null&&_loNativeJump!=null&&_app.Player.PrivateJumpForTest&&
                _loNativeJump.JumpUpSpeed==_loJumpSpeed&&_loNativeParameters._maxGroundedMoveSpeed==_loGroundSpeed,
                "A borrowed original asset was unloaded or changed while private parameters were active.");
            _loUnusedUnload=null;
        },.12f,()=>_loUnusedUnload!=null&&_loUnusedUnload.isDone,15f);
        Add("LO Player / native jump input on actual ground",()=>{
            GuardCopy();_app.CloseMenu();_loJumpOrigin=Controller.Motor.TransientPosition;
            NativeKeyboardProbe.PressAuxiliary(UnityEngine.InputSystem.Keyboard.current.spaceKey);
        },.18f,()=>Controller.IsGrounded,8f);
        Add("LO Player / actual upward native motor movement",()=>{
            GuardCopy();NativeKeyboardProbe.ReleaseAuxiliary();
            _loJumpDelta=Controller.Motor.TransientPosition.y-_loJumpOrigin.y;
            _loJumpVelocity=Controller.Motor.BaseVelocity.y;
            Require(_loJumpDelta>.03f&&float.IsFinite(_loJumpVelocity)&&!_app.Player.Flight&&
                Controller.Motor.enabled&&Controller.Motor._solveMovementCollisions,
                "High jump produced no real finite upward motion through the native motor.");
            _app.Player.HighJump=false;_app.Player.Tick();
        });
        Add("LO Player / original jump and collision-safe native landing restored",()=>{
            GuardCopy();
            Require(ActualJump().AbilityData==_loNativeJump&&Controller._parameters==_loNativeParameters&&
                _loNativeJump.JumpUpSpeed==_loJumpSpeed&&_loNativeJump.InitialHoldVelocity==_loJumpHold&&
                _loNativeParameters.hideFlags==_loParameterFlags&&_loNativeJump.hideFlags==_loJumpFlags&&
                _app.Player.JumpAssetRoots==0&&_app.Player.BorrowedAssetLeaseChangesForTest==0&&
                _app.Player.AssetLeaseRestoreConflictsForTest==0&&
                Controller.Motor._solveMovementCollisions&&Controller.Motor._solveGrounding,
                "Original behavior data/parameters or native landing were not restored.");
        },.12f,()=>Controller.IsGrounded,12f);
        Add("LO Vacpack / ordinary native pop still spends one",()=>{
            GuardCopy();_loAmmoEntry=_app.Actors.Catalog.First(e=>e.Reference=="SlimeDefinition.Pink"&&_app.Actors.CanStore(e,0));
            _app.Actors.FillAmmoForTest(_loAmmoEntry,0);
            var ammo=_app.Context.PlayerState.Ammo;ammo.SetSelectedSlot(0);ammo.Slots[0].Count=3;
            _app.Player.InfiniteItems=false;var popped=ammo.PopFromSelectedSlot();
            Require(popped?.Id==_loAmmoEntry.Type&&ammo.GetSlotCount(0)==2,"Default-off native pop did not spend exactly one.");
        });
        Add("LO Vacpack / metadata pop preserves a real existing item",()=>{
            GuardCopy();var ammo=_app.Context.PlayerState.Ammo;var slot=ammo.Slots[0];
            _app.Player.InfiniteItems=true;var before=slot.Metadata;int count=slot.Count;
            var popped=ammo.PopFromSelectedSlot();
            Require(popped?.Id==_loAmmoEntry.Type&&slot.Count==count&&slot.Id==_loAmmoEntry.Type&&
                slot.Metadata.Matches(before),"Infinite native pop changed quantity, item or its native metadata.");
        });
        Add("LO Vacpack / native shot overload preserves the last item",()=>{
            GuardCopy();var ammo=_app.Context.PlayerState.Ammo;ammo.Slots[0].Count=1;
            ammo.PopFromSelectedSlot(out var type,out var appearance);
            Require(type==_loAmmoEntry.Type&&ammo.GetSlotCount(0)==1&&ammo.GetSlotName(0)==type,
                "Native shot pop consumed/cleared the last item while infinite vacpack was enabled.");
        });
        Add("LO Vacpack / indexed and typed native spending preserve quantity",()=>{
            GuardCopy();var ammo=_app.Context.PlayerState.Ammo;ammo.Slots[0].Count=3;
            ammo.Decrement(0,2);Require(ammo.GetSlotCount(0)==3,"Indexed native spending consumed infinite ammo.");
            ammo.Decrement(_loAmmoEntry.Type,2);Require(ammo.GetSlotCount(0)==3,"Typed native spending consumed infinite ammo.");
            Require(!_app.Player.PreserveManagerSpend(null,0,1)&&!_app.Player.PreserveManagerSpend(ammo,0,4)&&
                !_app.Player.PreserveManagerSpend(ammo,0,0),
                "Infinite vacpack bypassed a foreign/missing, insufficient or invalid spend.");
            _loPreservedCount=_app.Player.PreservedSpends;
            Require(_loPreservedCount>=4,"No real native consumption hook executed.");
        });
        Add("LO Vacpack / explicit clear is not intercepted",()=>{
            GuardCopy();var ammo=_app.Context.PlayerState.Ammo;ammo.Clear(0);
            Require(ammo.GetSlotCount(0)==0&&ammo.GetSlotName(0)==null,"Infinite mode blocked an explicit inventory clear.");
            Require(!_app.Player.PreserveSlotSpend(ammo.Slots[0]),"Infinite mode manufactures an item in an empty slot.");
        });
        Add("LO Vacpack / disabling restores last-item native consumption",()=>{
            GuardCopy();var ammo=_app.Context.PlayerState.Ammo;
            _app.Player.InfiniteItems=false;_app.Actors.FillAmmoForTest(_loAmmoEntry,0);ammo.SetSelectedSlot(0);ammo.Slots[0].Count=1;
            var popped=ammo.PopFromSelectedSlot();
            Require(popped?.Id==_loAmmoEntry.Type&&ammo.GetSlotCount(0)==0&&ammo.GetSlotName(0)==null,
                "Disabling infinite vacpack did not restore native empty-slot semantics.");
        });
        Add("LO Player / reset releases all new cheats and private assets",()=>{
            GuardCopy();_app.Player.Reset();_app.Player.Tick();
            Require(!_app.Player.HighJump&&!_app.Player.InfiniteItems&&_app.Player.SpeedMultiplier==1&&
                _app.Player.JumpAssetRoots==0&&_app.UiForTests.TextListenerRootsForTest==0,
                "Reset retained a new cheat, text listener or private jump asset.");
            File.WriteAllText(Path.Combine(_app.DataDirectory,"lo-player-proof.json"),JsonSerializer.Serialize(new{
                Version=ToolkitMod.Version,Scope="Owned-copy native controller/ability/input/AmmoSlot APIs; not physical OS shooting",
                NativeGroundSpeedBefore=_loGroundSpeed,NativeAirSpeedBefore=_loAirSpeed,
                NativeGravityUnchanged=_loGravity,NativeJumpLaunchBefore=_loJumpSpeed,
                NativeJumpHoldBefore=_loJumpHold,ActualJumpDisplacementY=_loJumpDelta,
                ActualJumpMotorVelocityY=_loJumpVelocity,NativePreservedSpendCallbacks=_loPreservedCount,
                SharedNativeAssetsUnchanged=true,PrivateJumpAssetRootsAfterReset=_app.Player.JumpAssetRoots,
                RefVector3DetourUsed=false,EmptySlotFilled=false
            },new JsonSerializerOptions{WriteIndented=true}));
            RetireLoFixtures();_app.Player.GodMode=_app.Player.InfiniteEnergy=true;
        });
    }
}