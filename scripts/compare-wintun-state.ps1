param([string]$ReportDirectory='build/reports/wintun-approved')
$ErrorActionPreference='Stop'
Get-NetAdapter -IncludeHidden | Select-Object Name,InterfaceDescription,InterfaceGuid,ifIndex,Status,MacAddress | Sort-Object InterfaceGuid | ConvertTo-Json -Depth 4 | Set-Content "$ReportDirectory/adapters-after.json"
Get-NetRoute | Select-Object DestinationPrefix,NextHop,InterfaceIndex,RouteMetric,Protocol,PolicyStore | Sort-Object InterfaceIndex,DestinationPrefix,NextHop | ConvertTo-Json -Depth 4 | Set-Content "$ReportDirectory/routes-after.json"
Get-DnsClientServerAddress | Select-Object InterfaceAlias,InterfaceIndex,AddressFamily,ServerAddresses | Sort-Object InterfaceIndex,AddressFamily | ConvertTo-Json -Depth 5 | Set-Content "$ReportDirectory/dns-after.json"
Get-NetIPInterface | Select-Object InterfaceAlias,InterfaceIndex,AddressFamily,ConnectionState,InterfaceMetric | Sort-Object InterfaceIndex,AddressFamily | ConvertTo-Json | Set-Content "$ReportDirectory/ipinterfaces-after.json"
Find-NetRoute -RemoteIPAddress 1.1.1.1 | Select-Object InterfaceAlias,InterfaceIndex,IPAddress,DestinationPrefix,NextHop | ConvertTo-Json | Set-Content "$ReportDirectory/selected-route-after.json"
Get-CimInstance Win32_SystemDriver | Where-Object { $_.Name -match 'wintun|wireguard|wiresock' -or $_.PathName -match 'wintun|wireguard|wiresock' } | Select-Object Name,State,PathName | ConvertTo-Json | Set-Content "$ReportDirectory/drivers-after.json"
pnputil.exe /enum-drivers /class Net > "$ReportDirectory/driver-store-after.txt"
$summary=@{}
foreach($kind in @('adapters','routes','dns','ipinterfaces','selected-route','drivers')) {
    $left=@(Get-Content "$ReportDirectory/$kind-before.json" -Raw | ConvertFrom-Json | ForEach-Object { $_ | ConvertTo-Json -Compress -Depth 8 } | Sort-Object)
    $right=@(Get-Content "$ReportDirectory/$kind-after.json" -Raw | ConvertFrom-Json | ForEach-Object { $_ | ConvertTo-Json -Compress -Depth 8 } | Sort-Object)
    $diff=if($left.Count -eq 0 -and $right.Count -eq 0){@()}else{@(Compare-Object $left $right)}
    $summary[$kind]=@{Identical=($diff.Count -eq 0);Differences=$diff}
}
$summary | ConvertTo-Json -Depth 10 | Set-Content "$ReportDirectory/comparison.json"
curl.exe --noproxy '*' --connect-timeout 8 --max-time 15 --fail --silent --show-error --output NUL --write-out 'WiFi_HTTP=%{http_code} total_s=%{time_total}\n' https://example.com/ | Tee-Object "$ReportDirectory/connectivity-after.txt"
$curlExit=$LASTEXITCODE
$summary | ConvertTo-Json -Depth 10
if($curlExit){throw 'Post-test Internet connectivity failed; inspect saved actual Windows state.'}
