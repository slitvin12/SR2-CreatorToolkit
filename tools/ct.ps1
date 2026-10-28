# ct — CLI for the Creator Toolkit Agent Director bridge (Slime Rancher 2, MelonLoader).
# Usage: ct help. Exit codes: 0 = 2xx, 1 = usage/local error, 2 = bridge answered with an error (4xx/5xx, 417 = check failed),
# 3 = bridge unreachable (game not running, still loading or HTTP disabled).
# Environment: SR2_DIR (game folder), CT_PORT (override port), CT_IPC=0 (no file-IPC fallback).
$ErrorActionPreference = "Stop"
$utf8 = New-Object System.Text.UTF8Encoding($false)
try { [Console]::OutputEncoding = $utf8 } catch { }
$OutputEncoding = $utf8
$SteamAppId = "1657630"
$ProcessName = "SlimeRancher2"

function Find-GameDir {
    if ($env:SR2_DIR) { return $env:SR2_DIR }
    $candidates = New-Object System.Collections.Generic.List[string]
    $candidates.Add("D:\SteamLibrary\steamapps\common\Slime Rancher 2")
    try {
        $steam = [Microsoft.Win32.Registry]::GetValue("HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", $null)
        if ($steam) {
            $steam = ([string]$steam).Replace("/", "\")
            $candidates.Add((Join-Path $steam "steamapps\common\Slime Rancher 2"))
            $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
            if (Test-Path -LiteralPath $vdf) {
                foreach ($m in [regex]::Matches([IO.File]::ReadAllText($vdf), "`"path`"\s+`"([^`"]+)`"")) {
                    $lib = $m.Groups[1].Value.Replace("\\", "\")
                    $candidates.Add((Join-Path $lib "steamapps\common\Slime Rancher 2"))
                }
            }
        }
    } catch { }
    foreach ($c in $candidates) { if (Test-Path -LiteralPath (Join-Path $c "SlimeRancher2.exe")) { return $c } }
    return $candidates[0]
}

$Game = Find-GameDir
$DataDir = Join-Path $Game "UserData\CreatorToolkitAgent"
$OptInFile = Join-Path $Game "UserData\CreatorToolkit\ct-test.enabled"

function Get-Port {
    if ($env:CT_PORT) { return [int]$env:CT_PORT }
    $bridge = Join-Path $DataDir "bridge.json"
    if (Test-Path -LiteralPath $bridge) {
        try {
            $j = [IO.File]::ReadAllText($bridge) | ConvertFrom-Json
            if ($j.port) { return [int]$j.port }
        } catch { }
    }
    return 9898
}
$Port = Get-Port

function Write-Text([string]$text) {
    if ($null -eq $text) { return }
    [Console]::Out.Write($text)
    if (-not $text.EndsWith("`n")) { [Console]::Out.WriteLine() }
}

function Escape([string]$s) { return [Uri]::EscapeDataString($s) }

# "k=v" tokens -> ?k=v&... (UTF-8 percent-encoding)
function Build-Query($pairs) {
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($p in $pairs) {
        $s = [string]$p
        $eq = $s.IndexOf("=")
        if ($eq -lt 0) { $parts.Add((Escape $s) + "=1") }
        else { $parts.Add((Escape $s.Substring(0, $eq)) + "=" + (Escape $s.Substring($eq + 1))) }
    }
    if ($parts.Count -eq 0) { return "" }
    return "?" + [string]::Join("&", $parts)
}

function Test-Pair([string]$s) { return $s -match "^[A-Za-z][A-Za-z0-9_]*=" }

# Splits CLI tokens into positional words and k=v pairs.
function Split-Tokens($tokens) {
    $pos = New-Object System.Collections.Generic.List[string]
    $kv = New-Object System.Collections.Generic.List[string]
    foreach ($t in $tokens) {
        $s = [string]$t
        if (Test-Pair $s) { $kv.Add($s) } else { $pos.Add($s) }
    }
    return @{ Pos = $pos; Kv = $kv }
}

function Get-TimeoutFor($kv, [int]$fallback) {
    foreach ($p in $kv) {
        if ($p -match "^timeout=(\d+)") { return [int]$Matches[1] + 20 }
    }
    return $fallback
}

# Returns @{ Status; Type; Bytes; Text; Error }; Status 0 = unreachable.
function Invoke-Bridge([string]$method, [string]$target, $body, $contentType, [int]$timeoutSec) {
    $url = "http://127.0.0.1:" + $Port + $target
    $req = [System.Net.HttpWebRequest]::Create($url)
    $req.Method = $method
    $req.Proxy = $null
    $req.KeepAlive = $false
    $req.Timeout = $timeoutSec * 1000
    $req.ReadWriteTimeout = $timeoutSec * 1000
    $req.ServicePoint.Expect100Continue = $false
    $resp = $null
    try {
        if ($null -ne $body) {
            $data = $utf8.GetBytes($body)
            if ($contentType) { $req.ContentType = $contentType } else { $req.ContentType = "text/plain; charset=utf-8" }
            $req.ContentLength = $data.Length
            $stream = $req.GetRequestStream()
            $stream.Write($data, 0, $data.Length)
            $stream.Close()
        } elseif ($method -eq "POST") { $req.ContentLength = 0 }
        $resp = $req.GetResponse()
    } catch {
        $ex = $_.Exception
        while ($null -ne $ex -and -not ($ex -is [System.Net.WebException])) { $ex = $ex.InnerException }
        if ($null -eq $ex) { return @{ Status = 0; Error = $_.Exception.Message } }
        if ($null -eq $ex.Response) { return @{ Status = 0; Error = ([string]$ex.Status + ": " + $ex.Message) } }
        $resp = $ex.Response
    }
    try {
        $ms = New-Object System.IO.MemoryStream
        $rs = $resp.GetResponseStream()
        $rs.CopyTo($ms)
        $rs.Close()
        $bytes = $ms.ToArray()
        $type = [string]$resp.ContentType
        $text = $null
        if (-not $type.StartsWith("image/")) { $text = $utf8.GetString($bytes) }
        return @{ Status = [int]$resp.StatusCode; Type = $type; Bytes = $bytes; Text = $text }
    } finally { $resp.Close() }
}

function Get-GameProcess { return @([System.Diagnostics.Process]::GetProcessesByName($ProcessName)) }

# File IPC: write ipc\cmd.txt atomically, wait for the matching record in ipc\out.jsonl.
function Invoke-Ipc([string]$line, [int]$timeoutSec) {
    $dir = Join-Path $DataDir "ipc"
    if (-not (Test-Path -LiteralPath $dir)) { return $null }
    $cmd = Join-Path $dir "cmd.txt"
    $out = Join-Path $dir "out.jsonl"
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSec)
    while ((Test-Path -LiteralPath $cmd) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    $start = 0L
    if (Test-Path -LiteralPath $out) { $start = (Get-Item -LiteralPath $out).Length }
    $tmp = Join-Path $dir ("ct-" + $PID + ".tmp")
    [IO.File]::WriteAllText($tmp, $line + "`n", $utf8)
    [IO.File]::Move($tmp, $cmd)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 150
        if (-not (Test-Path -LiteralPath $out)) { continue }
        $fs = New-Object System.IO.FileStream($out, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            if ($fs.Length -lt $start) { $start = 0L }
            [void]$fs.Seek($start, [System.IO.SeekOrigin]::Begin)
            $reader = New-Object System.IO.StreamReader($fs, $utf8)
            $chunk = $reader.ReadToEnd()
        } finally { $fs.Close() }
        foreach ($rec in $chunk.Split([char]10)) {
            if ($rec.Trim().Length -eq 0) { continue }
            try { $o = $rec | ConvertFrom-Json } catch { continue }
            if (([string]$o.line).Trim() -eq $line.Trim()) { return @{ Status = [int]$o.status; Text = $rec; Type = "application/json"; Ipc = $true } }
        }
    }
    return @{ Status = 0; Error = "file IPC: no answer within " + $timeoutSec + " s (is the game focused and running?)" }
}

# Runs one request, prints the reply, returns the exit code.
function Send([string]$method, [string]$path, $kv, $body = $null, [int]$timeoutSec = 0, $contentType = $null) {
    if ($timeoutSec -le 0) { $timeoutSec = Get-TimeoutFor $kv 120 }
    $qs = Build-Query $kv
    if ($qs.Length -gt 0 -and $path.Contains("?")) { $qs = "&" + $qs.Substring(1) }
    $target = $path + $qs
    $r = Invoke-Bridge $method $target $body $contentType $timeoutSec
    if ($r.Status -eq 0 -and $env:CT_IPC -ne "0" -and $null -eq $body -and (Get-GameProcess).Count -gt 0) {
        $ipc = Invoke-Ipc ($method + " " + $target) $timeoutSec
        if ($null -ne $ipc) {
            if ($ipc.Status -ne 0) { [Console]::Error.WriteLine("(HTTP unreachable, answered through file IPC)") }
            $r = $ipc
        }
    }
    if ($r.Status -eq 0) {
        [Console]::Error.WriteLine("Bridge unreachable on 127.0.0.1:" + $Port + " - " + $r.Error)
        if ((Get-GameProcess).Count -eq 0) { [Console]::Error.WriteLine("SlimeRancher2.exe is not running: ct launch") }
        else { [Console]::Error.WriteLine("The game is running: still loading, mod not installed or HttpEnabled=false (see ct status).") }
        return 3
    }
    if ($null -ne $r.Text) { Write-Text $r.Text }
    else { Write-Text ("<" + $r.Type + ", " + $r.Bytes.Length + " bytes>") }
    if ($r.Status -ge 200 -and $r.Status -lt 300) { return 0 }
    return 2
}

function Show-Usage {
    Write-Text @"
ct - Creator Toolkit Agent Director CLI (SR2). Port: $Port. Game: $Game
Any extra k=v token is passed as a query parameter (Cyrillic is encoded automatically).
Add shot=NAME [marks=imgui|canvas|all] [scale=0.5] to any game command to get a screenshot right after it.

  ct status                       local check: process, bridge.json, ping, opt-in
  ct launch [seconds]             start SR2 through Steam and wait for the bridge (default 300 s)
  ct kill                         terminate SlimeRancher2.exe (unsaved progress is lost)
  ct ping | state [k=v] | api     liveness / full state / HTTP cheat sheet (/help)
  ct ui [label] [kind=tab]        visible Creator Toolkit IMGUI elements (canvas=1 adds native uGUI)
  ct canvas [tree=1]              native uGUI selectables (their id = cid)
  ct click <label|id|cN|x,y>      click: label (falls back to native text=), IMGUI id, native cid, screen pixels
  ct menu [open|close|toggle]     ct tab <0..7|name>
  ct shot [name] [marks|imgui|canvas|grid|small]   screenshot (+ annotated <name>.view.png and legend)
  ct png [name] [file.png] [view] download a screenshot (view = annotated copy)
  ct wait <until> [arg] [k=v]     ready | gameplay | menu | nomenu | tab N | ui LABEL | noui LABEL | stable [N] | near [POINT] | noerrors
  ct assert <checks>              noerrors menu=open tab=2 ui=A^|B noui=X fps=30 near=test ... (exit 2 when a check fails)
  ct errors [reset] | log [text]  problems since the last reset / message feed
  ct smoke [k=v]                  run scenario smoke (all tabs, screenshots, no new errors)
  ct run <scenario> [k=v]         run UserData\CreatorToolkitAgent\scenarios\<scenario>.txt
  ct batch <file|"line;;line">    run local lines through /batch (stop=0 brief=1 delay=ms)
  ct tk "<cmd;cmd>" [k=v]         relay to Creator Toolkit DiagnosticIo (until=m28 status=1)
  ct spawnlist [filter] | spawn <item> [count]       test spawning (opt-in + test save)
  ct point [name] | points | reset | tp <point|x,y,z>  test points and teleport (opt-in + test save)
  ct optin on|off|status          create/remove UserData\CreatorToolkit\ct-test.enabled
  ct get|post <path?query> [body] raw request; ct ipc "<line>" forces file IPC
Exit codes: 0 ok, 1 usage, 2 bridge error / failed check, 3 bridge unreachable.
"@
}

function Show-Status {
    $procs = Get-GameProcess
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("game dir   : " + $Game + $(if (Test-Path -LiteralPath (Join-Path $Game "SlimeRancher2.exe")) { "" } else { "  (SlimeRancher2.exe not found, set SR2_DIR)" }))
    $mod = Join-Path $Game "Mods\CreatorToolkitAgentDirector.dll"
    if (Test-Path -LiteralPath $mod) {
        $info = Get-Item -LiteralPath $mod
        $lines.Add("mod        : " + $mod + " (" + $info.Length + " bytes, " + $info.LastWriteTime.ToString("yyyy-MM-dd HH:mm") + ")")
    } else { $lines.Add("mod        : NOT INSTALLED (" + $mod + ")") }
    if ($procs.Count -gt 0) { $lines.Add("process    : running, pid " + [string]::Join(",", @($procs | ForEach-Object { $_.Id }))) }
    else { $lines.Add("process    : not running") }
    $lines.Add("port       : " + $Port)
    $lines.Add("opt-in     : " + $(if (Test-Path -LiteralPath $OptInFile) { "on" } else { "off (ct optin on)" }))
    foreach ($l in $lines) { Write-Text $l }
    $r = Invoke-Bridge "GET" "/ping" $null $null 5
    if ($r.Status -eq 0) { Write-Text ("bridge     : unreachable (" + $r.Error + ")"); return 3 }
    Write-Text ("bridge     : HTTP " + $r.Status + " " + $r.Text)
    if ($r.Status -ge 200 -and $r.Status -lt 300) { return 0 }
    return 2
}

function Invoke-Launch([int]$seconds) {
    $r = Invoke-Bridge "GET" "/ping" $null $null 5
    if ($r.Status -ne 0) { Write-Text $r.Text; return 0 }
    if ((Get-GameProcess).Count -eq 0) {
        [Console]::Error.WriteLine("Starting Slime Rancher 2 through Steam...")
        [void][System.Diagnostics.Process]::Start("steam://rungameid/" + $SteamAppId)
    } else { [Console]::Error.WriteLine("SlimeRancher2.exe is already running, waiting for the bridge...") }
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 2
        $script:Port = Get-Port
        $r = Invoke-Bridge "GET" "/ping" $null $null 5
        if ($r.Status -ne 0) { Write-Text $r.Text; [Console]::Error.WriteLine("Bridge is up. Next: ct wait ready timeout=180"); return 0 }
    }
    [Console]::Error.WriteLine("Bridge did not answer within " + $seconds + " s.")
    return 3
}

function Invoke-Kill {
    $procs = Get-GameProcess
    if ($procs.Count -eq 0) { Write-Text "SlimeRancher2.exe is not running"; return 0 }
    foreach ($p in $procs) {
        try { $p.Kill(); [void]$p.WaitForExit(15000); Write-Text ("killed pid " + $p.Id) }
        catch { [Console]::Error.WriteLine("cannot kill pid " + $p.Id + ": " + $_.Exception.Message); return 1 }
    }
    return 0
}

function Invoke-OptIn([string]$mode) {
    switch ($mode) {
        "on" {
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OptInFile))
            if (-not (Test-Path -LiteralPath $OptInFile)) { [IO.File]::WriteAllText($OptInFile, "", $utf8) }
            Write-Text ("opt-in on: " + $OptInFile)
        }
        "off" {
            if (Test-Path -LiteralPath $OptInFile) { [IO.File]::Delete($OptInFile) }
            Write-Text "opt-in off"
        }
        default { Write-Text ("opt-in " + $(if (Test-Path -LiteralPath $OptInFile) { "on" } else { "off" }) + ": " + $OptInFile) }
    }
    return 0
}

