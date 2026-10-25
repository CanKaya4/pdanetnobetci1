using System.Net;
using System.Net.Sockets;
using TetherMax;
if(args.SequenceEqual(new[]{"--guardian-fixture"})){await Task.Delay(600);return;}
if(args.Length==2&&args[0]=="--hotspot-reload"){
 HotspotPreferences.FilePath=args[1];var value=HotspotPreferences.Read();
 if(value?.Ssid!="Salon TV"||value.Band!=1||value.Password!="Loopback-Only-Password")throw new Exception("hotspot process reload failed");return;
}
static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
foreach(string malformed in new[]{"null","[]","{bad}","{\"type\":1}","{\"type\":\"network-active\",\"guid\":\"invalid\",\"session\":\"fixture\"}","{\"type\":\"network-stopped\",\"recovered\":\"true\"}"}) {
 Require(!NetworkReport.TryParse(malformed,out var bad),"malformed report accepted");bad?.Dispose();
}
foreach(string report in new[]{"{\"type\":\"network-channel-closed\"}","{\"type\":\"ip-counters\",\"payload\":{}}","{\"type\":\"network-stopped\",\"recovered\":true}"}) {
 Require(NetworkReport.TryParse(report,out var good),"valid report rejected");good?.Dispose();
}
foreach(string malformed in new[]{"null","[]","{\"checks\":[null,null,null]}","{\"checks\":[1,2,3]}"}) {using var bad=System.Text.Json.JsonDocument.Parse(malformed);Require(!TunnelHealth.Healthy(bad.RootElement),"malformed health caused false success");}
Require(OwnedForward.Inspect("phone tcp:1000 localabstract:io.tethermax.usb.v1\n","phone","tcp:1000","io.tethermax.usb.v1")==ForwardState.Exact,"exact mapping missed");
Require(OwnedForward.Inspect("","phone","tcp:1000","io.tethermax.usb.v1")==ForwardState.Missing,"missing mapping not repairable");
foreach(string listing in new[]{"other tcp:1000 localabstract:io.tethermax.usb.v1","phone tcp:1000 localabstract:foreign","phone tcp:1000 localabstract:io.tethermax.usb.v1\nother tcp:1000 localabstract:foreign"})Require(OwnedForward.Inspect(listing,"phone","tcp:1000","io.tethermax.usb.v1")==ForwardState.Conflict,"foreign mapping could be rebound/removed");
Require(OwnedForward.Inspect("","phone","tcp:0","io.tethermax.usb.v1")==ForwardState.Conflict,"invalid port accepted");
Console.WriteLine("PASS malformed IPC/health is rejected without throwing; USB repairs require exact serial/port/remote ownership");
foreach(var fixture in new[]{("OKAY00040029",AdbServerState.Compatible),("OKAY00040028",AdbServerState.Conflict),("FAIL0004nope",AdbServerState.Conflict),("OKA",AdbServerState.Conflict)}) {
 using var serverFixture=new TcpListener(IPAddress.Loopback,0);serverFixture.Start();int fixturePort=((IPEndPoint)serverFixture.LocalEndpoint).Port;
 var serving=Task.Run(async()=>{using var client=await serverFixture.AcceptTcpClientAsync();var stream=client.GetStream();byte[] request=new byte[16];await stream.ReadExactlyAsync(request);Require(System.Text.Encoding.ASCII.GetString(request)=="000chost:version","ADB probe contacted a device/transport");await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(fixture.Item1));});
 Require(await AdbServerProbe.Inspect(fixturePort)==fixture.Item2,"ADB protocol compatibility misclassified");await serving;
 serverFixture.Stop();Require(await AdbServerProbe.Inspect(fixturePort)==AdbServerState.Missing,"missing ADB server not distinguished from foreign server");
}
Console.WriteLine("PASS loopback-only ADB server protocol probe: compatible, foreign, failed/truncated and missing; no adb executable used");
var usbAvailability=new UsbAvailability();
Require(!usbAvailability.Observe(false,false)&&!usbAvailability.Observe(true,true),"transient device inventory tore down session");
Require(!usbAvailability.Observe(true,false)&&usbAvailability.Observe(true,false),"persistent forward loss ignored");
for(int i=0;i<100;i++)Require(!usbAvailability.Observe(false,false),"persistent loss repeated cleanup");
Require(!usbAvailability.Observe(true,true)&&!usbAvailability.Observe(false,false)&&usbAvailability.Observe(false,false),"healthy observation did not reset loss state");
usbAvailability.Reset();Require(!usbAvailability.Observe(false,false),"new session inherited transport loss");
Console.WriteLine("PASS transient ADB omission preserves session, persistent loss requests cleanup once, healthy/new session resets");
Pairing.Folder=Path.Combine(Path.GetTempPath(),"TetherMax-connect-"+Guid.NewGuid().ToString("N"));
try {
 var settingsPath=Path.Combine(Pairing.Folder,"preferences.json");
 var initial=new TrafficSettings("gaming",5,30,true,"adguard");var updated=initial with{Mode="streaming"};
 SettingsFile.Write(settingsPath,initial);SettingsFile.Write(settingsPath,updated);
 Require(SettingsFile.Read<TrafficSettings>(settingsPath)==updated,"atomic settings roundtrip");
 File.WriteAllText(settingsPath,"{interrupted");Require(SettingsFile.Read<TrafficSettings>(settingsPath)==initial,"last-good backup was not recovered");
 SettingsFile.Write(settingsPath,updated);File.WriteAllText(settingsPath,"null");Require(SettingsFile.Read<TrafficSettings>(settingsPath)==initial,"corrupt input overwrote last-good backup");
 Require(!Directory.EnumerateFiles(Pairing.Folder,"*.tmp").Any(),"settings staging file leaked");
 Console.WriteLine("PASS flushed atomic settings and last-good fallback across interrupted/corrupt saves");
 using(var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,Arguments="--guardian-fixture"})!){
  var exited=child.WaitForExitAsync();var network=new SystemNetwork();typeof(SystemNetwork).GetField("guardian",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(network,child);
  var first=network.DisposeAsync().AsTask();var second=network.DisposeAsync().AsTask();Require(!second.IsCompleted||exited.IsCompletedSuccessfully,"concurrent disposal skipped guardian recovery wait");await Task.WhenAll(first,second);Require(exited.IsCompletedSuccessfully,"recovery wait returned before guardian exit");
 }Console.WriteLine("PASS concurrent network disposal shares the guardian recovery completion; no network helper started");

 Require(SessionLifecycle.ShouldStopHotspot(false,true,false,false),"first automatic cleanup was skipped");
 for(int tick=0;tick<100;tick++)Require(!SessionLifecycle.ShouldStopHotspot(false,true,false,true),"failed hotspot cleanup retried from timer");
 Require(!SessionLifecycle.ShouldStopHotspot(true,true,false,false)&&!SessionLifecycle.ShouldStopHotspot(false,true,true,false),"active or in-flight hotspot was stopped");
 Console.WriteLine("PASS 100 timer ticks do not repeat failed hotspot cleanup or elevation");
 using(var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,Arguments="--guardian-fixture"})!){
  var neverConnected=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  using var helperDeadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
  bool earlyExit=false;try{await SessionLifecycle.WaitForHelper(neverConnected.Task,child,helperDeadline.Token);}catch(IOException){earlyExit=true;}
  Require(earlyExit&&!helperDeadline.IsCancellationRequested,"early helper exit waited for startup timeout");
 }
 using(var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,Arguments="--guardian-fixture"})!){
  await SessionLifecycle.WaitForHelper(Task.CompletedTask,child,CancellationToken.None);
  using var canceled=new CancellationTokenSource();canceled.Cancel();
  bool stopped=false;try{await SessionLifecycle.WaitForHelper(new TaskCompletionSource<bool>().Task,child,canceled.Token);}catch(OperationCanceledException){stopped=true;}
  Require(stopped,"helper startup ignored user cancellation");await child.WaitForExitAsync();
 }
 Console.WriteLine("PASS early helper exit, established IPC and startup cancellation; no elevated helper started");

 // A real private pipe verifies graceful stop arrives before channel closure.
 var graceful=new SystemNetwork();var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
 var server=(System.IO.Pipes.NamedPipeServerStream)typeof(SystemNetwork).GetField("pipe",flags)!.GetValue(graceful)!;
 string name=(string)typeof(SystemNetwork).GetProperty("Name",flags)!.GetValue(graceful)!;
 using(var peer=new System.IO.Pipes.NamedPipeClientStream(".",name,System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.Asynchronous)){
  var accepting=server.WaitForConnectionAsync();await peer.ConnectAsync(3000);await accepting;
  var writer=new StreamWriter(server,new System.Text.UTF8Encoding(false),4096,true){AutoFlush=true};
  typeof(SystemNetwork).GetField("commands",flags)!.SetValue(graceful,writer);
  using var reader=new StreamReader(peer);var command=reader.ReadLineAsync();
  await graceful.DisposeAsync();Require(await command=="stop","shutdown closed IPC before graceful stop");
 }
 Console.WriteLine("PASS real private pipe graceful stop before disposal and concurrent guardian exit wait");
 var resilient=new SystemNetwork();
 var resilientPipe=(System.IO.Pipes.NamedPipeServerStream)typeof(SystemNetwork).GetField("pipe",flags)!.GetValue(resilient)!;
 string resilientName=(string)typeof(SystemNetwork).GetProperty("Name",flags)!.GetValue(resilient)!;
 using(var peer=new System.IO.Pipes.NamedPipeClientStream(".",resilientName,System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.Asynchronous)) {
  var accepting=resilientPipe.WaitForConnectionAsync();await peer.ConnectAsync(3000);await accepting;
  var confirmed=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  var reading=(Task)typeof(SystemNetwork).GetMethod("ReadReports",flags)!.Invoke(resilient,new object[]{new Action<string>(line=>{if(line.Contains("windows-connectivity"))confirmed.TrySetResult(true);})})!;
  var output=new StreamWriter(peer,new System.Text.UTF8Encoding(false),4096,true){AutoFlush=true};
  Guid adapter=Guid.NewGuid();
  await output.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new{type="network-active",guid=adapter,session="fixture"}));
  await output.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new{type="windows-connectivity",result=new{adapterGuid=adapter,adapterBytes=new{sent=1,received=1},checks=new[]{new{name="windows-https-no-proxy",passed=true},new{name="windows-system-dns",passed=true},new{name="windows-direct-udp-dns",passed=true}}}}));
  await confirmed.Task.WaitAsync(TimeSpan.FromSeconds(3));Require(resilient.Active,"fixture never became active");
  foreach(string bad in new[]{"{bad}","null","{\"type\":\"network-stopped\",\"recovered\":\"wrong\"}"})await output.WriteLineAsync(bad);
  await Task.Delay(50);Require(resilient.Active,"malformed diagnostics tore down a healthy IPC session");
  output.Dispose();await resilient.DisposeAsync();await reading.WaitAsync(TimeSpan.FromSeconds(3));
 }
 Console.WriteLine("PASS real private pipe: malformed samples preserve the active session; cancellation joins the reader");
 var cellular=new CellularAvailability();Require(!cellular.Observe(null)&&!cellular.Observe("unavailable")&&cellular.Observe("unavailable")&&!cellular.Observe(null),"cellular recovery debounce failed");
 Require(!cellular.Observe("CELLULAR Network fixture")&&!cellular.Observe(null)&&!cellular.Observe(null)&&cellular.Observe(null),"cellular recovery did not reset after good sample");Console.WriteLine("PASS transient cellular loss is debounced and recovery is requested once");
 for(int i=0;i<10;i++)Require(!cellular.Observe("control-unavailable"),"control delay tore down a working data path");
 string fixturePassword="Loopback-Only-Password";var encrypted=System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(fixturePassword),null,System.Security.Cryptography.DataProtectionScope.CurrentUser);var preferences=new HotspotPreferences("fixture",1,Convert.ToBase64String(encrypted));Require(preferences.Password==fixturePassword,"DPAPI password roundtrip failed");Require(!System.Text.Json.JsonSerializer.Serialize(preferences).Contains(fixturePassword),"password property leaked into JSON");Console.WriteLine("PASS Windows DPAPI hotspot password roundtrip; plaintext excluded from serialized settings");
 HotspotPreferences.FilePath=Path.Combine(Pairing.Folder,"hotspot.json");
 HotspotPreferences.Save("Salon TV",fixturePassword,1);
 var savedHotspot=HotspotPreferences.Read();Require(savedHotspot?.Ssid=="Salon TV"&&savedHotspot.Band==1&&savedHotspot.Password==fixturePassword,"saved hotspot did not survive reload");
 Require(!File.ReadAllText(HotspotPreferences.FilePath).Contains(fixturePassword),"password leaked to disk");
 using(var reload=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"--hotspot-reload",HotspotPreferences.FilePath}})!){await reload.WaitForExitAsync();Require(reload.ExitCode==0,"hotspot settings failed in fresh process");}
 HotspotPreferences.Save("Salon TV 2",fixturePassword+"2",1);
 Require(HotspotPreferences.Read()?.Password==fixturePassword+"2","edited password was not persisted");
 bool invalid=false;try{HotspotPreferences.Save("Salon TV","short",1);}catch(IOException){invalid=true;}
 Require(invalid&&HotspotPreferences.Read()?.Password==fixturePassword+"2","invalid edit destroyed saved credentials");
 Console.WriteLine("PASS persisted SSID/password/2.4 GHz reload, encrypted disk storage, edits and invalid-input preservation");
 var requestedSettings=new TrafficSettings("gaming",12,60,true,"adguard");
 using(var ack=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new{phoneSettingsConfirmed=true,settings=requestedSettings})))Require(requestedSettings.Acknowledged(ack.RootElement),"complete settings acknowledgement rejected");
 using(var oldAck=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new{phoneSettingsConfirmed=true,settings=requestedSettings with{UploadMbps=10}})))Require(!requestedSettings.Acknowledged(oldAck.RootElement),"stale ceiling acknowledged");
 using(var unconfirmed=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new{phoneSettingsConfirmed=false,settings=requestedSettings})))Require(!requestedSettings.Acknowledged(unconfirmed.RootElement),"phone confirmation skipped");
 Console.WriteLine("PASS exact live mode/DNS/ceiling acknowledgement and control-channel debounce");
 using(var empty=System.Text.Json.JsonDocument.Parse("{\"checks\":[]}"))Require(!Readiness.Healthy(empty.RootElement),"empty health accepted");
 using(var partial=System.Text.Json.JsonDocument.Parse("{\"checks\":[{\"name\":\"windows-https-no-proxy\",\"passed\":true}]}"))Require(!Readiness.Healthy(partial.RootElement),"partial health accepted");
 Console.WriteLine("PASS missing/partial health never reports connected");
 string checks="[{\"name\":\"windows-https-no-proxy\",\"passed\":true},{\"name\":\"windows-system-dns\",\"passed\":true},{\"name\":\"windows-direct-udp-dns\",\"passed\":true}]";
 foreach(var counters in new[]{"",",\"adapterGuid\":\"00000000-0000-0000-0000-000000000000\",\"adapterBytes\":{\"sent\":1,\"received\":1}",",\"adapterGuid\":\"11111111-1111-1111-1111-111111111111\",\"adapterBytes\":{\"sent\":0,\"received\":1}"}) {
  using var health=System.Text.Json.JsonDocument.Parse("{\"checks\":"+checks+counters+"}");Require(!Readiness.Healthy(health.RootElement),"Internet without proven tunnel traffic accepted");
 }
 using(var health=System.Text.Json.JsonDocument.Parse("{\"checks\":"+checks+",\"adapterGuid\":\"11111111-1111-1111-1111-111111111111\",\"adapterBytes\":{\"sent\":100,\"received\":100}}"))Require(Readiness.Healthy(health.RootElement),"proven tunnel checks rejected");
 Console.WriteLine("PASS HTTPS/DNS success requires nonempty tunnel identity and bidirectional interface traffic");

 using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
 int observations=0;await Readiness.WaitFor(()=>Task.FromResult(++observations==3),deadline.Token);Require(observations==3,"late readiness was skipped");Console.WriteLine("PASS delayed readiness is observed");
 using(var canceled=new CancellationTokenSource()){canceled.Cancel();bool stopped=false;try{await Readiness.WaitFor(()=>Task.FromResult(false),canceled.Token);}catch(OperationCanceledException){stopped=true;}Require(stopped,"cancel ignored");}Console.WriteLine("PASS user cancellation stops readiness");
 using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();using var delayed=new TcpListener(IPAddress.Loopback,port);
 var ready=Readiness.WaitForListener(port,()=>false,deadline.Token);Require(!ready.IsCompleted,"unbound port accepted");delayed.Start();await ready;Console.WriteLine("PASS real loopback listener readiness; no handshake bytes consumed");
 bool failed=false;try{await Readiness.WaitForListener(port,()=>true,deadline.Token);}catch(IOException){failed=true;}Require(failed,"dead worker accepted");Console.WriteLine("PASS worker exit fails before ready");
 for(int i=0;i<100;i++){double delay=Readiness.BackoffSeconds(i);Require(delay>=2&&delay<62,"unbounded retry delay");}Console.WriteLine("PASS retry backoff remains bounded");
 using var cancelPair=new CancellationTokenSource();cancelPair.Cancel();bool pairingStopped=false;try{await Readiness.Pair(port,"fixture",_=>Task.FromResult(true),cancelPair.Token);}catch(OperationCanceledException){pairingStopped=true;}Require(pairingStopped,"pair retry ignores cancellation");Console.WriteLine("PASS pairing retry cancellation");
} finally {if(Directory.Exists(Pairing.Folder))Directory.Delete(Pairing.Folder,true);}
