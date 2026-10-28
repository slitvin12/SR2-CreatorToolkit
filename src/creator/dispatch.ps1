# On-demand current-user task breaks MCP's process/stream inheritance. No schedule, no password.
param([switch]$Rebuild,[switch]$SelfTest,[switch]$BuildOnly)
$ErrorActionPreference='Stop'
$r=(Resolve-Path "$PSScriptRoot\..\..").Path
$name='CreatorToolkit-SR2-MCP-Local'
$service=New-Object -ComObject Schedule.Service
$service.Connect()
$folder=$service.GetFolder('\')
$existing=$null
try {$existing=$folder.GetTask($name)}catch{}
if($existing -and !$existing.Xml.Contains($r)){throw 'Refusing to replace an unrelated scheduled task'}
$task=$service.NewTask(0)
$task.RegistrationInfo.Description='On-demand CreatorToolkit build/test or closed-game launch; no automatic save/quit and no recurring trigger.'
$user=[Security.Principal.WindowsIdentity]::GetCurrent().Name
$task.Principal.UserId=$user
$task.Principal.LogonType=3
$task.Principal.RunLevel=0
$task.Settings.AllowDemandStart=$true
$task.Settings.DisallowStartIfOnBatteries=$false
$task.Settings.StopIfGoingOnBatteries=$false
$task.Settings.ExecutionTimeLimit='PT35M'
$task.Settings.MultipleInstances=2
$action=$task.Actions.Create(0)
$action.Path=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$args='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$PSScriptRoot+'\run-task.ps1"'
if($Rebuild){$args+=' -Rebuild'}
if($SelfTest){$args+=' -SelfTest'}
if($BuildOnly){$args+=' -BuildOnly'}
$action.Arguments=$args
$action.WorkingDirectory=$r
$registered=$folder.RegisterTaskDefinition($name,$task,6,$user,$null,3,$null)
$instance=$registered.Run($null)
Write-Output ('On-demand task started: '+$name+'; state='+$instance.State)
Write-Output ('Log: '+(Join-Path $r '_work\creator-task.log'))
