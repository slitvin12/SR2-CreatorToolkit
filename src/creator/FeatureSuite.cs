using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;
using UnityEngine;
using UnityEngine.InputSystem;
using UObj=UnityEngine.Object;

namespace CreatorToolkit;

// Opt-in native integration tests. Permanent operations NEVER run on the user's GameModel.
internal sealed partial class FeatureSuite
{
    private sealed class AmmoExpectation
    {
        public int Slot {get;set;} public int Count {get;set;}
        public string Reference {get;set;}="";
    }
    private sealed class Recovery
    {
        public string MainName {get;set;} public string MainSave {get;set;}
        public string TestName {get;set;} public string TestSave {get;set;}
        public Dictionary<string,string> MainHashes {get;set;} = new();
        public string ImmutableBaselineSha256 {get;set;}="";
        public string RunId {get;set;}="";
        public string SavePath {get;set;}
        public string Boot {get;set;} = "return";
        public List<string> Rows {get;set;} = new();
        public int Passed {get;set;} public int Failed {get;set;} public bool Completed {get;set;}
        public int Currency {get;set;} public int Pedia {get;set;} public int AmmoCount {get;set;} public int AmmoSlot {get;set;}
        public string AmmoRef {get;set;}
        public List<AmmoExpectation> AmmoSlots {get;set;}=new();
        public bool Autosave {get;set;} public bool Background {get;set;}
        public bool God {get;set;} public bool Energy {get;set;} public bool Flight {get;set;}
        public bool FreezeTime {get;set;} public bool FreezeMobs {get;set;} public bool Tarr {get;set;} public bool Hud {get;set;}
        public float Speed {get;set;} = 1;
        public float X {get;set;} public float Y {get;set;} public float Z {get;set;}
        public bool Stress {get;set;}
        public string CustomMarkers {get;set;}="";
    }
    private sealed class Step
    {
        internal string Name; internal Action Run; internal float Delay;
        internal Func<bool> AwaitReady; internal float WaitSeconds;
    }
    private readonly ToolkitMod _app;
    private readonly List<Step> _steps=new();
    private readonly List<string> _rows=new();
    private Recovery _recovery;
    private int _phase=-1,_index,_failed,_passed,_waitingStep=-1;
    private float _next,_pointerWaitUntil,_stepWaitUntil;
    private bool _matrixTransitPending;
    private float _matrixTransitDeadline;
    private Il2CppMonomiPark.SlimeRancher.DataModel.GameModel _mainModel;
    private int _savedCurrency, _savedPedia, _savedAmmoCount, _savedAmmoSlot;
    private string _savedAmmoRef;
    private readonly List<AmmoExpectation> _savedAmmoSlots=new();
    private bool _requested,_oldAutosave,_oldBackground,_oldKeyboardEnabled,_restoreInput,_quitAfter,_completed;
    private bool _freshBoot,_quitting,_reportWriteFailed;
    private string _reportWriteFailure="";
    private bool _stressRequested;
    private IntPtr _isolatedSession;
    private InputSettings.BackgroundBehavior _oldInputPolicy;
    private bool _god,_energy,_flight,_freezeTime,_freezeMobs,_tarr,_hud;
    private float _speed;
    private Vector3 _position,_probePosition;
    private readonly List<CinemaTools.Waypoint> _waypoints=new();
    internal bool Active=>_requested||_phase>=0;
    internal int CachedStepCount=>_steps.Count;
    internal bool SkipMainNativeTests=>AuditSaveGuard.Required&&
        !(_freshBoot&&(_phase==1||_phase==2||_phase==3));
    internal void GuardNativeProbeCopy()
    {
        var meta=NativeSaveMetadata.Read(Save);
        Require(_freshBoot&&_mainModel==null&&_recovery!=null&&_app.Ready&&meta!=null&&meta.HasValue&&
            meta.Value.GameName==_recovery.TestName&&_recovery.TestName!=_recovery.MainName,
            "Native UI/input test refused outside the isolated copy.");
        VerifyMainHashes();
        // The copy's smoke test precedes the functional matrix. Bind that
        // verified fresh-process copy here, never on an arbitrary gameplay
        // session, so every later input event can use the strict same-session
        // guard without weakening protection of the original save.
        if(_isolatedSession==IntPtr.Zero&&(_phase==1||_phase==3))_isolatedSession=_app.Session;
        GuardCopy();
    }
    internal string Status="Функциональная проверка не запущена";
    private string Marker=>Path.Combine(_app.DataDirectory,"functional.request");
    private string StateFile=>Path.Combine(_app.DataDirectory,"functional-recovery.json");
    private SRCharacterController Controller=>_app.Context.Player.GetComponentInChildren<SRCharacterController>(true);
    private AutoSaveDirector Save=>GameContext.Instance.AutoSaveDirector;

