using System;
using System.Collections.Generic;
using System.IO;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;
using Il2CppMonomiPark.SlimeRancher.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CreatorToolkit;

// Opt-in runtime tests. No currency/ammo/spawn/unlocks/weather commands.
// Full native keyboard state event exercises Unity input -> actions -> controller, NOT OS focus or a velocity override.
internal sealed class SmokeTests
{
    private readonly ToolkitMod _app;
    private readonly List<string> _results = new();
    private bool _requested, _movementOnly, _startAttempted, _continueAttempted, _autoSaveWasDisabled, _restoreSave;
    private bool _hadKey, _sawKey, _failed, _restorePlayer, _keyInjected;
    private bool _autoContinue, _restoreBackground, _oldRunInBackground, _preparingMovement;
    private int _stage = -1, _rendered;
    private float _deadline, _lastContinue;
    private float _tabCaptureDeadline;
    private float _nativeUiCloseAfter;
    private int _nativeUiCloseRequests;
    private bool _tabCaptureQueued;
    private DateTime _tabCaptureRequestedUtc,_tabCapturePreviousUtc;
    private string _guiError = "";
    private IntPtr _session;
    private ButtonControl _key;
    private Keyboard _probeKeyboard;
    private InputSettings.BackgroundBehavior _oldInputBackground;
    private bool _restoreInputBackground, _keyboardWasEnabled;
    private Vector3 _origin;
    private SRCharacterController _controller;
    private int _health, _energy;
    internal string Status = "idle";
    internal bool HoldsPlayerController=>!ReferenceEquals(_controller,null);
    private string Marker => Path.Combine(_app.DataDirectory, "selftest.request");
    private string ExplicitSave => Path.Combine(_app.DataDirectory,"selftest-save.txt");

    internal SmokeTests(ToolkitMod app)
    {
        _app=app;
        if(_app.SelftestFileRequest!=null) EnableBackground();
    }
    private void EnableBackground()
    {
        if(!_restoreBackground) { _oldRunInBackground=Application.runInBackground; _restoreBackground=true; }
        Application.runInBackground=true;
    }
    private void RestoreBackground()
    {
        if(!_restoreBackground) return;
        try { Application.runInBackground=_oldRunInBackground; }
        finally { _restoreBackground=false; }
    }
    private void RequireCopyAuditRequest()
    {
        if(!AuditSaveGuard.Required)throw new InvalidOperationException("Native selftests require an explicitly requested isolated-copy audit; arbitrary/main-world probes are refused.");
    }
    internal void Request() { RequireCopyAuditRequest();Cancel(); EnableBackground(); _requested=true; _movementOnly=false; Status="waiting for isolated-copy gameplay"; }
    internal void RequestMovement() { RequireCopyAuditRequest();Cancel(); EnableBackground(); _requested=true; _movementOnly=true; Status="waiting for isolated-copy movement test"; }

