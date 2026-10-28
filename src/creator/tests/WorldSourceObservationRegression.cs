using System;
using System.IO;
using System.Linq;
using P=CreatorToolkit.WorldSourceObservationPolicy;
internal static class WorldSourceObservationRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        check(P.GoldNames.Count==10&&P.GoldNames.Distinct(StringComparer.Ordinal).Count()==10,"exact ten native gold identities unique");
        foreach(string name in P.GoldNames)
        {
            check(P.IsGoldName(name),"exact identity accepted "+name);
            check(!P.IsGoldName(name.ToLowerInvariant())&&!P.IsGoldName(name+" "),"case and suffix aliases rejected "+name);
        }
        foreach(string name in new[]{"Gold","crate_reef_01","ContainerFields01","ContainerGoldSlime",null,""})
            check(!P.IsGoldName(name),"unrelated or missing identity rejected "+name);
        for(int bits=0;bits<64;bits++)
        {
            bool B(int bit)=>(bits&(1<<bit))!=0;
            foreach(var kind in new[]{P.Kind.ActualGold,P.Kind.PotentialGold,P.Kind.PotentialSlime})
                check(P.ActualAllowed(kind,B(0),B(1),B(2),B(3),B(4),B(5))==(kind==P.Kind.ActualGold&&bits==63),"actual requires exact live loaded active finite evidence "+kind+"/"+bits);
        }
        foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
        {
            check(!P.FinitePosition(bad,0,0)&&!P.FinitePosition(0,bad,0)&&!P.FinitePosition(0,0,bad),"invalid coordinate rejected "+bad);
            check(!P.Weight(bad)&&!P.Fresh(1,bad,10)&&!P.Fresh(bad,1,10)&&!P.Fresh(1,1,bad),"nonfinite time or weight rejected "+bad);
        }
        check(P.FinitePosition(0,-10,123.5f)&&P.Weight(0)&&P.Weight(1)&&!P.Weight(-1),"finite positions and nonnegative weights are data not probability");
        check(P.Fresh(10,20,10)&&!P.Fresh(10,20.01f,10)&&!P.Fresh(10,9,10)&&!P.Fresh(10,10,0),"snapshot expiry and rollback fail closed");
        check(P.ValidKey("IdentifiableType.ContainerFieldsGold01")&&!P.ValidKey("x\ny")&&!P.ValidKey(null)&&!P.ValidKey(new string('x',257)),"exact keys finite and control-free");
        check(P.Text(null)==""&&P.Text(new string('x',300)).Length==256&&P.Text("a\nb\rc\td")=="a b c d","display text bounded and sanitized");
        string Compact(string n)=>string.Concat(File.ReadAllText(Path.Combine(root,"src","creator",n)).Where(c=>!char.IsWhiteSpace(c)));
        string source=Compact("WorldSourceObservations.cs"),map=Compact("MapTools.cs");
        check(source.Contains("Math.Clamp(budget,1,64)")&&source.Contains("_work.MoveNext()")&&source.Contains("P.Fresh(_started,now,LifetimeSeconds)"),"static bounded tick and census expiry wiring not runtime certification");
        check(!source.Contains(".Spawn(")&&!source.Contains(".CanSpawn(")&&!source.Contains("InstantiateActor")&&!source.Contains("SetActive(")&&!source.Contains(".Evaluate(")&&!source.Contains("File."),"observer contains no spawn query-evaluation loading or save mutation");
        check(source.Contains("SourceOrigin")&&source.Contains("ConfiguredSpawnLocation")&&source.Contains("QueryPresent=source._allowSpawnQuery!=null"),"configured source positions and query presence not evaluation");
        check(map.Contains("Sources.InvalidateScene()")&&map.Contains("Sources.InvalidateSession()")&&map.Contains("Points.RemoveAll(p=>p.SourceObservation)"),"lifecycle clears source markers");
        check(map.Contains("if(point.SourceObservation)return;")&&map.Contains("MapForPoint(point)==null"),"source display never emits navigation marker and unknown map is refused");
    }
}
