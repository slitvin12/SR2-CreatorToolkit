using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace CreatorToolkit;

// Read-only, explicitly approved cross-process reference. Audit code never
// creates, updates, restores, or rolls this file forward from current saves.
internal sealed class AuditImmutableBaseline
{
    public int FormatVersion {get;set;}
    public string ApprovalDecision {get;set;}="";
    public string ApprovalReference {get;set;}="";
    public string ApprovedUtc {get;set;}="";
    public string MainName {get;set;}="";
    public string MainSave {get;set;}="";
    public string SavePath {get;set;}="";
    public Dictionary<string,string> OriginalHashes {get;set;}=new();

    internal static string Sha256(string path)
    {
        using var sha=SHA256.Create();
        using var stream=File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
    internal static bool SaveKey(string value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=80&&
        value.All(c=>c>='a'&&c<='z'||c>='A'&&c<='Z'||c>='0'&&c<='9'||c=='_'||c=='-');
    internal static bool Digest(string value)=>value!=null&&value.Length==64&&
        value.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');
    internal static bool SamePath(string a,string b)=>!string.IsNullOrWhiteSpace(a)&&!string.IsNullOrWhiteSpace(b)&&
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),StringComparison.OrdinalIgnoreCase);
    internal static bool Checkpoint(string gameName,string fileName)
    {
        if(!SaveKey(gameName)||string.IsNullOrEmpty(fileName)||Path.GetFileName(fileName)!=fileName)return false;
        string leaf=fileName.EndsWith(".sav",StringComparison.OrdinalIgnoreCase)?fileName[..^4]:fileName;
        // Native destinations are Windows filesystem identities, not case-
        // sensitive display labels. A case-only alias must never evade a deny.
        if(!leaf.StartsWith(gameName+"_",StringComparison.OrdinalIgnoreCase))return false;
        string number=leaf[(gameName.Length+1)..];
        return number.Length>0&&number.Length<=10&&number.All(c=>c>='0'&&c<='9');
    }
    internal void Validate()
    {
        if(FormatVersion!=1||ApprovalDecision!="keep-current"&&ApprovalDecision!="authorized-restored-snapshot"||
            string.IsNullOrWhiteSpace(ApprovalReference)||ApprovalReference.Length>1024||
            string.IsNullOrWhiteSpace(ApprovedUtc)||!(ApprovedUtc.EndsWith("Z",StringComparison.Ordinal)||
                ApprovedUtc.EndsWith("+00:00",StringComparison.Ordinal))||
            !DateTimeOffset.TryParse(ApprovedUtc,out var approved)||approved.Offset!=TimeSpan.Zero||
            !SaveKey(MainName)||!Checkpoint(MainName,MainSave)||MainSave.EndsWith(".sav",StringComparison.OrdinalIgnoreCase)||
            !Path.IsPathRooted(SavePath)||!Directory.Exists(SavePath)||OriginalHashes==null||
            OriginalHashes.Count<1||OriginalHashes.Count>512)
            throw new InvalidDataException("Explicitly approved immutable audit baseline is missing or invalid.");
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var pair in OriginalHashes)
        {
            if(!Path.IsPathRooted(pair.Key)||!SamePath(Path.GetDirectoryName(pair.Key),SavePath)||
                !pair.Key.EndsWith(".sav",StringComparison.OrdinalIgnoreCase)||!Digest(pair.Value)||
                !seen.Add(Path.GetFullPath(pair.Key)))
                throw new InvalidDataException("Immutable original hash identity is invalid.");
        }
        if(!OriginalHashes.Keys.Any(p=>SamePath(p,Path.Combine(SavePath,MainSave+".sav"))))
            throw new InvalidDataException("The exact protected main checkpoint is absent from the immutable baseline.");
    }
    internal void Verify(string ownedCopy="")
    {
        Validate();
        if(ownedCopy.Length>0)ValidateOwnedCopy(ownedCopy);
        var expected=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var pair in OriginalHashes)
        {
            expected.Add(Path.GetFullPath(pair.Key),pair.Value);
            if(!File.Exists(pair.Key)||Sha256(pair.Key)!=pair.Value)
                throw new InvalidDataException("Immutable original save changed or disappeared: "+Path.GetFileName(pair.Key));
        }
        // Detect checkpoint rotation/addition too, not only changed/missing old
        // paths. Only this audit's one exact owned-copy family may be extra.
        foreach(string path in Directory.GetFiles(SavePath,"*.sav"))
            if(!expected.ContainsKey(Path.GetFullPath(path))&&!Checkpoint(ownedCopy,Path.GetFileName(path)))
                throw new InvalidDataException("Unexpected native save outside the immutable baseline: "+Path.GetFileName(path));
    }
    internal void ValidateOwnedCopy(string name)
    {
        if(!SaveKey(name)||string.Equals(name,MainName,StringComparison.OrdinalIgnoreCase)||
            OriginalHashes.Keys.Any(p=>Checkpoint(name,Path.GetFileName(p))))
            throw new InvalidDataException("The owned copy collides with immutable originals, including Windows case aliases.");
    }
    internal bool OriginalDestination(string name)
    {
        if(string.IsNullOrWhiteSpace(name))return true;
        string path=Path.IsPathRooted(name)?name:Path.Combine(SavePath,name);
        string native=path.EndsWith(".sav",StringComparison.OrdinalIgnoreCase)?path:path+".sav";
        return OriginalHashes.Keys.Any(p=>SamePath(p,path)||SamePath(p,native));
    }
    internal static AuditImmutableBaseline Read(string path)
    {
        if(!File.Exists(path)||new FileInfo(path).Length>256*1024)
            throw new InvalidDataException("An explicitly approved audit-immutable-baseline.json is required; it is never generated automatically.");
        var value=JsonSerializer.Deserialize<AuditImmutableBaseline>(File.ReadAllText(path));
        if(value==null)throw new InvalidDataException("Immutable audit baseline is empty.");
        value.Validate();return value;
    }
}

// Pure policy, also compiled into the offline tests. When an audit is armed,
// every native game-write/delete entry must fail closed except the exact owned
// copy. Closing and failed setup deny even that copy. Normal gameplay is not
// silently converted to a discard-progress session.
internal static class AuditSavePolicy
{
    internal static bool RestoreAutosaveDisabled(bool auditRequired,bool cachedValue)=>auditRequired||cachedValue;
    internal static bool GameWrite(bool required,bool ready,bool closing,bool readOnlyProbe,
        string ownedCopy,string currentGame,string displayName)=>
        !required||ready&&!closing&&!readOnlyProbe&&AuditImmutableBaseline.SaveKey(ownedCopy)&&
        currentGame==ownedCopy&&displayName!=null&&displayName.StartsWith("[CT AUDIT] ",StringComparison.Ordinal);
    internal static bool NamedMutation(bool required,bool ready,bool closing,bool readOnlyProbe,
        string ownedCopy,string gameName)=>
        !required||ready&&!closing&&!readOnlyProbe&&AuditImmutableBaseline.SaveKey(ownedCopy)&&gameName==ownedCopy;
    internal static bool FileMutation(bool required,bool ready,bool closing,bool readOnlyProbe,
        string ownedCopy,string fileName)=>
        !required||ready&&!closing&&!readOnlyProbe&&AuditImmutableBaseline.Checkpoint(ownedCopy,fileName);
}