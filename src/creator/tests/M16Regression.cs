using System;
using System.Linq;
using System.IO;
using CreatorToolkit;
internal static class M16Regression
{
    internal static void Run(Action<bool,string> report,string root)
    {
        void Check(bool ok,string name)=>report(ok,"m15 / followup / "+name);
        Check(UiThemePolicy.OpeningProgress(-1)==0&&UiThemePolicy.OpeningProgress(0)==0,"entry starts at rest without negative motion");
        Check(UiThemePolicy.OpeningProgress(.09f)>0&&UiThemePolicy.OpeningProgress(.09f)<1,"entry has intermediate presentation");
        Check(UiThemePolicy.OpeningProgress(.18f)==1&&UiThemePolicy.OpeningProgress(10)==1,"entry finishes in 180 unscaled ms");
        Check(UiThemePolicy.OpeningProgress(float.NaN)==1,"invalid entry clock fails to usable settled state");
        float previous=0;bool monotonic=true;
        for(int i=0;i<100;i++){float v=UiThemePolicy.OpeningProgress(i*.002f);monotonic&=v>=previous&&v<=1;previous=v;}
        Check(monotonic,"entry is bounded and monotonic");
        Check(MapOverlayLayout.MinSize>=14f&&MapOverlayLayout.MaxSize<=32f&&MapOverlayLayout.DefaultSize>=MapOverlayLayout.MinSize&&MapOverlayLayout.DefaultSize<=MapOverlayLayout.MaxSize,"icon size stays inside one fixed readable range on every zoom level");
        var coincident = MapOverlayLayout.Place(new[] { (100f, 100f, 0), (100f, 100f, 1), (100f, 100f, 2) }, MapOverlayLayout.DefaultSize);
        Check(coincident.Count == 3 && coincident.Select(m => m.Item).OrderBy(i => i).SequenceEqual(new[] { 0, 1, 2 }),"coincident objects always stay separate markers");
        Check(coincident.All(m => m.X == 100f && m.Y == 100f),"coincident markers keep the exact same true position instead of being pushed apart");
        var points=Enumerable.Range(0,90).Select(i=>(100f,100f,i)).ToArray();
        var separated=MapOverlayLayout.Place(points,MapOverlayLayout.DefaultSize);var again=MapOverlayLayout.Place(points,MapOverlayLayout.DefaultSize);
        Check(separated.Count==90,"all 90 coincident physical points remain free separate markers");
        Check(separated.Select(m=>m.Item).Order().SequenceEqual(Enumerable.Range(0,90)),"placement loses or duplicates no identities");
        Check(separated.All(m=>m.X==100&&m.Y==100),"no marker is ever displaced from its projected position");
        Check(separated.All(m=>m.Size==MapOverlayLayout.DefaultSize),"identical coordinates never change the fixed icon size");
        Check(separated.Count(m=>m.Crowded)==89,"every stacked icon is honestly flagged as overlapping");
        Check(separated.Zip(again).All(p=>p.First.X==p.Second.X&&p.First.Y==p.Second.Y&&p.First.Size==p.Second.Size),"placement deterministic without frame-dependent jitter");
        var solo=MapOverlayLayout.Place(new[]{(123f,456f,1)},MapOverlayLayout.DefaultSize).Single();
        Check(solo.X==123&&solo.Y==456,"isolated marker never moves or snaps to an invented position");
        Check(MapOverlayLayout.Place(new[]{(float.NaN,10f,1),(10f,20f,2)},MapOverlayLayout.DefaultSize).Single().Item==2,"invalid projection rejected");
        for(int bits=0;bits<64;bits++){bool B(int i)=>(bits&(1<<i))!=0;Check(WorldSourceObservationPolicy.ActualContainerAllowed(B(0),B(1),B(2),B(3),B(4),B(5))==(bits==63),"actual regular container evidence gate "+bits);}
        Check(WorldSourceObservationPolicy.PhysicalPointKey(WorldSourceObservationPolicy.Kind.PotentialGold,"source-a","loc-1")==WorldSourceObservationPolicy.PhysicalPointKey(WorldSourceObservationPolicy.Kind.PotentialContainer,"source-a","loc-1"),"regular and golden alternatives share one physical crate source");
        Check(WorldSourceObservationPolicy.PhysicalPointKey(WorldSourceObservationPolicy.Kind.ActualContainer,"source-a","loc-1")!=WorldSourceObservationPolicy.PhysicalPointKey(WorldSourceObservationPolicy.Kind.PotentialContainer,"source-a","loc-1"),"live pot and potential location remain explicitly different facts");
        string Read(string n)=>File.ReadAllText(Path.Combine(root,"src","creator",n));
        string ui=Read("ToolkitUi.cs"),input=Read("InputCapture.cs"),sources=Read("WorldSourceObservations.cs"),map=Read("MapTools.cs"),opt=Read("OptimizationUi.cs"),tools=Read("OptimizationTools.cs");
        Check(ui.Contains("DrawRoundedSurface(track,_sliderTrackPaint")&&ui.Contains("DrawRoundedSurface(filled,_sliderFillPaint")&&!ui.Contains("GUI.Box(track,GUIContent.none,_trackStyle)"),"thin slider uses non-overlapping capsule slices instead of oversized 20px native borders");
        Check(input.Contains("BeginOpenAnimation()")&&ui.Contains("24f*(1f-entrance)")&&ui.Contains("OpeningProgress>=1f"),"every real Insert/open path animates fixed window geometry and gates input until settled");
        Check(ui.Contains("152+(i/filterColumns)*44")&&ui.Contains("MapLayerOrder.FilterWidth(area.width,filterLayers.Length)"),"every native map layer label (incl. food and plort layers) fits two filter rows");
        {
            string fandom=Read("FandomMarkers.tsv"),index=Read("MapIndex.tsv"),progress=Read("MapProgress.cs"),mini=Read("MiniMapUi.cs");
            string catalog=index.Split('\n')[0].Trim();
            Check(fandom.StartsWith(catalog+" exact=1",StringComparison.Ordinal),"exact extra map layers are gated by the same Addressables catalog as MapIndex");
            var rows=fandom.Split('\n').Where(l=>l.StartsWith("M\t",StringComparison.Ordinal)).Select(l=>l.TrimEnd('\r').Split('\t')).ToArray();
            Check(rows.Length>900&&rows.All(c=>c.Length>=14),"exact extra layer rows carry refs, state key, filter kind and exact flag");
            Check(rows.Count(c=>c[13]=="1")>=rows.Length-12,"only the few wiki-only markers (decor, unsnapped slimes) remain estimates");
            Check(rows.Select(c=>c[2]+"|"+c[1]).Distinct().Count()==rows.Length,"exact extra layer ids are unique per layer");
            Check(map.Contains("if(entry.Exact&&!catalogMatches){skipped++;continue;}"),"a changed game build disables exact baked layers instead of showing stale positions");
            Check(progress.Contains("map-marks-")&&progress.Contains("File.Move(temporary,_marksFile,true)")&&!progress.Contains("SaveGame("),"done marks are atomic per-save files beside mod settings, never written into the game save");
            Check(progress.Contains("model.AllSlots()")&&progress.Contains("slot.filled")&&map.Contains("StateDone(point)"),"plort statue/door progress reads native puzzle slot state");
            Check(mini.Contains("Event.current.type!=EventType.Repaint")&&mini.Contains("_nextMiniMap=Time.unscaledTime+.5f")&&mini.Contains("MiniMapPoints.Count>80"),"minimap is repaint-only, refreshed twice a second and bounded");
        }
        Check(ui.Contains("DetailedHelp?\"Скрыть подсказки\":\"Подсказки\"")||ui.Contains("Подсказки"),"explanatory player copy is user-expandable");
        Check(sources.Contains("type.prefab.GetComponent<BreakOnImpactBase>()")&&sources.Contains("containerTypes[type.ReferenceId]"),"regular pots come from exact native zone crate tables and container-prefab evidence");
        Check(sources.Contains("if(finished)")&&map.Contains("GroupBy(o=>"),"source publications atomic and alternatives aggregate by physical source/location");
        Check(map.Contains("Место появления, не живой объект")&&map.Contains("Условия появления не вычислялись"),"source UI does not invent current spawn eligibility or fake live objects");
        Check(!map.Contains("if(!observation.SourceActive||!observation.SourceEnabled)continue;")&&map.Contains("o.Weight>0"),"director-registered disabled behaviors retain source locations; zero-weight options are not presented as possible");
        Check(sources.Contains("FindObjectsOfType<BreakOnImpactBase>()")&&sources.Contains("ReadActual(lookup,type,actorModel)"),"live canonical container components supplement exact actor model identities without inventing spawns");
        Check(Read("M15SliderProbe.cs").Contains("visible intermediate opening frame")&&Read("M15SliderProbe.cs").Contains("entrance produces intermediate frames"),"native probe verifies real menu-open animation before measuring sliders");
        Check(opt.Contains("_optRevision!=tools.UiRevision")&&tools.Contains("UiRevision++"),"Potato values refresh after external IPC apply/restore without overwriting an in-progress drag");
        Check(ui.Contains("только загруженные сцены"),"source result count states its loaded-world scope rather than implying a global static census");
        Check(!opt.Contains("Slider(71")&&!opt.Contains("Slider(72")&&opt.Contains("tools.GeometrySupported"),"unsupported HDRP and vegetation controls removed from main screen");
        Check(tools.Contains("Apply(OptimizationRequest.Potato with{ParticleMax=256});\n            ApplyGeometryLow();")&&tools.Contains("catch{Restore(false);throw;}"),"one-click potato includes verified scalar LOD with failure rollback");
    }
}