    internal void Tick()
    {
        try
        {
            if(_stage < 0)
            {
                string option=_app.SelftestFileRequest;
                if(option!=null)
                {
                    RequireCopyAuditRequest();
                    _requested=true;
                    EnableBackground();
                    _autoContinue=option == "continue";
                    if(!_app.Ready && option == "continue" && !_continueAttempted && Time.unscaledTime > _lastContinue)
                    {
                        if(AuditSaveGuard.Required)AuditSaveGuard.RequireReady();
                        _lastContinue=Time.unscaledTime+2f;
                        var loader=SystemContext.Instance?.SceneLoader;
                        if(loader == null || loader.IsSceneLoadInProgress) return;
                        if(!_startAttempted)
                        {
                            var engagement=UnityEngine.Object.FindObjectOfType<Il2CppMonomiPark.SlimeRancher.UI.MainMenu.PlatformEngagementPrompt>();
                            if(engagement!=null && engagement.SceneGroupToLoad!=null && engagement.ShouldShowInteractionPrompt())
                            {
                                _startAttempted=true;
                                Status="loading native engagement-screen destination for opt-in test";
                                engagement.LoadSceneGroup();
                                return;
                            }
                            var start=UnityEngine.Object.FindObjectOfType<StandaloneStartScreen>();
                            if(start != null && start.pastFirstFrame && !start.isLoading && start.defaultSceneGroup != null)
                            {
                                // Opt-in only: same native destination as the start screen, no global OS key injection.
                                _startAttempted=true;
                                start.isLoading=true;
                                Status="loading native start-screen destination for opt-in test";
                                try { loader.LoadSceneGroup(start.defaultSceneGroup); }
                                catch { if(!start.WasCollected) start.isLoading=false; throw; }
                                return;
                            }
                        }
                        var save=GameContext.Instance?.AutoSaveDirector;
                        if(save != null)
                        {
                            // A validated audit checkpoint is explicit authorization,
                            // independent of whether the menu offers Continue.
                            var summary=_app.Audit?.BootSummary()??ReadExplicitSummary(save);
                            if(summary==null&&save.HasContinue())summary=save.GetSaveToContinue();
                            if(summary != null && !summary.IsInvalid && !summary.GameOver)
                            {
                                _continueAttempted=true;
                                Status="loading validated explicit/Continue save for opt-in test";
                                save.BeginLoad(summary.SaveIdentifier);
                                if(File.Exists(ExplicitSave))File.Delete(ExplicitSave);
                            }
                            else Status="waiting for an explicitly selected valid save; no arbitrary newest-save fallback";
                        }
                    }
                }
                else if(!_requested) RestoreBackground();
                if(!_requested || !_app.Ready) return;
                if(_app.Audit?.SkipMainNativeTests==true)
                {Status="Основной мир: только создание копии / возврат, без игровых тестов";return;}
                if(NativeUiPriority.HasNativeFocus)
                {
                    if(_autoContinue&&Time.unscaledTime>=_nativeUiCloseAfter&&_nativeUiCloseRequests<4)
                    {
                        _app.Audit.GuardNativeProbeCopy();
                        var display=Il2CppMonomiPark.SlimeRancher.UI.Framework.Displays.UIDisplayManager._instance?._lastState?.TopFocus;
                        if(display!=null&&!display.IsClosing)
                        {
                            _nativeUiCloseRequests++;_nativeUiCloseAfter=Time.unscaledTime+2f;
                            _app.LoggerInstance.Msg("Owned-copy smoke closes actual blocking native display: "+display.Name);
                            display.Close();
                        }
                    }
                    Status="waiting for actual native display to close; no overlay input or forced unpause";
                    return;
                }
                if(Time.timeScale <= 0f)
                {
                    if(_autoContinue)
                    {
                        SystemContext.Instance.SceneLoader.UnpauseGame();
                        Status="resuming opt-in test world in background";
                    }
                    return;
                }
                var characterInput=_app.Context.Player.GetComponentInChildren<SRCharacterInput>(true);
                bool backgroundAutomation=_autoContinue && !Application.isFocused;
                if(characterInput == null || !characterInput._inputEnabled || (Cursor.lockState != CursorLockMode.Locked && !backgroundAutomation))
                    { Status="waiting for unfrozen player / gameplay input"; return; }
                _app.Audit?.GuardNativeProbeCopy();
                Begin();
                return;
            }
            if(!SafetyPolicy.SameSession(_session,_app.Session)) { Cancel(); return; }
            if(_stage < ToolkitUi.TabCount)
            {
                if(Time.unscaledTime < _deadline) return;
                // Cold native font/style initialization can consume the first
                // 0.7s and leave only one repaint. Wait for a real fresh PNG,
                // rather than silently certifying an old screenshot.
                bool capture=_rendered>=3&&FreshTabCapture();
                if(!capture&&Time.unscaledTime<_tabCaptureDeadline)return;
                Record(capture,"fresh native tab "+_stage+" PNG captured after at least three repaints");
                Record(_rendered > 0 && _guiError.Length == 0,"GUI tab "+_stage+" rendered="+_rendered+" error="+_guiError);
                _app.CloseMenu();
                Record(_app.Input.MissingAfterRestore == 0,"native actions restored after tab "+_stage+" missing="+_app.Input.MissingAfterRestore);
                _stage++;
                if(_stage < ToolkitUi.TabCount) OpenTab(); else StartMovement();
                return;
            }
            if(_stage == ToolkitUi.TabCount)
            {
                if(_preparingMovement)
                {
                    // Closing the final tab can finish before SR2's own pause
                    // animation/input ownership has settled. Do not certify a
                    // frozen player or inject W during that transition.
                    if(_autoContinue && Time.timeScale<=0f&&!NativeUiPriority.HasNativeFocus)
                        SystemContext.Instance.SceneLoader.UnpauseGame();
                    var preparingInput=_app.Context.Player.GetComponentInChildren<SRCharacterInput>(true);
                    if(Time.timeScale<=0f || preparingInput==null || !preparingInput._inputEnabled)
                    {
                        if(Time.unscaledTime>_deadline)
                            throw new InvalidOperationException("Gameplay did not resume after closing the final test tab.");
                        Status="waiting for native gameplay after final tab";
                        return;
                    }
                    _preparingMovement=false;
                    BeginWalking();
                    return;
                }
                if(_key != null) _sawKey |= _key.isPressed;
                if(Time.unscaledTime < _deadline) return;
                var input=_app.Context.Player.GetComponentInChildren<SRCharacterInput>(true);
                if(_app.MenuOpen) throw new InvalidOperationException("Menu reopened during movement test; user input interrupted the probe.");
                Vector3 end=_controller.Motor.TransientPosition;
                float moved=new Vector2(end.x-_origin.x,end.z-_origin.z).magnitude;
                Record(_sawKey,"queued W observed by native Keyboard");
                Record(moved > 0.03f,"native motor walking horizontal delta="+moved.ToString("F4")+" input="+(input!=null?input.MovementInput.ToString():"none")+" controller="+_controller.InputVector+" velocity="+_controller.Motor.BaseVelocity.ToString("F3")+" motorEnabled="+_controller.Motor.enabled+" grounded="+_controller.IsGrounded);
                _results.Add("INFO motor position="+end.ToString("F4")+" scene-root position="+_app.Context.Player.transform.position.ToString("F4"));
                NativeKeyboardProbe.ReleaseW();
                _keyInjected=false;
                _stage=ToolkitUi.TabCount+1;
                _deadline=Time.unscaledTime+1f;
                Status="waiting for native W release";
                return;
            }
            if(_stage == ToolkitUi.TabCount+1)
            {
                bool released=_key == null || !_key.isPressed;
                if(!released && Time.unscaledTime < _deadline) return;
                Record(released,"native W released after test");
                Finish();
            }
        }
        catch(Exception e)
        {
            Record(false,"runtime test exception: "+e);
            Finish();
        }
    }

