param([string]$DesktopOutput='build/desktop')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
  $env:JAVA_HOME='C:\Program Files\Android\Android Studio\jbr'
  $env:ANDROID_SDK_ROOT=Join-Path $env:LOCALAPPDATA 'Android\Sdk'
  $env:PATH="$env:JAVA_HOME\bin;$env:PATH"
  $bt=Join-Path $env:ANDROID_SDK_ROOT 'build-tools\35.0.0'
  $jar=Join-Path $env:ANDROID_SDK_ROOT 'platforms\android-35\android.jar'
  $out=Join-Path 'build' ('android-'+[guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Force "$out/classes","$out/dex",build/artifacts,build/reports | Out-Null
  function Check { if($LASTEXITCODE){throw "Build command failed: $LASTEXITCODE"} }
  & "$bt/aapt2.exe" compile --dir android/res -o "$out/resources.zip"; Check
  & "$bt/aapt2.exe" link -o "$out/base.apk" --manifest android/AndroidManifest.xml -I $jar --min-sdk-version 26 --target-sdk-version 35 "$out/resources.zip"; Check
  $src=Get-ChildItem engine/src,android/src -Recurse -Filter *.java | ForEach-Object FullName
  javac -encoding UTF-8 --release 8 -Xlint:all,-options -classpath $jar -d "$out/classes" $src; Check
  $classes=Get-ChildItem "$out/classes" -Recurse -Filter *.class | ForEach-Object FullName
  & "$bt/d8.bat" --lib $jar --min-api 26 --output "$out/dex" $classes; Check
  Copy-Item "$out/base.apk" "$out/unsigned.apk"
  $apk=(Resolve-Path "$out/unsigned.apk").Path
  Push-Location "$out/dex"
  try { jar --update --file $apk classes.dex; Check } finally { Pop-Location }
  & "$bt/zipalign.exe" -f -p 4 "$out/unsigned.apk" "$out/aligned.apk"; Check
  if (!(Test-Path build/android/debug.keystore)) { throw 'Development signing key missing; do not replace an installed identity silently.' }
  & "$bt/apksigner.bat" sign --v4-signing-enabled false --ks build/android/debug.keystore --ks-pass pass:android --ks-key-alias androiddebugkey --out build/artifacts/tethermax-android-1.0.0-rc4.apk "$out/aligned.apk"; Check
  & "$bt/apksigner.bat" verify --verbose build/artifacts/tethermax-android-1.0.0-rc4.apk; Check
} finally {Pop-Location}
