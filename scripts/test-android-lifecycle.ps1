$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$javaBin='C:\Program Files\Android\Android Studio\jbr\bin'
$output=Join-Path $taskRoot 'build/lifecycle-classes'
New-Item -ItemType Directory -Force $output | Out-Null
& "$javaBin/javac.exe" --release 8 -Xlint:all,-options -Werror -d $output "$taskRoot/android/src/io/tethermax/app/ServiceState.java" "$taskRoot/android/test/io/tethermax/app/ServiceStateTest.java"
if($LASTEXITCODE){throw 'Lifecycle regression compilation failed'}
& "$javaBin/java.exe" -cp $output io.tethermax.app.ServiceStateTest
if($LASTEXITCODE){throw 'Lifecycle regression failed'}
