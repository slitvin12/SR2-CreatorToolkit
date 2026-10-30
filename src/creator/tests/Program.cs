using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using CreatorToolkit;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition)
    {
        if (Environment.GetEnvironmentVariable("CT_TEST_ALL") == "1") { Console.WriteLine("FAIL " + name); return; }
        throw new InvalidOperationException("FAIL " + name);
    }
    passed++;
    Console.WriteLine("PASS " + name);
}
void Throws(Action action, string name)
{
    bool threw = false;
    try { action(); } catch (ArgumentOutOfRangeException) { threw = true; }
    Check(threw, name);
}
Check(SafetyPolicy.SpawnCount(-1) == 1, "spawn minimum");
Check(SafetyPolicy.SpawnCount(26) == 25, "spawn maximum");
foreach (int n in new[] { 1, 5, 10, 25 }) Check(SafetyPolicy.SpawnCount(n) == n, "spawn preset " + n);
Check(SafetyPolicy.Speed(0) == 1, "speed minimum");
Check(SafetyPolicy.Speed(10) == 10&&SafetyPolicy.Speed(50)==10, "LO explicitly requested speed maximum ten");
Check(PlayerMovementPolicy.JumpHeight(0)==1&&PlayerMovementPolicy.JumpHeight(20)==5&&
    PlayerMovementPolicy.JumpHeight(float.NaN)==1,"jump height has finite one-to-five limits");
Check(PlayerMovementPolicy.JumpVelocityFactor(4)==2,"fourfold ballistic jump height doubles native launch speed");
Check(PlayerMovementPolicy.PreserveSpend(1,1)&&PlayerMovementPolicy.PreserveSpend(100,25)&&
    !PlayerMovementPolicy.PreserveSpend(0,1)&&!PlayerMovementPolicy.PreserveSpend(1,2)&&
    !PlayerMovementPolicy.PreserveSpend(10,0)&&!PlayerMovementPolicy.PreserveSpend(10,-1),
    "infinite vacpack never creates a missing item or bypasses insufficient/invalid spending");
Check(SafetyPolicy.Speed(float.NaN) == 1, "speed rejects NaN");
Check(SafetyPolicy.Speed(float.PositiveInfinity) == 1, "speed rejects infinity");
var textBuffer=new TextEditBuffer();
textBuffer.Focus("Мятная тропа",48);textBuffer.Type('!');
Check(textBuffer.Value=="Мятная тропа!"&&textBuffer.Caret==13,"native Cyrillic commits edit the focused Unicode buffer immediately");
textBuffer.Focus("",48);int typedFixture=0;
foreach(char c in "Мятная тропа")if(textBuffer.Type(c))typedFixture++;
Check(textBuffer.Value=="Мятная тропа"&&typedFixture==12,
    "twelve separate Cyrillic and space commits preserve the actual fixture exactly");
textBuffer.Focus("Мята",48);textBuffer.Type(' ');textBuffer.Type(' ');textBuffer.Type('Ё');
Check(textBuffer.Value=="Мята  Ё","spaces remain immediate editable characters, not trimmed mid-entry");
textBuffer.Focus("A🙂Ё",48);textBuffer.Backspace();textBuffer.Backspace();
Check(textBuffer.Value=="A"&&textBuffer.Caret==1,"backspace removes a surrogate pair atomically, never a half emoji");
textBuffer.Focus("е\u0301",48);textBuffer.Backspace();
Check(textBuffer.Value=="","backspace removes a combining grapheme atomically");
textBuffer.Focus("A🙂Ё",48);textBuffer.SetCaret(2);textBuffer.Move(1);textBuffer.Delete();
Check(textBuffer.Value=="A🙂"&&textBuffer.Caret==3,"caret never lands inside a surrogate pair and delete follows its actual boundary");
textBuffer.Focus("",48);bool highAccepted=textBuffer.Type('\ud83d');bool lowAccepted=textBuffer.Type('\ude42');
Check(!highAccepted&&lowAccepted&&textBuffer.Value=="🙂","native UTF-16 callbacks assemble a supplementary code point exactly once");
textBuffer.Focus("Мята",48);textBuffer.Preview("漢字");
Check(textBuffer.Value=="Мята"&&textBuffer.PreviewValue=="Мята漢字","IME preview does not prematurely mutate committed marker text");
textBuffer.Preview("");textBuffer.Type('漢');textBuffer.Type('字');
Check(textBuffer.Value=="Мята漢字"&&textBuffer.Composition=="","IME committed characters are not duplicated with the preview");
textBuffer.SelectAll();textBuffer.Type('Ё');
Check(textBuffer.Value=="Ё"&&textBuffer.Caret==1,"select-all replacement preserves native Cyrillic");
Check(TextEditBuffer.Sanitize("A\nЁ\tB",80)=="AЁB"&&TextEditBuffer.Sanitize("🙂",1)=="",
    "single-line sanitation excludes control characters and never truncates a surrogate pair");
textBuffer.Focus("abc",3);textBuffer.Type('x');
Check(textBuffer.Value=="abc","the focused input enforces its actual per-field character budget");
Check(TextEditBuffer.ImePayloadValid(152,64,20,64)&&TextEditBuffer.ImePayloadValid(24,0,20,64)&&
    !TextEditBuffer.ImePayloadValid(25,1,20,64)&&!TextEditBuffer.ImePayloadValid(152,65,20,64)&&
    !TextEditBuffer.ImePayloadValid(152,-1,20,64)&&!TextEditBuffer.ImePayloadValid(152,64,16,64),
    "native IME event length/header/capacity are checked before any UTF-16 buffer read");
Check(TextEditBuffer.ScalarValid('М')&&TextEditBuffer.ScalarValid('Ё')&&
    TextEditBuffer.ScalarValid(0x1f642)&&TextEditBuffer.ScalarValid(0x10ffff),
    "native UTF-32 events accept full Cyrillic and supplementary scalars without an unmanaged char delegate");
Check(!TextEditBuffer.ScalarValid(-1)&&!TextEditBuffer.ScalarValid(0x110000)&&
    !TextEditBuffer.ScalarValid(0xd800)&&!TextEditBuffer.ScalarValid(0xdfff),
    "native UTF-32 events reject non-scalars before conversion");
var gate=new TransitSafety();
Check(!gate.Observe(true,0,1,2,3),"transit never releases on its first ground hit");
foreach(float t in new[]{.1f,.2f,.3f,.4f,.5f,.6f,.7f})Check(!gate.Observe(true,t,1,2,3),"transit holds through warmup "+t);
Check(gate.Observe(true,.8f,1,2,3)&&gate.Samples>=4,"transit releases only after repeated stable collisions for 0.75 seconds");
Check(!gate.Observe(false,.9f,1,2,3)&&gate.Samples==0,"missing collision immediately invalidates previous readiness");
Check(!gate.Observe(true,1,1,2,3),"stream reload cannot reuse a previous stable window");
Check(!gate.Observe(true,float.NaN,1,2,3)&&gate.Samples==0,"transit rejects nonfinite clock");
Check(!gate.Observe(true,2,float.PositiveInfinity,2,3),"transit rejects nonfinite ground");
gate.Reset();gate.Observe(true,0,0,0,0);gate.Observe(true,.4f,.1f,0,0);
Check(!gate.Observe(true,.8f,.2f,0,0)&&gate.Samples==1,"cumulative slow ground drift resets the stable window");
Check(!gate.Observe(true,.1f,.2f,0,0)&&gate.Samples==1,"backwards time cannot complete a landing");
var canonical=new CatalogOrder(0,0,0,0,"Z");
Check(canonical.CompareTo(new CatalogOrder(0,0,1,0,"A"))<0,"canonical rank precedes alphabetic ID");
Check(CatalogOrder.FamilyRank("Еда","Фрукты")<CatalogOrder.FamilyRank("Еда","Овощи")&&
    CatalogOrder.FamilyRank("Еда","Овощи")<CatalogOrder.FamilyRank("Еда","Мясо"),"food families have separate fruit/vegetable/meat order");
Check(new CatalogOrder(1,0,0,2,"x").CompareTo(new CatalogOrder(1,0,1,0,"a"))<0,"largo order follows first then second native parent");
Check(new CatalogOrder(0,0,5,0,"z").CompareTo(new CatalogOrder(0,0,CatalogOrder.Unknown,0,"a"))<0,"unverified types cannot precede known encyclopedia entries");
Check(SafetyPolicy.CurrencyDelta(50, 100, 100) == 50, "currency clamps native cap");
Check(SafetyPolicy.CurrencyDelta(int.MaxValue - 5, int.MaxValue, int.MaxValue) == 5, "currency no signed overflow");
Check(SafetyPolicy.CurrencyDelta(100, 1, 100) == 0, "currency already full");
Check(SafetyPolicy.CurrencyDelta(100, -1, 1000) == 0, "currency rejects negative request");
Check(SafetyPolicy.CurrencyDelta(120, 100, 100) == 0, "currency above native cap");
Check(SafetyPolicy.AmmoFill == 100, "ammo fill exactly 100");
Check(!SafetyPolicy.NeedsRefill(-1)&&!SafetyPolicy.NeedsRefill(0),"auto refill never revives empty/invalid slot");
Check(SafetyPolicy.NeedsRefill(1)&&SafetyPolicy.NeedsRefill(25),"auto refill threshold inclusive");
Check(!SafetyPolicy.NeedsRefill(26)&&!SafetyPolicy.NeedsRefill(int.MaxValue),"auto refill does not overwrite large counts");
Check(SafetyPolicy.NearbyReturnOffset(0,.12f,0),"saved return accepts grounded center");
Check(SafetyPolicy.NearbyReturnOffset(3,2,0)&&SafetyPolicy.NearbyReturnOffset(0,-2,-3),"saved return accepts bounded edge");
Check(!SafetyPolicy.NearbyReturnOffset(3.01f,0,0)&&!SafetyPolicy.NearbyReturnOffset(3,0,3),"saved return rejects excessive horizontal shift");
Check(!SafetyPolicy.NearbyReturnOffset(0,2.01f,0)&&!SafetyPolicy.NearbyReturnOffset(0,-2.01f,0),"saved return does not jump floors");
Check(!SafetyPolicy.NearbyReturnOffset(float.NaN,0,0)&&!SafetyPolicy.NearbyReturnOffset(0,float.PositiveInfinity,0),"saved return rejects nonfinite offsets");
Check(SafetyPolicy.MaxWaypoints == 5, "waypoint limit five");
Check(StressComparisonPlan.TotalReloads==12,"ABBA checkpoint comparison has exactly twelve reloads");
Check(Enumerable.Range(0,3).All(i=>!StressComparisonPlan.Armed(i))&&
    Enumerable.Range(9,3).All(i=>!StressComparisonPlan.Armed(i)),"ABBA features-off blocks bracket the armed blocks");
Check(Enumerable.Range(3,6).All(StressComparisonPlan.Armed),"ABBA middle six reloads exercise armed conveniences");
Check(Enumerable.Range(0,12).Select(StressComparisonPlan.Name).Distinct().Count()==12,"ABBA sample labels are unique and bounded");
Throws(()=>StressComparisonPlan.Armed(-1),"ABBA rejects negative reload indices");
Throws(()=>StressComparisonPlan.Name(12),"ABBA rejects indices beyond its finite plan");
var boundaryMarkers = MapOverlayLayout.Place(new[] { (49f, 49f, 0), (51f, 49f, 1) }, MapOverlayLayout.DefaultSize);
Check(boundaryMarkers.Count == 2 && boundaryMarkers.Select(m => m.Item).OrderBy(i => i).SequenceEqual(new[] { 0, 1 }), "near objects across a grid boundary stay two separate markers");
Check(boundaryMarkers[0].X == 49f && boundaryMarkers[0].Y == 49f && boundaryMarkers[1].X == 51f && boundaryMarkers[1].Y == 49f, "both near objects keep their exact own map position instead of being pushed aside");
var crowded = MapOverlayLayout.Place(new[] { (0f, 0f, 0), (20f, 0f, 1) }, MapOverlayLayout.DefaultSize);
Check(crowded[0].Size == MapOverlayLayout.DefaultSize && crowded[1].Size == MapOverlayLayout.DefaultSize && crowded[1].Crowded && !crowded[0].Crowded, "a crowded neighbour keeps the same fixed icon size and is only flagged as overlapping");
var farApart = MapOverlayLayout.Place(new[] { (0f, 0f, 0), (400f, 0f, 1) }, MapOverlayLayout.DefaultSize);
Check(farApart.All(m => !m.Crowded && m.Size == MapOverlayLayout.DefaultSize), "isolated markers keep the icon size the player asked for");
Check(MapOverlayLayout.Place(new[] { (0f, 0f, 0), (400f, 0f, 1) }, MapOverlayLayout.MaxSize).All(m => m.Size == MapOverlayLayout.MaxSize), "the icon-size slider raises every isolated marker");
Check(MapOverlayLayout.Place(new[] { (0f, 0f, 0) }, 1000f).Single().Size == MapOverlayLayout.MaxSize && MapOverlayLayout.Place(new[] { (0f, 0f, 0) }, 1f).Single().Size == MapOverlayLayout.MinSize, "a hostile icon size is clamped into the readable range");
Check(MapOverlayLayout.Place(new[] { (0f, 0f, 0) }, MapOverlayLayout.MinSize).Single().HitBox == MapOverlayLayout.MinSize, "the smallest icon keeps an exact tappable hit box");
var layoutRandom = new Random(37);
var denseMarkers = Enumerable.Range(0, 1200).Select(i => ((float)layoutRandom.NextDouble() * 1920, (float)layoutRandom.NextDouble() * 1080, i)).ToArray();
var markerLayout = MapOverlayLayout.Place(denseMarkers, MapOverlayLayout.DefaultSize);
Check(markerLayout.Select(m => m.Item).OrderBy(i => i).SequenceEqual(Enumerable.Range(0, 1200)), "no map object is merged away or duplicated at any zoom");
bool markerAnchored = true;
for (int i = 0; i < markerLayout.Count; i++)
    markerAnchored &= markerLayout[i].X == denseMarkers[i].Item1 && markerLayout[i].Y == denseMarkers[i].Item2 && Equals(markerLayout[i].Item, denseMarkers[i].Item3);
Check(markerAnchored, "every marker is drawn on its own projected pixel, metre-accurate at every zoom");
Check(markerLayout.Select(m => m.Size).Distinct().Count() == 1 && markerLayout.Any(m => m.Crowded), "one atlas never mixes icon sizes even where markers overlap");
var repeatLayout = MapOverlayLayout.Place(denseMarkers, MapOverlayLayout.DefaultSize);
Check(markerLayout.Zip(repeatLayout).All(p => p.First.X == p.Second.X && p.First.Y == p.Second.Y && p.First.Size == p.Second.Size && Equals(p.First.Item, p.Second.Item)), "marker layout is deterministic for a fixed native point order");
Check(MapOverlayLayout.Place(new[] { (float.NaN, 0f, 0), (0f, float.PositiveInfinity, 1), (10f, 10f, 2) }, MapOverlayLayout.DefaultSize).Single().Item == 2, "map layout rejects non-finite projection samples");
string[] rawLayers = { "Капсулы", "Гордо", "Телепорты", "Ноды карты", "Дроны", "Ресурсы", "Еда", "Плорты", "Двери", "Механизмы", "Горшочки", "Спавнеры", "Ресурсы: ценные", "Редкое" };
var layerOrder = MapLayerOrder.Sorted(rawLayers);
Check(layerOrder.Length == rawLayers.Length && layerOrder.Distinct().Count() == layerOrder.Length, "every atlas layer survives sorting exactly once");
Check(layerOrder.Take(MapLayerOrder.Objects).SequenceEqual(new[] { "Капсулы", "Гордо", "Двери", "Механизмы", "Телепорты", "Ноды карты", "Дроны" }), "atlas panel lists real physical objects before finds and spawn surfaces");
Check(layerOrder.Skip(MapLayerOrder.Objects).SequenceEqual(new[] { "Горшочки", "Спавнеры", "Ресурсы", "Еда", "Плорты", MapLayerOrder.FandomGather, MapLayerOrder.FandomRare }), "finds and spawn surfaces keep one stable readable order");
Check(MapLayerOrder.Sorted(new[] { "Будущий слой", "Ресурсы" }).SequenceEqual(new[] { "Ресурсы", "Будущий слой" }), "an unknown future layer stays reachable at the end of the panel");
Check(MapLayerOrder.Short("Ноды карты") == "Ноды" && MapLayerOrder.Short(MapLayerOrder.FandomGather) == "Ценные" && MapLayerOrder.Short("Капсулы") == "Капсулы", "narrow grid captions stay short while other layers keep their full name");
Check(!SafetyPolicy.SameSession(IntPtr.Zero, IntPtr.Zero), "no confirmation without save");
Check(!SafetyPolicy.SameSession(new IntPtr(1), new IntPtr(2)), "stale-save confirmation rejected");
Check(SafetyPolicy.SameSession(new IntPtr(42), new IntPtr(42)), "matching-save confirmation accepted");
Check(SafetyPolicy.ForwardHour(3600, 7200) == 7200, "time forward-only");
Throws(() => SafetyPolicy.ForwardHour(3600, 0), "time refuses rewind");
Throws(() => SafetyPolicy.ForwardHour(0, double.NaN), "time refuses NaN");

Check(ExplorerPolicy.MaxCustomWaypoints==32&&ExplorerPolicy.MaxNameLength==48&&ExplorerPolicy.ColorCount==6,"custom markers have bounded count, names and palette");
Check(ExplorerPolicy.MaxRadarTargets==32&&ExplorerPolicy.RadarScanSeconds==1&&ExplorerPolicy.FeedUnitsPerTick==4,"radar output and feeder work budgets are bounded");
Check(ExplorerPolicy.NavigationHudTop>=176,"navigation rows leave native slime name and diet text unobscured");
Check(ExplorerPolicy.NavigationHudStride>=ExplorerPolicy.NavigationHudRowHeight+8,"simultaneous radar and custom compass rows retain a clear gap");
Check(ExplorerPolicy.Within(10000,100)&&!ExplorerPolicy.Within(10000.1f,100),"radar radius is inclusive but cannot extend into distant terrain");
Check(!ExplorerPolicy.Within(float.NaN,100)&&!ExplorerPolicy.Within(-1,30),"spatial helpers reject invalid distances");
Check(!ExplorerPolicy.Coordinates(float.NaN,0,0)&&!ExplorerPolicy.Coordinates(0,float.PositiveInfinity,0)&&!ExplorerPolicy.Coordinates(100000,0,0),"custom coordinates reject nonfinite/out-of-world values");
foreach(var pair in new[]{(0f,0),(45f,1),(90f,2),(180f,4),(-45f,7),(-90f,6),(360f,0),(22.49f,0),(22.5f,1)})
    Check(ExplorerPolicy.DirectionIndex(pair.Item1)==pair.Item2,"eight-sector compass "+pair.Item1);
Check(ExplorerPolicy.Conserved(25,24,50,51),"native food transfer conserves total items");
Check(!ExplorerPolicy.Conserved(25,25,50,51)&&!ExplorerPolicy.Conserved(25,24,50,50),"food transfer rejects duplication and lost portions");
Check(!ExplorerPolicy.Conserved(1,0,int.MaxValue,int.MinValue),"food conservation cannot certify an overflowing destination");
foreach(string name in new[]{"StrangeDiamondCraft","SunSapCraft","LavaDustCraft","PrimordyOilCraft"})
    Check(ExplorerPolicy.RareMaterial(name),"rare resource uses a verified native asset "+name);
Check(!ExplorerPolicy.RareMaterial("JellystoneCraft")&&!ExplorerPolicy.RareMaterial("DiamondGuess"),"radar cannot guess translated or unverified rare IDs");
Check(WaypointStore.SaveKey("20261013155400_5")&&!WaypointStore.SaveKey("../main")&&!WaypointStore.SaveKey("main.sav")&&!WaypointStore.SaveKey("main/other")&&!WaypointStore.SaveKey("main\\other"),"per-save marker filename rejects path traversal and save-file extensions");
var markerStore=new WaypointStore{Save="20261013155400_5"};
for(int i=0;i<32;i++)markerStore.Points.Add(new WaypointRecord{Id=Guid.NewGuid().ToString("N"),Name="Метка "+i,Group="zoneFields",Color=i%6,X=240+i,Y=8.7f,Z=350,ExactHeight=i%2==0});
string markerJson=markerStore.Json();var reloadedMarkers=WaypointStore.Read(markerJson,markerStore.Save);
Check(reloadedMarkers.Points.Count==32&&reloadedMarkers.Points.Zip(markerStore.Points).All(p=>p.First.Id==p.Second.Id&&p.First.Name==p.Second.Name&&p.First.Color==p.Second.Color&&p.First.ExactHeight==p.Second.ExactHeight&&p.First.X==p.Second.X),"persistent named colored markers round-trip all identity/position fields");
WaypointStore alteredMarker=null;
void RejectMarkers(Action alter,string title)
{
    var valid=WaypointStore.Read(markerJson,markerStore.Save);alteredMarker=valid;alter();
    bool rejected=false;try{valid.Validate(markerStore.Save);}catch(InvalidDataException){rejected=true;}
    Check(rejected,title);
}
RejectMarkers(()=>alteredMarker.Points.Add(alteredMarker.Points[0]),"custom store refuses a 33rd marker");
RejectMarkers(()=>alteredMarker.Points[1].Id=alteredMarker.Points[0].Id,"custom store rejects duplicate marker identity");
RejectMarkers(()=>alteredMarker.Points[0].Name=new string('x',49),"marker names cannot overflow their UI limit");
RejectMarkers(()=>alteredMarker.Points[0].Name="bad\nname","control characters cannot enter marker labels");
RejectMarkers(()=>alteredMarker.Points[0].Color=6,"marker colors cannot address beyond the palette");
RejectMarkers(()=>alteredMarker.Points[0].X=float.NaN,"invalid persistent marker coordinates fail closed");
RejectMarkers(()=>alteredMarker.Version=2,"unknown marker schema versions are not silently rewritten");
RejectMarkers(()=>alteredMarker.Save="other-save","custom markers cannot bleed across save identities");
bool largeMarkerRejected=false;try{WaypointStore.Read(new string(' ',128*1024+1),markerStore.Save);}catch(InvalidDataException){largeMarkerRejected=true;}
Check(largeMarkerRejected,"marker input has a bounded parse budget");

