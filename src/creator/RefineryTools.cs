using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
namespace CreatorToolkit;
// Native GadgetDirector is the refinery owner in this build, not AmmoModel.
// Only registered refinery resources; no direct dictionary, save-file or analytics writes.
internal sealed class RefineryTools
{
    internal sealed class Row
    {
        internal IdentifiableType Type;
        internal string Name,Reference;
        internal int Count;
    }
    private readonly ToolkitMod _app;
    private IntPtr _session;
    internal readonly List<Row> Rows=new();
    internal string Status {get;private set;}="Откройте каталог Преобразователя";
    internal int Limit {get;private set;}
    internal RefineryTools(ToolkitMod app)=>_app=app;
    private GadgetDirector Director()
    {
        if(!_app.Ready||_app.Map.Busy||_app.Audit.MainAutosaveHeld)
            throw new InvalidOperationException("Мир загружается или выполняется защитный возврат.");
        var director=_app.Context.GadgetDirector;
        if(director==null||director.WasCollected||director._model==null||director.RefineryTypeGroup==null)
            throw new InvalidOperationException("Нативное хранилище Преобразователя недоступно.");
        return director;
    }
    internal void Refresh()
    {
        var director=Director();var fresh=new List<Row>();var seen=new HashSet<string>();
        Limit=M26RefineryPolicy.Cap(GadgetDirector.REFINERY_MAX);
        int examined=0;
        using(InfiniteBuildHooks.RealCounts())
        foreach(var type in NativeEnumerable.Items(director.RefineryTypeGroup.GetAllMembers()))
        {
            if(++examined>2048||fresh.Count>=1024)throw new InvalidOperationException("Непредвиденный размер каталога Преобразователя.");
            if(type==null||type.WasCollected||string.IsNullOrEmpty(type.ReferenceId)||!seen.Add(type.ReferenceId))continue;
            if(!director.RefineryTypeGroup.IsMember(type))continue;
            string name=_app.Actors.Catalog.FirstOrDefault(e=>!e.IsPrototype&&!e.IsRadiant&&e.Reference==type.ReferenceId)?.Name??type.ReferenceId;
            fresh.Add(new Row{Type=type,Reference=type.ReferenceId,Name=name,Count=director.GetItemCount(type)});
        }
        fresh.Sort((a,b)=>string.Compare(a.Name,b.Name,StringComparison.CurrentCulture));
        Rows.Clear();Rows.AddRange(fresh);_session=_app.Session;
        Status=$"Нативных ресурсов: {Rows.Count} · лимит {Limit} · значения из GadgetDirector";
    }
    internal void Request(bool fill,string reference=null)
    {
        Refresh();
        if(reference!=null&&!Rows.Any(r=>r.Reference==reference))throw new InvalidOperationException("Ресурс больше не доступен.");
        var expected=_session;
        _app.Confirm(fill?"Заполнить Преобразователь?":"Добавить ресурсы Преобразователя?",
            (reference==null?"Все зарегистрированные ресурсы":Rows.First(r=>r.Reference==reference).Name)+
            (fill?$": довести до {Limit} шт.":": добавить до 100 шт. к текущему количеству.")+
            " Изменение реальное и сохранится игрой. Предметы вакпака и изготовленные гаджеты не меняются. Отмены нет.",
            ()=>Apply(expected,fill,reference));
    }
    private void Apply(IntPtr expected,bool fill,string reference)
    {
        if(expected!=_app.Session)throw new InvalidOperationException("Сеанс изменился: пополнение отменено.");
        Refresh();var director=Director();
        if(reference!=null&&!Rows.Any(r=>r.Reference==reference))throw new InvalidOperationException("Ресурс отсутствует в новом каталоге.");
        int changed=0,total=0;
        using(InfiniteBuildHooks.RealCounts())
        {
            // Preflight the entire selection before the first native mutation.
            var plan=new List<(Row Row,int Before,int Add)>();
            foreach(var row in Rows)
            {
                if(reference!=null&&row.Reference!=reference)continue;
                if(!director.RefineryTypeGroup.IsMember(row.Type))throw new InvalidOperationException("Недопустимый ресурс.");
                int before=director.GetItemCount(row.Type);
                int add=M26RefineryPolicy.Addition(before,GadgetDirector.REFINERY_MAX,director.GetRefinerySpaceAvailable(row.Type),fill);
                plan.Add((row,before,add));
            }
            try
            {
                foreach(var item in plan)
                {
                    if(item.Add==0)continue;
                    if(director.GetItemCount(item.Row.Type)!=item.Before)throw new InvalidOperationException("Количество ресурса изменилось во время операции.");
                    director.AddItem(item.Row.Type,item.Add,false); // overflow is explicitly forbidden
                    int after=director.GetItemCount(item.Row.Type);item.Row.Count=after;
                    if(after!=item.Before+item.Add)throw new InvalidOperationException("Нативное чтение после AddItem не подтвердило количество: "+item.Row.Name);
                    changed++;total+=item.Add;
                }
            }
            catch(Exception e)
            {
                Status=$"Операция остановлена; подтверждено ресурсов: {changed}, единиц: {total}. Текущий ресурс мог измениться частично. Автоотката нет: {e.Message}";
                throw new InvalidOperationException(Status,e);
            }
        }
        Status=$"Преобразователь: изменено ресурсов {changed}, добавлено {total} шт. · проверено нативным GetItemCount";
        _app.Notify(Status);
    }
    internal void Reset(){Rows.Clear();_session=IntPtr.Zero;Limit=0;Status="Откройте каталог Преобразователя";}
}
