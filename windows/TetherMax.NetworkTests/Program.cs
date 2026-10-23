using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using TetherMax.Network;

class Program
{
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static Plan PlanForSelf()
    {
        using var self=Process.GetCurrentProcess();long start=self.StartTime.ToUniversalTime().Ticks;
        return new Plan(Guid.NewGuid(),Guid.NewGuid(),"TetherMax-IP-aabbccdd",123,Guid.NewGuid(),Guid.NewGuid(),self.Id,start,self.Id,start);
    }
    sealed class MemoryBackend:IBackend
    {
        public List<string> Events=new(); public string? Fail;public bool Guarded,Configured;
        public void Guard(Plan p){Events.Add("guard");Guarded=true;if(Fail=="guard")throw new IOException("lost guard acknowledgement");}
        public void Configure(Plan p){Events.Add("configure");Configured=true;if(Fail=="configure")throw new IOException("partial configuration");}
        public void RemoveConfiguration(Plan p){Events.Add("remove-config");if(Fail=="remove-config")throw new IOException("ownership changed");Configured=false;}
        public void RemoveGuard(Plan p){Events.Add("remove-guard");if(Fail=="remove-guard")throw new IOException("BFE busy");Guarded=false;}
    }
    sealed class FileBackend(string folder):IBackend
    {
        public void Guard(Plan p)=>File.WriteAllText(Path.Combine(folder,"owned-guard"),p.Id.ToString());
        public void Configure(Plan p)=>File.WriteAllText(Path.Combine(folder,"owned-config"),p.Id.ToString());
        public void RemoveConfiguration(Plan p){if(File.Exists(Path.Combine(folder,"fail-recovery")))throw new IOException("injected recovery failure");string file=Path.Combine(folder,"os-process.json");if(File.Exists(file)){using var process=JsonDocument.Parse(File.ReadAllText(file));if(ProcessIdentity.Alive(process.RootElement.GetProperty("pid").GetInt32(),process.RootElement.GetProperty("start").GetInt64()))throw new IOException("OS executor must stop before rollback");}File.Delete(Path.Combine(folder,"owned-config"));}
        public void RemoveGuard(Plan p)=>File.Delete(Path.Combine(folder,"owned-guard"));
    }
    static Process Child(params string[] args)
    {
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in args)start.ArgumentList.Add(arg);return Process.Start(start)??throw new IOException("test child failed");
    }
    static async Task Until(Func<bool> predicate,string failure,int ms=10000)
    {var timer=Stopwatch.StartNew();while(!predicate()){if(timer.ElapsedMilliseconds>ms)throw new Exception(failure);await Task.Delay(50);}}
    static async Task FixtureOwner(string folder,string mode)
    {
        var j=new Journal(folder,false);var p=PlanForSelf();j.Pulse();Process? watcher=null;var backend=new FileBackend(folder);
        using(var gate=j.Lock())Transaction.Apply(p,backend,()=>j.Intent(p),()=>{
            watcher=Child("--fixture-watch",folder);var ready=Stopwatch.StartNew();
            while(!File.Exists(j.PathFor("watcher-ready"))){if(ready.ElapsedMilliseconds>5000)throw new Exception("watcher did not acknowledge");Thread.Sleep(20);}
        });
        if(mode=="os-executor") {
            using var executor=Child("--fixture-os-worker");
            j.WriteNew("os-process.json",JsonSerializer.Serialize(new{pid=executor.Id,start=executor.StartTime.ToUniversalTime().Ticks}));
        }
        Console.WriteLine("READY");Console.Out.Flush();
        while(!j.Completed) {
            if(mode!="stall")j.Pulse();
            if(watcher!.HasExited)Watchdog.Recover(j,p,backend,()=>{});
            await Task.Delay(100);
        }
        watcher?.Dispose();
    }
    static async Task FixtureProcessTest(string mode)
    {
        string folder=Path.Combine(Path.GetTempPath(),"TetherMax-offline-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"outside-sentinel"),"preserve");
        using var owner=Child("--fixture-owner",folder,mode);
        try {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string? ready=await owner.StandardOutput.ReadLineAsync(timeout.Token);
            if(ready!="READY")throw new Exception("fixture owner did not start: "+await owner.StandardError.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(2)));
            var j=new Journal(folder,false);var p=j.Read();
            Require(File.Exists(j.PathFor("owned-guard"))&&File.Exists(j.PathFor("owned-config")),"fixture configuration missing");
            if(mode=="watcher-crash") {int pid=int.Parse(File.ReadAllText(j.PathFor("watcher-ready")));using var watcher=Process.GetProcessById(pid);watcher.Kill();}
            else if(mode!="stall") {
                if(mode=="retry")File.WriteAllText(j.PathFor("fail-recovery"),"injected");
                owner.Kill();await owner.WaitForExitAsync();
            }
            if(mode=="retry") {
                await Until(()=>File.Exists(j.PathFor("recovery-errors.log")),"recovery failure not recorded");
                Require(File.Exists(j.PathFor("owned-guard"))&&!j.Completed,"fail-closed guard was lost on rollback failure");
                File.Delete(j.PathFor("fail-recovery"));
            }
            await Until(()=>j.Completed,"independent fixture recovery failed");
            Require(!File.Exists(j.PathFor("owned-guard"))&&!File.Exists(j.PathFor("owned-config")),"fixture resource leak");
            Require(File.ReadAllText(j.PathFor("outside-sentinel"))=="preserve","unowned state modified");
            Console.WriteLine("PASS independent process "+mode);
        }finally {
            if(!owner.HasExited){owner.Kill();await owner.WaitForExitAsync();}
            // Leave unique test evidence in temp; never recursively delete a computed path.
        }
    }
    static async Task<int> Main(string[] args)
    {
        try {
            ProcessLaunch.DetachStandardHandleInheritance();
            if(args.SequenceEqual(new[]{"--fixture-os-worker"})){await Task.Delay(TimeSpan.FromMinutes(2));return 0;}
            if(args.Length==2&&args[0]=="--fixture-watch"){var j=new Journal(args[1],false);return await Watchdog.Run(j,j.Read(),new FileBackend(args[1]),TimeSpan.FromSeconds(1),()=>{});}
            if(args.Length==3&&args[0]=="--fixture-owner"){await FixtureOwner(args[1],args[2]);return 0;}
            if(args.SequenceEqual(new[]{"--native-isolated-runner"}))return await NativeTests.Run().WaitAsync(TimeSpan.FromMinutes(4));
            if(args.SequenceEqual(new[]{"--native-rescue"}))return await NativeTests.Rescue();
            if(args.Length==1&&args[0]=="--native-owner")return await NativeTests.Owner();
            if(args.Length!=0)throw new Exception("Unknown test mode");
            Require(Marshal.SizeOf<Wfp.Filter>()==200&&Marshal.SizeOf<Wfp.Condition>()==40&&Marshal.SizeOf<Wfp.Sublayer>()==72,"x64 WFP ABI mismatch");
            Console.WriteLine("PASS WFP x64 ABI sizes and offsets");
            var p=PlanForSelf();
            foreach(string fail in new[]{"none","persist","arm","guard","configure"}) {
                var b=new MemoryBackend{Fail=fail};bool persisted=false,armed=false,complete=false;
                try{Transaction.Apply(p,b,()=>{if(fail=="persist")throw new IOException();persisted=true;},()=>{if(fail=="arm")throw new IOException();armed=true;});}catch(IOException){}
                Require(!b.Guarded||persisted&&armed,"mutation before durable independent recovery");
                Transaction.Rollback(p,b,()=>b.Events.Add("stop-worker"),()=>complete=true);
                Require(complete&&!b.Guarded&&!b.Configured,"partial apply rollback failed");
                Require(b.Events.IndexOf("remove-config")<b.Events.IndexOf("stop-worker")&&b.Events.IndexOf("stop-worker")<b.Events.IndexOf("remove-guard"),"unsafe recovery order");
            }
            foreach(string fail in new[]{"remove-config","remove-guard"}) {
                var b=new MemoryBackend{Fail=fail,Guarded=true,Configured=true};bool complete=false;
                try{Transaction.Rollback(p,b,()=>{},()=>complete=true);}catch(IOException){}
                Require(!complete&&b.Guarded,"recovery failure opened fallback");
            }
            string script=NativeBackend.ConfigureScript(p),rollback=NativeBackend.RemoveScript(p);
            string selection=NativeBackend.PowerShell("function Get-NetAdapter {param([switch]$IncludeHidden) [pscustomobject]@{InterfaceGuid='{''GUID''}';Name='TetherMax-IP-aabbccdd'}}; ".Replace("{''GUID''}","{"+p.AdapterGuid+"}")+NativeBackend.Adapter(p)+"if($a.Count -ne 1){throw 'Brace-wrapped string GUID selection regression'}");
            Require(!script.Contains("Remove-NetRoute")&&!script.Contains("Disable-NetAdapter")&&!script.Contains("Set-NetFirewallProfile"),"unscoped network mutation");
            Require(script.Contains("-PolicyStore ActiveStore")&&script.Contains("128.0.0.0/1")&&rollback.Contains("Adapter identity changed"),"scoped ownership missing");
            Console.WriteLine("PASS transaction faults, recovery order, exact adapter ownership");
            MockCmdlets.Run(p);
            string metadataFolder=Path.Combine(Path.GetTempPath(),"TetherMax-record-"+Guid.NewGuid().ToString("N")+"-ö'path");Directory.CreateDirectory(metadataFolder);
            var metadataJournal=new Journal(metadataFolder,false);
            NativeBackend.PowerShell(NativeBackend.ExecutorRecordScript(metadataJournal));
            NativeBackend.PowerShell(NativeBackend.ExecutorRecordScript(metadataJournal));
            using(var metadata=JsonDocument.Parse(File.ReadAllText(metadataJournal.PathFor("os-process.json"))))
                Require(metadata.RootElement.GetProperty("pid").GetInt32()>0&&metadata.RootElement.GetProperty("start").GetInt64()>0&&!File.Exists(metadataJournal.PathFor("os-process.tmp")),"executor metadata publication failed");
            Console.WriteLine("PASS actual Windows PowerShell atomic metadata replacement in quoted Unicode path; no network commands");
            string completePayload=new('r',1024*1024);
            Task publish=Task.Run(()=>metadataJournal.WriteNew("atomic-ready",completePayload));
            while(!File.Exists(metadataJournal.PathFor("atomic-ready")))await Task.Yield();
            Require(metadataJournal.ReadPublished("atomic-ready")==completePayload,"readiness visible before complete publication");await publish;
            Console.WriteLine("PASS journal/readiness becomes visible only after complete atomic publication");
            var sharingLock=new FileStream(metadataJournal.PathFor("atomic-ready"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
            var pendingRead=Task.Run(()=>metadataJournal.ReadPublished("atomic-ready"));
            await Task.Delay(100);Require(!pendingRead.IsCompleted,"sharing lock was not respected");sharingLock.Dispose();
            Require(await pendingRead==completePayload,"sharing violation retry corrupted readiness data");
            Console.WriteLine("PASS Windows sharing lock publication retry");
            var recoveryBackend=new MemoryBackend{Guarded=true,Configured=true};
            var recoveryOwner=metadataJournal.Lock();
            var concurrentRecovery=Task.Run(()=>Watchdog.Recover(metadataJournal,p,recoveryBackend,()=>{}));
            await Task.Delay(100);Require(!concurrentRecovery.IsCompleted,"concurrent recovery must wait for active owner");recoveryOwner.Dispose();
            await concurrentRecovery.WaitAsync(TimeSpan.FromSeconds(5));
            Require(metadataJournal.Completed&&!recoveryBackend.Guarded&&!recoveryBackend.Configured,"concurrent recovery did not finish safely");
            Watchdog.Recover(metadataJournal,p,recoveryBackend,()=>throw new Exception("completed recovery repeated"));
            Console.WriteLine("PASS concurrent recovery waits for ownership and completed recovery is idempotent");
            foreach(string mode in new[]{"owner-crash","stall","watcher-crash","retry","os-executor"})await FixtureProcessTest(mode);
            Console.WriteLine("All offline network checks passed; no OS networking or physical USB used.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
