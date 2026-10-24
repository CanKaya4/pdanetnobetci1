using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace TetherMax;

// A diagnostic view, not an undocumented remote-control API for MyPublicWiFi.
// Never infer shared-client connectivity from an adapter or process being up.
internal sealed class SharingPanel : StackPanel
{
    readonly TextBlock status = Text("Paylaşım henüz denetlenmedi.", 20);
    readonly TextBlock details = Text("", 14);
    readonly Button refresh;
    readonly ComboBox privateAdapter=new(){MinWidth=280,Margin=new Thickness(0,0,0,16)};
    readonly Func<SystemNetwork?> network;
    bool reading;
    bool changing;
    sealed record Hotspot(string Guid,string Name){public override string ToString()=>Name;}
    static TextBlock Text(string value, int size) => new() { Text=value, FontSize=size,
        Foreground=UiTheme.Ink, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,0,0,18) };
    public SharingPanel(Func<SystemNetwork?> network)
    {
        this.network=network;
        Margin=new Thickness(28);
        Children.Add(Text("TV ve Wi-Fi Paylaşımı",28));
        Children.Add(Text("Galaxy → USB → TetherMax → Windows paylaşımı → TV",14));
        Children.Add(status);
        Children.Add(Text("İlk kurulum: MyPublicWiFi → No Internet Sharing → Start Hotspot. SSID ve parolanız aynı kalır. Ardından TetherMax ile Bağlan, burada yerel Wi-Fi adaptörünü seç ve Paylaşımı başlat. Windows ICS, DHCP/DNS ve NAT sağlar.",14));
        Children.Add(privateAdapter);
        var actions=new WrapPanel();Children.Add(actions);
        refresh=new Button { Content="Güvenli bağlantı teşhisi", Padding=new Thickness(16,12,16,12), Margin=new Thickness(0,0,8,16) };
        refresh.Click+=async(_,_)=>await Inspect();actions.Children.Add(refresh);
        var open=new Button { Content="MyPublicWiFi’yi aç", Padding=new Thickness(16,12,16,12), Margin=new Thickness(0,0,8,16) };
        open.Click+=(_,_)=>{
            try {
                string path=new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)}
                    .Select(p=>Path.Combine(p,"MyPublicWiFi","MyPublicWiFi.exe")).FirstOrDefault(File.Exists)??throw new IOException("MyPublicWiFi kurulumu bulunamadı.");
                Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
            }catch(Exception e){status.Text="MyPublicWiFi açılamadı: "+e.Message;}
        };actions.Children.Add(open);
        foreach(var action in new[]{("Paylaşımı başlat",true),("Paylaşımı durdur",false)}){
            var button=new Button{Content=action.Item1,Padding=new Thickness(16,12,16,12),Margin=new Thickness(0,0,8,16)};
            button.Click+=async(_,_)=>await ChangeSharing(action.Item2);actions.Children.Add(button);
        }
        Children.Add(details);
        Children.Add(Text("MyPublicWiFi erişim noktasını yönetir. TetherMax yalnızca seçtiğiniz Wi-Fi Direct adaptörünün Windows ICS paylaşımını yönetici onayıyla açar/kapatır. Başka ICS paylaşımı varsa üzerine yazmaz. Tünel kapanmadan önce bu paylaşım kapatılır; kurtarma tamamlanmazsa koruma kaldırılmaz.",14));
        Children.Add(Text("TV interneti, TV trafik yolu, video oynatma ve bağlantı kaybında kaçak koruması: DOĞRULANMADI. Komşu tablosu DHCP kaydı değildir. TV başına veri ölçümü mevcut değil; toplam tünel sayacı TV sayacı olarak gösterilmez.",14));
        Loaded+=async(_,_)=>await Inspect();
    }
    async Task ChangeSharing(bool enabled)
    {
        if(changing)return;
        var active=network();
        if(active?.Active!=true||active.SessionFolder==null){status.Text="Önce ana ekrandan TetherMax bağlantısını kurun.";return;}
        if(enabled&&privateAdapter.SelectedItem is not Hotspot){status.Text="MyPublicWiFi erişim noktasını No Internet Sharing ile açıp teşhisi yenileyin; etkin Wi-Fi Direct adaptörünü seçin.";return;}
        changing=true;
        try {
            status.Text="Windows yönetici onayı ve paylaşım sonucu bekleniyor…";
            var launch=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"TetherMax.Network.exe")){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
            launch.ArgumentList.Add(enabled?"share":"unshare");launch.ArgumentList.Add(active.SessionFolder);
            if(enabled)launch.ArgumentList.Add(((Hotspot)privateAdapter.SelectedItem!).Guid);
            launch.ArgumentList.Add("--approve-windows-sharing");
            using var child=Process.Start(launch)??throw new IOException("Paylaşım yardımcısı başlatılamadı.");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(75));
            if(child.ExitCode!=0)throw new IOException("Paylaşım yardımcısı çıkış kodu "+child.ExitCode+". Mevcut paylaşım çakışması veya etkin olmayan Wi-Fi adaptörü olabilir. Korumalı oturum kaydı korunmuştur.");
            await Inspect();status.Text=enabled?"Windows ICS yapılandırıldı · TV internetini ayrıca doğrulayın":"TetherMax Windows ICS paylaşımı kapatıldı.";
        }catch(Exception e){status.Text="Paylaşım işlemi: "+e.Message;}
        finally{changing=false;}
    }
    async Task Inspect()
    {
        if(reading)return;reading=true;refresh.IsEnabled=false;status.Text="Windows paylaşım durumu okunuyor…";
        try {
            string script=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"sharing-audit.ps1"));
            var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"))
                {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            foreach(var argument in new[]{"-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(script))})start.ArgumentList.Add(argument);
            using var child=Process.Start(start)??throw new IOException("Paylaşım teşhisi başlatılamadı.");
            var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            try{await child.WaitForExitAsync(timeout.Token);}catch{try{child.Kill();}catch{}throw;}
            if(child.ExitCode!=0)throw new IOException((await error).Trim());
            string json=await output;
            using var document=JsonDocument.Parse(json);var root=document.RootElement;
            var lines=new List<string>();
            if(network() is { } current)lines.Add(current.SharingStatus);
            string? selected=(privateAdapter.SelectedItem as Hotspot)?.Guid;
            var hotspots=root.GetProperty("adapters").EnumerateArray().Where(a=>a.GetProperty("description").GetString()?.Contains("Wi-Fi Direct",StringComparison.OrdinalIgnoreCase)==true&&a.GetProperty("status").GetString()=="Up")
                .Select(a=>new Hotspot(a.GetProperty("guid").GetString()!,a.GetProperty("name").GetString()!)).ToArray();
            privateAdapter.ItemsSource=hotspots;privateAdapter.SelectedItem=hotspots.FirstOrDefault(h=>h.Guid==selected)??(hotspots.Length==1?hotspots[0]:null);
            foreach(var app in root.GetProperty("myPublicWiFi").EnumerateArray())lines.Add("MyPublicWiFi "+app.GetProperty("version").GetString()+" · "+(app.GetProperty("running").GetBoolean()?"İşlem açık (hotspot durumu ayrı)":"İşlem kapalı"));
            if(root.GetProperty("myPublicWiFi").GetArrayLength()==0)lines.Add("MyPublicWiFi bulunamadı.");
            foreach(var a in root.GetProperty("adapters").EnumerateArray().Where(a=>a.GetProperty("name").GetString()?.StartsWith("TetherMax-IP-")==true))
                lines.Add("TetherMax: "+a.GetProperty("name").GetString()+" · "+a.GetProperty("status").GetString()+" · "+a.GetProperty("guid").GetString());
            foreach(var route in root.GetProperty("selectedIPv4").EnumerateArray()) {
                var names=route.GetProperty("selection").EnumerateArray().Select(r=>r.GetProperty("InterfaceAlias").GetString()).Distinct();
                lines.Add("Windows rota seçimi · "+route.GetProperty("target").GetString()+" → "+string.Join(", ",names));
            }
            foreach(var c in root.GetProperty("ics").EnumerateArray().Where(c=>c.GetProperty("enabled").GetBoolean()))
                lines.Add("Windows ICS · "+c.GetProperty("name").GetString()+" · "+(c.GetProperty("type").GetInt32()==0?"İnternet kaynağı":"Yerel paylaşım"));
            if(root.GetProperty("icsError").ValueKind==JsonValueKind.String)lines.Add("Windows ICS: bu izin düzeyinde okunamadı; kapalı kabul edilmedi.");
            lines.Add("Yerel komşu kaydı: "+root.GetProperty("neighbors").GetArrayLength()+" (TV/bağlı cihaz sayısı değildir).");
            details.Text=string.Join(Environment.NewLine+Environment.NewLine,lines);
            status.Text="Yapılandırma okundu · TV veri yolu henüz doğrulanmadı";
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","reports");Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder,"sharing-"+Guid.NewGuid().ToString("N")+".json"),json);
        } catch(Exception e){status.Text="Paylaşım teşhisi alınamadı: "+e.Message;}
        finally{reading=false;refresh.IsEnabled=true;}
    }
}
