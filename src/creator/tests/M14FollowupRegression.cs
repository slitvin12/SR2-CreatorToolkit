using System;
using System.IO;
using System.Linq;
internal static class M14FollowupRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void Check(bool value,string name)=>check(value,"m14 / followup static wiring / "+name);
        string Read(string name)=>string.Concat(File.ReadAllText(Path.Combine(root,"src","creator",name)).Where(c=>!char.IsWhiteSpace(c)));
        string ui=Read("ToolkitUi.cs"),host=Read("ToolkitMod.cs"),capsules=Read("CapsuleTools.cs");
        Check(ui.Contains("privateboolCanHover(Rectrect)=>InputEnabled&&PointerWithinGroups()&&rect.Contains(Pointer())"),"hover respects all ancestor clips not just row hitbox");
        Check(ui.Contains("boolhovered=CanHover(rect)")&&ui.Contains("boolhovered=CanHover(rowRect)"),"buttons and atlas hover use fixed clipped hitboxes");
        Check(ui.Contains("InputEnabled&&PointerWithinGroups()&&e!=null&&e.type==EventType.ScrollWheel"),"wheel cannot consume outside an ancestor clip");
        Check(ui.Contains("!ReferenceEquals(_capsuleLayout.View,view)||_capsuleLayout.Width!=viewport.width"),"capsule text layout rebuilds on changed view or width");
        Check(ui.Contains("layout.Extent+=layout.DescriptionHeight;_capsuleLayout=layout;")&&ui.Contains("varheights=_capsuleLayout.Rows;"),"cached content extent includes empty rewards description");
        Check(ui.Contains("_capsuleLayout=null;_family="),"session reset releases old managed capsule layout");
        Check(host.Contains("UiPage:")&&host.Contains("UiNavigation:")&&host.Contains("LoggerInstance.Msg(\"UInavigation:"),"navigation exposes applied page and completion or rejection");
        Check(host.Contains("!Ready?\"not-ready\":NativeUiPriority.HasNativeFocus?\"native-focus\":Map.Busy?\"transit\":Pending!=null?\"confirmation-active\":null"),"UI navigation preserves native priority transit and confirmation gates");
        Check(ui.Contains("_app.Map.Visible.Clear();_app.Map.Visible.Add(\"Капсулы\")")&&host.Contains("NavigateMenu(command,_ui.ShowCapsules)"),"explicit capsule navigation selects only capsule UI layer");
        Check(ui.Contains("if(_tab==3&&_app.Ready)_hour=_app.World.Hour()"),"weather UI navigation initializes slider from current native hour");
        int a=capsules.IndexOf("internalCachedCapsuleSnapshotSnapshotCachedEvidence()",StringComparison.Ordinal);
        int b=a<0?-1:capsules.IndexOf("internalvoidLoad(",a,StringComparison.Ordinal);
        string snapshot=a>=0&&b>a?capsules.Substring(a,b-a):"";
        Check(snapshot.Length>0&&snapshot.Contains("_evidence.TryGetValue(id,outvarevidence)")&&!snapshot.Contains("Get(")&&!snapshot.Contains("Build(")&&!snapshot.Contains("UnityEngine")&&!snapshot.Contains("ValidateLoaded"),"cached capsule evidence accessor never refreshes native state or builds UI views");
        Check(snapshot.Contains("CachedPodLimit")&&snapshot.Contains("CachedTotalRewardLimit")&&snapshot.Contains("GenerationAfter!=result.GenerationBefore"),"cached capsule snapshot has finite budgets and generation consistency");
    }
}
