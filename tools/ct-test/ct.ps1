# CreatorToolkit live-test harness. Documentation (Russian): tools\ct-test\TESTING.md
#
# Safety by design:
#  * prep/launch refuse while Slime Rancher 2 (or any game-directory process) runs;
#  * prep backs up the whole save folder (hash-verified) and fingerprints every protected save
#    (every *.sav that does not belong to the test save); hashes/finish compare them again;
#  * nothing here edits a save or kills the game; quit asks the mod to save and quit, and only
#    while the test save is loaded;
#  * the mod obeys ct-/m28- commands only while UserData\CreatorToolkit\ct-test.enabled exists,
#    and commands that change a save run only on the dedicated test save ($TestSave, the same
#    name as M26AcceptanceCore.TestGameName); continue never loads any other save.
param(
 [Parameter(Position=0)][string]$Op='help',
 [Parameter(Position=1,ValueFromRemainingArguments=$true)][string[]]$Rest=@(),
 [int]$TimeoutSec=180,
 [string]$Pattern='',
 [switch]$NoInstall,
 [switch]$M27
)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
try{[Console]::OutputEncoding=[Text.Encoding]::UTF8}catch{}
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Game=if($env:SR2_GAME_DIR){$env:SR2_GAME_DIR}else{'D:\SteamLibrary\steamapps\common\Slime Rancher 2'}
$TestSave=if($env:CT_TEST_SAVE){$env:CT_TEST_SAVE}else{'20261020001829_6'}
$Data=Join-Path $Game 'UserData\CreatorToolkit'
$Out=Join-Path $Root '_work\ct-test'
$Shots=Join-Path $Out 'shots'
$Scenarios=Join-Path $PSScriptRoot 'scenarios'
$Baseline=Join-Path $Out 'baseline.json'
$AllOptIns=@('ct-test.enabled','m27-acceptance.enabled')
$Usage=@(
 'ct.ps1 <op> [args]   (powershell -NoProfile -ExecutionPolicy Bypass -File tools\ct-test\ct.ps1 ...)',
 '  prep [-NoInstall] [-M27]  closed game: verified save backup, protected-save baseline, install, opt-in',
 '  launch                    start the game (closed-game launcher) and wait for the main menu',
 '  continue                  main menu -> test save through ct-continue-test, then wait until ready',
 '  menu | ready              wait for the main menu / for gameplay',
 '  cmd <line> [line...]      send IPC lines; ct-/m28- lines print their CtTest result',
 '  status [key...] [-Pattern re]   status.txt values',
 '  shot <name>               game screenshot -> _work\ct-test\shots\<name>.png',
 '  show <file> [lines]       print and copy a UserData\CreatorToolkit file (dump results)',
 '  run <scenario>            tools\ct-test\scenarios\<scenario>.txt -> _work\ct-test\<scenario>.log',
 '  click <x> <y> | key <vk> [holdMs]   real input to the SR2 window (top-left window pixels)',
 '  log [n] [-Pattern re]     MelonLoader Latest.log counts and matching lines',
 '  quit | wait-exit          save and quit through the mod (test save only) / wait for the process',
 '  hashes | finish           compare protected saves / closed game: remove opt-ins and final check'
)
New-Item -ItemType Directory -Force $Shots | Out-Null
$script:LastCt=''
$script:Failures=0

