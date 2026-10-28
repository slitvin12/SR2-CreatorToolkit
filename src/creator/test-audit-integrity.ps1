# Offline synthetic fixtures ONLY; no game launch, commands, installs or native
# save access. The full PowerShell preflight must be tested, not just its text.
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
$root=(Resolve-Path "$PSScriptRoot\..\..").Path
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('CT-ImmutableGate-'+[Guid]::NewGuid().ToString('N'))
$data=Join-Path $fixture 'diagnostics';$saves=Join-Path $fixture 'synthetic-saves'
$checks=0;$rows=New-Object 'System.Collections.Generic.List[string]'
function Check([bool]$ok,[string]$message){
 if(!$ok){throw ('FAIL offline immutable gate: '+$message)}
 $script:checks++;$script:rows.Add($message);[Console]::WriteLine('PASS offline immutable gate: '+$message)
}
function Reject([scriptblock]$action,[string]$message){
 $rejected=$false;try{& $action|Out-Null}catch{$rejected=$true}
 Check $rejected $message
}
function Sha([string]$path){
 $h=[Security.Cryptography.SHA256]::Create()
 try{[BitConverter]::ToString($h.ComputeHash([IO.File]::ReadAllBytes($path))).Replace('-','').ToLowerInvariant()}
 finally{$h.Dispose()}
}
try{
 [IO.Directory]::CreateDirectory($data)|Out-Null;[IO.Directory]::CreateDirectory($saves)|Out-Null
 $main=Join-Path $saves 'main_fixture_0.sav';$other=Join-Path $saves 'other_fixture_0.sav'
 [IO.File]::WriteAllText($main,'SYNTHETIC offline fixture, not a game save')
 [IO.File]::WriteAllText($other,'SYNTHETIC unrelated offline fixture, not a game save')
 $mainText=[IO.File]::ReadAllText($main);$hashes=[ordered]@{};$hashes[$main]=Sha $main;$hashes[$other]=Sha $other
 $b=[ordered]@{
  FormatVersion=1;ApprovalDecision='keep-current';ApprovalReference='OFFLINE SYNTHETIC TEST ONLY';
  ApprovedUtc='2026-01-01T00:00:00Z';MainName='main_fixture';MainSave='main_fixture_0';SavePath=$saves;OriginalHashes=$hashes
 }
 $path=Join-Path $data 'audit-immutable-baseline.json';$pinPath=Join-Path $data 'audit-immutable-baseline.sha256'
 [IO.File]::WriteAllText($path,($b|ConvertTo-Json -Depth 6))
 $pin=Sha $path;[IO.File]::WriteAllText($pinPath,$pin)
 $gate=Join-Path $PSScriptRoot 'audit-integrity.ps1'
 $ok=& $gate -DataDirectory $data -ExpectedBaselineSha256 $pin -RequireCopyRetired
 Check ($ok.OriginalBytesVerified -and $ok.OriginalHashFiles -eq 2 -and $ok.OwnedCopyFiles -eq 0) 'positive complete original set'
 Check ((Sha $main) -ceq $hashes[$main] -and (Sha $path) -ceq $pin) 'read-only gate preserves fixture original and reference bytes'
 Reject {& $gate -DataDirectory $data -ExpectedBaselineSha256 ('0'*64)} 'different cross-process baseline SHA is refused'
 [IO.File]::WriteAllText($pinPath,('0'*64))
 Reject {& $gate -DataDirectory $data} 'changed or stale approved SHA pin is refused'
 [IO.File]::WriteAllText($pinPath,$pin)
 [IO.File]::WriteAllText($main,$mainText+' changed')
 Reject {& $gate -DataDirectory $data} 'changed original is not accepted as a fresh baseline'
 [IO.File]::WriteAllText($main,$mainText)
 $extra=Join-Path $saves 'main_fixture_1.sav';[IO.File]::WriteAllText($extra,'SYNTHETIC rotated original fixture')
 Reject {& $gate -DataDirectory $data} 'extra original-family checkpoint is refused'
 [IO.File]::Delete($extra)
 $copy=Join-Path $saves 'copy_fixture_0.sav';[IO.File]::WriteAllText($copy,'SYNTHETIC owned-copy fixture')
 $allowed=& $gate -DataDirectory $data -OwnedCopyName 'copy_fixture' -ExpectedBaselineSha256 $pin
 Check ($allowed.OriginalBytesVerified -and $allowed.OwnedCopyFiles -eq 1) 'exact registered copy family is admitted'
 Reject {& $gate -DataDirectory $data -OwnedCopyName 'copy_fixture' -RequireCopyRetired} 'copy-retired gate refuses a remaining owned checkpoint'
 Reject {& $gate -DataDirectory $data -OwnedCopyName 'unrelated_copy'} 'other copy family cannot excuse extra files'
 Reject {& $gate -DataDirectory $data -OwnedCopyName 'MAIN_FIXTURE'} 'case-only main alias cannot register as a copy on Windows'
 Reject {& $gate -DataDirectory $data -OwnedCopyName 'OTHER_FIXTURE'} 'case-only unrelated original alias cannot register as a copy'
 $recoveryPath=Join-Path $data 'functional-recovery.json'
 $recovery=[ordered]@{
  RunId=('a'*32);ImmutableBaselineSha256=$pin;MainName='main_fixture';MainSave='main_fixture_0';SavePath=$saves;
  TestName='copy_fixture';TestSave='copy_fixture_0';Boot='run';MainHashes=$hashes
 }
 [IO.File]::WriteAllText($recoveryPath,($recovery|ConvertTo-Json -Depth 6))
 $recover=& $gate -DataDirectory $data -ExpectedBaselineSha256 $pin -RecoveryFile $recoveryPath
 Check ($recover.RecoveryRunId -ceq ('a'*32) -and $recover.RecoveryBoot -ceq 'run') 'full recovery identity and exact owned checkpoint are verified before launch'
 Reject {& $gate -DataDirectory $data -RecoveryFile $recoveryPath} 'missing expected recovery digest cannot disable cross-process verification'
 $recovery.ImmutableBaselineSha256=''
 [IO.File]::WriteAllText($recoveryPath,($recovery|ConvertTo-Json -Depth 6))
 Reject {& $gate -DataDirectory $data -ExpectedBaselineSha256 $pin -RecoveryFile $recoveryPath} 'missing recovery digest is refused even when the approved pin exists'
 $recovery.ImmutableBaselineSha256=$pin;$recovery.MainHashes=[ordered]@{};$recovery.MainHashes[$main]=$hashes[$main]
 [IO.File]::WriteAllText($recoveryPath,($recovery|ConvertTo-Json -Depth 6))
 Reject {& $gate -DataDirectory $data -ExpectedBaselineSha256 $pin -RecoveryFile $recoveryPath} 'recovery cannot shrink or redefine immutable originals'
 $recovery.MainHashes=$hashes;$recovery.TestSave='copy_fixture_99'
 [IO.File]::WriteAllText($recoveryPath,($recovery|ConvertTo-Json -Depth 6))
 Reject {& $gate -DataDirectory $data -ExpectedBaselineSha256 $pin -RecoveryFile $recoveryPath} 'recovery run/verify cannot launch a missing exact owned checkpoint'
 $recovery.TestSave='copy_fixture_0';$recovery.Boot='return'
 [IO.File]::WriteAllText(($recoveryPath+'.previous'),($recovery|ConvertTo-Json -Depth 6))
 [IO.File]::WriteAllText($recoveryPath,'{')
 $fallback=& $gate -DataDirectory $data -ExpectedBaselineSha256 $pin -RecoveryFile $recoveryPath
 Check ($fallback.RecoveryFromPrevious -and $fallback.RecoveryBoot -ceq 'return') 'complete previous recovery is admitted only for safe return after an interrupted current JSON'
 [IO.File]::Delete($copy)
 [IO.File]::Move($other,$other+'.missing')
 Reject {& $gate -DataDirectory $data} 'missing original is refused'
 [IO.File]::Move($other+'.missing',$other)
 [IO.File]::Delete($pinPath)
 Reject {& $gate -DataDirectory $data} 'missing approval pin is never generated automatically'
 [IO.File]::WriteAllText($pinPath,$pin)
 $final=& $gate -DataDirectory $data -ExpectedBaselineSha256 $pin -RequireCopyRetired
 Check ($final.OriginalBytesVerified -and (Sha $main) -ceq $hashes[$main] -and (Sha $other) -ceq $hashes[$other]) 'synthetic fixtures return to exact known bytes'
 $record=[ordered]@{
  OfflineOnly=$true;SyntheticFixturesOnly=$true;NativeSaveAccess=$false;GameLaunch=$false;Install=$false;
  Passed=$checks;Failed=0;GateScriptSha256=(Sha $gate);RecordedUtc=[DateTime]::UtcNow.ToString('o');Checks=$rows.ToArray()
 }
 $record|ConvertTo-Json -Depth 5|Set-Content (Join-Path $root '_work\creator-audit-integrity-offline-tests.json') -Encoding UTF8
 [Console]::WriteLine('SUMMARY: '+$checks+' offline PowerShell immutable-gate checks; no live native proof.')
}finally{
 if(Test-Path -LiteralPath $fixture){[IO.Directory]::Delete($fixture,$true)}
}