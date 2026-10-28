using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CreatorToolkit;

// Plain records only. User markers live beside mod settings, not in .sav bytes.
internal sealed class WaypointRecord
{
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public string Group {get;set;}="";
    public float X {get;set;} public float Y {get;set;} public float Z {get;set;}
    public int Color {get;set;}
    public bool ExactHeight {get;set;}
}
internal sealed class WaypointStore
{
    public int Version {get;set;}=1;
    public string Save {get;set;}="";
    public List<WaypointRecord> Points {get;set;}=new();
    internal static bool SaveKey(string value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=80&&
        value.All(c=>c>='a'&&c<='z'||c>='A'&&c<='Z'||c>='0'&&c<='9'||c=='_'||c=='-');
    internal void Validate(string expectedSave)
    {
        if(Version!=1||!SaveKey(Save)||Save!=expectedSave||Points==null||Points.Count>ExplorerPolicy.MaxCustomWaypoints)
            throw new InvalidDataException("Waypoint file identity/version/limit mismatch.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in Points)
            if(p==null||!Guid.TryParseExact(p.Id,"N",out _)||!seen.Add(p.Id)||
                string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>ExplorerPolicy.MaxNameLength||p.Name.Any(char.IsControl)||
                string.IsNullOrWhiteSpace(p.Group)||p.Group.Length>128||p.Color<0||p.Color>=ExplorerPolicy.ColorCount||
                !ExplorerPolicy.Coordinates(p.X,p.Y,p.Z))
                throw new InvalidDataException("Invalid custom waypoint.");
    }
    internal static WaypointStore Read(string json,string save)
    {
        if(json==null||json.Length>128*1024)throw new InvalidDataException("Waypoint file too large.");
        var store=JsonSerializer.Deserialize<WaypointStore>(json)??throw new InvalidDataException("Waypoint file empty.");
        store.Validate(save);return store;
    }
    internal string Json(){Validate(Save);return JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true});}
}