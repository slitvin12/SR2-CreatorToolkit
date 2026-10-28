using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppMonomiPark.SlimeRancher.UI.Pedia;
using Il2CppMonomiPark.SlimeRancher.UI.Framework.Displays;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Il2CppMonomiPark.SlimeRancher.Pedia;

namespace CreatorToolkit;

internal sealed partial class FeatureSuite
{
    private GameObject _nativePediaReference;
    private UIDisplayRequestHandle<Il2CppSystem.Object> _nativePediaRequest;
    private UIRuntimeDisplay _nativePediaDisplay;
    private PediaDirector _nativeReferenceDirector;
    private Il2CppSystem.Object _nativeReferenceToken;
    private IntPtr _nativeReferenceSession;
    private string _nativeReferenceName="";
    private bool _nativeReferenceClosing,_nativeReferencePopups,_nativeReferencePreload;
    private NativePointerLease _nativeReferencePointer;
    private PediaEntry _nativeClickEntry,_nativePinkEntry;
    private Vector2 _nativePediaClick;
    private int _nativeReferenceHandoffs;
    private readonly List<object> _nativePointerRows=new();
    private PediaCategoryButton _nativeCategoryTarget;
    private float _nativePointerTraceAfter;
    private static object NativeActionSnapshot(InputActionReference reference)
    {
        var action=reference?.action;
        return action==null?null:new{Name=action.name,Enabled=action.enabled,
            Map=action.actionMap?.name,MapEnabled=action.actionMap?.enabled,
            ControlCount=action.controls.Count,ActiveControl=action.activeControl?.path};
    }
    private bool TraceNativePediaPointer(string phase)
    {
        GuardCopy();
        bool targetHit=false;
        try
        {
            var events=EventSystem.current;var mouse=Mouse.current;
            var module=events?.currentInputModule;
            var inputModule=module?.TryCast<InputSystemUIInputModule>();
            var hits=new Il2CppSystem.Collections.Generic.List<RaycastResult>();
            if(events!=null)
                events.RaycastAll(new PointerEventData(events){position=_nativePediaClick},hits);
            var hitRows=new List<object>();
            for(int i=0;i<Math.Min(8,hits.Count);i++)
            {
                var hit=hits[i];var category=hit.gameObject?.GetComponentInParent<PediaCategoryButton>();
                bool match=category!=null&&_nativeCategoryTarget!=null&&
                    category.Pointer==_nativeCategoryTarget.Pointer;
                if(i==0)targetHit=match;
                hitRows.Add(new{Object=NativeReferenceObjectName(hit.gameObject),
                    Category=NativeReferenceObjectName(category),Target=match,
                    Module=NativeReferenceObjectName(hit.module),hit.depth,hit.distance});
            }
            var root=_nativePediaReference?.GetComponentInChildren<PediaRoot>(true);
            var last=inputModule!=null&&mouse!=null?inputModule.GetLastRaycastResult(mouse.deviceId):default;
            var canvases=_nativePediaReference==null?Array.Empty<Canvas>():
                _nativePediaReference.GetComponentsInChildren<Canvas>(true);
            var canvasRows=canvases.Take(16).Select(c=>new{Object=NativeReferenceObjectName(c),
                Active=c.gameObject.activeInHierarchy,c.enabled,Mode=c.renderMode.ToString(),
                Camera=NativeReferenceObjectName(c.worldCamera),c.targetDisplay,c.scaleFactor,
                PixelRect=c.pixelRect.ToString(),c.sortingOrder}).ToArray();
            var raycasterRows=new List<object>();
            if(_nativePediaReference!=null&&events!=null)
                foreach(var caster in _nativePediaReference.GetComponentsInChildren<GraphicRaycaster>(true).Take(16))
                {
                    var direct=new Il2CppSystem.Collections.Generic.List<RaycastResult>();
                    caster.Raycast(new PointerEventData(events){position=_nativePediaClick},direct);
                    var names=new List<string>();
                    for(int i=0;i<Math.Min(8,direct.Count);i++)
                        names.Add(NativeReferenceObjectName(direct[i].gameObject));
                    raycasterRows.Add(new{Object=NativeReferenceObjectName(caster),
                        Active=caster.isActiveAndEnabled,Camera=NativeReferenceObjectName(caster.eventCamera),
                        caster.ignoreReversedGraphics,Blocking=caster.blockingObjects.ToString(),
                        DirectHits=direct.Count,Names=names});
                }
            var groupRows=_nativeCategoryTarget==null?null:
                _nativeCategoryTarget.GetComponentsInParent<CanvasGroup>(true).Take(16)
                    .Select(g=>new{Object=NativeReferenceObjectName(g),g.alpha,g.interactable,
                        g.blocksRaycasts,g.ignoreParentGroups,Active=g.isActiveAndEnabled}).ToArray();
            var graphicRows=_nativeCategoryTarget==null?null:
                _nativeCategoryTarget.GetComponentsInChildren<Graphic>(true).Take(24).Select(g=>{
                    var canvas=g.canvas;var camera=canvas==null||canvas.renderMode==RenderMode.ScreenSpaceOverlay?
                        null:canvas.worldCamera;
                    return new{Object=NativeReferenceObjectName(g),Type=g.GetIl2CppType().FullName,
                        Active=g.isActiveAndEnabled,g.raycastTarget,g.depth,
                        Culled=g.canvasRenderer?.cull,
                        RectangleContains=RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform,_nativePediaClick,camera),
                        GraphicAllows=g.Raycast(_nativePediaClick,camera)};
                }).ToArray();
            _nativePointerRows.Add(new{Phase=phase,Frame=Time.frameCount,
                Target=NativeReferenceObjectName(_nativeCategoryTarget),
                TargetX=_nativePediaClick.x,TargetY=_nativePediaClick.y,TargetRaycast=targetHit,
                MouseX=mouse?.position.ReadValue().x,MouseY=mouse?.position.ReadValue().y,
                MouseDown=mouse?.leftButton.isPressed,MouseEnabled=mouse?.enabled,
                MouseDevice=mouse?.deviceId,AppFocused=Application.isFocused,
                EventSystemFocused=events?.isFocused,Navigation=events?.sendNavigationEvents,
                InputModule=module?.GetIl2CppType().FullName,ModuleEnabled=module?.isActiveAndEnabled,
                ShouldIgnoreFocus=inputModule?.shouldIgnoreFocus,
                ExplicitIgnoreFocus=inputModule?.explictlyIgnoreFocus,
                ActionsHooked=inputModule?.m_ActionsHooked,
                PointAction=NativeActionSnapshot(inputModule?.point),
                ClickAction=NativeActionSnapshot(inputModule?.leftClick),
                LastNativeRaycast=NativeReferenceObjectName(last.gameObject),
                CurrentScreen=root?._currentScreen?.TryCast<Il2CppSystem.Object>()?.GetIl2CppType().FullName,
                NextScreen=root?._nextScreen?.TryCast<Il2CppSystem.Object>()?.GetIl2CppType().FullName,
                Transitioning=root?._transitioningScreen,
                RegistrarFocus=root?._inputRegistrar?.HasFocus,
                NativeOwnership=NativeUiPriority.Diagnostics(),
                NativeMouseModeReturns=_app.Input.NativeMouseModeReturns,
                Canvases=canvasRows,Raycasters=raycasterRows,Groups=groupRows,Graphics=graphicRows,
                ModOpen=_app.MenuOpen,CursorLock=Cursor.lockState.ToString(),TimeScale=Time.timeScale,
                Hits=hitRows,RecordedUtc=DateTime.UtcNow.ToString("o")});
        }
        catch(Exception e){_nativePointerRows.Add(new{Phase=phase,DiagnosticError=e.ToString()});}
        File.WriteAllText(Path.Combine(_app.DataDirectory,"native-pedia-pointer-trace.json"),
            JsonSerializer.Serialize(new{Copy=_recovery.TestName,ReadOnlyRaycast=true,
                DirectCallbackInvoked=false,Rows=_nativePointerRows},
                new JsonSerializerOptions{WriteIndented=true}));
        return targetHit;
    }
    private bool ObserveNativeSlimeArticle()
    {
        if(Time.unscaledTime>=_nativePointerTraceAfter)
        {
            _nativePointerTraceAfter=Time.unscaledTime+1f;
            TraceNativePediaPointer("await actual article after category gesture");
        }
        return NativeSlimeArticleReady();
    }
    private bool NativeCategoryRaycastReady()
    {
        if(Time.unscaledTime<_nativePointerTraceAfter)return false;
        _nativePointerTraceAfter=Time.unscaledTime+.5f;
        return TraceNativePediaPointer("await native focus/fade/raycast readiness");
    }
    private static string NativeReferenceObjectName(UnityEngine.Object value)=>
        value==null||value.WasCollected?"":value.name;
    private void RecordNativeReference(string phase)
    {
        File.WriteAllText(Path.Combine(_app.DataDirectory,"native-reference-state.json"),JsonSerializer.Serialize(new{
            Version=ToolkitMod.Version,Copy=_recovery?.TestName,Phase=phase,ConfiguredDisplayName=_nativeReferenceName,
            RequestValid=_nativePediaRequest?.IsValidHandle,RuntimeName=_nativePediaDisplay?.Name,
            LoadingComplete=_nativePediaDisplay?.IsDoneLoading,Shown=_nativePediaDisplay?.IsShown,
            Closing=_nativePediaDisplay?.IsClosing,
            RootName=NativeReferenceObjectName(_nativePediaDisplay?.RootGameObject),
            DisplayName=NativeReferenceObjectName(_nativePediaDisplay?.DisplayGameObject),
            ActualPediaRoot=NativeReferenceObjectName(_nativePediaReference),
            NativeComponents=_nativePediaReference==null?Array.Empty<string>():
                _nativePediaReference.GetComponentsInChildren<MonoBehaviour>(true)
                    .Where(c=>c!=null).Select(c=>c.GetIl2CppType().FullName).Distinct().Take(64).ToArray(),
            TimeScale=Time.timeScale,NativeParametersLive=Controller?._parameters!=null,
            RecordedUtc=DateTime.UtcNow.ToString("o")},new JsonSerializerOptions{WriteIndented=true}));
    }
    private bool NativePediaReady()
    {
        GuardCopy();
        if(_nativePediaRequest==null||!_nativePediaRequest.IsValidHandle||
            !_nativePediaRequest.TryGetRequestedDisplay(out var display)||display==null)return false;
        _nativePediaDisplay=display;
        if(display.Name!=_nativeReferenceName||!display.IsDoneLoading||!display.IsShown||display.IsClosing)return false;
        var root=display.DisplayGameObject??display.RootGameObject;
        if(root==null||!root.activeInHierarchy)return false;
        // This game's current display is authoritative. A legacy PediaRoot
        // type existing in an interop assembly does not establish that the
        // current UI prefab contains it.
        if(root.GetComponentsInChildren<Il2CppTMPro.TMP_Text>(true).Length==0)return false;
        _nativePediaReference=root;
        return true;
    }
    private bool NativeSlimeArticleReady()
    {
        if(!NativePediaReady())return false;
        // This installed game's live component census identifies the new
        // PediaSlimeAndResourceDetails, not the unused legacy PediaEntryDetails.
        return _nativePediaReference.GetComponentsInChildren<PediaSlimeAndResourceDetails>(true)
            .Any(d=>d!=null&&d.gameObject.activeInHierarchy&&d._currentEntry!=null);
    }
    private static Vector2 NativeUiPoint(Component component)
    {
        var rt=component.GetComponent<RectTransform>();var canvas=component.GetComponentInParent<Canvas>();
        Require(rt!=null&&canvas!=null,"Native click has no actual canvas hit rectangle.");
        var point=RectTransformUtility.WorldToScreenPoint(
            canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,rt.TransformPoint(rt.rect.center));
        Require(point.x>0&&point.x<Screen.width&&point.y>0&&point.y<Screen.height,
            "Native click would be outside the real screen.");
        return point;
    }
    private void CloseOwnedNativeReference()
    {
        if(_nativeReferenceClosing||_nativePediaRequest==null)return;
        if(!_app.Ready||_app.Session!=_nativeReferenceSession)return;
        // Close the exact request even if its root was never found. Never
        // dereference a missing PediaRoot or guess a native display name.
        _nativeReferenceClosing=true;
        GameContext.Instance.UIDirector.CloseDisplay<Il2CppSystem.Object>(_nativePediaRequest);
    }
    private void RetireUiReferences()
    {
        RetireNativeFontExperiment();
        RetireNativeWindowProbe();
        _nativeReferencePointer?.Dispose();_nativeReferencePointer=null;_nativeClickEntry=_nativePinkEntry=null;
        NativeKeyboardProbe.ReleaseAuxiliary();
        try{CloseOwnedNativeReference();}
        catch(Exception e){_app.LoggerInstance.Warning("Owned native reference close: "+e.Message);}
        try
        {
            if(_nativeReferencePopups&&_nativeReferenceToken!=null)
                UIDirector.ShowPopupsAndTutorials(_nativeReferenceToken);
        }
        catch(Exception e){_app.LoggerInstance.Warning("Owned native reference popup lease: "+e.Message);}
        try
        {
            if(_nativeReferencePreload&&_nativeReferenceDirector!=null&&!_nativeReferenceDirector.WasCollected)
                _nativeReferenceDirector.ReleasePreloadPedia(_nativeReferenceToken);
        }
        catch(Exception e){_app.LoggerInstance.Warning("Owned native reference preload lease: "+e.Message);}
        _nativePediaReference=null;_nativePediaRequest=null;_nativePediaDisplay=null;
        _nativeReferenceDirector=null;_nativeReferenceToken=null;_nativeReferenceSession=IntPtr.Zero;
        _nativeReferenceName="";_nativeReferenceClosing=_nativeReferencePopups=_nativeReferencePreload=false;
        _nativeCategoryTarget=null;
    }
    private static bool CriticalNativeStep(string name)=>
        name.StartsWith("NativeReferences /",StringComparison.Ordinal)||
        name.StartsWith("NativeInput /",StringComparison.Ordinal)||
        name=="UI / native LMB sliders and scrollbar"||
        name.StartsWith("LO Player /",StringComparison.Ordinal)||
        name.StartsWith("Optimization /",StringComparison.Ordinal);
    private void BuildNativeUiReferenceSteps()
    {
        Add("NativeReferences / request exact configured Slimepedia display",()=>{
            GuardCopy();_app.CloseMenu();RetireUiReferences();
            _nativePointerRows.Clear();_nativePointerTraceAfter=0;
            Require(Time.timeScale>0&&Controller._parameters!=null,
                "Native gameplay was already paused or had no parameters before the reference.");
            _nativeReferenceDirector=_app.Context.PediaDirector;
            // Explicit copy-only article fixture, not a click substitute:
            // two real unlocked articles are required for a navigation test.
            foreach(string reference in new[]{"SlimeDefinition.Pink","SlimeDefinition.Cotton"})
            {
                var actor=_app.Actors.Catalog.FirstOrDefault(e=>e.Reference==reference);
                var fixtureEntry=actor==null?null:_nativeReferenceDirector.GetEntry(actor.Type);
                Require(fixtureEntry!=null,"Registered native article fixture missing: "+reference);
                if(!_nativeReferenceDirector.IsUnlocked(fixtureEntry))
                    _nativeReferenceDirector.Unlock(fixtureEntry,false);
                Require(_nativeReferenceDirector.IsUnlocked(fixtureEntry),"Native copy article did not unlock: "+reference);
                if(reference=="SlimeDefinition.Pink")_nativePinkEntry=fixtureEntry;
            }
            var configured=_nativeReferenceDirector._pediaDisplay;
            Require(configured!=null&&configured.IsValid(),"Native Pedia display handle is not configured.");
            _nativeReferenceName=configured.Name;
            Require(!string.IsNullOrWhiteSpace(_nativeReferenceName),"Native Pedia display name is empty.");
            _nativeReferenceSession=_app.Session;_nativeReferenceToken=new Il2CppSystem.Object();
            UIDirector.HidePopupsAndTutorials(_nativeReferenceToken);_nativeReferencePopups=true;
            _nativeReferenceDirector.RequestPreloadPedia(_nativeReferenceToken);_nativeReferencePreload=true;
            _app.Input.Open();
            Require(_app.MenuOpen,"Cannot establish the mod-owned input state before native Pedia handoff.");
            _nativeReferenceHandoffs=_app.Input.NativeHandoffs;
            _nativePediaRequest=GameContext.Instance.UIDirector.OpenDisplay(_nativeReferenceName);
            Require(_nativePediaRequest!=null&&_nativePediaRequest.IsValidHandle,"Native Pedia request was not accepted.");
            RecordNativeReference("request accepted");
        },.12f);
        Add("NativeReferences / configured native home display loaded",()=>{
            GuardCopy();Require(NativePediaReady(),"The exact native Pedia request has no visible text-bearing display.");
            RecordNativeReference("configured real Pedia home; no assumed direct-entry navigation");
        },.65f,NativePediaReady,15f);
        Add("NativeReferences / original Slimepedia home and actual font sources",()=>{
            GuardCopy();Require(NativePediaReady(),"The owned native Pedia display is not visible.");
            Require(!_app.MenuOpen&&_app.Input.NativeHandoffs>_nativeReferenceHandoffs&&
                _app.UiForTests.TextListenerRootsForTest==0&&EventSystem.current!=null&&EventSystem.current.sendNavigationEvents,
                "Native Pedia did not receive exclusive input/navigation ownership.");
            RecordNativeReference("visible configured native Pedia");
            _app.UiForTests.InspectNativePediaTypography(_nativePediaReference);
            Require(_app.UiForTests.NativePediaTypographyCheckedForTest,"No actual native Pedia TMP font could be read.");
            ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"native-original-pedia-home.png"));
        },.35f,NativePediaReady,15f);
        BuildNativeFontExperimentSteps();
        Add("NativeReferences / prepare actual Slimepedia category button",()=>{
            GuardCopy();
            var button=_nativePediaReference.GetComponentsInChildren<PediaCategoryButton>(true)
                .FirstOrDefault(b=>b!=null&&b.gameObject.activeInHierarchy&&
                    b._category?.TryCast<PediaRuntimeCategory>()?.Contains(_nativePinkEntry)==true);
            Require(button!=null,"No actual visible category contains the registered Pink article.");
            _nativeCategoryTarget=button;
            _nativePediaClick=NativeUiPoint(button._icon!=null?(Component)button._icon:button);
            TraceNativePediaPointer("category target before pointer lease");
            _nativeReferencePointer=new NativePointerLease();_nativeReferencePointer.Set(_nativePediaClick,false);
        },.2f);
        Add("NativeReferences / actual native category raycast readiness",()=>{
            GuardCopy();Require(TraceNativePediaPointer("native category raycast ready"),
                "Actual icon centre raycast does not reach its native category; trace retained.");
        },.2f,NativeCategoryRaycastReady,15f);
        Add("NativeReferences / actual category native mouse press",()=>{
            GuardCopy();TraceNativePediaPointer("category hover observed before press");
            _nativeReferencePointer.Set(_nativePediaClick,true);
        },.12f);
        Add("NativeReferences / actual category native mouse release",()=>{
            GuardCopy();TraceNativePediaPointer("category native press observed before release");
            _nativeReferencePointer.Set(_nativePediaClick,false);
        },.2f);
        Add("NativeReferences / category released pointer diagnostics",()=>{
            GuardCopy();TraceNativePediaPointer("category native release observed");
            ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"native-category-after-release.png"));
        },.2f);
        Add("NativeReferences / actual pointer opened slime article category",()=>{
            GuardCopy();Require(NativeSlimeArticleReady()&&!_app.MenuOpen&&
                Cursor.lockState!=CursorLockMode.Locked&&EventSystem.current.sendNavigationEvents,
                "Actual category mouse gesture did not open a native article screen.");
            RecordNativeReference("article category opened by actual native pointer");
            File.WriteAllText(Path.Combine(_app.DataDirectory,"native-pedia-category-click-proof.json"),
                JsonSerializer.Serialize(new{Copy=_recovery.TestName,ActualNativePointer=true,
                    DirectCallbackInvoked=false,ModOpen=_app.MenuOpen,
                    NativeMouseModeReturns=_app.Input.NativeMouseModeReturns,
                    NativeHandoffs=_app.Input.NativeHandoffs,RecordedUtc=DateTime.UtcNow.ToString("o")},
                    new JsonSerializerOptions{WriteIndented=true}));
            ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"native-original-slimepedia.png"));
        },.25f,ObserveNativeSlimeArticle,15f);
        Add("NativeReferences / prepare actual alternate article icon",()=>{
            GuardCopy();
            var details=_nativePediaReference.GetComponentsInChildren<PediaSlimeAndResourceDetails>(true)
                .FirstOrDefault(d=>d!=null&&d.gameObject.activeInHierarchy&&d._currentEntry!=null);
            var buttons=_nativePediaReference.GetComponentsInChildren<PediaEntryIconButton>(true);
            var button=buttons
                .FirstOrDefault(b=>b!=null&&b.gameObject.activeInHierarchy&&b._entry!=null&&
                    _nativeReferenceDirector.IsUnlocked(b._entry)&&
                    (details==null||b._entry.Pointer!=details._currentEntry.Pointer));
            File.WriteAllText(Path.Combine(_app.DataDirectory,"native-pedia-layout-proof.json"),JsonSerializer.Serialize(new{
                Copy=_recovery.TestName,ActualDetailsType=nameof(PediaSlimeAndResourceDetails),
                CurrentEntry=NativeReferenceObjectName(details?._currentEntry),
                Buttons=buttons.Where(b=>b!=null).Select(b=>new{
                    Object=NativeReferenceObjectName(b),Active=b.gameObject.activeInHierarchy,
                    Entry=NativeReferenceObjectName(b._entry),
                    Unlocked=b._entry!=null&&_nativeReferenceDirector.IsUnlocked(b._entry)}).ToArray(),
                RecordedUtc=DateTime.UtcNow.ToString("o")},new JsonSerializerOptions{WriteIndented=true}));
            Require(details!=null&&button!=null,"No visible alternate unlocked native article button/details; no callback was faked.");
            _nativeClickEntry=button._entry;
            _nativePediaClick=NativeUiPoint(button);
            _nativeReferencePointer.Set(_nativePediaClick,false);
        },.2f,NativeSlimeArticleReady,15f);
        Add("NativeReferences / actual article native mouse press",()=>{
            GuardCopy();_nativeReferencePointer.Set(_nativePediaClick,true);
        },.12f);
        Add("NativeReferences / actual article native mouse release",()=>{
            GuardCopy();_nativeReferencePointer.Set(_nativePediaClick,false);
        },.2f);
        Add("NativeReferences / actual pointer changed native article",()=>{
            GuardCopy();
            Require(_nativePediaReference.GetComponentsInChildren<PediaSlimeAndResourceDetails>(true).Any(d=>
                d!=null&&d.gameObject.activeInHierarchy&&d._currentEntry?.Pointer==_nativeClickEntry.Pointer),
                "Native mouse gesture did not navigate to the alternate article.");
            Require(!_app.MenuOpen&&Cursor.lockState!=CursorLockMode.Locked&&EventSystem.current.sendNavigationEvents,
                "Native article navigation lost its cursor or input ownership.");
            ScreenCapture.CaptureScreenshot(Path.Combine(_app.DataDirectory,"native-slimepedia-real-click.png"));
            File.WriteAllText(Path.Combine(_app.DataDirectory,"native-pedia-click-proof.json"),JsonSerializer.Serialize(new{
                Copy=_recovery.TestName,NativeHandoffs=_app.Input.NativeHandoffs,Entry=_nativeClickEntry.name,
                NativeMouseModeReturns=_app.Input.NativeMouseModeReturns,
                ActualNativePointer=true,DirectCallbackInvoked=false,ModOpen=_app.MenuOpen,
                Navigation=EventSystem.current.sendNavigationEvents,TextListenerRoots=_app.UiForTests.TextListenerRootsForTest,
                ScreenWidth=Screen.width,ScreenHeight=Screen.height,RecordedUtc=DateTime.UtcNow.ToString("o")},
                new JsonSerializerOptions{WriteIndented=true}));
        },.25f);
        Add("NativeReferences / actual native keyboard navigation starts",()=>{
            GuardCopy();NativeKeyboardProbe.PressAuxiliary(Keyboard.current.rightArrowKey);
        },.2f);
        Add("NativeReferences / idle mouse does not displace native keyboard",()=>{
            GuardCopy();NativeKeyboardProbe.ReleaseAuxiliary();
            Require(UIDisplayManager._instance.FocusModeDevice==UIDisplayFocusModeDevice.KEYBOARD&&
                !_app.MenuOpen&&NativePediaReady(),
                "Native keyboard could not reclaim navigation while the mouse was idle.");
            TraceNativePediaPointer("native keyboard reclaimed device mode");
            NativeKeyboardProbe.PressAuxiliary(Keyboard.current.insertKey);
        },.2f);
        Add("NativeReferences / Insert cannot steal the native article window",()=>{
            GuardCopy();Require(!_app.MenuOpen&&NativePediaReady(),"Insert stole input from the native article.");
            NativeKeyboardProbe.ReleaseAuxiliary();
            _nativeReferencePointer?.Dispose();_nativeReferencePointer=null;
        },.15f);
        Add("NativeReferences / close exact native Slimepedia request",()=>{
            GuardCopy();CloseOwnedNativeReference();RecordNativeReference("native close requested");
        },.12f);
        Add("NativeReferences / native pause and parameters restored after close",()=>{
            GuardCopy();RecordNativeReference("gameplay resumed");RetireUiReferences();_app.Input.RecoverGameplay();
        },.3f,()=>Time.timeScale>0&&Controller?._parameters!=null&&
            (_nativePediaReference==null||!_nativePediaReference.activeInHierarchy),15f);
        Add("NativeReferences / optional fabrication reference availability recorded",()=>{
            GuardCopy();
            // Missing optional reference is explicitly unavailable, not a
            // purported screenshot or an excuse to force Time.timeScale.
            File.WriteAllText(Path.Combine(_app.DataDirectory,"native-fabricator-reference.txt"),
                "No owned fabrication-display request was established. No fabrication reference captured; no guessed name, prefab, interaction or forced pause recovery was used.\n");
        });
    }
}
