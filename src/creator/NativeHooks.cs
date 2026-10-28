using System;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.KFC;
using Il2CppMonomiPark.SlimeRancher.Input;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.UnitPropertySystem;
using UnityEngine;

namespace CreatorToolkit;

// Installed explicitly: no automatic PatchAll or reliance on another mod's Harmony instance.
internal static class NativeHooks
{
    private static bool God => ToolkitMod.I != null && ToolkitMod.I.Player.GodMode && ToolkitMod.I.Ready;
    private static bool Energy => ToolkitMod.I != null && ToolkitMod.I.Player.InfiniteEnergy && ToolkitMod.I.Ready;
    internal static int Install(HarmonyLib.Harmony harmony)
    {
        int installed = 0;
        void Hook(Type target, string method, string prefix)
        {
            try
            {
                MethodInfo original = AccessTools.Method(target, method);
                MethodInfo patch = AccessTools.Method(typeof(NativeHooks), prefix);
                if (original == null || patch == null) throw new MissingMethodException(target.FullName, method);
                harmony.Patch(original, prefix: new HarmonyMethod(patch));
                installed++;
            }
            catch (Exception e) { ToolkitMod.I.Fail("patch " + target.Name + "." + method, e); }
        }
        Hook(typeof(PlayerDamageable), "Damage", nameof(DamageGuard));
        Hook(typeof(PlayerState), "Damage", nameof(DamageGuard));
        Hook(typeof(PlayerState), "CanBeDamaged", nameof(DamageGuard));
        Hook(typeof(PlayerModel), "LoseHealth", nameof(HealthGuard));
        Hook(typeof(PlayerDeathHandler), "OnDeath", nameof(HealthGuard));
        Hook(typeof(PlayerState), "SpendEnergy", nameof(EnergyGuard));
        Hook(typeof(PlayerModel), "SpendEnergy", nameof(EnergyGuard));
        Hook(typeof(UnitResource), "set_Value", nameof(EnergyResourceGuard));
        // Never detour ref Vector3 UpdateVelocity: the generated IL2CPP bridge corrupted
        // native velocity to NaN even with flight disabled. Flight uses a suspended motor instead.
        Hook(typeof(TimeDirector), "Update", nameof(ClockGuard));
        Hook(typeof(TimeDirector), "LateUpdate", nameof(ClockGuard));
        Hook(typeof(SubbehaviourPlexer), "UpdateInstance", nameof(SlimeAiGuard));
        Hook(typeof(SubbehaviourPlexer), "FixedUpdateInstance", nameof(SlimeAiGuard));
        // ForceUpdateInputMaps calls the native mode update. Detouring both produced
        // an IL2CPP invoke/reverse-PInvoke recursion in RecoverGameplay (captured crash stack).
        // Keep one guard; unpatched ForceUpdateInputMaps still enters this guarded update.
        Hook(typeof(InputDirector), "UpdateInputMaps", nameof(MenuInputMode));
        Hook(typeof(AmmoSlot),"DecrementAmmo",nameof(VacpackSlotSpend));
        foreach(var signature in new[]{
            (Types:new[]{typeof(int),typeof(int)},Patch:nameof(VacpackIndexSpend)),
            (Types:new[]{typeof(IdentifiableType),typeof(int)},Patch:nameof(VacpackTypeSpend))})
        {
            try
            {
                var original=AccessTools.Method(typeof(AmmoSlotManager),"Decrement",signature.Types);
                var patch=AccessTools.Method(typeof(NativeHooks),signature.Patch);
                if(original==null||patch==null)throw new MissingMethodException("AmmoSlotManager.Decrement exact overload");
                harmony.Patch(original,prefix:new HarmonyMethod(patch));installed++;
            }
            catch(Exception e){ToolkitMod.I.Fail("patch infinite vacpack exact overload",e);}
        }
        // No DrawDecoMeshes detour: native rendering reported NaN bounds before
        // the 0xc0000005 crash. Do not marshal/rewrite its float argument.
        try
        {
            var original=AccessTools.Method(typeof(Il2CppMonomiPark.SlimeRancher.SceneManagement.SceneLoader),"SetTurboLoadEnabled");
            var patch=AccessTools.Method(typeof(NativeHooks),nameof(LoadingBudget));
            if(original==null||patch==null)throw new MissingMethodException("SceneLoader.SetTurboLoadEnabled");
            harmony.Patch(original,postfix:new HarmonyMethod(patch));installed++;
        }
        catch(Exception e){ToolkitMod.I.Fail("patch smooth loading budget",e);}
        return installed;
    }

    private static bool VacpackSlotSpend(AmmoSlot __instance)
    {
        try{return !(ToolkitMod.I?.Player?.PreserveSlotSpend(__instance)??false);}
        catch(Exception e){ToolkitMod.I?.Fail("infinite vacpack slot guard",e);return true;}
    }
    private static bool VacpackIndexSpend(AmmoSlotManager __instance,int __0,int __1)
    {
        try{return !(ToolkitMod.I?.Player?.PreserveManagerSpend(__instance,__0,__1)??false);}
        catch(Exception e){ToolkitMod.I?.Fail("infinite vacpack indexed guard",e);return true;}
    }
    private static bool VacpackTypeSpend(AmmoSlotManager __instance,IdentifiableType __0,int __1)
    {
        try{return !(ToolkitMod.I?.Player?.PreserveManagerSpend(__instance,__0,__1)??false);}
        catch(Exception e){ToolkitMod.I?.Fail("infinite vacpack type guard",e);return true;}
    }

    // Loading hook has a primitive bool only; rendering remains unpatched.
    private static void LoadingBudget(Il2CppMonomiPark.SlimeRancher.SceneManagement.SceneLoader __instance,bool __0)
    {
        try{ToolkitMod.I?.Optimization?.AfterTurboLoad(__instance,__0);}
        catch(Exception e){ToolkitMod.I?.Fail("smooth native loading budget",e);}
    }

    private static void MenuInputMode(ref bool __0)
    {
        if (ToolkitMod.I != null && ToolkitMod.I.MenuOpen && !NativeUiPriority.HasNativeFocus) __0 = true;
    }

    private static bool DamageGuard(ref bool __result)
    {
        if (!God) return true;
        __result = false;
        return false;
    }
    private static bool HealthGuard() => !God;
    private static bool EnergyGuard() => !Energy;

    private static void EnergyResourceGuard(UnitResource __instance, ref float __0)
    {
        if (!Energy) return;
        try
        {
            var player = ToolkitMod.I.Context.PlayerState;
            UnitResource resource = player._model.energyResource;
            if (resource != null && resource.Pointer == __instance.Pointer) __0 = Math.Max(__0, player.GetMaxEnergy());
        }
        catch (Exception e) { ToolkitMod.I.Fail("energy resource", e); }
    }

    private static bool ClockGuard()
        => ToolkitMod.I == null || !ToolkitMod.I.World.FreezeTime || !ToolkitMod.I.Ready;

    private static bool SlimeAiGuard(SubbehaviourPlexer __instance)
    {
        var app = ToolkitMod.I;
        return app == null || !app.World.FreezeMobs || !app.Ready || __instance == null || __instance._emotions == null;
    }
}
