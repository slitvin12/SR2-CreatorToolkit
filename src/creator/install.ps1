# Standalone installer. No process termination, game launch, or save-file edits.
# A running game can receive a NEW, currently unused CreatorToolkit.dll only.
# Replacement and reversible retirement of MinecraftHUD require SR2 to be closed.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
$game = if($env:SR2_GAME_DIR){$env:SR2_GAME_DIR}else{'D:\SteamLibrary\steamapps\common\Slime Rancher 2'}
$mods = Join-Path $game 'Mods'
$source = Join-Path $root '_work\creator-stage\CreatorToolkit.dll'
$target = Join-Path $mods 'CreatorToolkit.dll'
$bridge = Join-Path $mods 'MinecraftHUD.dll'
$testSummary = Join-Path $root '_work\creator-tests.summary.txt'
$statusPath = Join-Path $root '_work\creator-install.json'
$temp = Join-Path $mods 'CreatorToolkit.dll.pending'
$atomicBackup = Join-Path $mods 'CreatorToolkit.dll.rollback'
$backup = $null
$targetBackup = $null
$bridgeBackup = $null
$replaced = $false
$added = $false
$tempCreated = $false
function Hash([string]$file) {
 $stream=[IO.File]::OpenRead($file)
 $sha=[Security.Cryptography.SHA256]::Create()
 try { -join ($sha.ComputeHash($stream) | ForEach-Object {$_.ToString('x2')}) }
 finally {$stream.Dispose();$sha.Dispose()}
}
function Running { return [bool](Get-Process SlimeRancher2 -ErrorAction SilentlyContinue) }
function Assert-Unlocked([string]$file) {
 if(Test-Path -LiteralPath $file) {
  try {$h=[IO.File]::Open($file,'Open','ReadWrite','None');$h.Dispose()}
  catch {throw ('File is locked; close SR2 before changing it: '+$file)}
 }
}
try {
 if(!(Test-Path -LiteralPath $source)){throw 'Build CreatorToolkit.dll first'}
 if(!(Test-Path -LiteralPath $testSummary)){throw 'Run test.ps1 on the current build first'}
 $summary=Get-Content -LiteralPath $testSummary -Raw
 $hash=Hash $source
 if($summary -notmatch '(?m)^PASS \d+' -or $summary -notmatch ('(?m)^DllSha256='+$hash+'\s*$')){throw 'Tests do not match this DLL; run test.ps1 again'}
 if(!(Test-Path -LiteralPath $mods)){throw 'SR2 Mods directory is missing'}
 $running=Running
 $exists=Test-Path -LiteralPath $target
 $change=(!$exists) -or ((Hash $target) -ne $hash)
 if($running -and $exists -and $change){throw 'SR2 is running. Refusing to overwrite an existing CreatorToolkit.dll; save and close the game first'}
 if(!$running) {
  if($change){Assert-Unlocked $target}
  Assert-Unlocked $bridge
 }
 if($change -or ((!$running) -and (Test-Path -LiteralPath $bridge))) {
  $backup=Join-Path $root ('backup\creator-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
  New-Item -ItemType Directory -Path $backup -Force | Out-Null
 }
 if($change) {
  if($exists) {
   $targetBackup=Join-Path $backup 'CreatorToolkit.dll'
   Copy-Item -LiteralPath $target -Destination $targetBackup
  }
  if(Test-Path -LiteralPath $temp){throw 'A pending install file already exists; inspect it before retrying'}
  Copy-Item -LiteralPath $source -Destination $temp
  $tempCreated=$true
  if((Hash $temp) -ne $hash){throw 'Staged copy checksum mismatch'}
  if($exists) {
   if(Running){throw 'SR2 started during installation; refusing replacement'}
   Assert-Unlocked $target
   if(Test-Path -LiteralPath $atomicBackup){throw 'A previous atomic rollback file exists; inspect it before retrying'}
   # Windows PowerShell coerces a null String argument to an empty path. Use a real,
   # same-volume backup path; the durable project backup has already been written.
   [IO.File]::Replace($temp,$target,$atomicBackup)
   $replaced=$true
  } else {
   [IO.File]::Move($temp,$target)
   $added=$true
  }
 }
 $running=Running
 $bridgePending=Test-Path -LiteralPath $bridge
 if($bridgePending -and !$running) {
  Assert-Unlocked $bridge
  if(Running){throw 'SR2 started during activation; old bridge was not changed'}
  if(!$backup) {
   $backup=Join-Path $root ('backup\creator-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
   New-Item -ItemType Directory -Path $backup -Force | Out-Null
  }
  $bridgeBackup=Join-Path $backup 'MinecraftHUD.dll'
  Move-Item -LiteralPath $bridge -Destination $bridgeBackup
  $bridgePending=$false
  Write-Output ('OLD BRIDGE BACKED UP: '+$bridgeBackup)
 }
 if((Hash $target) -ne $hash){throw 'Installed DLL checksum mismatch'}
 if($replaced -and (Test-Path -LiteralPath $atomicBackup)){Remove-Item -LiteralPath $atomicBackup -Force}
 $status=[ordered]@{
  Installed=$true;Dll=$target;Sha256=$hash;GameRunning=$running;
  BridgeActivationPending=[bool]$bridgePending;OldBridgeBackup=$bridgeBackup;
  BackupDirectory=$backup;LiveGameVerified=$false;InstalledAt=(Get-Date).ToString('o')
 }
 $status | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding UTF8
 'ok' | Set-Content (Join-Path $root '_work\creator-install.done') -Encoding ASCII
 Write-Output ('INSTALLED: '+$target)
 Write-Output ('SHA256: '+$hash)
 if($bridgePending) {
  Write-Output 'ACTIVATION PENDING: save/close SR2, rerun this install script to retire MinecraftHUD, then launch SR2.'
 } elseif($running) {
  Write-Output 'NEW DLL INSTALLED: restart SR2 to load it. No hot-loading was attempted.'
 } else {
  Write-Output 'READY FOR SR2 LAUNCH: old bridge retired; Insert opens Creator Toolkit, Esc closes it.'
 }
} catch {
 $failure=$_
 try {
  if($tempCreated -and (Test-Path -LiteralPath $temp)){Remove-Item -LiteralPath $temp -Force}
  if(!(Running)) {
   if($replaced -and $targetBackup -and (Test-Path -LiteralPath $targetBackup)) {
    Assert-Unlocked $target
    Copy-Item -LiteralPath $targetBackup -Destination $target -Force
   } elseif($added -and (Test-Path -LiteralPath $target)) {
    Assert-Unlocked $target
    Remove-Item -LiteralPath $target -Force
   }
   if($replaced -and (Test-Path -LiteralPath $atomicBackup)){Remove-Item -LiteralPath $atomicBackup -Force}
   if($bridgeBackup -and (Test-Path -LiteralPath $bridgeBackup) -and !(Test-Path -LiteralPath $bridge)) {
    Copy-Item -LiteralPath $bridgeBackup -Destination $bridge
   }
  }
 } catch {Write-Output ('Rollback requires manual inspection: '+$_.Exception.Message)}
 $failure | Out-String | Write-Output
 'failed' | Set-Content (Join-Path $root '_work\creator-install.done') -Encoding ASCII
 exit 1
}
