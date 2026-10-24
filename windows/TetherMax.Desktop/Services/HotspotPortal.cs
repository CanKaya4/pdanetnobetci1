using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
namespace TetherMax;
// A client reports completion only after its own browser completes an HTTPS fetch.
internal sealed class HotspotPortal : IAsyncDisposable {
 readonly TcpListener listener;
 readonly CancellationTokenSource stop=new();
 readonly string nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
 readonly ConcurrentDictionary<string,DateTimeOffset> verified=new();
 readonly Task loop;readonly bool loopback;readonly SemaphoreSlim slots=new(16);
 public IPEndPoint EndPoint=>(IPEndPoint)listener.LocalEndpoint;
 public IReadOnlyDictionary<string,DateTimeOffset> Verified=>verified;
 public HotspotPortal(IPAddress address,int port=80){loopback=IPAddress.IsLoopback(address);listener=new(address,port);listener.Start(16);loop=Run();}
 async Task Run(){try{while(!stop.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(stop.Token);if(slots.Wait(0))_=Serve(client);else client.Dispose();}}catch(OperationCanceledException){}catch(SocketException) when(stop.IsCancellationRequested){} }
 async Task Serve(TcpClient client){using(client){using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(8));try{
  var stream=client.GetStream();using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
  string? request=await reader.ReadLineAsync(timeout.Token);if(request==null)return;
  int total=request.Length;string? line;do{line=await reader.ReadLineAsync(timeout.Token);total+=line?.Length??0;if(total>8192)return;}while(!string.IsNullOrEmpty(line));
  var remote=((IPEndPoint)client.Client.RemoteEndPoint!).Address;
  if(loopback?!IPAddress.IsLoopback(remote):!remote.ToString().StartsWith("192.168.137.",StringComparison.Ordinal))return;
  bool accepted=request=="POST /verified/"+nonce+" HTTP/1.1";
  if(accepted)verified[remote.ToString()]=DateTimeOffset.UtcNow;
  string body=accepted?"OK":Page();byte[] bytes=Encoding.UTF8.GetBytes(body);
  string header="HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: "+bytes.Length+"\r\n\r\n";
  await stream.WriteAsync(Encoding.ASCII.GetBytes(header),timeout.Token);await stream.WriteAsync(bytes,timeout.Token);
 }catch(Exception e) when(e is IOException or SocketException or OperationCanceledException){} finally{slots.Release();} }}
 string Page()=>"""
<!doctype html><html lang="tr"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>TetherMax bağlantı kontrolü</title><style>body{background:#0c1424;color:#e5effa;font:18px system-ui;max-width:560px;margin:12vh auto;padding:24px}button{background:#55dce7;border:0;border-radius:14px;padding:16px;font-size:18px}p{line-height:1.6}</style><h1>TetherMax</h1><p id="status">Bu cihazın HTTPS bağlantısını kontrol edin.</p><button id="test">İnterneti kontrol et</button><script>document.getElementById('test').onclick=async()=>{const s=document.getElementById('status');s.textContent='HTTPS bağlantısı sınanıyor…';try{await fetch('https://www.gstatic.com/generate_204?t=NONCE-'+Date.now(),{mode:'no-cors',cache:'no-store',credentials:'omit',referrerPolicy:'no-referrer',signal:AbortSignal.timeout(12000)});await fetch('/verified/NONCE',{method:'POST',cache:'no-store'});s.textContent='Bu cihazda HTTPS bağlantısı çalıştı. Şimdi YouTube videosu açabilirsiniz.'}catch(e){s.textContent='HTTPS bağlantısı kurulamadı. TetherMax bilgisayarında hata durumu gösterilecek.'}};</script></html>
""".Replace("NONCE",nonce);
 public async ValueTask DisposeAsync(){stop.Cancel();listener.Stop();await loop;stop.Dispose();}
}
