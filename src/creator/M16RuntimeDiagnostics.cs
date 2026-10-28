using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEngine;
namespace CreatorToolkit;
internal static class M16RuntimeDiagnostics
{
    internal static string Snapshot(ToolkitMod app)
    {
        var text=new StringBuilder();
        void Row(string key,object value)=>text.AppendLine(key+" "+JsonSerializer.Serialize(value));
        Row("M16_SNAPSHOT",new{Utc=DateTime.UtcNow,app.Ready,app.MenuOpen,Opening=app.UiForTests.OpeningProgress,app.Map.NativeMapOpen,
            Zoom=app.Map.Ui?._viewModel?.MapZoom,
            app.UiForTests.MapProjectedForTest,app.UiForTests.MapMarkersForTest,app.UiForTests.MapCrowdedForTest,app.UiForTests.MapMarkerSizeForTest,
            Lod=QualitySettings.lodBias,Mip=QualitySettings.globalTextureMipmapLimit,Upload=QualitySettings.asyncUploadTimeSlice,
            Renderers=QualitySettings.streamingMipmapsRenderersPerFrame,IO=QualitySettings.streamingMipmapsMaxFileIORequests,
            Priority=Application.backgroundLoadingPriority.ToString(),Width=Screen.width,Height=Screen.height,app.Optimization.Active,
            app.Optimization.GeometryActive,app.Optimization.LastRestoreConflicts});
        var snapshot=app.Map.Sources.GetSnapshot(Time.unscaledTime);
        Row("M16_SOURCES",new{snapshot.CycleComplete,snapshot.Incomplete,Actual=snapshot.Actual.Count,Potential=snapshot.Potential.Count,
            ActualContainers=snapshot.Actual.Count(x=>x.Kind==WorldSourceObservationPolicy.Kind.ActualContainer),snapshot.Issues});
        foreach(var p in app.Map.Points.Where(p=>p.SourceObservation))Row("M16_SOURCE_POINT",new{p.Id,p.Category,p.Name,p.Detail,p.GroupName,
            Position=p.Position.ToString("F2"),MapConfirmed=app.Map.MapForPoint(p)!=null,Icon=p.SourceIcon?.name});
        foreach(var kind in new[]{"Горшочки","Спавнеры"})
        {
            var points=app.Map.Points.Where(p=>p.Category==kind).ToArray();
            Row("M16_SOURCE_SUMMARY",new{Category=kind,Count=points.Length,Unique=points.Select(p=>p.Id).Distinct().Count()});
        }
        return text.ToString()+M15RuntimeDiagnostics.Snapshot(app);
    }
}
