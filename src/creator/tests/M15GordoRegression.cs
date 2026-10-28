using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CreatorToolkit;

internal static class M15GordoRegression
{
    internal static void Run(Action<bool,string> report,string root)
    {
        void Check(bool v,string name)=>report(v,"m15 / gordo / "+name);
        Check(GordoMapPolicy.Key("gordo123")=="gordo123","persistent ID is retained verbatim independent of species and scene");
        Check(GordoMapPolicy.Key(null)==null&&GordoMapPolicy.Key("")==null&&GordoMapPolicy.Key("  ")==null,"missing identities are not merged into one fake physical gordo");
        Check(GordoMapPolicy.Key(new string('x',513))==null,"oversized native ID rejected without truncation collision");
        Check(GordoMapPolicy.Observe(false,0,50,-1)==GordoMapPolicy.State.Unknown,"missing model does not imply hungry or burst");
        Check(GordoMapPolicy.Observe(true,0,50,-1)==GordoMapPolicy.State.Hungry,"explicit zero meal count is unburst");
        Check(GordoMapPolicy.Observe(true,49,50,-1)==GordoMapPolicy.State.Hungry,"part-fed instance is not falsely marked complete");
        Check(GordoMapPolicy.Observe(true,50,50,-1)==GordoMapPolicy.State.Fed,"native meal target reached is fed not inferred burst");
        Check(GordoMapPolicy.Observe(true,55,50,-1)==GordoMapPolicy.State.Fed,"over-target pending animation remains fed");
        Check(GordoMapPolicy.Observe(true,-1,50,-1)==GordoMapPolicy.State.Burst,"native ALREADY_BURST_FLAG equality is the only burst proof");
        Check(GordoMapPolicy.Observe(true,-2,50,-1)==GordoMapPolicy.State.Unknown,"other negative counters remain unknown");
        Check(GordoMapPolicy.Observe(true,0,0,-1)==GordoMapPolicy.State.Hungry,"unloaded target zero is not instant fed completion");
        foreach(int count in new[]{-1,0,49,50})
            Check(GordoMapPolicy.Observe(true,count,50,-1,true)==GordoMapPolicy.State.Unknown,"identity conflict overrides apparently valid state "+count);
        Check(GordoMapPolicy.Merge(1,1,true,true)==GordoMapPolicy.Duplicate.Keep,"exact index/runtime duplicate remains one physical row");
        Check(GordoMapPolicy.Merge(1,2,true,false)==GordoMapPolicy.Duplicate.Replace,"authoritative native instance can update baked coordinates");
        Check(GordoMapPolicy.Merge(2,1,true,false)==GordoMapPolicy.Duplicate.Keep,"baked source cannot overwrite authoritative native coordinates");
        Check(GordoMapPolicy.Merge(1,1,false,true)==GordoMapPolicy.Duplicate.Conflict,"same persistent ID in different groups is a conflict not two instances");
        Check(GordoMapPolicy.Merge(1,1,true,false)==GordoMapPolicy.Duplicate.Conflict,"same persistent ID at incompatible positions fails closed");
        var canonical=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"Assets/Scene/zoneLive.unity"};
        Check(GordoMapPolicy.SceneAllowed("Assets/Scene/zoneLive.unity",canonical,true),"exact canonical scene membership retains intended instance");
        Check(!GordoMapPolicy.SceneAllowed("Assets/Scene/zoneLegacy.unity",canonical,true),"unreferenced legacy scene excluded despite sharing current bundle");
        Check(GordoMapPolicy.SceneAllowed("Assets/Scene/zoneLegacy.unity",canonical,false),"incomplete Addressables census cannot falsely delete unseen native content");
        Check(GordoMapPolicy.SceneAllowed(null,canonical,true),"old index without provenance is unknown rather than fabricated membership");
        var ids=new HashSet<string>(StringComparer.Ordinal){"gordo123"};
        Check(!GordoMapPolicy.DrawOverlay("gordo123",ids),"visible native UI marker suppresses second mod marker for exact same ID");
        Check(GordoMapPolicy.DrawOverlay("gordo124",ids),"another physical gordo of same species is never deduped by type");
        Check(GordoMapPolicy.DrawOverlay("gordo123",new HashSet<string>()),"inactive pooled native UI does not suppress mod marker");
        var gordoMarkers=MapOverlayLayout.Place(new[]{(10f,10f,"Cotton"),(10f,10f,"Pink"),(12f,12f,"Pods")},MapOverlayLayout.DefaultSize);
        Check(gordoMarkers.Count==3&&gordoMarkers.Select(m=>m.Item).OrderBy(x=>x).SequenceEqual(new[]{"Cotton","Pink","Pods"}),"different Gordo species and a nearby capsule stay three separate markers");
        Check(gordoMarkers.Zip(new[]{(10f,10f),(10f,10f),(12f,12f)}).All(p=>p.First.X==p.Second.Item1&&p.First.Y==p.Second.Item2),"a crowded Gordo keeps its exact map position instead of a pushed-apart icon");
        string ui=File.ReadAllText(Path.Combine(root,"src","creator","ToolkitUi.cs"));
        Check(!ui.Contains("HasSingleKind")&&ui.Contains("var nativeIcon=map.Icons.Point(point)"),"every map object wears its own species/category icon instead of a shared group icon");
        Check(!ui.Contains("Группа разных объектов")&&!ui.Contains("рядом: "),"no map marker is presented as a merged multi-object group");
        Check(ui.Contains("map.Icons.Category(point.Category)??map.Icons.Role(\"Atlas\")"),"an object without its own sprite falls back to category artwork instead of vanishing");
        Check(ui.Contains("MapMarkersForTest=markers.Count")&&ui.Contains("if(point.Category==\"Капсулы\")hoveredCapsule=point;"),"map publishes one marker per object and keeps the capsule hover card on its own marker");
        string map=File.ReadAllText(Path.Combine(root,"src","creator","MapTools.cs"));
        string native=File.ReadAllText(Path.Combine(root,"src","creator","MapGordoTools.cs"));
        Check(map.Contains("BuildGordoSceneMembership();")&&native.Contains("locator.Locate(reference.RuntimeKey,null,out var locations)"),"production membership resolves current native SceneGroup references without loading scenes");
        Check(native.Contains("nativeLocators[i]?.Locator")&&native.Contains("locations[i]")&&!native.Contains("NativeEnumerable.Items"),"production Addressables census avoids broken boxed IL2CPP collection enumerators");
        Check(native.Contains("_gordoPoints.TryGetValue(id,out var existing)")&&native.Contains("GordoMapPolicy.Merge"),"production index dedups exact IDs through tested merge policy");
        Check(native.Contains("existing.Position=position")&&!native.Contains("GroupBy(p=>p.Name"),"native actor supplementation updates a row instead of grouping away valid species instances");
        Check(map.Contains("RefreshOpenedPods();RefreshGordos();")&&map.Contains("DrawGordoOverlay(p)"),"half-second state refresh and native-overlay exclusion are wired into real map");
        Check(native.Contains("GordoEat.ALREADY_BURST_FLAG")&&native.Contains("model.gordoEatCount")&&!native.Contains("InitializeGordoModel")&&!native.Contains("DestroyGordoModel"),"gordo status reads actual model and native burst flag without mutation");
        Check(native.Contains("gameObject.activeInHierarchy")&&native.Contains("image.enabled")&&native.Contains("image.color.a<=0"),"only currently rendered native marker candidates suppress overlays");
        string[] index=File.ReadAllLines(Path.Combine(root,"src","creator","MapIndex.tsv"));
        var gordos=index.Skip(1).Select(x=>x.Split('\t')).Where(x=>x.Length>1&&x[1]=="Гордо").ToArray();
        Check(gordos.Length==24&&gordos.All(x=>x.Length>=11&&x[9].StartsWith("Assets/Scene/")&&int.TryParse(x[10],out int target)&&target>0),"every authored gordo row includes exact bundle scene provenance and native feeding target");
        Check(gordos.Select(x=>x[0]).Distinct(StringComparer.Ordinal).Count()==gordos.Length,"versioned authored index contains unique physical persistent IDs");
        Check(gordos.Count(x=>x[9] is "Assets/Scene/zoneLabyrinth1.unity" or "Assets/Scene/zoneLabyrinth2.unity" or "Assets/Scene/zoneRainbowCore.unity")==7,"seven legacy-source candidates are explicitly provenance-tagged, not silently merged by species");
        Check(native.Contains("ResetGordos()")&&map.Split("ResetGordos();").Length>=3,"scene and session changes discard stale identity state and native-marker presence");
    }
}
