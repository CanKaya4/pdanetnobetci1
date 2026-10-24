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
    async Task Command(string line){await commandLock.WaitAsync(stop.Token);try{if(commands==null)throw new IOException("Ağ yardımcısı hazır değil.");await commands.WriteLineAsync(line);}finally{commandLock.Release();}}
    public Task UpdateTraffic(TrafficSettings settings)=>Active?Command(JsonSerializer.Serialize(new{type="traffic-settings",settings})):throw new IOException("Önce USB bağlantısını kurun.");
    Task? heartbeat,read;
    readonly object disposeGate=new();Task? disposal;
    Guid adapterGuid;
    public bool Active {get;private set;}
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
        await pipe.WaitForConnectionAsync(timeout.Token);
        if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out uint pid)||pid!=guardian.Id)throw new IOException("Ağ yardımcısının IPC kimliği eşleşmiyor.");
        var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true,NewLine="\n"};
        await writer.WriteLineAsync(JsonSerializer.Serialize(new{Transport=transport,Token=token,MaxSeconds=maxSeconds,MaxPayloadBytes=maxSeconds==0?0L:1048576L,Profile=settings?.Mode??"auto",UploadMbps=settings?.UploadMbps??0,DownloadMbps=settings?.DownloadMbps??0,Adaptive=settings?.Adaptive??false,DnsProfile=settings?.DnsProfile??"adguard"}));token="";
        commands=writer;
        heartbeat=Task.Run(async()=>{try{while(!stop.IsCancellationRequested){await Command("heartbeat");await Task.Delay(1000,stop.Token);}}catch{}finally{try{writer.Dispose();}catch{}}});
        read=Task.Run(async()=>{try{using var reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true);while(await reader.ReadLineAsync(stop.Token) is string line){using var doc=JsonDocument.Parse(line);var item=doc.RootElement;string type=item.GetProperty("type").GetString()??"";if(type=="network-active"){Active=false;adapterGuid=item.GetProperty("guid").GetGuid();SessionFolder=item.GetProperty("session").GetString();}if(type=="windows-connectivity"){var result=item.GetProperty("result");Active=Readiness.Healthy(result)&&result.GetProperty("adapterGuid").GetGuid()==adapterGuid;}if(type=="sharing-state"){SharingClients=item.TryGetProperty("clients",out var clients)?clients.Deserialize<HotspotClient[]>()??Array.Empty<HotspotClient>():Array.Empty<HotspotClient>();SharingClientsError=item.TryGetProperty("clientsError",out var clientError)?clientError.GetString():null;}if(type=="sharing-state")SharingStatus=item.GetProperty("configured").GetBoolean()?"Windows ICS kaynağı: TetherMax · IPFORWARD koruması gözlendi · TV testi gerekli":"Windows paylaşımı etkin değil.";if(type=="sharing-error")SharingStatus="Paylaşım güvenlik hatası: "+item.GetProperty("message").GetString();if(type=="network-stopped"){Active=false;SharingStatus="TetherMax bağlantısı kapalı; paylaşım kurtarma sonucunu denetleyin.";}report(line);}}catch(Exception e){if(!stop.IsCancellationRequested)report("Ağ yardımcısı bağlantısı kapandı: "+e.Message);}finally{Active=false;if(!stop.IsCancellationRequested)report(JsonSerializer.Serialize(new{type="network-channel-closed"}));}});
    }
    public ValueTask DisposeAsync() {lock(disposeGate)return new ValueTask(disposal??=DisposeCore());}
    async Task DisposeCore()
    {
        stop.Cancel();pipe.Dispose();
        if(heartbeat!=null)await heartbeat;if(read!=null)await read;
        // Closing IPC asks the guardian to recover; never forcibly terminate it.
        if(guardian!=null){using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(50));try{await guardian.WaitForExitAsync(deadline.Token);}catch(OperationCanceledException e){throw new IOException("Ağ kurtarması henüz tamamlanmadı; yeni bağlantı güvenli biçimde engellendi.",e);}guardian.Dispose();}
        stop.Dispose();
    }
}