// Synthetic offline fixtures under the OS temp directory only. These bytes
// are not, and are never copied to, any native/user Slime Rancher save.
string auditFixtureDirectory=Path.Combine(Path.GetTempPath(),"CT-ImmutableFixture-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(auditFixtureDirectory);
string auditFixtureMain=Path.Combine(auditFixtureDirectory,"main_fixture_0.sav");
string auditFixtureOther=Path.Combine(auditFixtureDirectory,"other_fixture_2.sav");
File.WriteAllText(auditFixtureMain,"synthetic main fixture; not a game save");
File.WriteAllText(auditFixtureOther,"synthetic unrelated fixture; not a game save");
AuditImmutableBaseline FixtureBaseline()=>new()
{
    FormatVersion=1,ApprovalDecision="keep-current",ApprovalReference="OFFLINE SYNTHETIC TEST ONLY",
    ApprovedUtc="2026-01-01T00:00:00Z",MainName="main_fixture",MainSave="main_fixture_0",
    SavePath=auditFixtureDirectory,
    OriginalHashes=new Dictionary<string,string>
    {
        [auditFixtureMain]=AuditImmutableBaseline.Sha256(auditFixtureMain),
        [auditFixtureOther]=AuditImmutableBaseline.Sha256(auditFixtureOther)
    }
};
void RejectAuditFixture(Action action,string name)
{
    bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}
    Check(rejected,name);
}
try
{
    var baseline=FixtureBaseline();baseline.Verify();
    string original=File.ReadAllText(auditFixtureMain);
    string firstHash=AuditImmutableBaseline.Sha256(auditFixtureMain);
    baseline.Verify();Check(AuditImmutableBaseline.Sha256(auditFixtureMain)==firstHash,
        "immutable reference verification reads synthetic originals without rewriting them");
    Check(AuditImmutableBaseline.Checkpoint("copy_fixture","copy_fixture_0")&&
        AuditImmutableBaseline.Checkpoint("copy_fixture","copy_fixture_123.sav"),
        "owned native checkpoint accepts only an exact copy family and numeric checkpoint");
    foreach(string bad in new[]{"copy_fixture","copy_fixture_bad.sav","copy_fixture_1.sav.tmp","other_0.sav","../copy_fixture_0.sav"})
        Check(!AuditImmutableBaseline.Checkpoint("copy_fixture",bad),"copy family rejects non-exact/unsafe path "+bad);
    Check(!AuditImmutableBaseline.Checkpoint("","x_0.sav")&&
        !AuditImmutableBaseline.Checkpoint("../bad","../bad_0.sav"),"an absent or unsafe copy identity admits no extra native save");
    File.WriteAllText(auditFixtureMain,original+" changed");
    RejectAuditFixture(()=>baseline.Verify(),"changed original bytes fail the immutable reference instead of rolling it forward");
    File.WriteAllText(auditFixtureMain,original); // Synthetic fixture only.
    string rotated=Path.Combine(auditFixtureDirectory,"main_fixture_1.sav");
    File.WriteAllText(rotated,"synthetic rotation fixture");
    RejectAuditFixture(()=>baseline.Verify(),"new original-family checkpoint is detected even when every old original hash still matches");
    File.Delete(rotated);
    File.Move(auditFixtureOther,auditFixtureOther+".missing");
    RejectAuditFixture(()=>baseline.Verify(),"missing original checkpoint fails cross-run integrity");
    File.Move(auditFixtureOther+".missing",auditFixtureOther);
    string copy=Path.Combine(auditFixtureDirectory,"copy_fixture_0.sav");
    File.WriteAllText(copy,"synthetic owned-copy fixture");
    RejectAuditFixture(()=>baseline.Verify(),"copy-retired verification refuses even an otherwise owned extra checkpoint");
    baseline.Verify("copy_fixture");Check(File.Exists(copy),"only the exact registered copy family may coexist with immutable originals");
    RejectAuditFixture(()=>baseline.Verify("other_copy"),"a different copy family cannot excuse an unexpected native file");
    RejectAuditFixture(()=>baseline.Verify("main_fixture"),"owned-copy name cannot equal the protected main");
    RejectAuditFixture(()=>baseline.Verify("other_fixture"),"owned-copy name cannot collide with any unrelated immutable original");
    RejectAuditFixture(()=>baseline.ValidateOwnedCopy("MAIN_FIXTURE"),"Windows case-only main aliases cannot register as an owned copy");
    RejectAuditFixture(()=>baseline.ValidateOwnedCopy("OTHER_FIXTURE"),"Windows case-only unrelated-original aliases cannot register as an owned copy");
    Check(baseline.OriginalDestination("MAIN_FIXTURE_0.sav")&&
        baseline.OriginalDestination("MAIN_FIXTURE_0")&&
        baseline.OriginalDestination(auditFixtureMain.ToUpperInvariant()),
        "independent original-destination deny recognizes Windows case and native extensionless aliases");
    Check(!baseline.OriginalDestination("copy_fixture_0.sav"),"a distinct copy destination does not alias any original");
    File.Delete(copy);
    var invalid=FixtureBaseline();invalid.ApprovalDecision="auto-current";
    RejectAuditFixture(()=>invalid.Validate(),"an automatically chosen/rolled-forward current baseline is never accepted");
    invalid=FixtureBaseline();invalid.ApprovalReference="";
    RejectAuditFixture(()=>invalid.Validate(),"a baseline without explicit approval reference is rejected");
    invalid=FixtureBaseline();invalid.ApprovedUtc="2026-01-01T03:00:00+03:00";
    RejectAuditFixture(()=>invalid.Validate(),"baseline approval uses an explicit zero-offset UTC timestamp");
    invalid=FixtureBaseline();invalid.ApprovedUtc="2026-01-01T00:00:00";
    RejectAuditFixture(()=>invalid.Validate(),"machine-local timezone cannot supply a missing baseline approval timezone");
    invalid=FixtureBaseline();invalid.FormatVersion=2;
    RejectAuditFixture(()=>invalid.Validate(),"unknown immutable baseline schema cannot silently change save protection");
    invalid=FixtureBaseline();invalid.MainSave="main_fixture_99";
    RejectAuditFixture(()=>invalid.Validate(),"the approved exact main checkpoint must occur in immutable originals");
    invalid=FixtureBaseline();invalid.MainSave="../main_fixture_0";
    RejectAuditFixture(()=>invalid.Validate(),"immutable exact checkpoint is a leaf, never a traversal path");
    invalid=FixtureBaseline();invalid.OriginalHashes[auditFixtureMain]="not-a-sha";
    RejectAuditFixture(()=>invalid.Validate(),"malformed immutable SHA values fail closed");
    invalid=FixtureBaseline();invalid.OriginalHashes[Path.Combine(Path.GetTempPath(),"outside-fixture.sav")]=new string('a',64);
    RejectAuditFixture(()=>invalid.Validate(),"immutable original entries cannot escape the approved native save directory");
    string baselineFile=Path.Combine(auditFixtureDirectory,"fixture-baseline.json");
    File.WriteAllText(baselineFile,System.Text.Json.JsonSerializer.Serialize(baseline));
    string baselineFileHash=AuditImmutableBaseline.Sha256(baselineFile);
    AuditImmutableBaseline.Read(baselineFile).Verify();
    Check(AuditImmutableBaseline.Sha256(baselineFile)==baselineFileHash,"loading/verifying immutable baseline does not rewrite the reference file");
    File.WriteAllText(baselineFile,new string(' ',256*1024+1));
    RejectAuditFixture(()=>AuditImmutableBaseline.Read(baselineFile),"oversized immutable baseline has a bounded parse budget");
    File.Delete(baselineFile);
    RejectAuditFixture(()=>AuditImmutableBaseline.Read(baselineFile),"missing baseline is refused rather than generated from changed current saves");
    string checkpoint=Path.Combine(auditFixtureDirectory,"checkpoint.json");
    string runId=new string('a',32);
    string json1=System.Text.Json.JsonSerializer.Serialize(new{RunId=runId,Boot="run"});
    string json2=System.Text.Json.JsonSerializer.Serialize(new{RunId=runId,Boot="verify"});
    string json3=System.Text.Json.JsonSerializer.Serialize(new{RunId=runId,Boot="return"});
    AuditCheckpointStore.Write(checkpoint,json1);
    AuditCheckpointStore.Write(checkpoint,json2);
    Check(AuditCheckpointStore.Read(checkpoint,out bool previousUsed)==json2&&!previousUsed&&
        File.ReadAllText(checkpoint+".previous")==json1,"atomic checkpoint replacement retains the prior complete synthetic recovery");
    bool beforeCommitFailed=false;
    try{AuditCheckpointStore.Write(checkpoint,json3,stage=>{if(stage=="before-commit")throw new IOException("synthetic precommit failure");});}
    catch(IOException){beforeCommitFailed=true;}
    Check(beforeCommitFailed&&AuditCheckpointStore.Read(checkpoint,out previousUsed)==json2&&
        File.ReadAllText(checkpoint+".previous")==json1,"injected precommit interruption never truncates the published checkpoint or previous version");
    bool afterCommitFailed=false;
    try{AuditCheckpointStore.Write(checkpoint,json3,stage=>{if(stage=="after-commit")throw new IOException("synthetic postcommit interruption");});}
    catch(IOException){afterCommitFailed=true;}
    Check(afterCommitFailed&&AuditCheckpointStore.Read(checkpoint,out previousUsed)==json3&&
        File.ReadAllText(checkpoint+".previous")==json2,"an ambiguous postcommit exception is resolved by actual complete checkpoint readback");
    File.WriteAllText(checkpoint,"{"); // Synthetic metadata only.
    Check(AuditCheckpointStore.Read(checkpoint,out previousUsed)==json2&&previousUsed,
        "malformed current metadata retains a readable complete previous checkpoint");
    AuditCheckpointStore.Write(checkpoint,json3);
    Check(AuditCheckpointStore.Read(checkpoint,out previousUsed)==json3&&
        Directory.GetFiles(auditFixtureDirectory,"checkpoint.json.rejected-*").Length==1,
        "repair preserves opaque rejected synthetic metadata instead of overwriting the last valid previous checkpoint");
    AuditCheckpointStore.Retire(checkpoint,runId);
    Check(!AuditCheckpointStore.Exists(checkpoint)&&Directory.GetFiles(Path.Combine(auditFixtureDirectory,"audit-completed-recovery")).Length==2,
        "successful terminal retirement archives both current and previous recovery identities before ordinary startup");
    AuditCheckpointStore.Retire(checkpoint,runId);
    Check(!AuditCheckpointStore.Exists(checkpoint),"owned recovery retirement is idempotent after an interruption or duplicate acknowledgement");
    RejectAuditFixture(()=>AuditCheckpointStore.Write(checkpoint,"["),"incomplete synthetic JSON cannot replace a durable checkpoint");
}
finally{Directory.Delete(auditFixtureDirectory,true);}
Check(AuditSavePolicy.RestoreAutosaveDisabled(true,false)&&AuditSavePolicy.RestoreAutosaveDisabled(true,true),
    "protected cleanup never restores a pre-audit cached-false native autosave flag, including shutdown without a next Tick");
Check(!AuditSavePolicy.RestoreAutosaveDisabled(false,false)&&AuditSavePolicy.RestoreAutosaveDisabled(false,true),
    "normal non-audit cleanup preserves both native cached autosave settings");
Check(AuditSavePolicy.GameWrite(false,false,true,true,"","regular","Regular")&&
    AuditSavePolicy.NamedMutation(false,false,true,true,"","regular")&&
    AuditSavePolicy.FileMutation(false,false,true,true,"","regular_0.sav"),
    "unarmed normal gameplay retains native saving, cleanup and quit behavior");
Check(AuditSavePolicy.GameWrite(true,true,false,false,"copy_fixture","copy_fixture","[CT AUDIT] Synthetic"),
    "armed ready audit permits native serialization of its exact ownership-marked copy");
foreach(var state in new[]{(Ready:false,Closing:false,Probe:false),(Ready:true,Closing:true,Probe:false),(Ready:true,Closing:false,Probe:true)})
{
    Check(!AuditSavePolicy.GameWrite(true,state.Ready,state.Closing,state.Probe,"copy_fixture","copy_fixture","[CT AUDIT] Synthetic"),
        "setup failure, process closing or read-only copy probe refuses native game save "+state);
    Check(!AuditSavePolicy.NamedMutation(true,state.Ready,state.Closing,state.Probe,"copy_fixture","copy_fixture"),
        "setup failure, process closing or read-only copy probe refuses cleanup/delete "+state);
    Check(!AuditSavePolicy.FileMutation(true,state.Ready,state.Closing,state.Probe,"copy_fixture","copy_fixture_0.sav"),
        "setup failure, process closing or read-only copy probe refuses lower-level copy file write "+state);
}
Check(!AuditSavePolicy.GameWrite(true,true,false,false,"copy_fixture","main_fixture","Main")&&
    !AuditSavePolicy.GameWrite(true,true,false,false,"copy_fixture","other_fixture","Other"),
    "armed audit refuses every original game, not only the selected main");
Check(!AuditSavePolicy.GameWrite(true,true,false,false,"copy_fixture","copy_fixture","User game")&&
    !AuditSavePolicy.GameWrite(true,true,false,false,"","copy_fixture","[CT AUDIT] Synthetic"),
    "native metadata needs both exact registered copy identity and explicit native ownership label");
Check(AuditSavePolicy.NamedMutation(true,true,false,false,"copy_fixture","copy_fixture")&&
    !AuditSavePolicy.NamedMutation(true,true,false,false,"copy_fixture","main_fixture"),
    "native cleanup and deletion name gate admits only the owned copy");
Check(AuditSavePolicy.FileMutation(true,true,false,false,"copy_fixture","copy_fixture_7.sav")&&
    !AuditSavePolicy.FileMutation(true,true,false,false,"copy_fixture","main_fixture_0.sav"),
    "lower-level native game file mutation uses the exact owned-copy checkpoint gate");

string root = args.Length > 0 ? args[0] : throw new ArgumentException("Project root required");
string dll = Path.Combine(root, "_work", "creator-stage", "CreatorToolkit.dll");
using (var pe = new PEReader(File.OpenRead(dll)))
{
    var md = pe.GetMetadataReader();
    Check(md.GetString(md.GetAssemblyDefinition().Name) == "CreatorToolkit", "standalone DLL identity");
    var embedded = md.ManifestResources.Select(h => md.GetString(md.GetManifestResource(h).Name)).ToArray();
    Check(embedded.Contains("CreatorToolkit.LocEnglish.tsv") && embedded.Contains("CreatorToolkit.icon.png"), "0.8 English UI table and mod icon are embedded in the main assembly");
    Check(!File.Exists(Path.Combine(root, "_work", "creator-stage", "en", "CreatorToolkit.resources.dll")), "no satellite resource assembly next to the release DLL");
    var refs = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToArray();
    Check(!refs.Any(n => n.Contains("Minecraft", StringComparison.OrdinalIgnoreCase) || n.Contains("McHud", StringComparison.OrdinalIgnoreCase) || n.Contains("WebSocket", StringComparison.OrdinalIgnoreCase)), "no Minecraft/bridge/socket assembly dependency");
    Check(refs.Contains("MelonLoader") && refs.Contains("Assembly-CSharp"), "native MelonLoader and game API references");
    var types = md.TypeDefinitions.Where(h => md.GetTypeDefinition(h).GetDeclaringType().IsNil).ToDictionary(h => md.GetString(md.GetTypeDefinition(h).Namespace) + "." + md.GetString(md.GetTypeDefinition(h).Name));
    Check(md.GetAssemblyDefinition().Version==new Version(0,8,0,0),"release assembly version is 0.8.0");
    foreach (string name in new[] { "ToolkitMod", "ToolkitUi", "InputCapture", "PlayerTools", "ActorTools", "WorldTools", "UnlockTools", "CinemaTools", "MapTools", "PrototypeTools", "NativeUiProbe", "NativeAtlasProbe", "ConvenienceTools", "StressSuite", "DiagnosticIo", "NativeHooks", "EngineFrameProbe", "StressComparisonPlan", "TransitSafety", "CatalogOrder", "CapsuleDatabase", "CapsuleTools","ExplorerPolicy","WaypointStore","WaypointRecord","CustomWaypointTools","RadarTools","FarmTools","UiThemePolicy","CarePolicy","SlimeCareTools","GadgetTools","NativeIconTools","PhenomenaTools","PhenomenaWindowPolicy","AuditSaveGuard","AuditSavePolicy","AuditImmutableBaseline","AuditCheckpointStore" })
        Check(types.ContainsKey("CreatorToolkit." + name), "packaged module " + name);
    var mod = md.GetTypeDefinition(types["CreatorToolkit.ToolkitMod"]);
    var callbacks = mod.GetMethods().Select(h => md.GetString(md.GetMethodDefinition(h).Name)).ToArray();
    foreach (string callback in new[] { "OnInitializeMelon", "OnUpdate", "OnLateUpdate", "OnGUI", "OnApplicationQuit" }) Check(callbacks.Contains(callback), "Melon callback " + callback);
    var resources=md.ManifestResources.Select(h=>md.GetString(md.GetManifestResource(h).Name)).ToArray();
    Check(resources.Contains("CreatorToolkit.CreatorAtlas.rgba")&&resources.Contains("CreatorToolkit.CapsuleContents.json"),"single icon atlas and versioned capsule database embedded in release");
}

using (var pe = new PEReader(File.OpenRead(Path.Combine(root, "refs", "Assembly-CSharp.dll"))))
{
    var md = pe.GetMetadataReader();
    var types = md.TypeDefinitions.Where(h => md.GetTypeDefinition(h).GetDeclaringType().IsNil).ToDictionary(h => md.GetString(md.GetTypeDefinition(h).Namespace) + "." + md.GetString(md.GetTypeDefinition(h).Name));
    void Api(string type, string method)
    {
        if (!types.TryGetValue(type, out var h)) throw new InvalidOperationException("Missing native type " + type);
        Check(md.GetTypeDefinition(h).GetMethods().Any(m => md.GetString(md.GetMethodDefinition(m).Name) == method), "native hook target " + type + "." + method);
    }
    Api("Il2Cpp.PlayerDamageable", "Damage");
    Api("Il2Cpp.PlayerState", "Damage");
    Api("Il2Cpp.PlayerState", "CanBeDamaged");
    Api("Il2CppMonomiPark.SlimeRancher.DataModel.PlayerModel", "LoseHealth");
    Api("Il2Cpp.PlayerDeathHandler", "OnDeath");
    Api("Il2Cpp.PlayerState", "SpendEnergy");
    Api("Il2CppMonomiPark.SlimeRancher.DataModel.PlayerModel", "SpendEnergy");
    Api("Il2CppMonomiPark.UnitPropertySystem.UnitResource", "set_Value");
    Api("Il2Cpp.TimeDirector", "Update");
    Api("Il2Cpp.TimeDirector", "LateUpdate");
    Api("Il2CppMonomiPark.SlimeRancher.SubbehaviourPlexer", "UpdateInstance");
    Api("Il2CppMonomiPark.SlimeRancher.SubbehaviourPlexer", "FixedUpdateInstance");
    Api("Il2CppMonomiPark.SlimeRancher.Input.InputDirector", "UpdateInputMaps");
    Api("Il2CppMonomiPark.SlimeRancher.Input.InputDirector", "ForceUpdateInputMaps");
    Api("Il2CppMonomiPark.SlimeRancher.AutoSaveDirector", "SaveAllNow");
    Api("Il2CppMonomiPark.SlimeRancher.AutoSaveDirector", "SaveGameAndFlush");
    foreach(string name in new[]{"SaveIfInGameplay","SaveGame_GetSummary","SaveGame","SaveGameImpl",
        "CleanupAutosaves","DeleteGame","OnApplicationQuit","SetAutosaveDisabled"})
        Api("Il2CppMonomiPark.SlimeRancher.AutoSaveDirector",name);
    foreach(string name in new[]{"StoreGameData","DeleteGameData","DeleteGamesData","StoreDataStream"})
        Api("Il2Cpp.FileStorageProvider",name);
    Api("Il2CppMonomiPark.SlimeRancher.SceneManagement.SceneLoader", "LoadSceneGroup");
    Api("Il2CppMonomiPark.SlimeRancher.SceneManagement.SceneLoader", "IsAssetLoaded");
    Api("Il2CppMonomiPark.SlimeRancher.Pedia.PediaRuntimeCategory", "get_Items");
    Api("Il2Cpp.IdentifiableTypeGroup", "IsMember");
    Api("Il2Cpp.TeleportablePlayer", "PrepareForUnmanagedTeleport");
    Api("Il2CppMonomiPark.SlimeRancher.Weather.WeatherRegistry", "RemoveWeatherPatternInstance");
    Api("Il2Cpp.LookupDirector","GetPlotPrefab");
    Api("Il2Cpp.LandPlotLocation","Replace");
    Api("Il2Cpp.LandPlot","AddUpgrade");
    Api("Il2Cpp.PlortCollector","StartCollection");
    Api("Il2Cpp.SiloStorage","CanAnySlotAccept");
    Api("Il2Cpp.SiloStorage","MaybeAddToAnySlot");
    Api("Il2CppMonomiPark.SlimeRancher.Player.AmmoSlotManager","PopFromSelectedSlot");
    Api("Il2CppMonomiPark.SlimeRancher.Regions.RegionMember","IsInZone");
    Api("Il2Cpp.SlimeEat","MaybeSpinAndChomp");
    Api("Il2Cpp.SlimeEat","MaybeChomp");
    Api("Il2Cpp.Chomper","CanChomp");
    Api("Il2Cpp.SlimeEat","WillNowEat");
    Api("Il2Cpp.SlimeEat","GetEatMapById");
    Api("Il2CppMonomiPark.SlimeRancher.Slime.SlimeEmotions","Get");
    Api("Il2Cpp.GadgetDirector","HasBlueprint");
    Api("Il2Cpp.GadgetDirector","CanPlaceGadget");
    Api("Il2CppMonomiPark.SlimeRancher.DataModel.GadgetsModel","TrySelectGadget");
    Api("Il2CppMonomiPark.SlimeRancher.DataModel.GadgetsModel","SetLockedGadgetIfNotFavorite");
    Api("Il2CppMonomiPark.SlimeRancher.Weather.WeatherDirector","StopState");
    Api("Il2CppMonomiPark.SlimeRancher.Pedia.PediaEntry","get_Icon");
    Api("Il2CppMonomiPark.SlimeRancher.Player.Component.UpgradeComponent","get_Icon");
    Api("Il2CppMonomiPark.SlimeRancher.Aurora.AuroraObject","InTriggerWindow");
    Api("Il2Cpp.ReverseGravityZone","GetOperating");
    Api("Il2CppMonomiPark.SlimeRancher.ObservableValue`1","Set");
    Api("Il2CppMonomiPark.SlimeRancher.Player.PlayerItems.GadgetItem","PlaceGadgetEvent");
    Api("Il2CppMonomiPark.SlimeRancher.Player.PlayerItems.GadgetItem","IsPlacementValid");
    Api("Il2CppMonomiPark.SlimeRancher.Player.PlayerItems.GadgetItem","IsInLinkedGadgetRange");
    Api("Il2CppMonomiPark.SlimeRancher.World.Gadget","IsCompletelyGrounded");
    Api("Il2CppMonomiPark.SlimeRancher.World.Gadget","IsOverlapping");
    Api("Il2CppMonomiPark.SlimeRancher.Player.PlayerItems.PlayerItemController","get_GadgetItem");
    Api("Il2CppMonomiPark.SlimeRancher.Player.PlayerItems.PlayerItemController","ToggleItem");
}

