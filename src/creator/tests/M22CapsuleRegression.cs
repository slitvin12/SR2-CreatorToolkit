using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
namespace CreatorToolkit;
internal static class M22CapsuleRegression
{
    internal static void Run(Action<bool,string> check,string root)
    {
        void C(bool v,string n)=>check(v,"m15 / followup / m22 / "+n);
        var scenes=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"Assets/Scene/real.unity"};
        C(CapsuleMapPolicy.StaticAllowed("Assets/Scene/real.unity",scenes,true),"exact source accepted");
        C(CapsuleMapPolicy.StaticAllowed("assets\\scene\\REAL.unity",scenes,true),"source normalization does not change identity");
        C(!CapsuleMapPolicy.StaticAllowed("Assets/Scene/real_old.unity",scenes,true),"legacy source rejected, no prefix inference");
        C(!CapsuleMapPolicy.StaticAllowed(null,scenes,true)&&!CapsuleMapPolicy.StaticAllowed("",scenes,true),"missing source is not proof");
        C(!CapsuleMapPolicy.StaticAllowed("Assets/Scene/real.unity",null,true),"unresolved membership does not invent capsules");
        C(!CapsuleMapPolicy.StaticAllowed("Assets/Scene/real.unity",scenes,false),"inactive authored hierarchy not advertised");
        for(int mask=0;mask<32;mask++)
        {
            bool a=(mask&1)!=0,b=(mask&2)!=0,c=(mask&4)!=0,d=(mask&8)!=0,e=(mask&16)!=0;
            C(CapsuleMapPolicy.NativeAllowed(a,b,c,d,e?"pod1":null)==(mask==31),"native existence gate "+mask);
        }
        var source=File.ReadAllText(Path.Combine(root,"src/creator/MapTools.cs"));
        C(source.Contains("AddCapsuleIndex(point,c.Length>11&&c[11]==\"1\")")&&source.Contains("CapsuleMapPolicy.NativeAllowed"),"static and live policies wired into map");
        C(!source.Contains("existingPod.SourceScene=pod.gameObject.scene.name"),"native scene short name never destroys exact provenance");
        var rows=File.ReadAllLines(Path.Combine(root,"src/creator/MapIndex.tsv")).Skip(1).Select(l=>l.Split('\t')).Where(c=>c.Length>1&&c[1]=="Капсулы").ToArray();
        C(rows.Length==152&&rows.Select(c=>c[0]).Distinct().Count()==152,"all authored capsule identities preserved for auditing");
        using var audit=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"src/creator/tests/fixtures/m22-capsule-audit.json")));
        C(audit.RootElement.GetProperty("errors").GetArrayLength()==0&&audit.RootElement.GetProperty("bundles").GetArrayLength()==32,"fresh complete catalog scene bundle scan");
        var pods=audit.RootElement.GetProperty("pods").EnumerateArray().ToDictionary(p=>p.GetProperty("id").GetString());
        using var native=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"src/creator/tests/fixtures/m22-live-capsule-initial.json")));
        var membership=native.RootElement.GetProperty("Membership").EnumerateArray()
            .Where(m=>m.GetProperty("Complete").GetBoolean()).ToDictionary(m=>m.GetProperty("Group").GetString(),
                m=>new HashSet<string>(m.GetProperty("Scenes").EnumerateArray().Select(s=>s.GetString()),StringComparer.OrdinalIgnoreCase));
        C(rows.Count(c=>c[3]=="Sanctuary")==3,"three real Sanctuary capsules assigned to Sanctuary, not ranch");
        C(rows.Count(c=>c[11]=="0")==2,"two retired dormant Labyrinth instances not advertised");
        foreach(var row in rows)
        {
            bool found=pods.TryGetValue(row[0],out var pod);
            C(found&&row.Length==12&&row[9]==pod.GetProperty("scene").GetString(),row[0]+" exact scene provenance");
            bool active=found&&pod.GetProperty("activeHierarchy").GetBoolean()&&pod.GetProperty("enabled").GetInt32()==1;
            C(row[11]==(active?"1":"0"),row[0]+" full hierarchy activity recorded");
            membership.TryGetValue(row[3],out var canonical);
            C(CapsuleMapPolicy.StaticAllowed(row[9],canonical,active)==active,row[0]+" actual native scene ownership, no prefix guess");
            if(found)
            {
                var position=pod.GetProperty("position").EnumerateArray().Select(p=>p.GetDouble()).ToArray();
                C(Enumerable.Range(0,3).All(i=>Math.Abs(double.Parse(row[4+i],System.Globalization.CultureInfo.InvariantCulture)-position[i])<.001),row[0]+" world TRS coordinates");
            }
        }
    }
}
