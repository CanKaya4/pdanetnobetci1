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
            ProcessLaunch.DetachStandardHandleInheritance();
            if(args.Length==0||args.SequenceEqual(new[]{"status"})) {Console.WriteLine("TetherMax network controller inactive; no OS changes. connect requires separate global routing and USB consent.");return 0;}
            if(args.Length==4&&args[0]=="hotspot-start"&&args[3]=="--approve-windows-sharing") {
                Admin();if(!Regex.IsMatch(args[2],"^TetherMax-Hotspot-[a-f0-9]{32}$"))throw new IOException("Invalid hotspot pipe");
                using var channel=new NamedPipeClientStream(".",args[2],PipeDirection.InOut,PipeOptions.Asynchronous);
                await channel.ConnectAsync(15000);using var input=new StreamReader(channel,Encoding.UTF8,false,4096,true);using var output=new StreamWriter(channel,new UTF8Encoding(false),4096,true){AutoFlush=true};
                try {
                    using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));string line=await input.ReadLineAsync(deadline.Token)??throw new IOException("Hotspot request missing");
                    if(line.Length>2048)throw new IOException("Hotspot request too large");var request=JsonSerializer.Deserialize<NativeMobileHotspot.Request>(line)??throw new IOException("Invalid hotspot request");line="";
                    var j=new Journal(args[1]);using var lease=j.Lock(TimeSpan.FromSeconds(55));var p=j.Read();
                    var adapter=NativeMobileHotspot.Start(j,p,request,new NativeBackend(j));await output.WriteLineAsync(JsonSerializer.Serialize(new{adapter}));
                }catch(Exception e){await output.WriteLineAsync(JsonSerializer.Serialize(new{error=e.Message}));return 1;}return 0;
            }
            if((args.Length==4&&args[0]=="share"&&args[3]=="--approve-windows-sharing")||(args.Length==3&&args[0]=="unshare"&&args[2]=="--approve-windows-sharing")){
                Admin();var j=new Journal(args[1]);using var lease=j.Lock(TimeSpan.FromSeconds(55));var p=j.Read();
                string reports=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","reports");Directory.CreateDirectory(reports);
                string error=Path.Combine(reports,"sharing-error-"+p.Id.ToString("N")+".txt");
                try{if(args[0]=="share")OwnedSharing.Start(j,p,Guid.Parse(args[2]),new NativeBackend(j));else OwnedSharing.Stop(j,p,new NativeBackend(j));File.Delete(error);}
                catch(Exception e){File.WriteAllText(error,e.Message+" (0x"+e.HResult.ToString("X8")+")");throw;}
                return 0;
            }
            if(args.SequenceEqual(new[]{"recover-stale","--approve-network-recovery"})) {
                Admin();RecoverStale();return 0;
            }
            if(args.SequenceEqual(new[]{"recovery-self-test","--approve-network-recovery"})) {Admin();await DurableRecovery.SelfTest();return 0;}
            if(args.Length==2&&args[0]=="emergency") {Admin();DurableRecovery.Run(new Journal(args[1]));return 0;}
            if(args.Length==2&&args[0]=="watch") {Admin();var j=new Journal(args[1]);return await Watchdog.Run(j,j.Read(),new NativeBackend(j));}
            if(args.Length==3&&args[0]=="recover"&&args[2]=="--approve-network-recovery") {Admin();var j=new Journal(args[1]);Watchdog.Recover(j,j.Read(),new NativeBackend(j));return 0;}
            if(args.Length!=4||args[0]!="connect"||args[2]!="--approve-global-route-change"||args[3]!="--approve-usb-test"||!Regex.IsMatch(args[1],"^TetherMax-Network-[a-f0-9]{32}$"))
                throw new ArgumentException("No changes. Usage: connect <private-pipe> --approve-global-route-change --approve-usb-test | recover <protected-session-directory> --approve-network-recovery");
            Admin();return await Connect(args[1]);
        }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
    }
    static void RecoverStale() => StaleSessions.Recover(
        Directory.EnumerateDirectories(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"TetherMax-Network-*"),
        folder=>new Journal(folder),ProcessIdentity.Alive,
        (journal,plan)=>Watchdog.Recover(journal,plan,new NativeBackend(journal)));
    sealed record Request(string Transport,string Token,int MaxSeconds,long MaxPayloadBytes,string Profile="auto",double UploadMbps=0,double DownloadMbps=0,bool Adaptive=false,string DnsProfile="adguard");
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
    static Task<int> Connect(string pipeName)=>Task.Run(()=>
    {
        using var single=new Mutex(false,"Global\\TetherMax-Network-Session-v1");
        bool acquired=false;try{acquired=single.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
        if(!acquired)throw new IOException("Another system-wide session owns the controller");
        try{return Connected(pipeName).GetAwaiter().GetResult();}finally{single.ReleaseMutex();}
    });
    static async Task<int> Connected(string pipeName)
    {
        using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        using var lifetime=new CancellationTokenSource();
        await pipe.ConnectAsync(15000,lifetime.Token);
        using var reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true);
        var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true,NewLine="\n"};
        using var inputTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        string line=await reader.ReadLineAsync(inputTimeout.Token)??throw new IOException("Private request missing");
        if(line.Length>4096)throw new IOException("Private request too large");
        var request=JsonSerializer.Deserialize<Request>(line)??throw new IOException("Invalid private request");line="";
        if(!Regex.IsMatch(request.Transport,"^127\\.0\\.0\\.1:[0-9]{1,5}$")||!Regex.IsMatch(request.Token,"^[a-f0-9]{32}$")||request.MaxSeconds<0||request.MaxSeconds>300||request.MaxPayloadBytes<0||request.MaxPayloadBytes>10*1024*1024||(request.MaxSeconds>0&&request.MaxPayloadBytes==0))
            throw new IOException("Invalid private transport or bounded test duration");
        if(!new[]{"auto","gaming","streaming","daily","maximum"}.Contains(request.Profile)||!double.IsFinite(request.UploadMbps)||!double.IsFinite(request.DownloadMbps)||request.UploadMbps<0||request.UploadMbps>10000||request.DownloadMbps<0||request.DownloadMbps>10000)throw new IOException("Invalid traffic profile");
        new LiveTrafficSettings(request.Profile,request.UploadMbps,request.DownloadMbps,request.Adaptive,request.DnsProfile).Validate();
        // The controller mutex is held; recover old intents before activation.
        try{RecoverStale();}
        catch(Exception e){try{await writer.WriteLineAsync(JsonSerializer.Serialize(new{type="network-error",message=e.Message}));}catch{}throw;}
        Guid id=Guid.NewGuid();var journal=Journal.Create(id);
        using(var controller=Process.GetCurrentProcess())journal.WriteNew("controller-process.json",JsonSerializer.Serialize(new{pid=controller.Id,start=controller.StartTime.ToUniversalTime().Ticks}));
        string engine=Path.Combine(AppContext.BaseDirectory,"tethermax-engine.exe"),dll=Path.Combine(AppContext.BaseDirectory,"wintun.dll");
        string bearer=request.Token;
        journal.WriteNew("android-before.json",await CellularStatus(engine,request.Transport,bearer));
        var start=new ProcessStartInfo(engine){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string arg in new[]{"ip-worker","--approve-adapter-change","--approve-usb-test","--transport",request.Transport,"--payload-budget-bytes",request.MaxPayloadBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),"--wintun-dll",dll,"--journal",journal.PathFor("adapter.jsonl")})start.ArgumentList.Add(arg);
        foreach(string arg in new[]{"--profile",request.Profile,"--upload-mbps",request.UploadMbps.ToString(System.Globalization.CultureInfo.InvariantCulture),"--download-mbps",request.DownloadMbps.ToString(System.Globalization.CultureInfo.InvariantCulture),"--traffic-telemetry","--dns-profile",request.DnsProfile})start.ArgumentList.Add(arg);
        if(request.Adaptive)start.ArgumentList.Add("--adaptive");
        using var worker=Process.Start(start)??throw new IOException("IP worker did not start");
        var stderr=worker.StandardError.ReadToEndAsync();
        worker.StandardInput.NewLine="\n";await worker.StandardInput.WriteLineAsync(request.Token);await worker.StandardInput.FlushAsync();
        int maxSeconds=request.MaxSeconds;request=request with{Token=""};
        // Heartbeats to the native worker and supervisor lease continue while
        // startup/configuration is in progress. They never carry the bearer.
        using var inputLock=new SemaphoreSlim(1);
        async Task WorkerInput(byte[] frame){await inputLock.WaitAsync(lifetime.Token);try{await worker.StandardInput.BaseStream.WriteAsync(frame,lifetime.Token);await worker.StandardInput.BaseStream.FlushAsync(lifetime.Token);}finally{inputLock.Release();}}
        Task pulse=Task.Run(async()=>{try{while(!lifetime.IsCancellationRequested){journal.Pulse();await WorkerInput(new byte[]{0});await Task.Delay(1000,lifetime.Token);}}catch{lifetime.Cancel();}});
        Plan? plan=null;Process? watcher=null;Task? telemetry=null,sharingMonitor=null;
        using var sharingStop=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        JsonElement? lastCounters=null; Task? connectivity=null; using var connectivityStop=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var outputLock=new SemaphoreSlim(1);
        async Task Send(object value){await outputLock.WaitAsync();try{await writer.WriteLineAsync(JsonSerializer.Serialize(value));}finally{outputLock.Release();}}
        try {
            using var readyTimeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);readyTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            string identity=await worker.StandardOutput.ReadLineAsync(readyTimeout.Token)??throw new IOException("IP worker readiness missing");
            using var json=JsonDocument.Parse(identity);var root=json.RootElement;
            Guid guid=Guid.Parse(root.GetProperty("guid").GetString()!);string name=root.GetProperty("adapter").GetString()!;
            await Send(new{type="worker-ready",adapter=name});
            using var self=Process.GetCurrentProcess();
            plan=new Plan(id,guid,name,Wfp.Luid(guid),Guid.NewGuid(),Guid.NewGuid(),self.Id,self.StartTime.ToUniversalTime().Ticks,worker.Id,worker.StartTime.ToUniversalTime().Ticks);
            var backend=new NativeBackend(journal);
            // The independent watcher is not a child job and is not killed when
            // the GUI/guardian/worker crashes. Durable WFP state also survives it.
            using(var mutation=journal.Lock())
            Transaction.Apply(plan,backend,()=>journal.Intent(plan),()=>{
                DurableRecovery.Arm(journal,plan);
                var watchStart=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
                watchStart.ArgumentList.Add("watch");watchStart.ArgumentList.Add(journal.Folder);
                watcher=Process.Start(watchStart)??throw new IOException("Recovery watcher failed to start");
                watcher.StandardInput.Close();
                _=watcher.StandardOutput.ReadToEndAsync();_=watcher.StandardError.ReadToEndAsync();
                var limit=Stopwatch.StartNew();
                while(!File.Exists(journal.PathFor("watcher-ready"))) {if(watcher.HasExited||limit.Elapsed>TimeSpan.FromSeconds(10))throw new IOException("No independent recovery acknowledgement");Thread.Sleep(50);}
                if(journal.ReadPublished("watcher-ready")!=watcher.Id.ToString())throw new IOException("Recovery acknowledgement mismatch");
            });
            journal.WriteNew("measurement.json",JsonSerializer.Serialize(new{startedAt=DateTimeOffset.UtcNow,request.MaxPayloadBytes,MaxSeconds=maxSeconds,adapter=name,guid,operatorAccounting="not-measured",standardAndroidTethering="not-observed"}));
            telemetry=Task.Run(async()=>{try{using var log=new StreamWriter(new FileStream(journal.PathFor("host-counters.jsonl"),FileMode.CreateNew,FileAccess.Write,FileShare.Read)){AutoFlush=true};while(await worker.StandardOutput.ReadLineAsync() is string data){using var item=JsonDocument.Parse(data);if(item.RootElement.TryGetProperty("type",out var sampleType)&&sampleType.GetString() is "ip-counters" or "ip-counters-final")lastCounters=item.RootElement.Clone();await log.WriteLineAsync(JsonSerializer.Serialize(new{observedAt=DateTimeOffset.UtcNow,payload=item.RootElement.Clone()}));try{await Send(new{type="ip-counters",payload=item.RootElement.Clone()});}catch{}}}catch{lifetime.Cancel();}});
            await Send(new{type="network-active",adapter=name,guid,session=journal.Folder,scope="system-wide IPv4; non-loopback IPv6 blocked; not carrier certification"});
            sharingMonitor=Task.Run(async()=>{try{while(!sharingStop.IsCancellationRequested){await Task.Delay(5000,sharingStop.Token);try{await Send(OwnedSharing.Observe(journal,plan));}catch(IOException e) when((e.HResult&0xffff)==32||(e.HResult&0xffff)==33){/* Sharing helper or recovery owns the lock. */}}}catch(OperationCanceledException){}catch(Exception e){try{await Send(new{type="sharing-error",message=e.Message});}catch{}lifetime.Cancel();}});
            connectivity=Task.Run(async()=>{try{string result=await ConnectivityChecks.Run(guid,connectivityStop.Token);connectivityStop.Token.ThrowIfCancellationRequested();journal.WriteNew("windows-connectivity.json",result);await Send(new{type="windows-connectivity",result=JsonSerializer.Deserialize<JsonElement>(result)});}catch(OperationCanceledException) when(connectivityStop.IsCancellationRequested){}catch(Exception e){try{journal.WriteNew("windows-connectivity-error.txt",e.Message);await Send(new{type="network-error",message="Tunnel connectivity verification failed"});}catch{}lifetime.Cancel();}});
            Task heartbeat=Task.Run(async()=>{try{while(!lifetime.IsCancellationRequested){using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(5));string? command=await reader.ReadLineAsync(timeout.Token);if(command=="heartbeat")continue;if(command=="stop")break;if(command==null||command.Length>4096)break;
                    using var change=JsonDocument.Parse(command);var value=change.RootElement;
                    if(value.GetProperty("type").GetString()!="traffic-settings")break;
                    var settings=value.GetProperty("settings").Deserialize<LiveTrafficSettings>()??throw new IOException("Settings missing");await WorkerInput(settings.Frame());}}catch{}finally{lifetime.Cancel();}});
            var uptime=Stopwatch.StartNew();
            while(!lifetime.IsCancellationRequested&&!worker.HasExited&&watcher is {HasExited:false}&&(maxSeconds==0||uptime.Elapsed<TimeSpan.FromSeconds(maxSeconds)))await Task.Delay(100,lifetime.Token);
        }catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
        catch(Exception e){try{journal.WriteNew("activation-error.txt",e.ToString());}catch{}try{await Send(new{type="network-error",message=e.Message});}catch{}}
        finally {
            sharingStop.Cancel();if(sharingMonitor!=null)await sharingMonitor;
            connectivityStop.Cancel();if(connectivity!=null)await connectivity;
            try{journal.WriteNew("android-after.json",await CellularStatus(engine,request.Transport,bearer));}catch(Exception e){try{journal.WriteNew("android-after-error.txt",e.Message);}catch{}}
            bearer="";
            // Keep worker heartbeat alive until routes/DNS are removed. If the
            // worker already died, recovery still removes NRPT and persistent WFP.
            bool recovered=false;
            if(plan!=null) {
                try {Watchdog.Recover(journal,plan,new NativeBackend(journal),()=>{try{worker.StandardInput.Close();}catch{}if(!worker.WaitForExit(7000))ProcessIdentity.Stop(plan.WorkerPid,plan.WorkerStart);});recovered=true;}
                catch(Exception e){try{await Send(new{type="recovery-pending",session=journal.Folder,message=e.Message});}catch{}}
            } else {try{if(!worker.HasExited){worker.Kill();await worker.WaitForExitAsync();}recovered=true;}catch{}}
            lifetime.Cancel();try{worker.StandardInput.Close();}catch{}
            await pulse; if(telemetry!=null)await telemetry;
            try {
                JsonElement? ReadJson(string name){if(!File.Exists(journal.PathFor(name)))return null;using var doc=JsonDocument.Parse(File.ReadAllText(journal.PathFor(name)));return doc.RootElement.Clone();}
                await Send(new{type="network-report",endedAt=DateTimeOffset.UtcNow,recovered=recovered||journal.Completed,session=journal.Folder,
                    measurement=ReadJson("measurement.json"),windowsConnectivity=ReadJson("windows-connectivity.json"),androidBefore=ReadJson("android-before.json"),androidAfter=ReadJson("android-after.json"),hostLastSample=lastCounters,
                    hostFinalSample=lastCounters?.GetProperty("type").GetString()=="ip-counters-final",operatorAccounting="not-measured",androidStandardTetheringOff="not-observed"});
            }catch{}
            try{await Send(new{type="network-stopped",recovered,session=journal.Folder});}catch{}
            // Never kill the watcher on failed recovery.
            watcher?.Dispose();
            try{writer.Dispose();}catch{}
        }
        return journal.Completed?0:1;
    }
}
