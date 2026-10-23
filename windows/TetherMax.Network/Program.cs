using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TetherMax.Network;

public static class Program
{
    static void Admin()
    {
        using var identity=WindowsIdentity.GetCurrent();
        if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new UnauthorizedAccessException("Explicit elevated network approval required");
    }
    public static async Task<int> Main(string[] args)
    {
        try {
            if(args.Length==0||args.SequenceEqual(new[]{"status"})) {Console.WriteLine("TetherMax network controller inactive; no OS changes. connect requires separate global routing and USB consent.");return 0;}
            if(args.Length==2&&args[0]=="watch") {Admin();var j=new Journal(args[1]);return await Watchdog.Run(j,j.Read(),new NativeBackend(j));}
            if(args.Length==3&&args[0]=="recover"&&args[2]=="--approve-network-recovery") {Admin();var j=new Journal(args[1]);Watchdog.Recover(j,j.Read(),new NativeBackend(j));return 0;}
            if(args.Length!=4||args[0]!="connect"||args[2]!="--approve-global-route-change"||args[3]!="--approve-usb-test"||!Regex.IsMatch(args[1],"^TetherMax-Network-[a-f0-9]{32}$"))
                throw new ArgumentException("No changes. Usage: connect <private-pipe> --approve-global-route-change --approve-usb-test | recover <protected-session-directory> --approve-network-recovery");
            Admin();return await Connect(args[1]);
        }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
    }
    sealed record Request(string Transport,string Token,int MaxSeconds,long MaxPayloadBytes);
    static async Task<string> CellularStatus(string engine,string transport,string bearer)
    {
        var start=new ProcessStartInfo(engine){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string arg in new[]{"cellular-status","--approve-usb-test","--transport",transport})start.ArgumentList.Add(arg);
        using var p=Process.Start(start)??throw new IOException("Cellular status process did not start");
        p.StandardInput.NewLine="\n";await p.StandardInput.WriteLineAsync(bearer);p.StandardInput.Close();
        var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try{await p.WaitForExitAsync(deadline.Token);}catch{p.Kill();throw;}
        string data=await output;if(data.Length>8192||p.ExitCode!=0)throw new IOException("Cellular source rejected: "+await error);
        return data;
    }
    static async Task<int> Connect(string pipeName)
    {
        using var single=new Mutex(false,"Global\\TetherMax-Network-Session-v1");
        bool acquired=false;try{acquired=single.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
        if(!acquired)throw new IOException("Another system-wide session owns the controller");
        try{return await Connected(pipeName);}finally{single.ReleaseMutex();}
    }
    static async Task<int> Connected(string pipeName)
    {
        using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        using var lifetime=new CancellationTokenSource();
        await pipe.ConnectAsync(15000,lifetime.Token);
        using var reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true);
        using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true,NewLine="\n"};
        using var inputTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        string line=await reader.ReadLineAsync(inputTimeout.Token)??throw new IOException("Private request missing");
        if(line.Length>4096)throw new IOException("Private request too large");
        var request=JsonSerializer.Deserialize<Request>(line)??throw new IOException("Invalid private request");line="";
        if(!Regex.IsMatch(request.Transport,"^127\\.0\\.0\\.1:[0-9]{1,5}$")||!Regex.IsMatch(request.Token,"^[a-f0-9]{32}$")||request.MaxSeconds<0||request.MaxSeconds>300||request.MaxPayloadBytes<0||request.MaxPayloadBytes>10*1024*1024||(request.MaxSeconds>0&&request.MaxPayloadBytes==0))
            throw new IOException("Invalid private transport or bounded test duration");
        // Refuse a new activation while earlier durable intents need recovery.
        foreach(string folder in Directory.EnumerateDirectories(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"TetherMax-Network-*"))
            if(File.Exists(Path.Combine(folder,"intent.json"))&&!File.Exists(Path.Combine(folder,"complete")))throw new IOException("Unrecovered prior network session: "+folder);
        Guid id=Guid.NewGuid();var journal=Journal.Create(id);
        string engine=Path.Combine(AppContext.BaseDirectory,"tethermax-engine.exe"),dll=Path.Combine(AppContext.BaseDirectory,"wintun.dll");
        string bearer=request.Token;
        journal.WriteNew("android-before.json",await CellularStatus(engine,request.Transport,bearer));
        var start=new ProcessStartInfo(engine){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string arg in new[]{"ip-worker","--approve-adapter-change","--approve-usb-test","--transport",request.Transport,"--payload-budget-bytes",request.MaxPayloadBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),"--wintun-dll",dll,"--journal",journal.PathFor("adapter.jsonl")})start.ArgumentList.Add(arg);
        using var worker=Process.Start(start)??throw new IOException("IP worker did not start");
        var stderr=worker.StandardError.ReadToEndAsync();
        worker.StandardInput.NewLine="\n";await worker.StandardInput.WriteLineAsync(request.Token);await worker.StandardInput.FlushAsync();
        int maxSeconds=request.MaxSeconds;request=request with{Token=""};
        // Heartbeats to the native worker and supervisor lease continue while
        // startup/configuration is in progress. They never carry the bearer.
        Task pulse=Task.Run(async()=>{try{while(!lifetime.IsCancellationRequested){journal.Pulse();await worker.StandardInput.BaseStream.WriteAsync(new byte[]{0},lifetime.Token);await worker.StandardInput.BaseStream.FlushAsync(lifetime.Token);await Task.Delay(1000,lifetime.Token);}}catch{lifetime.Cancel();}});
        Plan? plan=null;Process? watcher=null;Task? telemetry=null;
        var outputLock=new SemaphoreSlim(1);
        async Task Send(object value){await outputLock.WaitAsync();try{await writer.WriteLineAsync(JsonSerializer.Serialize(value));}finally{outputLock.Release();}}
        try {
            using var readyTimeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);readyTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            string identity=await worker.StandardOutput.ReadLineAsync(readyTimeout.Token)??throw new IOException("IP worker readiness missing");
            using var json=JsonDocument.Parse(identity);var root=json.RootElement;
            Guid guid=Guid.Parse(root.GetProperty("guid").GetString()!);string name=root.GetProperty("adapter").GetString()!;
            using var self=Process.GetCurrentProcess();
            plan=new Plan(id,guid,name,Wfp.Luid(guid),Guid.NewGuid(),Guid.NewGuid(),self.Id,self.StartTime.ToUniversalTime().Ticks,worker.Id,worker.StartTime.ToUniversalTime().Ticks);
            var backend=new NativeBackend(journal);
            // The independent watcher is not a child job and is not killed when
            // the GUI/guardian/worker crashes. Durable WFP state also survives it.
            using(var mutation=journal.Lock())
            Transaction.Apply(plan,backend,()=>journal.Intent(plan),()=>{
                var watchStart=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
                watchStart.ArgumentList.Add("watch");watchStart.ArgumentList.Add(journal.Folder);
                watcher=Process.Start(watchStart)??throw new IOException("Recovery watcher failed to start");
                var limit=Stopwatch.StartNew();
                while(!File.Exists(journal.PathFor("watcher-ready"))) {if(watcher.HasExited||limit.Elapsed>TimeSpan.FromSeconds(10))throw new IOException("No independent recovery acknowledgement");Thread.Sleep(50);}
                if(File.ReadAllText(journal.PathFor("watcher-ready"))!=watcher.Id.ToString())throw new IOException("Recovery acknowledgement mismatch");
            });
            telemetry=Task.Run(async()=>{try{while(await worker.StandardOutput.ReadLineAsync(lifetime.Token) is string data){using var item=JsonDocument.Parse(data);await Send(new{type="ip-counters",payload=item.RootElement.Clone()});}}catch{lifetime.Cancel();}});
            await Send(new{type="network-active",adapter=name,guid,session=journal.Folder,scope="system-wide IPv4; non-loopback IPv6 blocked; not carrier certification"});
            Task heartbeat=Task.Run(async()=>{try{while(!lifetime.IsCancellationRequested){using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(5));string? command=await reader.ReadLineAsync(timeout.Token);if(command!="heartbeat")break;}}catch{}finally{lifetime.Cancel();}});
            var uptime=Stopwatch.StartNew();
            while(!lifetime.IsCancellationRequested&&!worker.HasExited&&watcher is {HasExited:false}&&(maxSeconds==0||uptime.Elapsed<TimeSpan.FromSeconds(maxSeconds)))await Task.Delay(100,lifetime.Token);
        }catch(Exception e){try{await Send(new{type="network-error",message=e.Message});}catch{}}
        finally {
            try{journal.WriteNew("android-after.json",await CellularStatus(engine,request.Transport,bearer));}catch(Exception e){try{journal.WriteNew("android-after-error.txt",e.Message);}catch{}}
            bearer="";
            // Keep worker heartbeat alive until routes/DNS are removed. If the
            // worker already died, recovery still removes NRPT and persistent WFP.
            bool recovered=false;
            if(plan!=null) {
                try {Watchdog.Recover(journal,plan,new NativeBackend(journal));recovered=true;}
                catch(Exception e){try{await Send(new{type="recovery-pending",session=journal.Folder,message=e.Message});}catch{}}
            } else {try{if(!worker.HasExited){worker.Kill();await worker.WaitForExitAsync();}recovered=true;}catch{}}
            lifetime.Cancel();try{worker.StandardInput.Close();}catch{}
            await pulse; if(telemetry!=null)await telemetry;
            try{await Send(new{type="network-stopped",recovered,session=journal.Folder});}catch{}
            // Never kill the watcher on failed recovery.
            watcher?.Dispose();
        }
        return journal.Completed?0:1;
    }
}
