# Read-only sharing diagnostics. No credentials, configuration writes, or probes.
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$report=[ordered]@{
 schemaVersion=1
 observedAt=[DateTimeOffset]::UtcNow.ToString('o')
 myPublicWiFi=@()
 adapters=@()
 selectedIPv4=@()
 ics=@()
 icsError=$null
 neighbors=@()
 limitations=@('ARP neighbors are not proof of TV identity or DHCP leases.', 'Adapter counters include all traffic; they are not per-TV usage.', 'Route selection alone does not certify MyPublicWiFi NAT or IPFORWARD.', 'Carrier hotspot accounting is not observed.')
}
$paths=@((Join-Path ${env:ProgramFiles(x86)} 'MyPublicWiFi\MyPublicWiFi.exe'),(Join-Path $env:ProgramFiles 'MyPublicWiFi\MyPublicWiFi.exe'))
foreach($path in $paths){if(Test-Path -LiteralPath $path){$report.myPublicWiFi+=@{path=$path;version=(Get-Item -LiteralPath $path).VersionInfo.ProductVersion;running=@(Get-Process MyPublicWiFi -ErrorAction SilentlyContinue).Count -gt 0}}}
$adapters=@(Get-NetAdapter -IncludeHidden)
$ips=@(Get-NetIPAddress -AddressFamily IPv4)
$report.adapters=@($adapters | ForEach-Object {
 $adapter=$_
 [pscustomobject]@{name=$adapter.Name;description=$adapter.InterfaceDescription;guid=[string]$adapter.InterfaceGuid;index=$adapter.ifIndex;status=[string]$adapter.Status;ipv4=@($ips|Where-Object {$_.InterfaceIndex -eq $adapter.ifIndex}|Select-Object IPAddress,PrefixLength)}
})
foreach($target in @('1.1.1.1','8.8.8.8')){
 try{$report.selectedIPv4+=@{target=$target;selection=@(Find-NetRoute -RemoteIPAddress $target|Select-Object InterfaceAlias,InterfaceIndex,DestinationPrefix,NextHop);error=$null}}
 catch{$report.selectedIPv4+=@{target=$target;selection=@();error=$_.Exception.Message}}
}
try{
 $manager=New-Object -ComObject HNetCfg.HNetShare
 $connections=@($manager.EnumEveryConnection)
 if($connections.Count -eq 0 -or $null -eq $connections[0]){throw 'Windows ICS enumeration is unavailable in this security context.'}
 foreach($connection in $connections){
  $props=$manager.NetConnectionProps($connection)
  $config=$manager.INetSharingConfigurationForINetConnection($connection)
  $report.ics+=@{name=$props.Name;guid=$props.Guid;enabled=$config.SharingEnabled;type=if($config.SharingEnabled){$config.SharingConnectionType}else{$null}}
 }
}catch{$report.icsError=$_.Exception.Message}
# Only local neighbors, never a device-name lookup or an Internet probe.
$report.neighbors=@(Get-NetNeighbor -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object {$_.State -in 'Reachable','Stale','Delay','Probe' -and $_.LinkLayerAddress -ne '00-00-00-00-00-00'} | Select-Object InterfaceIndex,IPAddress,LinkLayerAddress,State)
$report|ConvertTo-Json -Depth 8
