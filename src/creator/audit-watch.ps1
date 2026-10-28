param(
 [Parameter(Mandatory=$true)][string]$Steam,
 [Parameter(Mandatory=$true)][string]$UnityLog,
 [Parameter(Mandatory=$true)][string]$ImmutableBaselineSha256,
 [string]$InitialRecoveryRunId='',
 [string]$InitialRecoveryBoot=''
)
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
$g='D:\SteamLibrary\steamapps\common\Slime Rancher 2'
$d=Join-Path $g 'UserData\CreatorToolkit';$state=Join-Path $d 'functional-recovery.json'
$until=(Get-Date).AddMinutes($(if(Test-Path (Join-Path $d 'stress.request')){30}else{12}))
function Require([bool]$ok,[string]$message){if(!$ok){throw $message}}
function Sha([string]$path){
 $h=[Security.Cryptography.SHA256]::Create()
 try{[BitConverter]::ToString($h.ComputeHash([IO.File]::ReadAllBytes($path))).Replace('-','').ToLowerInvariant()}
 finally{$h.Dispose()}
}
function ReadShared([string]$path){
 $stream=New-Object IO.FileStream($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
 $reader=New-Object IO.StreamReader($stream)
 try{$reader.ReadToEnd()}finally{$reader.Dispose();$stream.Dispose()}
}
function ReadRecovery {
 foreach($path in @($state,($state+'.previous'))){
  if(!(Test-Path -LiteralPath $path)){continue}
  try{
   $value=(ReadShared $path)|ConvertFrom-Json
   if($value.RunId -cmatch '^[a-f0-9]{32}$'){return $value}
  }catch{continue}
 }
 throw 'No complete run-correlated recovery header; no automated restart'
}
Require ($ImmutableBaselineSha256 -cmatch '^[a-f0-9]{64}$') 'Watcher requires the original nonempty immutable baseline digest'
$seen=$false;$runId=$InitialRecoveryRunId;$lastCheckpoint=$InitialRecoveryBoot
$launchUntil=(Get-Date).AddSeconds(90);$observedGameSinceLaunch=$false;$pendingReported=$false
while((Get-Date) -lt $until){
 $running=[bool](Get-Process SlimeRancher2 -ErrorAction SilentlyContinue)
 if($running){$observedGameSinceLaunch=$true}
 $hasState=(Test-Path $state) -or (Test-Path ($state+'.previous'))
 if($hasState){
  $seen=$true;$r=ReadRecovery
  Require ($r.ImmutableBaselineSha256 -ceq $ImmutableBaselineSha256) 'Recovery changed the original immutable digest'
  if(!$runId){$runId=$r.RunId}
  Require ($r.RunId -ceq $runId) 'A different audit replaced the watched run'
 }
 if($seen -and !$hasState){
  # State disappearance means protected return, NOT accepted completion.
  $terminalPath=Join-Path $d 'audit-terminal.json'
  Require (Test-Path $terminalPath) 'Recovery disappeared without a durable terminal outcome'
  $terminal=(ReadShared $terminalPath)|ConvertFrom-Json
  Require ($terminal.RunId -ceq $runId -and $terminal.ImmutableBaselineSha256 -ceq $ImmutableBaselineSha256 -and
   $terminal.ReturnCompleted -and $terminal.CopyRetired -and $terminal.OriginalBytesVerified -and
   $terminal.PostExitHashVerificationRequired) 'Terminal outcome is missing, stale, or uncorrelated'
  if($running){
   if(!$pendingReported){[Console]::WriteLine('PROTECTED RETURN; EXIT VERIFICATION PENDING. No save/quit command was sent.');$pendingReported=$true}
   Start-Sleep -Seconds 1;continue
  }
  $final=& "$PSScriptRoot\audit-integrity.ps1" -DataDirectory $d -ExpectedBaselineSha256 $ImmutableBaselineSha256 -RequireNoGame -RequireCopyRetired
  Require ($final.OriginalBytesVerified -and $final.OwnedCopyFiles -eq 0) 'Post-exit immutable originals/copy retirement not verified'
  Require ((Sha (Join-Path $d 'functional.txt')) -ceq $terminal.ReportSha256) 'Final native report changed after its terminal commit'
  $featureAccepted=$terminal.FeatureResult -ceq 'PASS' -and $terminal.Completed -and $terminal.Failed -eq 0 -and !$terminal.ReportWriteFailed
  [ordered]@{
   RunId=$runId;ImmutableBaselineSha256=$ImmutableBaselineSha256;VerifiedUtc=[DateTime]::UtcNow.ToString('o');
   ActualGameProcessCount=0;GameDirectoryProcessCount=0;MatchesApprovedOriginalBaseline=$true;CopyRetired=$true;
   OriginalHashFiles=$final.OriginalHashFiles;FeatureReportAccepted=$featureAccepted;ReleaseAccepted=$false;
   NoProcessKill=$true;NoSaveAndQuitCommand=$true;PostExitVerificationComplete=$true
  }|ConvertTo-Json -Depth 5|Set-Content (Join-Path $d 'audit-post-exit-proof.json') -Encoding UTF8
  Require $featureAccepted 'Post-exit integrity passed, but native feature report failed or remains incomplete; no release acceptance'
  [Console]::WriteLine('Feature report and post-exit pinned integrity passed; visual/performance/release acceptance is separate.')
  exit 0
 }
 if($running){Start-Sleep -Seconds 1;continue}
 if((Get-Date) -lt $launchUntil){Start-Sleep -Seconds 1;continue}
 Require $observedGameSinceLaunch 'No actual game process was observed for the prior launch; never retry Steam launch blindly'
 Require $hasState 'Game exited without an owned-copy recovery/terminal route'
 $immutable=& "$PSScriptRoot\audit-integrity.ps1" -DataDirectory $d -RecoveryFile $state -ExpectedBaselineSha256 $ImmutableBaselineSha256 -RequireNoGame
 Require ($immutable.RecoveryRunId -ceq $runId) 'Recovery cannot replace watched run identity'
 $boot=$immutable.RecoveryBoot
 Require ($boot -cne $lastCheckpoint) 'Same checkpoint repeated without progress; no restart loop'
 $lastCheckpoint=$boot
 $prefix=$UnityLog+'.audit-'+$runId+'-'+$boot
 foreach($entry in @(@((Join-Path $d 'functional.txt'),($prefix+'.txt')),@($UnityLog,($prefix+'.log')),
  @((Join-Path $g 'MelonLoader\Latest.log'),($prefix+'.melon.log')))){
  if(Test-Path $entry[0]){
   Require (!(Test-Path $entry[1])) 'Unique checkpoint evidence already exists; not overwritten'
   Copy-Item -LiteralPath $entry[0] -Destination $entry[1]
  }
 }
 Require (!(Test-Path (Join-Path $d 'cmd.txt'))) 'An unread game command exists; no replacement or blind replay'
 [IO.File]::WriteAllText((Join-Path $d 'selftest.request'),'continue',(New-Object Text.UTF8Encoding($false)))
 Start-Process -FilePath $Steam -ArgumentList ('-applaunch 1657630 --melonloader.hideconsole -logFile "'+$UnityLog+'"')
 $launchUntil=(Get-Date).AddSeconds(90);$observedGameSinceLaunch=$false
}
throw 'Audit remains pending or failed at the launcher deadline. No process kill, main save, or premature acceptance.'