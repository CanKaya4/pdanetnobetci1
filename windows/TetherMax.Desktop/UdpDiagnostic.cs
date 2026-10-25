using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace TetherMax;
internal static class UdpDiagnostic
{
    static async Task<byte[]> Read(NetworkStream stream,int n,CancellationToken token) { byte[] b=new byte[n];await stream.ReadExactlyAsync(b,token);return b; }
    public static async Task<List<string>> Run(int socksPort)
    {
        var results=new List<string>();var rtts=new List<double>();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));var ct=timeout.Token;
        try {
            using var control=new TcpClient();await control.ConnectAsync(IPAddress.Loopback,socksPort,ct);using var stream=control.GetStream();
            await stream.WriteAsync(new byte[]{5,1,0},ct);if(!(await Read(stream,2,ct)).SequenceEqual(new byte[]{5,0}))throw new IOException("SOCKS doğrulaması reddedildi.");
            await stream.WriteAsync(new byte[]{5,3,0,1,0,0,0,0,0,0},ct);
            byte[] header=await Read(stream,4,ct);if(!header.SequenceEqual(new byte[]{5,0,0,1}))throw new IOException("UDP ilişkilendirmesi reddedildi.");
            byte[] addr=await Read(stream,6,ct);var host=new IPAddress(addr.AsSpan(0,4));int port=(addr[4]<<8)|addr[5];
            if(!IPAddress.IsLoopback(host)||port==0)throw new IOException("UDP dinleyicisi localhost değil.");
            using var udp=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));udp.Connect(host,port);
            byte[] question=new byte[]{7,101,120,97,109,112,108,101,3,99,111,109,0,0,1,0,1};
            for(int i=0;i<5;i++) {
                using var sample=CancellationTokenSource.CreateLinkedTokenSource(ct);sample.CancelAfter(TimeSpan.FromSeconds(3));
                byte[] id=RandomNumberGenerator.GetBytes(2);
                byte[] query=new byte[]{0,0,0,1,1,1,1,1,0,53}.Concat(id).Concat(new byte[]{1,0,0,1,0,0,0,0,0,0}).Concat(question).ToArray();
                var watch=Stopwatch.StartNew();
                try {
                    await udp.SendAsync(query,sample.Token);byte[] response=(await udp.ReceiveAsync(sample.Token)).Buffer;
                    if(response.Length<22+question.Length||!response.AsSpan(0,10).SequenceEqual(query.AsSpan(0,10))||response[10]!=id[0]||response[11]!=id[1]||(response[12]&128)==0||(response[13]&15)!=0||(response[12]&2)!=0||(response[16]==0&&response[17]==0)||!response.AsSpan(22,question.Length).SequenceEqual(question))throw new IOException("UDP DNS yanıtı doğrulanamadı.");
                    rtts.Add(watch.Elapsed.TotalMilliseconds);
                }catch(Exception ex)when(ex is IOException or SocketException or OperationCanceledException){results.Add("UDP DNS "+(i+1)+": yanıt yok / geçersiz.");}
                await Task.Delay(250,ct);
            }
            results.Add("UDP DNS: "+rtts.Count+"/5 doğrulanmış yanıt (1.1.1.1:53). Bu küçük örneklem genel paket kaybı ölçümü değildir.");
            if(rtts.Count>0){rtts.Sort();results.Add($"DNS RTT: min={rtts[0]:F1} ms medyan={rtts[rtts.Count/2]:F1} ms max={rtts[^1]:F1} ms. ICMP ping veya USB gecikmesi değildir.");}
        }catch(Exception ex){results.Add("UDP testi: "+ex.Message);}
        return results;
    }
}
