using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using UnityEngine;
namespace CreatorToolkit;
// Explicit one-owned-object smoke test; never feeds a story Gordo or calls save.
internal static class M18GordoProbe
{
    private static ToolkitMod _app;
    private static ActorTools.Entry _entry;
    private static IntPtr _session;
    private static int _stage=-1,_frame;
    private static float _deadline,_started;
    private static readonly HashSet<string> _before=new();
    private static string _created;
    internal static void Start(ToolkitMod app)
    {
        if(_stage>=0||!app.Ready||app.Map.Busy||app.Actors.OwnedCount!=0||app.Actors.GordoSpawnPending)
            throw new InvalidOperationException("Gordo smoke requires idle gameplay with no mod-owned objects.");
        _entry=app.Actors.Catalog.FirstOrDefault(e=>e.Category=="Гордо"&&e.Reference=="IdentifiableType.PinkGordo"&&e.CanSpawn);
        if(_entry==null)throw new InvalidOperationException("Native Pink Gordo unavailable.");
        _app=app;_session=app.Session;_before.Clear();_created=null;
        foreach(var pair in app.Context.GameModel.identifiables)
            if(pair.Value?.TryCast<SnareModel>()!=null)_before.Add(pair.Value.actorId.Value.ToString());
        _stage=0;_started=Time.realtimeSinceStartup;_deadline=Time.unscaledTime+12f;
    }
    internal static void Tick()
    {
        if(_stage<0)return;
        try
        {
            if(!_app.Ready||_app.Session!=_session)throw new InvalidOperationException("Gordo smoke session changed.");
            if(Time.unscaledTime>_deadline)throw new InvalidOperationException("Gordo smoke timed out.");
            if(_stage==0){_app.Actors.Spawn(_entry,1);_stage=1;_frame=Time.frameCount;return;}
            if(_stage==1)
            {
                if(_app.Actors.GordoSpawnPending||Time.frameCount<_frame+3)return;
                if(_app.Actors.OwnedCount!=1)throw new InvalidOperationException(_app.Actors.GordoSpawnStatus);
                foreach(var pair in _app.Context.GameModel.identifiables)
                {
                    var model=pair.Value?.TryCast<SnareModel>();if(model==null)continue;
                    string id=model.actorId.Value.ToString();if(_before.Contains(id))continue;
                    if(model.gordoTypeId!=_entry.Type||!model.HasSnaredGordo())
                        throw new InvalidOperationException("Created snare did not confirm exact Pink Gordo/model.");
                    _created=id;break;
                }
                if(_created==null)throw new InvalidOperationException("Created native SnareModel not in GameModel.");
                _app.Actors.Cleanup();_frame=Time.frameCount;_stage=2;return;
            }
            if(Time.frameCount<_frame+4)return;
            if(_app.Actors.OwnedCount!=0)throw new InvalidOperationException("Gordo ownership did not clear.");
            foreach(var pair in _app.Context.GameModel.identifiables)
                if(pair.Value?.TryCast<SnareModel>()!=null&&pair.Value.actorId.Value.ToString()==_created)
                    throw new InvalidOperationException("Owned snare remained registered after cleanup.");
            Finish(true,null);
        }
        catch(Exception e)
        {
            try{if(_app.Ready&&_app.Session==_session&&!_app.Actors.GordoSpawnPending&&_app.Actors.OwnedCount>0)_app.Actors.Cleanup();}catch(Exception cleanup){e=new AggregateException(e,cleanup);}
            Finish(false,e.ToString());
        }
    }
    private static void Finish(bool success,string error)
    {
        File.WriteAllText(Path.Combine(_app.DataDirectory,"m18-gordo-proof.json"),JsonSerializer.Serialize(new{Success=success,Error=error,Elapsed=Time.realtimeSinceStartup-_started,CreatedNativeSnareId=_created,OwnedAfter=_app.Actors.OwnedCount,Scope="One native Pink Gordo spawn and cleanup; no feeding, other species, save calls or story Gordo mutation"}));
        _stage=-1;_app=null;_entry=null;_before.Clear();
    }
}
