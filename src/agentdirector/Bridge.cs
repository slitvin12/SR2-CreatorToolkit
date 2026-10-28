using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CreatorToolkitAgentDirector;

/// <summary>Builds requests from "METHOD /path?query" + optional body (shared by HTTP and file IPC).</summary>
internal static class RequestFactory
{
    internal static AgentRequest Create(string method, string target, string contentType, string body, string source, string line)
    {
        method = string.IsNullOrEmpty(method) ? "GET" : method.ToUpperInvariant();
        target = (target ?? "/").Trim();
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            int slash = target.IndexOf('/', 7);
            target = slash < 0 ? "/" : target.Substring(slash);
        }
        if (!target.StartsWith("/", StringComparison.Ordinal)) target = "/" + target;
        int q = target.IndexOf('?');
        string path = q < 0 ? target : target.Substring(0, q);
        string query = q < 0 ? "" : target.Substring(q + 1);
        int hash = query.IndexOf('#');
        if (hash >= 0) query = query.Substring(0, hash);
        path = Args.Unescape(path).Trim().ToLowerInvariant();
        while (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal)) path = path.Substring(0, path.Length - 1);
        var args = new Args();
        Args.ParseQuery(query, args);
        if (!string.IsNullOrWhiteSpace(body))
        {
            string trimmed = body.Trim();
            bool json = (contentType ?? "").IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0 || trimmed.StartsWith("{", StringComparison.Ordinal);
            bool form = (contentType ?? "").IndexOf("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) >= 0;
            if (json && trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                if (MiniJson.Parse(trimmed) is Dictionary<string, object> map)
                    foreach (var kv in map) args.Set(kv.Key, MiniJson.Scalar(kv.Value));
            }
            else if (form || (trimmed.IndexOf('=') > 0 && trimmed.IndexOf('\n') < 0 && trimmed.IndexOf(' ') < 0))
                Args.ParseQuery(trimmed, args);
            else args.Set("body", body);
        }
        return new AgentRequest(method, path, args, source, line ?? (method + " " + target)) { RawBody = body };
    }

    /// <summary>One command line: "[GET|POST|HEAD] /path?query [body]" (file IPC, /batch, scenarios).</summary>
    internal static AgentRequest FromLine(string line, string source)
    {
        line = (line ?? "").Trim();
        string method = "GET", rest = line;
        int space = line.IndexOf(" ", StringComparison.Ordinal);
        string head = space < 0 ? line : line.Substring(0, space);
        if (head.Equals("GET", StringComparison.OrdinalIgnoreCase) || head.Equals("POST", StringComparison.OrdinalIgnoreCase) || head.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
        {
            method = head.ToUpperInvariant();
            rest = space < 0 ? "/" : line.Substring(space + 1).Trim();
        }
        int bodyAt = rest.IndexOf(" ", StringComparison.Ordinal);
        string target = bodyAt < 0 ? rest : rest.Substring(0, bodyAt);
        string body = bodyAt < 0 ? null : rest.Substring(bodyAt + 1).Trim();
        try
        {
            bool json = body != null && body.StartsWith("{", StringComparison.Ordinal);
            return Create(method, target, json ? "application/json" : null, body, source, line);
        }
        catch (ArgumentException e)
        {
            var bad = new AgentRequest(method, target, null, source, line);
            bad.Json(400, "{\"ok\":false,\"status\":400,\"error\":" + JsonWriter.Quoted(e.Message) + "}");
            return bad;
        }
    }
}

/// <summary>Loopback-only HTTP/1.1 server (Connection: close). Never binds a non-loopback address.</summary>
internal sealed class HttpBridge : IDisposable
{
    private const int MaxHeader = 64 * 1024, MaxBody = 1024 * 1024;
    private readonly Action<AgentRequest> _execute;
    private TcpListener _listener;
    private Thread _thread;
    private volatile bool _stop;
    private int _active;
    internal int Port { get; private set; }
    internal string LastError { get; private set; } = "";
    internal long Served;
    internal int ActiveConnections => Volatile.Read(ref _active);

    internal HttpBridge(Action<AgentRequest> execute) { _execute = execute; }

