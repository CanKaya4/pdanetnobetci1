$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskInstalled=Join-Path $env:LOCALAPPDATA 'Programs/TetherMax'
$taskReport=Join-Path $taskRoot 'build/reports/romance-rc12-install.json'
function State([string]$phase,[string]$message){@{phase=$phase;message=$message;at=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath $taskReport -Encoding UTF8}
try{
 State 'stopping-old-gui' 'User requested RC12 upgrade. Only the installed TetherMax GUI is stopped.'
 $taskExe=Join-Path $taskInstalled 'TetherMax.exe'
 $taskGuis=@(Get-CimInstance Win32_Process -Filter "Name='TetherMax.exe'" | Where-Object {$_.ExecutablePath -eq $taskExe})
 foreach($taskGui in $taskGuis){Stop-Process -Id $taskGui.ProcessId -ErrorAction Stop}
 State 'waiting-for-owned-cleanup' 'Waiting for existing TetherMax guardian/worker cleanup. Neither is killed.'
 $taskDeadline=[DateTime]::UtcNow.AddSeconds(60)
 do{
  $taskOwners=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -in @('TetherMax.Network.exe','tethermax-engine.exe') -and $_.ExecutablePath -and (Split-Path $_.ExecutablePath -Parent) -eq $taskInstalled})
  if(-not $taskOwners.Count){break}
  Start-Sleep -Milliseconds 500
 }while([DateTime]::UtcNow -lt $taskDeadline)
 if($taskOwners.Count){throw 'Existing owned cleanup did not finish. Update stopped without killing network processes.'}
 State 'installing' 'Installing the verified local per-user MSI. No auto-connect or system service changes.'
 $taskMsi=Join-Path $taskRoot 'build/artifacts/tethermax-windows-x64-1.0.0-rc12.msi'
 $taskLog=Join-Path $taskRoot 'build/reports/romance-rc12-msi.log'
 $taskSetup=Start-Process msiexec.exe -ArgumentList @('/i',('"'+$taskMsi+'"'),'/qn','/norestart','REBOOT=ReallySuppress','/L*v',('"'+$taskLog+'"')) -WindowStyle Hidden -Wait -PassThru
 if($taskSetup.ExitCode -notin @(0,3010)){throw ('MSI failed: '+$taskSetup.ExitCode)}
 $taskVersion=(Get-Item -LiteralPath $taskExe).VersionInfo.ProductVersion
 if(-not $taskVersion.StartsWith('1.0.0-rc.12')){throw ('Unexpected installed version: '+$taskVersion)}
 State 'complete' ('Desktop installed: '+$taskVersion+'. App not launched elevated; no connection was started.')
}catch{State 'failed' $_.Exception.Message;exit 1}
