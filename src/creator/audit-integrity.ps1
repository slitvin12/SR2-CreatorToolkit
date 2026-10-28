# Read-only cross-run integrity gate. This script NEVER creates a baseline,
# accepts changed save bytes, restores/deletes a save, or sends a game command.
param(
 [string]$DataDirectory='D:\SteamLibrary\steamapps\common\Slime Rancher 2\UserData\CreatorToolkit',
 [string]$OwnedCopyName='',
 [string]$ExpectedBaselineSha256='',
 [string]$RecoveryFile='',
 [switch]$RequireCopyRetired,
 [switch]$RequireNoGame
)
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
function Require([bool]$ok,[string]$message){if(!$ok){throw $message}}
function Sha([string]$path){
 $h=[Security.Cryptography.SHA256]::Create();$stream=[IO.File]::OpenRead($path)
 try{[BitConverter]::ToString($h.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}
 finally{$stream.Dispose();$h.Dispose()}
}
function Canonical([string]$path){[IO.Path]::GetFullPath($path).TrimEnd([char[]]@('\','/'))}
function Checkpoint([string]$game,[string]$leaf){
 if(!($game -cmatch '^[A-Za-z0-9_-]{1,80}$')){return $false}
 return $leaf -imatch ('^'+[regex]::Escape($game)+'_\d{1,10}(\.sav)?$')
}
if($RequireNoGame){
 Require (!(Get-Process SlimeRancher2 -ErrorAction SilentlyContinue)) 'SR2 is running; no save/quit or replacement is allowed'
 $game='D:\SteamLibrary\steamapps\common\Slime Rancher 2'
 Require (@(Get-CimInstance Win32_Process|Where-Object {$_.ExecutablePath -like ($game+'*')}).Count -eq 0) 'A game-directory executable is running'
}
$path=Join-Path $DataDirectory 'audit-immutable-baseline.json'
$pinPath=Join-Path $DataDirectory 'audit-immutable-baseline.sha256'
Require ((Test-Path -LiteralPath $path) -and (Test-Path -LiteralPath $pinPath)) 'Explicit baseline approval and SHA pin are required; no new baseline is generated'
Require ((Get-Item -LiteralPath $path).Length -le 262144) 'Immutable baseline exceeds its parse budget'
$pin=[IO.File]::ReadAllText($pinPath).Trim();$sha=Sha $path
Require ($pin -cmatch '^[a-f0-9]{64}$' -and $pin -ceq $sha) 'Immutable baseline differs from its approved SHA pin'
if($ExpectedBaselineSha256){
 Require ($ExpectedBaselineSha256 -cmatch '^[a-f0-9]{64}$' -and $sha -ceq $ExpectedBaselineSha256) 'Cross-process immutable baseline changed'
}
$b=[IO.File]::ReadAllText($path)|ConvertFrom-Json
Require ($b.FormatVersion -eq 1 -and $b.ApprovalDecision -cin @('keep-current','authorized-restored-snapshot') -and
 ![string]::IsNullOrWhiteSpace($b.ApprovalReference) -and $b.ApprovalReference.Length -le 1024) 'Explicit immutable-baseline decision is missing'
$date=[DateTimeOffset]::MinValue
Require ($b.ApprovedUtc -cmatch '(Z|\+00:00)$' -and [DateTimeOffset]::TryParse($b.ApprovedUtc,[ref]$date) -and $date.Offset -eq [TimeSpan]::Zero) 'Baseline approval must have an explicit UTC timestamp'
Require ($b.MainName -cmatch '^[A-Za-z0-9_-]{1,80}$' -and (Checkpoint $b.MainName $b.MainSave) -and
 !$b.MainSave.EndsWith('.sav',[StringComparison]::OrdinalIgnoreCase)) 'Exact immutable main checkpoint is invalid'
Require ([IO.Path]::IsPathRooted($b.SavePath) -and (Test-Path -LiteralPath $b.SavePath -PathType Container)) 'Approved native save location is unavailable'
$savePath=Canonical $b.SavePath
$entries=@($b.OriginalHashes.psobject.Properties)
Require ($entries.Count -ge 1 -and $entries.Count -le 512) 'Immutable original hash set is empty or oversized'
$known=New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($entry in $entries){
 $file=$entry.Name
 Require ([IO.Path]::IsPathRooted($file) -and (Canonical ([IO.Path]::GetDirectoryName($file))) -ieq $savePath -and
 $file.EndsWith('.sav',[StringComparison]::OrdinalIgnoreCase) -and $entry.Value -cmatch '^[a-f0-9]{64}$') 'Invalid immutable original identity'
 $canonical=Canonical $file
 Require (!$known.ContainsKey($canonical)) 'Duplicate immutable original path'
 $known.Add($canonical,$entry.Value)
 Require ((Test-Path -LiteralPath $file -PathType Leaf) -and (Sha $file) -ceq $entry.Value) ('Immutable original changed or disappeared: '+[IO.Path]::GetFileName($file))
}
Require ($known.ContainsKey((Canonical (Join-Path $savePath ($b.MainSave+'.sav'))))) 'Exact protected main checkpoint is absent'
$recovery=$null;$previousUsed=$false
if($RecoveryFile){
 Require ($ExpectedBaselineSha256 -cmatch '^[a-f0-9]{64}$') 'Recovery launch requires a nonempty expected immutable baseline digest'
 foreach($candidate in @($RecoveryFile,($RecoveryFile+'.previous'))){
  if(!(Test-Path -LiteralPath $candidate -PathType Leaf)){continue}
  try{
   Require ((Get-Item -LiteralPath $candidate).Length -le 1048576) 'Recovery exceeds its parse budget'
   $parsed=[IO.File]::ReadAllText($candidate)|ConvertFrom-Json
   if($null -eq $parsed -or $parsed -is [Array]){throw 'Recovery is not a JSON object'}
   $recovery=$parsed;$previousUsed=$candidate -cne $RecoveryFile;break
  }catch{continue}
 }
 Require ($null -ne $recovery) 'No complete recovery JSON was readable; no launch'
 Require ($recovery.ImmutableBaselineSha256 -cmatch '^[a-f0-9]{64}$' -and
  $recovery.ImmutableBaselineSha256 -ceq $ExpectedBaselineSha256 -and
  $recovery.RunId -cmatch '^[a-f0-9]{32}$') 'Recovery baseline digest or run identity is missing/different'
 Require ($recovery.Boot -cin @('run','verify','return') -and
  $recovery.MainName -ceq $b.MainName -and $recovery.MainSave -ceq $b.MainSave -and
  (Canonical $recovery.SavePath) -ieq $savePath) 'Recovery main/stage/provider identity differs'
 Require ($recovery.TestName -cmatch '^[A-Za-z0-9_-]{1,80}$') 'Recovery owned-copy name is invalid'
 if($OwnedCopyName){Require ($OwnedCopyName -ceq $recovery.TestName) 'Conflicting owned-copy launch identities'}
 $OwnedCopyName=$recovery.TestName
 if($recovery.Boot -cin @('run','verify')){
  Require ((Checkpoint $OwnedCopyName $recovery.TestSave) -and
   !$recovery.TestSave.EndsWith('.sav',[StringComparison]::OrdinalIgnoreCase) -and
   (Test-Path -LiteralPath (Join-Path $savePath ($recovery.TestSave+'.sav')) -PathType Leaf)) 'Recovery exact owned checkpoint is invalid or missing'
 }
 $recoveryHashes=@($recovery.MainHashes.psobject.Properties)
 Require ($recoveryHashes.Count -eq $entries.Count) 'Recovery cannot replace the immutable original hash set'
 $seenRecovery=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
 foreach($entry in $recoveryHashes){
  $key=Canonical $entry.Name
  Require ($seenRecovery.Add($key) -and $known.ContainsKey($key) -and $known[$key] -ceq $entry.Value) 'Recovery original hashes differ'
 }
}
if($OwnedCopyName){
 Require ($OwnedCopyName -ine $b.MainName -and $OwnedCopyName -cmatch '^[A-Za-z0-9_-]{1,80}$') 'Owned-copy identity is invalid'
 foreach($file in $known.Keys){Require (!(Checkpoint $OwnedCopyName ([IO.Path]::GetFileName($file)))) 'Owned copy collides with immutable originals'}
}
$copyFiles=0
foreach($file in [IO.Directory]::GetFiles($savePath,'*.sav')){
 if($known.ContainsKey((Canonical $file))){continue}
 Require (!$RequireCopyRetired -and (Checkpoint $OwnedCopyName ([IO.Path]::GetFileName($file)))) ('Unexpected native save outside immutable originals: '+[IO.Path]::GetFileName($file))
 $copyFiles++
}
[pscustomobject]@{
 ImmutableBaselineSha256=$sha;MainName=$b.MainName;MainSave=$b.MainSave;SavePath=$b.SavePath;
 OriginalHashFiles=$entries.Count;OriginalBytesVerified=$true;OwnedCopyName=$OwnedCopyName;
 OwnedCopyFiles=$copyFiles;NoSaveByteWrite=$true;VerifiedUtc=[DateTime]::UtcNow.ToString('o');
 RecoveryRunId=$(if($recovery){$recovery.RunId}else{''});
 RecoveryBoot=$(if($recovery){if($previousUsed){'return'}else{$recovery.Boot}}else{''});
 RecoveryFromPrevious=$previousUsed
}