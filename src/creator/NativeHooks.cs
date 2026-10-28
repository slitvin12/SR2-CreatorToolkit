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
// Coexistence (CoexistencePolicy): every hook runs last (Priority.Last), is tracked by
// HookCoexistence and never throws into the shared call chain — a fault takes the vanilla path.
internal static class NativeHooks
{
    private static bool God => ToolkitMod.I != null && ToolkitMod.I.Player.GodMode && ToolkitMod.I.Ready;
    private static bool Energy => ToolkitMod.I != null && ToolkitMod.I.Player.InfiniteEnergy && ToolkitMod.I.Ready;
    internal static int Install(HarmonyLib.Harmony harmony)
    {
        int installed = 0;
        void Hook(Type target, string method, string prefix)
        {
            MethodInfo original = null, patch = null;
            try
            {
                original = AccessTools.Method(target, method);
                patch = AccessTools.Method(typeof(NativeHooks), prefix);
                if (original == null || patch == null) throw new MissingMethodException(target.FullName, method);
                harmony.Patch(original, prefix: new HarmonyMethod(patch, HarmonyLib.Priority.Last));
                HookCoexistence.Track(original, patch);
                installed++;
            }
            catch (Exception e) { HookCoexistence.InstallFailed(original, patch, e); ToolkitMod.I.Fail("patch " + target.Name + "." + method, e); }
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
            MethodInfo original=null,patch=null;
            try
            {
                original=AccessTools.Method(typeof(AmmoSlotManager),"Decrement",signature.Types);
                patch=AccessTools.Method(typeof(NativeHooks),signature.Patch);
                if(original==null||patch==null)throw new MissingMethodException("AmmoSlotManager.Decrement exact overload");
                harmony.Patch(original,prefix:new HarmonyMethod(patch,HarmonyLib.Priority.Last));HookCoexistence.Track(original,patch);installed++;
            }
            catch(Exception e){HookCoexistence.InstallFailed(original,patch,e);ToolkitMod.I.Fail("patch infinite vacpack exact overload",e);}
        }
        // No DrawDecoMeshes detour: native rendering reported NaN bounds before
        // the 0xc0000005 crash. Do not marshal/rewrite its float argument.
        MethodInfo loader=null,budget=null;
        try
        {
            loader=AccessTools.Method(typeof(Il2CppMonomiPark.SlimeRancher.SceneManagement.SceneLoader),"SetTurboLoadEnabled");
            budget=AccessTools.Method(typeof(NativeHooks),nameof(LoadingBudget));
            if(loader==null||budget==null)throw new MissingMethodException("SceneLoader.SetTurboLoadEnabled");
            harmony.Patch(loader,postfix:new HarmonyMethod(budget,HarmonyLib.Priority.Last));HookCoexistence.Track(loader,budget);installed++;
        }
        catch(Exception e){HookCoexistence.InstallFailed(loader,budget,e);ToolkitMod.I.Fail("patch smooth loading budget",e);}
        return installed;
    }

    private static bool VacpackSlotSpend(AmmoSlot __instance)
    {
        try{return !(ToolkitMod.I?.Player?.PreserveSlotSpend(__instance)??false);}
        catch(Exception e){return HookCoexistence.Fault(nameof(VacpackSlotSpend),e);}
    }
    private static bool VacpackIndexSpend(AmmoSlotManager __instance,int __0,int __1)
    {
        try{return !(ToolkitMod.I?.Player?.PreserveManagerSpend(__instance,__0,__1)??false);}
        catch(Exception e){return HookCoexistence.Fault(nameof(VacpackIndexSpend),e);}
    }
    private static bool VacpackTypeSpend(AmmoSlotManager __instance,IdentifiableType __0,int __1)
    {
        try{return !(ToolkitMod.I?.Player?.PreserveManagerSpend(__instance,__0,__1)??false);}
        catch(Exception e){return HookCoexistence.Fault(nameof(VacpackTypeSpend),e);}
    }

    // Loading hook has a primitive bool only; rendering remains unpatched.
    private static void LoadingBudget(Il2CppMonomiPark.SlimeRancher.SceneManagement.SceneLoader __instance,bool __0)
    {
        try{ToolkitMod.I?.Optimization?.AfterTurboLoad(__instance,__0);}
        catch(Exception e){HookCoexistence.Fault(nameof(LoadingBudget),e);}
    }

    private static void MenuInputMode(ref bool __0)
    {
        try { if (ToolkitMod.I != null && ToolkitMod.I.MenuOpen && !NativeUiPriority.HasNativeFocus) __0 = true; }
        catch (Exception e) { HookCoexistence.Fault(nameof(MenuInputMode), e); }
    }

    private static bool DamageGuard(ref bool __result)
    {
        try
        {
            if (!God) return true;
            __result = false;
            return false;
        }
        catch (Exception e) { return HookCoexistence.Fault(nameof(DamageGuard), e); }
    }
    private static bool HealthGuard()
    {
        try { return !God; }
        catch (Exception e) { return HookCoexistence.Fault(nameof(HealthGuard), e); }
    }
    private static bool EnergyGuard()
    {
        try { return !Energy; }
        catch (Exception e) { return HookCoexistence.Fault(nameof(EnergyGuard), e); }
    }

    private static void EnergyResourceGuard(UnitResource __instance, ref float __0)
    {
        if (!Energy) return;
        try
        {
            var player = ToolkitMod.I.Context.PlayerState;
            UnitResource resource = player._model.energyResource;
            if (resource != null && resource.Pointer == __instance.Pointer) __0 = Math.Max(__0, player.GetMaxEnergy());
        }
        catch (Exception e) { HookCoexistence.Fault(nameof(EnergyResourceGuard), e); }
    }

    private static bool ClockGuard()
    {
        try { return ToolkitMod.I == null || !ToolkitMod.I.World.FreezeTime || !ToolkitMod.I.Ready; }
        catch (Exception e) { return HookCoexistence.Fault(nameof(ClockGuard), e); }
    }

    private static bool SlimeAiGuard(SubbehaviourPlexer __instance)
    {
        try
        {
            var app = ToolkitMod.I;
            return app == null || !app.World.FreezeMobs || !app.Ready || __instance == null || __instance._emotions == null;
        }
        catch (Exception e) { return HookCoexistence.Fault(nameof(SlimeAiGuard), e); }
    }
}
