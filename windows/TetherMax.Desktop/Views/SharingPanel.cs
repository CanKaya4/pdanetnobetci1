using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace TetherMax;
internal sealed class SharingPanel : StackPanel {
 public string StatusText=>status.Text;
 public async Task ToggleFromTray(){if(hotspot!=null||recoveryPending)await Stop();else await Start();}
 public async Task StopForExit(){await Stop();if(recoveryPending||changing)throw new IOException("Hotspot kurtarması tamamlanmadı; yeniden deneyin.");}
 readonly Func<SystemNetwork?> network;
 readonly TextBox ssid=new(){Text="TetherMax",MaxLength=32};
 readonly ComboBox band=new(){ItemsSource=new[]{"Otomatik","2,4 GHz · TV ve eski cihazlar","5 GHz · destekleyen cihazlar"},SelectedIndex=1};
 readonly PasswordBox password=new(){MaxLength=63};
 readonly TextBlock status=Text("USB bağlantısı bekleniyor",22);
 readonly TextBlock source=Text("",13),acceptance=Text("İstemci interneti henüz doğrulanmadı",14);
 readonly Button start=new(){Content="Hotspot Başlat"},end=new(){Content="Durdur",IsEnabled=false};
 readonly StackPanel clients=new();readonly TextBlock clientState=Text("Hotspot kapalı",14);string? clientSignature;readonly Dictionary<string,DateTime> firstSeen=new();
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(2)};
 NativeHotspot? hotspot;HotspotPortal? portal;bool changing;bool? lastActive;bool recoveryPending;
 string? session;
 static TextBlock Text(string value,int size)=>new(){Text=value,FontSize=size,Foreground=UiTheme.Ink,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)};
 static Border Card(UIElement child)=>new(){Background=UiTheme.Surface,CornerRadius=new CornerRadius(18),Padding=new Thickness(22),Margin=new Thickness(0,0,0,18),Child=child};
 static void Input(Control c){c.Margin=new Thickness(0,0,0,16);c.Padding=new Thickness(12);c.FontSize=16;c.Background=UiTheme.Background;c.Foreground=UiTheme.Ink;c.BorderBrush=UiTheme.Muted;c.BorderThickness=new Thickness(1);}
 public SharingPanel(Func<SystemNetwork?> network,Func<string,Task>? applyDns=null,Func<TrafficSettings>? currentSettings=null){
  this.network=network;Margin=new Thickness(28);Children.Add(Text("Wi-Fi Hotspot",30));Children.Add(Text("USB internetinizi telefon, TV ve diğer cihazlarla paylaşın.",14));
  var state=new StackPanel();state.Children.Add(new RomanceScene{Mood="hotspot"});state.Children.Add(status);state.Children.Add(source);state.Children.Add(acceptance);Children.Add(Card(state));
  var settings=new StackPanel();settings.Children.Add(Text("Ağ ayarları",20));settings.Children.Add(Text("Wi-Fi ağ adı",13));Input(ssid);settings.Children.Add(ssid);settings.Children.Add(Text("WPA2 parolası · en az 8 karakter",13));Input(password);settings.Children.Add(password);settings.Children.Add(Text("Yayın bandı",13));Input(band);settings.Children.Add(band);settings.Children.Add(Text("TV için 2,4 GHz seçin. Desteklenmeyen bantta yayın başlatılmaz.",13));
  password.Password=Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
  try{var saved=HotspotPreferences.Read();if(saved!=null){ssid.Text=saved.Ssid;band.SelectedIndex=saved.Band;password.Password=saved.Password;}}catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or FormatException or JsonException){status.Text="Kayıtlı ağ ayarı açılamadı; ağ adı ve parolayı yeniden girin.";}
  // Valid edits survive restart even if USB or hotspot startup fails.
  void SaveSettings(){if(!HotspotPreferences.Valid(ssid.Text,password.Password,band.SelectedIndex))return;try{HotspotPreferences.Save(ssid.Text,password.Password,band.SelectedIndex);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or CryptographicException){acceptance.Text="Hotspot ayarları kaydedilemedi: "+e.Message;}}
  ssid.TextChanged+=(_,_)=>SaveSettings();password.PasswordChanged+=(_,_)=>SaveSettings();band.SelectionChanged+=(_,_)=>SaveSettings();
  var generate=new Button{Content="Yeni güvenli parola üret",Padding=new Thickness(12),Margin=new Thickness(0,0,0,12)};generate.Click+=(_,_)=>{if(!changing&&hotspot==null)password.Password=Convert.ToHexString(RandomNumberGenerator.GetBytes(6));};settings.Children.Add(generate);
  var reveal=new CheckBox{Content="Parolayı göster",Foreground=UiTheme.Ink,Margin=new Thickness(0,0,0,12)};var visible=Text("",14);reveal.Checked+=(_,_)=>visible.Text=password.Password;reveal.Unchecked+=(_,_)=>visible.Text="";password.PasswordChanged+=(_,_)=>{if(reveal.IsChecked==true)visible.Text=password.Password;};settings.Children.Add(reveal);settings.Children.Add(visible);
  var actions=new WrapPanel();foreach(var b in new[]{start,end}){b.Padding=new Thickness(20,12,20,12);b.Margin=new Thickness(0,0,12,0);actions.Children.Add(b);}settings.Children.Add(actions);Children.Add(Card(settings));
  var devices=new StackPanel();devices.Children.Add(Text("Bağlı cihazlar",20));devices.Children.Add(clientState);devices.Children.Add(clients);devices.Children.Add(Text("Ad, IP ve MAC Windows tarafından bildirilir. İlk görülme zamanı bu uygulamaya aittir. Cihaz başına hız ölçülmediğinde tahmin gösterilmez.",12));Children.Add(Card(devices));
  var verify=new StackPanel();verify.Children.Add(Text("Bağlı cihazda internet kontrolü",20));verify.Children.Add(Text("Wi-Fi'ye bağlanan cihazın tarayıcısında http://192.168.137.1 adresini açın ve İnterneti kontrol et düğmesine dokunun. Sonuç bu ekranda görünecek.",14));Children.Add(Card(verify));
  var dns=new StackPanel();dns.Children.Add(Text("Paylaşılan internetin DNS filtresi",20));
  var profiles=new ComboBox{Margin=new Thickness(0,0,0,12),ItemsSource=new[]{"AdGuard · reklam / izleyici filtresi","AdGuard Aile","Cloudflare","Özgün DNS"},SelectedIndex=0};dns.Children.Add(profiles);
  var dnsApply=new Button{Content="DNS profilini uygula",Padding=new Thickness(16,10,16,10)};dns.Children.Add(dnsApply);var dnsState=Text("",14);dns.Children.Add(dnsState);
  dnsApply.Click+=async(_,_)=>{dnsApply.IsEnabled=false;try{if(applyDns==null)throw new IOException("Önizleme modunda ayar uygulanmaz.");await applyDns(new[]{"adguard","family","cloudflare","system"}[profiles.SelectedIndex]);dnsState.Text="Seçili profil: "+(currentSettings?.Invoke().DnsProfile??"bilinmiyor");}catch(Exception e){dnsState.Text=e.Message;}finally{dnsApply.IsEnabled=true;}};
  dns.Children.Add(Text("Normal TCP/UDP DNS sorguları seçilen servise USB/hücresel yoldan aktarılır. Cihazın kendi şifreli DNS’i ayrı çalışabilir. DNS filtreleri YouTube gibi uygulamalardaki tüm reklamları kaldıramaz.",13));Children.Add(Card(dns));

  start.Click+=async(_,_)=>await Start();end.Click+=async(_,_)=>await Stop();timer.Tick+=async(_,_)=>await Tick();Loaded+=(_,_)=>{timer.Start();_=Tick();};Dispatcher.ShutdownStarted+=(_,_)=>{timer.Stop();hotspot?.Dispose();if(portal!=null)_=portal.DisposeAsync();};
 }
 async Task Tick(){
  var n=network();bool active=n?.Active==true;
  start.IsEnabled=active&&!changing&&hotspot==null&&!recoveryPending;end.IsEnabled=!changing&&(hotspot!=null||recoveryPending);
  source.Text=active?"Kaynak: doğrulanmış TetherMax USB / Wintun":"Önce ana ekrandan TetherMax bağlantısını kurun.";
  if(hotspot==null&&!changing&&lastActive!=active)status.Text=active?"Hotspot başlatılmaya hazır":"USB bağlantısı bekleniyor";
  lastActive=active;
  if(SessionLifecycle.ShouldStopHotspot(active,hotspot!=null,changing,recoveryPending)){await Stop(true);if(!recoveryPending)status.Text="USB bağlantısı kapandı · hotspot durduruldu";}
  else if(hotspot?.Failure is string error)status.Text=error;
  else if(hotspot?.ClientError is string clientError)status.Text="İstemci bağlantısı: "+clientError;
  var peers=active&&hotspot!=null?n!.SharingClients:Array.Empty<SystemNetwork.HotspotClient>();
  string signature=(hotspot!=null)+":"+active+JsonSerializer.Serialize(peers)+n?.SharingClientsError;
  if(clientSignature!=signature){clientSignature=signature;clients.Children.Clear();clientState.Text=n?.SharingClientsError is string issue?"Cihaz listesi alınamadı: "+issue:hotspot==null?"Hotspot kapalı":peers.Length+" cihaz bağlı";
   foreach(var peer in peers){if(!firstSeen.ContainsKey(peer.Mac))firstSeen[peer.Mac]=DateTime.Now;string ip=string.Join(", ",peer.Names.Where(x=>IPAddress.TryParse(x,out _)));string name=string.Join(", ",peer.Names.Where(x=>!IPAddress.TryParse(x,out _)));clients.Children.Add(Card(Text((string.IsNullOrWhiteSpace(name)?"Wi-Fi cihazı":name)+Environment.NewLine+"IP: "+(ip.Length==0?"Windows henüz bildirmedi":ip)+Environment.NewLine+"MAC: "+peer.Mac+" · İlk görülme: "+firstSeen[peer.Mac].ToString("HH:mm:ss"),14)));}
  }
  if(portal?.Verified.Count>0)acceptance.Text=string.Join(Environment.NewLine,portal.Verified.Select(p=>p.Key+" · cihaz tarayıcısında HTTPS doğrulandı"));
 }
 async Task<bool> Share(bool enable){
  var n=network();string folder=session??n?.SessionFolder??throw new IOException("Etkin USB oturumu bulunamadı.");
  var info=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"TetherMax.Network.exe")){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
  info.ArgumentList.Add(enable?"share":"unshare");info.ArgumentList.Add(folder);if(enable)info.ArgumentList.Add(hotspot!.AdapterGuid.ToString());info.ArgumentList.Add("--approve-windows-sharing");
  using var child=Process.Start(info)??throw new IOException("Paylaşım yardımcısı başlatılamadı.");await child.WaitForExitAsync();if(child.ExitCode!=0){string id=Path.GetFileName(folder).Replace("TetherMax-Network-","");string errorPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","reports","sharing-error-"+id+".txt");string reason=File.Exists(errorPath)?await File.ReadAllTextAsync(errorPath):"Yardımcı çıkış kodu "+child.ExitCode;throw new IOException("Paylaşım hazır değil: "+reason);}return true;
 }
 async Task Start(){
  if(changing||hotspot!=null)return;if(network()?.Active!=true){status.Text="Önce USB bağlantısını kurun.";return;}
  changing=true;ssid.IsEnabled=password.IsEnabled=band.IsEnabled=false;start.IsEnabled=false;
  try{
   session=network()!.SessionFolder;status.Text="Wi-Fi ağı oluşturuluyor…";hotspot=new(Dispatcher);await hotspot.Start(ssid.Text,password.Password,band.SelectedIndex,session!);
   try{HotspotPreferences.Save(ssid.Text,password.Password,band.SelectedIndex);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException){acceptance.Text="Hotspot açık; sonraki açılış için parola kaydedilemedi.";}
   status.Text="Wi-Fi açık · "+(band.SelectedIndex==1?"2,4 GHz":band.SelectedIndex==2?"5 GHz":"Otomatik bant")+" · istemci internet kontrolü bekleniyor";
   try{portal=new(IPAddress.Parse("192.168.137.1"));acceptance.Text="Bağlı cihazda http://192.168.137.1 adresini açın.";}
   catch(System.Net.Sockets.SocketException){acceptance.Text="Yerel kontrol sayfası açılamadı (80 numaralı port kullanımda olabilir). Bağlı cihazda HTTPS / YouTube testi yapın.";}
  }catch(Exception e){
   status.Text=e.Message+" · 0x"+e.HResult.ToString("X8");
   // The native helper rolls back its own failed start. Do not open another
   // elevation prompt when the original request failed or was cancelled.
   if(hotspot!=null){hotspot.Dispose();hotspot=null;}
  }finally{changing=false;ssid.IsEnabled=password.IsEnabled=band.IsEnabled=hotspot==null;await Tick();}
 }
 async Task Stop(bool automatic=false){
  if(changing||(hotspot==null&&!recoveryPending))return;changing=true;end.IsEnabled=false;
  try{if(automatic){if(network() is SystemNetwork owner)await owner.DisposeAsync();}else await Share(false);if(portal!=null){await portal.DisposeAsync();portal=null;}hotspot?.Dispose();hotspot=null;session=null;recoveryPending=false;status.Text="Hotspot durduruldu";acceptance.Text="İstemci interneti henüz doğrulanmadı";}
  catch(Exception e){recoveryPending=true;status.Text="Durdurma/kurtarma tamamlanmadı: "+e.Message;}
  finally{changing=false;ssid.IsEnabled=password.IsEnabled=band.IsEnabled=hotspot==null;}
 }
}
