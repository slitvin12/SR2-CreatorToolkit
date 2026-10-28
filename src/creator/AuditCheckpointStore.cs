using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CreatorToolkit;

// Audit-owned JSON only, never native save files. Same-directory flush+atomic
// replace retains a previous valid checkpoint. Fault injection is offline only.
internal static class AuditCheckpointStore
{
    internal static bool Exists(string path)=>File.Exists(path)||File.Exists(path+".previous");
    private static bool ValidJson(string text)
    {
        try{using var doc=JsonDocument.Parse(text);return doc.RootElement.ValueKind==JsonValueKind.Object;}
        catch(JsonException){return false;}
    }
    internal static void Write(string path,string json,Action<string> fault=null)
    {
        if(!ValidJson(json))throw new InvalidDataException("Audit checkpoint must be a complete JSON object.");
        string temporary=path+".pending-"+Guid.NewGuid().ToString("N");
        try
        {
            byte[] bytes=new UTF8Encoding(false).GetBytes(json);
            if(bytes.Length>1024*1024)throw new InvalidDataException("Audit checkpoint exceeds its bounded parse/write budget.");
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
            fault?.Invoke("before-commit");
            if(File.Exists(path))
            {
                if(ValidJson(File.ReadAllText(path)))File.Replace(temporary,path,path+".previous",true);
                else
                {
                    // Preserve opaque/corrupt metadata and the last valid
                    // previous checkpoint. This never repairs native .sav.
                    File.Move(path,path+".rejected-"+Guid.NewGuid().ToString("N"));
                    File.Move(temporary,path);
                }
            }
            else File.Move(temporary,path);
            fault?.Invoke("after-commit");
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    internal static string Read(string path,out bool previousUsed)
    {
        Exception last=null;
        foreach(string candidate in new[]{path,path+".previous"})
        {
            try
            {
                if(!File.Exists(candidate))continue;
                if(new FileInfo(candidate).Length>1024*1024)throw new InvalidDataException("Audit recovery parse budget exceeded.");
                string json=File.ReadAllText(candidate);
                if(!ValidJson(json))throw new InvalidDataException("Incomplete audit recovery JSON.");
                previousUsed=candidate!=path;return json;
            }
            catch(Exception e) when(e is IOException||e is UnauthorizedAccessException||e is InvalidDataException){last=e;}
        }
        throw new InvalidDataException("No readable complete audit recovery checkpoint was found.",last);
    }
    internal static void Retire(string path,string runId)
    {
        if(!Guid.TryParseExact(runId,"N",out _))throw new InvalidDataException("Audit retirement run identity is invalid.");
        string history=Path.Combine(Path.GetDirectoryName(path),"audit-completed-recovery");
        Directory.CreateDirectory(history);
        foreach(string source in new[]{path,path+".previous"})
        {
            if(!File.Exists(source))continue;
            string json=File.ReadAllText(source);
            using var doc=JsonDocument.Parse(json);
            if(!doc.RootElement.TryGetProperty("RunId",out var id)||id.GetString()!=runId)
                throw new InvalidDataException("Refusing to retire recovery metadata from a different audit.");
            using var sha=SHA256.Create();
            string hash=Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(source))).ToLowerInvariant();
            string destination=Path.Combine(history,runId+"-"+hash+".json");
            if(File.Exists(destination))
            {
                if(File.ReadAllText(destination)!=json)throw new InvalidDataException("Retired recovery metadata differs; both preserved.");
                File.Delete(source); // Audit-owned JSON, already archived byte-identically.
            }
            else File.Move(source,destination);
        }
    }
}