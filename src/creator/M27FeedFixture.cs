using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.Slime;
using UnityEngine;

namespace CreatorToolkit;

// M27 acceptance fixture for the live autofeed check. Reached only through
// M27AcceptanceIpc (opt-in file); "spawn" and "hungry" change the world and are
// additionally limited to the dedicated test save there. "state" is read-only.
internal static class M27FeedFixture
{
    private const float NearRadius=9f;
    private static readonly CultureInfo CI=CultureInfo.InvariantCulture;

    private static List<LandPlot> Corrals(ToolkitMod app)
    {
        var list=new List<LandPlot>();
        var plots=app.Context?.GameModel?.landPlots;
        if(plots==null)return list;
        foreach(var pair in plots)
        {
            var model=pair.Value;
            if(model==null||model.typeId!=LandPlot.Id.CORRAL)continue;
            var root=model.gameObj;
            if(root==null||root.WasCollected||!root.activeInHierarchy)continue;
            var plot=root.GetComponentInChildren<LandPlot>(true);
            if(plot!=null&&!plot.WasCollected&&plot.GetPlotId()==LandPlot.Id.CORRAL)list.Add(plot);
        }
        return list;
    }
    private static Vector3 PlayerPosition(ToolkitMod app)
    {
        var motor=app.Player?.Controller?.Motor;
        return motor!=null?motor.TransientPosition:Vector3.zero;
    }
    private static LandPlot Nearest(ToolkitMod app)
    {
        LandPlot best=null;float bestDistance=float.MaxValue;var origin=PlayerPosition(app);
        foreach(var plot in Corrals(app))
        {
            float d=(plot.transform.position-origin).sqrMagnitude;
            if(d<bestDistance){bestDistance=d;best=plot;}
        }
        return best??throw new InvalidOperationException("no loaded corral");
    }
    private static string Where(ToolkitMod app,LandPlot plot)=>plot.transform.position.ToString("F1")+
        " dist="+Vector3.Distance(plot.transform.position,PlayerPosition(app)).ToString("0.#",CI);

    // Same tracker walk as CorralAutoFeedTools.Feed.
    private static List<SlimeEat> Slimes(ToolkitMod app)
    {
        var result=new List<SlimeEat>();var seen=new HashSet<IntPtr>();
        var types=new Il2CppSystem.Collections.Generic.List<IdentifiableType>();
        var members=new Il2CppSystem.Collections.Generic.List<Identifiable>();
        foreach(var plot in Corrals(app))
        {
            var trackers=plot._trackers;
            if(trackers==null)continue;
            for(int t=0;t<trackers.Length;t++)
            {
                var tracker=trackers[t];
                if(tracker==null||tracker.WasCollected)continue;
                types.Clear();
                tracker.GetTrackedIdentifiableTypes(types);
                for(int k=0;k<types.Count;k++)
                {
                    var type=types[k];
                    if(type==null||type.TryCast<SlimeDefinition>()==null)continue;
                    members.Clear();
                    tracker.GetTrackedItemsOfType(type,members);
                    for(int m=0;m<members.Count;m++)
                    {
                        var ident=members[m];
                        if(ident==null||ident.WasCollected)continue;
                        var eater=ident.GetComponent<SlimeEat>();
                        if(eater!=null&&!eater.WasCollected&&eater._emotions!=null&&seen.Add(eater.Pointer))result.Add(eater);
                    }
                }
            }
        }
        return result;
    }

    internal static string Spawn(ToolkitMod app,int count)
    {
        var plot=Nearest(app);
        ActorTools.Entry chosen=null,fallback=null;
        foreach(var entry in app.Actors.Catalog)
        {
            if(entry==null||!entry.CanSpawn||entry.IsRadiant||entry.IsPrototype)continue;
            var definition=entry.Type.TryCast<SlimeDefinition>();
            if(definition==null||definition.IsLargo)continue;
            if(entry.Type.name=="Pink"){chosen=entry;break;}
            fallback??=entry;
        }
        chosen??=fallback??throw new InvalidOperationException("no spawnable slime in the creator catalog");
        var player=app.Context.Player.GetComponentInChildren<TeleportablePlayer>(true);
        var group=plot._region?.SceneGroup??player.SceneGroup;
        var center=plot.transform.position;
        int made=0;
        for(int i=0;i<count;i++)
        {
            float angle=i*(Mathf.PI*2f/count);
            var position=center+new Vector3(Mathf.Cos(angle)*1.6f,1.2f,Mathf.Sin(angle)*1.6f);
            var model=app.Context.GameModel.InstantiateActorModel(chosen.Type,group,position,Quaternion.identity);
            if(model==null)continue;
            var go=model.GetGameObject()??InstantiationHelpers.InstantiateActorFromModel(model);
            if(go!=null)made++;
        }
        return "spawned "+made+"/"+count+" '"+chosen.Name+"' ("+chosen.Type.name+") in corral "+Where(app,plot);
    }

    internal static string Hungry(ToolkitMod app)
    {
        float cutoff=SlimeEmotions.HUNGRY_CUTOFF,starving=SlimeEmotions.STARVING_CUTOFF;
        float value=Mathf.Clamp(cutoff+.2f,cutoff+.02f,Mathf.Max(cutoff+.02f,starving-.02f));
        int set=0;
        foreach(var eater in Slimes(app)){eater._emotions.Set(SlimeEmotions.Emotion.HUNGER,value);set++;}
        return "hunger "+value.ToString("0.###",CI)+" set on "+set+" corral slimes (HUNGRY_CUTOFF="+cutoff.ToString("0.###",CI)+
            " STARVING_CUTOFF="+starving.ToString("0.###",CI)+")";
    }

    internal static string State(ToolkitMod app)
    {
        var slimes=Slimes(app);
        float cutoff=SlimeEmotions.HUNGRY_CUTOFF,sum=0;int hungry=0;
        foreach(var eater in slimes){float h=eater._emotions.Get(SlimeEmotions.Emotion.HUNGER);sum+=h;if(h>=cutoff)hungry++;}
        var plot=Nearest(app);
        var categories=new Dictionary<IntPtr,string>();
        foreach(var entry in app.Actors.Catalog)
            if(entry?.Type!=null&&!entry.IsRadiant&&!string.IsNullOrEmpty(entry.Category))categories[entry.Type.Pointer]=entry.Category;
        var counts=new SortedDictionary<string,int>(StringComparer.Ordinal);
        var seen=new HashSet<IntPtr>();
        var hits=Physics.OverlapSphere(plot.transform.position,NearRadius,~0,QueryTriggerInteraction.Ignore);
        for(int i=0;hits!=null&&i<hits.Length;i++)
        {
            var collider=hits[i];
            if(collider==null)continue;
            var ident=collider.GetComponentInParent<Identifiable>();
            if(ident==null||ident.identType==null||!seen.Add(ident.Pointer))continue;
            var type=ident.identType;
            string key=type.TryCast<SlimeDefinition>()!=null?"slime":categories.TryGetValue(type.Pointer,out var category)?category:"other";
            counts[key]=counts.TryGetValue(key,out int n)?n+1:1;
        }
        return "corrals="+Corrals(app).Count+" trackedSlimes="+slimes.Count+" hungry="+hungry+
            " avgHunger="+(slimes.Count>0?sum/slimes.Count:0f).ToString("0.###",CI)+" cutoff="+cutoff.ToString("0.###",CI)+
            " nearest="+Where(app,plot)+" near"+NearRadius.ToString("0",CI)+"m["+string.Join(" ",counts.Select(p=>p.Key+"="+p.Value))+"]";
    }
}
