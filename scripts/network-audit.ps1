param([string]$OutputPath)
# Read-only. No packets, ADB, driver loading or network configuration changes.
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
function Rows($items) { return @($items) }
$report=[ordered]@{
  schemaVersion=1
  observedAt=[DateTimeOffset]::UtcNow.ToString('o')
  scope='Windows configuration snapshot only; no end-to-end path certification'
  systemWideCertified=$false
  hotspotOffIndependence='not-measured'
  adapters=Rows (Get-NetAdapter -IncludeHidden | Select-Object Name,InterfaceDescription,InterfaceGuid,ifIndex,Status)
  interfaces=Rows (Get-NetIPInterface | Select-Object InterfaceIndex,InterfaceAlias,AddressFamily,ConnectionState,InterfaceMetric,NlMtu)
  routes=Rows (Get-NetRoute -PolicyStore ActiveStore | Select-Object InterfaceIndex,InterfaceAlias,AddressFamily,DestinationPrefix,NextHop,RouteMetric,Protocol)
  dns=Rows (Get-DnsClientServerAddress | Select-Object InterfaceIndex,InterfaceAlias,AddressFamily,ServerAddresses)
  counters=Rows (Get-NetAdapterStatistics | Select-Object Name,ReceivedBytes,SentBytes)
  selectedRoutes=@()
}
# Find-NetRoute calculates routing policy; it sends no connectivity probes.
foreach($target in @('1.1.1.1','8.8.8.8','2606:4700:4700::1111')) {
  try {
    $choice=@(Find-NetRoute -RemoteIPAddress $target -ErrorAction Stop | Select-Object InterfaceIndex,InterfaceAlias,IPAddress,DestinationPrefix,NextHop,RouteMetric)
    $report.selectedRoutes+=@{target=$target;selection=$choice;error=$null}
  } catch {
    $report.selectedRoutes+=@{target=$target;selection=@();error=$_.Exception.Message}
  }
}
$json=$report | ConvertTo-Json -Depth 8
if($OutputPath) {
  # Never overwrite previous evidence.
  $full=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
  $stream=[IO.File]::Open($full,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
  try { $bytes=[Text.UTF8Encoding]::new($false).GetBytes($json);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true) } finally {$stream.Dispose()}
}
$json
