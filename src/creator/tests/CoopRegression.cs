using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using CreatorToolkit;
using P = CreatorToolkit.MultiplayerPolicy;

internal static class CoopRegression
{
    // Real production co-op policy (Ranching Together / SR2MP) plus static wiring and package
    // metadata. No live multiplayer session, no Unity, no save access.
    internal static void Run(Action<bool,string> check,string root)
    {
        void Check(bool value,string name)=>check(value,"coop / "+name);
        static string Compact(string s)=>Regex.Replace(s,"\\s+","");
        string dir=Path.Combine(root,"src","creator");

        foreach(var kind in Enum.GetValues<CoopKind>())Check(P.Deny(kind,CoopRole.Offline,false)==null,"offline allows "+kind);
        Check(P.Footer(CoopRole.Offline,true)==""&&P.PlayerCheats(CoopRole.Offline,false),"offline: no footer, no restrictions");
        foreach(var role in Enum.GetValues<CoopRole>())foreach(bool cheats in new[]{false,true})
            Check(P.Deny(CoopKind.Free,role,cheats)==null,"free actions allowed "+role+" cheats="+cheats);
        foreach(var role in new[]{CoopRole.Host,CoopRole.Client})foreach(bool cheats in new[]{false,true})
            Check(P.Deny(CoopKind.SoloOnly,role,cheats)!=null,"time/mob freeze denied in session "+role+" cheats="+cheats);
        Check(P.Deny(CoopKind.Cheat,CoopRole.Host,false)?.Contains("Allow Cheats")==true,"host without server cheats is told to enable Allow Cheats");
        Check(P.Deny(CoopKind.Cheat,CoopRole.Client,false)!=null,"guest cheats follow the host rule");
        Check(P.Deny(CoopKind.Cheat,CoopRole.Host,true)==null&&P.Deny(CoopKind.Cheat,CoopRole.Client,true)==null,"server cheats allow host and guest cheats");
        Check(!P.PlayerCheats(CoopRole.Host,false)&&!P.PlayerCheats(CoopRole.Client,false)&&P.PlayerCheats(CoopRole.Client,true),"god mode/flight/speed follow server cheats");
        Check(P.Deny(CoopKind.HostWorld,CoopRole.Client,true)!=null,"guest never changes host clock or weather");
        Check(P.Deny(CoopKind.HostWorld,CoopRole.Host,true)==null&&P.Deny(CoopKind.HostWorld,CoopRole.Host,false)!=null,"host clock/weather needs server cheats");
        Check(P.Classify("freeze time")==CoopKind.SoloOnly&&P.Classify("freeze mobs")==CoopKind.SoloOnly&&P.Classify("Ускорить симуляцию в 5 раз?")==CoopKind.SoloOnly,"freeze and simulation rate are solo-only");
        Check(P.Classify("weather")==CoopKind.HostWorld&&P.Classify("set hour")==CoopKind.HostWorld&&P.Classify("time +6")==CoopKind.HostWorld,"weather and clock belong to the host");
        Check(P.Classify("spawn")==CoopKind.Cheat&&P.Classify("currency max")==CoopKind.Cheat&&P.Classify("teleport waypoint")==CoopKind.Cheat&&
            P.Classify("refill vitals")==CoopKind.Cheat&&P.Classify("Открыть весь атлас?")==CoopKind.Cheat&&P.Classify("Открыть все двери")==CoopKind.Cheat,"spawn, money, vitals, teleport and unlocks are cheats");
        Check(P.Classify("Разместить «Тест»?")==CoopKind.Cheat&&P.Known("Разместить «Тест»?"),"free gadget placement is a cheat");
        Check(P.Classify("cleanup owned")==CoopKind.Free&&P.Classify("SOS rescue")==CoopKind.Free&&P.Classify("safe transit return")==CoopKind.Free&&P.Classify("cancel safe transit")==CoopKind.Free,"cleanup and rescue stay available");
        Check(P.Classify("graphics preset")==CoopKind.Free&&P.Classify("open atlas")==CoopKind.Free&&P.Classify("info HUD")==CoopKind.Free,"graphics, atlas and HUD stay available");
        Check(!P.Known("unknown label")&&!P.Known("")&&!P.Known(null),"unclassified labels are reported");
        Check(P.Footer(CoopRole.Host,false)=="Кооп · хост · без читов"&&P.Footer(CoopRole.Client,true)=="Кооп · гость · читы вкл","footer wording");

        // Every literal action label in the sources must be classified (new actions can not slip through).
        var labels=new SortedSet<string>(StringComparer.Ordinal);
        foreach(string file in Directory.GetFiles(dir,"*.cs"))
            foreach(Match m in Regex.Matches(File.ReadAllText(file),"\\b(?:Queue|Confirm)\\(\"([^\"]*)\""))labels.Add(m.Groups[1].Value);
        string unlock=File.ReadAllText(Path.Combine(dir,"UnlockUi.cs"));
        int a=unlock.IndexOf("UnlockTitles={",StringComparison.Ordinal),b=a<0?-1:unlock.IndexOf("};",a,StringComparison.Ordinal);
        Check(a>=0&&b>a,"unlock confirmation titles found");
        if(a>=0&&b>a)foreach(Match m in Regex.Matches(unlock.Substring(a,b-a),"\"([^\"]*)\""))labels.Add(m.Groups[1].Value);
        Check(labels.Count>=100,"source scan found action labels ("+labels.Count+")");
        var unknown=labels.Where(l=>!P.Known(l)).ToArray();
        Check(unknown.Length==0,"every Queue/Confirm label has a co-op class"+(unknown.Length==0?"":": "+string.Join(", ",unknown)));
        foreach(string l in new[]{"freeze time","weather","spawn","flight","Открыть карту и ноды"})Check(labels.Contains(l),"policy label still used in sources: "+l);

        // Static wiring (not behavioural proof).
        string mod=Compact(File.ReadAllText(Path.Combine(dir,"ToolkitMod.cs")));
        string actors=Compact(File.ReadAllText(Path.Combine(dir,"ActorTools.cs")));
        string bridgeRaw=File.ReadAllText(Path.Combine(dir,"MultiplayerBridge.cs")),bridge=Compact(bridgeRaw);
        string ui=File.ReadAllText(Path.Combine(dir,"ToolkitUi.cs"));
        int queue=mod.IndexOf("internalvoidQueue(stringlabel,Actionaction)",StringComparison.Ordinal);
        int gate=queue<0?-1:mod.IndexOf("stringdenied=Coop?.Deny(label);if(denied!=null){Notify(denied);return;}",queue,StringComparison.Ordinal);
        int enqueue=queue<0?-1:mod.IndexOf("_jobs.Enqueue(",queue,StringComparison.Ordinal);
        Check(gate>queue&&gate<enqueue,"Queue refuses denied actions before enqueueing");
        Check(mod.Contains("stringlate=Coop?.Deny(label);if(late!=null)thrownewInvalidOperationException(late);action();"),"queued action rechecks the rule right before it runs");
        Check(mod.Contains("stringdenied=Coop?.Deny(title);if(denied!=null){Notify(denied);return;}Pending=newConfirmation"),"confirmation is refused before it is shown");
        Check(mod.Contains("BindSession();Coop.Poll();"),"session role is polled every frame before the Ready gate");
        Check(mod.Contains("_readyTicks=new(string,Action)[]{(\"cooprules\",Coop.Enforce),"),"co-op rules are enforced first among ready ticks");
        Check(mod.Contains("Coop=newMultiplayerBridge(this);Player=newPlayerTools(this);"),"bridge exists before any tool");
        Check(mod.Contains("[assembly:AssemblyMetadata(\"SR2ModManager.CompatibleWith\",\"SR2MP;Starlight;InfiniteHealth\")]"),"mod manager learns the shared hooks were checked with SR2MP, Starlight and Infinite Health");
        Check(actors.Contains("_app.Coop?.AnnounceSpawn(go);")&&actors.Contains("_app.Coop?.AnnounceSpawn(actor.Object);"),"ordinary and radiant spawns are announced to SR2MP");
        int gordo=actors.IndexOf("if(_app.Coop?.InSession==true)thrownewInvalidOperationException(\"Кооп:Гордо",StringComparison.Ordinal);
        Check(gordo>=0&&gordo<actors.IndexOf("BeginGordoSpawn(entry);return;",StringComparison.Ordinal),"unsynchronised gordo snare is refused in co-op");
        Check(bridge.Contains("StartsWith(\"SR2MP,\",StringComparison.Ordinal)")&&bridge.Contains("\"CheatsEnabled\"")&&bridge.Contains("\"RegisterSpawnOverNetwork\"")&&
            bridge.Contains("Bind(main,\"Server\",\"IsRunning\")")&&bridge.Contains("Bind(main,\"Client\",\"IsConnected\")"),"bridge binds the SR2MP session API by reflection");
        Check(!bridgeRaw.Contains("using SR2MP")&&!bridgeRaw.Contains("typeof(SR2MP")&&!bridgeRaw.Contains("using Starlight"),"bridge has no compile-time SR2MP/Starlight dependency");
        Check(bridge.Contains("_nextPoll=now+0.5f;")&&bridge.Contains("_nextProbe=now+10f;"),"session poll is throttled (0.5 s, probe every 10 s)");
        Check(bridge.Contains("if(Role==CoopRole.Offline)returnnull;")&&bridge.Contains("if(Role==CoopRole.Offline)return;"),"offline bridge is inert");
        Check(ui.Contains("coop?_app.Coop.Footer:\"Insert  ·  Esc\""),"menu footer shows the co-op rule while in a session");

        string dll=Path.Combine(root,"_work","creator-stage","CreatorToolkit.dll");
        using var pe=new PEReader(File.OpenRead(dll));
        var md=pe.GetMetadataReader();
        var refs=md.AssemblyReferences.Select(h=>md.GetString(md.GetAssemblyReference(h).Name)).ToArray();
        Check(!refs.Any(n=>n.Equals("SR2MP",StringComparison.OrdinalIgnoreCase)||n.Equals("Starlight",StringComparison.OrdinalIgnoreCase)),"release DLL does not reference SR2MP or Starlight");
        var types=md.TypeDefinitions.Select(h=>md.GetString(md.GetTypeDefinition(h).Name)).ToHashSet(StringComparer.Ordinal);
        Check(types.Contains("MultiplayerBridge")&&types.Contains("MultiplayerPolicy"),"co-op modules packaged");
        bool metadata=false;
        foreach(var h in md.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attr=md.GetCustomAttribute(h);
            if(attr.Constructor.Kind!=HandleKind.MemberReference)continue;
            var parent=md.GetMemberReference((MemberReferenceHandle)attr.Constructor).Parent;
            if(parent.Kind!=HandleKind.TypeReference||md.GetString(md.GetTypeReference((TypeReferenceHandle)parent).Name)!="AssemblyMetadataAttribute")continue;
            var blob=md.GetBlobReader(attr.Value);
            if(blob.ReadUInt16()!=1)continue;
            if(blob.ReadSerializedString()=="SR2ModManager.CompatibleWith"&&blob.ReadSerializedString()=="SR2MP;Starlight;InfiniteHealth")metadata=true;
        }
        Check(metadata,"release DLL carries SR2ModManager.CompatibleWith = SR2MP;Starlight;InfiniteHealth");
    }
}