using(var pe=new PEReader(File.OpenRead(Path.Combine(root,"refs","UnityEngine.CoreModule.dll"))))
{
    var md=pe.GetMetadataReader();
    var type=md.TypeDefinitions.Single(h=>md.GetString(md.GetTypeDefinition(h).Namespace)=="UnityEngine.Scripting"&&md.GetString(md.GetTypeDefinition(h).Name)=="GarbageCollector");
    var methods=md.GetTypeDefinition(type).GetMethods().Select(h=>md.GetString(md.GetMethodDefinition(h).Name)).ToArray();
    Check(methods.Contains("GetMode")&&methods.Contains("SetMode"),"native Unity GC mode inspection/restoration APIs verified in the actual bindings");
    foreach(var target in new[]{
        ("Unity.Profiling.LowLevel.Unsafe","ProfilerRecorderHandle","GetByName__Unmanaged"),
        ("Unity.Profiling","ProfilerRecorder","Dispose"),
        ("UnityEngine","FrameTimingManager","GetLatestTimings"),
        ("UnityEngine.Profiling","Profiler","GetMonoUsedSizeLong")})
    {
        var found=md.TypeDefinitions.Single(h=>md.GetString(md.GetTypeDefinition(h).Namespace)==target.Item1&&md.GetString(md.GetTypeDefinition(h).Name)==target.Item2);
        Check(md.GetTypeDefinition(found).GetMethods().Any(h=>md.GetString(md.GetMethodDefinition(h).Name)==target.Item3),
            "native diagnostic binding "+target.Item2+"."+target.Item3);
    }
}

string hooks = File.ReadAllText(Path.Combine(root, "src", "creator", "NativeHooks.cs"));
Check(!hooks.Contains("PatchAll("), "no duplicate automatic PatchAll installation");
Check(!hooks.Contains("Hook(typeof(KFCCharacterController), \"UpdateVelocity\""), "no ref-Vector3 native velocity detour that corrupted ordinary movement");
string toolkit = File.ReadAllText(Path.Combine(root, "src", "creator", "ToolkitMod.cs"));
Check(toolkit.Contains("SameSession(confirmed.Session, Session)"), "confirmation validates captured save");
Check(toolkit.Contains("Pending = null;"), "closing menu discards confirmation");
string actors = File.ReadAllText(Path.Combine(root, "src", "creator", "ActorTools.cs"));
Check(actors.Contains("foreach (GameObject go in _owned)"), "cleanup enumerates owned objects only");
Check(actors.Split("TryCast<GadgetDefinition>()", StringSplitOptions.None).Length >= 3, "gadget types excluded from actor catalog and guarded before model registration");
Check(actors.Contains("slot.SetFromMetadata(before)") && actors.Contains("slot.Count = beforeCount"), "failed ammo injection rolls back native slot");
string player = File.ReadAllText(Path.Combine(root, "src", "creator", "PlayerTools.cs"));
Check(player.Contains("Object.Instantiate(_original)"), "speed edits private cloned parameters");
Check(player.Contains("_controller._parameters != _clone) RestoreParameters()"), "speed refresh respects freshly applied native upgrade parameters");
Check(player.Contains("Object.Instantiate(_jumpOriginal)")&&player.Contains("_jumpBehavior.AbilityData=_jumpClone")&&
    player.Contains("_jumpBehavior.AbilityData==_jumpClone")&&player.Contains("_jumpClone._jumpUpSpeed=_jumpBaseSpeed*factor"),
    "high jump borrows a live native behavior and restores only its privately owned data clone");
Check(player.Contains("GodMode = InfiniteEnergy = HighJump = InfiniteItems = false")&&
    player.Contains("manager.Pointer!=own.Pointer")&&player.Contains("slots[i]?.Pointer==slot.Pointer"),
    "new cheats reset per session and preserve ammo only in the actual player's inventory");
string unlocks = File.ReadAllText(Path.Combine(root, "src", "creator", "UnlockTools.cs"));
Check(unlocks.Contains("var registered = model.upgradeDefinitions;") && unlocks.Contains("definitions.Add(registered[i])") && unlocks.Contains("foreach (UpgradeDefinition definition in definitions)"), "upgrades snapshot concrete indexed native definitions before mutating callbacks");
Check(unlocks.Contains("keys.Add(pair.Key)") && unlocks.Contains("foreach(int key in keys)handler.RemoveModifiers(key)"), "upgrade notifications pre-clear native modifier keys from a managed snapshot");
Check(unlocks.Contains("director.DebugAllUnlocked();") && !unlocks.Contains("NativeEnumerable.Items(director.AllEntries())"), "pedia uses native complete-unlock API without covariant enumeration");
Check(actors.Contains("ctx.GameModel.DestroyIdentifiableModel(model);"), "failed prefab creation rolls back registered actor model");
Check(toolkit.Contains("(\"weather\",World.TickWeather)") && toolkit.Contains("(\"map unlock\",Unlocks.Tick)") && toolkit.Contains("foreach(var tick in _readyTicks)SafeTick(tick.Label,tick.Run)"), "weather failure cannot block map completion or other world tools");
string world = File.ReadAllText(Path.Combine(root, "src", "creator", "WorldTools.cs"));
Check(world.Contains("if (!match) continue;") && !world.Contains("entry.Tier <= 0"), "registered zero-map-tier weather states remain selectable");
Check(world.Contains("pattern.StartingTransitions") && world.Contains("pattern.RunningTransitions") && world.Contains("AddState(group.FromState)") && !world.Contains("pattern.DEBUG_GetAllStates()") && world.Contains("foreach (IWeatherState state in states)"), "weather snapshots read typed transition graph without covariant native HashSet enumeration/casts");
Check(world.Contains("Weather.AddRange(entries);") && world.LastIndexOf("WeatherZone = zone;", StringComparison.Ordinal) > world.IndexOf("Weather.AddRange(entries);", StringComparison.Ordinal), "weather zone cache committed only after complete successful refresh");
Check(player.Contains("failed safety query must never leave native collisions disabled"), "noclip exit has collision restoration finally");
Check(player.Contains("finally { _motor.enabled = _oldMotorEnabled; }") && !player.Contains("OverrideVelocity("), "flight restores native motor without intercepting normal velocity");
string ui = File.ReadAllText(Path.Combine(root, "src", "creator", "ToolkitUi.cs"));
Check(!ui.Contains("GUI.HorizontalSlider("), "no stripped native horizontal slider calls");
Check(!ui.Contains("GUI.BeginScrollView("), "no stripped native scrollbar path");
Check(!ui.Contains("GUI.TextField("), "no stripped GUIStateObjects text-editor path");
Check(!ui.Contains("GUI.FocusControl("), "no stripped focus path on tab/slider interaction");
Check(!ui.Contains("GUI.DrawTextureWithTexCoords("), "no crashing managed texture-argument wrapper; atlas sprites use native DrawTexture plus clipping");
Check(!toolkit.Contains("kb.f1Key.wasPressedThisFrame")&&toolkit.Contains("if (kb.insertKey.wasPressedThisFrame)")&&
    ui.Contains("\"Insert  ·  Esc\"")&&!ui.Contains("\"МИР ЗАГРУЖЕН\"")&&
    !ui.Contains("SLIME RANCHER 2  ·  мастерская создателя"),"LO header cleanup and Insert-only hotkey are explicit");
Check(ui.Contains("ammo.GetSlotCount(i)")&&ui.Contains("179, sw, 44")&&
    ui.Contains("DrawRoundedSurface(new Rect(x+14")&&!ui.Contains("_tabIcons"),
    "slot row uses non-overlapping forty-pixel faces and scrollbar uses a scaled capsule, not an egg");
string nativeText=File.ReadAllText(Path.Combine(root,"src","creator","NativeTextInput.cs"));
Check(!nativeText.Contains("add_onTextInput")&&!nativeText.Contains("Action<char>")&&
    nativeText.Contains("InputSystem.onEvent+=_imeEventCallback")&&
    nativeText.Contains("InputSystem.onEvent-=_imeEventCallback")&&!ui.Contains("e.character.ToString()"),
    "focused fields consume one native event source and unsubscribe it on blur without a lossy char trampoline");
Check(nativeText.Contains("Marshal.SizeOf<TextEvent>()==24")&&
    nativeText.Contains("Marshal.OffsetOf<TextEvent>(nameof(TextEvent.character)).ToInt32()==20")&&
    nativeText.Contains("int scalar=*(int*)(raw+20)")&&nativeText.Contains("TextEditBuffer.ScalarValid(scalar)"),
    "actual native TextEvent ABI is checked before reading its bounded UTF-32 payload");
Check(nativeText.Contains("TextEditBuffer.ImePayloadValid")&&nativeText.Contains("ptr.sizeInBytes<24")&&
    !nativeText.Contains("Action<IMECompositionString>")&&!nativeText.Contains("new IMECompositionString("),
    "IME preview validates actual event bytes without entering the truncated generated value bridge");
Check(nativeText.Contains("_pendingTextWrites[_editingField]=_textBuffer.Value")&&
    ui.Contains("_lastReturnedText=value")&&ui.Contains("_label.CalcSize(RawContent(_textBuffer.CaretPrefix))"),
    "blur retains unpainted commits and caret uses the true native glyph widths");
string suite=File.ReadAllText(Path.Combine(root,"src","creator","FeatureSuite.cs"));
string nativeReference=File.ReadAllText(Path.Combine(root,"src","creator","NativeUiReferences.cs"));
Check(nativeReference.Contains("_nativeReferenceDirector._pediaDisplay")&&
    nativeReference.Contains("OpenDisplay(_nativeReferenceName)")&&
    nativeReference.Contains("TryGetRequestedDisplay(out var display)")&&
    nativeReference.Contains("display.IsDoneLoading")&&!nativeReference.Contains("Resources.FindObjectsOfTypeAll<PediaRoot>"),
    "original Pedia reference awaits its actual configured asynchronous request, never a guessed prefab/root census");
Check(nativeReference.Contains("CloseDisplay<Il2CppSystem.Object>(_nativePediaRequest)")&&
    nativeReference.Contains("if(_nativeReferenceClosing||_nativePediaRequest==null)return")&&
    nativeReference.Contains("UIDirector.ShowPopupsAndTutorials")&&
    nativeReference.Contains("ReleasePreloadPedia")&&!nativeReference.Contains("Time.timeScale="),
    "owned native reference closes without a root and restores its exact popup/preload leases without forced unpausing");
Check(nativeReference.Contains("value==null||value.WasCollected?\"\":value.name")&&
    !nativeReference.Contains("RootGameObject?.name"),
    "native reference diagnostics respect Unity fake-null after the owned display has been destroyed");
Check(suite.Contains("if(CriticalNativeStep(candidate.Name))")&&suite.Contains("if(CriticalNativeStep(step.Name))")&&
    nativeReference.Contains("name==\"UI / native LMB sliders and scrollbar\""),
    "critical asynchronous/menu/input failures stop the native matrix before dependent motion tests");
string loTests=File.ReadAllText(Path.Combine(root,"src","creator","LoPlayerTests.cs"));
Check(loTests.Contains("GuardCopy();")&&loTests.Contains("NativeKeyboardProbe.PressAuxiliary")&&
    loTests.Contains("Controller.Motor.TransientPosition.y-_loJumpOrigin.y")&&
    loTests.Contains("ammo.PopFromSelectedSlot(out var type,out var appearance)")&&
    loTests.Contains("ammo.Decrement(0,2)")&&loTests.Contains("ammo.Decrement(_loAmmoEntry.Type,2)"),
    "LO player matrix includes real native jump motion, both shot/pop and both native spending overloads");
Check(loTests.Contains("ammo.Clear(0)")&&loTests.Contains("ammo.GetSlotCount(0)==0")&&
    loTests.Contains("RetireLoFixtures();")&&suite.Contains("BuildLoPlayerSteps();"),
    "infinite-vacpack matrix tests empty/last/clear/default-off/reset semantics on the owned copy");
Check(player.Contains("HideFlags.DontUnloadUnusedAsset")&&player.Contains("if(asset.hideFlags==expected)asset.hideFlags=before")&&
    player.Contains("ReleaseBorrowedAsset(_original")&&player.Contains("ReleaseBorrowedAsset(_jumpOriginal")&&
    loTests.Contains("Resources.UnloadUnusedAssets()")&&loTests.Contains("_loNativeJump.hideFlags==_loJumpFlags"),
    "private movement assets lease native originals through unused-asset unload and restore only their own flags");
string textProbe=File.ReadAllText(Path.Combine(root,"src","creator","NativeTextProbe.cs"));
Check(textProbe.Contains("GuardStressCopy();")&&textProbe.Contains("StateEvent.From(keyboard")&&
    textProbe.Contains("InputSystem.QueueTextEvent")&&textProbe.Contains("ptr.sizeInBytes!=bytes")&&
    !textProbe.Contains("IMECompositionEvent.Create("),
    "text/IME probe snapshots native device headers and never calls the incomplete generated event constructor");
Check(suite.Contains("if(_isolatedSession==IntPtr.Zero&&(_phase==1||_phase==3))_isolatedSession=_app.Session;")&&
    !suite.Contains("if(!Active)return;\n        var meta=NativeSaveMetadata.Read(Save);"),
    "native smoke input binds only an identified fresh-process copy before the stricter same-session guard");
string smoke = File.ReadAllText(Path.Combine(root, "src", "creator", "SmokeTests.cs"));
Check(!smoke.Contains("InputState.Change(_key"), "no invalid direct bitfield keyboard writes");
Check(smoke.Contains("Vector3 end=_controller.Motor.TransientPosition") && !smoke.Contains("_origin=_app.Context.Player.transform.position"), "walking/restore use authoritative native motor position, not scene root");
Check(smoke.Contains("backgroundBehavior=_oldInputBackground"), "opt-in background input policy restored after native movement probe");
string keyProbe = File.ReadAllText(Path.Combine(root, "src", "creator", "NativeKeyboardProbe.cs"));
Check(keyProbe.Contains("StateEvent.From(keyboard,out InputEventPtr eventPtr") && keyProbe.Contains("InputSystem.QueueEvent(eventPtr)"), "movement test queues correctly sized native keyboard state event");
Check(keyProbe.Contains("keyboard.wKey.isPressed") && !keyProbe.Contains("keybd_event("), "test does not override held W or inject keys into other applications");
string capture = File.ReadAllText(Path.Combine(root, "src", "creator", "InputCapture.cs"));
Check(!capture.Contains("action.Disable()") && !capture.Contains("a.Disable()"), "no per-action disabling behind native director");
Check(capture.Contains("director.ForceUpdateInputMaps(_resumeUiMode)"), "native input mode restored after menu close");
Check(hooks.Contains("Hook(typeof(InputDirector), \"UpdateInputMaps\"") && !hooks.Contains("Hook(typeof(InputDirector), \"ForceUpdateInputMaps\""), "single input-mode detour avoids forced-entry recursion");
Check(capture.Contains("GetInputActionFromEvent(input._moveVertical,movement)"), "resume mode captured from native movement actions");
Check(toolkit.Contains("!loader.IsSceneLoadInProgress && loader.IsCurrentSceneGroupGameplay()"), "gameplay readiness excludes menus and scene loading");
Check(toolkit.Contains("!save.SaveAllNow(false,null)") && toolkit.Contains("save.SaveGameAndFlush();"), "quit requires confirmed native save and flush");
string saveMeta=File.ReadAllText(Path.Combine(root,"src","creator","NativeSaveMetadata.cs"));
Check(saveMeta.Contains("IL2CPP.il2cpp_field_get_value(") && !saveMeta.Contains("IL2CPP.il2cpp_value_box("), "native metadata reads avoid Nullable boxing/unwrapping bug");
Check(actors.Contains("slot.Definition.IsAllowed(entry.Type)") && actors.Contains("if(!CanStore(entry,index))"), "ammo checks native slot compatibility before clearing contents");
Check(ui.Contains("GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white") && ui.Contains("GUI.contentColor=originalContent"), "own GUI colors restored without inheriting game dimming");
string atlas=File.ReadAllText(Path.Combine(root,"src","creator","MapTools.cs"));
Check(atlas.Contains("header==\"#catalog=\"+installed"),"atlas index must match installed catalog hash");
Check(atlas.Contains("new Il2CppSystem.Nullable<Vector3>()"),"map teleport uses a valid native Nullable wrapper");
Check(atlas.Contains("save.SetAutosaveDisabled(true)")&&atlas.Contains("RestoreAutosaveDisabled(AuditSaveGuard.Required,_oldAutosave)"),"map transit suspends autosave and restores its cached value only outside protected audits");
Check(atlas.Contains("TryMapGround")&&atlas.Contains("NativeTeleport(_origin.Position,_origin.Group)"),"map teleport validates terrain and returns on failure");
Check(atlas.Contains("marker.SceneGroup")&&atlas.Contains("TryFindZoneAt"),"map click resolves native scene and rejects sea");
Check(ui.Contains("_dragScroll = id")&&ui.Contains("Mouse.current.leftButton.isPressed"),"scrollbar implements native LMB dragging");
Check(ui.Contains("var bases = slime.BaseSlimes;")&&ui.Contains("bases[0]?.Icon")&&ui.Contains("bases[1]?.Icon"),"largos combine both native parent icons");
Check(actors.Contains("CreatorToolkit.VisualPreview.")&&actors.Contains("entry.IsPrototype"),"missing slimes are explicitly labeled visual previews");
var index=File.ReadAllLines(Path.Combine(root,"src","creator","MapIndex.tsv"));
Check(index.Length>250&&index[0].StartsWith("#catalog="),"versioned offline map index packaged");
Check(index.Skip(1).Count(l=>l.Split('\t')[1]=="Капсулы")>=140,"map index includes unloaded capsules and shadow caches");
string capsulePath=Path.Combine(root,"src","creator","CapsuleContents.json");
using(var stream=File.OpenRead(capsulePath))
{
    var database=CapsuleDatabase.Read(stream,index[0].Substring("#catalog=".Length));
    Check(database.Pods.Count==152&&database.Assets.Count==126,"capsule database contains all 152 ID records and 126 resolved assets");
    var ids=index.Skip(1).Select(l=>l.Split('\t')).Where(c=>c[1]=="Капсулы").Select(c=>c[0]).ToHashSet(StringComparer.Ordinal);
    Check(ids.SetEquals(database.Pods.Keys),"every indexed capsule has an exact ID record, without name heuristics");
    var diamond=database.Pods["pod1210187464"].Rewards.ToDictionary(a=>database.Assets[a.Key].AssetName,a=>a.Count);
    Check(diamond.Count==3&&diamond["StrangeDiamondCraft"]==1&&diamond["DeepBrineCraft"]==2&&diamond["JellystoneCraft"]==3,"repeated native spawn references aggregate exact mixed reward quantities");
    Check(database.Pods.Values.Count(p=>p.Rewards.Count==0)==2,"two unassigned native rewards remain explicitly unknown, not invented");
    Check(database.Pods.Values.Count(p=>p.ShadowCache)==22,"shadow-cache IDs remain distinct from ordinary treasure pods");
    Check(database.Assets.Values.All(a=>!string.IsNullOrWhiteSpace(a.AssetPath)&&!string.IsNullOrWhiteSpace(a.File)&&long.TryParse(a.PathId,out _)),"each reward retains exact native asset/file/path-ID provenance");
    Check(database.Assets.Values.All(a=>a.Title!=null&&(a.Title.Key!=0||!string.IsNullOrEmpty(a.Title.Name))&&!string.IsNullOrEmpty(a.Title.Table)),"every reward title retains its actual numeric or string native localization key");
    Check(database.Assets.Values.Count(a=>!string.IsNullOrEmpty(a.Title.Name))==9,"nine material rewards preserve string keys rather than losing them as zero numeric IDs");
    Check(database.Assets.Values.Count(a=>a.Title.Table.StartsWith("GUID:",StringComparison.Ordinal))==86&&
        database.Assets.Values.Where(a=>a.Title.Table.StartsWith("GUID:",StringComparison.Ordinal)).All(a=>Guid.TryParse(a.Title.Table.Substring(5),out _)),
        "86 GUID-backed title tables retain valid typed-GUID payloads rather than literal names");
}
using(var stream=File.OpenRead(capsulePath))Check(CapsuleDatabase.Read(stream,"different-game-version").Pods.Count==0,"capsule contents fail closed on a different Addressables catalog");
Check(File.ReadAllBytes(Path.Combine(root,"src","creator","CreatorAtlas.rgba")).Length==256*256*4,"sixteen map/menu pictograms share one bounded 256×256 texture");
string comfort=File.ReadAllText(Path.Combine(root,"src","creator","ConvenienceTools.cs"));
Check(comfort.Contains("_refillSession!=_app.Session")&&comfort.Contains("ammo._selectedAmmoIdx!=_refillSlot")&&comfort.Contains("slot.Id.ReferenceId!=_refillId"),"auto refill is bound to session, selected slot and native resource ID");
Check(comfort.Contains("slot.Count<=0")&&comfort.Contains("SafetyPolicy.NeedsRefill(slot.Count,target)")&&!comfort.Contains("ammo.Clear("),"auto refill does not revive empty slots or replace metadata");
Check(comfort.Contains("Profile=0;")&&comfort.Contains("before.Session!=_app.Session")&&comfort.Contains("_app.Player.SpeedMultiplier=before.Speed"),"profile restore validates its captured session and exact speed");
Check(comfort.Contains("controller.IsGrounded")&&comfort.Contains("IsClearPosition")&&comfort.Contains("TryMapGround"),"SOS captures grounded collision-checked positions only");
Check(comfort.Contains("_app.Map.RequestTeleport("),"SOS uses guarded native scene transit");
Check(ui.Contains("_app.Cinema.HideHud||_app.Map.NativeMapOpen")&&ui.Contains("case 7:ConveniencePage(body)"),"info HUD is suppressed for clean frame/maps and convenience page is present");
Check(smoke.Contains("_app.Audit?.BootSummary()??ReadExplicitSummary(save)")&&smoke.Contains("summary.Name!=target[0]||summary.SaveName!=target[1]"),"explicit test boot uses exact native save identity without arbitrary file selection");
Check(smoke.Contains("ToolkitUi.TabCount")&&!smoke.Contains("_stage=7;"),"smoke test tab count follows current menu");
Check(ui.Contains("DrawRoundedSurface(face,surface")&&
    ui.Contains("HoverText(face,content,ButtonTextStyle(content,face.width/UiThemePolicy.HoverScale(offset)),offset)")&&
    ui.Contains("return Pressed(rect);")&&!ui.Contains("GUI.Button("),
    "jelly buttons preserve the same native LMB stream as sliders");