function Invoke-Png($pos, $kv) {
    $name = "agent"; $file = $null; $view = $false
    foreach ($p in $pos) {
        if ($p -eq "view") { $view = $true }
        elseif ($p.ToLowerInvariant().EndsWith(".png")) { $file = $p }
        else { $name = $p }
    }
    $q = New-Object System.Collections.Generic.List[string]
    $q.Add("name=" + $name)
    if ($view) { $q.Add("view=1") }
    foreach ($p in $kv) { $q.Add($p) }
    $r = Invoke-Bridge "GET" ("/screenshot.png" + (Build-Query $q)) $null $null 30
    if ($r.Status -eq 0) { [Console]::Error.WriteLine("Bridge unreachable: " + $r.Error); return 3 }
    if ($r.Status -ne 200) { Write-Text $r.Text; return 2 }
    if (-not $file) { $file = $name + $(if ($view) { ".view.png" } else { ".png" }) }
    $full = [IO.Path]::GetFullPath($file)
    [IO.File]::WriteAllBytes($full, $r.Bytes)
    Write-Text ("saved " + $full + " (" + $r.Bytes.Length + " bytes)")
    return 0
}

function Invoke-Click($pos, $kv) {
    $q = New-Object System.Collections.Generic.List[string]
    $label = $null
    if ($pos.Count -gt 0) {
        $t = $pos[0]
        if ($t -match "^#?(\d+)$") { $q.Add("id=" + $Matches[1]) }
        elseif ($t -match "^[cC](\d+)$") { $q.Add("cid=" + $Matches[1]) }
        elseif ($t -match "^(\d+)[,;](\d+)$") { $q.Add("x=" + $Matches[1]); $q.Add("y=" + $Matches[2]) }
        else { $label = [string]::Join(" ", $pos); $q.Add("label=" + $label) }
    }
    foreach ($p in $kv) { $q.Add($p) }
    if ($null -eq $label) { return Send "POST" "/ui/click" $q }
    $target = "/ui/click" + (Build-Query $q)
    $r = Invoke-Bridge "POST" $target $null $null (Get-TimeoutFor $kv 60)
    if ($r.Status -eq 404) {
        # Not a Creator Toolkit element: try a native uGUI button with this text.
        $q2 = New-Object System.Collections.Generic.List[string]
        $q2.Add("text=" + $label)
        foreach ($p in $kv) { if (-not $p.StartsWith("kind=")) { $q2.Add($p) } }
        $r2 = Invoke-Bridge "POST" ("/ui/click" + (Build-Query $q2)) $null $null (Get-TimeoutFor $kv 60)
        if ($r2.Status -ne 404 -and $r2.Status -ne 0) { [Console]::Error.WriteLine("(no IMGUI label match; clicked native uGUI text)"); $r = $r2 }
    }
    if ($r.Status -eq 0) { return Send "POST" "/ui/click" $q }
    Write-Text $r.Text
    if ($r.Status -ge 200 -and $r.Status -lt 300) { return 0 }
    return 2
}

