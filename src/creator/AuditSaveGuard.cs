using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Persist;
using NativeStream=Il2CppSystem.IO.MemoryStream;
using NativeStrings=Il2CppSystem.Collections.Generic.List<string>;

namespace CreatorToolkit;

// Process-lifetime audit guard. It deliberately does not depend on ToolkitMod.I:
// native quit callbacks can run after Melon's cleanup has set that field null.
// No prefix writes a file, restores a .sav, or invokes another save operation.
internal static class AuditSaveGuard
{
    internal const int RequiredHookCount=13;
    // Deliberately hard-block native audits in this staged safety revision.
    // Missing/ineffective optional-mod hooks still need an independently
    // reviewed startup/write-denial boundary; build-only tests cannot prove it.
    internal static bool NativeAuditExecutionEnabled=>false;
    private static volatile bool _required,_hooksReady,_closing,_readOnlyProbe;
    private static string _directory="",_ownedCopy="",_baselineSha="",_fault="",_runId="";
    private static AuditImmutableBaseline _baseline;
    private static int _installed,_blocked;
    private static readonly object EvidenceLock=new();
    private static readonly List<string> RecentBlocks=new();
    private static string _lastVerifiedUtc="";
    private static bool? _initialAutosaveDisabled;
    internal static bool Required=>_required;
    internal static bool Ready=>_required&&NativeAuditExecutionEnabled&&_hooksReady&&_baseline!=null&&_fault.Length==0;
    internal static string MainName=>_baseline?.MainName??"";
    internal static string MainSave=>_baseline?.MainSave??"";
    internal static string BaselineSha256=>_baselineSha;
    internal static bool? InitialAutosaveDisabled=>_initialAutosaveDisabled;
    internal static string OwnedCopy=>Volatile.Read(ref _ownedCopy);
    internal static string RunId=>Volatile.Read(ref _runId);
    internal static bool Closing=>_closing;

