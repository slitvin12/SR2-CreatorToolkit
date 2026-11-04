using System;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController.Abilities;

namespace CreatorToolkit;

// 0.8.3: native jump after NoClip. The KCC motor sleeps through the whole flight, so its grounding
// report and the jump ability keep a pre-flight snapshot plus any Space presses buffered while
// flying up. After native physics are restored the stale snapshot is discarded exactly once: the
// next native motor tick re-probes the ground and raises a real "landed" event for the jump.
internal sealed partial class PlayerTools
{
    internal static bool LegacyFlightExitForTest;
    internal int FlightExitRefreshes {get;private set;}
    internal string LastFlightExitIssue {get;private set;}

    internal JumpAbilityBehavior NativeJump()
    {
        var abilities=Controller?.AbilityBehaviors;
        if(abilities==null)return null;
        for(int i=0;i<abilities.Length;i++)
        {
            var found=abilities[i]?.TryCast<JumpAbilityBehavior>();
            if(found!=null&&found.CharacterController==_controller)return found;
        }
        return null;
    }

    private void RefreshGroundingAfterFlight()
    {
        if(LegacyFlightExitForTest||_controller==null||_controller.WasCollected||_motor==null||_motor.WasCollected)return;
        try
        {
            var jump=NativeJump();
            if(jump!=null)
            {
                // Presses made while flying were never consumed by the sleeping motor.
                jump._jumpRequested=false;jump.StartAbilityEventReceived=false;jump.StopAbilityEventReceived=false;
            }
            var grounding=_motor.GroundingStatus;
            grounding.IsStableOnGround=false;grounding.FoundAnyGround=false;grounding.SnappingPrevented=false;
            _motor.GroundingStatus=grounding;
            FlightExitRefreshes++;LastFlightExitIssue=null;
        }
        catch(Exception e)
        {
            LastFlightExitIssue=e.GetType().Name+": "+e.Message;
            MelonLoader.MelonLogger.Warning("Flight exit grounding refresh skipped: "+LastFlightExitIssue);
        }
    }
}
