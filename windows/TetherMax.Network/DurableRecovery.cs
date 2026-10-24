using System.Diagnostics;

namespace TetherMax.Network;

// A protected executable and SYSTEM task outlive both interactive recovery actors.
public static class DurableRecovery
{
    static string Quote(string value)=>"'"+value.Replace("'","''")+"'";
    public static void Arm(Journal journal,Plan plan)
    {
        string bin=Path.Combine(journal.Folder,"recovery-bin");Directory.CreateDirectory(bin);
        foreach(string source in Directory.EnumerateFiles(AppContext.BaseDirectory))
            File.Copy(source,Path.Combine(bin,Path.GetFileName(source)),false);
        string helper=Path.Combine(bin,"TetherMax.Network.exe");
        string arguments="emergency \""+journal.Folder+"\"";
        NativeBackend.PowerShell("$action=New-ScheduledTaskAction -Execute "+Quote(helper)+" -Argument "+Quote(arguments)+";"+
            "$boot=New-ScheduledTaskTrigger -AtStartup;$repeat=New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1);"+
            "$principal=New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest;"+
            "$settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 3) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries;"+
            "Register-ScheduledTask -TaskName "+Quote(plan.Tag)+" -Action $action -Trigger @($boot,$repeat) -Principal $principal -Settings $settings | Out-Null;"+
            "Start-ScheduledTask -TaskName "+Quote(plan.Tag));
        var deadline=Stopwatch.StartNew();
        while(!File.Exists(journal.PathFor("scheduler-ready"))) {
            if(deadline.Elapsed>TimeSpan.FromSeconds(20))throw new IOException("Independent SYSTEM recovery task did not acknowledge; network activation refused");
            Thread.Sleep(100);
        }
    }
        public static async Task SelfTest()
    {
        // No adapter or guard is installed. Exercise the real SYSTEM scheduler,
        // ownership-scoped native cleanup and durable completion on an absent session.
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe")){UseShellExecute=false,CreateNoWindow=true};
        start.ArgumentList.Add("/c");start.ArgumentList.Add("ping -n 2 127.0.0.1 >nul");
        using var child=Process.Start(start)??throw new IOException("Recovery probe child failed");
        int pid=child.Id;long ticks=child.StartTime.ToUniversalTime().Ticks;await child.WaitForExitAsync();
        var id=Guid.NewGuid();var journal=Journal.Create(id);
        var plan=new Plan(id,Guid.NewGuid(),"TetherMax-IP-"+id.ToString("N")[..8],123,Guid.NewGuid(),Guid.NewGuid(),pid,ticks,pid,ticks);
        journal.Intent(plan);journal.Pulse();Arm(journal,plan);
        var timer=Stopwatch.StartNew();while(!journal.Completed){if(timer.Elapsed>TimeSpan.FromSeconds(55))throw new IOException("Real SYSTEM recovery preflight failed: "+journal.Folder);await Task.Delay(100);}
        using var wfp=new Wfp();if(wfp.Exists(plan.V4Filter)||wfp.Exists(plan.V6Filter))throw new IOException("Recovery preflight filters remain");
        Console.WriteLine("PASS independent SYSTEM scheduled recovery and native absent-session rollback: "+journal.Folder);
    }
    public static void Run(Journal journal)
    {
        var plan=journal.Read();
        if(!File.Exists(journal.PathFor("scheduler-ready")))journal.WriteNew("scheduler-ready","SYSTEM recovery executable started");
        if(!journal.Completed) {
            bool alive=ProcessIdentity.Alive(plan.GuardianPid,plan.GuardianStart);
            bool recent=DateTime.UtcNow-File.GetLastWriteTimeUtc(journal.PathFor("heartbeat"))<TimeSpan.FromSeconds(15);
            if(alive&&recent)return;
            Watchdog.Recover(journal,plan,new NativeBackend(journal));
        }
        NativeBackend.PowerShell("Unregister-ScheduledTask -TaskName "+Quote(plan.Tag)+" -Confirm:$false -ErrorAction SilentlyContinue");
    }
}