Check(actors.Contains("if(!entry.CanSpawn)")&&actors.Contains("!registered.Contains(type.Pointer)"),"unregistered/incomplete native records remain visible but cannot mutate the save");
Check(atlas.Contains("_clickDragged|=")&&atlas.Contains("CompleteMapClick")&&atlas.Contains("OpenMap();else"),"map click commits only a completed non-drag gesture and opens through native display root");
Check(atlas.Contains("TryScreenWorld(screen,out Vector3 position)")&&atlas.Contains("ScreenPointToLocalPointInRectangle")&&atlas.Contains("determinant"),"map click inverts and validates native projection instead of accepting snapped marker coordinates");
Check(unlocks.Contains("zones.Count==0||events.Count==0")&&unlocks.Contains("registeredZones[i]?.Zone")&&unlocks.Contains("_mapReadyAfter"),"map reveal waits for real zone/fog assets before completing");
Check(smoke.Contains("_preparingMovement")&&smoke.Contains("BeginWalking();")&&smoke.Contains("waiting for native gameplay after final tab"),"walking probe waits for native pause/input transition after last tab");
Check(unlocks.Contains("var ui = _app.Map.Ui;")&&unlocks.Contains("_app.Map.NativeMapOpen"),"map reveal uses the initialized live map display rather than a pooled component");
Check(atlas.Contains("if(group==null||!group.IsGameplay)")&&!atlas.Contains("group=entry.SceneGroups[0]"),"native map destination is not replaced by an arbitrary zone group");
Check(atlas.Contains("LastTransitSucceeded=false;")&&atlas.Contains("LastTransitSucceeded=true;"),"transit success cannot be inferred from an older SOS return point");
Check(ui.Contains("(rect.height - 52) / row"),"catalog row viewport reserves its footer hint");
Check(ui.Contains("MapOverlayLayout.Place(projected,map.MarkerSize)")&&ui.Contains("markerBoxes.Any(r=>r.Overlaps(label))"),"map zone labels avoid exact marker bounds");
string layerOrderSource=File.ReadAllText(Path.Combine(root,"src","creator","MapLayerOrder.cs"));
Check(layerOrderSource.Contains("internal const int Objects = 7;")&&
    ui.Contains("var layers=MapLayerOrder.Sorted(MapTools.Categories)")&&
    ui.Contains("group==0?\"ОБЪЕКТЫ\":\"НАХОДКИ И СПАВНЫ\"")&&
    ui.Contains("string caption=MapLayerOrder.Short(category)"),
    "atlas layer panel groups objects before finds and keeps every caption inside the two-column grid");
Check(ui.Contains("Slider(4,new Rect(16,134,292,28),map.MarkerSize")&&
    ui.Contains("MapOverlayLayout.MinSize,MapOverlayLayout.MaxSize")&&ui.Contains("map.PanelExpanded?MapLayerOrder.PanelHeight(layers.Length):56"),
    "atlas exposes one player-facing icon-size slider inside a panel tall enough for every control");
string fandomSource=File.ReadAllText(Path.Combine(root,"src","creator","FandomMapMarkers.cs"));
Check(fandomSource.Contains("GatherCategory=\"Ресурсы: ценные\";")&&fandomSource.Contains("RareCategory=\"Редкое\";")&&
    layerOrderSource.Contains("FandomGather = \"Ресурсы: ценные\"")&&layerOrderSource.Contains("FandomRare = \"Редкое\""),
    "atlas layer order mirrors the fandom category names instead of drifting from them");
Check(File.ReadAllText(Path.Combine(root,"src","creator","MapTools.cs")).Contains("Categories={\"Капсулы\",\"Гордо\",\"Телепорты\",\"Ноды карты\",\"Дроны\",\"Ресурсы\",\"Двери\",\"Механизмы\",\"Горшочки\",\"Спавнеры\",FandomMapMarkers.GatherCategory,FandomMapMarkers.RareCategory,MapResourceLayerPolicy.Food,MapResourceLayerPolicy.Plorts}"),
    "offline layer fixtures mirror the fourteen production map categories");
Check(ui.Contains("markerExclusion.Contains(p)")&&ui.Contains("markers.FindIndex(m=>m.Item==map.Selected)"),"atlas excludes panel margins and draws the selected object above stacked markers");
Check(ui.Contains("nativeMarkerExclusions.Any(r=>r.Contains(p))")&&ui.Contains("nativeChrome.Any(r=>r.Overlaps(label))"),"atlas marker and label layout reserves native zone titles, counters and zoom controls");
Check(atlas.Contains("NativeChromeRects.Any(r=>r.Contains(p))")&&!atlas.Contains("rects.AddRange(NativeChromeRects)"),"native map controls exclude teleport gestures without raycast shields that would block their clicks");
string stress=File.ReadAllText(Path.Combine(root,"src","creator","StressSuite.cs"));
Check(toolkit.Contains("_readyTicks=new (string,Action)[]")&&!toolkit.Contains("SafeTick(\"player\", Player.Tick)"),"per-frame native tick delegates are cached once");
Check(player.Contains("ctx.Player.Pointer!=_playerPointer")&&player.Contains("_playerPointer=IntPtr.Zero;_overlaps=null"),"player controller cache invalidates and releases overlap roots");
Check(comfort.Contains("_nextZoneText=Time.unscaledTime+5f")&&comfort.Contains("if(flags==_hudFlags&&speed==_hudSpeed)return"),"HUD localization and effect text are invalidation cached");
Check(ui.Contains("Event.current.type!=EventType.Repaint")&&ui.Contains("tools.HudShortZone"),"HUD draws only on repaint and uses pre-trimmed zone text");
Check(ui.Contains("internal void Dispose()")&&ui.Contains("UnityEngine.Object.Destroy(texture)")&&ui.Contains("_ownsFont"),"owned UI textures/font have explicit cleanup without destroying skin font");
Check(ui.Contains("internal void ResetSession()")&&toolkit.Contains("_ui?.ResetSession()"),"per-world UI selections and dynamic caches are released");
Check(atlas.Contains("_uiCandidates")&&atlas.Contains("TopStackableUI")&&atlas.Contains("_discoverUi=true"),"atlas display discovery uses cached candidates and native UI-stack invalidation");
Check(atlas.Contains("_pendingFocus=null;_origin=null;_destinationGroup=null"),"atlas reset releases old focus and transit roots");
Check(world.Contains("_retiredFrozen")&&world.Contains("_frozen.Remove(key)")&&world.Contains("snapshot.Disabled.Clear();snapshot.Body=null"),"streamed frozen actors are pruned and their native roots released");
Check(toolkit.Contains("session==IntPtr.Zero&&Session!=IntPtr.Zero&&SystemContext.Instance?.SceneLoader?.IsSceneLoadInProgress==true"),"transient streamed-context absence does not destroy an active safe transit");
Check(stress.Contains("_app.Audit.GuardStressCopy()")&&suite.Contains("Stress copy ownership missing."),"stress mutations and reloads require verified owned save isolation");
Check(stress.Contains("_frames.Count<60000")&&stress.Contains("_oldContexts.Clear()"),"stress telemetry storage is bounded and releases old-world wrappers");
Check(stress.Contains("StateEvent.From(keyboard,out InputEventPtr ptr")&&stress.Contains("state.Dispose()")&&!stress.Contains("keybd_event"),"stress uses correctly sized disposable native keyboard events only");
Check(stress.Contains("_burst<2000")&&stress.Contains("_routeLeg<9")&&stress.Contains("PlannedReloads=9")&&stress.Contains("_reloads<PlannedReloads"),"stress workload is finite and explicitly bounded");
Check(stress.Contains("LoadStressCopyFromMenu()")&&suite.Contains("LoadMainMenuSceneGroup()")&&suite.Contains("Save.BeginLoad(summary.SaveIdentifier)"),"stress exercises native same-process unload and validated reload");
Check(stress.Contains("Measuring=false;Move(0)")&&stress.Contains("AfterExplicitTestOnlyGc=true"),"test collections are outside FPS windows and movement is released");
Check(stress.Contains("Process.GetCurrentProcess()")&&stress.Contains("Profiler.GetTotalAllocatedMemoryLong()")&&stress.Contains("GC.GetTotalMemory(false)"),"stress distinguishes process, Unity native and retained managed memory");
Check(suite.Contains("_steps.RemoveRange(0,_index);_index=0;")&&suite.Contains("SerializeModifiedCopy);"),"completed test closures cannot retain old-world objects during the memory test");
Check(ui.Contains("texture.name=\"CreatorToolkit_UI_IconAtlas\"")&&stress.Contains("texture.name.StartsWith(\"CreatorToolkit_UI_\""),
    "native owned-texture census includes the single atlas as well as all UI backgrounds");
Check(ui.Contains("private bool InputEnabled=>GUI.enabled&&!_bodyInteractionBlocked")&&ui.Contains("bool focused=InputEnabled")&&
    ui.Contains("if (InputEnabled && PointerWithinGroups() && e != null && e.type == EventType.ScrollWheel")&&ui.Contains("if (!InputEnabled || mouse == null"),
    "audit palette stays readable while pointer, text editing, slider and scroll interaction remain locked");
Check(suite.Contains("CapturePersistenceState();")&&suite.Contains("_recovery.TestSave=written.SaveName;")&&
    suite.Contains("foreach(var expected in _savedAmmoSlots)")&&suite.Contains("actualCount==expected.Count&&actualRef==expected.Reference"),
    "persistence validates all exact native slot contents against the exact owned checkpoint written before fresh-process reload");
Check(player.Contains("Math.Min(hits,_overlaps.Length);i++)_overlaps[i]=null"),"reused native overlap buffer releases collider roots after every query");
Check(stress.Contains("Position=target.InitialPlayerPosition,ExactHeight=false")&&stress.Contains("ReleaseTestRoots();_app.Fail")&&stress.Contains("if(!Active){ReleaseTestRoots();return;}"),"stress spawn transforms use guarded map grounding and all exit paths release test roots");
Check(toolkit.Contains("bool stableMenu=")&&toolkit.Contains("if(!stableMenu && ctx != null"),"settled main-menu unload releases a stale native SceneContext binding");
Check(atlas.Contains("loader.LoadSceneGroup(group,parameters)")&&atlas.Contains("TeleportPlayer=true,PauseGame=true")&&atlas.Contains("loader.CurrentSceneGroup!=group")&&atlas.Contains("loader.IsAssetLoaded(scenes[i])"),"cross-region transit waits for native destination scene assets, not model membership alone");
Check(atlas.Contains("_app.Player.BeginTransitHold();")&&atlas.Contains("_collisionGate.Observe(ready,now")&&
    atlas.Contains("TransitSafety.PlacementSettleSeconds"),"native teleport holds physics before loading and gates release on warmup plus post-placement collision");
Check(player.Contains("_holdMotor.enabled = false;")&&player.Contains("_holdGravity")&&player.Contains("finally { _holdMotor = null; _holdController = null; }"),
    "transit hold snapshots and restores native gravity/motor state, then releases native roots");
Check(actors.Contains("runtime?.Items")&&actors.Contains("g.IsMember(type)")&&actors.Contains("entries.Sort((a,b)=>a.Order.CompareTo(b.Order))"),
    "catalog uses native encyclopedia sequence and native dietary membership, not translated alphabetical heuristics");
Check(ui.Contains("OverlayCardRects.Add")&&atlas.Contains("foreach(var rect in OverlayCardRects)sources.Add")&&ui.Contains("CapsuleCard(capsuleBounds,capsule,false)"),
    "detailed click/hover capsule card is raycast shielded against map teleport click-through");
Check(File.ReadAllText(Path.Combine(root,"src","creator","CapsuleTools.cs")).Contains("TableReference.GuidFromString(key.Table)"),
    "native localization parses serialized GUID table references with the actual GUID API");
string customCode=File.ReadAllText(Path.Combine(root,"src","creator","CustomWaypointTools.cs"));
string radarCode=File.ReadAllText(Path.Combine(root,"src","creator","RadarTools.cs"));
string farmCode=File.ReadAllText(Path.Combine(root,"src","creator","FarmTools.cs"));
string explorerUi=File.ReadAllText(Path.Combine(root,"src","creator","ExplorerUi.cs"));
Check(customCode.Contains("File.Move(temporary,_file,true)")&&customCode.Contains("waypoints-")&&!customCode.Contains(".sav"),
    "named custom markers use atomic versioned settings files, never rewrite native saves");
Check(customCode.Contains("_writable=false")&&customCode.Contains("WaypointStore.Read")&&customCode.Contains("if(_camera==null||!record.ExactHeight)return;"),
    "invalid marker files fail closed and X/Z-only clicks cannot fabricate a ground-height beacon");
Check(atlas.Contains("CustomPointMode")&&atlas.Contains("_app.Custom.Add(point,name,color)")&&explorerUi.Contains("_app.Map.RequestTeleport(chosen)"),
    "native custom clicks retain pointer guards and custom teleports use the existing safe scene/ground pipeline");
Check(radarCode.Contains("FavoriteIdents")&&radarCode.Contains("RegionMember.IsInZone(zone)")&&radarCode.Contains("MaxRadarTargets")&&radarCode.Contains("if(!Enabled)return;"),
    "radar resolves native diets, filters current-zone membership and does no disabled polling");
Check(!radarCode.Contains("MaterialPropertyBlock")&&!radarCode.Contains("Renderer.material")&&radarCode.Contains("_camera=null"),
    "directional radar does not duplicate renderer materials and releases its cached camera on reset");
Check(farmCode.Contains("_app.Confirm(")&&farmCode.Contains("collector.StartCollection()")&&!farmCode.Contains("DestroyActor")&&!farmCode.Contains("InstantiateActorModel"),
    "farm automation uses confirmed native devices, not fake loot or destructive actor vacuuming");
Check(farmCode.Contains("CanAnySlotAccept(slot.Metadata)")&&farmCode.Contains("source.PopFromSelectedSlot()")&&farmCode.Contains("MaybeAddToAnySlot(popped)")&&farmCode.Contains("ExplorerPolicy.Conserved"),
    "native feeder transfer gates real capacity and verifies item conservation");
Check(farmCode.Contains("foreach(var snapshot in snapshots)snapshot.Restore()")&&farmCode.Contains("sourceSnapshot.Restore()")&&farmCode.Contains("if(!Feed||"),
    "failed feeder transfers restore both native inventories and disabled feeding cannot mutate a slot");
Check(!radarCode.Contains("FindObjectsOfType")&&!radarCode.Contains("AllActors(")&&!radarCode.Contains("FillActorsList(")&&
    radarCode.Contains("GameModel.identifiablesByIdent")&&radarCode.Contains("index.TryGetValue(query.Type,out var models)"),
    "radar discovery uses the authoritative per-definition native index without global object arrays or copied actor registries");
Check(radarCode.Contains("actor.identType!=query.Type")&&radarCode.Contains("!go.activeInHierarchy")&&
    radarCode.Contains("_queries.Clear()")&&radarCode.Contains("Scans=ExaminedModelCount=0"),
    "indexed radar still verifies actual loaded actor identity and releases definition queries on reset");
Check(!farmCode.Contains("FindObjectsOfType")&&farmCode.Contains("GameModel.landPlots")&&
    farmCode.Contains("model.typeId!=LandPlot.Id.CORRAL")&&farmCode.Contains("model.gameObj")&&
    farmCode.Contains("root.GetComponentInChildren<PlortCollector>(true)")&&farmCode.Contains("root.GetComponentInChildren<SlimeFeeder>(true)"),
    "farm discovery reads native loaded corral models rather than scanning all Unity objects");
Check(toolkit.Contains("(\"zone radar\",Radar.Tick)")&&toolkit.Contains("(\"corral assistant\",Farm.Tick)")&&toolkit.Contains("restore custom waypoints"),
    "new modules have cached tick delegates and session-bound reset hooks");
Check(explorerUi.Contains("ExplorerSections")&&explorerUi.Contains("DrawExplorerHud")&&ui.Contains("CustomMapCard(capsuleBounds,custom)"),
    "new native panels/compass/map popovers reuse the existing pastel UI rather than adding a bridge");
string explorerTests=File.ReadAllText(Path.Combine(root,"src","creator","ExplorerTests.cs"));
Check(explorerTests.Contains("Radar / indexed native definitions and loaded actor discovery")&&
    explorerTests.Contains("Farm / indexed loaded native corral device discovery")&&explorerTests.Contains("_app.Radar.QueryDefinitionCount==0"),
    "native fixture tests cover indexed discovery and zero query/device caches after disabling the features");
string atlasProbe=File.ReadAllText(Path.Combine(root,"src","creator","NativeAtlasProbe.cs"));
Check(explorerUi.Contains("_radarSelection=slimes.FirstOrDefault(e=>e.Type.ReferenceId==tools.SlimeReference)"),
    "opening radar settings preserves the actual native selected slime");
Check(ui.Contains("map.DisplayName(point)")&&ui.Contains("map.DisplayName(map.Selected)")&&
    atlas.Contains("return view.Baked?view.RewardSummary:view.Title;")&&
    File.ReadAllText(Path.Combine(root,"src","creator","CapsuleTools.cs")).Contains("view.Confirmed=CapsuleTruthPolicy.Confirmed(view.Verification);"),
    "capsule map/sidebar labels use localized version-gated rewards without falsely claiming native verification");
Check(suite.Contains("candidate.AwaitReady")&&suite.Contains("_stepWaitUntil=Time.unscaledTime+candidate.WaitSeconds")&&
    explorerTests.Contains("map closes after native card capture")&&atlasProbe.Contains("if(_stage==10)Finish(true)"),
    "native UI readiness is deadline-bounded and screenshot frames precede display cleanup");
Check(explorerTests.Contains("Position=group.InitialPlayerPosition,ExactHeight=false")&&
    explorerTests.Contains("_app.Map.LastSceneAssetsReady")&&explorerTests.Contains("GuardCopy();Require"),
    "farm fixture loads the actual native ranch sector on an owned copy before purchasing a corral");
Check(explorerTests.Contains("CollectorFixturePosition(area)")&&explorerTests.Contains("bounds.max.y-.02f")&&
    explorerTests.Contains("Physics.RaycastAll(origin,Vector3.down,bounds.size.y+.6f")&&
    explorerTests.Contains("area.bounds.Contains(spawn)")&&!explorerTests.Contains("TryMapGround(area.bounds.center,false"),
    "collector fixture uses a bounded in-trigger native ground ray rather than a guessed height or distant roof");
Check(explorerUi.Contains("y=ExplorerPolicy.NavigationHudTop")&&explorerUi.Contains("y+=ExplorerPolicy.NavigationHudStride"),
    "native directional HUD uses the tested nonoverlapping vertical layout");
Check(explorerUi.Contains("rect.width-32,104)")&&explorerUi.Contains("Телепорт — через безопасную прогрузку зоны.\",_atlasDetail"),
    "custom marker details use top-aligned readable map text with enough wrapped height");
Check(ui.Contains("if(hover.Length>0)")&&!ui.Contains("hoveredCapsule==null")&&
    ui.Contains("!capsuleBounds.Overlaps(left)?left:!capsuleBounds.Overlaps(above)?above:below"),
    "generic map hover tooltips are never suppressed and move clear of a selected capsule or custom-marker card");
Check(explorerTests.Contains("ExplorerSectionVisibleForTest(3)")&&explorerTests.Contains("Farm proof requires its actual visible settings panel."),
    "native farm screenshot test verifies a visible panel instead of passing a world-only capture");
