# Call in a detached -NonInteractive PowerShell process. Never save/quit a
# running game automatically: protected audit exit and post-exit hashes are
# separate, explicit steps before this launcher may install or start anything.
param([switch]$Rebuild,[switch]$SelfTest)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$r=(Resolve-Path "$PSScriptRoot\..\..").Path
$g='D:\SteamLibrary\steamapps\common\Slime Rancher 2'
$d=Join-Path $g 'UserData\CreatorToolkit'
$state=Join-Path $d 'functional-recovery.json'
$fullAudit=(Test-Path (Join-Path $d 'functional.request')) -or
 (Test-Path $state) -or (Test-Path ($state+'.previous'))
try {
 if(Get-Process SlimeRancher2 -ErrorAction SilentlyContinue){throw 'SR2 is running. Launcher sends no quit/save command; complete a separately verified protected exit first.'}
 if(@(Get-CimInstance Win32_Process|Where-Object {$_.ExecutablePath -like ($g+'*')}).Count -gt 0){throw 'A game-directory executable is running; no replacement or launch'}
 if($fullAudit -and !$SelfTest){throw 'Protected audit state exists. Omitting -SelfTest never bypasses its integrity/admission gate; no install or launch.'}
 if($SelfTest -and !$fullAudit){throw 'Native selftests require an explicitly requested isolated-copy functional audit; arbitrary/main-world probes are not run.'}
 if($fullAudit){
  $integrityArgs=@{DataDirectory=$d;RequireNoGame=$true}
  if((Test-Path $state) -or (Test-Path ($state+'.previous'))){
   $integrityArgs.RecoveryFile=$state
   $integrityArgs.ExpectedBaselineSha256=[IO.File]::ReadAllText((Join-Path $d 'audit-immutable-baseline.sha256')).Trim()
  }
  else{$integrityArgs.RequireCopyRetired=$true}
  $integrity=& "$PSScriptRoot\audit-integrity.ps1" @integrityArgs
  if(!$integrity.OriginalBytesVerified){throw 'Immutable original hash gate did not return proof'}
  $nativeAuditAdmissionReviewed=$false
  if(!$nativeAuditAdmissionReviewed){throw 'Native audit execution is intentionally blocked in this staged revision: independent startup/write-denial review is pending. No install or launch.'}
 }
 # A command left unread after a crashed/closed process must not affect the new instance.
 Remove-Item (Join-Path $d 'cmd.txt') -ErrorAction SilentlyContinue
 if($Rebuild) {
  & powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PSScriptRoot\release.ps1" -Install
  if($LASTEXITCODE -ne 0){throw 'Build/test/install failed; no launch'}
 }
 if($SelfTest){'continue'|Set-Content (Join-Path $d 'selftest.request') -Encoding UTF8}
 # Launch through the running Steam client to avoid SteamAPI's executable restart.
 # Hide the loader console; never redirect native GUI stdin to an EOF file.
 $steam=(Get-Process steam -ErrorAction SilentlyContinue | Select-Object -First 1).Path
 if(!$steam){$steam=(Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamExe}
 if(!(Test-Path $steam)){throw 'Steam executable was not found; no launch attempted'}
 $unityLog=Join-Path $r '_work\creator-restart-unity.log'
 $arguments='-applaunch 1657630 --melonloader.hideconsole -logFile "'+$unityLog+'"'
 Start-Process -FilePath $steam -ArgumentList $arguments
 Write-Output ('Requested SR2 launch through Steam; Unity log: '+$unityLog)
 'launched'|Set-Content (Join-Path $r '_work\creator-restart.done') -Encoding ASCII
 if($fullAudit){
  & "$PSScriptRoot\audit-watch.ps1" -Steam $steam -UnityLog $unityLog -ImmutableBaselineSha256 $integrity.ImmutableBaselineSha256 -InitialRecoveryRunId $integrity.RecoveryRunId -InitialRecoveryBoot $integrity.RecoveryBoot
  if($LASTEXITCODE -ne 0){throw 'Audit checkpoint launcher stopped or native report failed; no process kill or main save attempted.'}
 }
} catch {
 $_|Out-String|Write-Output
 'failed'|Set-Content (Join-Path $r '_work\creator-restart.done') -Encoding ASCII
 exit 1
}
