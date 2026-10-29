$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskMsi=Join-Path $taskRoot 'build/artifacts/tethermax-windows-x64-1.0.0-rc13.msi'
$installer=New-Object -ComObject WindowsInstaller.Installer
$database=$installer.GetType().InvokeMember('OpenDatabase','InvokeMethod',$null,$installer,@($taskMsi,0))
function Rows([string]$sql){
 $view=$database.GetType().InvokeMember('OpenView','InvokeMethod',$null,$database,@($sql))
 try{
  $view.GetType().InvokeMember('Execute','InvokeMethod',$null,$view,$null) | Out-Null
  while($record=$view.GetType().InvokeMember('Fetch','InvokeMethod',$null,$view,$null)){
   try{$record.GetType().InvokeMember('StringData','GetProperty',$null,$record,@(1))}
   finally{[Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) | Out-Null}
  }
 }finally{$view.GetType().InvokeMember('Close','InvokeMethod',$null,$view,$null) | Out-Null;[Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) | Out-Null}
}
try{
 $version=@(Rows "SELECT Value FROM Property WHERE Property = 'ProductVersion'")
 if($version.Count -ne 1 -or $version[0] -ne '1.0.13'){throw 'Unexpected MSI version'}
 $tables=@(Rows 'SELECT Name FROM _Tables')
 $actions=if($tables -contains 'CustomAction'){@(Rows 'SELECT Action FROM CustomAction')}else{@()}
 $services=if($tables -contains 'ServiceInstall'){@(Rows 'SELECT Name FROM ServiceInstall')}else{@()}
 if($actions.Count -or $services.Count){throw 'Unexpected installer custom actions or services'}
 $report=@{mode='read-only MSI database; not installed';productVersion=$version[0];customActions=@($actions);services=@($services);sha256=(Get-FileHash $taskMsi -Algorithm SHA256).Hash.ToLowerInvariant()}
 $report | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $taskRoot 'build/reports/rc13-msi-inspection.json')
 Write-Output 'PASS MSI 1.0.13 read-only inspection; no custom actions or service installation'
}finally{[Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null;[Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null}