    internal bool Start(int basePort, int span)
    {
        for (int port = basePort; port < basePort + Math.Max(1, span) && port <= 65535; port++)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
                listener.Start(64);
                _listener = listener; Port = port;
                break;
            }
            catch (SocketException e)
            {
                LastError = "port " + port + ": " + e.SocketErrorCode;
                try { listener?.Stop(); } catch (Exception) { }
            }
        }
        if (_listener == null) return false;
        _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "CT-Agent-HTTP" };
        _thread.Start();
        return true;
    }

    private void AcceptLoop()
    {
        while (!_stop)
        {
            TcpClient client;
            try { client = _listener.AcceptTcpClient(); }
            catch (ObjectDisposedException) { return; }
            catch (InvalidOperationException) { return; }
            catch (SocketException e)
            {
                if (_stop) return;
                LastError = "accept: " + e.SocketErrorCode;
                Thread.Sleep(50);
                continue;
            }
            if (_stop) { try { client.Close(); } catch (Exception) { } return; }
            if (Volatile.Read(ref _active) >= 32)
            {
                // Bounded concurrency: reply busy instead of queueing unbounded threads.
                ThreadPool.QueueUserWorkItem(RejectBusy, client);
                continue;
            }
            Interlocked.Increment(ref _active);
            ThreadPool.QueueUserWorkItem(ServeWork, client);
        }
    }

    private static void RejectBusy(object state) => Reject((TcpClient)state, 503, "too many concurrent connections");

    private void ServeWork(object state)
    {
        try { Serve((TcpClient)state); }
        finally { Interlocked.Decrement(ref _active); }
    }

    private static void Reject(TcpClient client, int code, string message)
    {
        using (client)
        {
            try
            {
                client.SendTimeout = 2000;
                var body = Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":" + JsonWriter.Quoted(message) + "}");
                WriteResponse(client.GetStream(), code, "application/json; charset=utf-8", body, false, 0);
            }
            catch (Exception) { }
        }
    }

    private void Serve(TcpClient client)
    {
        using (client)
        {
            NetworkStream stream = null;
            try
            {
                client.NoDelay = true;
                client.ReceiveTimeout = 10000;
                client.SendTimeout = 15000;
                stream = client.GetStream();
                if (!ReadHead(stream, out string head, out byte[] rest)) return;
                var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                var parts = lines[0].Split(' ');
                if (parts.Length < 2) { Fail(stream, 400, "bad request line"); return; }
                string method = parts[0].ToUpperInvariant(), target = parts[1];
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 1; i < lines.Length; i++)
                {
                    int colon = lines[i].IndexOf(':');
                    if (colon > 0) headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
                }
                // Browser/CSRF guard: real agents (curl, PowerShell, Python) never send these.
                if (headers.ContainsKey("Origin") || headers.ContainsKey("Sec-Fetch-Site") || headers.ContainsKey("Sec-Fetch-Mode"))
                { Fail(stream, 403, "browser requests are refused (Origin/Sec-Fetch headers)"); return; }
                if (headers.TryGetValue("Host", out string host) && !LoopbackHost(host))
                { Fail(stream, 403, "non-loopback Host header refused"); return; }
                if (method != "GET" && method != "POST" && method != "HEAD")
                { Fail(stream, 405, "only GET, POST and HEAD are supported"); return; }
                if (headers.TryGetValue("Transfer-Encoding", out string te) && te.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
                { Fail(stream, 411, "chunked bodies are not supported; send Content-Length"); return; }
                int length = 0;
                if (headers.TryGetValue("Content-Length", out string cl) && (!int.TryParse(cl, out length) || length < 0))
                { Fail(stream, 400, "bad Content-Length"); return; }
                if (length > MaxBody) { Fail(stream, 413, "body exceeds 1 MiB"); return; }
                if (length > rest.Length && headers.TryGetValue("Expect", out string expect) && expect.Equals("100-continue", StringComparison.OrdinalIgnoreCase))
                {
                    var cont = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                    stream.Write(cont, 0, cont.Length);
                }
                var body = new byte[length];
                int have = Math.Min(rest.Length, length);
                Buffer.BlockCopy(rest, 0, body, 0, have);
                while (have < length)
                {
                    int n = stream.Read(body, have, length - have);
                    if (n <= 0) { Fail(stream, 400, "truncated body"); return; }
                    have += n;
                }
                headers.TryGetValue("Content-Type", out string contentType);
                AgentRequest request;
                try { request = RequestFactory.Create(method, target, contentType, Decode(body, body.Length), "http", null); }
                catch (ArgumentException e) { Fail(stream, 400, e.Message); return; }
                try
                {
                    _execute(request);
                    WriteResponse(stream, request.Status, request.ContentType, request.Body, method == "HEAD", request.Id);
                    Interlocked.Increment(ref Served);
                }
                finally { request.Release(); }
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            catch (Exception e)
            {
                LastError = "serve: " + e.GetType().Name + ": " + e.Message;
                try { if (stream != null) Fail(stream, 500, e.Message); } catch (Exception) { }
            }
        }
    }

    private static bool LoopbackHost(string host)
    {
        host = host.Trim().ToLowerInvariant();
        if (host.StartsWith("[", StringComparison.Ordinal))
        {
            int end = host.IndexOf(']');
            host = end > 0 ? host.Substring(1, end - 1) : host;
        }
        else
        {
            int colon = host.LastIndexOf(':');
            if (colon >= 0) host = host.Substring(0, colon);
        }
        return host == "127.0.0.1" || host == "localhost" || host == "::1" || host.Length == 0;
    }

    /// <summary>UTF-8 first; Windows consoles sometimes send CP1251 Cyrillic in raw URLs.</summary>
    internal static string Decode(byte[] bytes, int count)
    {
        if (count == 0) return "";
        try { return new UTF8Encoding(false, true).GetString(bytes, 0, count); }
        catch (ArgumentException)
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(1251).GetString(bytes, 0, count);
            }
            catch (Exception) { return Encoding.UTF8.GetString(bytes, 0, count); }
        }
    }

    private static bool ReadHead(NetworkStream stream, out string head, out byte[] rest)
    {
        head = null; rest = null;
        var buffer = new byte[8192];
        var data = new MemoryStream();
        while (true)
        {
            int n = stream.Read(buffer, 0, buffer.Length);
            if (n <= 0) return false;
            data.Write(buffer, 0, n);
            var all = data.GetBuffer();
            int len = (int)data.Length;
            for (int i = Math.Max(0, len - n - 3); i + 3 < len; i++)
            {
                if (all[i] == 13 && all[i + 1] == 10 && all[i + 2] == 13 && all[i + 3] == 10)
                {
                    head = Decode(all, i);
                    rest = new byte[len - i - 4];
                    Buffer.BlockCopy(all, i + 4, rest, 0, rest.Length);
                    return true;
                }
            }
            if (len > MaxHeader) throw new IOException("headers too large");
        }
    }

    private static void Fail(NetworkStream stream, int code, string message)
    {
        var body = Encoding.UTF8.GetBytes("{\"ok\":false,\"status\":" + code + ",\"error\":" + JsonWriter.Quoted(message) + "}");
        WriteResponse(stream, code, "application/json; charset=utf-8", body, false, 0);
    }

    internal static string Reason(int code) => code switch
    {
        200 => "OK", 202 => "Accepted", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found",
        405 => "Method Not Allowed", 409 => "Conflict", 411 => "Length Required", 413 => "Payload Too Large",
        417 => "Expectation Failed", 429 => "Too Many Requests", 500 => "Internal Server Error", 501 => "Not Implemented",
        503 => "Service Unavailable", 504 => "Gateway Timeout", _ => "Status"
    };

    private static void WriteResponse(Stream stream, int code, string contentType, byte[] body, bool headOnly, long id)
    {
        body ??= Array.Empty<byte>();
        var head = new StringBuilder(256)
            .Append("HTTP/1.1 ").Append(code).Append(' ').Append(Reason(code)).Append("\r\n")
            .Append("Content-Type: ").Append(contentType ?? "application/octet-stream").Append("\r\n")
            .Append("Content-Length: ").Append(body.Length).Append("\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("X-Agent-Request: ").Append(id).Append("\r\n")
            .Append("Connection: close\r\n\r\n");
        var bytes = Encoding.ASCII.GetBytes(head.ToString());
        stream.Write(bytes, 0, bytes.Length);
        if (!headOnly && body.Length > 0) stream.Write(body, 0, body.Length);
        stream.Flush();
    }

    public void Dispose()
    {
        _stop = true;
        try { _listener?.Stop(); } catch (Exception) { }
        try { if (_thread != null && _thread.IsAlive) _thread.Join(500); } catch (Exception) { }
        _listener = null;
    }
}

