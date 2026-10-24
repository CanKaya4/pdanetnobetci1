using System.IO;
using System.Net;
using System.Net.NetworkInformation;
namespace TetherMax;
internal static class Readiness {
 public static bool Healthy(System.Text.Json.JsonElement result) {
  if(!result.TryGetProperty("checks",out var checks)||checks.ValueKind!=System.Text.Json.JsonValueKind.Array||checks.GetArrayLength()!=3)return false;
  var required=new HashSet<string>{"windows-https-no-proxy","windows-system-dns","windows-direct-udp-dns"};
  foreach(var check in checks.EnumerateArray()) {
   if(!check.TryGetProperty("name",out var name)||name.ValueKind!=System.Text.Json.JsonValueKind.String||!required.Remove(name.GetString()!)||!check.TryGetProperty("passed",out var passed)||passed.ValueKind!=System.Text.Json.JsonValueKind.True)return false;
  }
  return required.Count==0;
 }

 public static double BackoffSeconds(int attempt)=>Math.Min(60,Math.Pow(2,Math.Min(attempt,5))*2)+Random.Shared.NextDouble()*2;
 public static async Task WaitFor(Func<Task<bool>> ready,CancellationToken cancel) {
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(TimeSpan.FromSeconds(30));
  int attempt=0;
  while(true) {timeout.Token.ThrowIfCancellationRequested();if(await ready()){timeout.Token.ThrowIfCancellationRequested();return;}await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1500,100*Math.Pow(2,Math.Min(attempt++,4)))) ,timeout.Token);}
 }
 public static async Task<string> Pair(int port,string serial,Func<string,Task<bool>> approve,CancellationToken cancel) {
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel);deadline.CancelAfter(TimeSpan.FromSeconds(60));
  for(int attempt=0;;attempt++) {
   try {return await Pairing.Connect(port,serial,approve,deadline.Token);}
   catch(Exception e) when(attempt<4 && (e is System.Net.Sockets.SocketException || e is EndOfStreamException)) {await Task.Delay(TimeSpan.FromSeconds(BackoffSeconds(attempt)),deadline.Token);}
  }
 }
 public static Task WaitForListener(int port,Func<bool> exited,CancellationToken cancel)=>WaitFor(()=>{
  if(exited())throw new IOException("Aktarım motoru hazır olmadan durdu.");
  return Task.FromResult(IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p=>p.Port==port&&IPAddress.IsLoopback(p.Address)));
 },cancel);
}

