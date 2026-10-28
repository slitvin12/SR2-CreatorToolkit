using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Aurora;
using UnityEngine;

namespace CreatorToolkit;

// Opt-in observatory for actual loaded native phenomena. DTOs retain primitive
// tokens/text/coordinates only, never an AuroraObject, collider or Rigidbody.
internal sealed class PhenomenaTools
{
    internal const int MaxRows=48,MaxExamined=256;
    internal sealed class Row
    {
        internal string Id,Name,Kind,State;
        internal IntPtr Token,Session;
        internal Vector3 Position;
        internal float StartHour,EndHour;
        internal bool CanWait;
    }
    private readonly ToolkitMod _app;
    internal readonly List<Row> Rows=new();
    internal bool Inspect {get;private set;}
    private float _next;
    internal int Examined {get;private set;}
    internal int NativeActorRoots=>0;
    internal string Status {get;private set;}="Исследование феноменов выключено";
    internal PhenomenaTools(ToolkitMod app)=>_app=app;
    internal void SetInspect(bool enabled)
    {
        Inspect=enabled;
        if(enabled)Refresh();
        else{Rows.Clear();Examined=0;Status="Исследование феноменов выключено";}
    }
    private static bool ActiveHours(AuroraObject source,out float start,out float end)=>
        PhenomenaWindowPolicy.ActiveHours(source._triggerStartHour,source._triggerEndHour,source._triggerState.ToString(),out start,out end);
    private static bool Loaded(Component obj)=>obj!=null&&!obj.WasCollected&&
        obj.gameObject.scene.IsValid()&&obj.gameObject.scene.isLoaded;
    internal void Tick()
    {
        if(Inspect&&_app.MenuOpen&&_app.Ready&&!_app.Map.Busy&&Time.unscaledTime>=_next)Refresh();
    }
    internal void Refresh()
    {
        Rows.Clear();Examined=0;_next=Time.unscaledTime+2f;
        if(!_app.Ready||_app.Map.Busy){Status="Дождитесь окончания загрузки сцены";return;}
        var player=_app.Player.Controller.Motor.TransientPosition;
        var rows=new List<Row>();
        foreach(var aurora in UnityEngine.Object.FindObjectsOfType<AuroraObject>(true))
        {
            if(Examined>=MaxExamined)break;Examined++;
            if(!Loaded(aurora)||!aurora._isSetup)continue;
            var position=aurora.transform.position;
            if(!ExplorerPolicy.Coordinates(position.x,position.y,position.z))continue;
            bool hours=ActiveHours(aurora,out float start,out float end);
            rows.Add(new Row{Id="aurora:"+aurora.Pointer,Name=aurora.gameObject.name,Kind="Aurora",
                State=aurora._currState.ToString(),Token=aurora.Pointer,Session=_app.Session,Position=position,
                StartHour=start,EndHour=end,CanWait=aurora.isActiveAndEnabled&&hours});
        }
        foreach(var zone in UnityEngine.Object.FindObjectsOfType<ReverseGravityZone>(true))
        {
            if(Examined>=MaxExamined)break;Examined++;
            if(!Loaded(zone))continue;
            var position=zone.transform.position;
            if(!ExplorerPolicy.Coordinates(position.x,position.y,position.z))continue;
            rows.Add(new Row{Id="gravity:"+zone.Pointer,Name=zone.gameObject.name,Kind="Локальная антигравитация",
                State=zone.GetOperating()?"Работает":"Выключена",Token=zone.Pointer,Session=_app.Session,Position=position});
        }
        Rows.AddRange(rows.OrderBy(r=>(r.Position-player).sqrMagnitude).Take(MaxRows));
        Status=Rows.Count==0?"В загруженной сцене нативные Aurora / ReverseGravityZone не обнаружены":
            "Нативных объектов: "+Rows.Count+" · исследование не включает и не создаёт эффекты";
    }
    private void Guard(Row row)
    {
        if(!_app.Ready||_app.Map.Busy||row==null||row.Session!=_app.Session||
            !Rows.Any(r=>r.Id==row.Id&&r.Token==row.Token&&r.Session==row.Session))
            throw new InvalidOperationException("Феномен относится к другой сцене; обновите исследование.");
    }
    private AuroraObject ResolveAurora(Row row)
    {
        Guard(row);
        var source=UnityEngine.Object.FindObjectsOfType<AuroraObject>(true)
            .FirstOrDefault(a=>Loaded(a)&&a.Pointer==row.Token&&a._isSetup&&a.isActiveAndEnabled);
        if(source==null||!ActiveHours(source,out _,out _))
            throw new InvalidOperationException("Нативное окно Aurora больше недоступно.");
        return source;
    }
    internal void RequestWait(Row row)
    {
        var source=ResolveAurora(row);
        ActiveHours(source,out float start,out _);
        float hour=PhenomenaWindowPolicy.After(start);
        _app.Confirm("Дождаться твёрдой фазы Aurora?",
            "Игровое время продвинется только вперёд к "+hour.ToString("F2")+" ч. "+
            "Игра сама управляет своим переходом, звуком и коллизиями. Объекты, шейдеры и локальная гравитация вручную не меняются.",
            ()=>{
                var current=ResolveAurora(row);
                ActiveHours(current,out float activeStart,out _);
                _app.World.SetHour(PhenomenaWindowPolicy.After(activeStart));
                _app.CloseMenu();Refresh();
            });
    }
    internal void RequestTravel(Row row)
    {
        Guard(row);
        Vector3 position;
        if(row.Kind=="Aurora")position=ResolveAurora(row).transform.position;
        else
        {
            var current=UnityEngine.Object.FindObjectsOfType<ReverseGravityZone>(true)
                .FirstOrDefault(z=>Loaded(z)&&z.Pointer==row.Token);
            if(current==null)throw new InvalidOperationException("Локальный объём больше не загружен.");
            position=current.transform.position;
        }
        var group=SystemContext.Instance.SceneLoader.CurrentSceneGroup;
        if(group==null||!group.IsGameplay)throw new InvalidOperationException("Группа этой сцены недоступна.");
        var point=new MapTools.Point{Id=row.Id,Name=row.Name,Category="Феномены",Group=group,GroupName=group.name,
            Position=position,ExactHeight=false,AllowNearbyLanding=true,Detail=row.Kind+" · "+row.State};
        _app.Map.RequestTeleport(point);
    }
    internal void Reset()
    {
        Inspect=false;Rows.Clear();Rows.TrimExcess();Examined=0;_next=0;
        Status="Исследование феноменов выключено";
    }
}