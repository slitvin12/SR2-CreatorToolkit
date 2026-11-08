using System;
using Il2Cpp;
using UnityEngine;

namespace CreatorToolkit;

// 0.8.7: teleports and NoClip exits no longer land the player inside level geometry.
// ClearCapsule (OverlapCapsule) reports only triangles that cross the capsule, so a capsule lying
// wholly inside a concave MeshCollider (a rock, a trunk, a cliff hull) crossed nothing and looked
// clear: the transit placed the player "in the textures", where grounding finds no floor and the
// jump never re-arms. From inside a closed hull the nearest surface along a ray is that hull's own
// back face: a back-face query sees it, an ordinary query does not. Enclosed = back faces around
// the body (and above the head). Convex colliders are still caught by ClearCapsule.
internal sealed partial class PlayerTools
{
    private static readonly Vector3[] EnclosureSides=
    {
        Vector3.forward,Vector3.right,Vector3.back,Vector3.left,
        new Vector3(.7071f,0f,.7071f),new Vector3(.7071f,0f,-.7071f),new Vector3(-.7071f,0f,-.7071f),new Vector3(-.7071f,0f,.7071f),
    };
    private bool _enclosureUnavailable;
    internal int EnclosedRejections {get;private set;}
    internal int TransitExitRefreshes {get;private set;}

    internal bool Enclosed(Vector3 feet)
    {
        if(_enclosureUnavailable||_motor==null||_motor.WasCollected||_controller==null||_controller.WasCollected)return false;
        if(!float.IsFinite(feet.x)||!float.IsFinite(feet.y)||!float.IsFinite(feet.z))return true;
        bool before;
        try{before=Physics.queriesHitBackfaces;}
        catch(Exception e){DisableEnclosure(e);return false;}
        try
        {
            Vector3 center=feet+(_motor.CharacterTransformToCapsuleBottomHemi+_motor.CharacterTransformToCapsuleTopHemi)*.5f;
            Vector3 head=feet+_motor.CharacterTransformToCapsuleTopHemi;
            bool above=BackFaceFirst(head,Vector3.up,300f);
            int sides=0;
            for(int i=0;i<EnclosureSides.Length;i++)if(BackFaceFirst(center,EnclosureSides[i],150f))sides++;
            bool enclosed=sides>=6||(above&&sides>=3);
            if(enclosed)EnclosedRejections++;
            return enclosed;
        }
        catch(Exception e){DisableEnclosure(e);return false;}
        finally{try{Physics.queriesHitBackfaces=before;}catch{}}
    }

    private void DisableEnclosure(Exception e)
    {
        _enclosureUnavailable=true;
        MelonLoader.MelonLogger.Warning("Enclosure probe unavailable, capsule overlap only: "+e.GetType().Name+": "+e.Message);
    }

    // The first solid surface along the ray shows only with back faces on: we are inside its hull.
    private bool BackFaceFirst(Vector3 origin,Vector3 direction,float range)
    {
        Physics.queriesHitBackfaces=true;
        if(!NearestSolid(origin,direction,range,out float any))return false;
        Physics.queriesHitBackfaces=false;
        return !NearestSolid(origin,direction,any+.05f,out float front)||front>any+.02f;
    }

    private bool NearestSolid(Vector3 origin,Vector3 direction,float range,out float distance)
    {
        distance=float.MaxValue;
        var hits=Physics.RaycastAll(origin,direction,range,_motor.CollidableLayers,QueryTriggerInteraction.Ignore);
        for(int i=0;i<hits.Length;i++)
        {
            var hit=hits[i];var collider=hit.collider;
            if(collider==null||hit.distance>=distance||collider.transform.IsChildOf(_controller.transform))continue;
            if(collider.GetComponentInParent<IdentifiableActor>()!=null)continue; // slimes and items are never a hull
            distance=hit.distance;
        }
        return distance<float.MaxValue;
    }

    // Liquids and the slime sea: never a landing or a Gordo floor (the names TryMapGround skips).
    internal static bool HazardSurface(Collider collider)
    {
        if(collider==null)return true;
        string name=(collider.name+" "+collider.transform.parent?.name).ToLowerInvariant();
        return name.Contains("water")||name.Contains("slimesea")||name.Contains("lava");
    }

    // Actors may land in water; the slime sea and lava destroy them.
    internal static bool DeadlySurface(Collider collider)
    {
        if(collider==null)return true;
        string name=(collider.name+" "+collider.transform.parent?.name).ToLowerInvariant();
        return name.Contains("slimesea")||name.Contains("lava");
    }

    // Placement mode: the player's own colliders and the layers the walking motor collides with.
    internal Transform PlacementRoot=>_controller!=null&&!_controller.WasCollected?_controller.transform:null;
    internal int PlacementLayers=>_motor!=null&&!_motor.WasCollected?_motor.CollidableLayers.value:0;

    // 0.8.7: the safe-transit hold sleeps the motor like NoClip (motor off, gravity bypassed,
    // abilities stopped), so the landing kept the pre-teleport grounding report and a buffered
    // jump: the first jumps after a teleport were ignored. The same one-time discard as the 0.8.3
    // flight exit; the next native motor tick probes the new ground and re-arms the jump.
    private void RefreshGroundingAfterTransit()
    {
        if(_controller==null||_controller.WasCollected||_motor==null||_motor.WasCollected)return;
        try
        {
            var jump=NativeJump();
            if(jump!=null){jump._jumpRequested=false;jump.StartAbilityEventReceived=false;jump.StopAbilityEventReceived=false;}
            var grounding=_motor.GroundingStatus;
            grounding.IsStableOnGround=false;grounding.FoundAnyGround=false;grounding.SnappingPrevented=false;
            _motor.GroundingStatus=grounding;
            TransitExitRefreshes++;
        }
        catch(Exception e){MelonLoader.MelonLogger.Warning("Transit exit grounding refresh skipped: "+e.GetType().Name+": "+e.Message);}
    }
}
