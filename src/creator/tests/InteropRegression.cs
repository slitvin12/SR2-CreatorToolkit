using System.Text.RegularExpressions;
using CreatorToolkit;
using P = CreatorToolkit.ModInteropPolicy;

internal static class InteropRegression
{
    // Third-party mod support (Nexus Mods catalogue + Ranching Together «Official Mod Support»):
    // pure policy plus static wiring. No Unity, MelonLoader or live multi-mod session.
    internal static void Run(Action<bool,string> check,string root)
    {
        void Check(bool value,string name)=>check(value,"interop / "+name);
        static string Compact(string s)=>Regex.Replace(s,"\\s+","");
        string dir=Path.Combine(root,"src","creator");

        // Recognition by MelonInfo name or assembly name, case/punctuation-insensitive.
        Check(P.Find("Vacuum Modifications")?.Nexus==45&&P.Find("VacuumModifications")?.Nexus==45&&P.Find("vacuum-modifications")?.Nexus==45,"Vacuum Modifications by melon name, assembly and slug");
        Check(P.Find("Placement Improvements")?.Nexus==179&&P.Find("BuildImprovements")?.Nexus==179&&P.Find("nl.lunarsnail.buildimprovements")?.Nexus==179,"Placement Improvements by name, DLL name and Starlight id");
        Check(P.Find("Better Drones")?.Nexus==177&&P.Find("BetterDrones")?.Nexus==177&&P.Find("de.pyeight.betterdrones")?.Nexus==177,"Better Drones by name, DLL name and Starlight id");
        Check(P.Find("MoreVaccablesMod")?.Nexus==42&&P.Find("Infinite health")?.Nexus==57&&P.Find("InfiniteHealth")?.Nexus==57,"More Vaccables and Infinite Health by their real melon/assembly names");
        Check(P.Find("SR2MP")?.Name=="Ranching Together"&&P.Find("Starlight")?.Nexus==60,"Ranching Together and Starlight");
        Check(P.Find("")==null&&P.Find(null)==null&&P.Find("CreatorToolkit")==null&&P.Find("MelonLoader")==null&&P.Find("Il2CppMonomiPark.SlimeRancher")==null,"unknown, empty and framework names are not mods");
        var rt=P.All.Where(k=>k.RanchingTogether).Select(k=>k.Name).OrderBy(n=>n,StringComparer.Ordinal).ToArray();
        Check(rt.SequenceEqual(new[]{"Better Drones","Placement Improvements","Vacuum Modifications"}),"exactly the Ranching Together officially supported mods are flagged: "+string.Join(", ",rt));
        Check(P.All.All(k=>k.Nexus>0&&k.Url=="https://www.nexusmods.com/slimerancher2/mods/"+k.Nexus),"every known mod links its Nexus page");
        var keys=P.All.SelectMany(k=>new[]{P.Norm(k.Name)}.Concat(k.Aliases.Select(P.Norm)).Distinct()).ToList();
        Check(keys.All(x=>x.Length>0)&&keys.GroupBy(x=>x).All(g=>g.Count()==1),"names and aliases never collide between mods");
        var found=P.Detect(new[]{"Vacuum Modifications","VacuumModifications","BetterDrones","MelonLoader",null,"","CreatorToolkit"});
        Check(found.Select(k=>k.Name).SequenceEqual(new[]{"Better Drones","Vacuum Modifications"}),"detection de-duplicates melon+assembly names and ignores the rest");
        Check(P.Detect(null).Count==0,"detection tolerates no input");

        // Vacpack fill target: vanilla exactly 100; with a slot-limit mod the slot's own limit.
        Check(P.FillTarget(250,false)==100&&P.FillTarget(0,false)==100&&P.FillTarget(40,false)==100,"without a slot-limit mod the fill stays exactly 100");
        Check(P.FillTarget(250,true)==250&&P.FillTarget(40,true)==40&&P.FillTarget(1,true)==1,"with a slot-limit mod the fill follows the slot limit (raised or lowered)");
        Check(P.FillTarget(0,true)==100&&P.FillTarget(-5,true)==100,"unreadable or zero limit falls back to 100");
        Check(P.FillTarget(int.MaxValue,true)==P.MaxCustomFill&&P.MaxCustomFill==9999,"'unlimited' slots are capped at 9999");
        Check(P.CustomLimits(true,0)&&P.CustomLimits(false,1)&&!P.CustomLimits(false,0),"limits are custom with a known limit mod or any foreign MaxCount patch");
        Check(SafetyPolicy.RefillAt(100)==25&&SafetyPolicy.RefillAt(250)==62&&SafetyPolicy.RefillAt(40)==10&&SafetyPolicy.RefillAt(3)==1&&SafetyPolicy.RefillAt(1)==1,"refill point is a quarter of the target, vanilla keeps 25");
        Check(SafetyPolicy.NeedsRefill(25,100)&&!SafetyPolicy.NeedsRefill(26,100)&&SafetyPolicy.NeedsRefill(62,250)&&!SafetyPolicy.NeedsRefill(63,250)&&!SafetyPolicy.NeedsRefill(0,250),"refill with a custom target is inclusive and never revives empty slots");
        Check(Enumerable.Range(-2,130).All(c=>SafetyPolicy.NeedsRefill(c)==SafetyPolicy.NeedsRefill(c,SafetyPolicy.AmmoFill)),"vanilla refill behaviour unchanged");
        Check(P.FillText(false)=="100 шт."&&P.FillText(true)=="до лимита слота"&&P.RefillText(false)=="25 → 100 шт.","UI texts keep the vanilla wording");

        // Diagnostics line.
        string s=P.Summary(new[]{P.Find("VacuumModifications"),P.Find("Better Drones"),P.Find("BetterDrones"),null},true,new[]{"com.bread.vacmods"," ","COM.bread.vacmods"});
        Check(s=="mods=[Better Drones(RT), Vacuum Modifications(vac-limits,RT)] vac-limits=slot [com.bread.vacmods]","summary line: "+s);
        Check(P.Summary(null,false,null)=="mods=[none] vac-limits=vanilla","summary without mods");
        Check(P.Tags(P.Find("InfiniteHealth"))=="(same-as-god-mode)"&&P.Tags(P.Find("BuildImprovements"))=="(native-placement,RT)"&&P.Tags(null)=="","duplicate-cheat and placement tags");

        // Static wiring (not behavioural proof).
        string interop=File.ReadAllText(Path.Combine(dir,"ModInterop.cs")),ic=Compact(interop);
        Check(ic.Contains("MelonBase.RegisteredMelons")&&ic.Contains("AppDomain.CurrentDomain.GetAssemblies()"),"detection by loaded melons and assemblies");
        Check(ic.Contains("AccessTools.PropertyGetter(typeof(AmmoSlot),nameof(AmmoSlot.MaxCount))")&&ic.Contains("HarmonyLib.Harmony.GetPatchInfo(getter)")&&ic.Contains("o!=own"),"foreign MaxCount patches are detected, our own ignored");
        Check(!Regex.IsMatch(interop,@"\.(Patch|Unpatch|UnpatchAll|PatchAll|Invoke)\(")&&!interop.Contains("Activator"),"interop is read-only: never patches or calls into other mods");
        string proj=File.ReadAllText(Path.Combine(dir,"CreatorToolkit.csproj"));
        Check(!Regex.IsMatch(proj,"VacuumModifications|BuildImprovements|BetterDrones|InfiniteHealth|MoreVaccables",RegexOptions.IgnoreCase),"no compile-time reference to any other mod");
        string comfort=Compact(File.ReadAllText(Path.Combine(dir,"ConvenienceTools.cs")));
        Check(comfort.Contains("inttarget=ModInterop.FillTarget(slot);")&&comfort.Contains("SafetyPolicy.NeedsRefill(slot.Count,target)")&&comfort.Contains("slot.Count=target;")&&!comfort.Contains("slot.Count=SafetyPolicy.AmmoFill"),"auto refill follows the slot target");
        string actors=Compact(File.ReadAllText(Path.Combine(dir,"ActorTools.cs")));
        Check(actors.Contains("inttarget=ModInterop.FillTarget(slot);")&&actors.Contains("ammo.MaybeAddResource(entry.Type,index,target,true)")&&actors.Contains("slot.Count=target;")&&actors.Contains("ammo.GetSlotCount(index)!=target"),"vacpack fill follows the slot target");
        string mod=Compact(File.ReadAllText(Path.Combine(dir,"ToolkitMod.cs")));
        Check(mod.Contains("ModInterop.Configure(this);")&&mod.Contains("(\"modinterop\",ModInterop.Tick)")&&mod.Contains("Interop:{ModInterop.Summary}\\n"),"interop is configured, ticked and in the diagnostics snapshot");
        Check(mod.Contains("[assembly:AssemblyMetadata(\"SR2ModManager.CompatibleWith\",\"SR2MP;Starlight;InfiniteHealth\")]"),"Infinite Health is declared checked: both cancel PlayerModel.LoseHealth for the same purpose");
    }
}
