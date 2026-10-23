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
    public static async Task<int> Rescue()
    {
        Admission();var lifetime=Stopwatch.StartNew();string done=Path.Combine(Workspace,"build","reports","native-rescue-finished");
        int parent=int.Parse(Environment.GetEnvironmentVariable("TETHERMAX_NATIVE_TEST_PID")!);
        long parentStart=long.Parse(Environment.GetEnvironmentVariable("TETHERMAX_NATIVE_TEST_START")!);
        while(lifetime.Elapsed<TimeSpan.FromMinutes(15)&&!File.Exists(done)) {
            bool parentAlive=ProcessIdentity.Alive(parent,parentStart);
            foreach(string folder in Directory.EnumerateDirectories(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"TetherMax-Network-*")) {
                if(File.Exists(Path.Combine(folder,"complete")))continue;
                string stamp=File.Exists(Path.Combine(folder,"intent.json"))?"intent.json":"controller-process.json";
                if(!File.Exists(Path.Combine(folder,stamp))||(parentAlive&&DateTime.UtcNow-File.GetLastWriteTimeUtc(Path.Combine(folder,stamp))<TimeSpan.FromSeconds(90)))continue;
                // Disposable-VM emergency ONLY: restore runner control-plane
                // access so a failed fail-closed test can publish diagnostics.
                // This mode is never packaged into the product or counted PASS.
                string log=Path.Combine(Workspace,"build","reports","native-rescue.log");
                try {
                    var j=new Journal(folder);File.AppendAllText(log,"Emergency VM recovery required: "+folder+Environment.NewLine);
                    foreach(string processFile in new[]{"watcher-process.json","controller-process.json"})if(File.Exists(j.PathFor(processFile))){using var identity=JsonDocument.Parse(File.ReadAllText(j.PathFor(processFile)));ProcessIdentity.Stop(identity.RootElement.GetProperty("pid").GetInt32(),identity.RootElement.GetProperty("start").GetInt64());}
                    if(!File.Exists(j.PathFor("intent.json")))continue;
                    var p=j.Read();
                    ProcessIdentity.Stop(p.WorkerPid,p.WorkerStart);ProcessIdentity.Stop(p.GuardianPid,p.GuardianStart);
                    await Task.Delay(1000);
                    try{new NativeBackend(j).RemoveConfiguration(p);}catch(Exception e){File.AppendAllText(log,e+Environment.NewLine);}
                    using var wfp=new Wfp();wfp.Remove(p);
                }catch(Exception e){File.AppendAllText(log,e+Environment.NewLine);}
            }
            if(!parentAlive)return 1;
            await Task.Delay(1000);
        }
        return 0;
    }
    public static async Task<int> Owner()
    {
        Admission();
        string token=Environment.GetEnvironmentVariable("TETHERMAX_TEST_TOKEN")??throw new Exception("fixture token missing");
        string transport=Environment.GetEnvironmentVariable("TETHERMAX_NATIVE_TRANSPORT")??throw new Exception("fixture transport missing");
        string name="TetherMax-Network-"+Guid.NewGuid().ToString("N");
        using var pipe=new System.IO.Pipes.NamedPipeServerStream(name,System.IO.Pipes.PipeDirection.InOut,1,System.IO.Pipes.PipeTransmissionMode.Byte,System.IO.Pipes.PipeOptions.Asynchronous|System.IO.Pipes.PipeOptions.CurrentUserOnly);
        using var helper=Start(Path.Combine(Bundle,"TetherMax.Network.exe"),"connect",name,"--approve-global-route-change","--approve-usb-test");
        var errors=helper.StandardError.ReadToEndAsync();
        using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(150));
        await pipe.WaitForConnectionAsync(stop.Token);
        var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true,NewLine="\n"};
        using var reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new{Transport=transport,Token=token,MaxSeconds=120,MaxPayloadBytes=1048576L}));token="";
        var pulse=Task.Run(async()=>{try{while(!stop.IsCancellationRequested){await writer.WriteLineAsync("heartbeat");await Task.Delay(500,stop.Token);}}catch{}});
        try {
            while(await reader.ReadLineAsync(stop.Token) is string line) {
                using var message=JsonDocument.Parse(line);string? type=message.RootElement.GetProperty("type").GetString();
                if(type=="network-active"){Console.WriteLine(message.RootElement.GetProperty("session").GetString());Console.Out.Flush();}
                if(type=="network-error"){Console.Error.WriteLine(line);throw new Exception(line);}
                if(type=="network-stopped")break;
            }
        }finally{stop.Cancel();await pulse;try{writer.Dispose();}catch{}}
        await helper.WaitForExitAsync();
        if(helper.ExitCode!=0)throw new Exception("Production guardian failed: "+await errors.WaitAsync(TimeSpan.FromSeconds(3)));
        return 0;
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
        using(var parent=Process.GetCurrentProcess()){Environment.SetEnvironmentVariable("TETHERMAX_NATIVE_TEST_PID",parent.Id.ToString());Environment.SetEnvironmentVariable("TETHERMAX_NATIVE_TEST_START",parent.StartTime.ToUniversalTime().Ticks.ToString());}
        using var rescue=Start(Environment.ProcessPath!,"--native-rescue");
        string token=Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        Environment.SetEnvironmentVariable("TETHERMAX_TEST_TOKEN",token);Environment.SetEnvironmentVariable("TETHERMAX_FIXTURE_MAP_BENCHMARK","1");
        Environment.SetEnvironmentVariable("TETHERMAX_FIXTURE_CELLULAR_ATTESTATION","1");
        using var java=Start("java","-cp",Path.Combine(Workspace,"build","engine-classes"),"io.tethermax.engine.TestServer");
        var javaErrors=java.StandardError.ReadToEndAsync();
        try {
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
        await using var trap=await FallbackTrap.Start(Bundle,"127.0.0.1:"+javaPort,token);
        async Task<bool> TrapUDP()
        {
            using var c=new UdpClient(AddressFamily.InterNetwork);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));
            byte[] bytes=RandomNumberGenerator.GetBytes(64);
            try{c.Connect(IPAddress.Parse("198.18.0.2"),udpPort);await c.SendAsync(bytes);var response=await c.ReceiveAsync(timeout.Token);return response.Buffer.SequenceEqual(bytes);}catch(SocketException){return false;}catch(OperationCanceledException){return false;}
        }
        async Task TrapV6(){using var c=new UdpClient(AddressFamily.InterNetworkV6);try{await c.SendAsync(new byte[]{1},new IPEndPoint(IPAddress.Parse("2001:db8:ffff::1"),32123));}catch(SocketException){}await Task.Delay(1500);}
        Check(await TrapUDP(),"fallback trap positive control failed before guard");
        await Task.Delay(2000);long v6Before=trap.Rejected;await TrapV6();Check(trap.Rejected>v6Before,"IPv6 trap positive control did not observe packets");
        string testsExe=Environment.ProcessPath!;
        try {
            foreach(string crash in new[]{"gui","guardian","worker","watcher"}) {
                using var owner=Start(testsExe,"--native-owner");var ownerErrors=owner.StandardError.ReadToEndAsync();Journal? j=null;Plan? p=null;
                try {
                    using var startTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    string folder=await owner.StandardOutput.ReadLineAsync(startTimeout.Token)??throw new Exception("Native owner failed: "+await ownerErrors.WaitAsync(TimeSpan.FromSeconds(3)));
                    j=new Journal(folder);p=j.Read();using(var wfp=new Wfp()){Check(wfp.Exists(p.V4Filter)&&wfp.Exists(p.V6Filter),"native persistent WFP filters not installed");}
                    var selected=NativeBackend.PowerShell("(Find-NetRoute -RemoteIPAddress 1.1.1.1 | Select-Object -First 1).InterfaceAlias");Check(selected.Trim()==p.AdapterName,"real Windows route not on Wintun");
                    string fallbackSelected=NativeBackend.PowerShell("(Find-NetRoute -RemoteIPAddress 198.18.0.2 | Select-Object -First 1).InterfaceAlias");Check(fallbackSelected.Trim()==trap.Name,"negative probe must target the owned fallback adapter");
                    Check(!await TrapUDP(),"IPv4 UDP leaked to the alternate owned adapter");
                    long rejectedBefore=trap.Rejected;await TrapV6();Check(trap.Rejected==rejectedBefore,"IPv6 escaped the WFP guard");
                    using var handler=new HttpClientHandler{UseProxy=false,ServerCertificateCustomValidationCallback=(_,remote,_,_)=>remote?.GetCertHashString()==cert.GetCertHashString()};
                    using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(15)};
                    byte[] received=await http.GetByteArrayAsync("https://198.18.0.1:"+tcpPort+"/");Check(received.SequenceEqual(body),"native HTTPS corruption");
                    using var datagram=new UdpClient(AddressFamily.InterNetwork);datagram.Connect(IPAddress.Parse("198.18.0.1"),udpPort);
                    byte[] payload=RandomNumberGenerator.GetBytes(1200);await datagram.SendAsync(payload);
                    using var queryTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));var reply=await datagram.ReceiveAsync(queryTimeout.Token);Check(reply.Buffer.SequenceEqual(payload),"native UDP mismatch");
                    IPAddress[] addresses=await Dns.GetHostAddressesAsync("tethermax-"+Guid.NewGuid().ToString("N")+".invalid",queryTimeout.Token);
                    Check(addresses.Contains(IPAddress.Parse("198.18.0.1"))&&dnsQueries>0,"Windows DNS did not traverse relay fixture");
                    if(crash=="gui"){owner.Kill();await owner.WaitForExitAsync();}
                    if(crash=="guardian")ProcessIdentity.Stop(p.GuardianPid,p.GuardianStart);
                    if(crash=="worker")ProcessIdentity.Stop(p.WorkerPid,p.WorkerStart);
                    if(crash=="watcher"){int pid=int.Parse(File.ReadAllText(j.PathFor("watcher-ready")));using var watcher=Process.GetProcessById(pid);watcher.Kill();await watcher.WaitForExitAsync();}
                    var timer=Stopwatch.StartNew();while(!j.Completed){if(timer.Elapsed>TimeSpan.FromSeconds(65))throw new Exception("Native independent rollback timeout");await Task.Delay(200);}
                    using(var wfp=new Wfp()){Check(!wfp.Exists(p.V4Filter)&&!wfp.Exists(p.V6Filter),"native filter leak");}
                    string restored=NativeBackend.PowerShell("Find-NetRoute -RemoteIPAddress 1.1.1.1 | Select-Object InterfaceIndex,DestinationPrefix,NextHop | ConvertTo-Json -Compress");Check(restored==baseline,"previous Windows route was not restored");
                    Check(await TrapUDP(),"rollback did not restore ordinary alternate-path behavior");
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
            Environment.SetEnvironmentVariable("TETHERMAX_FIXTURE_CELLULAR_ATTESTATION",null);
        }
        Check(!File.Exists(Path.Combine(Workspace,"build","reports","native-rescue.log")),"Disposable VM emergency recovery was required; native implementation failed");
        File.WriteAllText(Path.Combine(Workspace,"build","reports","native-rescue-finished"),"done");
        return 0;
        }finally{stop.Cancel();if(!java.HasExited){java.Kill();await java.WaitForExitAsync();}}
    }
}
