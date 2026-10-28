# Standalone CreatorToolkit build. Never compiles or launches Minecraft/the bridge.
param([switch]$Install)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
$stage = Join-Path $root '_work\creator-stage'
$dotnet = Join-Path $root '_work\dotnet\dotnet.exe'
$env:DOTNET_ROOT = Join-Path $root '_work\dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$game = if($env:SR2_GAME_DIR){$env:SR2_GAME_DIR}else{'D:\SteamLibrary\steamapps\common\Slime Rancher 2'}
$target = Join-Path $game 'Mods\CreatorToolkit.dll'
function Assert-Unlocked([string]$file) {
 if(Test-Path -LiteralPath $file) {
  try {$handle=[IO.File]::Open($file,'Open','ReadWrite','None');$handle.Dispose()}
  catch {throw ('File is locked; no build/install overwrite is allowed: '+$file)}
 }
}
try {
 New-Item -ItemType Directory -Force $stage | Out-Null
 Assert-Unlocked (Join-Path $stage 'CreatorToolkit.dll')
 if($Install){Assert-Unlocked $target}
 & $dotnet build "$PSScriptRoot\CreatorToolkit.csproj" -c Release -o $stage -nologo -v minimal --no-incremental --ignore-failed-sources -p:UseSharedCompilation=false
 if($LASTEXITCODE -ne 0){throw 'CreatorToolkit build failed'}
 if(!(Test-Path (Join-Path $stage 'CreatorToolkit.dll'))){throw 'Build produced no CreatorToolkit.dll'}
 Write-Output ('BUILT '+(Join-Path $stage 'CreatorToolkit.dll'))
 if($Install) {
  & powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PSScriptRoot\install.ps1"
  if($LASTEXITCODE -ne 0){throw 'Installation did not complete'}
 }
 'ok'|Set-Content (Join-Path $root '_work\creator-build.done') -Encoding ASCII
} catch {
 $_|Out-String|Write-Output
 'failed'|Set-Content (Join-Path $root '_work\creator-build.done') -Encoding ASCII
 exit 1
}
