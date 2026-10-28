using System;
namespace CreatorToolkit;
internal enum M25ShadowMode { Game, Low, Off }
internal readonly record struct M25RuntimeRequest(int ShadowDistance,M25ShadowMode Shadows,int DetailDistance,int Raycasts,bool Sleep)
{
    internal static M25RuntimeRequest Full=>new(0,0,0,0,false);
    internal static M25RuntimeRequest Balanced=>new(35,M25ShadowMode.Low,60,128,false);
    internal static M25RuntimeRequest Maximum=>new(20,M25ShadowMode.Off,35,64,false);
    internal M25RuntimeRequest Clamped()=>new(M25RuntimePolicy.Optional(ShadowDistance,10,500),
        Enum.IsDefined(typeof(M25ShadowMode),Shadows)?Shadows:0,M25RuntimePolicy.Optional(DetailDistance,10,500),M25RuntimePolicy.Optional(Raycasts,16,4096),Sleep);
    internal bool Any=>this!=Full;
}
internal static class M25RuntimePolicy
{
    internal static int Optional(int n,int min,int max)=>n<=0?0:Math.Clamp(n,min,max);
    internal static int ParticleMax(int n)=>Optional(n,16,4096);
    internal static float Distance(float before,int cap)=>!float.IsFinite(before)||before<0?throw new ArgumentOutOfRangeException(nameof(before)):cap<=0?before:Math.Min(before,cap);
    internal static float BoundsDistanceSquared(float px,float py,float pz,float cx,float cy,float cz,float ex,float ey,float ez)
    {
        if(!float.IsFinite(px)||!float.IsFinite(py)||!float.IsFinite(pz)||!float.IsFinite(cx)||!float.IsFinite(cy)||!float.IsFinite(cz)||!float.IsFinite(ex)||!float.IsFinite(ey)||!float.IsFinite(ez))return float.NaN;
        if(ex<0||ey<0||ez<0||ex+ey+ez<=0)return float.NaN;
        double x=Math.Max(0,Math.Abs((double)px-cx)-ex),y=Math.Max(0,Math.Abs((double)py-cy)-ey),z=Math.Max(0,Math.Abs((double)pz-cz)-ez);
        double d=x*x+y*y+z*z;return d>float.MaxValue?float.NaN:(float)d;
    }
    internal static bool CullDecor(float distanceSq,int distance,bool hidden)=>float.IsFinite(distanceSq)&&distanceSq>=0&&distance>0&&distanceSq>(double)(distance+(hidden?0:10))*(distance+(hidden?0:10));
    // Conservative sleep, NOT simulation culling: never stop moving/falling actors.
    internal static bool MaySleep(bool visible,float distanceSq,float speedSq,float angularSq)=>
        !visible&&float.IsFinite(distanceSq)&&distanceSq>40*40&&float.IsFinite(speedSq)&&speedSq<.01f&&float.IsFinite(angularSq)&&angularSq<.01f;
}
// Testable scalar ownership. A failed restore retains the lease for retry.
internal sealed class M25ScalarLease<T> where T:IEquatable<T>
{
    private readonly Func<T> _read;private readonly Action<T> _write;
    internal T Before {get;} internal T Expected {get;}
    internal bool Owned {get;private set;} internal bool Conflict {get;private set;}
    internal M25ScalarLease(Func<T> read,Action<T> write,T target)
    {_read=read;_write=write;Before=read();Expected=target;Owned=!Before.Equals(target);}
    internal void Apply(){if(!Owned)return;_write(Expected);if(!_read().Equals(Expected))throw new InvalidOperationException("Runtime scalar readback mismatch");}
    internal void Restore(){if(!Owned)return;T current=_read();if(current.Equals(Expected)){_write(Before);if(!_read().Equals(Before))throw new InvalidOperationException("Runtime restore readback mismatch");}else if(!current.Equals(Before))Conflict=true;Owned=false;}
}
