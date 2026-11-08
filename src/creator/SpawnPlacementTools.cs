using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CreatorToolkit;

// 0.8.7 «Выбрать место…»: a catalog entry is placed where the cursor points, with a chosen facing.
// The menu input capture stays on (UI action maps), so LMB, the wheel and Q/E never reach the
// vacpack or the camera; only the menu panel steps aside for a light overlay. Nothing exists in
// the world before LMB: the preview is only drawn, the spawn itself is the ordinary queued,
// co-op-gated "spawn" action; a Gordo keeps the native snare transaction and the 8 m spacing.
internal sealed class SpawnPlacementTools
{
    internal const float MaxDistance=60f;
    private const float GoldenAngle=2.39996323f;
    private readonly ToolkitMod _app;
    private readonly List<Vector3> _gordos=new();
    private Vector3[] _positions=Array.Empty<Vector3>();
    private Il2CppReferenceArray<Collider> _overlaps;
    private IntPtr _session;
    private float _gordosAt=-10f;
    internal SpawnPlacementTools(ToolkitMod app)=>_app=app;
    internal bool Active {get;private set;}
    internal bool Showing=>Active&&_app.Ready&&_app.MenuOpen&&_app.Session==_session;
    internal ActorTools.Entry Entry {get;private set;}
    internal bool Gordo {get;private set;}
    internal int Count {get;private set;}
    internal float Yaw {get;private set;}
    internal bool HasPoint {get;private set;}
    internal bool Valid {get;private set;}
    internal Vector3 Point {get;private set;}
    internal Vector3 Ground {get;private set;}
    internal float Distance {get;private set;}
    internal string Problem {get;private set;}
    internal int Placed {get;private set;}
    internal IReadOnlyList<Vector3> Positions=>_positions;
    internal float Radius=>Gordo?1.6f:.45f+(Count<=1?0f:.55f*Mathf.Sqrt(Count-1));

    internal void Begin(ActorTools.Entry entry,int count)
    {
        if(!_app.Ready||_app.Map.Busy)throw new InvalidOperationException("Дождитесь загрузки мира.");
        string denied=_app.Coop?.Deny("spawn");
        if(denied!=null)throw new InvalidOperationException(denied);
        if(entry==null||entry.Type==null)throw new InvalidOperationException("Выберите объект в каталоге.");
        bool gordo=ActorTools.IsGordoEntry(entry);
        if(gordo)
        {
            if(_app.Coop?.InSession==true)throw new InvalidOperationException("Кооп: Гордо доступны только в одиночной игре.");
            if(!entry.CanSpawn)throw new InvalidOperationException(entry.UnavailableReason+" Создание отключено для защиты сейва.");
            if(_app.Actors.GordoSpawnPending)throw new InvalidOperationException("Гордо уже создаётся. Дождитесь завершения.");
        }
        else _app.Actors.RequireSpawnable(entry);
        Reset();
        Entry=entry;Gordo=gordo;Count=gordo?1:SafetyPolicy.SpawnCount(count);
        var cam=Camera.main;
        Yaw=cam!=null?Mathf.Repeat(cam.transform.eulerAngles.y+180f,360f):0f; // faces the player at first
        _session=_app.Session;_gordosAt=-10f;Active=true;
        _app.Notify(gordo?"Место для Гордо: ЛКМ — поставить, колесо или Q/E — поворот, ПКМ — назад.":"Место и поворот: ЛКМ — поставить, колесо или Q/E — поворот, ПКМ — назад.");
    }

    internal void Cancel(string message)
    {
        if(!Active)return;
        Reset();
        if(message!=null)_app.Notify(message);
    }

    internal void Reset()
    {
        Active=false;HasPoint=Valid=false;Problem=null;Entry=null;Placed=0;
        _positions=Array.Empty<Vector3>();_gordos.Clear();
    }

    internal void Tick()
    {
        if(!Active)return;
        if(_app.Session!=_session||_app.Map.Busy){Cancel("Выбор места прерван: мир изменился.");return;}
        if(!_app.MenuOpen){Cancel(Placed>0?"Выбор места завершён.":"Выбор места закрыт.");return;}
        if(Entry==null||!_app.Actors.Catalog.Contains(Entry)){Cancel("Каталог обновился; выберите объект заново.");return;}
        var mouse=Mouse.current;var kb=Keyboard.current;var cam=Camera.main;
        if(mouse==null||cam==null){HasPoint=Valid=false;Problem="Мышь или камера недоступны";return;}
        bool fine=kb!=null&&(kb.leftShiftKey.isPressed||kb.rightShiftKey.isPressed);
        float wheel=mouse.scroll.ReadValue().y;
        if(wheel>.01f)Yaw+=fine?5f:15f;else if(wheel<-.01f)Yaw-=fine?5f:15f;
        if(kb!=null)
        {
            float step=(fine?30f:120f)*Mathf.Min(Time.unscaledDeltaTime,.1f);
            if(kb.qKey.isPressed)Yaw-=step;
            if(kb.eKey.isPressed)Yaw+=step;
        }
        Yaw=Mathf.Repeat(Yaw,360f);
        Vector2 cursor=mouse.position.ReadValue();
        Measure(cam,cursor);
        if(kb!=null&&kb.rKey.wasPressedThisFrame){FacePlayer(cam);Measure(cam,cursor);}
        if(mouse.rightButton.wasPressedThisFrame){Cancel("Выбор места отменён.");return;}
        if(mouse.leftButton.wasPressedThisFrame||kb!=null&&(kb.enterKey.wasPressedThisFrame||kb.numpadEnterKey.wasPressedThisFrame))Place();
    }

