$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
 function Check {if($LASTEXITCODE){throw "Build failed: $LASTEXITCODE"}}
 & scripts/build-mobile.ps1 -Name 'tethermax-android-1.0.0-rc13.apk';Check
 $out=Join-Path $taskRoot 'build/rc13-publish'
 dotnet publish windows/TetherMax.Desktop -c Release -r win-x64 --self-contained true -o $out;Check
 dotnet publish windows/TetherMax.Network -c Release -r win-x64 --self-contained true -o $out;Check
 $env:GOROOT=(Resolve-Path ../.tools/go).Path;$env:GOTOOLCHAIN='go1.26.3';$env:GOCACHE=Join-Path $taskRoot '.ci-tools/go-cache'
 & "$env:GOROOT/bin/go.exe" -C host build -trimpath -o (Join-Path $out 'tethermax-engine.exe') ./cmd/tethermax;Check
 $wintun=Join-Path $taskRoot '.ci-tools/wintun-0.14.1/wintun/bin/amd64/wintun.dll'
 if((Get-FileHash $wintun -Algorithm SHA256).Hash -ne 'E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE'){throw 'Wintun checksum mismatch'}
 if((Get-AuthenticodeSignature $wintun).Status -ne 'Valid'){throw 'Wintun signature invalid'}
 Copy-Item $wintun (Join-Path $out 'wintun.dll')
 Copy-Item .ci-tools/wintun-0.14.1/wintun/LICENSE.txt (Join-Path $out 'WINTUN-LICENSE.txt')
 $zip=Join-Path $taskRoot 'build/platform-tools-r37.0.1-win.zip'
 if(!(Test-Path $zip)){Invoke-WebRequest -Uri 'https://dl.google.com/android/repository/platform-tools_r37.0.1-win.zip' -OutFile $zip}
 # SHA-1 is also published in Google's HTTPS SDK manifest. Pin SHA-256 here.
 if((Get-FileHash $zip -Algorithm SHA256).Hash -ne '45F4D63113E895EBDE0C90F194099A4676B6AC653BD28D54314A9E022BBC1A99'){throw 'Google platform-tools checksum mismatch'}
 Expand-Archive -LiteralPath $zip -DestinationPath build/rc13-adb-tools -Force
 $adbOut=Join-Path $out 'platform-tools';New-Item -ItemType Directory -Force $adbOut | Out-Null
 foreach($name in @('adb.exe','AdbWinApi.dll','AdbWinUsbApi.dll','libwinpthread-1.dll','NOTICE.txt','source.properties')){Copy-Item (Join-Path 'build/rc13-adb-tools/platform-tools' $name) (Join-Path $adbOut $name)}
 Copy-Item THIRD_PARTY_NOTICES.md (Join-Path $out 'THIRD_PARTY_NOTICES.md')
 $python='C:/Users/Ali/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
 & $python scripts/package-windows.py --publish-dir $out --version 1.0.13 --output build/artifacts/tethermax-windows-x64-1.0.0-rc13.msi;Check
 $sourceCommit=(git rev-parse HEAD).Trim();Check
 $sourceDirty=@(git status --porcelain);Check
 $files=@('build/artifacts/tethermax-android-1.0.0-rc13.apk','build/artifacts/tethermax-windows-x64-1.0.0-rc13.msi')
 $assets=@($files | ForEach-Object {@{name=Split-Path $_ -Leaf;bytes=(Get-Item $_).Length;sha256=(Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant()}})
 @{version='1.0.0-rc.13';sourceCommit=$sourceCommit;sourceClean=($sourceDirty.Count -eq 0);assets=$assets} | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 build/artifacts/rc13-release-manifest.json
 & scripts/inspect-smart-package.ps1;Check
 Get-FileHash build/artifacts/tethermax-android-1.0.0-rc13.apk,build/artifacts/tethermax-windows-x64-1.0.0-rc13.msi -Algorithm SHA256
}finally{Pop-Location}
