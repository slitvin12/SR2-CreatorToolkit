param([ValidateSet('Inspect','Capture','Key','Click','CloseWindow')][string]$Action='Inspect',[int]$Vk=0,[int]$HoldMs=100,[int]$X=0,[int]$Y=0)
$ErrorActionPreference='Stop'
$r=(Resolve-Path "$PSScriptRoot\..\..").Path
& (Join-Path $r '_work\creator-desktop.ps1') -Action $Action -Vk $Vk -HoldMs $HoldMs -X $X -Y $Y *> (Join-Path $r '_work\creator-desktop-task.log')