Check(atlas.Contains("if(!_clickDragged&&_clickPoint==null)ResolveMapClick(screen,null)")&&atlas.Contains("else if(candidates.Count==1)"),"valid native map gestures have zone-mapped fallback without guessing among multiple regions");
Check(stress.Contains("StopRefill(\"enter forced burst\");SetAmmoCount(100);ArmRefill()")&&stress.Contains("destination scenes actually loaded after transit"),"stress avoids a pending regular refill race and verifies real loaded scenes");
Check(hooks.Contains("!app.World.FreezeMobs || !app.Ready")&&hooks.Contains("!ToolkitMod.I.World.FreezeTime || !ToolkitMod.I.Ready"),"disabled native per-actor/time hooks short-circuit before native readiness queries");
Check(hooks.Contains("ToolkitMod.I.Player.GodMode && ToolkitMod.I.Ready")&&hooks.Contains("ToolkitMod.I.Player.InfiniteEnergy && ToolkitMod.I.Ready"),"disabled protection hooks avoid unnecessary native session queries");
Check(player.Contains("ring<=2")&&player.Contains("direction<8")&&player.Contains("SafetyPolicy.NearbyReturnOffset")&&player.Contains("TryMapGround(nearby,true,out landing)"),"nearby saved landing is bounded and retains native ground/clearance checks");
Check(atlas.Contains("_allowNearbyLanding&&_exactHeight?")&&File.ReadAllText(Path.Combine(root,"src","creator","CinemaTools.cs")).Contains("AllowNearbyLanding=true")&&comfort.Contains("AllowNearbyLanding=true"),"only saved positions opt into nearby landing; regular map targets keep exact rejection");
Check(stress.Contains("VerifyOccupiedSavedPoint();")&&stress.Contains("blocker.SetActive(false);UnityEngine.Object.Destroy(blocker)")&&stress.Contains("Physics.SyncTransforms()"),"occupied saved-position regression uses native collisions and releases its nonserialized fixture");
Check(world.Contains("registry.RemoveWeatherPatternInstance(WeatherZone,definition)")&&suite.Contains("Weather / no orphaned cleared pattern"),"clear weather removes exited instances before native region register/unregister");
Check(world.Contains("_resolvedPrimaryZone==current.Pointer")&&world.Contains("_resolvedWeatherRegistry==registry.Pointer")&&world.Contains("_resolvedWeatherZone=null"),"weather-zone discovery caches by native zone/registry and releases on world reset");
Check(atlas.Contains("if(_discoverUi&&Time.unscaledTime>=_nextUiDiscovery)")&&!atlas.Contains("(_discoverUi||_uiCandidates.Count==0)")&&atlas.Contains("_uiDiscoveryUntil=Time.unscaledTime+3f"),"atlas discovery is bounded to UI invalidation rather than empty-cache closed gameplay polling");
Check(stress.Contains("_sections.Count>=32")&&stress.Contains("_stalls.Count<128")&&toolkit.Contains("Stress.RecordSection(\"diagnostics\""),"opt-in stall profiler attributes sections with bounded storage");
Check(stress.Contains("TryNearbyMapGround(_origin,out reset)")&&stress.Contains("ResetOffsetMeters=_resetOffset")&&stress.Contains("if(!_app.Player.IsClearPosition(reset))"),"occupied FPS reset uses bounded production clearance helper and records trajectory offset");
string diagnosticIo=File.ReadAllText(Path.Combine(root,"src","creator","DiagnosticIo.cs"));
Check(diagnosticIo.Contains("_pendingStatus=snapshot;")&&diagnosticIo.Contains("if(_working)return;")&&diagnosticIo.Contains("_commands.Count+lines.Length>64"),"diagnostic worker coalesces one latest snapshot and bounds command storage");
Check(!toolkit.Contains("\"status.txt\"")&&toolkit.Contains("_diagnosticIo.TakeCommands()")&&toolkit.Contains("_diagnosticIo.QueueSnapshot(snapshot)"),"game-thread diagnostic path no longer performs status/command filesystem I/O");
Check(diagnosticIo.Contains("File.Move(_temporaryPath,_statusPath,true)")&&diagnosticIo.Contains("worker?.Wait(1000)")&&!diagnosticIo.Contains("UnityEngine")&&!diagnosticIo.Contains("Il2Cpp"),"diagnostic worker atomically publishes status, drains on quit, and has no native gameplay access");
Check(suite.Contains("mouse.leftButton.isPressed||mouse.rightButton.isPressed")&&suite.Contains("_next=Time.unscaledTime+.25f;return;")&&suite.Contains("Ввод пользователя не переопределяется"),"native pointer probes wait for released user input without overriding held buttons");
string smokeTick=smoke.Substring(smoke.IndexOf("internal void Tick()",StringComparison.Ordinal),
    smoke.IndexOf("private Il2CppMonomiPark.SlimeRancher.Persist.Summary ReadExplicitSummary",StringComparison.Ordinal)-smoke.IndexOf("internal void Tick()",StringComparison.Ordinal));
Check(smokeTick.Contains("_app.SelftestFileRequest")&&!smokeTick.Contains("File.Exists(Marker)")&&!smokeTick.Contains("File.ReadAllText(Marker)"),
    "inactive self-test tick uses a cached request rather than game-thread marker discovery");
Check(diagnosticIo.Contains("generation==_selftestGeneration")&&diagnosticIo.Contains("_selftestGeneration++;_selftestOption=null;")&&diagnosticIo.Contains("Length>4096"),
    "self-test request cache is bounded and invalidates stale in-flight discoveries");
Check(diagnosticIo.Contains("catch(UnauthorizedAccessException) when(attempt<5)")&&diagnosticIo.Contains("catch(IOException) when(attempt<5)"),
    "bounded worker sharing retries handle both Windows rename error categories without changing permissions");
Check(smoke.Split("_app.ForgetSelftestFileRequest();",StringSplitOptions.None).Length==3&&smoke.Contains("if(_app.SelftestFileRequest==null) RestoreBackground();"),
    "explicit begin/cancel acknowledgements clear cached markers while session cancellation preserves pending boot requests");
Check(stress.Contains("il2cppGrowth>8*1024*1024")&&stress.Contains("owned-object bounds alone cannot distinguish engine caches"),
    "repeated-reload report does not hide IL2CPP growth behind stable module-owned object counts");
Check(smoke.Contains("try{RestoreState();}")&&smoke.Contains("finally{_controller=null;_session=IntPtr.Zero;_probeKeyboard=null;}")&&smoke.Contains("!ReferenceEquals(_controller,null)"),
    "completed smoke tests release controller/keyboard/session roots on every restore exit");
string memoryBody=stress.Substring(stress.IndexOf("private void Memory(",StringComparison.Ordinal),
    stress.IndexOf("private void BeginRoute(",StringComparison.Ordinal)-stress.IndexOf("private void Memory(",StringComparison.Ordinal));
Check(memoryBody.Contains("_app.Audit.GuardStressCopy();")&&memoryBody.Contains("Require(!Measuring,"),
    "all explicit collector/mode changes require an owned copy and are outside FPS windows");
Check(memoryBody.Contains("SetMode(originalMode)")&&memoryBody.Contains("Require(modeRestored,")&&memoryBody.Contains("Il2CppBeforeCollectionBytes=beforeCollection"),
    "native GC snapshots record pre-collection bytes and verify original mode restoration");
Check(stress.Contains("+\"/\"+PlannedReloads")&&!stress.Contains("+\"/3\""),
    "reload progress denominator follows the actual finite plan");
Check(!stress.Contains("GarbageCollector.GCMode=")&&!toolkit.Contains("GarbageCollector.SetMode(")&&!comfort.Contains("GC.Collect("),
    "stripped GC property setter is avoided and production HUD/refill/diagnostics never force native collection or change its mode");
string engine=File.ReadAllText(Path.Combine(root,"src","creator","EngineFrameProbe.cs"));
Check(engine.Contains("GetByName__Unmanaged(ProfilerCategory.Any,p,utf8.Length)")&&!engine.Contains("Profiler.enabled="),
    "opt-in engine recorders use verified UTF-8 native lookup without enabling the global profiler");
Check(engine.Contains("c.Recorder.Dispose()")&&stress.Contains("_engine?.Dispose();_engine=null;")&&stress.Contains("_engineHitches.Count<128"),
    "native telemetry has bounded hitch storage and releases recorder handles before lifecycle comparisons");
Check(suite.Contains("BeginStressMenuReload(bool saveCheckpoint=true,bool armOptimizationForUnload=false)")&&
    suite.Contains("if(saveCheckpoint)")&&stress.Contains("BeginStressMenuReload(saveCheckpoint:false)"),
    "control reloads reuse the owned native checkpoint without explicit save churn");
Check(stress.Contains("StressCheckpointFingerprint()==_comparisonCheckpointHash")&&suite.Contains("return Hash(Path.Combine(storage.savePath,leaf+\".sav\"))"),
    "control reloads verify immutable owned-checkpoint bytes before and after every native load");
Check(ui.Contains("_atlasHelp.wordWrap=false")&&ui.Contains("_atlasDetail.fontSize=16")&&ui.Contains("Море и препятствия — отказ."),
    "atlas safety help uses two short unwrapped lines instead of a clipped three-line paragraph");
Check(UiThemePolicy.TextureBudget==16,"native parchment jelly theme has an exact sixteen-texture shared budget");
double PaletteLuminance(uint rgb,double shade=1)
{
    double Linear(double c)=>c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4);
    return .2126*Linear(((rgb>>16)&255)/255d*shade)+
        .7152*Linear(((rgb>>8)&255)/255d*shade)+.0722*Linear((rgb&255)/255d*shade);
}
foreach(uint rgb in new uint[]{0x00D4BD,0xFF6B7A,0xFFC83B,0xC2A9EB,0xF5EEDB})
    Check((PaletteLuminance(rgb,UiThemePolicy.JellyShadeFloor(rgb))+.05)/
        (PaletteLuminance(UiThemePolicy.Ink)+.05)>=4.5,
        "dark native ink keeps AA contrast even at the jelly face shade floor "+rgb.ToString("X6"));
Check(UiThemePolicy.ButtonDepth(false,false)==4,"jelly resting shadow is four pixels");
Check(UiThemePolicy.ButtonDepth(true,false)==3,"hover compresses the jelly shadow to three pixels");
Check(UiThemePolicy.ButtonDepth(true,true)==1,"held jelly shadow compresses to one pixel");
Check(UiThemePolicy.ButtonDepth(false,true)==4,"unrelated held mouse never depresses another control");
Check(UiThemePolicy.FaceOffset(false,false)==0&&UiThemePolicy.FaceOffset(true,false)==-4&&UiThemePolicy.FaceOffset(true,true)==3,
    "LO hover lifts four pixels while a held face preserves its press depth");
var hoverMotion=new UiThemePolicy.HoverAnimation();
UiThemePolicy.StepHover(ref hoverMotion,-4,.060f);
Check(Math.Abs(hoverMotion.Position+2)<.0001f,"hover smoothstep midpoint is exactly half lift at 60 ms");
UiThemePolicy.StepHover(ref hoverMotion,-4,.060f);
Check(hoverMotion.Position==-4&&Math.Abs(UiThemePolicy.HoverScale(hoverMotion.Position)-1.05f)<.00001f,
    "hover settles at minus four pixels and scale 1.05 by 120 ms");
UiThemePolicy.StepHover(ref hoverMotion,0,.060f);
Check(Math.Abs(hoverMotion.Position+2)<.0001f,"pointer exit returns through smoothstep midpoint without overshoot");
UiThemePolicy.StepHover(ref hoverMotion,0,.060f);
Check(hoverMotion.Position==0&&UiThemePolicy.HoverScale(hoverMotion.Position)==1,
    "pointer exit restores exact rest position and scale at 120 ms");
UiThemePolicy.StepHover(ref hoverMotion,-4,.030f);
float reversing=hoverMotion.Position;
UiThemePolicy.StepHover(ref hoverMotion,0,0);
Check(hoverMotion.Position==reversing,"hover reversal starts continuously from the current visual position");
UiThemePolicy.StepHover(ref hoverMotion,0,.12f);
Check(hoverMotion.Position==0,"reversed hover also settles within 120 ms");
UiThemePolicy.StepHover(ref hoverMotion,-4,float.NaN);
Check(hoverMotion.Position==0,"invalid frame delta cannot inject NaN into hover motion");
UiThemePolicy.StepHover(ref hoverMotion,-4,10);
Check(hoverMotion.Position==-4,"a delayed unscaled frame completes, rather than extends, the 120 ms tween");
UiThemePolicy.StepHover(ref hoverMotion,float.NaN,.12f);
Check(hoverMotion.Position==0&&UiThemePolicy.HoverScale(float.NaN)==1,"nonfinite targets and scale fail safely to rest");
foreach(float dt in new[]{1f/30,1f/60,1f/144})
{
    var h=new UiThemePolicy.HoverAnimation();bool bounded=true;
    for(int i=0;i<150;i++){UiThemePolicy.StepHover(ref h,-4,dt);bounded&=float.IsFinite(h.Position)&&h.Position>=-4&&h.Position<=0;}
    Check(bounded&&h.Position==-4,"hover is bounded and frame-rate independent at delta "+dt);
}
Check(UiThemePolicy.Heading==0x2C3E50&&UiThemePolicy.Amber==0xFFAA33&&UiThemePolicy.Body==0x4A4540,
    "LO heading, amber and body palette tokens match exactly");
foreach(uint surface in new[]{UiThemePolicy.Canvas,UiThemePolicy.Card})
{
    Check((PaletteLuminance(surface)+.05)/(PaletteLuminance(UiThemePolicy.Body)+.05)>=4.5,
        "Cyrillic body text has AA token contrast on surface "+surface.ToString("X6"));
    Check((PaletteLuminance(surface)+.05)/(PaletteLuminance(UiThemePolicy.Heading)+.05)>=4.5,
        "Cyrillic headings have AA token contrast on surface "+surface.ToString("X6"));
}
Check(ui.Contains("HoverChildRect")&&ui.Contains("Time.unscaledTime-motion.Time")&&ui.Contains("return Pressed(rect);"),
    "hover scales native icon and card visuals with unscaled time while retaining original hitboxes");
Check(UiThemePolicy.SliderKnobCenter(10,200,0,30)==25,"slime slider start keeps its circular knob inside the rail");
Check(UiThemePolicy.SliderKnobCenter(10,200,1,30)==195,"slime slider end keeps its circular knob inside the rail");
Check(UiThemePolicy.SliderKnobCenter(10,200,.5f,30)==110,"slime slider middle preserves the value-to-pointer mapping");
Check(UiThemePolicy.SliderKnobCenter(10,10,1,30)==15,"small slider rail clamps rather than inverting its knob bounds");
Check(UiThemePolicy.SliderKnobCenter(10,0,1,30)==10,"zero-width slider geometry remains finite");
Check(UiThemePolicy.Coverage(31,31,64,20)==1,"rounded parchment interior is fully covered");
Check(UiThemePolicy.Coverage(0,0,64,20)==0,"rounded parchment corners remain antialiased");
Check(UiThemePolicy.Coverage(31,0,64,32)>0,"round slime knob has a covered top with antialiased edge");
Check(UiThemePolicy.CapsuleRadius(16,500,20)==8&&UiThemePolicy.CapsuleRadius(24,44,20)==12&&
    UiThemePolicy.CapsuleRadius(0,44,20)==0&&UiThemePolicy.CapsuleRadius(float.NaN,44,20)==0,
    "capsule nine-slice corners never overlap on a narrow native rail");
Check(Enumerable.Range(0,64).All(x=>Enumerable.Range(0,64).All(y=>
    UiThemePolicy.Coverage(x,y,64,32)>=0&&UiThemePolicy.Coverage(x,y,64,32)<=1)),
    "all procedural circle coverage values stay in the alpha range");
Check(UiThemePolicy.NativeFontRank("Segoe UI",true)<0,"system dashboard font is rejected by native font selection");
Check(UiThemePolicy.NativeFontRank("Arial",true)<0,"plain built-in Arial is not falsely called an authentic rounded game font");
Check(UiThemePolicy.NativeFontRank("CreatorToolkit fallback",true)<0,"native font census does not rediscover its own owned fallback");
Check(UiThemePolicy.NativeFontRank("LegacyRuntime Arial",true)<0,"Unity's service font cannot masquerade as a rounded game typeface");
Check(UiThemePolicy.NativeFontRank("OpenSans-Semibold",true)>UiThemePolicy.NativeFontRank("Arial Rounded MT Bold",true),"actual native Cyrillic face is preferred to an OS imitation");
Check(!UiThemePolicy.RoundedFace("OpenSans-Semibold"),"native provenance never falsely relabels OpenSans as Mikado or a rounded face");
Check(UiThemePolicy.NativeFontRank("Trebuchet MS",true)>=360&&UiThemePolicy.NativeFontRank("Comic Sans MS",true)>=360,
    "both explicit rounded Cyrillic fallback families meet the rounded-face policy");
Check(UiThemePolicy.NativeFontRank("Mikado Bold",true)>UiThemePolicy.NativeFontRank("Generic Game Font",true),
    "known native rounded family wins over generic loaded font");
Check(UiThemePolicy.NativeFontRank("Mikado Bold",true)>UiThemePolicy.NativeFontRank("Mikado Bold",false),
    "dynamic native font is preferred within the same rounded family");
Check(ui.Contains("Resources.FindObjectsOfTypeAll<Font>()")&&ui.Contains("FontHasCyrillic(candidate)")&&
    ui.Contains("ResolveGameFont();")&&!ui.Contains("CreateDynamicFontFromOSFont(\"Segoe UI\""),
    "UI resolves real native fonts before a Cyrillic-verified rounded OS fallback");
Check(ui.Contains("Arial Rounded MT Bold")&&ui.Contains("Trebuchet MS")&&ui.Contains("Comic Sans MS")&&
    ui.Contains("_label.fontStyle=_ownsFont?FontStyle.Bold:FontStyle.Normal"),
    "rounded fallback families use bold rendering and explicit font ownership");
Check(UiThemePolicy.Canvas==0xF8F4EA&&UiThemePolicy.Ink==0x2C2621&&UiThemePolicy.Card==0xFFFDF9&&
    ui.Contains("_parchment.a==1f")&&!ui.Contains("_glass ="),"requested opaque parchment/dark-ink/pastel palette replaces dark glass");
Check(ui.Contains("UiThemePolicy.FaceOffset(hovered,held)")&&ui.Contains("visualBounds??rect")&&
    ui.Contains("Button(rect,GUIContent.none,selected?_selectedButton:flavor,visual)"),"native-art bookmark protrusion and press animation do not move hit targets");
Check(ui.Contains("filterMode=FilterMode.Bilinear")&&ui.Contains("UiThemePolicy.Coverage")&&
    ui.Contains("_textures.Count!=ThemeTextureBudget"),"antialiased procedural surfaces are allocated once and checked against a fixed budget");
Check(ui.Contains("private bool SlimeSwitch")&&ui.Contains("GUI.DrawTexture(knob,_slimeKnob)")&&
    ui.Contains("GradientTitle(new Rect")&&ui.Contains("_badgeCircle"),"slime switches, circular glossy slider and gradient badge header are wired into live UI");
string uiProbe=File.ReadAllText(Path.Combine(root,"src","creator","NativeUiProbe.cs"));
Check(uiProbe.Contains("if(!NativeF1PediaFocused())")&&uiProbe.Contains("case 171:")&&
    uiProbe.Contains("Time.unscaledTime+15f")&&uiProbe.Contains("CloseDisplay(_nativeF1Name)"),
    "real F1 fixture observes the configured native display before closing it and waits for native gameplay before Insert");
Check(uiProbe.Contains("native-keyboard-window-proof.json")&&
    uiProbe.Contains("InsertPressed=keyboard?.insertKey.wasPressedThisFrame")&&
    uiProbe.Contains("NativeOwnership=NativeUiPriority.Diagnostics()")&&
    !uiProbe.Contains("Time.timeScale=")&&!uiProbe.Contains("Cursor.lockState="),
    "native hotkey lifecycle diagnostics retain actual key/focus state without forcing pause or cursor");
Check(suite.Contains("Theme / real font census and Cyrillic coverage")&&suite.Contains("Theme / exact opaque native parchment palette")&&
    suite.Contains("ThemeTexturesForTest")&&uiProbe.Contains("jelly-tab-pressed"),
    "native matrix checks font provenance/palette/resources and captures an actual held jelly bookmark");
Check(ui.Contains("candidate.fontNames")&&ui.Contains("FontRoundedForTest")&&suite.Contains("ui.FontNativeForTest"),
    "native census records actual font families and matrix rejects generic service-font substitution");
Check(ui.Contains("rect.width,22f")&&ui.Contains("const float diameter=32")&&ui.Contains("flavor=index switch"),
    "thick capsule rail, larger round slime knob and saturated candy bookmark flavors are live");
Check(OptimizationRequest.Potato.Clamped()==new OptimizationRequest(3,70,40,50,true),
    "potato preset is explicit and safe; it does not promise a specific VRAM capacity");
Check(OptimizationRequest.Full.Clamped()==OptimizationRequest.Full,"full-quality policy is stable");
Check(OptimizationPolicy.Mip(-1)==0&&OptimizationPolicy.Mip(99)==4,"texture limit clamped to 0 through 4");
Check(OptimizationPolicy.Render(0)==50&&OptimizationPolicy.Render(150)==100,"render stays within 50 through 100 percent");
Check(OptimizationPolicy.Density(-1)==10&&OptimizationPolicy.Density(500)==100,"decorative density never becomes a blanket zero");
Check(OptimizationPolicy.Rounded(float.NaN,75,50,100)==75&&
    OptimizationPolicy.Rounded(float.PositiveInfinity,75,50,100)==75,"sliders reject non-finite input without inventing a setting");
Check(OptimizationPolicy.SourceDimension(1024,4)==64&&OptimizationPolicy.SourceDimension(4096,4)==256,
    "mipmap 4 is one sixteenth, not universally 64 by 64");
Check(OptimizationPolicy.SourceDimension(0,2)==0&&OptimizationPolicy.SourceDimension(1,4)==1,
    "small or unavailable source dimensions remain honest");
