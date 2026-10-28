using CreatorToolkit;
using S = CreatorToolkit.ScrollMotionPolicy;

internal static class M14Regression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void Check(bool value,string name)=>check(value,"m14 / "+name);
        bool Near(float a,float b,float tolerance=.001f)=>float.IsFinite(a)&&float.IsFinite(b)&&Math.Abs(a-b)<=tolerance;
        // Actual pure production policy; no copied motion implementation.
        float[] values={-20f,0f,.125f,48.25f,400f,float.NaN,float.PositiveInfinity,float.NegativeInfinity};
        float[] limits={-1f,0f,150f,float.NaN,float.PositiveInfinity};
        foreach(float value in values)
        foreach(float limit in limits)
        {
            float bound=float.IsFinite(limit)&&limit>0?limit:0;
            float expected=!float.IsFinite(value)||value<0?0:value>bound?bound:value;
            Check(S.Clamp(value,limit)==expected,"scroll pure clamp invalid and bounds "+value+" / "+limit);
        }
        foreach(float delta in new[]{.01f,.125f,-.01f,-.125f})
        {
            float next=S.Wheel(64f,delta,48f,1000f);
            Check(Near(next,64f+delta*48f)&&next!=64f,"scroll pure fractional signed wheel retained "+delta);
        }
        Check(Near(S.Wheel(S.Wheel(64,.125f,48,1000),.125f,48,1000),S.Wheel(64,.25f,48,1000)),"scroll pure fractional deltas accumulate without sign quantization");
        Check(Near(S.Wheel(S.Wheel(64,.125f,48,1000),-.125f,48,1000),64),"scroll pure opposite small deltas cancel before bounds");
        Check(S.Wheel(10,-100,48,150)==0,"scroll pure wheel lower bound");
        Check(S.Wheel(10,100,48,150)==150,"scroll pure wheel upper bound");
        Check(S.Wheel(10,1,48,-1)==0,"scroll pure negative viewport extent clamps closed");
        Check(S.Wheel(float.NaN,0,48,150)==0,"scroll pure invalid wheel target sanitized");
        foreach(float delta in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            Check(S.Wheel(64,delta,48,150)==64,"scroll pure nonfinite delta ignored "+delta);
        foreach(float row in new[]{0f,-1f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            Check(S.Wheel(64,.25f,row,150)==64,"scroll pure invalid row scale ignored "+row);
        Check(S.Wheel(64,float.MaxValue,48,150)==0,"scroll pure overflow remains finite and fail closed");
        foreach(float seconds in new[]{0f,-.01f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            Check(S.Step(20,100,seconds,150)==20,"scroll pure invalid or paused elapsed time never advances "+seconds);
        Check(S.Step(-10,100,0,150)==0,"scroll pure paused time still clamps current lower bound");
        Check(S.Step(500,100,0,150)==150,"scroll pure paused time still clamps current upper bound");
        Check(S.Step(float.NaN,100,0,150)==0,"scroll pure invalid current sanitized");
        Check(S.Step(20,float.NaN,.05f,150)<20,"scroll pure invalid target safely approaches zero");
        Check(S.Step(20,100,.05f,float.NaN)==0,"scroll pure invalid limit has no motion");
        Check(S.Step(20,20,.05f,150)==20,"scroll pure stationary target is stable");
        Check(S.Step(99.99f,100,.01f,150)==100,"scroll pure subpixel convergence settles at exact target");
        Check(S.Step(20,100,1,150)==S.Step(20,100,.1f,150),"scroll pure large elapsed time capped at one tenth second");
        foreach(float current in new[]{0f,40f,140f})
        foreach(float target in new[]{0f,60f,150f})
        foreach(float dt in new[]{.001f,1f/120f,1f/30f,.1f})
        {
            float next=S.Step(current,target,dt,150);
            Check(float.IsFinite(next)&&next>=Math.Min(current,target)&&next<=Math.Max(current,target),"scroll pure monotone no overshoot "+current+" / "+target+" / "+dt);
        }
        // Semigroup comparisons deliberately stay away from the final .05px snap.
        // Each interval AND its sum is <= .1; no dependence on frame count or FPS.
        foreach(float current in new[]{0f,900f})
        foreach(float a in new[]{.005f,.01f,.025f})
        foreach(float b in new[]{.005f,.02f,.05f})
        {
            float one=S.Step(current,400,a+b,1000);
            float split=S.Step(S.Step(current,400,a,1000),400,b,1000);
            Check(Near(one,split),"scroll pure elapsed-time semigroup away from settling "+current+" / "+a+" / "+b);
        }
        float Integrate(float current,int steps,float dt)
        {for(int i=0;i<steps;i++)current=S.Step(current,1000,dt,1200);return current;}
        Check(Near(Integrate(0,6,1f/60f),Integrate(0,12,1f/120f)),"scroll pure equal elapsed time at 60 and 120 partitions");
        Check(Near(Integrate(0,3,1f/30f),Integrate(0,12,1f/120f)),"scroll pure equal elapsed time at 30 and 120 partitions");
        Check(Near(Integrate(0,1,.1f),Integrate(0,12,1f/120f)),"scroll pure equal elapsed time at 10 and 120 partitions");
        Check(S.Step(0,S.Wheel(0,.125f,48,150),.01f,150)>0&&S.Step(0,S.Wheel(0,.125f,48,150),.01f,150)<6,"scroll pure fractional input flows into smooth sub-row movement");
        // Wiring is separate textual evidence, not pointer/render/game behavior.
        string ui=File.ReadAllText(Path.Combine(root,"src","creator","ToolkitUi.cs"));
        string compact=string.Concat(ui.Where(c=>!char.IsWhiteSpace(c)));
        Check(compact.Contains("ScrollMotionPolicy.Wheel(motion.Target,e.delta.y,rowHeight,limit)")&&!compact.Contains("Mathf.Sign(e.delta.y)"),"scroll static wiring consumes full wheel delta not its sign");
        Check(compact.Contains("motion.Frame!=Time.frameCount")&&compact.Contains("ScrollMotionPolicy.Step(scroll.y,motion.Target,Time.unscaledTime-motion.Time,limit)"),"scroll static wiring unscaled elapsed time and per-frame guard present");
        Check(!compact.Contains("Mathf.Round(scroll.y/rowHeight)")&&!compact.Contains("Mathf.Round(_weatherScroll.y/44f)"),"scroll static wiring former catalog and weather row snaps absent");
        CapsuleChecks(Check);
        IconChecks(Check);
        LifecycleWiring(Check,root);
    }

    private static void CapsuleChecks(Action<bool,string> Check)
    {
        var ids=new (string Expected,string Actual,bool Match)[]{("pod-A","pod-A",true),("pod-A","pod-a",false),("pod-A","pod-AA",false),("pod-A"," pod-A",false),("pod-A","pod-A ",false),(null,null,false),("","",false),(" "," ",false),("pod-A",null,false),("pod-A","pod-B",false)};
        for(int i=0;i<ids.Length;i++)Check(CapsuleTruthPolicy.SameId(ids[i].Expected,ids[i].Actual)==ids[i].Match,"capsule pure exact identity case "+i);
        foreach(bool same in new[]{false,true})
        foreach(bool has in new[]{false,true})
        foreach(bool locked in new[]{false,true})
        foreach(bool open in new[]{false,true})
        {
            var expected=!same||!has||locked==open?CapsuleTruthPolicy.OpenState.Unknown:open?CapsuleTruthPolicy.OpenState.Open:CapsuleTruthPolicy.OpenState.Locked;
            Check(CapsuleTruthPolicy.State("pod-A",same?"pod-A":"pod-B",has,locked,open)==expected,"capsule pure state rejects unloaded wrong ID absent contradictory flags "+same+"/"+has+"/"+locked+"/"+open);
        }
        foreach(var state in Enum.GetValues<CapsuleTruthPolicy.OpenState>())
            Check(CapsuleTruthPolicy.ShouldHideOpened(state)==(state==CapsuleTruthPolicy.OpenState.Open),"capsule pure hide opened preserves unknown and locked "+state);
        foreach(var status in Enum.GetValues<CapsuleTruthPolicy.RewardStatus>())
            Check(CapsuleTruthPolicy.Confirmed(status)==(status==CapsuleTruthPolicy.RewardStatus.NativeMatch),"capsule pure only native multiset match confirms reward "+status);
        CapsuleTruthPolicy.RewardAmount R(string kind,string asset,long count)=>new(kind,asset,count);
        var expectedRewards=new[]{R("Blueprint","Shared",1),R("Component","Shared",1),R("Item","Shared",3)};
        var reordered=new[]{R("Item","Shared",1),R("Component","Shared",1),R("Item","Shared",2),R("Blueprint","Shared",1)};
        Check(CapsuleTruthPolicy.TryMultiset(reordered,out var multiset)&&multiset.Count==3&&multiset[new("Item","Shared")]==3,"capsule pure duplicate items aggregate while reward kinds remain independent");
        Check(CapsuleTruthPolicy.SameRewards(expectedRewards,reordered),"capsule pure native reward order and duplicate grouping do not alter multiset");
        Check(CapsuleTruthPolicy.SameRewards(Array.Empty<CapsuleTruthPolicy.RewardAmount>(),Array.Empty<CapsuleTruthPolicy.RewardAmount>()),"capsule pure observed supported empty multisets can agree");
        Check(!CapsuleTruthPolicy.TryMultiset(null,out _)&&!CapsuleTruthPolicy.SameRewards(null,expectedRewards),"capsule pure absent reward observation is not an empty multiset");
        Check(!CapsuleTruthPolicy.SameRewards(new[]{R("Item","Shared",1)},new[]{R("Blueprint","Shared",1)}),"capsule pure same asset across kinds never aliases");
        Check(!CapsuleTruthPolicy.SameRewards(new[]{R("Item","Shared",1)},new[]{R("Item","shared",1)}),"capsule pure native asset identity is ordinal case sensitive");
        Check(!CapsuleTruthPolicy.SameRewards(expectedRewards,new[]{R("Blueprint","Shared",1),R("Component","Shared",1),R("Item","Shared",2)}),"capsule pure wrong reward amount mismatches");
        Check(!CapsuleTruthPolicy.SameRewards(expectedRewards,reordered.Append(R("Item","Extra",1))),"capsule pure extra reward mismatches");
        Check(!CapsuleTruthPolicy.SameRewards(expectedRewards,reordered.Where(r=>r.Kind!="Component")),"capsule pure missing component reward mismatches");
        var invalid=new[]{R(null,"Asset",1),R("item","Asset",1),R("Recipe","Asset",1),R("Item",null,1),R("Item","",1),R("Item"," ",1),R("Item","Asset",0),R("Item","Asset",-1)};
        for(int i=0;i<invalid.Length;i++)Check(!CapsuleTruthPolicy.TryMultiset(new[]{invalid[i]},out _),"capsule pure unsupported malformed or nonpositive reward rejected "+i);
        Check(!CapsuleTruthPolicy.TryMultiset(new[]{R("Item","Overflow",long.MaxValue),R("Item","Overflow",1)},out _),"capsule pure checked reward aggregation rejects overflow");
        Check(CapsuleTruthPolicy.TryMultiset(new[]{R("Item","Boundary",long.MaxValue-1),R("Item","Boundary",1)},out var max)&&max[new("Item","Boundary")]==long.MaxValue,"capsule pure exact long boundary remains valid");
        foreach(bool baked in new[]{false,true})
        foreach(bool same in new[]{false,true})
        foreach(bool observed in new[]{false,true})
        foreach(bool supported in new[]{false,true})
        {
            var status=!same||!observed||!supported?CapsuleTruthPolicy.RewardStatus.Unverifiable:!baked?CapsuleTruthPolicy.RewardStatus.Unknown:CapsuleTruthPolicy.RewardStatus.NativeMatch;
            Check(CapsuleTruthPolicy.Verification(baked,same,observed,supported,expectedRewards,reordered)==status,"capsule pure version identity observation support gate "+baked+"/"+same+"/"+observed+"/"+supported);
        }
        Check(CapsuleTruthPolicy.Verification(true,true,true,true,expectedRewards,Array.Empty<CapsuleTruthPolicy.RewardAmount>())==CapsuleTruthPolicy.RewardStatus.NativeMismatch,"capsule pure supported native mismatch is explicit");
        Check(CapsuleTruthPolicy.Verification(true,true,true,false,expectedRewards,reordered)==CapsuleTruthPolicy.RewardStatus.Unverifiable,"capsule pure unsupported extra reward cannot confirm apparent match");
        foreach(bool opened in new[]{false,true})
        foreach(bool matching in new[]{false,true})
        {
            var state=CapsuleTruthPolicy.State("pod-A","pod-A",true,!opened,opened);
            var status=CapsuleTruthPolicy.Verification(true,true,true,true,expectedRewards,matching?reordered:Array.Empty<CapsuleTruthPolicy.RewardAmount>());
            Check((state==CapsuleTruthPolicy.OpenState.Open)==opened&&CapsuleTruthPolicy.Confirmed(status)==matching,"capsule pure opening and reward verification are independent "+opened+"/"+matching);
        }
        const string fixture="""{"CatalogHash":"catalog-v1","Assets":[{"Key":"x","Kind":"Item","AssetName":"Rock"}],"Pods":[{"Id":"pod-A","Rewards":[{"Key":"x","Count":2}]}]}""";
        CapsuleDatabase Read(string hash)
        {using var stream=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fixture));return CapsuleDatabase.Read(stream,hash);}
        var database=Read("catalog-v1");
        Check(database.VersionMatched&&database.Pods.Count==1&&!CapsuleTruthPolicy.Confirmed(CapsuleTruthPolicy.RewardStatus.Baked),"capsule pure version gated baked data is not native confirmation");
        Check(CapsuleTruthPolicy.SameRewards(database.ExpectedRewards("pod-A"),new[]{R("Item","Rock",2)}),"capsule pure database adapts exact ID rewards into real policy");
        var wrongVersion=Read("catalog-v2");
        Check(!wrongVersion.VersionMatched&&wrongVersion.Pods.Count==0&&wrongVersion.Assets.Count==0,"capsule pure catalog version mismatch discards baked mapping");
        Check(!database.ExpectedRewards("pod-a").Any()&&!database.ExpectedRewards(null).Any(),"capsule pure absent or case changed ID never borrows another pod rewards");
    }

    private static void IconChecks(Action<bool,string> Check)
    {
        Check(NativeIconPolicy.CensusAttemptsPerEvent==2&&NativeIconPolicy.CensusRetrySeconds==1f,"icons pure declared bounded census window");
        foreach(float now in new[]{-1f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
        {
            var invalid=new NativeIconPolicy.CensusState{Attempts=1,NextAttempt=3};
            Check(!NativeIconPolicy.TryBeginCensus(ref invalid,now)&&invalid.Attempts==1&&invalid.NextAttempt==3,"icons pure invalid census clock leaves budget unchanged "+now);
        }
        var census=new NativeIconPolicy.CensusState();
        Check(NativeIconPolicy.TryBeginCensus(ref census,0)&&census.Attempts==1&&census.NextAttempt==1,"icons pure first census consumes exactly one attempt");
        Check(!NativeIconPolicy.TryBeginCensus(ref census,.5f)&&census.Attempts==1&&census.NextAttempt==1,"icons pure before deadline does not consume retry");
        Check(NativeIconPolicy.TryBeginCensus(ref census,1)&&census.Attempts==2&&census.NextAttempt==2,"icons pure exact deadline admits one late census");
        Check(!NativeIconPolicy.TryBeginCensus(ref census,100)&&census.Attempts==2,"icons pure idle time cannot reopen exhausted event");
        Check(!NativeIconPolicy.TryBeginCensus(ref census,.25f)&&census.Attempts==2,"icons pure rollback clock cannot reset exhausted budget");
        census=default;
        Check(NativeIconPolicy.TryBeginCensus(ref census,10)&&census.Attempts==1&&census.NextAttempt==11,"icons pure explicit event reset rearms budget");
        Check(NativeIconPolicy.TryBeginCensus(ref census,11)&&!NativeIconPolicy.TryBeginCensus(ref census,12),"icons pure rearmed event is still bounded");
        var late=new NativeIconPolicy.CensusState();bool loaded=false;int probes=0;
        NativeIconPolicy.Resolution Discover()=>NativeIconPolicy.ResolveExact("WeatherRain",name=>{probes++;return loaded&&name=="iconWeatherRain";});
        NativeIconPolicy.TryBeginCensus(ref late,0);var missing=Discover();
        Check(!missing.Resolved&&probes>0,"icons pure early missing native art remains unresolved");
        loaded=true;int before=probes;
        Check(!NativeIconPolicy.TryBeginCensus(ref late,.5f)&&probes==before,"icons pure late art causes no unbudgeted census before deadline");
        bool retry=NativeIconPolicy.TryBeginCensus(ref late,1);var discovered=retry?Discover():missing;
        Check(retry&&discovered.Resolved&&discovered.AssetName=="iconWeatherRain","icons pure late native asset resolves on admitted retry");
        before=probes;
        Check(!NativeIconPolicy.TryBeginCensus(ref late,2)&&probes==before,"icons pure no additional lookups after late retry budget");
        var roles=new (string Identity,string Role)[]{("ClearWeatherPattern","WeatherClear"),("WeatherPattern_Wind","WeatherWind"),("WindLavaPattern","WeatherLava"),("WindSunFlare","WeatherSolar"),("SolarClear","WeatherSolar"),("wind lava solar",""),("clear wind",""),("windy",""),("clearing",""),(null,""),("",""),("SUNFLARE","WeatherSolar"),("clear-sun-flare","WeatherSolar"),("lavender",""),("clear lava","WeatherLava"),("wind lava","WeatherLava")};
        for(int i=0;i<roles.Length;i++)Check(NativeIconPolicy.WeatherPresetRole(roles[i].Identity)==roles[i].Role,"icons pure exact semantic weather tokens and hazard precedence "+i);
        foreach(var state in Enum.GetValues<NativeIconPolicy.Availability>())
        {
            var resolution=new NativeIconPolicy.Resolution("WeatherWind",state,"asset","evidence");
            Check(resolution.Resolved==(state==NativeIconPolicy.Availability.Resolved)&&resolution.Declared==(state!=NativeIconPolicy.Availability.UnknownRole),"icons pure availability states do not claim resolved on missing unloaded ambiguous or budget limits "+state);
        }
        object meta=new(),small=new();bool metaLive=false;int smallReads=0,iconReads=0;
        object Select(out NativeIconPolicy.WeatherIconSource source)=>NativeIconPolicy.SelectWeatherIcon(()=>meta,()=>{smallReads++;return small;},()=>{iconReads++;return null;},o=>ReferenceEquals(o,small)||ReferenceEquals(o,meta)&&metaLive,out source);
        var first=Select(out var firstSource);
        Check(ReferenceEquals(first,small)&&firstSource==NativeIconPolicy.WeatherIconSource.PediaSmallIcon&&smallReads==1&&iconReads==0,"icons pure unloaded metadata uses live native small icon lazily");
        metaLive=true;var second=Select(out var secondSource);
        Check(ReferenceEquals(second,meta)&&secondSource==NativeIconPolicy.WeatherIconSource.MetadataIcon&&smallReads==1&&iconReads==0,"icons pure late live metadata replaces fallback without extra reads");
        metaLive=false;var third=Select(out var thirdSource);
        Check(ReferenceEquals(third,small)&&thirdSource==NativeIconPolicy.WeatherIconSource.PediaSmallIcon&&smallReads==2,"icons pure collected metadata never remains sticky resolved");
    }

    private static void LifecycleWiring(Action<bool,string> Check,string root)
    {
        string Compact(string name)=>string.Concat(File.ReadAllText(Path.Combine(root,"src","creator",name)).Where(c=>!char.IsWhiteSpace(c)));
        string Slice(string text,string begin,string end)
        {int a=text.IndexOf(begin,StringComparison.Ordinal);if(a<0)return "";int b=text.IndexOf(end,a+begin.Length,StringComparison.Ordinal);return b<0?"":text.Substring(a,b-a);}
        string capsule=Compact("CapsuleTools.cs"),icons=Compact("NativeIconTools.cs");
        string validate=Slice(capsule,"internalViewValidateLoadedPod(","internalboolMatchesLoadedPod(");
        Check(validate.Contains("CapsuleTruthPolicy.SameId(id,pod._id)")&&validate.IndexOf("SameId(",StringComparison.Ordinal)<validate.IndexOf("pod.Blueprint",StringComparison.Ordinal)&&validate.Contains("CapsuleTruthPolicy.Verification("),"capsule static wiring exact native ID gate precedes reward observation and real pure verification");
        Check(capsule.Contains("_evidence.Clear();_states.Clear();_views.Clear();EvidenceGeneration++;")&&capsule.Contains("internalvoidInvalidateScene(){ClearNativeEvidence();InvalidateLocalization();UpdateStatus();}")&&capsule.Contains("_loaded=false;_installed=null;Database=newCapsuleDatabase();"),"capsule static wiring scene and session invalidation clear evidence independently of version hash not lifecycle execution");
        string forget=Slice(capsule,"internalvoidForgetLoadedPod(","privatevoidUpdateStatus(");
        Check(forget.Contains("_evidence.Remove(id)")&&!forget.Contains("_states.Remove")&&!capsule.Contains("HasRecipe("),"capsule static wiring unloaded rewards do not overwrite observed opening or infer recipes");
        Check(capsule.Contains("_retryAt[reward.Key]=now+TextRetrySeconds;_pending.Enqueue(reward)")&&capsule.Contains("if(changed){_views.Clear();UpdateStatus();}"),"capsule static wiring late localization retries invalidate cached views not executed retry proof");
        Check(icons.Contains("if(_rebindRequested){_census=default;_rebindRequested=false;}")&&icons.Contains("Time.unscaledTime<_nextCensus||!NativeIconPolicy.TryBeginCensus(ref_census,Time.unscaledTime)")&&icons.Contains("_nextCensus=Time.unscaledTime+NativeIconPolicy.CensusRetrySeconds"),"icons static wiring coalesced events use pure attempt budget and global cooldown");
        Check(icons.Contains("!e.IsPrototype&&!e.IsRadiant")&&icons.Contains("string.Equals(e.Type.name,reward.AssetName,StringComparison.Ordinal)")&&icons.Contains("registered.Pointer!=item.Type.Pointer")&&!icons.Contains("e.Type?.prefab?.name==reward.AssetName"),"icons static wiring exact item asset registry identity replaces prefab name binding");
        string getters=Slice(icons,"internalNativeIconPolicy.ResolutionRoleInfo(","internalvoidVisitUiTextures(");
        Check(getters.Length>0&&!getters.Contains("FindObjectsOfTypeAll")&&!getters.Contains("ReadWeatherMetadata(")&&!getters.Contains("TryBeginCensus("),"icons static wiring diagnostic getters do not discover or retry assets");
        Check(icons.Contains("varweatherSnapshots=_weatherLookups.Values.Select(")&&icons.Contains("foreach(varsavedinweatherSnapshots)")&&icons.Contains("NativeIconPolicy.TryBeginWeatherLookup(reflookup.State,NativeUiPriority.FocusGeneration,Time.unscaledTime)"),"icons static wiring general rebind preserves per-pattern bounded metadata epoch not runtime acceptance");
    }

}
