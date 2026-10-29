$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskReport=Join-Path $taskRoot 'build/reports/rc7-install.txt'
Start-Transcript -Path $taskReport -Force
try {
 $taskInstalled='C:\Users\Ali\AppData\Local\Programs\TetherMax'
 $taskData='C:\Users\Ali\AppData\Local\TetherMax'
 $taskAdb='C:\Users\Ali\AppData\Local\Android\Sdk\platform-tools\adb.exe'
 $taskBackup=Join-Path $taskRoot ('build/rc7-backup-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
 New-Item -ItemType Directory -Path $taskBackup | Out-Null
 Get-ChildItem -LiteralPath $taskData -File | Copy-Item -Destination $taskBackup
 # Only the GUI is stopped; its private channel closing asks the existing
 # guardian to roll back. Never terminate the elevated guardian or its worker.
 $taskGui=@(Get-CimInstance Win32_Process -Filter "Name='TetherMax.exe'" | Where-Object {$_.ExecutablePath -eq (Join-Path $taskInstalled 'TetherMax.exe')})
 foreach($taskItem in $taskGui){Stop-Process -Id $taskItem.ProcessId -ErrorAction Stop}
 $taskUntil=[DateTime]::UtcNow.AddSeconds(120)
 do {
  $taskOwners=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -in @('TetherMax.Network.exe','tethermax-engine.exe') -and $_.ExecutablePath -and (Split-Path $_.ExecutablePath -Parent) -eq $taskInstalled})
  if(!$taskOwners.Count){break};Start-Sleep -Milliseconds 500
 }while([DateTime]::UtcNow -lt $taskUntil)
 if($taskOwners.Count){throw 'Existing guardian recovery is still running; update stopped without killing it.'}
 Write-Output 'Existing network guardian and worker exited after GUI channel closed.'
 # First use the existing guardian recovery code for any abandoned owned state.
 $taskRecovery=Start-Process -FilePath (Join-Path $taskInstalled 'TetherMax.Network.exe') -ArgumentList @('recover-stale','--approve-network-recovery') -WindowStyle Hidden -Wait -PassThru
 if($taskRecovery.ExitCode){throw ('Owned stale session recovery failed: '+$taskRecovery.ExitCode)}
 Write-Output 'Owned stale recovery completed before installation.'
 & $taskAdb -s R5GL16BFE6T install -r (Join-Path $taskRoot 'build/artifacts/tethermax-android-1.0.0-rc7.apk')
 if($LASTEXITCODE){throw 'Android replacement failed; existing data was not removed.'}
 $taskMsi=Join-Path $taskRoot 'build/artifacts/tethermax-windows-x64-1.0.0-rc7.msi'
 $taskLog=Join-Path $taskRoot 'build/reports/rc7-msi.log'
 $taskSetup=Start-Process msiexec.exe -ArgumentList @('/i',('"'+$taskMsi+'"'),'/qn','/norestart','/L*v',('"'+$taskLog+'"')) -WindowStyle Hidden -Wait -PassThru
 if($taskSetup.ExitCode -notin @(0,3010)){throw ('MSI failed: '+$taskSetup.ExitCode)}
 $taskVersion=(Get-Item -LiteralPath (Join-Path $taskInstalled 'TetherMax.exe')).VersionInfo.ProductVersion
 if(!$taskVersion.StartsWith('1.0.0-rc.7')){throw ('Unexpected desktop version: '+$taskVersion)}
 foreach($taskFile in Get-ChildItem -LiteralPath $taskBackup -File){$taskCurrent=Join-Path $taskData $taskFile.Name;if(!(Test-Path -LiteralPath $taskCurrent)){throw 'Saved application data is missing after upgrade.'};if((Get-FileHash -LiteralPath $taskFile.FullName).Hash -ne (Get-FileHash -LiteralPath $taskCurrent).Hash){throw 'Saved application data changed unexpectedly during upgrade.'}}
 Write-Output ('Installed desktop: '+$taskVersion+'; existing settings and pairing data preserved.')
 & $taskAdb -s R5GL16BFE6T shell dumpsys package io.tethermax.app | Select-String 'versionCode=|versionName='
 Write-Output 'RC7_INSTALL_SUCCESS'
} catch {Write-Output ('RC7_INSTALL_FAILED: '+$_.Exception.Message);exit 1}
finally {Stop-Transcript}