for(int i=0;i<5;i++)Check(!string.IsNullOrWhiteSpace(OptimizationPolicy.MipLabel(i)),"mipmap label "+i);
foreach(int original in new[]{0,1,7,16,100,1000})
    foreach(int percent in new[]{10,40,50,100})
        Check(OptimizationPolicy.ParticleCap(original,percent)>=0&&OptimizationPolicy.ParticleCap(original,percent)<=original,
            "particle cap cannot raise native capacity "+original+"/"+percent);
Check(OptimizationPolicy.NativeBatch(0,8)==0&&OptimizationPolicy.NativeBatch(3,8)==3&&
    OptimizationPolicy.NativeBatch(1000,8)==8,"native batching does not invent a budget for sentinel values or raise a low original limit");
Check(OptimizationPolicy.AmbientParticle("AmbientDust",false,false)&&
    !OptimizationPolicy.AmbientParticle("SlimeDangerDust",false,false)&&
    !OptimizationPolicy.AmbientParticle("AmbientRain",true,false)&&
    !OptimizationPolicy.AmbientParticle("AmbientRain",false,true),"only collision-free, trigger-free named ambient particles are eligible");
Check(OptimizationPolicy.Foliage("grassMesh",true,true)&&
    !OptimizationPolicy.Foliage("harvestGrass",true,true)&&
    !OptimizationPolicy.Foliage("grassMesh",false,true)&&
    !OptimizationPolicy.Foliage("grassMesh",true,false),"foliage reduction is restricted to initialized instanced non-harvestable decoration");
Check(OptimizationPolicy.Owns(7,7)&&!OptimizationPolicy.Owns(8,7)&&
    !OptimizationPolicy.Owns(float.NaN,70f),"restore ownership never treats external or invalid values as the module's own setting");
var paint=new UiPaintTelemetry();
paint.Record(0,"Player",2,null,1);paint.Record(0,"Player",1,.8,2);
Check(paint.Rows.Count==2&&paint.Rows[0].FirstInSession&&!paint.Rows[1].FirstInSession&&paint.Rows[1].NativeClick,
    "tab CPU measurements distinguish first session repaint and native handled-click interval");
paint.Record(0,"Player",double.NaN,null,3);paint.Record(0,"Player",1,double.PositiveInfinity,3);
Check(paint.Rows.Count==2,"UI timing rejects non-finite measurements rather than emitting invalid telemetry");
for(int i=0;i<200;i++)paint.Record(0,"Player",1,null,i+4);
Check(paint.Rows.Count<=10,"early warm tab-body CPU samples remain bounded");
paint.Reset();Check(paint.Rows.Count==0&&paint.Wants(0,false),"UI telemetry resets without retaining session or native roots");
string optimization=File.ReadAllText(Path.Combine(root,"src","creator","OptimizationTools.cs"));
string optUi=string.Join("\n",new[]{File.ReadAllText(Path.Combine(root,"src","creator","OptimizationUi.cs")),File.ReadAllText(Path.Combine(root,"src","creator","M19GraphicsBasicsUi.cs")),File.ReadAllText(Path.Combine(root,"src","creator","M19GraphicsDetailsUi.cs"))});
string particleSafety=File.ReadAllText(Path.Combine(root,"src","creator","NativeParticleSafety.cs"));
Check(optimization.Contains("Snapshot();")&&optimization.Contains("internal void Reset()=>Restore(false)")&&
    toolkit.Contains("Optimization?.Reset(); // Before native save/profile persistence"),
    "graphics module captures originals and resets before native persistence, session changes and quit");
Check(optimization.Contains("VisitLoadedUiTextures(Consider)")&&optimization.Contains("if(!texture.isReadable)")&&
    !optimization.Contains("Resources.FindObjectsOfTypeAll<Sprite>()")&&
    optimization.Contains("mip=_mipBefore")&&optUi.Contains("tools.MipSupported"),
    "mip protection targets actual UI and refuses unreadable imports instead of touching splash art or bypassing Unity");
Check(optimization.Contains("if(_app.Audit.MainAutosaveHeld)"),
    "returned-main evidence protection refuses accidental graphics application until the protected process exits");
Check(optimization.Contains("WorldPopulator.ACTORS_PER_FRAME")&&optimization.Contains("WorldPopulator.NON_ACTORS_PER_FRAME")&&
    !optimization.Contains("_cachedAutoSimulation")&&!optimization.Contains("Physics.autoSimulation=")&&
    !optimization.Contains("fixedDeltaTime=")&&!optimization.Contains("Time.timeScale="),
    "smooth loading retains read-only model diagnostics without modifying physics or collider cooking");
Check(!optimization.Contains("Screen.SetResolution")&&!optimization.Contains("SetDynamicResScaler")&&
    !optimization.Contains("currentPlatformRenderPipelineSettings")&&!optimization.Contains("DynamicResolutionHandler")&&
    !optimization.Contains("allowDynamicResolution="),
    "crash quarantine removes boxed HDRP writes and dynamic camera/scaler mutation");
Check(particleSafety.Contains("if(!Supported")&&particleSafety.Contains("collision=trigger=true")&&
    optimization.Contains("NativeParticleSafety.TryRead")&&!optimization.Contains(".collision.enabled"),
    "stripped particle safety reads fail closed rather than assuming colliders and triggers are disabled");
Check(optimization.Contains("ParticlePercent=particles")&&optimization.Contains("ParticlesSupported?request.ParticlePercent:100")&&
    optUi.Contains("GUI.enabled=enabled&&tools.ParticlesSupported"),
    "unsupported native particle reduction is shown unavailable rather than reported as applied");
Check(optimization.Contains("stamp!=_sceneStamp")&&optimization.Contains("SceneManager.GetSceneAt(i).handle")&&
    optimization.Contains("if(!liveParticles.Contains(id))dead.Add(id)"),
    "streamed-scene swaps invalidate bounded particle snapshots even when the scene count stays equal");
Check(optUi.Contains("_dragSlider<0")&&optUi.Contains("EventType.Repaint")&&optUi.Contains("QueueOptimization(_optDraft)")&&
    !optUi.Contains("new Texture2D"),"expensive graphics settings apply once after slider release using existing shared surfaces");
Check(ui.Contains("_tableHeading.fontSize=20")&&ui.Contains("_tableHeading.wordWrap=false")&&
    File.ReadAllText(Path.Combine(root,"src","creator","CareUi.cs")).Contains("entry.Definition.Icon"),
    "care header gets a one-line cached style and gadget rows borrow original game art");
Check(CarePolicy.Within(625)&&!CarePolicy.Within(625.01f),"care radius includes exactly 25m and rejects beyond");
Check(!CarePolicy.Within(float.NaN)&&!CarePolicy.Within(float.PositiveInfinity)&&!CarePolicy.Within(-1),"care range rejects nonfinite and negative distances");
Check(!CarePolicy.Within(1,26)&&!CarePolicy.Within(1,0),"care radius never exceeds 25m");
Check(CarePolicy.Surface(1,0)&&CarePolicy.Surface(.9f,30)&&!CarePolicy.Surface(.5f,30),"gadget policy accepts ground and rejects steep normals");
Check(!CarePolicy.Surface(float.NaN,30)&&!CarePolicy.Surface(.9f,float.NaN)&&!CarePolicy.Surface(0,45),"gadget slope rejects unknown and vertical surfaces");
Check(CarePolicy.NativePlacementSurface(1,70)&&!CarePolicy.NativePlacementSurface(.907f,70)&&
    CarePolicy.NativePlacementSurface(.95f,70),"native 70-degree horizontal inclination allows up to 20-degree ground, not the refused 25-degree stairs");
Check(CarePolicy.NativePlacementSurface(1,90)&&!CarePolicy.NativePlacementSurface(.999f,90)&&
    !CarePolicy.NativePlacementSurface(.6f,0),"native inclination endpoints retain both the native sine threshold and extra terrain safety");
Check(!CarePolicy.NativePlacementSurface(1,float.NaN)&&!CarePolicy.NativePlacementSurface(1,91)&&
    !CarePolicy.NativePlacementSurface(float.NaN,70),"native placement inclination rejects unknown and out-of-contract metadata");
Check(CarePolicy.SpentOne(4,3)&&!CarePolicy.SpentOne(0,-1)&&!CarePolicy.SpentOne(4,4)&&!CarePolicy.SpentOne(4,2),"native one-unit conservation is exact");
Check(CarePolicy.MealBudget(-1)==0&&CarePolicy.MealBudget(3)==3&&CarePolicy.MealBudget(100)==8,"one-shot feeding is bounded to existing units and eight targets");
Check(PhenomenaWindowPolicy.ActiveHours(5.015885f,19.01316f,"OFF",out float onNight,out float offDay)&&
    Math.Abs(onNight-19.01316f)<.00001f&&Math.Abs(offDay-5.015885f)<.00001f,
    "complementary native OFF trigger window yields the actual night/solid ON window");
Check(PhenomenaWindowPolicy.ActiveHours(19,5,"ON",out float onDirect,out float offDirect)&&onDirect==19&&offDirect==5,
    "direct ON trigger window is not inverted twice");
Check(!PhenomenaWindowPolicy.ActiveHours(float.NaN,5,"ON",out _,out _)&&
    !PhenomenaWindowPolicy.ActiveHours(5,5,"OFF",out _,out _)&&
    !PhenomenaWindowPolicy.ActiveHours(5,19,"TRANSITIONING",out _,out _),"unknown native phase/window cannot authorize clock mutation");
Check(Math.Abs(PhenomenaWindowPolicy.After(23.99f)-.01f)<.00001f&&
    Math.Abs(PhenomenaWindowPolicy.After(19.01316f)-19.03316f)<.00001f,"native phase waiting wraps 24h while entering just after its actual ON edge");
string care=File.ReadAllText(Path.Combine(root,"src","creator","SlimeCareTools.cs"));
string gadgets=File.ReadAllText(Path.Combine(root,"src","creator","GadgetTools.cs"));
string careUi=File.ReadAllText(Path.Combine(root,"src","creator","CareUi.cs"));
string careTests=File.ReadAllText(Path.Combine(root,"src","creator","CareTests.cs"));
Check(care.Contains("identifiablesByIdent")&&!care.Contains("FindObjectsOfType"),"slime care scans native typed indices rather than the Unity object universe");
Check(care.Contains("ActorToken")&&care.Contains("_session != _app.Session")&&care.Contains("actor?.Pointer != hit.ActorToken"),"inspection tokens are resolved in the current session instead of dereferencing old native wrappers");
Check(care.Contains("meal.IsFavorite")&&care.Contains("meal.BecomesIdent == null")&&care.Contains("eater.WillNowEat(food)")&&care.Contains("eater.IsSleeping()"),"care honors native favorite diet, hunger, sleep, and transformation refusal");
Check(care.Contains("ammo.PopFromSelectedSlot()")&&care.Contains("CarePolicy.SpentOne")&&care.Contains("eater.MaybeSpinAndChomp(portion,false)")&&!care.Contains("_emotions.Set("),"favorite care consumes one real inventory unit and delegates chomp/emotions/plorts to native code");
Check(care.Contains("handoffAttempted = true")&&care.Contains("if (!handoffAttempted)")&&care.Contains("slot.SetFromMetadata(metadata)"),"only definitely pre-handoff failures refund food; ambiguous native mutations are not repeated");
Check(care.Contains("CarePolicy.MaxTargets")&&care.Contains("CarePolicy.MaxExamined")&&care.Contains("CarePolicy.FeedCooldown")&&care.Contains("Visible(eater)"),"care bounds retained snapshots, scan work, repeat rate and line of sight");
Check(gadgets.Contains("HasBlueprint")&&gadgets.Contains("director.GetItemCount(basis) <= 0")&&!gadgets.Contains("AddBlueprint(")&&!gadgets.Contains("SetCount("),"production gadget placement never unlocks or manufactures stock");
Check(gadgets.Contains("native.PlaceGadgetEvent(eventData)")&&!gadgets.Contains("InstantiateActorModel")&&gadgets.Contains("placed.GetModel()")&&gadgets.Contains("CarePolicy.SpentOne"),"one native gadget event registers its dedicated model and consumes one crafted unit");
Check(gadgets.Contains("IsCompletelyGrounded")&&gadgets.Contains("IsOverlapping")&&gadgets.Contains("IsInLinkedGadgetRange")&&gadgets.Contains("director.CanPlaceGadget")&&gadgets.Contains("Quaternion.LookRotation(facing.normalized, hit.normal)"),"native grounded/overlap/linked-range checks and surface normal gate placement");
Check(gadgets.Contains("collider.attachedRigidbody != null")&&gadgets.Contains("QueryTriggerInteraction.Ignore")&&gadgets.Contains("native.ClearHeldGadget()"),"gadget placement refuses moving bodies/triggers and retires its borrowed native preview");
Check(careUi.Contains("Накормить вокруг…")&&careUi.Contains("Разместить 1 экземпляр…")&&careUi.Contains("ResetCreationUi")&&ui.Contains("case 1:CreationPage(body)"),"new care and gadget subpanels preserve the eight primary tabs");
Check(world.Contains("RefreshActiveWeather(zone)")&&world.Contains("data.Director._runningStates")&&world.Contains("ActiveWeather.Clear()")&&world.Contains("\"lava\" =>")&&world.Contains("\"solar\" =>"),"weather panel reads live registry/director states and only registered lava/solar presets");
Check(explorerUi.Contains("rect.y+358,rect.width-32,56")&&explorerUi.Contains("Метка хранится отдельно\\nот игрового сейва."),"custom map footer has explicit two-line text and 56px reserved height");
Check(ui.Contains("ResetCreationUi();_headerSlime=null")&&suite.Contains("_steps.Clear();_steps.TrimExcess();_waypoints.Clear()")&&uiProbe.Contains("_stage=-1;_mouse=null"),"session/finished audit releases borrowed header sprite, matrix closures and native probe device roots");
Check(careTests.Contains("GuardCopy()")&&careTests.Contains("normal chomp reduces hunger")&&careTests.Contains("single native event creates registered GadgetModel")&&suite.Contains("BuildCareSteps()"),"new live native matrix fixtures are guarded by the isolated-copy harness");
Check(smoke.Contains("_app.Audit?.SkipMainNativeTests==true")&&smoke.Contains("_app.Audit?.GuardNativeProbeCopy()")&&
    suite.Contains("_app.Tests.CancelRequest(); // Returning main is loaded, never tested.")&&
    !suite.Contains("Main checkpoint save failed"),"all native UI/input probes run only on the isolated copy; bootstrap/return never tests or rewrites main sav");
Check(toolkit.Contains("_ui.InitializeReadyGui()")&&ui.Contains("internal void InitializeReadyGui()=>Styles()"),
    "ready-world GUI styles initialize normally without opening or testing the main-world menu");
Check(gadgets.Contains("owner._playerState!=_app.Context.PlayerState")&&gadgets.Contains("NativeOwner?.GadgetItem")&&
    gadgets.Contains("owner.ToggleItem(eventData)")&&gadgets.Contains("if(job.ModeChanged&&"),
    "gadget item resolves from its exact native PlayerItemController and temporary construction mode is restored");
Check(care.Contains("forward*reach")&&care.Contains("if (!CanOffer(eater,food))")&&careTests.Contains("Production full/sleeping eligibility"),
    "food is offered at the normal bite reach; fixture exercises shared production full/sleep eligibility");
string returnControl=File.ReadAllText(Path.Combine(root,"src","creator","AuditReturnControl.cs"));
Check(returnControl.Contains("VerifyMainHashes()")&&returnControl.Contains("audit-return-proof.json")&&
    returnControl.Contains("_holdUntil=UnityEngine.Time.unscaledTime+900f")&&toolkit.Contains("case \"auditrelease\"")&&
    returnControl.Contains("SettingRestored=false,ProtectionRemainsArmed=true")&&
    !returnControl.Contains("SetAutosaveDisabled(_heldAutosaveWasDisabled)"),
    "evidence acknowledgement never releases main saving; deadline requests a protected no-save exit");
string saveGuard=File.ReadAllText(Path.Combine(root,"src","creator","AuditSaveGuard.cs"));
string immutablePolicy=File.ReadAllText(Path.Combine(root,"src","creator","AuditSavePolicy.cs"));
string immutableGate=File.ReadAllText(Path.Combine(root,"src","creator","audit-integrity.ps1"));
string protectedRestart=File.ReadAllText(Path.Combine(root,"src","creator","restart.ps1"));
string protectedWatcher=File.ReadAllText(Path.Combine(root,"src","creator","audit-watch.ps1"));
Check(saveGuard.Contains("internal const int RequiredHookCount=13")&&saveGuard.Contains("installed==RequiredHookCount")&&
    toolkit.IndexOf("AuditSaveGuard.Install",StringComparison.Ordinal)<toolkit.IndexOf("new FeatureSuite(this)",StringComparison.Ordinal),
    "all thirteen exact writer/delete/quit hooks must be installed before audit recovery and opt-in boot");
Check(saveGuard.Contains("NativeAuditExecutionEnabled=>false")&&protectedRestart.Contains("$nativeAuditAdmissionReviewed=$false")&&
    protectedRestart.Contains("No install or launch."),
    "staged safety revision hard-blocks native audits until independent startup/write-denial review, not just a baseline choice");
Check(!saveGuard.Contains("Hook(director,\"SetAutosaveDisabled\"")&&saveGuard.Contains("regardless of that flag"),
    "audit protection does not detour a tiny native autosave flag setter or trust that flag as its write barrier");
Check(saveGuard.Contains("audit-immutable-baseline.sha256")&&saveGuard.Contains("no automatic replacement")&&
    immutablePolicy.Contains("Unexpected native save outside the immutable baseline"),
    "approved cross-process baseline is SHA-pinned and detects changed, missing and extra original checkpoints");
Check(suite.Contains("ImmutableBaselineSha256=AuditSaveGuard.BaselineSha256,MainHashes=AuditSaveGuard.OriginalHashes()")&&
    suite.Contains("AuditSaveGuard.ValidateRecovery")&&!suite.Contains("_recovery.MainHashes[file]=Hash(file)"),
    "new audits and recovery reuse the same immutable originals, never redefine them from current save bytes");
Check(suite.Contains("FindStoredSummary(AuditSaveGuard.MainName,AuditSaveGuard.MainSave)")&&
    smoke.Contains("if(AuditSaveGuard.Required)AuditSaveGuard.RequireReady()"),
    "initial audit boot is gated and uses the approved exact checkpoint, not a new latest main checkpoint");
Check(toolkit.Contains("Audit.RequestProtectedExit(\"explicit quit in protected audit process\")")&&
    !toolkit.Contains("ReleaseMainAutosaveHold(\"normal")&&saveGuard.Contains("private static bool NativeQuit()"),
    "quit in a protected audit bypasses SaveAllNow/flush and guards native shutdown too");
Check(returnControl.Contains("RequestProtectedExit(\"bounded evidence window elapsed; protected no-save exit\")")&&
    saveGuard.Contains("PostExitHashVerificationRequired=true")&&saveGuard.Contains("_closing=true; // Before Application.Quit"),
    "bounded return deadline keeps native saving blocked and requires an external post-exit hash check");
Check(toolkit.Contains("AuditSaveGuard.OnHostQuit();")&&saveGuard.Contains("No dependency on callback ordering or ToolkitMod.I.")&&
    !saveGuard.Contains("I=null")&&!saveGuard.Contains("_required=false"),
    "static save protection survives Melon cleanup and is never disarmed during native quit callbacks");
Check(saveGuard.Contains("Hook(provider,\"StoreGameData\"")&&saveGuard.Contains("Hook(provider,\"DeleteGamesData\"")&&
    saveGuard.Contains("Positive allowlist, not")&&saveGuard.Contains("nativeService&&!original")&&
    saveGuard.Contains("_baseline.OriginalDestination(name)"),
    "native file boundary denies unknown names; only exact owned checkpoints and unrelated native profile/settings pass");
Check(!protectedRestart.Contains("'quit'|Set-Content")&&protectedRestart.Contains("audit-integrity.ps1")&&
    protectedRestart.Contains("A game-directory executable is running"),
    "launcher never saves/quits a running game and requires immutable hashes before full-audit install/launch");
Check(protectedWatcher.Contains("-ExpectedBaselineSha256 $ImmutableBaselineSha256")&&
    immutableGate.Contains("Unexpected native save outside immutable originals")&&
    !immutableGate.Contains("WriteAll")&&!immutableGate.Contains("Set-Content"),
    "fresh-process checkpoint watcher rechecks one pinned baseline with a read-only complete-file-set gate");
Check(!protectedRestart.Contains("$fullAudit=$SelfTest -and")&&
    protectedRestart.Contains("Omitting -SelfTest never bypasses")&&
    immutableGate.Contains("Recovery launch requires a nonempty expected immutable baseline digest"),
    "pending audit state cannot bypass preflight by omitting SelfTest or passing an empty recovery digest");
Check(protectedWatcher.Contains("EXIT VERIFICATION PENDING")&&
    protectedWatcher.Contains("-RequireNoGame -RequireCopyRetired")&&
    protectedWatcher.Contains("FeatureReportAccepted=$featureAccepted;ReleaseAccepted=$false")&&
    protectedWatcher.IndexOf("PostExitVerificationComplete=$true",StringComparison.Ordinal)<
        protectedWatcher.IndexOf("exit 0",StringComparison.Ordinal),
    "watcher accepts neither state disappearance nor a failed/incomplete feature report before actual post-exit immutable verification");
Check(suite.Contains("AuditCheckpointStore.Write(StateFile")&&suite.Contains("if(previousUsed)")&&
    suite.Contains("_recovery.Boot=\"return\";WriteState();")&&suite.Contains("ReportSha256=Hash")&&
    suite.IndexOf("AuditCheckpointStore.Retire(StateFile",StringComparison.Ordinal)>
        suite.IndexOf("audit-terminal.json",StringComparison.Ordinal),
    "atomic recovery retains previous metadata; fallback returns safely and both checkpoints retire only after terminal/report commit");
