param([string]$SourceRef='1026e29',[int]$VersionCode=8)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 $out=Join-Path $root ('build/rollback-android-'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Force "$out/source","$out/classes","$out/dex" | Out-Null
 function Check {if($LASTEXITCODE){throw "Rollback APK build failed: $LASTEXITCODE"}}
 git archive --format=zip --output="$out/source.zip" $SourceRef;Check
 Expand-Archive -LiteralPath "$out/source.zip" -DestinationPath "$out/source"
 [xml]$manifest=Get-Content "$out/source/android/AndroidManifest.xml" -Raw
 $manifest.manifest.SetAttribute('versionCode','http://schemas.android.com/apk/res/android',[string]$VersionCode)
 $manifest.manifest.SetAttribute('versionName','http://schemas.android.com/apk/res/android','0.6.0-rollback')
 $manifest.Save("$out/AndroidManifest.xml")
 $env:JAVA_HOME='C:/Program Files/Android/Android Studio/jbr'
 $env:PATH="$env:JAVA_HOME/bin;$env:PATH"
 $sdk=Join-Path $env:LOCALAPPDATA 'Android/Sdk'
 $bt=Join-Path $sdk 'build-tools/35.0.0';$jar=Join-Path $sdk 'platforms/android-35/android.jar'
 & "$bt/aapt2.exe" link -o "$out/base.apk" --manifest "$out/AndroidManifest.xml" -I $jar --min-sdk-version 26 --target-sdk-version 35;Check
 $sources=Get-ChildItem "$out/source/engine/src","$out/source/android/src" -Recurse -Filter *.java | ForEach-Object FullName
 javac -encoding UTF-8 --release 8 -classpath $jar -d "$out/classes" $sources;Check
 $classes=Get-ChildItem "$out/classes" -Recurse -Filter *.class | ForEach-Object FullName
 & "$bt/d8.bat" --lib $jar --min-api 26 --output "$out/dex" $classes;Check
 Copy-Item "$out/base.apk" "$out/unsigned.apk"
 Push-Location "$out/dex"
 try {jar --update --file "$out/unsigned.apk" classes.dex;Check}finally{Pop-Location}
 & "$bt/zipalign.exe" -f -p 4 "$out/unsigned.apk" "$out/aligned.apk";Check
 if(!(Test-Path build/android/debug.keystore)){throw 'Original signing key required; never replace it'}
 & "$bt/apksigner.bat" sign --v4-signing-enabled false --ks build/android/debug.keystore --ks-pass pass:android --ks-key-alias androiddebugkey --out build/artifacts/tethermax-android-060-rollback.apk "$out/aligned.apk";Check
 & "$bt/apksigner.bat" verify --verbose build/artifacts/tethermax-android-060-rollback.apk;Check
 Write-Output 'Rollback APK built only; not installed. Higher versionCode permits an in-place rollback without uninstalling or clearing Keystore.'
}finally{Pop-Location}