    private void FacePlayer(Camera cam)
    {
        Vector3 from=HasPoint?Ground:cam.transform.position+cam.transform.forward*4f;
        Vector3 to=cam.transform.position-from;to.y=0f;
        if(to.sqrMagnitude>.0001f)Yaw=Mathf.Repeat(Mathf.Atan2(to.x,to.z)*Mathf.Rad2Deg,360f);
    }

    private void Measure(Camera cam,Vector2 screen)
    {
        HasPoint=Valid=false;Problem=null;
        Transform root=_app.Player.PlacementRoot;int mask=_app.Player.PlacementLayers;
        if(root==null||mask==0){Problem="Игрок ещё не готов";return;}
        if(screen.x<0f||screen.y<0f||screen.x>Screen.width||screen.y>Screen.height){Problem="Курсор вне окна";return;}
        if(Gordo&&_app.Actors.GordoSpawnPending){Problem="Гордо уже создаётся";return;}
        Transform body=_app.Context?.Player!=null?_app.Context.Player.transform:root;
        var hits=Physics.RaycastAll(cam.ScreenPointToRay(new Vector3(screen.x,screen.y,0f)),MaxDistance,mask,QueryTriggerInteraction.Ignore);
        float nearest=float.MaxValue;int best=-1;
        for(int i=0;i<hits.Length;i++)
        {
            var hit=hits[i];var collider=hit.collider;
            if(collider==null||hit.distance>=nearest||collider.transform.IsChildOf(root)||collider.transform.IsChildOf(body))continue;
            if(collider.GetComponentInParent<IdentifiableActor>()!=null)continue; // aim through slimes and items
            nearest=hit.distance;best=i;
        }
        if(best<0){Problem="Наведите курсор на землю ближе 60 м";return;}
        var chosen=hits[best];
        HasPoint=true;Point=chosen.point;Ground=chosen.point;Distance=Vector3.Distance(root.position,chosen.point);
        if(Gordo)MeasureGordo(chosen);else MeasureActors(chosen,mask,root,body);
    }

    private void MeasureGordo(RaycastHit hit)
    {
        if(PlayerTools.HazardSurface(hit.collider)){Problem="Вода, море слаймов и лава не подходят";return;}
        if(hit.normal.y<.65f){Problem="Слишком крутой склон для Гордо";return;}
        if(!_app.Player.TryColumnGround(hit.point,hit.point.y+1.5f,4f,out var ground)||Vector3.Distance(ground,hit.point)>1f)
        {Problem="Нет свободной устойчивой земли";return;}
        Ground=ground;
        RefreshGordos();
        for(int i=0;i<_gordos.Count;i++)if(Vector3.Distance(_gordos[i],ground)<8f){Problem="Другой Гордо ближе 8 м";return;}
        Valid=true;
    }

    private void RefreshGordos()
    {
        if(Time.unscaledTime-_gordosAt<1f)return;
        _gordosAt=Time.unscaledTime;_gordos.Clear();
        foreach(var other in UnityEngine.Object.FindObjectsOfType<GordoEat>())if(other!=null)_gordos.Add(other.transform.position);
    }

    private void MeasureActors(RaycastHit hit,int mask,Transform root,Transform body)
    {
        if(PlayerTools.DeadlySurface(hit.collider)){Problem="Море слаймов и лава не подходят";return;}
        Vector3 normal=hit.normal.sqrMagnitude>.5f?hit.normal.normalized:Vector3.up;
        Vector3 center=hit.point+normal*.55f+Vector3.up*.15f;
        if(Blocked(center,.38f,mask,root,body)){Problem="Мало места: сдвиньте курсор";return;}
        if(_positions.Length!=Count)_positions=new Vector3[Count];
        Quaternion rotation=Quaternion.Euler(0f,Yaw,0f);
        int stacked=0;
        for(int i=0;i<Count;i++)
        {
            Vector3 p=center+rotation*Offset(i);
            if(i>0&&Blocked(p,.32f,mask,root,body))p=center+Vector3.up*(.5f*++stacked); // walls and props: stack above the centre
            _positions[i]=p;
        }
        Valid=true;
    }

    // Golden-angle disc: the first actor at the centre, the rest evenly around it (~0.55 m apart).
    private static Vector3 Offset(int i)
    {
        if(i<=0)return Vector3.zero;
        float r=.55f*Mathf.Sqrt(i),a=i*GoldenAngle;
        return new Vector3(Mathf.Cos(a)*r,0f,Mathf.Sin(a)*r);
    }

    private bool Blocked(Vector3 center,float radius,int mask,Transform root,Transform body)
    {
        _overlaps??=new Il2CppReferenceArray<Collider>(32);
        int n=Physics.OverlapSphereNonAlloc(center,radius,_overlaps,mask,QueryTriggerInteraction.Ignore);
        try
        {
            if(n>=_overlaps.Length)return true;
            for(int i=0;i<n;i++)
            {
                var c=_overlaps[i];
                if(c==null||c.transform.IsChildOf(root)||c.transform.IsChildOf(body))continue;
                if(c.GetComponentInParent<IdentifiableActor>()!=null)continue; // other actors are simply pushed aside
                return true;
            }
            return false;
        }
        finally{for(int i=0;i<n&&i<_overlaps.Length;i++)_overlaps[i]=null;}
    }

    private void Place()
    {
        if(!Valid){_app.Notify(Problem??"Место не подходит");return;}
        var entry=Entry;float yaw=Yaw;
        if(Gordo)
        {
            Vector3 ground=Ground;
            Reset();
            _app.Queue("spawn",()=>_app.Actors.SpawnGordoAt(entry,ground,yaw));
            _app.CloseMenu();
            return;
        }
        var positions=(Vector3[])_positions.Clone();
        _app.Queue("spawn",()=>_app.Actors.SpawnPlaced(entry,positions,yaw));
        Placed+=positions.Length;
    }
}