Check(returnControl.Contains("WaypointStore.Read(existing,_recovery.TestName)")&&returnControl.Contains("Require(existing==json")&&
    returnControl.Contains("CancelForQuit();if(Active)return;_quitAfter=false;"),
    "owned sidecar retirement resumes idempotently and pre-copy cancellation continues a requested protected exit");
Check(smoke.Contains("RequireCopyAuditRequest()")&&smoke.Contains("arbitrary/main-world probes are refused")&&
    smoke.Contains("RestoreAutosaveDisabled(AuditSaveGuard.Required,_autoSaveWasDisabled)"),
    "standalone native probes explicitly refuse arbitrary/main worlds and protected cleanup cannot restore cached-false autosaving");
Check(gadgets.Contains("Time.frameCount<=job.ArmedFrame")&&gadgets.Contains("Time.unscaledTime+2f")&&
    gadgets.Contains("native._gadgetFootprintRendererInstance==null")&&!gadgets.Contains("native.Start()")&&
    gadgets.Contains("native._gadgetFootprintInstance.transform.SetPositionAndRotation(hit.point,rotation)")&&
    toolkit.Contains("(\"gadget placement\",Gadgets.Tick)"),
    "gadget transaction waits for native initialization, positions the native footprint, and never duplicates Start subscriptions");
Check(care.Contains("eater._chomper.CanChomp()")&&care.Contains("eater.MaybeSpinAndChomp(portion,false)")&&
    !careTests.Contains("body.constraints="),
    "favorite feeding honors the native bite cooldown and preserves unforced native motion");
Check(gadgets.Contains("if(job.EventIssued)")&&gadgets.Contains("job.EventIssued=true")&&
    gadgets.Contains("job.BeforeModels.Contains")&&gadgets.Contains("registry.Count!=job.ModelCountBefore+1")&&
    gadgets.Contains("CarePolicy.SpentOne(job.InventoryBefore,after)")&&gadgets.Contains("model.GetRot()*Vector3.up"),
    "single gadget event is acknowledged by its real registry/inventory/position/normal, not a private last-item cache");
Check(suite.Contains("_app.Map.Busy||_matrixTransitPending")&&suite.Contains("RebindStressCopy();_matrixTransitPending=false")&&
    suite.Contains("VerifyMainHashes();")&&careTests.Contains("create a separate owned native fixture"),
    "matrix defers owned scene transitions before strong copy rebind and never reuses a despawned sleep fixture");
Check(gadgets.Contains("inventory.TrySelectGadget(entry.Definition)")&&gadgets.Contains("OldSelectedSlot=inventory.SelectedSlotIndex.Value")&&
    gadgets.Contains("inventory.LockedSlot.Set(job.OldLocked)")&&gadgets.Contains("if(stillOwnSelection)")&&
    !gadgets.Contains("SetFavorite("),
    "native gadget selection is synchronized and restored without changing favorites or overwriting a user's intervening selection");
string nativeIcons=File.ReadAllText(Path.Combine(root,"src","creator","NativeIconTools.cs"));
string iconPolicyCode=File.ReadAllText(Path.Combine(root,"src","creator","NativeIconPolicy.cs"));
string phenomena=File.ReadAllText(Path.Combine(root,"src","creator","PhenomenaTools.cs"));
Check(gadgets.Contains("inventory.SetLockedGadgetIfNotFavorite(entry.Definition)")&&gadgets.Contains("inventory.LockedSlot.Set(job.OldLocked)"),
    "unfavorited crafted gadgets use the native temporary locked slot and restore it after the single event");
Check(world.Contains("director.StopState(state,zoneData.Parameters,true)")&&!world.Contains("_runningStates.Clear()")&&
    world.Contains("Завершение погоды"),"weather exits all current native effects through native API and reports pending exit honestly");
Check(suite.Contains("Stress / acceptance gate")&&suite.Contains("Native feature failures remain"),"a failed feature matrix cannot be promoted to an accepted stress run");
Check(ui.Contains("_iconTab.wordWrap=false")&&ui.Contains("IconTextStyle(text,rect.width-45)")&&
    ui.Contains("TabLabelsFitForTest")&&suite.Contains("Theme / native Cyrillic bookmarks fit one line"),
    "native Cyrillic tab labels use measured single-line 16/15/14 px styles with unchanged hit rectangles");
Check((nativeIcons+iconPolicyCode).Contains("\"iconPediaHomeWorld\"")&&(nativeIcons+iconPolicyCode).Contains("\"iconPediaCategoryScience\"")&&
    (nativeIcons+iconPolicyCode).Contains("\"marker_flag\"")&&!nativeIcons.Contains("\"iconMap\",\"MapNode\""),
    "world, science and waypoint categories resolve exact original sprites observed in the native census");
Check(careTests.Contains("camera.SetRotation(Quaternion.LookRotation")&&gadgets.Contains("cameraAimError=")&&
    gadgets.Contains("bool rayValid=native.IsPlacementValid(ray,hit)"),
    "owned gadget test aims the real native camera and still records every authoritative placement gate");
Check(gadgets.Contains("WorldRaycast(native,ray")&&gadgets.Contains("transform.IsChildOf(native._gadgetPlaceholderInstance.transform)")&&
    gadgets.Contains("_pending.ItemToken==native.Pointer")&&gadgets.Contains("if(hits.Length>512)return false")&&
    gadgets.Contains("bool rayValid=native.IsPlacementValid(ray,hit)"),"ground query excludes only its exact owned temporary preview, never real blockers or native validity gates");
Check(gadgets.Contains("native._baseGadget=surfaceGadget")&&gadgets.Contains("hit.collider.GetComponentInParent<Gadget>()")&&
    gadgets.Contains("surfaceGadget.GetModel()==null")&&gadgets.Contains("native._baseGadget=job.OldBase")&&
    !gadgets.Contains("CanStackOnStackableGadgets="),
    "native stacking classification follows the validated real surface and restores borrowed state without enabling stacking");
Check(atlasProbe.Contains("TryMapGround(candidate,false")&&atlasProbe.Contains("80f,96f")&&
    atlasProbe.Contains("groundRejected=")&&atlasProbe.Contains("_app.Map.OverlayContains(screen)"),
    "native map probe searches bounded genuine X/Z terrain at actual screen scale and logs rejected candidates without loosening production landing gates");
Check(ui.Contains("ButtonTextStyle(content,face.width/UiThemePolicy.HoverScale(offset))")&&ui.Contains("_buttonText14.fontSize=14")&&
    ui.Contains("ButtonLabelsFitForTest")&&suite.Contains("Theme / native long button labels preserve padding"),
    "long native Cyrillic action labels are measured with four bounded single-line styles and unchanged hitboxes");
Check(gadgets.Contains("CarePolicy.NativePlacementSurface(hit.normal.y,metadata.MaxValidPlacementSlope)")&&
    careTests.Contains("FindGadgetFixtureGround")&&careTests.Contains("ground.normal.y<.995f")&&
    careTests.Contains("aimed.collider!=collider")&&careTests.Contains("GuardCopy();"),
    "production honours native horizontal-angle metadata and positive fixture searches bounded real flat camera-visible ground on the isolated copy only");
Check(careTests.Contains("Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up)")&&
    careTests.Contains("new[]{4.2f,5.2f,6.3f}")&&careTests.Contains("Quaternion.AngleAxis(angle,Vector3.up)*forward"),
    "owned gadget fixture searches ahead of the actual camera and outside near-player geometry without ignoring native blockers");
Check(careTests.Contains("AimGadgetFixtureReady,12f")&&careTests.Contains("_gadgetAimRetries<12")&&
    careTests.Contains("error<=.15f&&aim<.1f")&&careTests.Contains("controller.SetRotation(Quaternion.LookRotation"),
    "native fixture waits for bounded real follow-camera convergence and validates the actual current ray before requesting placement");
foreach(var pills in new[]{
    new[]{"Все","Слаймы","Ларго","Эксперименты","Еда","Плорты","Ресурсы"},
    new[]{"Ясно","Дождь","Гроза","Слаймодождь","Пыльца","Ветер","Лава","Блики"},
    new[]{"Капсулы","Гордо","Телепорты","Ноды карты","Дроны","Ресурсы","Двери","Механизмы"}})
{
    float gap=pills[0]=="Ясно"?6f:8f;
    var widths=Enumerable.Range(0,pills.Length).Select(i=>UiThemePolicy.PillRowWidth(1100f,gap,pills,i)).ToArray();
    Check(widths.All(w=>float.IsFinite(w)&&w>120f)&&Math.Abs(widths.Sum()+gap*(pills.Length-1)-1100f)<.001f,
        "weighted native pill row stays inside its full container with positive padded widths: "+pills[0]);
}
Check(ui.Contains("filterX+=filterW+8f")&&ui.Contains("108+(i/2)*48")&&
    ui.Contains("sidebar=168")&&ui.Contains("SpawnTiles(")&&suite.Contains("ButtonLabelFitDiagnosticsForTest"),
    "longer native filter labels receive real layout width and each native measured failure names its exact font, padding and face width");
string nativePriority=File.ReadAllText(Path.Combine(root,"src","creator","NativeUiPriority.cs"));
string nativeWindows=File.ReadAllText(Path.Combine(root,"src","creator","NativeWindowTests.cs"));
string nativeReferences=File.ReadAllText(Path.Combine(root,"src","creator","NativeUiReferences.cs"));
Check(nativeReferences.Contains("GetComponentsInChildren<PediaSlimeAndResourceDetails>")&&
    !nativeReferences.Contains("GetComponentsInChildren<PediaEntryDetails>")&&
    nativeReferences.Contains("NativeSlimeArticleReady,15f")&&
    nativeReferences.Contains("native-pedia-layout-proof.json"),
    "native article fixture waits for the observed live details component and records actual unlocked buttons");
Check(nativeReferences.Contains("GetComponentsInChildren<PediaCategoryButton>")&&
    nativeReferences.Contains("Contains(_nativePinkEntry)")&&
    nativeReferences.Contains("native-pedia-category-click-proof.json")&&
    !nativeReferences.Contains(".ShowPedia("),
    "native Slimepedia category is opened by real mouse events, not an assumed ShowPedia callback");
Check(nativeReferences.Contains("events.RaycastAll(new PointerEventData(events)")&&
    nativeReferences.Contains("native-pedia-pointer-trace.json")&&
    nativeReferences.Contains("GetLastRaycastResult(mouse.deviceId)")&&
    nativeReferences.Contains("DirectCallbackInvoked=false"),
    "native article diagnostics compare read-only real raycasts and actual module results without invoking click handlers");
Check(nativeReferences.Contains("NativeActionSnapshot(inputModule?.point)")&&
    nativeReferences.Contains("NativeActionSnapshot(inputModule?.leftClick)")&&
    nativeReferences.Contains("EventSystemFocused=events?.isFocused")&&
    nativeReferences.Contains("category native press observed before release"),
    "native category input records actual mouse state, module action enablement and focus across the gesture");
Check(nativeReferences.Contains("GetComponentsInParent<CanvasGroup>(true)")&&
    nativeReferences.Contains("caster.Raycast(new PointerEventData(events)")&&
    nativeReferences.Contains("RegistrarFocus=root?._inputRegistrar?.HasFocus")&&
    nativeReferences.Contains("GraphicAllows=g.Raycast"),
    "native click diagnostics distinguish focus/fade/canvas filters from pointer/action failures without mutating UI");
Check(nativeReferences.Contains("NativeCategoryRaycastReady,15f")&&
    nativeReferences.Contains("await native focus/fade/raycast readiness")&&
    !nativeReferences.Contains(".blocksRaycasts="),
    "native category gesture waits for real raycast readiness without forcing canvas interactability");
Check(nativePriority.Contains("manager._lastState")&&nativePriority.Contains("TopStackableUI")&&
    !nativePriority.Contains("Resources.FindObjects"),"native input ownership uses real framework/legacy state, not cursor guesses or scans");
Check(nativePriority.Contains("UIDisplayFocusMode.NO_FOCUS")&&nativePriority.Contains("fake.IsFocused")&&
    !nativePriority.Contains("_pendingDisplays.Count>0"),
    "nonmodal tutorial/HUD displays and unfocused framework placeholders cannot block gameplay/menu input");
Check(nativePriority.Contains("if(top.TryCast<HudUI>()!=null)return false;")&&
    nativePriority.IndexOf("top.TryCast<HudUI>()",StringComparison.Ordinal)<
    nativePriority.IndexOf("top.TryCast<BaseUI>()",StringComparison.Ordinal),
    "actual native HudUI stack bottom is nonmodal without bypassing framework or legacy windows");
Check(capture.Contains("Active || NativeUiPriority.HasNativeFocus")&&
    capture.Contains("internal void YieldToNative()")&&toolkit.Contains("YieldToNativeUi()")&&
    hooks.Contains("!NativeUiPriority.HasNativeFocus"),"mod-only input capture yields and cannot force maps over native windows");
Check(capture.Contains("if(!_nativeHandoff)return")&&capture.Contains("bool pointerActivity=")&&
    capture.Contains("mouse.delta.ReadValue().sqrMagnitude")&&
    nativePriority.Contains("manager.SetFocusModeDevice(UIDisplayFocusModeDevice.MOUSE)")&&
    !nativePriority.Contains(".blocksRaycasts=")&&!capture.Contains(".blocksRaycasts="),
    "after its own handoff the mod routes actual mouse activity through the native device-mode API, never canvas flags");
Check(nativeReferences.Contains("idle mouse does not displace native keyboard")&&
    nativeReferences.Contains("FocusModeDevice==UIDisplayFocusModeDevice.KEYBOARD")&&
    toolkit.Contains("Input.TickNativeHandoff()")&&
    atlas.Contains("_app.Input.ExpectNativeWindow()"),
    "keyboard/gamepad remain native-owned with an idle mouse and explicit atlas handoff has a bounded activation window");
var nativeYield=capture.Split("internal void YieldToNative()")[1].Split("internal void OpenAfterNativeClose()")[0];
Check(!nativeYield.Contains("director.ForceUpdate")&&!nativeYield.Contains("Cursor.lockState =")&&
    nativeYield.Contains("Active=false")&&nativeYield.Contains("_baseline.Clear()"),
    "native handoff has no forced input-map recursion or stale cursor restoration");
Check(nativeWindows.Contains("native mouse pan changed map projection")&&nativeWindows.Contains("native wheel zoom changed map scale")&&
    suite.Contains("BuildNativeWindowInputSteps();"),"real native map pan/zoom/reopen checks precede graphics acceptance");
Check(nativeWindows.Contains("Mathf.Lerp(MapUtils.MIN_ZOOM,MapUtils.MAX_ZOOM,.5f)")&&
    nativeWindows.Contains("reverse native wheel changes map scale")&&
    !nativeWindows.Contains("SetMapZoom(2.0f)"),
    "native wheel fixture starts at actual native zoom-range midpoint and exercises both real wheel directions");
Check(nativeWindows.Contains("ScrollAction=NativeActionSnapshot(module?.scrollWheel)")&&
    nativeWindows.Contains("NativePanZoomParent=")&&nativeWindows.Contains("after real positive wheel")&&
    nativeWindows.Contains("control._isPointerActive"),
    "native wheel evidence preserves actual zoom bounds, pointer raycasts and native control readiness before asserting");
string nativeTmpProbe=File.ReadAllText(Path.Combine(root,"src","creator","NativeTmpLabelProbe.cs"));
string nativeFontExperiment=File.ReadAllText(Path.Combine(root,"src","creator","NativeFontExperiment.cs"));
Check(nativeTmpProbe.Contains("ToolkitMod.I.Audit.GuardStressCopy()")&&
    nativeTmpProbe.Contains("label.fontSharedMaterial = material")&&nativeTmpProbe.Contains("ResolvedGlyphs()")&&
    nativeTmpProbe.Contains("AtlasPopulationMode.Static")&&!nativeTmpProbe.Contains("CreateFontAsset("),
    "isolated native TMP specimen borrows proven static font/shared material and validates actual glyph provenance without creating substitute fonts");
Check(nativeFontExperiment.Contains("native-tmp-specimen-clear.png")&&
    nativeFontExperiment.Contains("native-tmp-specimen-imgui.png")&&
    nativeFontExperiment.Contains("RetireNativeFontExperiment()")&&
    nativeReferences.Contains("BuildNativeFontExperimentSteps()"),
    "native TMP experiment captures genuine glyphs with and without opaque IMGUI then disposes only owned resources; no production typography claim");
Check(iconPolicyCode.Contains("iconPlortPink_small")&&iconPolicyCode.Contains("iconPatchCorralFeeder")&&
    iconPolicyCode.Contains("iconPatchSiloBase")&&iconPolicyCode.Contains("iconWeatherThunderstorm")&&nativeIcons.Contains("ROLE "),
    "farm and weather roles resolve observed original assets with explicit provenance");
Check(ui.Contains("_selectedRowText.normal.textColor=TextTint(_ink)")&&
    ui.Contains("UiThemePolicy.JellyShadeFloor")&&UiThemePolicy.JellyShadeFloor(0xFF6B7A)>=.95f&&
    UiThemePolicy.JellyShadeFloor(0x00D4BD)>=.86f,
    "selected phenomenon rows use dark native ink on lighter jelly faces, retaining the exact shared texture budget");
Check(smoke.Contains("_rendered>=3&&FreshTabCapture()")&&smoke.Contains("file.LastWriteTimeUtc<=_tabCapturePreviousUtc")&&
    smoke.Contains("width==Screen.width&&height==Screen.height"),"each native tab must produce a fresh full-resolution PNG, not reuse an earlier candidate's image");
Check(nativeIcons.Contains("Resources.FindObjectsOfTypeAll<MapMarkerDefinition>()")&&nativeIcons.Contains("entry.SmallIcon??entry.Icon")&&
    nativeIcons.Contains("if(defs.Count==1)sprite=defs[0].Icon;")&&nativeIcons.Contains("if(parts.Count==1)sprite=parts[0].Icon;")&&!nativeIcons.Contains("new Texture2D"),
    "map and reward art borrows original game sprites rather than copying assets or geometric substitutes");
Check(ui.Contains("Rect iconRect=HoverChildRect(hit,new Rect(p2.x-half,p2.y-half,icon,icon),markerOffset)")&&
    ui.Contains("if(nativeIcon!=null)DrawSprite(iconRect,nativeIcon)")&&
    ui.Contains("NativeCategoryButton(")&&!ui.Contains("Icon(new Rect(hit.x+4,hit.y+4,36,36),MapIcon"),
    "native map markers and category filters no longer display handmade pictograms");
Check(ui.Contains("map.Icons.Reward(reward.Key)")&&ui.Contains("_wrap.CalcHeight(ButtonContent(reward.Title),viewport.width-92)")&&
    ui.Contains("view.Rewards"),"capsule reward cards combine original icons, native titles, counts and measured Cyrillic wrapping");
Check(phenomena.Contains("IntPtr Token,Session")&&phenomena.Contains("NativeActorRoots=>0")&&
    phenomena.Contains("FindObjectsOfType<AuroraObject>(true)")&&phenomena.Contains("FindObjectsOfType<ReverseGravityZone>(true)"),
    "phenomena observatory reads only actual loaded native sources and retains no scene actor wrapper");
Check(phenomena.Contains("ResolveAurora(row)")&&phenomena.Contains("_app.World.SetHour(")&&
    !phenomena.Contains(".SetState(")&&!phenomena.Contains(".SetOperating(")&&!phenomena.Contains(".UpdateShaderValue("),
    "native phase waiting only advances the game clock and never forces colliders/shaders/global gravity");
Check(phenomena.Contains("source._triggerState.ToString()")&&phenomena.Contains("PhenomenaWindowPolicy.After(activeStart)")&&
    !phenomena.Contains("SetHour((current._triggerStartHour"),"native solid-phase waiting honors ON versus OFF trigger-window semantics");
string phenomenaTests=File.ReadAllText(Path.Combine(root,"src","creator","PhenomenaTests.cs"));
Check(phenomenaTests.Contains("original native transition activates solid colliders")&&
    phenomenaTests.Contains("source.InTriggerWindow()")&&phenomenaTests.Contains("enabledSolidColliders=")&&
    phenomenaTests.Contains("Group=group,GroupName=group.name,Position=group.InitialPlayerPosition"),
    "native observatory has a positive real-scene, real-window and original-solid-collider matrix, not only an absent-source smoke test");
Check(phenomenaTests.Contains("cancellation leaves actual native phase and clock unchanged")&&
    phenomenaTests.Contains("_app.AcceptConfirmation()")&&phenomenaTests.Contains("LastStableGroundSamples>=4")&&
    suite.Contains("RetirePhenomenaFixtures();"),"native phase approval/cancel and guarded travel/retirement are explicitly tested on the owned copy");
Check(toolkit.Contains("Phenomena?.Reset()")&&atlas.Contains("Icons.Reset()")&&stress.Contains("m.PhenomenaActorRoots==0")&&
    stress.Contains("m.NativeIconAssetRoots<=NativeIconTools.AssetBudget"),"new native-art and phenomena caches are reset and stress-budgeted");
