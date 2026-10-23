using System.Diagnostics;

namespace TetherMax.Network;

public static class Watchdog
{
    public static void Recover(Journal journal,Plan p,IBackend backend,Action? stop=null)
    {
        using var lease=journal.Lock();
        if(journal.Completed)return;
        Transaction.Rollback(p,backend,stop??(()=>ProcessIdentity.Stop(p.WorkerPid,p.WorkerStart)),journal.Complete);
    }
    public static async Task<int> Run(Journal journal,Plan p,IBackend backend,TimeSpan? stall=null,Action? stop=null)
    {
        p.Validate();
        using(var self=Process.GetCurrentProcess())journal.WriteNew("watcher-process.json",System.Text.Json.JsonSerializer.Serialize(new{pid=self.Id,start=self.StartTime.ToUniversalTime().Ticks}));
        journal.WriteNew("watcher-ready",Environment.ProcessId.ToString());
        DateTime lastWrite=File.GetLastWriteTimeUtc(journal.PathFor("heartbeat"));
        var age=Stopwatch.StartNew();
        while(!journal.Completed)
        {
            DateTime next=File.GetLastWriteTimeUtc(journal.PathFor("heartbeat"));
            if(next!=lastWrite){lastWrite=next;age.Restart();}
            bool failed=!ProcessIdentity.Alive(p.GuardianPid,p.GuardianStart)||!ProcessIdentity.Alive(p.WorkerPid,p.WorkerStart)||age.Elapsed>(stall??TimeSpan.FromSeconds(8));
            if(failed)
            {
                // Retry transient CIM/ownership lock failures. Filters remain
                // persistent until every rollback step has succeeded.
                try{Recover(journal,p,backend,stop);return 0;}
                catch(Exception e){File.AppendAllText(journal.PathFor("recovery-errors.log"),DateTimeOffset.UtcNow+" "+e.Message+Environment.NewLine);}
            }
            await Task.Delay(200);
        }
        return 0;
    }
}