/// <summary>
/// File IPC fallback: the agent writes ipc\cmd.txt (one command per line, e.g. "GET /state" or
/// "POST /ui/click {\"label\":\"Спавн\"}"). Results are appended to ipc\out.jsonl; the last
/// response body is written atomically to ipc\last.json.
/// </summary>
internal sealed class FileBridge : IDisposable
{
    private readonly string _dir, _command, _processing, _out, _last, _status;
    private readonly Action<AgentRequest> _execute;
    private Thread _thread;
    private volatile bool _stop;
    internal string LastError { get; private set; } = "";
    internal long Processed;
    internal string Directory => _dir;

    internal FileBridge(string dir, Action<AgentRequest> execute)
    {
        _dir = dir; _execute = execute;
        _command = Path.Combine(dir, "cmd.txt");
        _processing = Path.Combine(dir, "cmd.processing");
        _out = Path.Combine(dir, "out.jsonl");
        _last = Path.Combine(dir, "last.json");
        _status = Path.Combine(dir, "ipc-status.txt");
    }

    internal void Start()
    {
        System.IO.Directory.CreateDirectory(_dir);
        // A batch interrupted by a crash is not replayed: it could repeat an action.
        try { if (File.Exists(_processing)) File.Move(_processing, _processing + ".abandoned", true); } catch (Exception) { }
        _thread = new Thread(Loop) { IsBackground = true, Name = "CT-Agent-FileIPC" };
        _thread.Start();
    }