    internal FeatureSuite(ToolkitMod app)
    {
        _app=app;_requested=File.Exists(Marker);
        _stressRequested=File.Exists(Path.Combine(_app.DataDirectory,"stress.request"));
        if(_requested)Status="Ожидание базовой проверки; затем — отдельная копия сейва";
        if(AuditCheckpointStore.Exists(StateFile))
        {
          try
          {
            _recovery=JsonSerializer.Deserialize<Recovery>(AuditCheckpointStore.Read(StateFile,out bool previousUsed));
            Require(_recovery!=null,"Audit recovery is empty.");
            Require(_recovery.Boot=="run"||_recovery.Boot=="verify"||_recovery.Boot=="return","Unknown owned-copy recovery stage.");
            AuditSaveGuard.ValidateRecovery(_recovery.MainName,_recovery.MainSave,_recovery.SavePath,
                _recovery.ImmutableBaselineSha256,_recovery.MainHashes,_recovery.RunId);
            AuditSaveGuard.RegisterOwnedCopy(_recovery.MainName,_recovery.TestName,_recovery.ImmutableBaselineSha256,_recovery.RunId);
            AuditSaveGuard.VerifyOriginals();
            if(previousUsed)
            {
                // A fallback checkpoint never repeats permanent test steps.
                // Preserve the rejected JSON and publish only a safe return.
                _recovery.Boot="return";WriteState();
            }
            _stressRequested=_recovery.Stress;
            _requested=false;_freshBoot=true;
            _rows.AddRange(_recovery.Rows);_passed=_recovery.Passed;_failed=_recovery.Failed;_completed=_recovery.Completed;
            _savedCurrency=_recovery.Currency;_savedPedia=_recovery.Pedia;_savedAmmoCount=_recovery.AmmoCount;_savedAmmoSlot=_recovery.AmmoSlot;_savedAmmoRef=_recovery.AmmoRef;
            if(_recovery.AmmoSlots!=null)_savedAmmoSlots.AddRange(_recovery.AmmoSlots);
            _oldAutosave=_recovery.Autosave;_oldBackground=_recovery.Background;
            _god=_recovery.God;_energy=_recovery.Energy;_flight=_recovery.Flight;_speed=_recovery.Speed;
            _freezeTime=_recovery.FreezeTime;_freezeMobs=_recovery.FreezeMobs;_tarr=_recovery.Tarr;_hud=_recovery.Hud;
            _position=new Vector3(_recovery.X,_recovery.Y,_recovery.Z);
            _phase=_recovery.Boot=="run"?1:_recovery.Boot=="verify"?3:5;
            Status=_phase==5?"Восстановление основного сейва после проверки":"Загрузка проверочной копии в отдельном процессе";
          }
          catch(Exception e)
          {
            // Preserve the opaque recovery file and block all audit boot. A
            // malformed/old checkpoint must not partly initialize the mod or
            // fall back to an arbitrary Continue save.
            AuditSaveGuard.FailClosed("Immutable audit recovery refused: "+e.Message);
            _recovery=null;_requested=false;_freshBoot=false;_phase=-1;
            Status="Проверка заблокирована: неизменяемая база / восстановление не подтверждены.";
            _app.Fail("audit recovery gate",e);
          }
        }
    }
    internal Il2CppMonomiPark.SlimeRancher.Persist.Summary BootSummary()
    {
        if(!AuditSaveGuard.Required)return null;
        AuditSaveGuard.RequireReady();
        var storage=Save._storageProvider.TryCast<FileStorageProvider>();
        Require(storage!=null,"Audit boot requires the verified local save provider.");
        AuditSaveGuard.RequireNativePath(storage.savePath);
        VerifyMainHashes();
        if(!_freshBoot||_recovery==null)
        {
            Require(_requested,"Protected audit boot has no active request.");
            Save.SetAutosaveDisabled(true);
            return FindStoredSummary(AuditSaveGuard.MainName,AuditSaveGuard.MainSave);
        }
        bool copy=_phase==1||_phase==3;
        var result=FindStoredSummary(copy?_recovery.TestName:_recovery.MainName,copy?_recovery.TestSave:_recovery.MainSave);
        if(copy)Require(result.DisplayName.StartsWith("[CT AUDIT]",StringComparison.Ordinal),"Boot copy ownership marker missing.");
        Save.SetAutosaveDisabled(true);
        return result;
    }
    internal void Request()
    {
        if(Active)return;
        Require(!AuditCheckpointStore.Exists(StateFile),"An unresolved audit recovery file already exists.");
        Require(_app.Map?.Busy!=true,"Finish the native map transit before arming a protected audit.");
        Require(!MainAutosaveHeld,"The protected returned-main process must exit before another audit.");
        AuditSaveGuard.Arm();AuditSaveGuard.RequireReady();AuditSaveGuard.VerifyOriginals(true);
        _rows.Clear();_passed=_failed=0;_completed=false;_requested=true;Status="Ожидание создания отдельной копии сейва";
    }
    internal void CancelForQuit(){_quitAfter=true;Cancel();}
    internal void Cancel()
    {
        RetireUiReferences();
        _app.Stress?.Cancel();
        if(_recovery!=null){_phase=4;Status="Возвращаю основной сейв…";}
        else {_requested=false;_phase=-1;RestoreInput();}
    }
    private void GuardCopy()
    {
        var meta=NativeSaveMetadata.Read(Save);
        if(!_freshBoot||!_app.Ready||_recovery==null||meta==null||!meta.HasValue||meta.Value.GameName!=_recovery.TestName||
            _isolatedSession==IntPtr.Zero||_app.Session!=_isolatedSession||_mainModel!=null)
            throw new InvalidOperationException("Permanent test refused: active world is not the isolated copy.");
    }
    internal void GuardStressCopy()=>GuardCopy();
    internal void RebindStressCopyIfNeeded(){if(_app.Ready&&_isolatedSession!=_app.Session)RebindStressCopy();}
    internal void RebindStressCopy()
    {
        Require(_freshBoot&&_mainModel==null&&_recovery!=null,"Stress reload isolation missing.");
        var meta=NativeSaveMetadata.Read(Save);
        Require(_app.Ready&&meta!=null&&meta.HasValue&&meta.Value.GameName==_recovery.TestName,"Stress reload did not return to its owned copy.");
        _isolatedSession=_app.Session;GuardCopy();VerifyMainHashes();Save.SetAutosaveDisabled(true);EnableTestInput();
    }
    internal void BeginStressMenuReload(bool saveCheckpoint=true,bool armOptimizationForUnload=false)
    {
        GuardCopy();
        _app.Optimization.Reset(); // Never persist a test's transient graphics profile.
        if(saveCheckpoint)
        {
            Save.SetAutosaveDisabled(false);
            try{Require(Save.SaveAllNow(true,null),"Stress copy native checkpoint failed.");Save.SaveGameAndFlush();Save._storageProvider.Flush();}
            finally{Save.SetAutosaveDisabled(true);}
            _recovery.TestSave=FindStoredSummary(_recovery.TestName).SaveName;WriteState();
        }
        else Save.SetAutosaveDisabled(true);
        VerifyMainHashes();
        if(armOptimizationForUnload)_app.Optimization.ApplyPotato();
        SystemContext.Instance.SceneLoader.LoadMainMenuSceneGroup();
    }
    internal string StressCheckpointFingerprint()
    {
        GuardCopy();
        var storage=Save._storageProvider.TryCast<FileStorageProvider>();
        string leaf=_recovery.TestSave;
        Require(storage!=null&&Path.GetFileName(leaf)==leaf&&
            leaf.StartsWith(_recovery.TestName+"_",StringComparison.Ordinal),"Owned checkpoint fingerprint requires the exact native save.");
        // Read/hash the owned checkpoint only. Never replace/edit .sav bytes.
        return Hash(Path.Combine(storage.savePath,leaf+".sav"));
    }
    internal void LoadStressCopyFromMenu()
    {
        Require(_freshBoot&&_recovery!=null&&_mainModel==null&&!_app.Ready,"Stress load requires an unloaded owned world.");
        var summary=FindStoredSummary(_recovery.TestName,_recovery.TestSave);
        Require(summary.DisplayName.StartsWith("[CT AUDIT]",StringComparison.Ordinal),"Stress copy ownership missing.");
        Save.SetAutosaveDisabled(true);Save.BeginLoad(summary.SaveIdentifier);
    }
    private void Record(bool ok,string name,string detail="")
    {
        if(ok)_passed++;else _failed++;
        string row=(ok?"PASS ":"FAIL ")+name+(detail.Length>0?" — "+detail:"");
        _rows.Add(row);
        try{_app.LoggerInstance.Msg("Functional: "+row);}catch{/* Diagnostics cannot prevent recovery scheduling. */}
        WriteReport(false);
    }
    private static void Require(bool condition,string text){if(!condition)throw new InvalidOperationException(text);}
    private void Add(string name,Action action,float delay=.12f,Func<bool> awaitReady=null,float waitSeconds=5f)=>
        _steps.Add(new Step{Name=name,Run=action,Delay=delay,AwaitReady=awaitReady,WaitSeconds=waitSeconds});
    private void WriteState()=>AuditCheckpointStore.Write(StateFile,JsonSerializer.Serialize(_recovery));
    private bool WriteReport(bool final)
    {
      try
      {
        File.WriteAllText(Path.Combine(_app.DataDirectory,"functional.txt"),
            "CreatorToolkit "+ToolkitMod.Version+" native functional suite\n"+(final?(_failed>0?"FAILED":_completed?"PASS":"INCOMPLETE"):"RUNNING")+
            $"\nPassed={_passed} Failed={_failed}\nScope: native feature APIs on an isolated copy; current-zone weather states; no OS-keyboard certification.\n"+
            string.Join("\n",_rows)+"\n");
        _reportWriteFailed=false;_reportWriteFailure="";return true;
      }
      catch(Exception e) when(e is IOException||e is UnauthorizedAccessException)
      {
        if(!_reportWriteFailed){_failed++;_rows.Add("FAIL audit report I/O — "+e.Message);}
        _reportWriteFailed=true;_reportWriteFailure=e.Message;return false;
      }
    }
    private static string Hash(string path)
    {using var sha=SHA256.Create();return Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path))).ToLowerInvariant();}

    private Il2CppMonomiPark.SlimeRancher.Persist.Summary FindStoredSummary(string gameName,string exactSave=null)
    {
        var storage=Save._storageProvider.TryCast<FileStorageProvider>();
        Require(storage!=null,"Verified local save provider unavailable.");
        if(!string.IsNullOrEmpty(exactSave)&&exactSave!=gameName)
        {
            Require(Path.GetFileName(exactSave)==exactSave&&File.Exists(Path.Combine(storage.savePath,exactSave+".sav")),"Exact checkpoint file missing.");
            var exact=Save.LoadSummary(exactSave);
            Require(exact!=null&&!exact.IsInvalid&&exact.Name==gameName&&exact.SaveName==exactSave,"Exact checkpoint summary invalid.");
            return exact;
        }
        Il2CppMonomiPark.SlimeRancher.Persist.Summary latest=null;
        foreach(string path in Directory.GetFiles(storage.savePath,gameName+"_*.sav"))
        {
            string leaf=Path.GetFileNameWithoutExtension(path);
            var candidate=Save.LoadSummary(leaf);
            if(candidate==null||candidate.IsInvalid||candidate.Name!=gameName||candidate.SaveName!=leaf)continue;
            if(latest==null||candidate.SaveNumber>latest.SaveNumber)latest=candidate;
        }
        Require(latest!=null,"No verified stored summary for "+gameName);
        return latest;
    }

    private void BeginCopy()
    {
        Require(_app.Ready,"Load gameplay first.");
        Require(!_app.Map.Busy,"An audit cannot start during a native map transit.");
        Require(!_app.Player.HighJump&&!_app.Player.InfiniteItems&&!_app.Optimization.Active,
            "Disable temporary jump/infinite-vacpack/graphics overrides before the isolated audit.");
        Require(_app.Convenience.Profile==0&&!_app.Convenience.Refilling&&!_app.Convenience.TrackSafe,"Disable new profiles/automations before isolated audit.");
        Require(!_app.Custom.ShowCompass&&!_app.Map.CustomPointMode&&!_app.Radar.Enabled&&!_app.Farm.Enabled,
            "Disable custom navigation/radar/farm automation before isolated audit.");
        var save=Save;var meta=NativeSaveMetadata.Read(save);
        Require(meta!=null&&meta.HasValue,"Native game metadata unavailable.");
        var old=meta.Value;
        AuditSaveGuard.RequireReady();AuditSaveGuard.VerifyOriginals(true);
        Require(old.GameName==AuditSaveGuard.MainName,"Loaded game is not the approved protected main.");
        _mainModel=_app.Context.GameModel;_oldAutosave=AuditSaveGuard.InitialAutosaveDisabled??save._autosaveDisabled;_oldBackground=Application.runInBackground;
        _god=_app.Player.GodMode;_energy=_app.Player.InfiniteEnergy;_flight=_app.Player.Flight;_speed=_app.Player.SpeedMultiplier;
        _freezeTime=_app.World.FreezeTime;_freezeMobs=_app.World.FreezeMobs;_tarr=_app.World.DisableTarr;_hud=_app.Cinema.HideHud;
        _position=Controller.Motor.TransientPosition;_waypoints.AddRange(_app.Cinema.Waypoints);
        _app.CloseMenu();_app.Convenience.Reset();_app.Player.Reset();_app.World.Reset();_app.Cinema.Reset();
        // A previous normal-save/quit is NOT a safety precondition: it rotates
        // originals. Use the one explicitly approved immutable checkpoint.
        // Native serialization below writes ONLY the renamed owned clone.
        var main=FindStoredSummary(old.GameName,AuditSaveGuard.MainSave);
        meta=NativeSaveMetadata.Read(save);Require(meta!=null&&meta.HasValue&&meta.Value.GameName==old.GameName,"Main metadata changed during checkpoint.");
        old=meta.Value; // Preserve the updated save number, not the pre-checkpoint number.
        var storage=save._storageProvider.TryCast<FileStorageProvider>();
        Require(storage!=null,"Only verified local FileStorageProvider is supported for isolated testing.");
        AuditSaveGuard.RequireNativePath(storage.savePath);
        _recovery=new Recovery{MainName=old.GameName,MainSave=main.SaveName,SavePath=storage.savePath,
            TestName=DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"_"+(old.SlotIndex+1),
            ImmutableBaselineSha256=AuditSaveGuard.BaselineSha256,MainHashes=AuditSaveGuard.OriginalHashes(),
            RunId=Guid.NewGuid().ToString("N"),
            Autosave=_oldAutosave,Background=_oldBackground,God=_god,Energy=_energy,Flight=_flight,Speed=_speed,
            FreezeTime=_freezeTime,FreezeMobs=_freezeMobs,Tarr=_tarr,Hud=_hud,X=_position.x,Y=_position.y,Z=_position.z,Stress=_stressRequested};
        Require(_recovery.TestName!=_recovery.MainName,"Clone name collision.");
        Require(Directory.GetFiles(storage.savePath,_recovery.TestName+"_*.sav").Length==0,"An existing native game collides with the proposed owned-copy name.");
        AuditSaveGuard.RegisterOwnedCopy(_recovery.MainName,_recovery.TestName,_recovery.ImmutableBaselineSha256,_recovery.RunId);
        string backup=Path.Combine(_app.DataDirectory,"audit-backup-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backup);
        foreach(var pair in _recovery.MainHashes)
        {
            string destination=Path.Combine(backup,Path.GetFileName(pair.Key));
            File.Copy(pair.Key,destination,false);
            Require(Hash(destination)==pair.Value,"Immutable original backup verification failed.");
        }
        WriteState();save.SetAutosaveDisabled(true);Application.runInBackground=true;
        try
        {
            var test=new GameMetadata(_recovery.TestName,"[CT AUDIT] "+old.DisplayName,0,old.SlotIndex);
            var targetMetadata=new Il2CppSystem.Nullable<GameMetadata>{hasValue=true,value=test};
            NativeSaveMetadata.Write(save,targetMetadata);
            var prepared=NativeSaveMetadata.Read(save);
            Require(prepared!=null&&prepared.HasValue&&prepared.Value.GameName==_recovery.TestName,"Clone metadata preparation failed.");
            bool renamed=false;
            AutoSaveDirector.ModifySaveGameDelegate rename=new Action<Il2CppMonomiPark.SlimeRancher.Persist.GameV11,ISaveReferenceTranslation>((game,translation)=>
            { renamed=true;game.GameName=_recovery.TestName;game.DisplayName="[CT AUDIT] "+old.DisplayName;game.SaveSlotIndex=old.SlotIndex; });
            // This native method also honors the global save-disable flag. Enable
            // only for this synchronous clone write; no game frame can interleave.
            save.SetAutosaveDisabled(false);
            var serialized=save.SaveGame_GetSummary(true,rename);
            Require(renamed&&serialized!=null&&serialized.Name==_recovery.TestName,"Clone serialization refused; callback="+renamed+" name="+serialized?.Name);
            save._storageProvider.Flush();
            var summary=FindStoredSummary(_recovery.TestName);
            Require(summary.DisplayName.StartsWith("[CT AUDIT]",StringComparison.Ordinal),"Clone ownership marker missing.");
            _recovery.TestSave=summary.SaveName;WriteState();
        }
        finally { save.SetAutosaveDisabled(true);NativeSaveMetadata.Write(save,meta);save.SaveProfileImpl(main);save._storageProvider.Flush(); }
        VerifyMainHashes();
        if(File.Exists(Marker))File.Delete(Marker);
        RequestBoot("run");
    }
    private void RequestBoot(string stage)
    {
        VerifyMainHashes();RestoreInput();_app.CloseMenu();
        Save.SetAutosaveDisabled(true);
        Save.SaveProfileImpl(FindStoredSummary(_recovery.MainName,_recovery.MainSave));Save._storageProvider.Flush();
        _recovery.Boot=stage;_recovery.Rows=new List<string>(_rows);_recovery.Passed=_passed;_recovery.Failed=_failed;_recovery.Completed=_completed;
        _recovery.Currency=_savedCurrency;_recovery.Pedia=_savedPedia;_recovery.AmmoCount=_savedAmmoCount;_recovery.AmmoSlot=_savedAmmoSlot;_recovery.AmmoRef=_savedAmmoRef;
        _recovery.AmmoSlots=new List<AmmoExpectation>(_savedAmmoSlots);
        WriteState();WriteReport(false);
        File.WriteAllText(Path.Combine(_app.DataDirectory,"selftest.request"),"continue");
        Status="Безопасный перезапуск проверки: "+stage;
        AuditSaveGuard.PrepareProtectedQuit("owned audit checkpoint: "+stage,false);
        _quitting=true;Application.Quit();
    }
    private void VerifyMainHashes()
    {
        AuditSaveGuard.VerifyOriginals();
        if(_recovery!=null)AuditSaveGuard.ValidateRecovery(_recovery.MainName,_recovery.MainSave,_recovery.SavePath,
            _recovery.ImmutableBaselineSha256,_recovery.MainHashes,_recovery.RunId);
    }
    private void EnableTestInput()
    {
        var kb=Keyboard.current;Require(kb!=null,"Native Keyboard unavailable.");
        if(!_restoreInput){_oldKeyboardEnabled=kb.enabled;_oldInputPolicy=InputSystem.settings.backgroundBehavior;_restoreInput=true;}
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        if(!kb.enabled)InputSystem.EnableDevice(kb);
        SystemContext.Instance.SceneLoader.UnpauseGame();
    }
    private void RestoreInput()
    {
        NativeKeyboardProbe.ReleaseW();NativeKeyboardProbe.ReleaseAuxiliary();
        if(!_restoreInput)return;
        InputSystem.settings.backgroundBehavior=_oldInputPolicy;
        if(!_oldKeyboardEnabled&&!Application.isFocused&&Keyboard.current!=null&&Keyboard.current.enabled)InputSystem.DisableDevice(Keyboard.current);
        _restoreInput=false;
    }

    private void BuildSteps()
    {
        GuardCopy();EnableTestInput();_steps.Clear();_rows.Clear();_passed=_failed=_index=0;_completed=false;_waitingStep=-1;
        _app.Actors.RefreshCatalog();_app.Player.GodMode=_app.Player.InfiniteEnergy=true;
        BuildNativeUiReferenceSteps();
        BuildNativeWindowInputSteps();
        Add("UI / native LMB interaction start",()=>_app.UiProbe.Start(),5f);
        Add("UI / native LMB sliders and scrollbar",()=>Require(_app.UiProbe.Status=="PASS","Native pointer probe: "+_app.UiProbe.Status));
        BuildLoPlayerSteps();
        BuildOptimizationSteps();
        Add("Conveniences / defaults off",()=>Require(_app.Convenience.Profile==0&&!_app.Convenience.ShowHud&&!_app.Convenience.TrackSafe&&!_app.Convenience.Refilling&&!_app.Phenomena.Inspect,"A new automation enabled without opt-in."));
        Add("Profiles / explorer and HUD snapshot",()=>{
            _app.Convenience.SetProfile(1);_app.CloseMenu();_app.Convenience.RefreshHud();
            Require(_app.Player.GodMode&&_app.Player.InfiniteEnergy&&_app.Convenience.ShowHud&&Math.Abs(_app.Player.SpeedMultiplier-2)<.01f,"Explorer settings missing.");
            Require(_app.Convenience.HudCoordinates.Contains("X ")&&_app.Convenience.HudEffects.Contains("Исследователь"),"HUD content not updated from native state.");
        },.4f);
        Add("HUD / right capture",()=>ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"convenience-hud-right.png")),.25f);
        Add("HUD / left position",()=>_app.Convenience.HudPlacement=1,.3f);
        Add("HUD / left capture",()=>ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"convenience-hud-left.png")),.25f);
        Add("HUD / bottom position",()=>_app.Convenience.HudPlacement=2,.3f);
        Add("HUD / bottom capture",()=>ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"convenience-hud-bottom.png")),.25f);
        Add("Profiles / frame switches and suppresses HUD",()=>{
            _app.Convenience.SetProfile(2);
            Require(_app.Player.Flight&&_app.Cinema.HideHud&&_app.World.FreezeTime&&_app.World.FreezeMobs&&!_app.Convenience.ShowHud,"Frame profile does not suspend/show the expected native effects.");
        },.3f);
        Add("Profiles / original settings restored",()=>{
            _app.Convenience.SetProfile(0);_app.Convenience.HudPlacement=0;
            Require(!_app.Player.Flight&&!_app.Cinema.HideHud&&!_app.World.FreezeTime&&!_app.World.FreezeMobs&&!_app.Convenience.ShowHud&&Math.Abs(_app.Player.SpeedMultiplier-1)<.01f,"Profile changed the captured baseline.");
        });
        Add("Atlas / version-gated index",()=>{_app.Map.RefreshIndex();Require(_app.Map.IndexStatus=="Индекс совпадает с версией игры","Atlas index is not version matched.");Require(_app.Map.Points.Count(p=>p.Category=="Капсулы")>=140,"Missing unloaded capsule/reward points.");});
        Add("Catalog / actual native Slimepedia sequence",()=>{
            var ranks=new Dictionary<IntPtr,int>();int n=0;
            var categories=_app.Context.PediaDirector.Categories;
            int categoryCount=categories==null?0:categories.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Il2CppMonomiPark.SlimeRancher.Pedia.IPediaRuntimeCategory>>().Count;
            for(int i=0;i<categoryCount;i++)
            {
                var items=categories[i]?.TryCast<Il2CppMonomiPark.SlimeRancher.Pedia.PediaRuntimeCategory>()?.Items;
                int itemCount=items==null?0:items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Il2CppMonomiPark.SlimeRancher.Pedia.PediaEntry>>().Count;
                for(int j=0;j<itemCount;j++)
                {
                    var type=items[j]?.TryCast<Il2CppMonomiPark.SlimeRancher.Pedia.IdentifiablePediaEntry>()?.IdentifiableType;
                    if(type!=null&&!ranks.ContainsKey(type.Pointer))ranks.Add(type.Pointer,n++);
                }
            }
            var slimes=_app.Actors.Catalog.Where(e=>e.Category=="Слаймы"&&ranks.ContainsKey(e.Type.Pointer)).ToArray();
            Require(slimes.Length>=15,"Native encyclopedia ordering did not resolve enough slime entries.");
            Require(slimes.Select(e=>ranks[e.Type.Pointer]).SequenceEqual(slimes.Select(e=>ranks[e.Type.Pointer]).OrderBy(i=>i)),"Slime list differs from native Pedia.Items order.");
            Require(_app.Actors.CanonicalCount>=slimes.Length,"Catalog canonical accounting is inconsistent.");
        });
        foreach(var family in new[]{("FruitGroup","Фрукты"),("VeggieGroup","Овощи"),("MeatGroup","Мясо")})
        {
            var f=family;
            Add("Catalog / native food membership "+f.Item2,()=>{
                var group=Resources.FindObjectsOfTypeAll<IdentifiableTypeGroup>().First(g=>g.name==f.Item1);
                var actual=_app.Actors.Catalog.Where(e=>e.Category=="Еда"&&e.Family==f.Item2).ToArray();
                Require(actual.Length>0&&actual.All(e=>!e.IsPrototype&&group.IsMember(e.Type)),"Food family contains a guessed or incorrect native type.");
                Require(_app.Actors.Catalog.Where(e=>!e.IsPrototype&&group.IsMember(e.Type)).All(e=>e.Category=="Еда"&&e.Family==f.Item2),"Native food members are missing from their separate family.");
            });
        }
        Add("Capsules / all IDs and exact loaded rewards",()=>{
            var capsules=_app.Map.Capsules;
            Require(capsules.Database.Pods.Count==152&&capsules.Database.Assets.Count==126&&capsules.TextReady,"Versioned ID/reward database or localization work is incomplete.");
            Require(_app.Map.Points.Where(p=>p.Category=="Капсулы").All(p=>capsules.Database.Pods.ContainsKey(p.Id)),"An indexed capsule has no exact ID record.");
            int checkedPods=0;
            foreach(var pod in UObj.FindObjectsOfType<TreasurePod>())
            {Require(capsules.MatchesLoadedPod(pod.Id,pod),"Native reward differs from baked ID contents: "+pod.Id);checkedPods++;}
            Require(checkedPods>0,"No native capsule available for independent reward validation.");
            Require(capsules.NativeTitleCount==126,"Not all 126 native reward titles localized (including 86 GUID-backed tables).");
            foreach(var pod in capsules.Database.Pods.Values)
                Require(!capsules.Get(pod.Id).Description.Contains("[ассет]")&&!capsules.Get(pod.Id).Description.Contains("GUID:"),
                    "Technical reward localization identifier leaked into a capsule description.");
            foreach(var point in _app.Map.Points.Where(p=>p.Category=="Капсулы"))
            {
                string display=_app.Map.DisplayName(point);
                Require(!display.Contains("GUID:")&&!display.Contains("[ассет]")&&
                    !capsules.Database.Assets.Values.Any(r=>display.Contains(r.AssetName,StringComparison.Ordinal)),
                    "Technical asset name leaked into a capsule map/sidebar label.");
            }
            Require(_app.UiOwnedTextures==ToolkitUi.ThemeTextureBudget,"Jelly UI must own exactly fifteen shared surfaces and one icon atlas.");
        });
        Add("Theme / real font census and Cyrillic coverage",()=>{
            var ui=_app.UiForTests;
            Require(ui.FontCyrillicForTest&&ui.FontNativeForTest&&ui.FontResourceScansForTest>=1&&ui.FontResourceScansForTest<=2&&
                !ui.FontNameForTest.Contains("segoe",StringComparison.OrdinalIgnoreCase),
                "UI did not resolve native Cyrillic typography within the bounded original-Pedia census.");
            Require(ui.FontSourceForTest=="native-loaded Font",
                "An OS fallback cannot certify the requested native typography.");
        });
        Add("Theme / exact opaque native parchment palette",()=>{
            Require(_app.UiForTests.ThemePaletteForTest,"Native beige canvas/dark ink/pastel card differ from the requested palette.");
            Require(UiThemePolicy.RestDepth==4&&UiThemePolicy.HeldDepth==1&&UiThemePolicy.HoverDepth==3,
                "Jelly shadows do not compress from four pixels to one/three.");
        });
        Add("Theme / native Cyrillic bookmarks fit one line",()=>Require(_app.UiForTests.TabLabelsFitForTest,
            "A primary tab wraps/clips its native Cyrillic label or uses a font below the 14 px floor."));
        Add("Theme / native long button labels preserve padding",()=>{
            var ui=_app.UiForTests;bool fit=ui.ButtonLabelsFitForTest;
            _app.LoggerInstance.Msg("Native measured pill labels: "+ui.ButtonLabelFitDiagnosticsForTest);
            Require(fit,"A critical Cyrillic action/filter clips, loses its padding or uses a font below the 14 px floor. "+
                ui.ButtonLabelFitDiagnosticsForTest);
        });
        Add("Theme / shared antialiased surfaces remain bounded",()=>{
            Require(_app.UiForTests.ThemeTexturesForTest,"Theme surfaces are not shared, bilinear/clamped, or exceed the exact sixteen-texture budget.");
        });
        Add("Catalog / fruit filter and capture",()=>{
            _app.UiForTests.SetFamilyForTest(4,"Фрукты");_app.Input.Open();
            Require(_app.UiForTests.FilteredCountForTest==_app.Actors.Catalog.Count(e=>e.Category=="Еда"&&e.Family=="Фрукты"),"Fruit UI filter does not match canonical catalog.");
        },.25f);
        Add("Catalog / fruit screenshot",()=>ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"v040-catalog-fruit.png")),.2f);
        Add("Catalog / crafting filter and capture",()=>{
            _app.UiForTests.SetFamilyForTest(6,"Материалы");
            Require(_app.UiForTests.FilteredCountForTest>0,"Native crafting filter is empty.");
        },.25f);
        Add("Catalog / crafting screenshot",()=>ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"v040-catalog-crafting.png")),.2f);
        Add("Catalog / native slime order capture",()=>_app.UiForTests.SetForTest(1,1,1),.2f);
        Add("Catalog / slime screenshot",()=>ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"v040-catalog-slimes.png")),.2f);
        Add("Transit / held physics and cancellation",()=>{
            _app.CloseMenu();_probePosition=Controller.Motor.TransientPosition;
            var player=_app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true);
            _app.Map.RequestTeleport(new MapTools.Point{Id="audit-cancel",Name="Проверка отмены",Group=player.SceneGroup,GroupName=player.SceneGroup.name,Position=_probePosition,ExactHeight=true,AllowNearbyLanding=true});
            Require(_app.Player.TransitHeld&&!_app.Player.Flight&&!Controller.Motor.enabled&&Controller._BypassGravity_k__BackingField,"Teleport did not freeze native gravity/physics before loading.");
            Require(Controller.Motor._solveMovementCollisions&&Controller.Motor._solveGrounding&&Controller.Motor.Capsule.enabled,"Transit disabled native collisions like NoClip.");
            bool refused=false;try{_app.Player.SetFlight(true);}catch(InvalidOperationException){refused=true;}
            Require(refused,"NoClip could steal an active transit hold.");
            _app.Map.AbortTeleport();
        },.2f);
        Add("Transit / cancellation restores safe ground",()=>{
            Require(!_app.Map.Busy&&!_app.Player.TransitHeld&&Controller.Motor.enabled&&!Controller._BypassGravity_k__BackingField,"Cancelled transition did not restore physics.");
            Require(Vector3.Distance(Controller.Motor.TransientPosition,_probePosition)<3.5f,"Cancelled transition did not return near its collision-checked origin.");
            Require(_app.Map.LastStableGroundSamples>=4&&_app.Map.LastSceneAssetsReady,"Return bypassed native assets/collision readiness.");
        });
        Add("God Mode / native damage",()=>{
            var state=_app.Context.PlayerState;state.SetHealth(state.GetMaxHealth());int health=state.GetCurrHealth();
            var damage=new Il2CppMonomiPark.SlimeRancher.Damage.Damage{Amount=5};
            Require(!state.Damage(damage),"Damage unexpectedly accepted under God Mode.");
            var damageable=_app.Context.Player.GetComponentInChildren<PlayerDamageable>(true);
            if(damageable!=null)Require(!damageable.Damage(damage),"Damageable accepted damage under God Mode.");
            state._model.LoseHealth(5);Require(state.GetCurrHealth()==health,"God Mode changed health.");
        });
        Add("Infinite Energy / native SpendEnergy",()=>{
            var state=_app.Context.PlayerState;state.SetEnergy(state.GetMaxEnergy());int before=state.GetCurrEnergy();
            state.SpendEnergy(12);state._model.SpendEnergy(12);Require(state.GetCurrEnergy()==before,"Energy was spent.");
        });
        float nativeSpeed=Controller._parameters._maxGroundedMoveSpeed;
        foreach(float factor in new[]{1f,2f,3f,5f})
        {
            float f=factor;Add("Speed multiplier x"+f,()=>{_app.Player.SpeedMultiplier=f;_app.Player.Tick();Require(Controller._parameters!=null&&float.IsFinite(Controller._parameters._maxGroundedMoveSpeed)&&Math.Abs(Controller._parameters._maxGroundedMoveSpeed-nativeSpeed*f)<.01f,"Speed multiplier does not match native base parameters.");});
        }
        Add("Speed reset",()=>{_app.Player.SpeedMultiplier=1;_app.Player.Tick();Require(!Controller._parameters.name.Contains("CreatorToolkit"),"Private speed clone was not restored.");});
        Add("Flight / enable and native input",()=>{_app.Input.RecoverGameplay();_probePosition=Controller.Motor.TransientPosition;_app.Player.SetFlight(true);Require(!Controller.Motor.enabled,"Native motor still running in flight.");NativeKeyboardProbe.PressAuxiliary(Keyboard.current.spaceKey);},.25f);
        Add("Flight / real vertical movement",()=>{NativeKeyboardProbe.ReleaseAuxiliary();Require(Controller.Motor.TransientPosition.y-_probePosition.y>.03f,"No upward flight movement.");_app.Input.Open();_probePosition=Controller.Motor.TransientPosition;});
        Add("Flight / menu input probe",()=>NativeKeyboardProbe.PressAuxiliary(Keyboard.current.spaceKey),.2f);
        Add("Flight / menu suppresses motion",()=>{NativeKeyboardProbe.ReleaseAuxiliary();Require(Vector3.Distance(Controller.Motor.TransientPosition,_probePosition)<.03f,"Flight moved with menu open.");_app.CloseMenu();_app.Player.SetFlight(false);Require(Controller.Motor.enabled&&Controller.Motor._solveMovementCollisions&&Controller.Motor._solveGrounding,"Flight did not restore native physics.");});
        Add("Walk after flight exit",()=>{_app.Input.RecoverGameplay();_probePosition=Controller.Motor.TransientPosition;NativeKeyboardProbe.PressW();},.2f);
        Add("Walk / release",()=>{NativeKeyboardProbe.ReleaseW();Require(Vector3.Distance(Controller.Motor.TransientPosition,_probePosition)>.03f,"Walking failed after flight.");Require(float.IsFinite(Controller.Motor.BaseVelocity.x),"Non-finite native velocity.");});
        foreach(int quantity in new[]{1,5,10,25})
        {
            int q=quantity;Add("Spawner / quantity "+q,()=>{var e=_app.Actors.Catalog.First(x=>x.Category=="Плорты");_app.Actors.Spawn(e,q);Require(_app.Actors.OwnedCount==q,"Spawn count mismatch.");_app.Actors.Cleanup();});
        }
        // Every spawnable actor definition is exercised separately, then immediately destroyed natively.
        foreach(var item in _app.Actors.Catalog.ToArray())
        {
            var entry=item;
            if(!entry.CanSpawn)
            {
                Add("Unavailable native actor / refused "+entry.Reference,()=>{bool refused=false;try{_app.Actors.Spawn(entry,1);}catch(InvalidOperationException){refused=true;}Require(refused&&_app.Actors.OwnedCount==0,"Incomplete native actor was created.");});
                continue;
            }
            Add("Actor / "+entry.Reference,()=>{
                try{_app.Actors.Spawn(entry,1);Require(_app.Actors.OwnedCount==1,"Owned count="+_app.Actors.OwnedCount+"; expected 1.");}
                finally{_app.Actors.Cleanup();}
                Require(_app.Actors.OwnedCount==0,"Owned cleanup did not finish.");
            },.04f);
        }
        Add("Spawner / cleanup scope",()=>Require(_app.Actors.OwnedCount==0,"Owned actors remain."));
        Add("Prototypes / visual fixtures",()=>{
            _app.World.SetHour(12f);
            var previews=_app.Actors.Catalog.Where(e=>e.IsPrototype).ToArray();Require(previews.Length==2,"Expected two explicitly marked visual prototypes.");
            _app.Actors.Spawn(previews[0],1,-1.2f,4.5f);_app.Actors.Spawn(previews[1],1,1.2f,4.5f);
            _app.Cinema.SetHideHud(true);
        },.8f);
        Add("Prototypes / private materials and mosaic facets",()=>{
            Require(_app.Actors.PrototypeApplied==2,"Prototype visuals were not applied.");
            Require(_app.Actors.PrototypeFaceted>0,"Mosaic preview has no faceted geometry.");
            ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"prototype-visuals.png"));
        },.3f);
        Add("Prototypes / owned cleanup",()=>{_app.Cinema.SetHideHud(false);_app.Actors.Cleanup();});
        Add("Currency +10 000",()=>{int before=_app.Actors.Currency(),cap=_app.Context.PlayerState._model.maxCurrency;_app.Actors.AddCurrency(10000);Require(_app.Actors.Currency()==Math.Min((long)cap,(long)before+10000),"10k currency mismatch.");});
        Add("Currency +100 000",()=>{int before=_app.Actors.Currency(),cap=_app.Context.PlayerState._model.maxCurrency;_app.Actors.AddCurrency(100000);Require(_app.Actors.Currency()==Math.Min((long)cap,(long)before+100000),"100k currency mismatch.");});
        Add("Currency native maximum",()=>{_app.Actors.AddCurrency(0,true);Require(_app.Actors.Currency()==_app.Context.PlayerState._model.maxCurrency,"Native currency cap mismatch.");});
        Add("Ammo / 100 and slot selection",()=>{
            var ammo=_app.Context.PlayerState.Ammo;int slot=Enumerable.Range(0,ammo.Slots.Length).First(i=>ammo.Slots[i].IsUnlocked);
            ammo.SetSelectedSlot(slot);Require(ammo._selectedAmmoIdx==slot,"Cannot select ammo slot.");
            var entry=_app.Actors.Catalog.First(e=>e.Category=="Плорты");_app.Actors.FillAmmoForTest(entry,slot);
            Require(ammo.GetSlotCount(slot)==ModInterop.FillTarget(ammo.Slots[slot])&&ammo.GetSlotName(slot)==entry.Type,"Ammo count/id mismatch.");
        });
        Add("Auto refill / confirmation required",()=>{
            _app.Convenience.RequestRefill();
            Require(_app.Pending!=null&&!_app.Convenience.Refilling,"Auto refill enabled before approval.");
            _app.ShowTabForTest(7);_app.Input.Open();
        },.3f);
        Add("Auto refill / confirmation UI capture",()=>ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"convenience-refill-confirmation.png")),.3f);
        Add("Auto refill / cancelled unchanged",()=>{
            _app.CloseMenu();var ammo=_app.Context.PlayerState.Ammo;
            Require(_app.Pending==null&&!_app.Convenience.Refilling&&ammo.GetSlotCount(ammo._selectedAmmoIdx)==ModInterop.FillTarget(ammo.Slots[ammo._selectedAmmoIdx]),"Cancelled refill changed the slot or left automation armed.");
        });
        Add("Auto refill / approved request",()=>{
            _app.Convenience.RequestRefill();Require(_app.Pending!=null,"No refill approval request.");
            _app.AcceptConfirmation();
        },.3f);
        Add("Auto refill / approved binding",()=>{
            Require(_app.Pending==null&&_app.Convenience.Refilling,"Approved refill did not bind to its selected native slot.");
            _app.Convenience.StopRefill("Проверка подтверждения завершена");
        });
        Add("Auto refill / above threshold unchanged",()=>{
            var ammo=_app.Context.PlayerState.Ammo;var slot=ammo.Slots[ammo._selectedAmmoIdx];int above=SafetyPolicy.RefillAt(ModInterop.FillTarget(slot))+5;slot.Count=above;
            _app.Convenience.ArmRefillForTest();_app.Convenience.TickRefill();Require(slot.Count==above,"Auto refill ran above its threshold.");
        });
        Add("Auto refill / threshold and native callbacks",()=>{
            var ammo=_app.Context.PlayerState.Ammo;var slot=ammo.Slots[ammo._selectedAmmoIdx];int fill=ModInterop.FillTarget(slot);slot.Count=SafetyPolicy.RefillAt(fill);
            int before=_app.Convenience.RefillOperations;_app.Convenience.TickRefill();
            Require(slot.Count==fill&&ammo.GetSlotCount(ammo._selectedAmmoIdx)==fill&&_app.Convenience.RefillOperations>before,"Native auto refill did not produce exactly 100.");
            _app.ShowTabForTest(7);_app.Input.Open();
        },.3f);
        Add("Auto refill / armed UI capture",()=>ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"convenience-refill-armed.png")),.3f);
        Add("Auto refill / close UI",()=>_app.CloseMenu());
        Add("Auto refill / empty disarms",()=>{
            var ammo=_app.Context.PlayerState.Ammo;var slot=ammo.Slots[ammo._selectedAmmoIdx];slot.Count=0;
            _app.Convenience.TickRefill();Require(!_app.Convenience.Refilling&&slot.Count==0,"Empty slot was silently repopulated.");
        });
        Add("Auto refill / slot change disarms",()=>{
            var ammo=_app.Context.PlayerState.Ammo;int original=ammo._selectedAmmoIdx;
            var entry=_app.Actors.Catalog.First(e=>e.Category=="Плорты"&&_app.Actors.CanStore(e,original));
            _app.Actors.FillAmmoForTest(entry,original);_app.Convenience.ArmRefillForTest();
            int other=Enumerable.Range(0,ammo.Slots.Length).First(i=>i!=original&&ammo.Slots[i].IsUnlocked);
            ammo.SetSelectedSlot(other);_app.Convenience.TickRefill();Require(!_app.Convenience.Refilling,"Slot change did not stop auto refill.");ammo.SetSelectedSlot(original);
        });
        Add("Auto refill / new resource preserved",()=>{
            var ammo=_app.Context.PlayerState.Ammo;int index=ammo._selectedAmmoIdx;var old=ammo.Slots[index].Id;
            _app.Convenience.ArmRefillForTest();
            var entry=_app.Actors.Catalog.First(e=>e.Type!=old&&e.Category=="Плорты"&&_app.Actors.CanStore(e,index));
            _app.Actors.FillAmmoForTest(entry,index);_app.Convenience.TickRefill();
            Require(!_app.Convenience.Refilling&&ammo.Slots[index].Id==entry.Type&&ammo.Slots[index].Count==ModInterop.FillTarget(ammo.Slots[index]),"Newly inserted loot was changed.");
        });
        Add("Time +1 hour",()=>{var t=_app.Context.TimeDirector;double target=t.HoursFromNow(1);_app.World.AdvanceHours(1);Require(Math.Abs(t.WorldTime()-target)<2,"1h time mismatch.");});
        Add("Time +6 hours",()=>{var t=_app.Context.TimeDirector;double target=t.HoursFromNow(6);_app.World.AdvanceHours(6);Require(Math.Abs(t.WorldTime()-target)<2,"6h time mismatch.");});
        foreach(float hour in new[]{6f,12f,18f,0f}){float h=hour;Add("Time preset "+h,()=>{_app.World.SetHour(h);Require(Math.Abs(_app.World.Hour()-h)<.04f,"Hour preset mismatch.");});}
        double frozenAt=0;
        Add("Freeze time / on",()=>{_app.World.SetFreezeTime(true);frozenAt=_app.Context.TimeDirector.WorldTime();},.25f);
        Add("Freeze time / stable and restore",()=>{Require(Math.Abs(_app.Context.TimeDirector.WorldTime()-frozenAt)<.01,"World time advanced while frozen.");_app.World.SetFreezeTime(false);Require(!_app.Context.TimeDirector._worldModel.pauseWorldTime,"World time remained paused.");});
        _app.World.RefreshWeather();
        Add("Weather / registered current-zone catalog",()=>Require(_app.World.Weather.Count>0,"No weather states; current zone="+_app.World.ZoneName));
        foreach(var choice in _app.World.Weather.ToArray())
        {
            var weather=choice;Add("Weather / "+weather.Name,()=>{_app.World.SetWeather(weather);Require(_app.Context.WeatherRegistry.DEBUG_GetRunningWeather(weather.Zone).Count>0,"No running native weather pattern.");},.2f);
        }
        foreach(string preset in new[]{"rain","thunder","slime","pollen","wind"})
        {
            string p=preset;
            Add("Weather / preset "+p,()=>{
                var choice=_app.World.FindPreset(p);
                // An absent zone pattern is legitimate, but a registered matching
                // state must never be disabled merely because its map tier is zero.
                string token=p=="slime"?"slime":p;
                Require(choice!=null||!_app.World.Weather.Any(e=>e.Search.Contains(token)),"Registered weather preset disabled: "+p);
                if(choice==null)return;
                _app.World.SetWeather(choice);
                Require(_app.Context.WeatherRegistry.DEBUG_GetRunningWeather(choice.Zone).Count>0,"Preset did not start native weather: "+p);
            },.2f);
        }
        Add("Weather / clear",()=>{_app.World.ClearWeather();});
        Add("Weather / no orphaned cleared pattern",()=>{
            Require(_app.Context.WeatherRegistry.DEBUG_GetRunningWeather(_app.World.WeatherZone).Count==0,
                "Clear left weather instances with null state for native region registration.");
        });
        Add("Climate / live registry and WeatherDirector snapshot",()=>{
            _app.World.RefreshWeather();
            Require(_app.World.ActiveWeather.Count==0&&_app.World.NativeDirectorStates==0&&
                _app.World.ActiveWeatherSummary.Contains("ясно"),"Cleared current-zone climate snapshot is stale.");
            var native=_app.World.FindPreset("rain");
            Require(native!=null,"Current test zone has no registered native rain fixture.");
            _app.World.SetWeather(native);
            Require(_app.World.ActiveWeather.Count>0&&_app.World.ActiveWeatherSummary.Contains(native.State.GetName()),
                "Live climate panel did not read native current states.");
            _app.ShowTabForTest(3);_app.Input.Open();
        },.3f,()=>_app.World.ClimateClearForTest(),5f);
        Add("Climate / active native weather capture",()=>ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"v060-weather-active.png")),.3f);
        foreach(string key in new[]{"lava","solar"})
        {
            string preset=key;
            Add("Climate / zone-gated "+preset+" availability",()=>{
                var entry=_app.World.FindPreset(preset);
                if(entry==null){Require(_app.World.Weather.Count>0,"Native zone catalog disappeared.");return;}
                Require(entry.Zone==_app.World.WeatherZone&&_app.World.Weather.Contains(entry),"Climate preset fabricated an unregistered zone state.");
                _app.World.SetWeather(entry);
                Require(_app.World.ActiveWeather.Count>0,"Registered climate preset did not start native weather.");
            },.2f);
        }
        Add("Climate / clear preserves future native forecast",()=>{
            _app.CloseMenu();
            var data=_app.Context.WeatherRegistry._model._zoneDatas[_app.World.WeatherZone];
            double now=_app.Context.TimeDirector.WorldTime();
            int future=0;
            for(int i=0;i<data.Forecast.Count;i++)if(data.Forecast[i].StartTime>now)future++;
            _app.World.ClearWeather();
            int remaining=0;
            for(int i=0;i<data.Forecast.Count;i++)if(data.Forecast[i].StartTime>now)remaining++;
            Require(future==remaining,"Climate clear removed future forecasts: before="+future+" after="+remaining);
        });
        Add("Climate / native director exits after clear",()=>{
            Require(_app.World.ClimateClearForTest(),"Native director did not exit: registry="+
                _app.World.ActiveWeather.Count+" director="+_app.World.NativeDirectorStates);
        },.12f,()=>_app.World.ClimateClearForTest(),5f);
        Add("Tarr / disable",()=>{_app.World.SetDisableTarr(true);Require(!GameContext.Instance.GameSettingsDirector.tarrEnabled.Value,"Tarr setting still enabled.");});
        Add("Tarr / restore",()=>_app.World.SetDisableTarr(false));
        Add("Freeze mobs / fixture",()=>{_app.Actors.Spawn(_app.Actors.Catalog.First(e=>e.Category=="Слаймы"),1);},.15f);
        Add("Freeze mobs / on",()=>{_app.World.SetFreezeMobs(true);Require(_app.World.FrozenCount>0,"No native slime bodies frozen.");});
        Add("Freeze mobs / restore",()=>{_app.World.SetFreezeMobs(false);Require(_app.World.FrozenCount==0,"Frozen snapshots remain.");_app.Actors.Cleanup();});
        Add("HUD / hide",()=>{_app.Cinema.SetHideHud(true);Require(!_app.Context.HudVisible.Value,"HUD remained visible.");});
        Add("HUD / restore",()=>{_app.Cinema.SetHideHud(false);Require(_app.Context.HudVisible.Value,"HUD not restored.");});
        Add("Waypoints / five and limit",()=>{_app.Cinema.ResetWaypoints();for(int i=0;i<5;i++)_app.Cinema.SaveWaypoint("Test "+i);Require(_app.Cinema.Waypoints.Count==5,"Waypoint count mismatch.");bool rejected=false;try{_app.Cinema.SaveWaypoint("Overflow");}catch(InvalidOperationException){rejected=true;}Require(rejected,"Sixth waypoint allowed.");});
        Add("Waypoint / native teleport",()=>{var point=_app.Cinema.Waypoints[0];Controller.Motor.SetPosition(point.Position+Vector3.up*.25f,true);_app.Cinema.Teleport(point);},.25f);
        Add("Waypoint / collision-safe saved landing and remove",()=>{
            var target=_app.Cinema.Waypoints[0].Position;var actual=Controller.Motor.TransientPosition;var delta=actual-target;
            Require(SafetyPolicy.NearbyReturnOffset(delta.x,delta.y,delta.z)&&_app.Player.IsClearPosition(actual)&&
                !_app.Player.TransitHeld&&Controller.Motor.enabled&&_app.Map.LastSceneAssetsReady,
                "Saved point did not reach documented bounded collision-safe ground: target="+target.ToString("F3")+" actual="+actual.ToString("F3")+" "+_app.Map.TransitDetails);
            _app.LoggerInstance.Msg("Saved waypoint actual offset: "+delta.ToString("F3")+"; "+_app.Map.TransitDetails);
            _app.Cinema.Waypoints.RemoveAt(0);Require(_app.Cinema.Waypoints.Count==4,"Waypoint delete failed.");
        });
        Add("SOS / safe grounded checkpoint",()=>{
            _app.Convenience.SetSafeTracking(true);Require(_app.Convenience.CaptureSafe()||_app.Convenience.HasSafe,"No grounded collision-free SOS checkpoint recorded.");_probePosition=_app.Convenience.SafePosition;
        },.15f);
        Add("SOS / guarded return start",()=>{
            Require(_app.Convenience.HasSafe,"SOS checkpoint unavailable.");
            Require(_app.Player.TryMapGround(_probePosition+Vector3.right*4f,true,out var ground),"Nearby fixture has no safe surface.");
            _app.Player.PlaceForMap(ground);_app.Convenience.Rescue();
        },.15f);
        Add("SOS / landing and reset",()=>{
            Require(Vector3.Distance(Controller.Motor.TransientPosition,_probePosition)<1.5f&&Controller.Motor.enabled&&_app.Map.HasReturn,"SOS did not safely restore the checkpoint and motor.");
            _app.Convenience.SetSafeTracking(false);Require(!_app.Convenience.HasSafe&&!_app.Convenience.TrackSafe,"SOS did not clear on disable.");
        });
        Add("Permanent confirmation / cancel",()=>{_app.Confirm("Test","No permanent action must run.",()=>throw new InvalidOperationException("Cancelled confirmation executed."));_app.CloseMenu();Require(_app.Pending==null,"Closing did not discard confirmation.");});
        Add("Pedia / all entries",()=>{_app.Context.PediaDirector.DebugClearUnlocked();_app.Unlocks.Pedia();Require(_app.Context.PediaDirector._pediaModel.unlocked.Count>=_app.Context.PediaDirector.TotalNumberOfPediaEntries(),"Not all registered Pedia entries unlocked.");});
        Add("Upgrades / all native levels",()=>{_app.Unlocks.Upgrades();var m=_app.Context.GameModel.player.GetUpgradeModel();Require(m.upgradeDefinitions.Count>0,"No registered upgrade definitions.");for(int i=0;i<m.upgradeDefinitions.Count;i++)Require(!m.IncrementUpgradeLevel(m.upgradeDefinitions[i]),"Upgrade not at its native maximum: "+m.upgradeDefinitions[i].name);});
        Add("Doors / native models and callbacks",()=>{_app.Unlocks.Doors();foreach(var pair in _app.Context.GameModel.AllDoors())Require(pair.Value==null||pair.Value.state==Il2CppMonomiPark.World.AccessDoor.State.OPEN,"Closed registered door remains.");});
        BuildCareSteps();
        BuildExplorerSteps();
        Add("Map / native reveal",()=>_app.Unlocks.Map(),2f);
        Add("Map / reveal completion",()=>{
            Require(!_app.Unlocks.MapPending,"Map reveal still pending: "+_app.Map.DisplayStatus);
            Require(_app.Unlocks.RevealedZones>0&&_app.Unlocks.RevealedFogEvents>0,"No native zone/fog events revealed: "+_app.Unlocks.MapResult);
            foreach(var data in UnityEngine.Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.World.ZoneMapData>())
                if(data?.PrimaryZone!=null)Require(!_app.Context.MapDirector.IsZoneHidden(data),"Map zone remains hidden: "+data.name);
            Require(_app.Map.NativeMapOpen,"Native map did not open: "+_app.Map.DisplayStatus);
            Require(_app.Map.ProjectionError>=0&&_app.Map.ProjectionError<10f,"Map projection mismatch: "+_app.Map.ProjectionError);
            ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"atlas-revealed.png"));
        },.3f);
        Add("Map / overlay spacing and readable labels",()=>{
            var markers=_app.Map.MarkerHitRects;var labels=_app.UiMapLabelsForTest;
            Require(markers.Count>0&&labels.Count>0,"Native map overlay has no markers or readable zone labels.");
            // Exact map positions mean neighbour hit boxes may legitimately
            // overlap in a dense layer: icons keep one fixed size and never
            // move, so overlap never hides a marker's own coordinates.
            foreach(var chrome in _app.Map.NativeChromeRects)
            {
                foreach(var marker in markers)Require(!chrome.Overlaps(marker),"Marker covers a native map title/counter/zoom control.");
                foreach(var label in labels)Require(!chrome.Overlaps(label),"Zone label covers native map chrome.");
            }
            for(int i=0;i<labels.Count;i++)
            {
                foreach(var marker in markers)Require(!labels[i].Overlaps(marker),"Zone name is covered by a marker.");
                for(int j=i+1;j<labels.Count;j++)Require(!labels[i].Overlaps(labels[j]),"Zone names overlap.");
            }
        },.15f);
        Add("Map / native marker coordinates",()=>{
            int checkedPods=0;
            foreach(var pod in UnityEngine.Object.FindObjectsOfType<TreasurePod>())
            {
                var point=_app.Map.Points.FirstOrDefault(p=>p.Category=="Капсулы"&&p.Id==pod.Id);
                Require(point!=null,"Loaded pod absent from index: "+pod.Id);
                Require(Vector3.Distance(point.Position,pod.transform.position)<.3f,"Pod transform mismatch: "+pod.Id+" baked="+point.Position+" native="+pod.transform.position);
                checkedPods++;
            }
            Require(checkedPods>0,"No loaded pods available for coordinate validation.");
        },.12f);
        Add("Exploration / nearest unopened native capsule",()=>{
            var point=_app.Map.NearestUnopenedCapsule();Require(point!=null&&!_app.Map.Opened(point),"Nearest capsule is missing or already opened.");
            _app.Map.Focus(point);Require(_app.Map.Selected==point,"Nearest capsule did not focus on the native map.");
        },.3f);
        Add("Capsules / detailed contents capture",()=>{
            var point=_app.Map.Selected;
            Require(point?.Category=="Капсулы"&&_app.Map.Capsules.Get(point.Id).Confirmed,"Selected pod has no confirmed ID rewards.");
            Require(_app.Map.OverlayCardRects.Count==1,"Detailed capsule popover did not register its click-through shield.");
            foreach(var reward in _app.Map.Capsules.Get(point.Id).Rewards)
                Require(_app.Map.Icons.Reward(reward.Key)!=null,"Original reward icon unavailable: "+reward.Key);
            ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"v040-capsule-contents.png"));
        },.2f);
        Add("NativeIcons / genuine map and reward assets",()=>{
            foreach(string category in MapTools.Categories)
                Require(CanonMapIconPolicy.Owns(category)?FandomMapMarkers.Atlas!=null:_app.Map.Icons.Category(category)!=null,
                    (CanonMapIconPolicy.Owns(category)?"Fandom wiki marker atlas unavailable: ":"Original map category sprite unavailable: ")+category);
            Require(_app.Map.Icons.Category(CustomWaypointTools.Category)!=null,"Original native waypoint flag unavailable.");
            Require(_app.Map.Icons.AssetRoots<=NativeIconTools.AssetBudget&&_app.Map.Icons.DiscoveryScans<=2,
                "Native artwork discovery/borrowed asset cache is unbounded.");
            foreach(var reward in _app.Map.Capsules.Database.Assets.Values)
                Require(_app.Map.Icons.Reward(reward.Key)!=null,"Original native reward sprite unavailable: "+reward.AssetName);
        });
        Add("Exploration / route capture",()=>{
            ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"convenience-capsule-route.png"));
            NativeKeyboardProbe.PressAuxiliary(Keyboard.current.escapeKey);
        },.12f);
        Add("Map / native close",()=>NativeKeyboardProbe.ReleaseAuxiliary(),.3f);
        BuildPhenomenaSteps();
        Add("Map / LMB teleport probe start",()=>_app.MapProbe.Start(),.2f);
        Add("Map / LMB teleport and return",()=>Require(_app.MapProbe.Status=="PASS","Native map interaction probe: "+_app.MapProbe.Status));
        if(_stressRequested)
        {
            Add("Stress / isolated performance and reload run",StartStress,.2f);
            Add("Stress / completed without native errors or growing owned-object counts",
                AssertStressCompleted);
        }
        Add("Save / native serialization of modified copy",SerializeModifiedCopy);
    }

    private void StartStress()
    {
        GuardCopy();
        RetireExplorerFixtures();
        RetireCareFixtures();
        RetirePhenomenaFixtures();
        RetireOptimizationFixtures();
        RetireLoFixtures();
        RetireUiReferences();
        // Executed integration-test closures can capture native actors from the
        // old world. Retire the steps before the retained-memory/reload test so
        // the harness itself cannot root those objects and fake a game leak.
        _steps.RemoveRange(0,_index);_index=0;
        _app.Stress.Start();
    }
    private void AssertStressCompleted()=>Require(_app.Stress.Status=="PASS","Stress result: "+_app.Stress.Status);
    private void CapturePersistenceState()
    {
        GuardCopy();
        var ammo=_app.Context.PlayerState.Ammo;
        Require(ammo?.Slots!=null&&ammo.Slots.Length>0,"Native ammo snapshot unavailable.");
        _savedAmmoSlots.Clear();
        for(int i=0;i<ammo.Slots.Length;i++)
        {
            int count=ammo.GetSlotCount(i);
            // Empty slots may legitimately have no persisted identifier.
            // Counts are always exact; populated identifiers remain exact too.
            string reference=count>0?ammo.GetSlotName(i)?.ReferenceId??"":"";
            Require(count>=0&&(count==0||reference.Length>0),"Invalid native ammo snapshot at slot "+i);
            _savedAmmoSlots.Add(new AmmoExpectation{Slot=i,Count=count,Reference=reference});
        }
        _savedAmmoSlot=ammo._selectedAmmoIdx;
        Require(_savedAmmoSlot>=0&&_savedAmmoSlot<_savedAmmoSlots.Count,"Invalid selected native ammo index.");
        var selected=_savedAmmoSlots[_savedAmmoSlot];
        _savedAmmoCount=selected.Count;_savedAmmoRef=selected.Reference;
        _savedCurrency=_app.Actors.Currency();_savedPedia=_app.Context.PediaDirector._pediaModel.unlocked.Count;
        _app.LoggerInstance.Msg("Native persistence expected slots: "+JsonSerializer.Serialize(_savedAmmoSlots));
    }
    private void SerializeModifiedCopy()
    {
            GuardCopy();
            _app.CloseMenu();_app.Convenience.Reset();_app.Farm.Reset();_app.Radar.Reset();_app.Custom.HideGoal();
            _app.World.Reset();_app.Player.Reset();_app.Cinema.Reset();_app.Care.Reset();_app.Gadgets.Reset();RetireExplorerFixtures();RetireCareFixtures();RetirePhenomenaFixtures();RetireOptimizationFixtures();RetireLoFixtures();RetireUiReferences();
            Require(!_app.Convenience.Refilling&&!_app.Convenience.TrackSafe&&!_app.Convenience.ShowHud&&_app.Convenience.Profile==0,"Conveniences remain armed before copy serialization.");
            CapturePersistenceState();
            _recovery.CustomMarkers=_app.Custom.ExportForTest();
            Save.SetAutosaveDisabled(false);
            try
            {
                Require(Save.SaveAllNow(true,null),"Test copy failed native save serialization.");
                var written=Save.SaveGame_GetSummary(true,null);
                Require(written!=null&&written.Name==_recovery.TestName&&!written.IsInvalid,
                    "Native serialized checkpoint did not identify its owned copy.");
                Save._storageProvider.Flush();
                _recovery.TestSave=written.SaveName;
                // After immutable stress reloads, use the EXACT native write
                // just returned, not an independently selected latest file.
                FindStoredSummary(_recovery.TestName,_recovery.TestSave);
                _app.LoggerInstance.Msg("Native persistence exact checkpoint: "+_recovery.TestSave);
            }
            finally{Save.SetAutosaveDisabled(true);}
            VerifyMainHashes();
    }

    internal void Tick()
    {
        try
        {
            if(_quitting)return;
            if(_phase<0)
            {
                TickMainAutosaveHold();
                if(!_requested)return;
                if(!_app.Ready)return;
                _requested=false;BeginCopy();return;
            }
            Application.runInBackground=true;
            if(_phase==4)
            {
                RestoreInput();
                if(_recovery==null){FinishReturn();return;}
                if(GameContext.Instance?.AutoSaveDirector==null)return;
                RequestBoot("return");return;
            }
            if(_phase==5)
            {
                if(!_app.Ready)return;
                var meta=NativeSaveMetadata.Read(Save);if(meta==null||!meta.HasValue||meta.Value.GameName!=_recovery.MainName)return;
                FinishReturn();return;
            }
            if(_phase==1)
            {
                if(!_app.Ready||!_app.Tests.Status.StartsWith("PASS",StringComparison.Ordinal))return;
                var meta=NativeSaveMetadata.Read(Save);
                Require(_freshBoot&&_mainModel==null,"Fresh-process isolation not established.");
                Require(meta!=null&&meta.HasValue&&meta.Value.GameName==_recovery.TestName,"Isolated boot loaded an unexpected save.");
                _isolatedSession=_app.Session;VerifyMainHashes();
                _recovery.Boot="return";WriteState(); // A crash during the matrix returns to the original, never repeats permanent operations.
                Save.SetAutosaveDisabled(true);BuildSteps();_phase=2;_next=Time.unscaledTime;return;
            }
            if(_phase==3)
            {
                if(!_app.Ready||!_app.Tests.Status.StartsWith("PASS",StringComparison.Ordinal))return;
                _isolatedSession=_app.Session;
            }
            if(_phase==2&&_app.Stress.Active)
            {
                _app.Stress.Tick();Status=_app.Stress.Status;return;
            }
            if(_phase==2&&(_app.Map.Busy||_matrixTransitPending))
            {
                if(!_matrixTransitPending)
                {_matrixTransitPending=true;_matrixTransitDeadline=Time.unscaledTime+60f;VerifyMainHashes();}
                Require(Time.unscaledTime<_matrixTransitDeadline,"Owned matrix scene transition exceeded its bounded wait.");
                if(_app.Map.Busy||!_app.Ready){Status="Ожидание завершения штатного перехода проверочной копии";return;}
                // No permanent step runs while a scene unloads. A new native
                // context is accepted only after the exact same owned save and
                // original bytes pass the existing strong rebind checks.
                RebindStressCopy();_matrixTransitPending=false;
            }
            GuardCopy();Save.SetAutosaveDisabled(true);
            if(_phase==3)
            {
                var ammo=_app.Context.PlayerState.Ammo;
                Require(_app.Actors.Currency()==_savedCurrency,"Currency was not persisted in isolated save.");
                Require(_app.Context.PediaDirector._pediaModel.unlocked.Count==_savedPedia,"Pedia unlocks were not persisted.");
                Require(_savedAmmoSlots.Count>0,"Expected persisted ammo snapshot missing.");
                foreach(var expected in _savedAmmoSlots)
                {
                    int actualCount=ammo.GetSlotCount(expected.Slot);
                    string actualRef=actualCount>0?ammo.GetSlotName(expected.Slot)?.ReferenceId??"":"";
                    _app.LoggerInstance.Msg("Native persistence slot "+expected.Slot+": expected="+expected.Reference+" x"+expected.Count+
                        " actual="+actualRef+" x"+actualCount+" checkpoint="+_recovery.TestSave);
                    Require(actualCount==expected.Count&&actualRef==expected.Reference,
                        "Ammo was not persisted at slot "+expected.Slot+": expected="+expected.Reference+" x"+expected.Count+
                        " actual="+actualRef+" x"+actualCount+" checkpoint="+_recovery.TestSave);
                }
                Record(true,"Native save/reload persistence");
                Require(_recovery.CustomMarkers.Length>0&&_app.Custom.ExportForTest()==_recovery.CustomMarkers,
                    "Named colored custom markers did not persist across the final fresh process.");
                Require(!_app.Custom.ShowCompass&&!_app.Map.CustomPointMode&&!_app.Radar.Enabled&&!_app.Farm.Enabled&&!_app.Care.Inspect&&_app.Gadgets.Catalog.Count==0&&!_app.Phenomena.Inspect,
                    "New exploration automation was armed after a fresh world reload.");
                foreach(string id in _app.Custom.Records.Select(p=>p.Id).ToArray())_app.Custom.Delete(id);
                _app.Custom.DeleteOwnedAuditFile(_recovery.TestName);
                Record(true,"CustomWaypoints / fresh-process persistence and owned-file cleanup");
                _completed=true;RequestBoot("return");return;
            }
            if(_phase==2&&Time.unscaledTime>=_next&&!_app.UiProbe.Active&&!_app.MapProbe.Active&&!_app.Map.Busy&&!_app.Unlocks.MapPending)
            {
                if(_index>=_steps.Count)
                {
                    Require(_savedAmmoSlots.Count>0,"Final owned serialization did not capture expected slots.");
                    FindStoredSummary(_recovery.TestName,_recovery.TestSave);
                    RequestBoot("verify");return;
                }
                var candidate=_steps[_index];
                if(candidate.Name=="Stress / isolated performance and reload run"&&_failed>0)
                {
                    Record(false,"Stress / acceptance gate","Native feature failures remain; stress was not started or accepted.");
                    Cancel();return;
                }
                if(candidate.AwaitReady!=null)
                {
                    if(_waitingStep!=_index){_waitingStep=_index;_stepWaitUntil=Time.unscaledTime+candidate.WaitSeconds;}
                    if(!candidate.AwaitReady())
                    {
                        if(Time.unscaledTime<_stepWaitUntil)
                        {Status="Ожидание нативной готовности: "+candidate.Name;_next=Time.unscaledTime+.1f;return;}
                        _index++;_waitingStep=-1;
                        Record(false,candidate.Name,"Bounded native readiness timed out: "+_app.Map.DisplayStatus);
                        if(CriticalNativeStep(candidate.Name)){RetireUiReferences();Cancel();return;}
                        _next=Time.unscaledTime+candidate.Delay;return;
                    }
                    _waitingStep=-1;
                }
                if(candidate.Name=="UI / native LMB interaction start"||candidate.Name=="Map / LMB teleport probe start")
                {
                    var mouse=Mouse.current;
                    if(mouse==null||mouse.leftButton.isPressed||mouse.rightButton.isPressed)
                    {
                        if(_pointerWaitUntil==0)_pointerWaitUntil=Time.unscaledTime+90f;
                        Require(Time.unscaledTime<_pointerWaitUntil,"Native pointer remained unavailable/held for 90 s; user input was not overridden.");
                        Status="Проверка ожидает свободную мышь: отпустите ЛКМ/ПКМ. Ввод пользователя не переопределяется.";
                        _next=Time.unscaledTime+.25f;return;
                    }
                    _pointerWaitUntil=0;
                }
                var step=_steps[_index++];Status=$"Проверка {_index}/{_steps.Count}: {step.Name}";
                try{step.Run();Record(true,step.Name);}
                catch(Exception e)
                {
                    NativeKeyboardProbe.ReleaseW();NativeKeyboardProbe.ReleaseAuxiliary();
                    Record(false,step.Name,e.ToString());
                    if(CriticalNativeStep(step.Name)){RetireUiReferences();Cancel();return;}
                }
                _next=Time.unscaledTime+step.Delay;
            }
        }
        catch(Exception e)
        {
            // Reporting is best-effort; durable recovery and input cleanup do
            // not depend on the same failing disk write succeeding again.
            try{Record(false,"suite/isolation",e.ToString());}
            finally
            {
                _requested=false;_phase=-1;
                try{RestoreInput();}catch{/* Keep the durable checkpoint and static save guard. */}
                WriteReport(true);
                Status=_recovery!=null?"Проверка остановлена; сохранена точка безопасного возврата. Запись основного сейва заблокирована.":
                    "Проверка не запущена: безопасная изоляция не подтверждена.";
                try{_app.Fail("audit recovery required",e);}catch{/* Never rethrow only because diagnostics failed. */}
            }
        }
    }
    private void FinishReturn()
    {
        _app.Tests.CancelRequest(); // Returning main is loaded, never tested.
        VerifyMainHashes();
        var main=FindStoredSummary(_recovery.MainName,_recovery.MainSave);
        Save.SaveProfileImpl(main);Save._storageProvider.Flush();
        if(_recovery!=null&&_recovery.TestName!=_recovery.MainName)
        {
            var remaining=Directory.GetFiles(_recovery.SavePath,_recovery.TestName+"_*.sav");
            if(remaining.Length>0)
            {
                var owned=FindStoredSummary(_recovery.TestName);
                Require(owned.DisplayName.StartsWith("[CT AUDIT]",StringComparison.Ordinal),"Refusing to delete a save not marked as the owned audit copy.");
                Save.DeleteGame(_recovery.TestName);
            }
        }
        RetireOwnedAuditSidecar();
        // Prove copy retirement BEFORE removing the durable recovery route.
        // Failed cleanup/proof must remain recoverable, never silently orphaned.
        HoldAutosaveAndRecordReturn();Application.runInBackground=_oldBackground;
        _app.Player.GodMode=_god;_app.Player.InfiniteEnergy=_energy;_app.Player.SpeedMultiplier=_speed;
        _app.Player.Tick();if(_flight)_app.Player.SetFlight(true);
        if(_flight)Controller.Motor.SetPosition(_position,true);
        if(_freezeTime)_app.World.SetFreezeTime(true);if(_freezeMobs)_app.World.SetFreezeMobs(true);if(_tarr)_app.World.SetDisableTarr(true);if(_hud)_app.Cinema.SetHideHud(true);
        foreach(var point in _waypoints){point.Session=_app.Session;_app.Cinema.Waypoints.Add(point);}
        _steps.Clear();_steps.TrimExcess();_waypoints.Clear();RetireExplorerFixtures();RetireCareFixtures();RetirePhenomenaFixtures();RetireOptimizationFixtures();RetireLoFixtures();RetireUiReferences();
        Require(WriteReport(true),"Final audit report could not be committed: "+_reportWriteFailure);
        VerifyMainHashes();AuditSaveGuard.VerifyOriginals(true);
        AuditCheckpointStore.Write(Path.Combine(_app.DataDirectory,"audit-terminal.json"),JsonSerializer.Serialize(new
        {
            RunId=_recovery.RunId,ImmutableBaselineSha256=_recovery.ImmutableBaselineSha256,
            MainName=_recovery.MainName,MainSave=_recovery.MainSave,Copy=_recovery.TestName,
            DllSha256=Hash(typeof(ToolkitMod).Assembly.Location),ReturnedUtc=DateTime.UtcNow.ToString("o"),
            ReturnCompleted=true,CopyRetired=true,OriginalBytesVerified=true,OriginalHashFiles=_recovery.MainHashes.Count,
            MainNativeTestsSkipped=true,Passed=_passed,Failed=_failed,Completed=_completed,
            FeatureResult=_failed>0?"FAILED":_completed?"PASS":"INCOMPLETE",ReleaseAccepted=false,
            BeforeProcessExit=true,PostExitHashVerificationRequired=true,ReportWriteFailed=_reportWriteFailed,
            ReportSha256=Hash(Path.Combine(_app.DataDirectory,"functional.txt")),Protection=AuditSaveGuard.Evidence()
        }));
        // Every required fallible restore/report/terminal commit precedes
        // retirement of both current and previous durable recovery metadata.
        if(File.Exists(Marker))File.Delete(Marker);
        string stressMarker=Path.Combine(_app.DataDirectory,"stress.request");
        if(_stressRequested&&File.Exists(stressMarker))File.Delete(stressMarker);
        AuditCheckpointStore.Retire(StateFile,_recovery.RunId);
        _phase=-1;_requested=false;_recovery=null;_mainModel=null;
        Status=$"Проверка: {_passed} PASS / {_failed} FAIL. Возврат без записи основного сейва; защита действует до выхода.";
        _app.Notify(Status);
        if(_quitAfter){_quitAfter=false;_app.SaveAndQuit();}
    }
}
