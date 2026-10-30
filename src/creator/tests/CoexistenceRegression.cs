using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using CreatorToolkit;
using C = CreatorToolkit.CoexistencePolicy;

internal static class CoexistenceRegression
{
    // Hook coexistence with other MelonLoader mods (HarmonyX): pure policy plus static wiring and
    // package metadata. No Unity, no Harmony runtime, no live multi-mod session.
    internal static void Run(Action<bool,string> check,string root)
    {
        void Check(bool value,string name)=>check(value,"coexistence / "+name);
        static string Compact(string s)=>Regex.Replace(s,"\\s+","");
        string dir=Path.Combine(root,"src","creator");

        // Fault window: isolate exactly once at the threshold, never for sparse faults.
        var w=new C.FaultWindow();
        bool a1=w.Register(1_000),a2=w.Register(2_000),a3=w.Register(3_000),a4=w.Register(4_000);
        Check(C.Threshold==3&&C.WindowMs==60_000,"threshold is 3 faults per 60 s");
        Check(!a1&&!a2&&a3&&!a4,"third fault inside the window isolates, exactly once");
        var sparse=new C.FaultWindow();
        bool s1=sparse.Register(0),s2=sparse.Register(30_000),s3=sparse.Register(61_000),s4=sparse.Register(100_000);
        Check(!s1&&!s2&&!s3&&!s4,"faults spread wider than the window never isolate");
        var again=new C.FaultWindow();
        again.Register(0);again.Register(1);again.Register(2);
        bool r1=again.Register(70_000),r2=again.Register(70_001),r3=again.Register(70_002);
        Check(!r1&&!r2&&r3,"a new window can isolate again after the old one expired");
        var back=new C.FaultWindow();
        back.Register(50_000);back.Register(50_001);
        Check(!back.Register(10)&&back.Count==1,"clock going backwards restarts the window");

        // Groups and user-facing feature names.
        string[] builder={"RealBegin","RealEnd","SpendBegin","SpendEnd","CountWrite","CountRead","ScopeBegin","ScopeEnd"};
        Check(builder.All(b=>C.Group(b)==C.InfiniteBuilderGroup&&C.Feature(b)=="Бесконечный режим строителя"),"infinite builder hooks isolate as one group");
        Check(C.Group("DamageGuard")=="DamageGuard"&&C.Group(null)=="","other hooks isolate one by one");
        Check(C.Feature("SomethingElse")=="Функция Creator Toolkit","unknown hook gets a generic feature name");
        string notice=C.IsolationNotice("Бессмертие");
        Check(notice.Contains("«Бессмертие»")&&notice.Contains("чужие моды не затронуты"),"isolation notice names the feature and reassures about other mods");
        string summary=C.Summary(35,4,new Dictionary<string,int>{["Starlight"]=3,["SR2MP"]=3,["x"]=1,[" "]=9},0,1,0,0);
        Check(summary=="hooked=35 shared=4 [SR2MP×3, Starlight×3, x×1] foreign-transpilers=0 foreign-cancels=1 isolated=0 install-failures=0","census summary line: "+summary);
        Check(C.Summary(1,0,null,0,0,0,2)=="hooked=1 shared=0 foreign-transpilers=0 foreign-cancels=0 isolated=0 install-failures=2","census summary without foreign owners");
        Check(C.HookPolicyKey=="SR2ModManager.HookPolicy"&&C.HookPolicyValue=="cooperative","hook policy metadata key/value");

        // Static wiring (not behavioural proof).
        string hooksRaw=File.ReadAllText(Path.Combine(dir,"NativeHooks.cs")),hooks=Compact(hooksRaw);
        string buildRaw=File.ReadAllText(Path.Combine(dir,"InfiniteBuildHooks.cs")),build=Compact(buildRaw);
        string coexist=Compact(File.ReadAllText(Path.Combine(dir,"HookCoexistence.cs")));
        string mod=Compact(File.ReadAllText(Path.Combine(dir,"ToolkitMod.cs")));

        var guards=Regex.Matches(hooksRaw,@"private static (?:bool|void) (\w+)\(").Select(m=>m.Groups[1].Value).ToList();
        Check(guards.Count>=11,"native hook bodies found ("+guards.Count+")");
        var referenced=Regex.Matches(hooksRaw,@"nameof\((\w+)\)").Select(m=>m.Groups[1].Value).Where(n=>n!="NativeHooks").ToHashSet(StringComparer.Ordinal);
        Check(referenced.All(guards.Contains),"every nameof(...) in NativeHooks is a hook body defined there");
        for(int i=0;i<guards.Count;i++)
        {
            string g=guards[i];
            int start=hooksRaw.IndexOf("private static bool "+g+"(",StringComparison.Ordinal);
            if(start<0)start=hooksRaw.IndexOf("private static void "+g+"(",StringComparison.Ordinal);
            int end=i+1<guards.Count?hooksRaw.IndexOf(" "+guards[i+1]+"(",start+1,StringComparison.Ordinal):hooksRaw.Length;
            string body=Compact(hooksRaw.Substring(start,Math.Max(0,end-start)));
            Check(body.Contains("try{")&&body.Contains("catch(Exceptione){")&&body.Contains("HookCoexistence.Fault(nameof("+g+"),e)"),"hook "+g+" never throws into the shared call chain");
            Check(C.Feature(g)!="Функция Creator Toolkit","hook "+g+" has a user-facing feature name ("+C.Feature(g)+")");
            if(body.StartsWith("privatestaticbool",StringComparison.Ordinal))
                Check(body.Contains("returnHookCoexistence.Fault(nameof("+g+"),e);"),"cancelling hook "+g+" falls back to vanilla on a fault");
        }
        foreach(string g in new[]{"CountRead","CountWrite","SpendBegin"})
            Check(build.Contains("HookCoexistence.Fault(nameof("+g+"),e)"),"builder hook "+g+" reports faults to coexistence");
        Check(build.Contains("HookCoexistence.Fault(nameof(SpendBegin),e);returnVanilla(\"error\",held);"),"builder spend falls back to vanilla on a fault");
        Check(build.Contains("internalstaticvoidWithdraw(stringreason,ToolkitModapp)")&&coexist.Contains("InfiniteBuildHooks.Withdraw("),"isolating the builder group switches the mode off");

        var methods=Regex.Matches(hooksRaw+buildRaw,@"new HarmonyMethod\([^()]*\)").Select(m=>m.Value).ToList();
        Check(methods.Count>=4&&methods.All(m=>m.Contains("Priority.Last")),"every Creator Toolkit hook runs last (Priority.Last, "+methods.Count+" sites)");
        int patches=Regex.Matches(hooks,@"harmony\.Patch\(").Count;
        Check(patches==3&&Regex.Matches(hooks,@"HookCoexistence\.Track\(").Count==patches&&Regex.Matches(hooks,@"HookCoexistence\.InstallFailed\(").Count==patches,"every native patch site is tracked and withdraws itself when install fails");
        Check(build.Contains("HookCoexistence.Track(original,pre?.method);HookCoexistence.Track(original,post?.method);")&&build.Contains("HookCoexistence.InstallFailed(original,pre?.method,e);"),"builder patch sites are tracked");

        var forbidden=new[]{"transpiler:","finalizer:","[HarmonyTranspiler","[HarmonyFinalizer","UnpatchAll(","UnpatchSelf(","HarmonyPatchType.All","PatchAll("};
        var offenders=new List<string>();
        foreach(string file in Directory.GetFiles(dir,"*.cs"))
        {
            string text=File.ReadAllText(file);
            foreach(string f in forbidden)if(text.Contains(f,StringComparison.Ordinal))offenders.Add(Path.GetFileName(file)+": "+f);
        }
        Check(offenders.Count==0,"no transpilers, finalizers or mass (un)patching anywhere"+(offenders.Count==0?"":": "+string.Join(", ",offenders)));
        var unpatch=Regex.Matches(coexist,@"\.Unpatch\(([^)]*)\)").Select(m=>m.Groups[1].Value).ToList();
        Check(unpatch.Count==2&&unpatch.All(u=>u=="e.Original,e.Patch"||u=="original,patch"),"isolation unpatches only our own (method, patch) pairs");
        Check(coexist.Contains("info.Owners.Where(o=>o!=_harmony.Id)")&&coexist.Contains("p.owner!=_harmony.Id"),"census counts only foreign owners");
        Check(coexist.Contains("catch{/*Afaultreportmustneverbecomeasecondfaultinsidethehook.*/}returntrue;"),"fault reporting itself cannot throw and always picks vanilla");

        int configure=mod.IndexOf("HookCoexistence.Configure(HarmonyInstance,this);",StringComparison.Ordinal);
        int saveInstall=mod.IndexOf("AuditSaveGuard.Install(HarmonyInstance)",StringComparison.Ordinal),nativeInstall=mod.IndexOf("NativeHooks.Install(HarmonyInstance)",StringComparison.Ordinal);
        Check(configure>=0&&configure<saveInstall&&configure<nativeInstall,"coexistence is configured before any hook is installed");
        Check(mod.Contains("(\"hookcoexistence\",HookCoexistence.Tick),(\"modinterop\",ModInterop.Tick),(\"starlightbridge\",StarlightBridge.Tick)}"),"isolation, census and the Starlight bridge run every frame, also before Ready");
        Check(mod.Contains("Coexistence:{HookCoexistence.Summary}\\n"),"diagnostics snapshot carries the census line");
        Check(mod.Contains("[assembly:AssemblyMetadata(\"SR2ModManager.HookPolicy\",\"cooperative\")]"),"mod manager learns the hooks are cooperative");

        string dll=Path.Combine(root,"_work","creator-stage","CreatorToolkit.dll");
        using var pe=new PEReader(File.OpenRead(dll));
        var md=pe.GetMetadataReader();
        var types=md.TypeDefinitions.Select(h=>md.GetString(md.GetTypeDefinition(h).Name)).ToHashSet(StringComparer.Ordinal);
        Check(types.Contains("HookCoexistence")&&types.Contains("CoexistencePolicy"),"coexistence modules packaged");
        var meta=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var h in md.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attr=md.GetCustomAttribute(h);
            if(attr.Constructor.Kind!=HandleKind.MemberReference)continue;
            var parent=md.GetMemberReference((MemberReferenceHandle)attr.Constructor).Parent;
            if(parent.Kind!=HandleKind.TypeReference||md.GetString(md.GetTypeReference((TypeReferenceHandle)parent).Name)!="AssemblyMetadataAttribute")continue;
            var blob=md.GetBlobReader(attr.Value);
            if(blob.ReadUInt16()!=1)continue;
            string key=blob.ReadSerializedString()??"",value=blob.ReadSerializedString()??"";
            meta[key]=value;
        }
        Check(meta.TryGetValue(C.HookPolicyKey,out var policy)&&policy==C.HookPolicyValue,"release DLL carries SR2ModManager.HookPolicy = cooperative");
        Check(meta.TryGetValue("SR2ModManager.CompatibleWith",out var with)&&with=="SR2MP;Starlight;InfiniteHealth","release DLL still carries CompatibleWith = SR2MP;Starlight;InfiniteHealth");
    }
}
