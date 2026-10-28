using System;
using System.IO;
using System.Text.Json;

namespace CreatorToolkit;

internal sealed partial class FeatureSuite
{
    private bool _mainAutosaveHeld,_heldAutosaveWasDisabled,_holdExitRequested;
    private string _heldMainName="";
    private float _holdUntil;
    internal bool MainAutosaveHeld=>_mainAutosaveHeld;

    private void RetireOwnedAuditSidecar()
    {
        Require(_recovery!=null&&_recovery.TestName!=_recovery.MainName&&WaypointStore.SaveKey(_recovery.TestName),
            "Owned cancelled-copy sidecar identity missing.");
        string path=Path.Combine(_app.DataDirectory,"waypoints-"+_recovery.TestName+".json");
        if(!File.Exists(path))return;
        string json=File.ReadAllText(path);
        WaypointStore.Read(json,_recovery.TestName);
        string archive=Path.Combine(_app.DataDirectory,"audit-retired-sidecars");
        Directory.CreateDirectory(archive);
        string destination=Path.Combine(archive,Path.GetFileName(path));
        if(File.Exists(destination))
        {
            string existing=File.ReadAllText(destination);
            WaypointStore.Read(existing,_recovery.TestName);
            Require(existing==json,"Retired audit sidecar differs; both files preserved.");
        }
        else File.WriteAllText(destination,json);
        Require(File.ReadAllText(destination)==json,"Retired owned sidecar verification failed.");
        File.Delete(path);
    }

    private void HoldAutosaveAndRecordReturn()
    {
        // Native auto-save rotates main checkpoints. The protected process
        // remains read-only until exit; acknowledgement is NOT a save release.
        // No original .sav bytes are ever restored.
        VerifyMainHashes();
        AuditSaveGuard.VerifyOriginals(true);
        Require(Directory.GetFiles(_recovery.SavePath,_recovery.TestName+"_*.sav").Length==0,
            "Owned native copy remains; protected return is not complete.");
        Require(!File.Exists(Path.Combine(_app.DataDirectory,"waypoints-"+_recovery.TestName+".json")),
            "Owned audit sidecar remains; protected return is not complete.");
        Save.SetAutosaveDisabled(true);
        _heldAutosaveWasDisabled=_oldAutosave;_heldMainName=_recovery.MainName;
        _mainAutosaveHeld=true;_holdExitRequested=false;_holdUntil=UnityEngine.Time.unscaledTime+900f;
        var proof=new
        {
            Version=ToolkitMod.Version,RecordedUtc=DateTime.UtcNow.ToString("o"),RunId=_recovery.RunId,
            DllSha256=Hash(typeof(ToolkitMod).Assembly.Location),
            MainName=_recovery.MainName,MainSave=_recovery.MainSave,Copy=_recovery.TestName,
            CopyFilesRemaining=Directory.GetFiles(_recovery.SavePath,_recovery.TestName+"*.sav").Length,
            OriginalHashFiles=_recovery.MainHashes.Count,OriginalHashes=_recovery.MainHashes,
            OriginalBytesVerified=true,MainNativeTestsSkipped=true,
            Passed=_passed,Failed=_failed,Completed=_completed,
            AutosaveHeld=true,PreviousAutosaveDisabled=_oldAutosave,ProtectionRemainsArmed=true,
            ImmutableBaselineSha256=AuditSaveGuard.BaselineSha256,Protection=AuditSaveGuard.Evidence(),
            NoSaveExitAfterSeconds=900,NativeCopySidecarAbsent=!File.Exists(Path.Combine(_app.DataDirectory,"waypoints-"+_recovery.TestName+".json"))
        };
        File.WriteAllText(Path.Combine(_app.DataDirectory,"audit-return-proof.json"),JsonSerializer.Serialize(proof));
        _app.LoggerInstance.Msg("Audit returned-main original bytes verified and native autosave held for evidence: "+
            _recovery.MainHashes.Count+" files; no main-world UI/input tests.");
    }
    internal void ReleaseMainAutosaveHold(string reason="explicit evidence acknowledgement")
    {
        if(!_mainAutosaveHeld)return;
        VerifyMainHashes();Save.SetAutosaveDisabled(true);
        File.WriteAllText(Path.Combine(_app.DataDirectory,"audit-autosave-release.json"),JsonSerializer.Serialize(new
        {
            Version=ToolkitMod.Version,AcknowledgedUtc=DateTime.UtcNow.ToString("o"),MainName=_heldMainName,
            PreviousAutosaveDisabled=_heldAutosaveWasDisabled,SettingRestored=false,ProtectionRemainsArmed=true,
            ImmutableBaselineSha256=AuditSaveGuard.BaselineSha256,Reason=reason
        }));
        _app.Notify("Доказательства подтверждены; запись основного сейва остаётся заблокированной до выхода.");
    }
    internal void RequestProtectedExit(string reason)
    {
        Require(AuditSaveGuard.Required,"A no-save exit is available only for an explicitly protected audit process.");
        if(Active){CancelForQuit();if(Active)return;_quitAfter=false;}
        Require(_recovery==null,"Owned-copy recovery is unresolved; no premature exit acknowledgement.");
        AuditSaveGuard.VerifyOriginals(true);
        Il2Cpp.GameContext.Instance?.AutoSaveDirector?.SetAutosaveDisabled(true);
        RestoreInput();_app.Tests.CancelRequest();_app.CloseMenu();
        AuditSaveGuard.PrepareProtectedQuit(reason,true);
        _quitting=true;
        _app.LoggerInstance.Msg("Protected audit exit requested; no native game save or autosave release. Post-exit hashes still require verification.");
        UnityEngine.Application.Quit();
    }
    private void TickMainAutosaveHold()
    {
        if(!_mainAutosaveHeld)return;
        Save.SetAutosaveDisabled(true);
        if(UnityEngine.Time.unscaledTime>=_holdUntil&&!_holdExitRequested)
        {
            _holdExitRequested=true;
            // A deadline never restores writing on the returned main. If exit
            // proof fails, the process remains protected and fails closed.
            RequestProtectedExit("bounded evidence window elapsed; protected no-save exit");
        }
    }
}