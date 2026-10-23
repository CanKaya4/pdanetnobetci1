using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace TetherMax;

// GUI owns only IPC/consent. The elevated guardian and independent watcher own
// native mutation/recovery, so closing or killing this WPF process triggers it.
internal sealed class SystemNetwork: IAsyncDisposable
{
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe,out uint pid);
    readonly NamedPipeServerStream pipe;
    readonly CancellationTokenSource stop=new();
    Process? guardian;
    Task? heartbeat,read;
    public bool Active {get;private set;}
    public SystemNetwork()
    {
        string name="TetherMax-Network-"+Guid.NewGuid().ToString("N");
        Name=name;
        var acl=new PipeSecurity();acl.SetAccessRuleProtection(true,false);
        using var identity=WindowsIdentity.GetCurrent();
        acl.AddAccessRule(new PipeAccessRule(identity.User!,PipeAccessRights.FullControl,AccessControlType.Allow));
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),PipeAccessRights.FullControl,AccessControlType.Allow));
        pipe=NamedPipeServerStreamAcl.Create(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,acl);
    }
    string Name {get;}
    public async Task Start(string folder,string transport,string token,Action<string> report,int maxSeconds=120)
    {
        string helper=Path.Combine(folder,"TetherMax.Network.exe");
        if(!File.Exists(helper)||!File.Exists(Path.Combine(folder,"wintun.dll")))throw new IOException("Native ağ yardımcısı veya doğrulanmış Wintun pakette yok.");
        var start=new ProcessStartInfo(helper){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
        foreach(string arg in new[]{"connect",Name,"--approve-global-route-change","--approve-usb-test"})start.ArgumentList.Add(arg);
        guardian=Process.Start(start)??throw new IOException("Yönetici ağ süreci başlatılamadı.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(45));
        await pipe.WaitForConnectionAsync(timeout.Token);
        if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out uint pid)||pid!=guardian.Id)throw new IOException("Ağ yardımcısının IPC kimliği eşleşmiyor.");
        var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true,NewLine="\n"};
        await writer.WriteLineAsync(JsonSerializer.Serialize(new{Transport=transport,Token=token,MaxSeconds=maxSeconds}));token="";
        heartbeat=Task.Run(async()=>{try{while(!stop.IsCancellationRequested){await writer.WriteLineAsync("heartbeat");await Task.Delay(1000,stop.Token);}}catch{}finally{writer.Dispose();}});
        read=Task.Run(async()=>{try{using var reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true);while(await reader.ReadLineAsync(stop.Token) is string line){using var doc=JsonDocument.Parse(line);var item=doc.RootElement;string type=item.GetProperty("type").GetString()??"";if(type=="network-active")Active=true;if(type=="network-stopped")Active=false;report(line);}}catch(Exception e){if(!stop.IsCancellationRequested)report("Ağ yardımcısı bağlantısı kapandı: "+e.Message);}finally{Active=false;}});
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();pipe.Dispose();
        if(heartbeat!=null)await heartbeat;if(read!=null)await read;
        // Closing IPC asks the guardian to recover; never forcibly terminate it.
        if(guardian!=null){try{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(50));await guardian.WaitForExitAsync(deadline.Token);}catch{}guardian.Dispose();}
        stop.Dispose();
    }
}
