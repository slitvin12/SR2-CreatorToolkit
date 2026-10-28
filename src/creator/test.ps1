# Pure policy and metadata checks; never loads the game or executes a Melon mod.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
$dotnet = Join-Path $root '_work\dotnet\dotnet.exe'
$env:DOTNET_ROOT = Join-Path $root '_work\dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$stage = Join-Path $root '_work\creator-tests'
try {
 Remove-Item (Join-Path $root '_work\creator-tests.summary.txt') -ErrorAction SilentlyContinue
 New-Item -ItemType Directory -Force $stage | Out-Null
 & $dotnet build "$PSScriptRoot\tests\CreatorToolkit.Tests.csproj" -c Release -o $stage -nologo -v minimal --no-incremental --ignore-failed-sources -p:UseSharedCompilation=false
 if($LASTEXITCODE -ne 0){throw 'Policy test build failed'}
 & $dotnet (Join-Path $stage 'CreatorToolkit.Tests.dll') $root
 if($LASTEXITCODE -ne 0){throw 'Policy/metadata checks failed'}
 & powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'test-audit-integrity.ps1')
 if($LASTEXITCODE -ne 0){throw 'Offline PowerShell immutable-gate fixtures failed'}
 'ok'|Set-Content (Join-Path $root '_work\creator-tests.done') -Encoding ASCII
} catch {
 $_|Out-String|Write-Output
 'failed'|Set-Content (Join-Path $root '_work\creator-tests.done') -Encoding ASCII
 exit 1
}