    private void Loop()
    {
        while (!_stop)
        {
            try
            {
                Thread.Sleep(100);
                if (_stop || !File.Exists(_command)) continue;
                if (new FileInfo(_command).Length > 256 * 1024)
                {
                    File.Move(_command, _command + ".rejected", true);
                    LastError = "cmd.txt exceeds 256 KiB; renamed to cmd.txt.rejected";
                    continue;
                }
                try { File.Move(_command, _processing, true); }
                catch (IOException) { continue; } // producer still writing; retry next tick
                string[] lines = File.ReadAllLines(_processing, Encoding.UTF8);
                foreach (var raw in lines)
                {
                    if (_stop) break;
                    string line = raw.Trim().TrimStart('\uFEFF');
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    Run(line);
                }
                try { File.Delete(_processing); } catch (Exception) { }
            }
            catch (Exception e)
            {
                LastError = e.GetType().Name + ": " + e.Message;
                try { File.WriteAllText(_status, DateTime.UtcNow.ToString("O") + " " + LastError); } catch (Exception) { }
                Thread.Sleep(500);
            }
        }
    }

    private void Run(string line)
    {
        AgentRequest request = RequestFactory.FromLine(line, "file");
        try
        {
            if (!request.IsCompleted) _execute(request);
            Record(request);
            Interlocked.Increment(ref Processed);
        }
        finally { request.Release(); }
    }

    private void Record(AgentRequest request)
    {
        string type = request.ContentType ?? "";
        string bodyJson;
        if (type.StartsWith("application/json", StringComparison.Ordinal)) bodyJson = Encoding.UTF8.GetString(request.Body);
        else if (type.StartsWith("text/", StringComparison.Ordinal)) bodyJson = JsonWriter.Quoted(Encoding.UTF8.GetString(request.Body));
        else bodyJson = "{\"binary\":true,\"bytes\":" + request.Body.Length + "}";
        var w = new JsonWriter(bodyJson.Length + 256);
        w.Obj().P("id", request.Id).P("time", DateTime.UtcNow.ToString("O")).P("line", request.Line)
         .P("status", request.Status).P("contentType", type).Name("body").Raw(bodyJson).EndObj();
        string record = w.ToString().Replace("\r", "").Replace("\n", " ");
        try
        {
            if (File.Exists(_out) && new FileInfo(_out).Length > 8L * 1024 * 1024)
                File.Move(_out, Path.Combine(_dir, "out.1.jsonl"), true);
        }
        catch (Exception) { }
        File.AppendAllText(_out, record + "\n", new UTF8Encoding(false));
        string temp = _last + ".tmp";
        File.WriteAllText(temp, w.ToString(), new UTF8Encoding(false));
        File.Move(temp, _last, true);
    }

    public void Dispose()
    {
        _stop = true;
        try { if (_thread != null && _thread.IsAlive) _thread.Join(500); } catch (Exception) { }
    }
}