function Invoke-Batch($pos, $kv) {
    if ($pos.Count -eq 0) { [Console]::Error.WriteLine("ct batch <file.txt | ""line;;line"" | scenario>"); return 1 }
    $arg = [string]::Join(" ", $pos)
    $q = New-Object System.Collections.Generic.List[string]
    foreach ($p in $kv) { $q.Add($p) }
    if (Test-Path -LiteralPath $arg -PathType Leaf) {
        $body = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $arg).Path, $utf8) + "`n"
        return Send "POST" "/batch" $q $body (Get-TimeoutFor $kv 1800)
    }
    if ($arg.Contains(";;") -or $arg.Contains(" ") -or $arg.StartsWith("/")) {
        return Send "POST" "/batch" $q ($arg.Replace(";;", "`n") + "`n") (Get-TimeoutFor $kv 1800)
    }
    $q.Insert(0, "file=" + $arg)
    return Send "POST" "/batch" $q $null (Get-TimeoutFor $kv 1800)
}

function Add-Flags($q, $pos, [int]$from) {
    for ($i = $from; $i -lt $pos.Count; $i++) { $q.Add($pos[$i]) }
}

function Main($argv) {
    if ($argv.Count -eq 0) { Show-Usage; return 0 }
    $cmd = ([string]$argv[0]).ToLowerInvariant()
    $rest = @()
    if ($argv.Count -gt 1) { $rest = $argv[1..($argv.Count - 1)] }
    $t = Split-Tokens $rest
    $pos = $t.Pos; $kv = $t.Kv
    $q = New-Object System.Collections.Generic.List[string]
    switch ($cmd) {
        { $_ -in "help", "-h", "--help", "/?" } { Show-Usage; return 0 }
        "api" { return Send "GET" "/help" $kv }
        "status" { return Show-Status }
        "launch" { $s = 300; if ($pos.Count -gt 0) { $s = [int]$pos[0] }; return Invoke-Launch $s }
        "kill" { return Invoke-Kill }
        "optin" { $m = "status"; if ($pos.Count -gt 0) { $m = $pos[0].ToLowerInvariant() }; return Invoke-OptIn $m }
        "ping" { return Send "GET" "/ping" $kv $null 10 }
        "state" { return Send "GET" "/state" $kv }
        "ui" {
            if ($pos.Count -gt 0) { $q.Add("label=" + [string]::Join(" ", $pos)) }
            foreach ($p in $kv) { $q.Add($p) }
            return Send "GET" "/ui" $q
        }
        "canvas" { return Send "GET" "/ui/canvas" $kv }
        "click" { return Invoke-Click $pos $kv }
        "menu" {
            $a = "toggle"; if ($pos.Count -gt 0) { $a = $pos[0] }
            $q.Add("action=" + $a); foreach ($p in $kv) { $q.Add($p) }
            return Send "POST" "/menu" $q
        }
        "tab" {
            if ($pos.Count -gt 0) {
                $v = [string]::Join(" ", $pos)
                if ($v -match "^\d+$") { $q.Add("index=" + $v) } else { $q.Add("name=" + $v) }
            }
            foreach ($p in $kv) { $q.Add($p) }
            return Send "POST" "/tab" $q
        }
        { $_ -in "shot", "screenshot" } {
            $name = "agent"
            foreach ($p in $pos) {
                switch ($p.ToLowerInvariant()) {
                    { $_ -in "marks", "all" } { $q.Add("marks=all") }
                    "imgui" { $q.Add("marks=imgui") }
                    "canvas" { $q.Add("marks=canvas") }
                    "grid" { $q.Add("grid=1") }
                    { $_ -in "small", "half" } { $q.Add("scale=0.5") }
                    default { $name = $p }
                }
            }
            $q.Insert(0, "name=" + $name)
            foreach ($p in $kv) { $q.Add($p) }
            return Send "POST" "/screenshot" $q
        }
        "png" { return Invoke-Png $pos $kv }
        "wait" {
            if ($pos.Count -gt 0) {
                $u = $pos[0].ToLowerInvariant()
                $q.Add("until=" + $u)
                if ($pos.Count -gt 1) {
                    $arg = [string]::Join(" ", $pos.GetRange(1, $pos.Count - 1))
                    switch ($u) {
                        { $_ -in "ui", "noui" } { $q.Add("label=" + $arg) }
                        "near" { $q.Add("point=" + $arg) }
                        "tab" { $q.Add("tab=" + $arg) }
                        "stable" { $q.Add("quiet=" + $arg) }
                        default { $q.Add($arg) }
                    }
                }
            }
            foreach ($p in $kv) { $q.Add($p) }
            return Send "GET" "/wait" $q $null (Get-TimeoutFor $kv 330)
        }
        "assert" {
            foreach ($p in $pos) { $q.Add($p + "=1") }
            foreach ($p in $kv) { $q.Add($p) }
            return Send "GET" "/assert" $q
        }
        "errors" {
            if ($pos.Count -gt 0 -and $pos[0] -eq "reset") { return Send "POST" "/errors/reset" $kv }
            return Send "GET" "/errors" $kv
        }
        "log" {
            if ($pos.Count -gt 0) { $q.Add("contains=" + [string]::Join(" ", $pos)) }
            foreach ($p in $kv) { $q.Add($p) }
            return Send "GET" "/log" $q
        }
        { $_ -in "tk", "toolkit" } {
            if ($pos.Count -gt 0) { $q.Add("cmd=" + [string]::Join(" ", $pos)) }
            foreach ($p in $kv) { $q.Add($p) }
            return Send "POST" "/toolkit" $q
        }
        "smoke" {
            $q.Add("file=smoke"); $q.Add("brief=1"); foreach ($p in $kv) { $q.Add($p) }
            return Send "POST" "/batch" $q $null (Get-TimeoutFor $kv 1800)
        }
        "run" {
            if ($pos.Count -eq 0) { [Console]::Error.WriteLine("ct run <scenario>"); return 1 }
            $q.Add("file=" + $pos[0]); $q.Add("brief=1"); foreach ($p in $kv) { $q.Add($p) }
            return Send "POST" "/batch" $q $null (Get-TimeoutFor $kv 1800)
        }
        "batch" { return Invoke-Batch $pos $kv }
        "spawnlist" {
            if ($pos.Count -gt 0) { $q.Add("filter=" + [string]::Join(" ", $pos)) }
            foreach ($p in $kv) { $q.Add($p) }
            return Send "GET" "/spawn/list" $q
        }
        "spawn" {
            if ($pos.Count -gt 0) {
                $last = $pos[$pos.Count - 1]
                if ($pos.Count -gt 1 -and $last -match "^\d+$") {
                    $q.Add("item=" + [string]::Join(" ", $pos.GetRange(0, $pos.Count - 1))); $q.Add("count=" + $last)
                } else { $q.Add("item=" + [string]::Join(" ", $pos)) }
            }
            foreach ($p in $kv) { $q.Add($p) }
            return Send "POST" "/spawn" $q
        }
        "point" {
            $n = "test"; if ($pos.Count -gt 0) { $n = $pos[0] }
            $q.Add("name=" + $n)
            $explicit = $false
            foreach ($p in $kv) { if ($p -match "^(x|y|z|set|delete)=") { $explicit = $true } }
            if (-not $explicit) { $q.Add("set=1") }
            foreach ($p in $kv) { $q.Add($p) }
            return Send "POST" "/testpoint" $q
        }
        "points" { return Send "GET" "/testpoints" $kv }
        "reset" { return Send "POST" "/reset" $kv }
        { $_ -in "tp", "teleport" } {
            if ($pos.Count -gt 0) {
                if ($pos[0] -match "^(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)$") { $q.Add("x=" + $Matches[1]); $q.Add("y=" + $Matches[2]); $q.Add("z=" + $Matches[3]) }
                else { $q.Add("point=" + $pos[0]) }
            }
            foreach ($p in $kv) { $q.Add($p) }
            return Send "POST" "/teleport" $q
        }
        { $_ -in "get", "post" } {
            if ($pos.Count -eq 0) { [Console]::Error.WriteLine("ct get|post </path?query> [body]"); return 1 }
            $path = $pos[0]; if (-not $path.StartsWith("/")) { $path = "/" + $path }
            $body = $null
            if ($pos.Count -gt 1) { $body = [string]::Join(" ", $pos.GetRange(1, $pos.Count - 1)) }
            $ct = $null; if ($null -ne $body -and $body.TrimStart().StartsWith("{")) { $ct = "application/json" }
            return Send $cmd.ToUpperInvariant() $path $kv $body (Get-TimeoutFor $kv 330) $ct
        }
        "ipc" {
            if ($pos.Count -eq 0) { [Console]::Error.WriteLine("ct ipc ""GET /state?ui=0"""); return 1 }
            $line = [string]::Join(" ", $pos)
            $r = Invoke-Ipc $line (Get-TimeoutFor $kv 60)
            if ($null -eq $r) { [Console]::Error.WriteLine("No ipc folder: " + (Join-Path $DataDir "ipc")); return 3 }
            if ($r.Status -eq 0) { [Console]::Error.WriteLine($r.Error); return 3 }
            Write-Text $r.Text
            if ($r.Status -ge 200 -and $r.Status -lt 300) { return 0 }
            return 2
        }
        default {
            if ($cmd.StartsWith("/")) { return Send "GET" $cmd $kv $null (Get-TimeoutFor $kv 330) }
            [Console]::Error.WriteLine("Unknown command: " + $cmd + " (ct help)")
            return 1
        }
    }
}

try {
    $code = Main $args
    if ($code -is [array]) { $code = $code[$code.Length - 1] }
} catch { [Console]::Error.WriteLine("ct: " + $_.Exception.Message); $code = 1 }
exit [int]$code
