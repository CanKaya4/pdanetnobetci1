param([string]$SdkRoot=$env:ANDROID_SDK_ROOT,[string]$JavaHome=$env:JAVA_HOME,[string]$ArtifactName='tethermax-android-1.0.0-rc12-romance.apk')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
if(-not $SdkRoot){$SdkRoot=Join-Path $env:LOCALAPPDATA 'Android/Sdk'}
if(-not $JavaHome){$JavaHome=Join-Path $env:ProgramFiles 'Android/Android Studio/jbr'}
$tools=Join-Path $SdkRoot 'build-tools/35.0.0'
$framework=Join-Path $SdkRoot 'platforms/android-35/android.jar'
$key=Join-Path $taskRoot 'build/android/debug.keystore'
if(-not (Test-Path -LiteralPath $key)){throw 'Existing development signing key required; refusing to change application identity'}
$output=Join-Path $taskRoot ('build/android-gaming-'+[guid]::NewGuid().ToString('N'))
$artifact=Join-Path $taskRoot (Join-Path 'build/artifacts' $ArtifactName)
New-Item -ItemType Directory -Force "$output/classes","$output/dex",(Split-Path $artifact) | Out-Null
function Check {if($LASTEXITCODE){throw "Android build step failed: $LASTEXITCODE"}}
$env:JAVA_HOME=$JavaHome
& "$tools/aapt2.exe" compile --dir "$taskRoot/android/res" -o "$output/resources.zip"; Check
& "$tools/aapt2.exe" link -o "$output/base.apk" --manifest "$taskRoot/android/AndroidManifest.xml" -I $framework --min-sdk-version 26 --target-sdk-version 35 "$output/resources.zip"; Check
$sources=Get-ChildItem "$taskRoot/engine/src","$taskRoot/android/src" -Recurse -Filter '*.java' | ForEach-Object FullName
& "$JavaHome/bin/javac.exe" -encoding UTF-8 --release 8 -Xlint:all,-options -classpath $framework -d "$output/classes" $sources; Check
$classes=Get-ChildItem "$output/classes" -Recurse -Filter '*.class' | ForEach-Object FullName
& "$tools/d8.bat" --lib $framework --min-api 26 --output "$output/dex" $classes; Check
Copy-Item -LiteralPath "$output/base.apk" -Destination "$output/unsigned.apk"
Push-Location "$output/dex"
try{& "$JavaHome/bin/jar.exe" --update --file "$output/unsigned.apk" classes.dex; Check}finally{Pop-Location}
& "$tools/zipalign.exe" -f -p 4 "$output/unsigned.apk" "$output/aligned.apk"; Check
& "$tools/apksigner.bat" sign --v4-signing-enabled false --ks $key --ks-pass pass:android --ks-key-alias androiddebugkey --out $artifact "$output/aligned.apk"; Check
& "$tools/apksigner.bat" verify --verbose --print-certs $artifact; Check
& "$tools/zipalign.exe" -c -p 4 $artifact; Check
& "$tools/aapt2.exe" dump badging $artifact; Check
$hash=Get-FileHash -LiteralPath $artifact -Algorithm SHA256
Set-Content -LiteralPath ($artifact+'.sha256') -Value ($hash.Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($artifact)) -Encoding ascii
Write-Output "APK created and verified; no installation or device commands executed: $artifact"
