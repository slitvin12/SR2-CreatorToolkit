param([switch]$Rebuild,[switch]$SelfTest,[switch]$BuildOnly)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$r=(Resolve-Path "$PSScriptRoot\..\..").Path
$log=Join-Path $r '_work\creator-task.log'
$err=Join-Path $r '_work\creator-task.err'
Remove-Item $err -ErrorAction SilentlyContinue
try {
 if($BuildOnly) {
  & powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'release.ps1') *> $log
 } else {
  $taskArgs=@('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'restart.ps1'))
  if($Rebuild){$taskArgs+='-Rebuild'}
  if($SelfTest){$taskArgs+='-SelfTest'}
  & powershell @taskArgs *> $log
 }
 if($LASTEXITCODE -ne 0){throw ('CreatorToolkit pipeline failed with exit code '+$LASTEXITCODE+'; inspect '+$log)}
 exit 0
} catch {
 $_|Out-String|Set-Content $err -Encoding UTF8
 exit 1
}