function Running { [bool](Get-Process SlimeRancher2 -ErrorAction SilentlyContinue) }
function Refuse-Running {
 if(Running){throw 'Slime Rancher 2 is running; close it first (ct.ps1 quit)'}
 $procs=@(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($Game,[StringComparison]::OrdinalIgnoreCase) })
 if($procs.Count){throw ('game-directory process running: '+(($procs|ForEach-Object{$_.Name}) -join ','))}
}
function Save-Dir {
 if($env:SR2_SAVE_DIR){return $env:SR2_SAVE_DIR}
 $base=Join-Path $env:USERPROFILE 'AppData\LocalLow\MonomiPark\SlimeRancher2\Steam'
 $hit=Get-ChildItem $base -Directory -ErrorAction SilentlyContinue | Where-Object { @(Get-ChildItem $_.FullName -Filter ($TestSave+'_*.sav')).Count -gt 0 } | Select-Object -First 1
 if(!$hit){throw ('test save '+$TestSave+' not found under '+$base+' (set SR2_SAVE_DIR / CT_TEST_SAVE)')}
 $hit.FullName
}
function Read-Status { $p=Join-Path $Data 'status.txt'; for($i=0;$i -lt 10;$i++){ try{ return [IO.File]::ReadAllText($p) }catch{ Start-Sleep -Milliseconds 60 } }; '' }
function StVal([string]$text,[string]$key){ $m=[regex]::Match($text,'(?m)^'+[regex]::Escape($key)+': ?(.*?)\r?$'); if($m.Success){$m.Groups[1].Value.Trim()}else{'<none>'} }
function StatusAge { $p=Join-Path $Data 'status.txt'; if(Test-Path $p){[int]((Get-Date)-(Get-Item $p).LastWriteTime).TotalSeconds}else{-1} }
function Send-Raw([string[]]$lines){
 $c=Join-Path $Data 'cmd.txt'; $t0=Get-Date
 while(Test-Path $c){ if(((Get-Date)-$t0).TotalSeconds -gt 15){throw 'previous cmd.txt not consumed'}; Start-Sleep -Milliseconds 200 }
 $tmp=Join-Path $Data 'cmd.pending'; [IO.File]::WriteAllLines($tmp,$lines,(New-Object Text.UTF8Encoding $false)); Move-Item $tmp $c -Force
 $t0=Get-Date
 while(Test-Path $c){ if(((Get-Date)-$t0).TotalSeconds -gt 25){throw 'cmd.txt not consumed (game not running or not polling)'}; Start-Sleep -Milliseconds 150 }
}
# One IPC line. For ct-/m28- lines waits for the new CtTest status result.
function Send-Cmd([string]$line){
 $before=StVal (Read-Status) 'CtTest'
 Send-Raw @($line)
 if($line -notmatch '^(ct|m28)-'){ $script:LastCt=''; return ('SENT '+$line) }
 $t0=Get-Date
 while(((Get-Date)-$t0).TotalSeconds -lt 12){
  $now=StVal (Read-Status) 'CtTest'
  if($now -ne $before -and $now -ne '<none>'){ $script:LastCt=$now; return ('CT '+$now) }
  Start-Sleep -Milliseconds 250
 }
 $script:LastCt=''
 'CT (no new result - is ct-test.enabled present? run prep) last='+$before
}
function Wait-Menu {
 $t0=Get-Date
 while($true){
  $p=Get-Process SlimeRancher2 -ErrorAction SilentlyContinue | Select-Object -First 1
  $s=Read-Status;$age=StatusAge
  if($p -and $age -ge 0 -and $age -lt 5 -and (StVal $s 'GameplayScene') -eq 'False' -and (StVal $s 'SceneLoading') -eq 'False' -and (Get-Item (Join-Path $Data 'status.txt')).LastWriteTime -gt $p.StartTime){ return ('MENU READY after '+[int]((Get-Date)-$t0).TotalSeconds+'s') }
  if(((Get-Date)-$t0).TotalSeconds -gt $TimeoutSec){ return ('TIMEOUT menu proc='+[bool]$p+' age='+$age+' gameplay='+(StVal $s 'GameplayScene')) }
  Start-Sleep -Seconds 2
 }
}
function Wait-Ready {
 $t0=Get-Date
 while($true){
  $s=Read-Status
  if((StVal $s 'Ready') -eq 'True' -and (StVal $s 'GameplayScene') -eq 'True' -and (StVal $s 'SceneLoading') -eq 'False' -and (StatusAge) -lt 5){
   $name=StVal $s 'SaveGameName'
   return ('READY after '+[int]((Get-Date)-$t0).TotalSeconds+'s save='+$name+$(if($name -ne $TestSave){' WARNING: not the test save'}else{''}))
  }
  if(((Get-Date)-$t0).TotalSeconds -gt $TimeoutSec){ return ('TIMEOUT ready='+(StVal $s 'Ready')+' gameplay='+(StVal $s 'GameplayScene')+' loading='+(StVal $s 'SceneLoading')) }
  Start-Sleep -Seconds 2
 }
}
# Main menu -> test save through the mod's ct-continue-test (start screen, engagement prompt,
# then BeginLoad only when the continue save IS the test save; anything else is an error).
function Continue-Test {
 # Busy loaders and a main menu that is still starting (no AutoSaveDirector yet) are retried;
 # a continue save that is not the test save, a missing opt-in or a silent relay stop at once.
 for($i=1;$i -le 14;$i++){
  $s=Read-Status
  if((StVal $s 'Ready') -eq 'True' -and (StVal $s 'GameplayScene') -eq 'True'){ break }
  if((StVal $s 'SceneLoading') -eq 'True'){ '  loading...'; Start-Sleep -Seconds 4; continue }
  $r=Send-Cmd 'ct-continue-test'; '  try '+$i+': '+$r
  if($r -match 'not the test save|ignored|no new result'){ throw ('continue stopped: '+$r) }
  if($r -match 'BeginLoad|already in test save'){ break }
  Start-Sleep -Seconds 5
 }
 Wait-Ready
}
function Shot([string]$name){
 if(!$name){throw 'shot <name>'}
 $cap=Join-Path $Data 'capture.png'
 $before=if(Test-Path $cap){(Get-Item $cap).LastWriteTime}else{[datetime]::MinValue}
 Send-Raw @('capture')
 $t0=Get-Date
 while(!(Test-Path $cap) -or (Get-Item $cap).LastWriteTime -le $before){ if(((Get-Date)-$t0).TotalSeconds -gt 20){throw 'capture not written'}; Start-Sleep -Milliseconds 250 }
 Start-Sleep -Milliseconds 700
 $dst=Join-Path $Shots ($name+'.png'); Copy-Item $cap $dst -Force
 Add-Type -AssemblyName System.Drawing; $img=[Drawing.Image]::FromFile($dst); $w=$img.Width;$h=$img.Height;$img.Dispose()
 'SHOT '+$dst+' '+$w+'x'+$h
}
function Show([string]$file,[int]$n=60){
 if(!$file){throw 'show <file> [lines]'}
 $p=Join-Path $Data $file
 if(!(Test-Path $p)){ return ('NO FILE '+$file) }
 Copy-Item $p (Join-Path $Out $file) -Force
 $lines=[IO.File]::ReadAllLines($p,[Text.Encoding]::UTF8)
 '== '+$file+' ('+$lines.Count+' lines, copy in _work\ct-test)'
 $lines | Select-Object -First $n | ForEach-Object { $_.Substring(0,[Math]::Min(240,$_.Length)) }
}
function Desk([string]$action,[int]$vk=0,[int]$hold=100,[int]$x=0,[int]$y=0){
 # Real desktop input to the SR2 window only (src\creator\desktop-task.ps1 through an on-demand task).
 $name='CreatorToolkit-SR2-MCP-Input'
 $service=New-Object -ComObject Schedule.Service;$service.Connect();$folder=$service.GetFolder('\')
 $existing=$null;try{$existing=$folder.GetTask($name)}catch{}
 if($existing -and !$existing.Xml.Contains($Root)){throw 'Refusing to replace an unrelated scheduled task'}
 $task=$service.NewTask(0)
 $task.RegistrationInfo.Description='On-demand CreatorToolkit desktop input/capture for SR2 window only; no schedule.'
 $user=[Security.Principal.WindowsIdentity]::GetCurrent().Name
 $task.Principal.UserId=$user;$task.Principal.LogonType=3;$task.Principal.RunLevel=0
 $task.Settings.AllowDemandStart=$true;$task.Settings.DisallowStartIfOnBatteries=$false;$task.Settings.StopIfGoingOnBatteries=$false
 $task.Settings.ExecutionTimeLimit='PT2M';$task.Settings.MultipleInstances=2
 $a=$task.Actions.Create(0)
 $a.Path=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
 $a.Arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+(Join-Path $Root 'src\creator\desktop-task.ps1')+'" -Action '+$action+' -Vk '+$vk+' -HoldMs '+$hold+' -X '+$x+' -Y '+$y
 $a.WorkingDirectory=$Root
 $log=Join-Path $Root '_work\creator-desktop-task.log'
 Remove-Item $log -ErrorAction SilentlyContinue
 $reg=$folder.RegisterTaskDefinition($name,$task,6,$user,$null,3,$null)
 $null=$reg.Run($null)
 $t0=Get-Date
 Start-Sleep -Milliseconds 400
 while($reg.State -eq 4){ if(((Get-Date)-$t0).TotalSeconds -gt 60){throw 'desktop task timeout'}; Start-Sleep -Milliseconds 200 }
 if(Test-Path $log){(Get-Content $log -Raw).Trim()}else{'(no desktop log)'}
}
function Log-Summary([int]$n=20,[string]$pat=''){
 $ml=Join-Path $Game 'MelonLoader\Latest.log'
 if(!(Test-Path $ml)){ return 'no Latest.log' }
 $all=@(Get-Content $ml -Encoding UTF8)
 $err=@($all | Where-Object { $_ -match '\[ERROR\]|Exception' }); $warn=@($all | Where-Object { $_ -match '\[WARNING\]|\[WARN\]' })
 'LATEST.LOG lines='+$all.Count+' errors/exceptions='+$err.Count+' warnings='+$warn.Count
 $p=if($pat){$pat}else{'CT test|\[M28\]|\[ERROR\]|Exception'}
 $all | Select-String -Pattern $p | Select-Object -Last $n | ForEach-Object { '  '+$_.Line.Substring(0,[Math]::Min(300,$_.Line.Length)) }
}
function Protected-Files([string]$dir){ Get-ChildItem $dir -File -Filter '*.sav' | Where-Object { -not $_.Name.StartsWith($TestSave+'_') } | Sort-Object Name }
function Fingerprint([string]$dir){ @(Protected-Files $dir | ForEach-Object { [pscustomobject]@{Name=$_.Name;Length=$_.Length;Sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash;LastWriteTime=$_.LastWriteTime.ToString('o')} }) }
function Compare-Baseline {
 if(!(Test-Path $Baseline)){ return @{Lines=@('NO BASELINE - run prep first');Ok=0;Bad=1;Since='-';Backup='-'} }
 $b=Get-Content $Baseline -Raw | ConvertFrom-Json
 $now=@{}; foreach($f in (Fingerprint $b.SaveDir)){ $now[$f.Name]=$f }
 $lines=@(); $ok=0; $bad=0
 foreach($f in @($b.Files)){
  $n=$now[$f.Name]
  if(!$n){$bad++;$lines+=('MISSING '+$f.Name)}
  elseif($n.Sha256 -ne $f.Sha256){$bad++;$lines+=('DIFF '+$f.Name+' '+$n.Sha256.Substring(0,16)+' (baseline '+$f.Sha256.Substring(0,16)+')')}
  else{$ok++}
  $now.Remove($f.Name)
 }
 foreach($k in @($now.Keys)){ $lines+=('NEW  '+$k+' (not in the baseline)') }
 @{Lines=$lines;Ok=$ok;Bad=$bad;Since=$b.Time;Backup=$b.Backup}
}
function Expect([bool]$ok,[string]$what){ if($ok){'PASS '+$what}else{$script:Failures++;'FAIL '+$what} }
function Step([string]$line){
 $k,$v=($line.Trim() -split '\s+',2); if($null -eq $v){$v=''}
 $a=@($v -split '\s+' | Where-Object { $_ -ne '' })
 switch($k){
  'cmd'         { Send-Cmd $v }
  'sleep'       { Start-Sleep -Milliseconds ([int]([double]$v*1000)); 'slept '+$v }
  'shot'        { Shot $v }
  'show'        { if($a.Count -gt 1){Show $a[0] ([int]$a[1])}else{Show $a[0]} }
  'status'      { $s=Read-Status; 'AGE '+(StatusAge)+'s'; foreach($x in $a){ $x+': '+(StVal $s $x) } }
  'expect'      { $key,$re=$v -split '\s+',2; $val=StVal (Read-Status) $key; Expect ($val -match $re) ($key+' ~ /'+$re+'/ (value: '+$val.Substring(0,[Math]::Min(200,$val.Length))+')') }
  'expect-ct'   { Expect ($script:LastCt -match $v) ('last CtTest ~ /'+$v+'/') }
  'expect-file' { $f,$re=$v -split '\s+',2; $p=Join-Path $Data $f; Expect ((Test-Path $p) -and ([IO.File]::ReadAllText($p,[Text.Encoding]::UTF8) -match $re)) ($f+' ~ /'+$re+'/') }
  'ready'       { $r=Wait-Ready; $r; if($r -match '^TIMEOUT'){throw $r} }
  'menu'        { $r=Wait-Menu; $r; if($r -match '^TIMEOUT'){throw $r} }
  'continue'    { $r=@(Continue-Test); $r; if($r[-1] -match '^TIMEOUT'){throw $r[-1]} }
  'click'       { Desk 'Click' 0 100 ([int]$a[0]) ([int]$a[1]) }
  'key'         { Desk 'Key' ([int]$a[0]) $(if($a.Count -gt 1){[int]$a[1]}else{100}) 0 0 }
  'log'         { if($a.Count -gt 1){Log-Summary ([int]$a[0]) ($a[1..($a.Count-1)] -join ' ')}elseif($a.Count){Log-Summary ([int]$a[0])}else{Log-Summary} }
  default       { $script:Failures++; 'UNKNOWN STEP '+$line }
 }
}

$exitCode=0
try{
 switch($Op){
  'help' { $Usage; 'Details: tools\ct-test\TESTING.md' }
  'prep' {
   Refuse-Running
   $dir=Save-Dir
   $dst=Join-Path $Root ('backup\ct-saves-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
   New-Item -ItemType Directory -Force $dst | Out-Null
   Copy-Item -Path (Join-Path $dir '*') -Destination $dst -Recurse -Force
   $bad=0;$n=0
   foreach($f in Get-ChildItem $dir -File){$n++; if((Get-FileHash $f.FullName).Hash -ne (Get-FileHash (Join-Path $dst $f.Name)).Hash){$bad++}}
   if($bad){throw ('save backup mismatch '+$bad)}
   'SAVES BACKUP '+$dst+' files='+$n+' verified'
   $files=@(Fingerprint $dir)
   [pscustomobject]@{Time=(Get-Date).ToString('o');SaveDir=$dir;TestSave=$TestSave;Backup=$dst;Files=$files} | ConvertTo-Json -Depth 4 | Set-Content $Baseline -Encoding UTF8
   'BASELINE '+$files.Count+' protected saves (every *.sav except '+$TestSave+'_*)'
   $dll=Join-Path $Game 'Mods\CreatorToolkit.dll'
   if(!$NoInstall){
    'DLL BEFORE '+$(if(Test-Path $dll){(Get-FileHash $dll).Hash.ToLower()}else{'none'})
    & powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $Root 'src\creator\install.ps1') | Select-Object -Last 3
    if($LASTEXITCODE -ne 0){throw 'install failed'}
   }
   'DLL '+(Get-FileHash $dll).Hash.ToLower()
   $optIns=@('ct-test.enabled')+$(if($M27){@('m27-acceptance.enabled')}else{@()})
   foreach($o in $optIns){ ('ct-test session '+(Get-Date -Format s)) | Set-Content (Join-Path $Data $o) -Encoding ASCII; 'OPT-IN '+$o }
  }
  'launch' {
   Refuse-Running
   if(!(Test-Path (Join-Path $Data 'ct-test.enabled'))){'WARNING: ct-test.enabled missing - ct-/m28- commands will be ignored (run prep)'}
   $done=Join-Path $Root '_work\creator-restart.done'; Remove-Item $done -ErrorAction SilentlyContinue
   & powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $Root 'src\creator\dispatch.ps1')
   $t0=Get-Date
   while(!(Test-Path $done)){ if(((Get-Date)-$t0).TotalSeconds -gt 90){throw 'launcher did not finish'}; Start-Sleep -Milliseconds 500 }
   'LAUNCHER: '+(Get-Content $done -Raw).Trim()
   Wait-Menu
  }
  'menu'     { Wait-Menu }
  'ready'    { Wait-Ready }
  'continue' { Continue-Test }
  'cmd'      { if(!$Rest.Count){throw 'cmd <line> [line...]'}; foreach($l in $Rest){ Send-Cmd $l } }
  'status'   {
   $s=Read-Status; 'AGE '+(StatusAge)+'s'
   $keys=if($Rest.Count){$Rest}else{@('Ready','GameplayScene','SceneLoading','SaveGameName','CtTest')}
   foreach($k in $keys){ $k+': '+(StVal $s $k) }
   if($Pattern){ ($s -split "`n") | Where-Object { $_ -match $Pattern } | ForEach-Object { $_.TrimEnd() } | Select-Object -First 40 }
  }
  'shot'     { Shot ($Rest -join '') }
  'show'     { if($Rest.Count -gt 1){Show $Rest[0] ([int]$Rest[1])}else{Show ($Rest -join '')} }
  'click'    { if($Rest.Count -lt 2){throw 'click <x> <y> (top-left window pixel)'}; Desk 'Click' 0 100 ([int]$Rest[0]) ([int]$Rest[1]) }
  'key'      { if(!$Rest.Count){throw 'key <vk> [holdMs]'}; Desk 'Key' ([int]$Rest[0]) $(if($Rest.Count -gt 1){[int]$Rest[1]}else{100}) 0 0 }
  'log'      { Log-Summary $(if($Rest.Count){[int]$Rest[0]}else{20}) $Pattern }
  'run'      {
   if(!$Rest.Count){throw 'run <scenario name or path>'}
   $file=$Rest[0]; if(!(Test-Path $file)){ $file=Join-Path $Scenarios (($Rest[0] -replace '\.txt$','')+'.txt') }
   if(!(Test-Path $file)){throw ('scenario not found: '+$Rest[0])}
   $tag=[IO.Path]::GetFileNameWithoutExtension($file)
   $log=Join-Path $Out ($tag+'.log'); $done=Join-Path $Out ($tag+'.done')
   Remove-Item $done -ErrorAction SilentlyContinue
   ('== '+(Get-Date -Format HH:mm:ss)+' '+$tag) | Set-Content $log -Encoding UTF8
   try{
    foreach($line in (Get-Content $file -Encoding UTF8)){
     $t=$line.Trim(); if(!$t -or $t.StartsWith('#')){continue}
     ('['+(Get-Date -Format HH:mm:ss)+'] '+$t) | Add-Content $log -Encoding UTF8
     try{ Step $t | ForEach-Object { '  '+$_ } | Add-Content $log -Encoding UTF8 }
     catch{ $script:Failures++; ('  ERROR '+$_.Exception.Message) | Add-Content $log -Encoding UTF8; if($t -match '^(continue|ready|menu)'){ throw } }
    }
   }catch{ ('ABORTED '+$_.Exception.Message) | Add-Content $log -Encoding UTF8 }
   finally{ ('== '+(Get-Date -Format HH:mm:ss)+' end failures='+$script:Failures) | Add-Content $log -Encoding UTF8; 'done' | Set-Content $done }
   Get-Content $log -Encoding UTF8
   if($script:Failures){$exitCode=3}
  }
  'quit'     {
   if(!(Running)){'not running'}
   else{
    $s=Read-Status
    if((StVal $s 'GameplayScene') -eq 'True' -and (StVal $s 'SaveGameName') -ne $TestSave){throw ('loaded save '+(StVal $s 'SaveGameName')+' is not the test save; close the game yourself')}
    Send-Raw @('quit'); 'SENT quit (save and quit)'
    $t0=Get-Date; while(Running){ if(((Get-Date)-$t0).TotalSeconds -gt $TimeoutSec){'STILL RUNNING';break}; Start-Sleep -Seconds 2 }
    if(!(Running)){'EXITED after '+[int]((Get-Date)-$t0).TotalSeconds+'s'}
   }
  }
  'wait-exit'{ $t0=Get-Date; while(Running){ if(((Get-Date)-$t0).TotalSeconds -gt $TimeoutSec){'STILL RUNNING';break}; Start-Sleep -Seconds 2 }; if(!(Running)){'NOT RUNNING after '+[int]((Get-Date)-$t0).TotalSeconds+'s'} }
  'hashes'   { $c=Compare-Baseline; $c.Lines; 'PROTECTED SAVES ok='+$c.Ok+' bad='+$c.Bad+' (baseline '+$c.Since+')'; if($c.Bad){$exitCode=2} }
  'finish'   {
   if(Running){throw 'Slime Rancher 2 is still running; use ct.ps1 quit first'}
   foreach($o in $AllOptIns){ $p=Join-Path $Data $o; if(Test-Path $p){Remove-Item $p -Force; 'OPT-IN REMOVED '+$o}else{'opt-in absent '+$o} }
   $c=Compare-Baseline; $c.Lines
   'PROTECTED SAVES ok='+$c.Ok+' bad='+$c.Bad+' (baseline '+$c.Since+', backup '+$c.Backup+')'
   $dir=Save-Dir
   Get-ChildItem $dir -Filter ($TestSave+'_*.sav') | Sort-Object LastWriteTime -Descending | Select-Object -First 2 | ForEach-Object { 'TEST SAVE '+$_.Name+' '+$_.LastWriteTime.ToString('s') }
   Log-Summary 12 '\[ERROR\]|Exception'
   'DLL '+(Get-FileHash (Join-Path $Game 'Mods\CreatorToolkit.dll')).Hash.ToLower()
   if($c.Bad){$exitCode=2}
  }
  default { throw ('unknown op '+$Op+'; see ct.ps1 help') }
 }
}catch{ 'ERROR: '+$_.Exception.Message; $exitCode=1 }
exit $exitCode
