using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using TetherMax.Network;

static class NativeTests
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static string Workspace=>Environment.GetEnvironmentVariable("GITHUB_WORKSPACE")??throw new Exception("Isolated GitHub workspace required");
    static string Bundle=>Path.Combine(Workspace,"build","windows-network");
    static void Admission()
    {
        Check(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")=="true"&&Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT")=="github-hosted","Native tests require an ephemeral GitHub-hosted runner; forbidden on the user's PC");
        using var identity=WindowsIdentity.GetCurrent();Check(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),"Native isolated test requires runner admin");
    }
    static Process Start(string file,params string[] args)
    {
        var s=new ProcessStartInfo(file){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string arg in args)s.ArgumentList.Add(arg);return Process.Start(s)??throw new IOException("Isolated test process failed");
    }
    public static async Task<int> Owner()
    {
        Admission();
        string token=Environment.GetEnvironmentVariable("TETHERMAX_TEST_TOKEN")??throw new Exception("fixture token missing");
        string transport=Environment.GetEnvironmentVariable("TETHERMAX_NATIVE_TRANSPORT")??throw new Exception("fixture transport missing");
        var j=Journal.Create(Guid.NewGuid());
        using var worker=Start(Path.Combine(Bundle,"tethermax-engine.exe"),"ip-worker","--approve-adapter-change","--approve-usb-test","--transport",transport,"--wintun-dll",Path.Combine(Bundle,"wintun.dll"),"--journal",j.PathFor("adapter.jsonl"));
        var errors=worker.StandardError.ReadToEndAsync();
        worker.StandardInput.NewLine="\n";await worker.StandardInput.WriteLineAsync(token);await worker.StandardInput.FlushAsync();token="";
        using var cancel=new CancellationTokenSource();
        Task heartbeat=Task.Run(async()=>{try{while(!cancel.IsCancellationRequested){j.Pulse();await worker.StandardInput.BaseStream.WriteAsync(new byte[]{0},cancel.Token);await worker.StandardInput.BaseStream.FlushAsync(cancel.Token);await Task.Delay(500,cancel.Token);}}catch{}});
        Plan? p=null;Process? watcher=null;
        try {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string line=await worker.StandardOutput.ReadLineAsync(deadline.Token)??throw new Exception("Native adapter worker failed: "+await errors);
            using var identity=JsonDocument.Parse(line);Guid guid=Guid.Parse(identity.RootElement.GetProperty("guid").GetString()!);
            using var self=Process.GetCurrentProcess();
            p=new Plan(Guid.NewGuid(),guid,identity.RootElement.GetProperty("adapter").GetString()!,Wfp.Luid(guid),Guid.NewGuid(),Guid.NewGuid(),self.Id,self.StartTime.ToUniversalTime().Ticks,worker.Id,worker.StartTime.ToUniversalTime().Ticks);
            using(var gate=j.Lock())Transaction.Apply(p,new NativeBackend(j),()=>j.Intent(p),()=>{
                watcher=Start(Path.Combine(Bundle,"TetherMax.Network.exe"),"watch",j.Folder);var timer=Stopwatch.StartNew();
                while(!File.Exists(j.PathFor("watcher-ready"))){if(watcher.HasExited||timer.Elapsed>TimeSpan.FromSeconds(10))throw new Exception("Native watcher missing: "+watcher.StandardError.ReadToEnd());Thread.Sleep(25);}
            });
            _=Task.Run(async()=>{try{using var log=new StreamWriter(new FileStream(j.PathFor("native-counters.jsonl"),FileMode.CreateNew,FileAccess.Write,FileShare.ReadWrite)){AutoFlush=true};while(await worker.StandardOutput.ReadLineAsync(cancel.Token) is string data)await log.WriteLineAsync(data);}catch{}});
            Console.WriteLine(j.Folder);Console.Out.Flush();
            var uptime=Stopwatch.StartNew();
            while(!worker.HasExited&&!watcher!.HasExited&&uptime.Elapsed<TimeSpan.FromSeconds(120)&&!j.Completed)await Task.Delay(100);
        } finally {
            if(p!=null)try{Watchdog.Recover(j,p,new NativeBackend(j));}catch(Exception e){Console.Error.WriteLine(e);}
            cancel.Cancel();worker.StandardInput.Close();await heartbeat;watcher?.Dispose();
        }
        return j.Completed?0:1;
    }
    static async Task<byte[]> DnsReply(byte[] q)
    {
        await Task.CompletedTask;
        if(q.Length<17)throw new IOException("short DNS fixture query");
        int end=12;while(end<q.Length&&q[end]!=0){end+=1+q[end];}end+=5;
        if(end>q.Length)throw new IOException("bad DNS fixture question");
        bool a=BinaryPrimitives.ReadUInt16BigEndian(q.AsSpan(end-4))==1;
        byte[] reply=new byte[end+(a?16:0)];Array.Copy(q,reply,end);
        reply[2]=0x81;reply[3]=0x80;reply[6]=0;reply[7]=(byte)(a?1:0);reply[8]=reply[9]=reply[10]=reply[11]=0;
        if(a)new byte[]{0xc0,0x0c,0,1,0,1,0,0,0,0,0,4,198,18,0,1}.CopyTo(reply,end);
        return reply;
    }
    public static async Task<int> Run()
    {
        Admission();using var stop=new CancellationTokenSource();
        string token=Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        Environment.SetEnvironmentVariable("TETHERMAX_TEST_TOKEN",token);Environment.SetEnvironmentVariable("TETHERMAX_FIXTURE_MAP_BENCHMARK","1");
        using var java=Start("java","-cp",Path.Combine(Workspace,"build","engine-classes"),"io.tethermax.engine.TestServer");
        var javaErrors=java.StandardError.ReadToEndAsync();
        using var initialTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        string javaPort=await java.StandardOutput.ReadLineAsync(initialTimeout.Token)??throw new Exception("Java peer failed");
        Environment.SetEnvironmentVariable("TETHERMAX_NATIVE_TRANSPORT","127.0.0.1:"+int.Parse(javaPort));
        using var rsa=RSA.Create(2048);var certRequest=new CertificateRequest("CN=tethermax-fixture",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        using var cert=certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));
        var tcp=new TcpListener(IPAddress.Loopback,0);tcp.Start();int tcpPort=((IPEndPoint)tcp.LocalEndpoint).Port;
        using var udp=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));int udpPort=((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        using var dns=new UdpClient(new IPEndPoint(IPAddress.Loopback,53));int dnsQueries=0;
        byte[] body=RandomNumberGenerator.GetBytes(256*1024);
        Task tlsServer=Task.Run(async()=>{try{while(!stop.IsCancellationRequested){using var c=await tcp.AcceptTcpClientAsync(stop.Token);using var ssl=new SslStream(c.GetStream());await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions{ServerCertificate=cert},stop.Token);using var reader=new StreamReader(ssl,Encoding.ASCII,false,4096,true);while(await reader.ReadLineAsync(stop.Token) is string s&&s!=""){}await ssl.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n"),stop.Token);await ssl.WriteAsync(body,stop.Token);}}catch(OperationCanceledException){}catch(ObjectDisposedException){}});
        Task echoServer=Task.Run(async()=>{try{while(!stop.IsCancellationRequested){var packet=await udp.ReceiveAsync(stop.Token);await udp.SendAsync(packet.Buffer,packet.RemoteEndPoint,stop.Token);}}catch(OperationCanceledException){}catch(ObjectDisposedException){}});
        Task dnsServer=Task.Run(async()=>{try{while(!stop.IsCancellationRequested){var packet=await dns.ReceiveAsync(stop.Token);Interlocked.Increment(ref dnsQueries);await dns.SendAsync(await DnsReply(packet.Buffer),packet.RemoteEndPoint,stop.Token);}}catch(OperationCanceledException){}catch(ObjectDisposedException){}});
        string baseline=NativeBackend.PowerShell("Find-NetRoute -RemoteIPAddress 1.1.1.1 | Select-Object InterfaceIndex,DestinationPrefix,NextHop | ConvertTo-Json -Compress");
        string testsExe=Environment.ProcessPath!;
        try {
            foreach(string crash in new[]{"guardian","worker","watcher"}) {
                using var owner=Start(testsExe,"--native-owner");var ownerErrors=owner.StandardError.ReadToEndAsync();Journal? j=null;Plan? p=null;
                try {
                    using var startTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    string folder=await owner.StandardOutput.ReadLineAsync(startTimeout.Token)??throw new Exception("Native owner failed: "+await ownerErrors);
                    j=new Journal(folder);p=j.Read();using(var wfp=new Wfp()){Check(wfp.Exists(p.V4Filter)&&wfp.Exists(p.V6Filter),"native persistent WFP filters not installed");}
                    var selected=NativeBackend.PowerShell("(Find-NetRoute -RemoteIPAddress 1.1.1.1 | Select-Object -First 1).InterfaceAlias");Check(selected.Trim()==p.AdapterName,"real Windows route not on Wintun");
                    using var handler=new HttpClientHandler{UseProxy=false,ServerCertificateCustomValidationCallback=(_,remote,_,_)=>remote?.GetCertHashString()==cert.GetCertHashString()};
                    using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(15)};
                    byte[] received=await http.GetByteArrayAsync("https://198.18.0.1:"+tcpPort+"/");Check(received.SequenceEqual(body),"native HTTPS corruption");
                    using var datagram=new UdpClient(AddressFamily.InterNetwork);datagram.Connect(IPAddress.Parse("198.18.0.1"),udpPort);
                    byte[] payload=RandomNumberGenerator.GetBytes(1200);await datagram.SendAsync(payload);
                    using var queryTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));var reply=await datagram.ReceiveAsync(queryTimeout.Token);Check(reply.Buffer.SequenceEqual(payload),"native UDP mismatch");
                    IPAddress[] addresses=await Dns.GetHostAddressesAsync("tethermax-"+Guid.NewGuid().ToString("N")+".invalid",queryTimeout.Token);
                    Check(addresses.Contains(IPAddress.Parse("198.18.0.1"))&&dnsQueries>0,"Windows DNS did not traverse relay fixture");
                    if(crash=="guardian"){owner.Kill();await owner.WaitForExitAsync();}
                    if(crash=="worker")ProcessIdentity.Stop(p.WorkerPid,p.WorkerStart);
                    if(crash=="watcher"){int pid=int.Parse(File.ReadAllText(j.PathFor("watcher-ready")));using var watcher=Process.GetProcessById(pid);watcher.Kill();await watcher.WaitForExitAsync();}
                    var timer=Stopwatch.StartNew();while(!j.Completed){if(timer.Elapsed>TimeSpan.FromSeconds(65))throw new Exception("Native independent rollback timeout");await Task.Delay(200);}
                    using(var wfp=new Wfp()){Check(!wfp.Exists(p.V4Filter)&&!wfp.Exists(p.V6Filter),"native filter leak");}
                    string restored=NativeBackend.PowerShell("Find-NetRoute -RemoteIPAddress 1.1.1.1 | Select-Object InterfaceIndex,DestinationPrefix,NextHop | ConvertTo-Json -Compress");Check(restored==baseline,"previous Windows route was not restored");
                    Console.WriteLine("PASS native Wintun -> gVisor -> Java loopback HTTPS/UDP/Windows DNS; "+crash+" crash rollback");
                }finally {
                    if(j!=null&&p!=null&&!j.Completed)Watchdog.Recover(j,p,new NativeBackend(j));
                    if(!owner.HasExited){owner.Kill();await owner.WaitForExitAsync();}
                }
            }
        }finally {
            stop.Cancel();tcp.Stop();udp.Dispose();dns.Dispose();
            if(!java.HasExited){java.Kill();await java.WaitForExitAsync();}
            await Task.WhenAll(tlsServer,echoServer,dnsServer);
            Environment.SetEnvironmentVariable("TETHERMAX_TEST_TOKEN",null);Environment.SetEnvironmentVariable("TETHERMAX_FIXTURE_MAP_BENCHMARK",null);
        }
        return 0;
    }
}
