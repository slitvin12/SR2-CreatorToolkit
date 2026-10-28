# Run this script in a BACKGROUND powershell process when called through the MCP tunnel.
param([switch]$Install)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$root=(Resolve-Path "$PSScriptRoot\..\..").Path
try {
 foreach($script in @('build.ps1','test.ps1')) {
  & powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot $script)
  if($LASTEXITCODE -ne 0){throw ($script+' failed')}
 }
 if($Install) {
  & powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'install.ps1')
  if($LASTEXITCODE -ne 0){throw 'install.ps1 failed'}
 }
 'ok'|Set-Content (Join-Path $root '_work\creator-release.done') -Encoding ASCII
} catch {
 $_|Out-String|Write-Output
 'failed'|Set-Content (Join-Path $root '_work\creator-release.done') -Encoding ASCII
 exit 1
}
