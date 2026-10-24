using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace TetherMax.Network;

public static class ConnectivityChecks
{
    public static async Task<string> Run(Guid adapterGuid,CancellationToken cancellation)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));var ct=deadline.Token;
        var adapter=NetworkInterface.GetAllNetworkInterfaces().Single(n=>Guid.TryParse(n.Id,out var id)&&id==adapterGuid);
        var local=adapter.GetIPProperties().UnicastAddresses.Single(a=>a.Address.AddressFamily==AddressFamily.InterNetwork).Address;
        var before=adapter.GetIPv4Statistics();
        async Task<object> Check(string name,Func<Task<object>> action) {
            var timer=Stopwatch.StartNew();
            try{var detail=await action();ct.ThrowIfCancellationRequested();return new{name,passed=true,elapsedMs=timer.Elapsed.TotalMilliseconds,detail,error=(string?)null};}
            catch(Exception e){return new{name,passed=false,elapsedMs=timer.Elapsed.TotalMilliseconds,detail=(object?)null,error=e.Message};}
        }
        var results=await Task.WhenAll(
            Check("windows-https-no-proxy",async()=>{
                using var http=new HttpClient(new SocketsHttpHandler{UseProxy=false,AllowAutoRedirect=false,ConnectCallback=async(context,token)=>{
                    var socket=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);
                    try{socket.Bind(new IPEndPoint(local,0));await socket.ConnectAsync(context.DnsEndPoint,token);return new NetworkStream(socket,true);}
                    catch{socket.Dispose();throw;}
                }});
                using var response=await http.GetAsync("https://example.com/",HttpCompletionOption.ResponseHeadersRead,ct);
                response.EnsureSuccessStatusCode();return new{status=(int)response.StatusCode,tls="Windows default certificate validation"};}),
            Check("windows-system-dns",async()=>{
                var ips=await Dns.GetHostAddressesAsync("example.com",ct);if(!ips.Any(i=>i.AddressFamily==AddressFamily.InterNetwork))throw new IOException("No IPv4 DNS answer");
                return new{addresses=ips.Select(i=>i.ToString()).ToArray()};}),
            Check("windows-direct-udp-dns",async()=>{
                using var udp=new UdpClient(new IPEndPoint(local,0));udp.Connect("1.1.1.1",53);
                byte[] query=Convert.FromHexString("000001000001000000000000076578616D706C6503636F6D0000010001");RandomNumberGenerator.Fill(query.AsSpan(0,2));
                await udp.SendAsync(query,ct);var answer=(await udp.ReceiveAsync(ct)).Buffer;
                if(answer.Length<query.Length||answer[0]!=query[0]||answer[1]!=query[1]||(answer[2]&128)==0||(answer[2]&2)!=0||(answer[3]&15)!=0||(answer[6]==0&&answer[7]==0)||!answer.AsSpan(12,query.Length-12).SequenceEqual(query.AsSpan(12)))throw new IOException("Invalid or truncated UDP DNS response");
                return new{bytes=answer.Length};}));
        var after=adapter.GetIPv4Statistics();
        return JsonSerializer.Serialize(new{observedAt=DateTimeOffset.UtcNow,adapterGuid,checks=results,adapterBytes=new{sent=after.BytesSent-before.BytesSent,received=after.BytesReceived-before.BytesReceived},scope="ordinary Windows sockets while owned routes and WFP are active; carrier accounting not measured"});
    }
}
