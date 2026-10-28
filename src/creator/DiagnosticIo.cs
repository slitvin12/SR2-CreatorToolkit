using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CreatorToolkit;

// Only strings and filesystem I/O cross this boundary. Unity/IL2CPP/gameplay
// calls remain on the main thread. One in-flight + one latest status snapshot;
// at most 64 ordered commands, consumed once after successful file deletion.
// Self-test markers stay durable until an explicit main-thread acknowledgement;
// repeated discovery is off-thread and only a bounded string is cached.
internal sealed class DiagnosticIo : IDisposable
{
    private readonly object _gate=new();
    private readonly string _commandPath,_statusPath,_temporaryPath,_selftestPath;
    private readonly Queue<string> _commands=new();
    private string _pendingStatus,_selftestOption;
    private long _selftestGeneration;
    private Exception _error;
    private Task _worker;
    private bool _working,_closed;
    internal DiagnosticIo(string directory)
    {
        _commandPath=Path.Combine(directory,"cmd.txt");
        _statusPath=Path.Combine(directory,"status.txt");
        _temporaryPath=Path.Combine(directory,"status.tmp");
        _selftestPath=Path.Combine(directory,"selftest.request");
        // Read once during initialization to preserve opt-in startup/background
        // behavior. Every subsequent discovery happens only in the I/O worker.
        try{ReadSelftestRequest();}catch(Exception e){CaptureError(e);}
    }
    internal string SelftestRequestOption
    {
        get{lock(_gate){return _selftestOption;}}
    }
    internal void InvalidateSelftestRequest()
    {
        // Called AFTER an explicit marker deletion. An already-running read
        // from before that deletion must not resurrect the acknowledged request.
        lock(_gate){_selftestGeneration++;_selftestOption=null;}
    }
    internal void QueueSnapshot(string snapshot)
    {
        lock(_gate)
        {
            if(_closed)return;
            _pendingStatus=snapshot;
            if(_working)return;
            _working=true;_worker=Task.Run(Drain);
        }
    }
    internal string[] TakeCommands()
    {
        lock(_gate)
        {
            if(_commands.Count==0)return Array.Empty<string>();
            var result=_commands.ToArray();_commands.Clear();return result;
        }
    }
    internal Exception TakeError()
    {
        lock(_gate){var error=_error;_error=null;return error;}
    }
    private void Drain()
    {
        for(;;)
        {
            string snapshot;
            lock(_gate)
            {
                snapshot=_pendingStatus;_pendingStatus=null;
                if(snapshot==null){_working=false;return;}
            }
            try{ReadCommandFile();}catch(Exception e){CaptureError(e);}
            try{ReadSelftestRequest();}catch(Exception e){CaptureError(e);}
            try
            {
                File.WriteAllText(_temporaryPath,snapshot);
                // Atomic publication avoids exposing partially truncated status.
                // A short reader without delete-sharing can defer replacement;
                // retries are bounded and never block the game thread.
                for(int attempt=0;;attempt++)
                {
                    try{File.Move(_temporaryPath,_statusPath,true);break;}
                    catch(IOException) when(attempt<5){Thread.Sleep(20);}
                    catch(UnauthorizedAccessException) when(attempt<5){Thread.Sleep(20);}
                }
            }
            catch(Exception e){CaptureError(e);}
        }
    }
    private void CaptureError(Exception error){lock(_gate){_error=error;}}
    private void ReadSelftestRequest()
    {
        long generation;
        lock(_gate){if(_closed)return;generation=_selftestGeneration;}
        string option=null;
        try
        {
            if(File.Exists(_selftestPath))
            {
                if(new FileInfo(_selftestPath).Length>4096)
                    throw new InvalidOperationException("Self-test request exceeds 4 KiB; not consumed.");
                option=File.ReadAllText(_selftestPath).Trim();
            }
        }
        finally
        {
            lock(_gate)
            {
                if(!_closed&&generation==_selftestGeneration)_selftestOption=option;
            }
        }
    }
    private void ReadCommandFile()
    {
        // An external producer may still hold cmd.txt while publishing it.
        // Retry only Windows sharing/lock violations, off the game thread.
        // A durable locked command remains for the next snapshot; no enqueue
        // happens before deletion, so retries cannot duplicate the batch.
        for(int attempt=0;;attempt++)
        {
            try{ReadCommandFileOnce();return;}
            catch(IOException e) when((e.HResult&0xffff)==32||(e.HResult&0xffff)==33)
            {
                if(attempt>=5)return;
                Thread.Sleep(20);
            }
        }
    }
    private void ReadCommandFileOnce()
    {
        if(!File.Exists(_commandPath))return;
        if(new FileInfo(_commandPath).Length>65536)throw new InvalidOperationException("Diagnostic command file exceeds 64 KiB; not consumed.");
        var lines=File.ReadAllLines(_commandPath);
        if(lines.Length>64)throw new InvalidOperationException("Diagnostic command batch exceeds 64 lines; not consumed.");
        lock(_gate){if(_commands.Count+lines.Length>64)return;}
        // Only this worker can add; the main thread can only free queue space.
        // Do not hold the gate during potentially slow disk operations.
        File.Delete(_commandPath);
        lock(_gate){foreach(var line in lines)_commands.Enqueue(line);}
    }
    public void Dispose()
    {
        Task worker;
        lock(_gate){_closed=true;worker=_worker;}
        // Best-effort drain on application quit only, outside gameplay/FPS.
        worker?.Wait(1000);
        lock(_gate){_commands.Clear();_pendingStatus=null;_selftestOption=null;_error=null;_worker=null;}
    }
}