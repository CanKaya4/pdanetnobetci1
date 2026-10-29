$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskMsi=Join-Path $taskRoot 'build/artifacts/tethermax-windows-x64-1.0.0-rc13.msi'
$installer=New-Object -ComObject WindowsInstaller.Installer
$database=$installer.OpenDatabase($taskMsi,[int]0)
function Rows([string]$sql){
 $view=$database.OpenView($sql)
 try{
  $view.Execute() | Out-Null
  while($record=$view.Fetch()){
   try{$record.StringData(1)}
   finally{[Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) | Out-Null}
  }
 }finally{$view.Close() | Out-Null;[Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) | Out-Null}
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
