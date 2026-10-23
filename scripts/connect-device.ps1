param(
    [Parameter(Mandatory = $true)][string]$Serial,
    [switch]$ApproveDeviceTest,
    [ValidateRange(1024, 65535)][int]$ForwardPort = 38473,
    [ValidateRange(1024, 65535)][int]$SocksPort = 1088
)
$ErrorActionPreference = 'Stop'
if (!$ApproveDeviceTest) { throw 'Device testing requires explicit approval: -ApproveDeviceTest' }
$root = Split-Path $PSScriptRoot -Parent
$adb = Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'
$exe = Join-Path $root 'build\artifacts\tethermax-windows-amd64.exe'
if (!(Test-Path $adb) -or !(Test-Path $exe)) { throw 'ADB or locally built x64 executable is missing.' }
if ($ForwardPort -eq $SocksPort) { throw 'Forward and SOCKS ports must differ.' }
foreach ($port in @($ForwardPort, $SocksPort)) {
    if (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue) {
        throw "Local port $port is already in use. No existing listener will be changed."
    }
}
$wifi = Get-NetAdapter -Name 'Wi-Fi' -ErrorAction Stop
$route = @(Find-NetRoute -RemoteIPAddress 1.1.1.1 | Where-Object DestinationPrefix)
if ($wifi.Status -ne 'Up' -or $route.Count -ne 1 -or $route[0].InterfaceIndex -ne $wifi.ifIndex) {
    throw 'The current IPv4 Internet route must remain on the existing Wi-Fi adapter.'
}
Write-Host 'Enter the pairing code displayed by TetherMax on the phone. Do not paste it into chat.'
$secret = Read-Host 'Pairing code (hidden)' -AsSecureString
$previousCode = $env:TETHERMAX_PAIRING_CODE
$ownedForward = $false
try {
    $env:TETHERMAX_PAIRING_CODE = [System.Net.NetworkCredential]::new('', $secret).Password
    if ($env:TETHERMAX_PAIRING_CODE -cnotmatch '^[0-9a-f]{32}$') { throw 'Expected 32 lowercase hexadecimal characters.' }
    & $adb -s $Serial forward --no-rebind "tcp:$ForwardPort" localabstract:io.tethermax.usb.v1
    if ($LASTEXITCODE) { throw 'ADB forward refused. Existing forwards were not replaced.' }
    $ownedForward = $true
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $ForwardPort -ErrorAction Stop)
    if (!$listeners.Count) { throw 'ADB transport listener was not found.' }
    foreach ($listener in $listeners) {
        if (![System.Net.IPAddress]::IsLoopback([System.Net.IPAddress]::Parse($listener.LocalAddress))) {
            throw 'ADB exposed a non-loopback listener; removing only this test forward.'
        }
    }
    Write-Host "SOCKS endpoint: 127.0.0.1:$SocksPort. Keep this terminal open; Ctrl+C ends the test."
    Write-Host 'No adapter, global proxy, DNS or route settings are changed.'
    & $exe serve --approve-usb-test --transport "127.0.0.1:$ForwardPort" --listen "127.0.0.1:$SocksPort"
    if ($LASTEXITCODE) { throw "TetherMax exited with code $LASTEXITCODE" }
} finally {
    if ($ownedForward) {
        & $adb -s $Serial forward --remove "tcp:$ForwardPort"
        if ($LASTEXITCODE) { Write-Warning "Could not remove this test's forward tcp:$ForwardPort" }
    }
    $env:TETHERMAX_PAIRING_CODE = $previousCode
    $secret.Dispose()
}