    private Il2CppMonomiPark.SlimeRancher.Persist.Summary ReadExplicitSummary(Il2CppMonomiPark.SlimeRancher.AutoSaveDirector save)
    {
        if(!File.Exists(ExplicitSave))return null;
        string[] target=File.ReadAllText(ExplicitSave).Trim().Split('\t');
        if(target.Length!=2||Path.GetFileName(target[0])!=target[0]||Path.GetFileName(target[1])!=target[1])
            throw new InvalidOperationException("Explicit test save request must contain exact game name and save name.");
        var storage=save._storageProvider.TryCast<FileStorageProvider>();
        if(storage==null)throw new InvalidOperationException("Explicit test boot requires the verified local save provider.");
        if(target[1]=="latest")
        {
            // Latest checkpoint of ONE explicitly authorized GameName, never
            // the newest arbitrary save. Resolve after the previous native quit.
            Il2CppMonomiPark.SlimeRancher.Persist.Summary latest=null;
            foreach(var path in Directory.GetFiles(storage.savePath,target[0]+"_*.sav"))
            {
                string leaf=Path.GetFileNameWithoutExtension(path);var candidate=save.LoadSummary(leaf);
                if(candidate==null||candidate.IsInvalid||candidate.GameOver||candidate.Name!=target[0]||candidate.SaveName!=leaf)continue;
                if(latest==null||candidate.SaveNumber>latest.SaveNumber)latest=candidate;
            }
            if(latest==null)throw new InvalidOperationException("No validated checkpoint of the explicitly authorized GameName.");
            return latest;
        }
        if(!File.Exists(Path.Combine(storage.savePath,target[1]+".sav")))
            throw new InvalidOperationException("Explicitly authorized local save file is missing.");
        var summary=save.LoadSummary(target[1]);
        if(summary==null||summary.IsInvalid||summary.GameOver||summary.Name!=target[0]||summary.SaveName!=target[1])
            throw new InvalidOperationException("Explicit save summary failed identity/validity checks. No load was attempted.");
        return summary;
    }

