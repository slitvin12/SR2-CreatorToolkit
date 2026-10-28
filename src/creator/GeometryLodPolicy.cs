using System;
using System.Globalization;

namespace CreatorToolkit;

// Scalar-only lease. Delegates allow offline checks without loading Unity.
// No callback writes: only explicit ApplyLow / Restore may call the setter.
internal sealed class GeometryLodPolicy
{
    internal const float LowFactor=.75f;
    private readonly Func<float> _read;
    private readonly Action<float> _write;
    private bool _owned,_uncertain,_conflict;
    private float _baseline=float.NaN,_expected=float.NaN;
    internal bool Active=>_owned;
    internal bool Pending=>_owned&&(_uncertain||_conflict);
    // The scalar API exists in the inspected interop. Runtime read/write is
    // unprobed until the first explicit apply; no startup quality mutation.
    internal bool Supported {get;private set;}=true;
    internal bool ReadbackValid {get;private set;}
    internal float EffectiveBias {get;private set;}=float.NaN;
    internal int Conflicts {get;private set;}
    internal string Status {get;private set;}="Геометрия LOD: выключено; API ещё не проверен в игре";
    internal GeometryLodPolicy(Func<float> read,Action<float> write)
    {_read=read;_write=write;}
    private static bool Valid(float value)=>float.IsFinite(value)&&value>0f;
    // A positive floor must NEVER raise an already tiny native bias. Compute
    // in double, bound to the baseline, and reject all invalid native values.
    internal static float LowBias(float baseline)
    {
        if(!Valid(baseline))throw new ArgumentOutOfRangeException(nameof(baseline));
        return Math.Min(baseline,Math.Max(float.Epsilon,(float)((double)baseline*LowFactor)));
    }
    private float Read()
    {
        ReadbackValid=false;
        float value=_read();
        EffectiveBias=value;
        if(!Valid(value))
        {Supported=false;throw new InvalidOperationException("Некорректный lodBias: ожидается конечное положительное значение.");}
        ReadbackValid=true;Supported=true;return value;
    }
    private void Conflict()
    {if(!_conflict)Conflicts++;_conflict=true;}
    private void Released(string status)
    {_owned=false;_uncertain=false;Status=status;}
    internal void ApplyLow()
    {
        try
        {
            if(_owned&&(_uncertain||_conflict))
                throw new InvalidOperationException("Сначала завершите восстановление геометрии LOD.");
            float current=Read();
            // Explicit repeated apply is idempotent, never compounds .75,
            // never silently adopts or overwrites another owner's edit.
            if(_owned&&current!=_expected)
            {Conflict();throw new InvalidOperationException("lodBias изменён извне; изменение не перезаписано.");}
            if(!_owned){_baseline=current;_conflict=false;}
            float target=LowBias(_baseline);
            if(target>current)target=current;
            if(target==current)
            {Status=_owned?"Геометрия LOD: снижение уже применено":"Геометрия LOD: исходное значение уже минимально; без изменений";return;}
            // Establish ownership BEFORE the setter: it may mutate and throw.
            _expected=target;_owned=true;_uncertain=true;
            ReadbackValid=false;
            _write(target);
            if(Read()!=target)
            {Conflict();throw new InvalidOperationException("lodBias: запись не подтверждена чтением.");}
            _uncertain=false;
            Status="Геометрия LOD: 0.75 × исходного lodBias; детализация вдали ниже";
        }
        catch(Exception e)
        {
            if(!ReadbackValid)Supported=false;
            Status="Геометрия LOD: ошибка"+(_owned?"; восстановление требуется":"; без владения")+" · "+e.Message;
            throw;
        }
    }
    internal void Observe()
    {
        if(!_owned)return;
        try
        {
            if(Read()!=_expected)
            {Conflict();Status="Геометрия LOD: стороннее изменение; автоматическая запись запрещена";}
        }
        catch(Exception e)
        {_uncertain=true;Supported=false;Status="Геометрия LOD: чтение недоступно; владение сохранено · "+e.Message;}
    }
    internal void Restore()
    {
        if(!_owned)return;
        try
        {
            float current=Read();
            // Covers a setter that succeeded but threw, or a failed readback.
            if(current==_baseline)
            {Released("Геометрия LOD: исходное значение подтверждено");return;}
            if(current!=_expected)
            {
                Conflict();
                Released("Геометрия LOD: стороннее значение сохранено; восстановление пропущено");
                return;
            }
            _uncertain=true;ReadbackValid=false;
            _write(_baseline);
            if(Read()!=_baseline)
            {Conflict();throw new InvalidOperationException("Исходный lodBias не подтверждён чтением.");}
            Released("Геометрия LOD: исходное значение восстановлено");
        }
        catch(Exception e)
        {
            // Do not clear the lease on a read/set/readback exception. A later
            // explicit Restore or session cleanup can retry conditionally.
            _uncertain=true;
            if(!ReadbackValid)Supported=false;
            Status="Геометрия LOD: восстановление не завершено; владение сохранено · "+e.Message;
            throw;
        }
    }
    private static string Number(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
    internal string Diagnostics()=>
        "GeometrySupported: "+Supported+"\nGeometryActive: "+Active+"\nGeometryPending: "+Pending+
        "\nGeometryBaseline: "+Number(_baseline)+"\nGeometryExpected: "+Number(_expected)+
        "\nGeometryEffectiveBias: "+Number(EffectiveBias)+"\nGeometryReadbackValid: "+ReadbackValid+
        "\nGeometryConflict: "+_conflict+"\nGeometryConflicts: "+Conflicts+
        "\nGeometryFactor: "+Number(LowFactor)+"\nGeometryStatus: "+Status+"\n";
}