string ioDirectory=Path.Combine(Path.GetTempPath(),"CT-DiagnosticIo-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(ioDirectory);
string ioMarker=Path.Combine(ioDirectory,"selftest.request"),ioStatus=Path.Combine(ioDirectory,"status.txt"),ioCommands=Path.Combine(ioDirectory,"cmd.txt");
void WaitIoStatus(DiagnosticIo io,string expected)
{
    bool published=SpinWait.SpinUntil(()=>
    {
        try
        {
            using var stream=new FileStream(ioStatus,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            using var reader=new StreamReader(stream);
            return reader.ReadToEnd()==expected;
        }
        catch(IOException){return false;}
    },10000);
    if(!published)throw new InvalidOperationException("Diagnostic worker did not publish "+expected+"; worker error: "+(io.TakeError()?.ToString()??"none"));
}
try
{
    File.WriteAllText(ioMarker,"continue\r\n");
    using var io=new DiagnosticIo(ioDirectory);
    Check(io.SelftestRequestOption=="continue","offline worker caches the opt-in startup option exactly");
    Check(File.Exists(ioMarker),"request discovery never consumes a durable next-process marker");
    File.Delete(ioMarker);io.InvalidateSelftestRequest();
    Check(io.SelftestRequestOption==null,"explicit acknowledgement immediately clears the cached request");
    File.WriteAllText(ioMarker,"");io.QueueSnapshot("empty");WaitIoStatus(io,"empty");
    Check(io.SelftestRequestOption=="","worker distinguishes an empty request marker from no request");
    File.Delete(ioMarker);io.InvalidateSelftestRequest();io.QueueSnapshot("cleared");WaitIoStatus(io,"cleared");
    Check(io.SelftestRequestOption==null,"worker does not resurrect a deleted/acknowledged request");
    File.WriteAllText(ioCommands,"status\ncapture");io.QueueSnapshot("commands");WaitIoStatus(io,"commands");
    Check(io.TakeCommands().SequenceEqual(new[]{"status","capture"}),"worker preserves ordered diagnostic commands");
    Check(!File.Exists(ioCommands)&&io.TakeCommands().Length==0,"diagnostic commands are consumed exactly once");
    File.WriteAllText(ioCommands,"status\ncapture");
    using(var lockedWriter=new FileStream(ioCommands,FileMode.Open,FileAccess.Write,FileShare.None))
    {
        io.QueueSnapshot("command-locked");WaitIoStatus(io,"command-locked");
        Check(io.TakeError()==null,"transient command sharing lock is deferred without latching a gameplay error");
        Check(File.Exists(ioCommands)&&io.TakeCommands().Length==0,"locked command stays durable and unexecuted while status publication proceeds");
    }
    io.QueueSnapshot("command-released");WaitIoStatus(io,"command-released");
    Check(io.TakeError()==null&&io.TakeCommands().SequenceEqual(new[]{"status","capture"}),"released producer lock recovers the complete ordered command batch");
    io.QueueSnapshot("command-once");WaitIoStatus(io,"command-once");
    Check(!File.Exists(ioCommands)&&io.TakeCommands().Length==0,"retried diagnostic command batch is consumed exactly once");
    Exception sharingError=null;
    using(var lockedReader=new FileStream(ioStatus,FileMode.Open,FileAccess.Read,FileShare.Read))
    {
        io.QueueSnapshot("locked");
        bool bounded=SpinWait.SpinUntil(()=>{sharingError=io.TakeError();return sharingError!=null;},5000);
        Check(bounded&&(sharingError is IOException||sharingError is UnauthorizedAccessException),
            "locked Windows status readers produce a bounded I/O error rather than an unbounded worker wait");
    }
    io.QueueSnapshot("released");WaitIoStatus(io,"released");
    Check(io.TakeError()==null,"diagnostic publication recovers after the reader releases its file lock");
    File.WriteAllText(ioMarker,new string('x',4097));io.QueueSnapshot("oversize");WaitIoStatus(io,"oversize");
    Check(io.TakeError() is InvalidOperationException&&io.SelftestRequestOption==null&&File.Exists(ioMarker),
        "oversized self-test requests are rejected without blocking status publication or deleting the request");
}
finally{Directory.Delete(ioDirectory,true);}
// LO native-icon policy: simulated availability is NOT a loaded-game assertion.
foreach(string role in NativeIconPolicy.Roles)
{
    var absent=NativeIconPolicy.ResolveExact(role,_=>false);
    Check(absent.Declared&&!absent.Resolved&&absent.Availability==NativeIconPolicy.Availability.DeclaredUnresolved,
        "declared native icon role reports missing assets honestly: "+role);
}
foreach(var pair in new[]{("Compass","frameHudCompassFacing"),("PinkPlort","iconPlortPink_small"),
    ("Feeder","iconPatchCorralFeeder"),("Silo","iconPatchSiloBase")})
{
    var available=NativeIconPolicy.ResolveExact(pair.Item1,name=>name==pair.Item2);
    Check(available.Resolved&&available.AssetName==pair.Item2,"observed native alias resolves without schematic art: "+pair.Item1);
    Check(!NativeIconPolicy.ResolveExact(pair.Item1,name=>name=="time_sun").Resolved,
        "unrelated artwork cannot satisfy required native role: "+pair.Item1);
}
foreach(string role in new[]{"WeatherClear","WeatherRain","WeatherThunder","WeatherSlime","WeatherPollen","WeatherWind","WeatherLava","WeatherSolar"})
    Check(NativeIconPolicy.Roles.Contains(role),"all eight selectable weather roles are declared: "+role);
foreach(string role in new[]{"WeatherLava","WeatherSolar"})
    Check(NativeIconPolicy.ExactNames(role).Count==0&&!NativeIconPolicy.ResolveExact(role,_=>true).Resolved,
        "unobserved dedicated weather aliases never fabricate asset availability: "+role);
Check(!NativeIconPolicy.ResolveExact(null,_=>true).Declared&&!NativeIconPolicy.ResolveExact("not-a-native-role",_=>true).Declared,
    "unknown and null icon roles are not claimed declared or available");
Check(NativeIconPolicy.WeatherPresetRole("LavaRain")=="WeatherLava"&&NativeIconPolicy.WeatherPresetRole("SolarFlare")=="WeatherSolar"&&
    NativeIconPolicy.WeatherPresetRole("SunFlare")=="WeatherSolar"&&NativeIconPolicy.WeatherPresetRole("lava solar")==""&&
    NativeIconPolicy.WeatherPresetRole("lavender") == "",
    "weather role tokenization handles native identities and rejects ambiguous identities");
Check(NativeIconPolicy.WeatherBudget==24&&nativeIcons.Contains("pattern.Metadata")&&nativeIcons.Contains("metadata.Icon")&&
    nativeIcons.Contains("metadata.PediaEntry")&&nativeIcons.Contains("_weatherRoles.Count>=NativeIconPolicy.WeatherBudget"),
    "every native weather list entry uses its own authoritative bounded metadata path");
Check(ui.Contains("WeatherLava")&&ui.Contains("WeatherSolar")&&ui.Contains("WeatherRole(c.Pattern)")&&
    ui.Contains("choice!=null?_app.Map.Icons.WeatherRole(choice.Pattern):roles[i]")&&
    ui.Contains("нет иконки"),"weather buttons cover all native choices and visibly report unresolved art");
Check(explorerUi.Contains("NativeRequiredIcon(new Rect(18,162+o,42,42),\"PinkPlort\")")&&
    explorerUi.Contains("NativeRequiredIcon(new Rect(x+18,162+o,42,42),\"Feeder\")")&&
    explorerUi.Contains("NativeRequiredIcon(new Rect(rect.x+16,rect.y+14,36,36),\"Compass\")")&&
    !explorerUi.Contains("Icon(new Rect(rect.x+16,rect.y+14,36,36),15)"),
    "farm and custom map card use native role bindings rather than schematic atlas art");

// Same pure decision is called from NativeUiPriority.TickNativeFocus.
var noOwner=new NativeFocusPolicy.Identity();
var pediaOwner=new NativeFocusPolicy.Identity(new IntPtr(1),new IntPtr(2),new IntPtr(3));
var mapOwner=new NativeFocusPolicy.Identity(new IntPtr(4),new IntPtr(2),new IntPtr(5));
var modalOpen=NativeFocusPolicy.Evaluate(noOwner,pediaOwner,false);
Check(modalOpen.AcquireMouse&&modalOpen.UnlockCursor&&modalOpen.ReturnNavigation,
    "F1/modal opening acquires mouse and navigation with mod closed, expired lease and idle mouse");
var modalIdle=NativeFocusPolicy.Evaluate(pediaOwner,pediaOwner,false);
Check(!modalIdle.AcquireMouse&&!modalIdle.UnlockCursor&&modalIdle.ReturnNavigation,
    "idle native keyboard/gamepad may reclaim the same modal without repeated MOUSE forcing");
var modalMouse=NativeFocusPolicy.Evaluate(pediaOwner,pediaOwner,true);
Check(!modalMouse.AcquireMouse&&modalMouse.UnlockCursor&&modalMouse.ReturnNavigation,
    "existing native MOUSE owner repairs late hidden/locked cursor without reassigning device");
Check(NativeFocusPolicy.Evaluate(pediaOwner,mapOwner,false).AcquireMouse,
    "switching Pedia to Map reacquires pointer immediately");
Check(NativeFocusPolicy.Evaluate(pediaOwner,pediaOwner with{Root=new IntPtr(99)},false).AcquireMouse&&
    NativeFocusPolicy.Evaluate(pediaOwner,pediaOwner with{Manager=new IntPtr(99)},false).AcquireMouse,
    "replacement native root or manager is a fresh owner transition");
foreach(bool mouseMode in new[]{false,true})
{
    var gameplay=NativeFocusPolicy.Evaluate(pediaOwner,noOwner,mouseMode);
    Check(!gameplay.AcquireMouse&&!gameplay.UnlockCursor&&!gameplay.ReturnNavigation,
        "gameplay/HUD/NO_FOCUS absence never causes cursor writes, mode="+mouseMode);
}
Check(NativeFocusPolicy.Evaluate(noOwner,pediaOwner,false).AcquireMouse,
    "reopening the same native owner after observed absence acquires pointer again");
Check(nativePriority.Contains("NativeFocusPolicy.Evaluate")&&nativePriority.Contains("Cursor.lockState=CursorLockMode.None")&&
    nativePriority.Contains("Cursor.visible=true")&&nativePriority.Contains("events.sendNavigationEvents=true"),
    "tested focus decision controls actual native cursor/navigation side effects");
var focusTick=capture.Split("internal void TickNativeHandoff()")[1].Split("internal void ExpectNativeWindow()")[0];
Check(focusTick.IndexOf("NativeUiPriority.TickNativeFocus()",StringComparison.Ordinal)<focusTick.IndexOf("if(!_nativeHandoff)return",StringComparison.Ordinal),
    "native opening repair precedes the optional handoff lease guard");
var lateUpdate=toolkit.Split("public override void OnLateUpdate()")[1].Split("public override void OnGUI()")[0];
Check(lateUpdate.Contains("Input.TickNativeHandoff()")&&!lateUpdate.Contains("if (!MenuOpen) return;"),
    "late-frame modal acquisition runs even while CreatorToolkit is closed");

// Raycast geometry tests run without Unity, native canvases, a process or saves.
var viewport=new MapShieldPolicy.Box(0,0,200,100);
var source=new MapShieldPolicy.Box(10,10,40,40);
var cut=new MapShieldPolicy.Box(20,20,10,10);
var shieldPieces=MapShieldPolicy.Build(new[]{source},new[]{cut},viewport);
Check(shieldPieces.Count==4&&Math.Abs(shieldPieces.Sum(b=>b.Width*b.Height)-1500)<.01f,
    "native control exclusion is subtracted exactly from a mod card, without larger AABB shields");
bool Overlap(MapShieldPolicy.Box a,MapShieldPolicy.Box b)=>a.X<b.Right&&a.Right>b.X&&a.Y<b.Bottom&&a.Bottom>b.Y;
bool Inside(MapShieldPolicy.Box a,MapShieldPolicy.Box b)=>a.X>=b.X&&a.Y>=b.Y&&a.Right<=b.Right&&a.Bottom<=b.Bottom;
Check(shieldPieces.All(b=>b.Valid&&Inside(b,source)&&Inside(b,viewport)&&!Overlap(b,cut)),
    "all shield pieces stay finite, inside actual mod pixels and outside native controls");
Check(MapShieldPolicy.Build(Array.Empty<MapShieldPolicy.Box>(),Array.Empty<MapShieldPolicy.Box>(),viewport).Count==0&&
    MapShieldPolicy.Build(new[]{source},new[]{source},viewport).Count==0,
    "no overlay or a completely native-covered card produces no shield");
var clippedPieces=MapShieldPolicy.Build(new[]{new MapShieldPolicy.Box(-5,-5,20,20)},Array.Empty<MapShieldPolicy.Box>(),viewport);
Check(clippedPieces.Count==1&&clippedPieces[0].X==0&&clippedPieces[0].Y==0&&clippedPieces[0].Width==15&&clippedPieces[0].Height==15,
    "partly offscreen mod geometry clips to the viewport rather than expanding it");
foreach(var invalid in new[]{new MapShieldPolicy.Box(float.NaN,0,1,1),new MapShieldPolicy.Box(0,0,-1,1),
    new MapShieldPolicy.Box(0,0,1,float.PositiveInfinity),new MapShieldPolicy.Box(float.MaxValue,0,float.MaxValue,1)})
{
    Check(MapShieldPolicy.Build(new[]{invalid},Array.Empty<MapShieldPolicy.Box>(),viewport).Count==0,
        "invalid mod geometry never publishes native raycast shields");
    Check(MapShieldPolicy.Build(new[]{source},new[]{invalid},viewport).Count==0,
        "invalid native exclusions fail open without blocking any native input");
}
Check(MapShieldPolicy.Build(new[]{source},Array.Empty<MapShieldPolicy.Box>(),default).Count==0&&
    MapShieldPolicy.Build(null,Array.Empty<MapShieldPolicy.Box>(),viewport).Count==0&&
    MapShieldPolicy.Build(new[]{source},null,viewport).Count==0,
    "unknown source, exclusions or viewport fail open");
Check(MapShieldPolicy.Build(Enumerable.Repeat(source,MapShieldPolicy.MaxSources+1).ToArray(),Array.Empty<MapShieldPolicy.Box>(),viewport).Count==0&&
    MapShieldPolicy.Build(new[]{source},Enumerable.Repeat(cut,MapShieldPolicy.MaxExclusions+1).ToArray(),viewport).Count==0,
    "unbounded source or exclusion workloads cannot create raycast shields");
var separated=new[]{new MapShieldPolicy.Box(0,0,10,10),new MapShieldPolicy.Box(100,0,10,10)};
Check(MapShieldPolicy.Build(separated,Array.Empty<MapShieldPolicy.Box>(),viewport).All(b=>separated.Any(a=>Inside(b,a))),
    "separate mod markers never create an invented shield spanning native panning gaps");
var random=new Random(170015);bool clippedProperty=true;
for(int i=0;i<160;i++)
{
    var box=new MapShieldPolicy.Box(random.Next(-50,200),random.Next(-50,100),random.Next(1,80),random.Next(1,80));
    var exclusion=new MapShieldPolicy.Box(random.Next(0,190),random.Next(0,90),random.Next(1,60),random.Next(1,60));
    var pieces=MapShieldPolicy.Build(new[]{box},new[]{exclusion},viewport);
    clippedProperty&=pieces.All(piece=>piece.Valid&&Inside(piece,box)&&Inside(piece,viewport)&&!Overlap(piece,exclusion));
}
Check(clippedProperty,"160 seeded geometry cases preserve exact mod-only coverage and native exclusions");
Check(!atlas.Contains("Instantiate(Ui._mapBackground)")&&!atlas.Contains("SetAsLastSibling()")&&
    atlas.Contains("MapShieldPolicy.Build")&&atlas.Contains("CollectNativeControlExclusions")&&
    atlas.Contains("GetComponentsInChildren<Selectable>")&&atlas.Contains("MapZoomSlider"),
    "native background is never cloned/reordered and actual native buttons/zoom are subtracted");
var overlayDraw=ui.Split("internal void DrawMapOverlay()")[1].Split("private void DrawConfirmation")[0];
Check(overlayDraw.IndexOf("map.BeginMapOverlayFrame()",StringComparison.Ordinal)<overlayDraw.IndexOf("!map.NativeMapHasInput",StringComparison.Ordinal)&&
    overlayDraw.Contains("if(repaint)map.SyncRaycastShields()")&&overlayDraw.Contains("map.SuspendRaycastShields();")&&
    !overlayDraw.Contains("finally{try{if(Event.current"),
    "map shields retire before visibility gates and publish only completed Repaint geometry");
Check(overlayDraw.Contains("if(repaint)map.OverlayCardRects.Add(new Rect(panel.x*")&&
    overlayDraw.Contains("if(repaint)map.MarkerHitRects.Add"),
    "shield sources are only actually rendered panel/card/marker hit rectangles");

Check(!MapShieldPolicy.AllowModPointer(false,new[]{cut},5,5)&&
    !MapShieldPolicy.AllowModPointer(true,new[]{cut},25,25)&&
    MapShieldPolicy.AllowModPointer(true,new[]{cut},5,5),
    "native exclusion holes reject mod clicks while native raycasts remain free; unknown layouts disable mod actions");
Check(!MapShieldPolicy.AllowModPointer(true,null,5,5)&&
    !MapShieldPolicy.AllowModPointer(true,new[]{new MapShieldPolicy.Box(float.NaN,0,1,1)},5,5)&&
    !MapShieldPolicy.AllowModPointer(true,new[]{cut},float.NaN,5),
    "unreadable native action exclusions and invalid pointer coordinates fail closed for mod actions");
Check(atlas.Contains("if(!AllowsOverlayPointer(screen))return true;")&&
    atlas.Contains("MapShieldPolicy.AllowModPointer")&&atlas.Contains("exclusions.AddRange(_nativeControlRects)")&&
    ui.Contains("_nativeMapOverlayDrawing||_nativeMapPointerAllowed")&&
    ui.Contains("map.AllowsOverlayPointer(Mouse.current.position.ReadValue())"),
    "the tested native-hole policy gates both IMGUI actions and custom-point/teleport gestures");
Check(overlayDraw.IndexOf("map.PrepareNativeControlExclusions()",StringComparison.Ordinal)<
    overlayDraw.IndexOf("MapOverlayLayout.Place",StringComparison.Ordinal)&&
    ui.Contains("GUIUtility.ScaleAroundPivot(new Vector2(scale,scale),center)")&&ui.Contains("finally{GUI.matrix=previous;}"),
    "actual control exclusions precede overlay input and hover glyph pivots respect nested GUI groups");
Check(!OptimizationPolicy.NativeRenderScalingSupported&&!OptimizationPolicy.NativeFoliageReductionSupported,
    "unsafe native graphics capabilities remain explicitly unavailable after access violation");
Check(!hooks.Contains("Hook(typeof(DecoInstanceManager)")&&!hooks.Contains("FoliageDrawBudget(")&&
    !optimization.Contains("BeforeFoliage(")&&!optimization.Contains("ref float"),
    "no foliage render detour or float argument rewrite can corrupt native bounds");
Check(optimization.Contains("int foliage=FoliageSupported?request.FoliagePercent:100")&&
    optimization.Contains("FoliagePercent=foliage")&&optimization.Contains("return percent==100;"),
    "potato applies only supported features and reports unchanged foliage and render honestly");
Check(!optUi.Contains("Slider(71")&&!optUi.Contains("Slider(72")&&
    optUi.Contains("Максимум FPS")&&optUi.Contains("tools.ApplyMaximum()"),
    "unsafe graphics sliders and preset selectors cannot be activated in UI");
Check(optimization.Contains("OptimizationRenderSupported:")&&optimization.Contains("OptimizationFoliageReason:"),
    "diagnostics explain rendering quarantine rather than claiming unsupported reductions");
Check(toolkit.Contains("case \"graphics-potato\": if(Ready) Queue(\"diagnostic potato\",()=>Optimization.ApplyPotato())")&&
    toolkit.Contains("case \"graphics-restore\": if(Ready) Queue(\"diagnostic graphics restore\",()=>Optimization.Restore())"),
    "explicit ready-gated local diagnostics use the actual queued potato and restore paths");
Check(!OptimizationPolicy.NativeModelBatchWritesSupported,
    "native model batch writes are unavailable after confirmed WorldPopulator setter crash");
Check(!System.Text.RegularExpressions.Regex.IsMatch(optimization,
    @"WorldPopulator\.(?:NON_)?ACTORS_PER_FRAME\s*=")&&
    !optimization.Contains("il2cpp_field_static_set_value(")&&
    !optimization.Contains("set_ACTORS_PER_FRAME("),
    "apply and restore never call crashing model-batch setters or raw static-field write bypasses");
Check(optimization.Contains("SupportsNativeBatches=>OptimizationPolicy.NativeModelBatchWritesSupported")&&
    optimization.Contains("QualitySettings.streamingMipmapsRenderersPerFrame=_renderersExpected")&&
    optimization.Contains("QualitySettings.asyncUploadTimeSlice=_uploadExpected"),
    "smooth loading uses supported streaming APIs and honestly reports unavailable native model batching");
UiSprint2Regression.Run(Check,root);
PassiveFrameBenchmarkRegression.Run(Check,root);
GeometryLodRegression.Run(Check,root);
RadiantCatalogRegression.Run(Check,root);
M14Regression.Run(Check,root);
NativeIconOwnerBindingRegression.Run((v,n)=>Check(v,"m14 / owner binding / "+n),root);
M14FollowupRegression.Run(Check,root);
WorldSourceObservationRegression.Run((v,n)=>Check(v,"m14 / sources / "+n),root);
M15Regression.Run(Check,root);
M16Regression.Run(Check,root);
M17Regression.Run(Check,root);
M18Regression.Run(Check,root);
M19GraphicsRegression.Run(Check,root);
M20Regression.Run(Check,root);
M21WeatherRegression.Run(Check,root);
M22CapsuleRegression.Run(Check,root);
M15GordoRegression.Run(Check,root);
MapIconRegression.Run(Check,root);
M23DragGateRegression.Run(Check,root);
M23CanonIconRegression.Run(Check,root);
M23ResourceLayerRegression.Run(Check,root);
M24InfiniteBuilderRegression.Run(Check,root);
M25RuntimeRegression.Run(Check,root);
M26Regression.Run(Check,root);
M27Regression.Run(Check,root);
M28Regression.Run(Check,root);
M29Regression.Run(Check,root);
CoopRegression.Run(Check,root);
CoexistenceRegression.Run(Check,root);
InteropRegression.Run(Check,root);
M30CaptureRegression.Run(Check,root);
M31Regression.Run(Check,root);
Console.WriteLine($"SUMMARY: {passed} checks passed. Compilation/package/policy checks only; live-game validation is pending.");
string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(dll))).ToLowerInvariant();
File.WriteAllText(Path.Combine(root, "_work", "creator-tests.summary.txt"), $"PASS {passed}\nDllSha256={sha}\nLiveGameVerified=false\n");