    private void Begin()
    {
        _restorePlayer=false; _controller=null; _session=IntPtr.Zero;
        _app.CloseMenu();
        _app.Input.RecoverGameplay();
        _session=_app.Session;
        _results.Clear(); _failed=false; _requested=false;
        File.Delete(Marker);
        _app.ForgetSelftestFileRequest();
        var save=GameContext.Instance.AutoSaveDirector;
        _autoSaveWasDisabled=save._autosaveDisabled;
        save.SetAutosaveDisabled(true); _restoreSave=true;
        _controller=_app.Context.Player.GetComponentInChildren<SRCharacterController>(true);
        if(_controller == null || _controller.Motor == null) throw new InvalidOperationException("Native player motor unavailable.");
        _origin=_controller.Motor.TransientPosition;
        _health=_app.Context.PlayerState.GetCurrHealth(); _energy=_app.Context.PlayerState.GetCurrEnergy();
        _restorePlayer=true;
        _stage=_movementOnly?ToolkitUi.TabCount:0;
        if(_movementOnly) StartMovement(); else OpenTab();
    }

    private void OpenTab()
    {
        _rendered=0; _guiError="";
        _tabCaptureQueued=false;_tabCaptureRequestedUtc=DateTime.MinValue;
        string path=Path.Combine(_app.DataDirectory,"selftest-tab-"+_stage+".png");
        _tabCapturePreviousUtc=File.Exists(path)?File.GetLastWriteTimeUtc(path):DateTime.MinValue;
        _app.ShowTabForTest(_stage);
        _app.Input.Open();
        _deadline=Time.unscaledTime+0.7f;
        _tabCaptureDeadline=Time.unscaledTime+15f;
        Status="GUI tab "+_stage;
        var input=_app.Context.Player.GetComponentInChildren<SRCharacterInput>(true);
        if(input != null && input._moveVertical != null)
        {
            var actions=new Il2CppSystem.Collections.Generic.List<InputAction>();
            GameContext.Instance.InputDirector.InputActionController.GetInputActionFromEvent(input._moveVertical,actions);
            int active=0;
            for(int i=0;i<actions.Count;i++) if(actions[i].enabled) active++;
            Record(active==0,"movement actions suppressed while menu open: "+active);
        }
    }