    internal static void Configure(string directory)
    {
        _directory=directory;
        if(File.Exists(Path.Combine(directory,"functional.request"))||
            AuditCheckpointStore.Exists(Path.Combine(directory,"functional-recovery.json")))Arm();
    }
    internal static void Arm()
    {
        if(_required){RequireReady();return;}
        _required=true; // Remains true even if parsing fails: native writes deny.
        try
        {
            string path=Path.Combine(_directory,"audit-immutable-baseline.json");
            _baseline=AuditImmutableBaseline.Read(path);
            _baselineSha=AuditImmutableBaseline.Sha256(path);
            string pin=File.ReadAllText(Path.Combine(_directory,"audit-immutable-baseline.sha256")).Trim();
            if(!AuditImmutableBaseline.Digest(pin)||pin!=_baselineSha)
                throw new InvalidDataException("Approved immutable baseline SHA pin is absent or differs; no automatic replacement is allowed.");
        }
        catch(Exception e){_fault=e.Message;}
    }
    internal static void RequireReady()
    {
        if(!Ready)throw new InvalidOperationException("Audit game writes remain blocked; no audit boot: "+
            (_fault.Length>0?_fault:!NativeAuditExecutionEnabled?
                "native execution is intentionally disabled pending independent startup/write-denial review":
                "all exact native save hooks and an approved immutable baseline are required."));
        if(_closing)throw new InvalidOperationException("The protected process is closing.");
    }
    internal static void FailClosed(string reason)
    {
        _required=true;_fault=reason;
    }
    internal static void RequireNativePath(string path)
    {
        RequireReady();
        if(!AuditImmutableBaseline.SamePath(path,_baseline.SavePath))
            throw new InvalidOperationException("Native local save provider differs from the approved immutable baseline.");
    }
    internal static Dictionary<string,string> OriginalHashes()
    {
        RequireReady();return new Dictionary<string,string>(_baseline.OriginalHashes,StringComparer.OrdinalIgnoreCase);
    }
    internal static void RegisterOwnedCopy(string mainName,string name,string baselineSha,string runId)
    {
        RequireReady();
        _baseline.ValidateOwnedCopy(name);
        if(mainName!=MainName||baselineSha!=_baselineSha||!Guid.TryParseExact(runId,"N",out _)||
            RunId.Length>0&&RunId!=runId||
            OwnedCopy.Length>0&&OwnedCopy!=name)
            throw new InvalidOperationException("Owned copy / immutable baseline binding is invalid.");
        Volatile.Write(ref _ownedCopy,name);
        Volatile.Write(ref _runId,runId);
    }
    internal static void ValidateRecovery(string mainName,string mainSave,string savePath,string baselineSha,
        Dictionary<string,string> hashes,string runId)
    {
        RequireNativePath(savePath);
        if(mainName!=MainName||mainSave!=MainSave||baselineSha!=_baselineSha||!Guid.TryParseExact(runId,"N",out _)||hashes==null||
            hashes.Count!=_baseline.OriginalHashes.Count||
            _baseline.OriginalHashes.Any(p=>!hashes.TryGetValue(p.Key,out string value)||value!=p.Value))
            throw new InvalidOperationException("Recovery cannot redefine the cross-run immutable originals.");
    }
    internal static void VerifyOriginals(bool requireCopyRetired=false)
    {
        RequireReady();
        string path=Path.Combine(_directory,"audit-immutable-baseline.json");
        if(AuditImmutableBaseline.Sha256(path)!=_baselineSha||
            File.ReadAllText(Path.Combine(_directory,"audit-immutable-baseline.sha256")).Trim()!=_baselineSha)
            throw new InvalidOperationException("The approved immutable baseline file changed during this process.");
        _baseline.Verify(requireCopyRetired?"":OwnedCopy);
        _lastVerifiedUtc=DateTime.UtcNow.ToString("o");
    }
    internal static void Tick()
    {
        if(!_required)return;
        var save=GameContext.Instance?.AutoSaveDirector;
        if(save==null||save.WasCollected)return;
        if(!_initialAutosaveDisabled.HasValue)_initialAutosaveDisabled=save._autosaveDisabled;
        // Opt-in copy checkpoints temporarily enable native serialization
        // synchronously, then restore this flag before another game frame.
        // No per-frame boxed Nullable/GameMetadata allocation is needed here.
        save.SetAutosaveDisabled(true);
    }
    private static bool CanWriteCurrent(AutoSaveDirector save)
    {
        if(!_required)return true;
        if(!Ready||_closing||_readOnlyProbe)return false;
        try
        {
            var meta=NativeSaveMetadata.Read(save);
            return meta!=null&&meta.HasValue&&AuditSavePolicy.GameWrite(true,Ready,_closing,_readOnlyProbe,
                OwnedCopy,meta.Value.GameName,meta.Value.DisplayName)&&
                ProviderPath(save._storageProvider?.TryCast<FileStorageProvider>());
        }
        catch{return false;}
    }
    private static bool Allow(bool allowed,string operation,string target="")
    {
        if(allowed)return true;
        Interlocked.Increment(ref _blocked);
        lock(EvidenceLock)
        {
            if(RecentBlocks.Count>=24)RecentBlocks.RemoveAt(0);
            RecentBlocks.Add(operation+(target.Length>0?": "+target:""));
        }
        return false;
    }
    internal static object Evidence()
    {
        string[] recent;lock(EvidenceLock)recent=RecentBlocks.ToArray();
        return new
        {
            ProtectionRequired=_required,ProtectionReady=Ready,ProtectionRemainsArmed=_required,NativeAuditExecutionEnabled,
            Closing=_closing,ReadOnlyCopyProbe=_readOnlyProbe,InstalledSaveHooks=_installed,
            ExpectedSaveHooks=RequiredHookCount,MainName,MainSave,OwnedCopy,RunId,
            ImmutableBaselineSha256=_baselineSha,OriginalHashFiles=_baseline?.OriginalHashes.Count??0,
            LastOriginalVerificationUtc=_lastVerifiedUtc,BlockedNativeOperations=Volatile.Read(ref _blocked),
            InitialAutosaveDisabled=_initialAutosaveDisabled,Fault=_fault,RecentBlockedOperations=recent
        };
    }
    internal static void WriteEvidence(string fileName)
    {
        if(!_required)return;
        File.WriteAllText(Path.Combine(_directory,fileName),JsonSerializer.Serialize(Evidence()));
    }
    internal static void PrepareProtectedQuit(string reason,bool copyRetired)
    {
        VerifyOriginals(copyRetired);
        var evidence=Evidence();
        File.WriteAllText(Path.Combine(_directory,"audit-no-save-exit-request.json"),JsonSerializer.Serialize(new
        {
            RequestedUtc=DateTime.UtcNow.ToString("o"),Reason=reason,BeforeProcessExit=true,RunId,
            SaveAndQuitCommandSent=false,NativeSaveAllNowInvoked=false,NativeSaveGameAndFlushInvoked=false,
            OriginalBytesVerified=true,CopyRetired=copyRetired,PostExitHashVerificationRequired=true,Protection=evidence
        }));
        _closing=true; // Before Application.Quit and all native shutdown callbacks.
    }
    internal static void OnHostQuit()
    {
        if(!_required)return;
        _closing=true; // No dependency on callback ordering or ToolkitMod.I.
        try{WriteEvidence("audit-save-guard-at-quit.json");}catch{/* Keep protection armed even if diagnostics fail. */}
    }
    // Only the isolated-copy fixture may use this. It tests real prefixes by
    // refusing its OWN copy's writes, never by attempting a protected-main write.
    internal static void SetReadOnlyCopyProbe(bool enabled)
    {
        RequireReady();_readOnlyProbe=enabled;
    }
    internal static int BlockedOperations=>Volatile.Read(ref _blocked);

