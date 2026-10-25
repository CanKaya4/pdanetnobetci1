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
    StreamWriter? commands;readonly SemaphoreSlim commandLock=new(1,1);
    async Task Command(string line){using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);deadline.CancelAfter(TimeSpan.FromSeconds(3));await commandLock.WaitAsync(deadline.Token);try{if(commands==null)throw new IOException("Ağ yardımcısı hazır değil.");await commands.WriteLineAsync(line.AsMemory(),deadline.Token);}finally{commandLock.Release();}}
    public Task UpdateTraffic(TrafficSettings settings)=>Active?Command(JsonSerializer.Serialize(new{type="traffic-settings",settings})):throw new IOException("Önce USB bağlantısını kurun.");
    Task? heartbeat,read;
    readonly object disposeGate=new();Task? disposal;
    Guid adapterGuid;
    volatile bool receivedStop;
    bool active; public bool Active {get=>Volatile.Read(ref active);private set=>Volatile.Write(ref active,value);}
    public string? SessionFolder {get;private set;}
    public sealed record HotspotClient([property:System.Text.Json.Serialization.JsonPropertyName("mac")]string Mac,[property:System.Text.Json.Serialization.JsonPropertyName("names")]string[] Names);
    public HotspotClient[] SharingClients {get;private set;}=Array.Empty<HotspotClient>();
    public string? SharingClientsError {get;private set;}
    public string SharingStatus {get;private set;}="Windows paylaşımı etkin değil.";
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
    public async Task Start(string folder,string transport,string token,Action<string> report,int maxSeconds=120,TrafficSettings? settings=null,CancellationToken cancellation=default)
    {
        string helper=Path.Combine(folder,"TetherMax.Network.exe");
        if(!File.Exists(helper)||!File.Exists(Path.Combine(folder,"wintun.dll")))throw new IOException("Native ağ yardımcısı veya doğrulanmış Wintun pakette yok.");
        var start=new ProcessStartInfo(helper){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
        foreach(string arg in new[]{"connect",Name,"--approve-global-route-change","--approve-usb-test"})start.ArgumentList.Add(arg);
        guardian=Process.Start(start)??throw new IOException("Yönetici ağ süreci başlatılamadı.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token,cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(45));
        await SessionLifecycle.WaitForHelper(pipe.WaitForConnectionAsync(timeout.Token),guardian,timeout.Token);
        if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out uint pid)||pid!=guardian.Id)throw new IOException("Ağ yardımcısının IPC kimliği eşleşmiyor.");
        var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true,NewLine="\n"};
        await writer.WriteLineAsync(JsonSerializer.Serialize(new{Transport=transport,Token=token,MaxSeconds=maxSeconds,MaxPayloadBytes=maxSeconds==0?0L:1048576L,Profile=settings?.Mode??"auto",UploadMbps=settings?.UploadMbps??0,DownloadMbps=settings?.DownloadMbps??0,Adaptive=settings?.Adaptive??false,DnsProfile=settings?.DnsProfile??"adguard"}));token="";
        commands=writer;
        heartbeat=Task.Run(async()=>{try{while(!stop.IsCancellationRequested){await Command("heartbeat");await Task.Delay(1000,stop.Token);}}catch(OperationCanceledException)when(stop.IsCancellationRequested){}catch(Exception error){Diagnostics.Record("ipc-heartbeat",error);pipe.Dispose();}});
        read=Task.Run(()=>ReadReports(report));
    }
    async Task ReadReports(Action<string> report) {
        try {
            using var reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true);
            while(await reader.ReadLineAsync(stop.Token) is string line) {
                if(!NetworkReport.TryParse(line,out var document))continue;
                using(document)try {
                    var item=document!.RootElement;string type=item.GetProperty("type").GetString()!;
                    if(type=="network-active"){Active=false;adapterGuid=item.GetProperty("guid").GetGuid();SessionFolder=item.GetProperty("session").GetString();}
                    if(type=="windows-connectivity")Active=TunnelHealth.Healthy(item.GetProperty("result"),adapterGuid);
                    if(type=="sharing-state") {
                        SharingClients=item.TryGetProperty("clients",out var clients)?clients.Deserialize<HotspotClient[]>()??Array.Empty<HotspotClient>():Array.Empty<HotspotClient>();
                        SharingClientsError=item.TryGetProperty("clientsError",out var error)?error.GetString():null;
                        SharingStatus=item.GetProperty("configured").GetBoolean()?"Windows ICS kaynağı: TetherMax · TV testi gerekli":"Windows paylaşımı etkin değil.";
                    }
                    if(type=="sharing-error")SharingStatus="Paylaşım güvenlik hatası: "+item.GetProperty("message").GetString();
                    if(type=="network-stopped"){receivedStop=true;Active=false;SharingStatus="TetherMax bağlantısı kapalı; kurtarma sonucunu denetleyin.";}
                    report(line);
                }catch(Exception error)when(error is JsonException or InvalidOperationException or FormatException or KeyNotFoundException){Diagnostics.Record("ipc-sample",error);}
            }
        }catch(Exception error){if(!stop.IsCancellationRequested)Diagnostics.Record("ipc-read",error);}
        finally{Active=false;if(!stop.IsCancellationRequested&&!receivedStop)try{report(JsonSerializer.Serialize(new{type="network-channel-closed"}));}catch(Exception error){Diagnostics.Record("ipc-close-report",error);}}
    }
    public ValueTask DisposeAsync() {lock(disposeGate){if(disposal==null||disposal.IsFaulted)disposal=DisposeCore();return new ValueTask(disposal);}}
    async Task DisposeCore()
    {
        Active=false;
        try {
            // Keep the report reader alive until rollback completes.
            // Closing IPC remains the fallback for a failed graceful stop.
            if(commands!=null) {
                try{await Command("stop").WaitAsync(TimeSpan.FromSeconds(3));}
                catch{pipe.Dispose();}
            } else pipe.Dispose();
            if(guardian!=null) {
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(120));
                try{await guardian.WaitForExitAsync(deadline.Token);}
                catch(OperationCanceledException e){throw new IOException("Ağ kurtarması henüz tamamlanmadı; yeni bağlantı güvenli biçimde engellendi.",e);}
            }
        } finally {
            stop.Cancel();pipe.Dispose();
            if(heartbeat!=null)await heartbeat;if(read!=null)await read;
        }
        try{commands?.Dispose();}catch(Exception error)when(error is IOException or ObjectDisposedException){Diagnostics.Record("ipc-writer-close",error);}
        guardian?.Dispose();stop.Dispose();
    }
}
