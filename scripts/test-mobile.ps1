$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$javaBin='C:/Program Files/Android/Android Studio/jbr/bin'
$out=Join-Path $taskRoot 'build/mobile-tests'
New-Item -ItemType Directory -Force $out | Out-Null
$names=@('ServiceState','HostLease','PositionQuality','GuardianPolicy','SampleHistory')
foreach($name in $names){
 & "$javaBin/javac.exe" -encoding UTF-8 --release 8 -Xlint:all,-options -Werror -d $out "$taskRoot/android/src/io/tethermax/app/$name.java" "$taskRoot/android/test/io/tethermax/app/$($name)Test.java"
 if($LASTEXITCODE){throw "Compilation failed: $name"}
 & "$javaBin/java.exe" -cp $out "io.tethermax.app.$($name)Test"
 if($LASTEXITCODE){throw "Failed: $name"}
}
$engineSources=Get-ChildItem "$taskRoot/engine/src","$taskRoot/engine/test" -Filter *.java -Recurse | ForEach-Object FullName
& "$javaBin/javac.exe" -encoding UTF-8 --release 8 -Xlint:all,-options -Werror -d $out $engineSources
if($LASTEXITCODE){throw 'Engine fixture compilation failed'}
foreach($name in @('RelayDnsTest','DnsOverHttpsTest','RelayFaultTest')){
 & "$javaBin/java.exe" -cp $out "io.tethermax.engine.$name"
 if($LASTEXITCODE){throw "Failed: $name"}
}