    internal static int Install(HarmonyLib.Harmony harmony)
    {
        int installed=0;
        void Hook(Type type,string name,Type[] args,string prefix)
        {
            try
            {
                MethodInfo original=AccessTools.Method(type,name,args);
                MethodInfo patch=AccessTools.Method(typeof(AuditSaveGuard),prefix);
                if(original==null||patch==null)throw new MissingMethodException(type.FullName,name);
                harmony.Patch(original,prefix:new HarmonyMethod(patch));installed++;
            }
            catch(Exception e)
            {
                _fault="Exact native save hook unavailable: "+type.Name+"."+name+": "+e.Message;
                ToolkitMod.I?.Fail("audit immutable save guard",e);
            }
        }
        var director=typeof(AutoSaveDirector);
        var change=typeof(AutoSaveDirector.ModifySaveGameDelegate);
        Hook(director,"SaveIfInGameplay",Type.EmptyTypes,nameof(CurrentVoid));
        Hook(director,"SaveAllNow",new[]{typeof(bool),change},nameof(CurrentBool));
        Hook(director,"SaveGameAndFlush",Type.EmptyTypes,nameof(CurrentVoid));
        Hook(director,"SaveGame_GetSummary",new[]{typeof(bool),change},nameof(CurrentSummary));
        Hook(director,"SaveGame",new[]{typeof(bool),change},nameof(CurrentVoid));
        Hook(director,"SaveGameImpl",new[]{typeof(bool),change},nameof(CurrentGame));
        Hook(director,"CleanupAutosaves",new[]{typeof(string)},nameof(NamedMutation));
        Hook(director,"DeleteGame",new[]{typeof(string)},nameof(NamedMutation));
        Hook(director,"OnApplicationQuit",Type.EmptyTypes,nameof(NativeQuit));
        // Do not detour the tiny flag setter. The process tick retains native
        // autosave suspension, while all real game writers/deleters deny main
        // writes regardless of that flag or any caller's ignore-disable option.
        var provider=typeof(FileStorageProvider);
        Hook(provider,"StoreGameData",new[]{typeof(string),typeof(string),typeof(NativeStream)},nameof(StoreGame));
        Hook(provider,"DeleteGameData",new[]{typeof(string)},nameof(DeleteFile));
        Hook(provider,"DeleteGamesData",new[]{typeof(NativeStrings)},nameof(DeleteFiles));
        Hook(provider,"StoreDataStream",new[]{typeof(string),typeof(NativeStream)},nameof(StoreStream));
        _installed=installed;_hooksReady=installed==RequiredHookCount;
        return installed;
    }
    private static bool CurrentVoid(AutoSaveDirector __instance)=>
        Allow(CanWriteCurrent(__instance),"native game save");
    private static bool CurrentBool(AutoSaveDirector __instance,ref bool __result)
    {
        if(Allow(CanWriteCurrent(__instance),"native SaveAllNow"))return true;
        __result=false;return false;
    }
    private static bool CurrentSummary(AutoSaveDirector __instance,ref Summary __result)
    {
        if(Allow(CanWriteCurrent(__instance),"native SaveGame_GetSummary"))return true;
        __result=null;return false;
    }
    private static bool CurrentGame(AutoSaveDirector __instance,ref GameV11 __result)
    {
        if(Allow(CanWriteCurrent(__instance),"native SaveGameImpl"))return true;
        __result=null;return false;
    }
    private static bool NamedMutation(string __0)=>
        Allow(AuditSavePolicy.NamedMutation(_required,Ready,_closing,_readOnlyProbe,OwnedCopy,__0),"native game cleanup/delete",__0??"");
    private static bool NativeQuit()
    {
        if(!_required)return true;
        _closing=true;
        return Allow(false,"native AutoSaveDirector.OnApplicationQuit");
    }
    private static bool ProviderPath(FileStorageProvider provider)
    {
        if(!_required)return true;
        try{return Ready&&provider!=null&&!provider.WasCollected&&AuditImmutableBaseline.SamePath(provider.savePath,_baseline.SavePath);}
        catch{return false;}
    }
    private static bool FileAllowed(FileStorageProvider provider,string name)
    {
        if(!_required)return true;
        try
        {
            if(!ProviderPath(provider)||string.IsNullOrEmpty(name))return false;
            if(_baseline.OriginalDestination(name))return false; // Independent canonical original deny, extensionless names too.
            string leaf=Path.GetFileName(name);
            if(leaf!=name&&(!Path.IsPathRooted(name)||!AuditImmutableBaseline.SamePath(Path.GetDirectoryName(name),_baseline.SavePath)))return false;
            return AuditSavePolicy.FileMutation(true,Ready,_closing,_readOnlyProbe,OwnedCopy,leaf);
        }
        catch{return false;}
    }
    private static bool StoreGame(FileStorageProvider __instance,string __1)=>
        Allow(FileAllowed(__instance,__1),"native StoreGameData",__1??"");
    private static bool DeleteFile(FileStorageProvider __instance,string __0)=>
        Allow(FileAllowed(__instance,__0),"native DeleteGameData",__0??"");
    private static bool DeleteFiles(FileStorageProvider __instance,NativeStrings __0)
    {
        if(!_required)return true;
        try
        {
            if(__0==null||__0.Count>512)return Allow(false,"native DeleteGamesData invalid list");
            for(int i=0;i<__0.Count;i++)
                if(!FileAllowed(__instance,__0[i]))return Allow(false,"native DeleteGamesData",__0[i]??"");
            return true;
        }
        catch{return Allow(false,"native DeleteGamesData invalid state");}
    }
    private static bool StoreStream(FileStorageProvider __instance,string __0)
    {
        if(!_required)return true;
        // Positive allowlist, not "anything without .sav": this helper also
        // writes native profile/settings. All other names need the exact copy
        // gate, including extensionless game names and temporary save files.
        try
        {
            if(!ProviderPath(__instance)||string.IsNullOrEmpty(__0))return Allow(false,"native StoreDataStream invalid path");
            string leaf=Path.GetFileName(__0);
            bool local=leaf==__0||Path.IsPathRooted(__0)&&AuditImmutableBaseline.SamePath(Path.GetDirectoryName(__0),_baseline.SavePath);
            bool nativeService=leaf==Path.GetFileName(FileStorageProvider.PROFILE_FILENAME)||
                leaf==Path.GetFileName(FileStorageProvider.SETTINGS_FILENAME);
            bool original=_baseline.OriginalDestination(__0);
            if(local&&nativeService&&!original&&!leaf.EndsWith(".sav",StringComparison.OrdinalIgnoreCase))return true;
            return Allow(FileAllowed(__instance,__0),"native StoreDataStream",__0);
        }
        catch{return Allow(false,"native StoreDataStream invalid state");}
    }
}