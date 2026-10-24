using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace TetherMax.Network;

public sealed record Plan(Guid Id, Guid AdapterGuid, string AdapterName, ulong Luid,
    Guid V4Filter, Guid V6Filter, int GuardianPid, long GuardianStart, int WorkerPid, long WorkerStart)
{
    public string Tag => "TetherMax-"+Id.ToString("N");
    public void Validate()
    {
        if(Id==Guid.Empty||AdapterGuid==Guid.Empty||V4Filter==Guid.Empty||V6Filter==Guid.Empty||Luid==0||
           new[]{Id,V4Filter,V6Filter}.Distinct().Count()!=3||GuardianPid<=0||WorkerPid<=0||GuardianStart<=0||WorkerStart<=0||
           !System.Text.RegularExpressions.Regex.IsMatch(AdapterName,"^TetherMax-IP-[a-f0-9]{8}$"))
            throw new InvalidDataException("Invalid owned network session");
    }
}

public interface IBackend
{
    void Guard(Plan p);
    void Configure(Plan p);
    void RemoveConfiguration(Plan p);
    void RemoveGuard(Plan p);
}

// This order is shared by production and fault-injection tests. The durable
// intent and independent watcher acknowledgement precede every OS mutation.
public static class Transaction
{
    public static void Apply(Plan p,IBackend backend,Action persist,Action arm)
    {
        p.Validate();persist();arm();backend.Guard(p);backend.Configure(p);
    }
    public static void Rollback(Plan p,IBackend backend,Action stopWorker,Action complete)
    {
        p.Validate();backend.RemoveConfiguration(p);stopWorker();backend.RemoveGuard(p);complete();
    }
}

public sealed class Journal
{
    public string Folder {get;}
    public Journal(string folder,bool checkSecurity=true)
    {
        Folder=Path.GetFullPath(folder);
        if(checkSecurity) CheckSecurity(Folder);
    }
    public static Journal Create(Guid id)
    {
        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"TetherMax-Network-"+id.ToString("N"));
        if(Directory.Exists(folder))throw new IOException("Session directory already exists");
        var security=new DirectorySecurity();security.SetAccessRuleProtection(true,false);
        foreach(var sid in new[]{WellKnownSidType.BuiltinAdministratorsSid,WellKnownSidType.LocalSystemSid})
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        new DirectoryInfo(folder).Create(security);
        return new Journal(folder);
    }
    static void CheckSecurity(string folder)
    {
        var info=new DirectoryInfo(folder);
        if(!info.Exists||(info.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Missing or redirected recovery directory");
        var security=info.GetAccessControl();
        if(!security.AreAccessRulesProtected)throw new IOException("Recovery ACL must be protected");
        var admins=new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null);
        var system=new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null);
        foreach(FileSystemAccessRule rule in security.GetAccessRules(true,true,typeof(SecurityIdentifier)))
            if(rule.AccessControlType==AccessControlType.Allow && !rule.IdentityReference.Equals(admins)&&!rule.IdentityReference.Equals(system))
                throw new IOException("Untrusted recovery directory ACL");
    }
    public string PathFor(string name)=>Path.Combine(Folder,name);
    public void WriteNew(string name,string data)
    {
        string staged=PathFor(name+"."+Guid.NewGuid().ToString("N")+".tmp");
        using(var file=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {byte[] bytes=System.Text.Encoding.UTF8.GetBytes(data);file.Write(bytes);file.Flush(true);}
        // Publish only after the writer has flushed AND closed. File.Exists on
        // an open destination was not a durable/readable readiness handshake.
        File.Move(staged,PathFor(name));
    }
        // Rename publication can briefly retain a Windows sharing lock. Retry only
    // sharing/lock violations; malformed state and access denial still fail.
    public string ReadPublished(string name)
    {
        for(int attempt=0;;attempt++) {
            try{return File.ReadAllText(PathFor(name));}
            catch(IOException e) when(attempt<40 && ((e.HResult&0xffff)==32||(e.HResult&0xffff)==33)) {Thread.Sleep(25);}
        }
    }
    public Plan Read()
    {
        string path=PathFor("intent.json");
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Redirected recovery intent");
        var p=JsonSerializer.Deserialize<Plan>(ReadPublished("intent.json"))??throw new IOException("Missing recovery plan");p.Validate();return p;
    }
    public void Intent(Plan p)=>WriteNew("intent.json",JsonSerializer.Serialize(p));
    public void Complete(){if(!File.Exists(PathFor("complete")))WriteNew("complete","recovered");}
    public bool Completed=>File.Exists(PathFor("complete"));
    public void Pulse()
    {
        using var file=new FileStream(PathFor("heartbeat"),FileMode.OpenOrCreate,FileAccess.Write,FileShare.ReadWrite);
        file.WriteByte(1);file.Flush(true);
    }
    public FileStream Lock(TimeSpan? wait=null)
    {
        var timer=Stopwatch.StartNew();
        for(;;) {
            try{return new FileStream(PathFor("recovery.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException e) when(wait.HasValue && timer.Elapsed<wait.Value && ((e.HResult&0xffff)==32||(e.HResult&0xffff)==33)) {Thread.Sleep(50);}
        }
    }
}

public static class ProcessIdentity
{
    public static bool Alive(int pid,long ticks)
    {try{using var p=Process.GetProcessById(pid);return !p.HasExited&&p.StartTime.ToUniversalTime().Ticks==ticks;}catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}catch(System.ComponentModel.Win32Exception){return false;}}
    public static void Stop(int pid,long ticks)
    {
        if(!Alive(pid,ticks))return;
        using var process=Process.GetProcessById(pid);
        if(process.StartTime.ToUniversalTime().Ticks!=ticks)throw new IOException("Worker PID was reused");
        process.Kill();if(!process.WaitForExit(10000))throw new IOException("Worker did not exit");
    }
}
