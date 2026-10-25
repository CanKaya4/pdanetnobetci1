using System.Text.Json;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;
namespace TetherMax.Network;

// Mobile Hotspot owns NAT/DHCP. Never call EnableSharing over its live session.
// Its durable intent is recovered by the same guardian as the Wintun source.
public static class NativeMobileHotspot {
 public sealed record Request(string Ssid,string Password,int Band);
 sealed record Ownership(Guid Source,string Ssid,int Band,bool TimeoutWasEnabled=false);
 static NetworkOperatorTetheringManager Manager(Plan p) {
  var profiles=NetworkInformation.GetConnectionProfiles().Where(c=>c.NetworkAdapter?.NetworkAdapterId==p.AdapterGuid).ToArray();
  if(profiles.Length!=1)throw new IOException("Doğrulanmış Wintun bağlantı profili bulunamadı. USB bağlantısını yeniden kurun.");
  var capability=NetworkOperatorTetheringManager.GetTetheringCapabilityFromConnectionProfile(profiles[0]);
  if(capability!=TetheringCapability.Enabled)throw new IOException("Windows hotspot kullanılamıyor: "+capability);
  return NetworkOperatorTetheringManager.CreateFromConnectionProfile(profiles[0]);
 }
 static Ownership? Read(Journal j,Plan p) {
  if(!File.Exists(j.PathFor("mobile-hotspot.json")))return null;
  var o=JsonSerializer.Deserialize<Ownership>(j.ReadPublished("mobile-hotspot.json"))??throw new IOException("Hotspot intent missing");
  if(o.Source!=p.AdapterGuid||o.Ssid.Length is <1 or >32||o.Band is <0 or >2)throw new IOException("Invalid mobile hotspot ownership");return o;
 }
 static void Match(NetworkOperatorTetheringManager m,Ownership o) {
  var c=m.GetCurrentAccessPointConfiguration();
  if(c.Ssid!=o.Ssid||(int)c.Band!=o.Band)throw new IOException("Hotspot configuration changed; recovery guard retained");
 }
 public static Guid Start(Journal j,Plan p,Request request,NativeBackend backend) {
  p.Validate();
  if(j.Completed||!ProcessIdentity.Alive(p.GuardianPid,p.GuardianStart)||!ProcessIdentity.Alive(p.WorkerPid,p.WorkerStart))throw new IOException("Etkin USB oturumu yok.");
  using(var h=JsonDocument.Parse(j.ReadPublished("windows-connectivity.json")))OwnedSharing.ValidateSource(h.RootElement,p);
  if(System.Text.Encoding.UTF8.GetByteCount(request.Ssid) is <1 or >32||request.Password.Length is <8 or >63||request.Password.Any(c=>c<32||c>126)||request.Band is <0 or >2)throw new IOException("Ağ adı, parola veya bant geçersiz.");
  var previous=OwnedSharing.Read(j,p);if(previous!=null&&!previous.NativeManaged)throw new IOException("Yeni hotspot sağlayıcısı için USB bağlantısını yeniden kurun.");
  var m=Manager(p);
  if(m.TetheringOperationalState!=TetheringOperationalState.Off)throw new IOException("Başka bir hotspot açık. Önce onu durdurun.");
  NativeBackend.PowerShell(OwnedSharing.Connections+"if(@($connections|Where-Object {$m.INetSharingConfigurationForINetConnection($_).SharingEnabled}).Count){throw 'Existing ICS configuration must not be replaced'}");
  var config=new NetworkOperatorTetheringAccessPointConfiguration{Ssid=request.Ssid,Passphrase=request.Password,Band=(TetheringWiFiBand)request.Band};
  if(!config.IsBandSupportedAsync(config.Band).AsTask().GetAwaiter().GetResult())throw new IOException("Seçilen Wi-Fi bandı bu adaptörde desteklenmiyor.");
  var o=new Ownership(p.AdapterGuid,request.Ssid,request.Band,NetworkOperatorTetheringManager.IsNoConnectionsTimeoutEnabled());
  string staged="mobile-hotspot-"+Guid.NewGuid().ToString("N")+".json";
  j.WriteNew(staged,JsonSerializer.Serialize(o));File.Move(j.PathFor(staged),j.PathFor("mobile-hotspot.json"),true);
  File.Delete(j.PathFor("sharing-disabled"));
  try {
   if(o.TimeoutWasEnabled)NetworkOperatorTetheringManager.DisableNoConnectionsTimeout();
   m.ConfigureAccessPointAsync(config).AsTask().GetAwaiter().GetResult();Match(m,o);
   var result=m.StartTetheringAsync().AsTask().GetAwaiter().GetResult();
   if(result.Status!=TetheringOperationStatus.Success)throw new IOException("Windows hotspot başlatılamadı: "+result.Status+" "+result.AdditionalErrorMessage);
   Observe(j,p);
   string json=NativeBackend.PowerShell("$until=[DateTime]::UtcNow.AddSeconds(15);do{$a=@(Get-NetAdapter -IncludeHidden|Where-Object {$_.InterfaceDescription -like '*Wi-Fi Direct*' -and $_.Status -eq 'Up'});if($a.Count -eq 1){break};Start-Sleep -Milliseconds 250}while([DateTime]::UtcNow -lt $until);if($a.Count -ne 1){throw 'Hotspot adapter not ready'};([guid]$a[0].InterfaceGuid).ToString('D')");
   Guid adapter=Guid.Parse(json.Trim());
   OwnedSharing.Start(j,p,adapter,backend,true);return adapter;
  }catch{OwnedSharing.Stop(j,p,backend);throw;}
 }
 public static void Observe(Journal j,Plan p) {
  var o=Read(j,p)??throw new IOException("Hotspot ownership missing");var m=Manager(p);Match(m,o);
  if(m.TetheringOperationalState!=TetheringOperationalState.On)throw new IOException("Windows hotspot kapandı.");
 }
 public static object[] Clients(Journal j,Plan p,uint index){
  var o=Read(j,p)??throw new IOException("Hotspot ownership missing");var m=Manager(p);Match(m,o);
  var peers=m.GetTetheringClients();Dictionary<string,string[]> neighbors;try{neighbors=HotspotNeighbors.Read(index);}catch{neighbors=new();}
  return peers.Select(c=>{string key=c.MacAddress.Replace(":","").Replace("-","").ToUpperInvariant();return (object)new{mac=c.MacAddress,names=c.HostNames.Select(h=>h.CanonicalName).Concat(neighbors.TryGetValue(key,out var ips)?ips:Array.Empty<string>()).Distinct().ToArray()};}).ToArray();
 }

 public static void Stop(Journal j,Plan p) {
  var o=Read(j,p);if(o==null)return;var m=Manager(p);
  if(m.TetheringOperationalState==TetheringOperationalState.Off){if(o.TimeoutWasEnabled)NetworkOperatorTetheringManager.EnableNoConnectionsTimeout();return;}
  Match(m,o);var result=m.StopTetheringAsync().AsTask().GetAwaiter().GetResult();
  if(result.Status!=TetheringOperationStatus.Success||m.TetheringOperationalState!=TetheringOperationalState.Off)throw new IOException("Windows hotspot durdurulamadı: "+result.Status);
  if(o.TimeoutWasEnabled)NetworkOperatorTetheringManager.EnableNoConnectionsTimeout();
 }
}
