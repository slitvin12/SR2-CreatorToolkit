using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.UI.Map;
using UnityEngine;

namespace CreatorToolkit;

// Explicit main-thread read-only evidence. Never runs from OnGUI or auto-save.
internal static class M15RuntimeDiagnostics
{
    internal static string Snapshot(ToolkitMod app)
    {
        var text=new StringBuilder();
        void Row(string kind,object row)=>text.AppendLine(kind+" "+JsonSerializer.Serialize(row));
        Row("M15_SNAPSHOT",new{Utc=DateTime.UtcNow,app.Ready,Session=app.Session.ToInt64(),NativeMapOpen=app.Map.NativeMapOpen});
        if(!app.Ready)return text.ToString();
        try
        {
            int flag=GordoEat.ALREADY_BURST_FLAG;
            Row("GORDO_NATIVE_CONSTANT",new{AlreadyBurstFlag=flag});
            var models=app.Context.GameModel.AllGordos();
            int emitted=0;
            foreach(var pair in models)
            {
                if(emitted++>=256){text.AppendLine("GORDO_MODEL_TRUNCATED true");break;}
                var model=pair.Value;
                Row("GORDO_MODEL",new{Id=pair.Key,Present=model!=null&&!model.WasCollected,
                    Count=model?.gordoEatCount,Target=model?.targetCount,Seen=model?.gordoSeen,
                    Type=model?.identifiableType?.ReferenceId,HasGameObject=model?.gameObj!=null});
            }
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<GordoEat>())
            {
                if(actor==null||actor.WasCollected)continue;
                var go=actor.gameObject;
                Row("GORDO_ACTOR",new{actor.Id,Name=go.name,Scene=go.scene.name,Active=go.activeInHierarchy,
                    Position=actor.transform.position.ToString("F3"),Target=actor.TargetCount,
                    Slime=actor.SlimeDefinition?.ReferenceId,Count=actor.GordoModel?.gordoEatCount,
                    ModelPointer=actor.GordoModel?.Pointer.ToInt64()});
            }
            foreach(var marker in Resources.FindObjectsOfTypeAll<GordoMarkerUI>())
            {
                if(marker==null||marker.WasCollected)continue;
                var go=marker.gameObject;
                Row("GORDO_NATIVE_MARKER",new{Id=marker._gordoIdentifier,Name=go.name,Scene=go.scene.name,
                    Active=go.activeInHierarchy,SceneValid=go.scene.IsValid(),SceneLoaded=go.scene.isLoaded,
                    Type=marker._identifiableType?.ReferenceId,ImageEnabled=marker._iconImage?.enabled});
            }
            Row("GORDO_INDEX_SUMMARY",new{app.Map.GordoPointCount,app.Map.GordoDuplicateRows,app.Map.GordoConflicts,app.Map.GordoNativeVisible});
            foreach(string rejection in app.Map.GordoRejected)Row("GORDO_REJECTED_SCENE",new{Reason=rejection});
            foreach(var point in app.Map.Points.Where(p=>p.Category=="Гордо"))
                Row("GORDO_INDEX",new{point.Id,point.Name,point.GroupName,Position=point.Position.ToString("F3"),point.SourceScene,point.GordoTarget,State=app.Map.GordoState(point).ToString(),StateLabel=app.Map.GordoStateLabel(point)});
            foreach(string role in new[]{"WeatherClear","WeatherWind","WeatherLava","WeatherSolar"})
                Row("WEATHER_ROLE",new{Role=role,Diagnostic=app.Map.Icons.RoleDiagnostic(role)});
            var ui=app.UiForTests;
            Row("SLIDER_ALLOCATIONS",new{Calls=ui.SliderMeasuredCallsForTest,Bytes=ui.SliderAllocatedBytesForTest,
                Scope="Measured entire production Slider adapter, including Unity/IL2CPP wrapper calls, not all OnGUI",ui.SliderDragForTest});
        }
        catch(Exception e){Row("M15_READ_ERROR",new{Type=e.GetType().Name,e.Message});}
        return text.ToString();
    }
}
