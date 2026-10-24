using System.Net;
using System.Net.Sockets;
using TetherMax;
if(args.SequenceEqual(new[]{"--guardian-fixture"})){await Task.Delay(600);return;}
static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
Pairing.Folder=Path.Combine(Path.GetTempPath(),"TetherMax-connect-"+Guid.NewGuid().ToString("N"));
try {
 using(var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,Arguments="--guardian-fixture"})!){
  var exited=child.WaitForExitAsync();var network=new SystemNetwork();typeof(SystemNetwork).GetField("guardian",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(network,child);
  var first=network.DisposeAsync().AsTask();var second=network.DisposeAsync().AsTask();Require(!second.IsCompleted||exited.IsCompletedSuccessfully,"concurrent disposal skipped guardian recovery wait");await Task.WhenAll(first,second);Require(exited.IsCompletedSuccessfully,"recovery wait returned before guardian exit");
 }Console.WriteLine("PASS concurrent network disposal shares the guardian recovery completion; no network helper started");

 var cellular=new CellularAvailability();Require(!cellular.Observe(null)&&!cellular.Observe("unavailable")&&cellular.Observe("unavailable")&&!cellular.Observe(null),"cellular recovery debounce failed");
 Require(!cellular.Observe("CELLULAR Network fixture")&&!cellular.Observe(null)&&!cellular.Observe(null)&&cellular.Observe(null),"cellular recovery did not reset after good sample");Console.WriteLine("PASS transient cellular loss is debounced and recovery is requested once");
 for(int i=0;i<10;i++)Require(!cellular.Observe("control-unavailable"),"control delay tore down a working data path");
 string fixturePassword="Loopback-Only-Password";var encrypted=System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(fixturePassword),null,System.Security.Cryptography.DataProtectionScope.CurrentUser);var preferences=new HotspotPreferences("fixture",1,Convert.ToBase64String(encrypted));Require(preferences.Password==fixturePassword,"DPAPI password roundtrip failed");Require(!System.Text.Json.JsonSerializer.Serialize(preferences).Contains(fixturePassword),"password property leaked into JSON");Console.WriteLine("PASS Windows DPAPI hotspot password roundtrip; plaintext excluded from serialized settings");
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