    internal void Rendered()
    {
        if(_stage < 0 || _stage >= ToolkitUi.TabCount || Event.current == null || Event.current.type != EventType.Repaint) return;
        _rendered++;
        if(_rendered == 3)
        {
            try
            {
                _tabCaptureRequestedUtc=DateTime.UtcNow;
                ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"selftest-tab-"+_stage+".png"));
                _tabCaptureQueued=true;
            }
            catch(Exception e) { _results.Add("WARN screenshot: "+e.Message); }
        }
    }
    private bool FreshTabCapture()
    {
        if(!_tabCaptureQueued)return false;
        string path=Path.Combine(_app.DataDirectory,"selftest-tab-"+_stage+".png");
        if(!File.Exists(path))return false;
        var file=new FileInfo(path);
        if(file.Length<24||file.LastWriteTimeUtc<=_tabCapturePreviousUtc||
            file.LastWriteTimeUtc<_tabCaptureRequestedUtc)return false;
        try
        {
            using var stream=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
            var header=new byte[24];
            if(stream.Read(header,0,header.Length)!=header.Length)return false;
            if(header[0]!=137||header[1]!=80||header[2]!=78||header[3]!=71||
                header[4]!=13||header[5]!=10||header[6]!=26||header[7]!=10)return false;
            int width=(header[16]<<24)|(header[17]<<16)|(header[18]<<8)|header[19];
            int height=(header[20]<<24)|(header[21]<<16)|(header[22]<<8)|header[23];
            return width==Screen.width&&height==Screen.height;
        }
        catch(IOException){return false;} // One bounded wait; never replace old evidence to force success.
    }
    internal void GuiFailed(Exception e) { if(_stage >= 0 && _stage < ToolkitUi.TabCount) _guiError=e.ToString(); }

    private void StartMovement()
    {
        _stage=ToolkitUi.TabCount;
        _app.CloseMenu();
        _preparingMovement=true;
        _deadline=Time.unscaledTime+8f;
        Status="waiting for native gameplay after final tab";
    }

    private void BeginWalking()
    {
        _app.Input.RecoverGameplay();
        if(_controller == null || _controller.Motor == null) throw new InvalidOperationException("Player motor unavailable");
        if(Keyboard.current == null) throw new InvalidOperationException("Native Keyboard unavailable");
        _key=Keyboard.current.wKey;
        _hadKey=_key.isPressed;
        _sawKey=false;
        if(_app.Player.Flight || _app.World.FreezeMobs || _app.World.FreezeTime)
            throw new InvalidOperationException("Disable flight/freeze before the native walking test.");
        _origin=_controller.Motor.TransientPosition;
        // Background device resets can discard queued events even while native action maps are enabled.
        // This opt-in-only setting is restored; normal gameplay focus policy is never changed.
        _probeKeyboard=Keyboard.current;
        _keyboardWasEnabled=_probeKeyboard.enabled;
        if(!_keyboardWasEnabled && Application.isFocused)
            throw new InvalidOperationException("Keyboard is explicitly disabled while focused; will not override it.");
        _oldInputBackground=InputSystem.settings.backgroundBehavior;
        _restoreInputBackground=true;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        if(!_probeKeyboard.enabled) InputSystem.EnableDevice(_probeKeyboard);
        _results.Add("INFO probe keyboard enabled="+_probeKeyboard.enabled+" focus="+Application.isFocused+" priorBackground="+_oldInputBackground);
        _deadline=Time.unscaledTime+0.20f;
        Status="queued W native movement probe (0.20s)";
        // This native input test deliberately does not patch PollInput, UpdateVelocity or transform movement.
        if(_hadKey) throw new InvalidOperationException("W is already held in native Keyboard; test cancelled.");
        NativeKeyboardProbe.PressW();
        _keyInjected=true;
    }

    private void Restore()
    {
        try{RestoreState();}
        finally{_controller=null;_session=IntPtr.Zero;_probeKeyboard=null;}
    }

    private void RestoreState()
    {
        _preparingMovement=false;
        try
        {
            if(_key != null)
            {
                try { if(_keyInjected) NativeKeyboardProbe.ReleaseW(); }
                finally { _keyInjected=false; _key=null; }
            }
        }
        finally
        {
            if(_restoreInputBackground)
            {
                try
                {
                    InputSystem.settings.backgroundBehavior=_oldInputBackground;
                    if(!_keyboardWasEnabled && !Application.isFocused && _probeKeyboard != null && !_probeKeyboard.WasCollected && _probeKeyboard.enabled)
                        InputSystem.DisableDevice(_probeKeyboard);
                }
                catch(Exception e) { Record(false,"input background policy restore: "+e.Message); }
                finally { _restoreInputBackground=false; _probeKeyboard=null; }
            }
            _app.CloseMenu();
            try
            {
                if(_restorePlayer && SafetyPolicy.SameSession(_session,_app.Session) && _controller != null && !_controller.WasCollected && _controller.Motor != null)
                {
                    _controller.Motor.SetPosition(_origin,true);
                    _controller.ResetVelocity(false);
                    _app.Context.PlayerState.SetHealth(_health);
                    _app.Context.PlayerState.SetEnergy(_energy);
                }
            }
            finally
            {
                _restorePlayer=false;
                if(_restoreSave)
                {
                    try { GameContext.Instance.AutoSaveDirector.SetAutosaveDisabled(
                        AuditSavePolicy.RestoreAutosaveDisabled(AuditSaveGuard.Required,_autoSaveWasDisabled)); }
                    finally { _restoreSave=false; }
                }
            }
        }
    }

    private void Record(bool ok,string text)
    {
        _failed |= !ok;
        string line=(ok?"PASS ":"FAIL ")+text;
        _results.Add(line);
        _app.LoggerInstance.Msg("SelfTest: "+line);
    }
    private void Finish()
    {
        try { Restore(); }
        catch(Exception e) { Record(false,"restore: "+e); }
        try { RestoreBackground(); }
        catch(Exception e) { Record(false,"background restore: "+e); }
        _stage=-1; _requested=false;
        Status=_failed?"FAILED (see selftest.txt)":"PASS (GUI/native actions/queued W)";
        File.WriteAllText(Path.Combine(_app.DataDirectory,"selftest.txt"),
            "CreatorToolkit "+ToolkitMod.Version+" runtime self-test\n"+Status+"\nScope: GUI rendering, native action restoration, queued full-device W/native motor walking and release (temporary opt-in background policy restored); not an OS/physical keyboard test; no permanent unlock/spawn/currency tests.\n"+string.Join("\n",_results)+"\n");
        if(!_failed) _app.Notify("Самотест: GUI и нативное управление прошли проверку.");
    }
    internal void CancelRequest()
    {
        File.Delete(Marker);
        _app.ForgetSelftestFileRequest();
        Cancel();
    }
    internal void Cancel()
    {
        if(_stage >= 0 || _restoreSave || _key != null)
        {
            try { Restore(); } catch(Exception e) { _app.Fail("selftest restore",e); }
        }
        _stage=-1; _requested=false; Status="idle";
        if(_app.SelftestFileRequest==null) RestoreBackground();
    }
}
