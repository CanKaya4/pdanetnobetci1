param([string]$AndroidSource='android',[string]$Name='tethermax-android-guardian-rc9.apk',[string]$Platform='android-35')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
 $javaBin='C:/Program Files/Android/Android Studio/jbr/bin'
 $env:JAVA_HOME=Split-Path $javaBin -Parent
 $env:PATH="$javaBin;$env:PATH"
 $sdk=Join-Path $env:LOCALAPPDATA 'Android/Sdk'
 $bt=Join-Path $sdk 'build-tools/35.0.0'
 $androidJar=Join-Path $sdk "platforms/$Platform/android.jar"
 $out=Join-Path $taskRoot ('build/mobile-'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Force "$out/classes","$out/dex",build/artifacts,build/reports | Out-Null
 function Check {if($LASTEXITCODE){throw "Mobile build failed: $LASTEXITCODE"}}
 & "$bt/aapt2.exe" compile --dir "$AndroidSource/res" -o "$out/resources.zip";Check
 & "$bt/aapt2.exe" link -o "$out/base.apk" --manifest "$AndroidSource/AndroidManifest.xml" -I $androidJar --min-sdk-version 26 --target-sdk-version 35 "$out/resources.zip";Check
 $sources=Get-ChildItem engine/src,"$AndroidSource/src" -Recurse -Filter *.java | ForEach-Object FullName
 & "$javaBin/javac.exe" -encoding UTF-8 --release 8 -Xlint:all,-options -classpath $androidJar -d "$out/classes" $sources;Check
 $classes=Get-ChildItem "$out/classes" -Recurse -Filter *.class | ForEach-Object FullName
 & "$bt/d8.bat" --lib $androidJar --min-api 26 --output "$out/dex" $classes;Check
 Copy-Item "$out/base.apk" "$out/unsigned.apk"
 Push-Location "$out/dex"
 try {& "$javaBin/jar.exe" --update --file "$out/unsigned.apk" classes.dex;Check}finally{Pop-Location}
 & "$bt/zipalign.exe" -f -p 4 "$out/unsigned.apk" "$out/aligned.apk";Check
 if(!(Test-Path build/android/debug.keystore)){throw 'Original signing key required; do not replace installed identity'}
 $apk=Join-Path $taskRoot "build/artifacts/$Name"
 & "$bt/apksigner.bat" sign --v4-signing-enabled false --ks build/android/debug.keystore --ks-pass pass:android --ks-key-alias androiddebugkey --out $apk "$out/aligned.apk";Check
 & "$bt/apksigner.bat" verify --verbose --print-certs $apk;Check
 & "$bt/aapt2.exe" dump badging $apk | Out-File "build/reports/$Name.badging.txt";Check
 Get-FileHash $apk -Algorithm SHA256
}finally{Pop-Location}
