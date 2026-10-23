using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json;
using System.Globalization;

namespace TetherMax;
internal sealed class App : Application
{
    [STAThread] public static void Main() { new App().Run(new Dashboard()); }
}
internal sealed class Dashboard : Window
{
    readonly ComboBox devices = new() { MinWidth=260,Margin=new Thickness(0,12,0,12) };
    readonly TextBlock state = new() { Text="Bağlantı kapalı",FontSize=24,Foreground=Brushes.White,Margin=new Thickness(0,10,0,10) };
    readonly TextBox log = new() { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=220,Background=new SolidColorBrush(Color.FromRgb(16,24,39)),Foreground=Brushes.LightGray,BorderThickness=new Thickness(0) };
    readonly CheckBox reconnect = new() { Content="USB yeniden geldiğinde bağlan",Foreground=Brushes.White,Margin=new Thickness(0,12,0,12) };
    readonly TrafficPanel trafficPanel=new();
    Process? engine; CancellationTokenSource? session; bool busy, closing, refreshing; string? selectedSerial; string? pairForward, dataForward;
    int socksPort=1088;
    SystemNetwork? systemNetwork;
    readonly DispatcherTimer timer = new() { Interval=TimeSpan.FromSeconds(5) };
    readonly string adb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Android","Sdk","platform-tools","adb.exe");
    readonly string root = AppContext.BaseDirectory;
    static readonly string SettingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","preferences.json");
    string EnginePath => Path.Combine(root,"tethermax-engine.exe");
    public Dashboard()
    {
        Title="TetherMax · USB bağlantısı"; Width=960;Height=900;MinWidth=760;MinHeight=650;
        Background=new SolidColorBrush(Color.FromRgb(16,24,39));
        var panel=new StackPanel { Margin=new Thickness(32) }; Content=new ScrollViewer { Content=panel };
        panel.Children.Add(new TextBlock{Text="TetherMax",FontSize=36,FontWeight=FontWeights.Bold,Foreground=Brushes.White});
        panel.Children.Add(new TextBlock{Text="USB motoru · Güvenli eşleştirme önizlemesi",Foreground=Brushes.MediumAquamarine,Margin=new Thickness(0,4,0,12)});
        panel.Children.Add(state); panel.Children.Add(devices);
        var actions=new WrapPanel();panel.Children.Add(actions);
        Button(actions,"Telefonları yenile",async()=>await Refresh());
        Button(actions,"Bağlan",async()=>await Connect()); Button(actions,"Bağlantıyı kes",async()=>{reconnect.IsChecked=false;await Disconnect();});
        Button(actions,"İnternet testi",async()=>await Diagnose());
        Button(actions,"IP motoru testi",async()=>await DiagnoseIP());
        Button(actions,"Windows ağ yolu raporu",async()=>await AuditNetwork());
        Button(actions,"Sistem geneli USB testi",async()=>await ConnectSystemNetwork());
        Button(actions,"Eşleştirmeyi unut",async()=>{await Disconnect();if(devices.SelectedItem is Device d){Pairing.Forget(d.Serial);Note("Windows eşleştirmesi silindi. Telefonda da Güvenilen bilgisayarları unut seçeneğini kullanın.");}});
        panel.Children.Add(reconnect);
        try {
            if(File.Exists(SettingsPath)) { var saved=System.Text.Json.JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsPath));selectedSerial=saved?.Serial;reconnect.IsChecked=saved?.Reconnect==true;trafficPanel.Load(saved?.Traffic??new()); }
        } catch { Note("Tercihler okunamadı; otomatik bağlantı kapalı tutuldu."); }
        reconnect.Checked+=(_,_)=>SavePreferences();reconnect.Unchecked+=(_,_)=>SavePreferences();
        panel.Children.Add(new TextBlock{Text="Yalnızca localhost SOCKS · Windows varsayılan interneti değiştirilmez",Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,20)});
        trafficPanel.ApplySettings=async settings=>{if(engine!=null&&!engine.HasExited){await engine.StandardInput.WriteLineAsync(JsonSerializer.Serialize(settings));await engine.StandardInput.FlushAsync();}SavePreferences(settings);};
        panel.Children.Add(trafficPanel);
        panel.Children.Add(new TextBlock{Text="Deneysel akış zamanlama · Sistem geneli yönlendirme ve MyPublicWiFi ağ kurulumu kapalıdır. Oyun gecikmesinde iyileşme henüz ölçülmedi.",Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
        panel.Children.Add(new TextBlock{Text="TANILAMA",Foreground=Brushes.MediumAquamarine,Margin=new Thickness(0,0,0,8)});panel.Children.Add(log);
        Loaded+=async(_,_)=>{await Refresh();timer.Start();};
        timer.Tick+=async(_,_)=>{if(busy||closing)return;await Refresh();if(engine?.HasExited==true){Note("Motor durdu.");await Disconnect();}if(reconnect.IsChecked==true&&engine==null&&systemNetwork==null&&selectedSerial!=null&&devices.Items.Cast<Device>().Any(d=>d.Serial==selectedSerial))await Connect();};
        Closing+=async(_,e)=>{if(closing)return;e.Cancel=true;closing=true;timer.Stop();await Disconnect();Close();};
    }
    sealed record Device(string Serial,string Model) { public override string ToString()=>Model+" · "+Serial; }
    sealed record Preferences(string? Serial,bool Reconnect,TrafficSettings? Traffic=null);
    void SavePreferences(TrafficSettings? settings=null){try{Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);File.WriteAllText(SettingsPath,System.Text.Json.JsonSerializer.Serialize(new Preferences(selectedSerial,reconnect.IsChecked==true,settings??trafficPanel.Settings)));}catch{Note("Tercihler kaydedilemedi.");}}
    void Button(Panel parent,string label,Func<Task> handler)
    {
        var button=new Button{Content=label,Padding=new Thickness(16,10,16,10),Margin=new Thickness(0,0,8,8)};
        button.Click+=async(_,_)=>{try{await handler();}catch(Exception ex){Note(ex.Message);}};parent.Children.Add(button);
    }
    void Note(string message){log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine);if(log.Text.Length>65536)log.Text=log.Text[^49152..];log.ScrollToEnd();}
    async Task AuditNetwork()
    {
        string script=Path.Combine(root,"network-audit.ps1");
        if(!File.Exists(script))throw new IOException("Salt okunur ağ raporu betiği pakette bulunamadı.");
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"))
        {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
        // Inline bundled read-only commands work with the default Restricted
        // script-file policy. No process/machine execution policy is changed.
        string commands=Convert.ToBase64String(Encoding.Unicode.GetBytes(await File.ReadAllTextAsync(script)));
        foreach(string argument in new[]{"-NoProfile","-NonInteractive","-EncodedCommand",commands})start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new IOException("Ağ raporu başlatılamadı.");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await process.WaitForExitAsync(deadline.Token);}catch{try{process.Kill();}catch{}throw;}
        string json=await output, failure=await error;
        if(process.ExitCode!=0)throw new IOException("Ağ raporu alınamadı: "+failure.Trim());
        using var report=JsonDocument.Parse(json);
        foreach(var route in report.RootElement.GetProperty("selectedRoutes").EnumerateArray())
        {
            string target=route.GetProperty("target").GetString()??"?";
            foreach(var selection in route.GetProperty("selection").EnumerateArray())
            {
                if(selection.TryGetProperty("InterfaceAlias",out var name))Note("Windows rota seçimi · "+target+" → "+name.GetString());
            }
            if(route.GetProperty("error").ValueKind==JsonValueKind.String)Note("Windows rota seçimi · "+target+": "+route.GetProperty("error").GetString());
        }
        Note("Aktarım oturumu: "+(systemNetwork?.Active==true?"TetherMax Wintun IPv4; WFP dış IPv6 engeli etkin olarak raporlandı":engine!=null&&!engine.HasExited?"TetherMax localhost SOCKS; yalnızca proxy kullanan uygulamalar":"TetherMax motoru kapalı")+". Operatör muhasebesi ve hotspot kapalı çalışma ayrı ölçülmelidir.");
        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","reports");
        Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"network-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".json");
        using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
        {byte[] bytes=Encoding.UTF8.GetBytes(json);await file.WriteAsync(bytes);file.Flush(true);}
        Note("Salt okunur yapılandırma ve adaptör sayaçları kaydedildi: "+path+". Bu kayıt trafik yakalama kanıtı değildir.");
    }
    async Task<string> Adb(params string[] arguments)
    {
        if(!File.Exists(adb))throw new IOException("Resmî Android SDK platform-tools bulunamadı. Mevcut ADB kurulumuna müdahale edilmedi.");
        var start=new ProcessStartInfo(adb){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in arguments)start.ArgumentList.Add(arg);
        using var p=Process.Start(start)??throw new IOException("ADB başlatılamadı.");
        var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try{await p.WaitForExitAsync(timeout.Token);}catch{try{p.Kill();}catch{}throw;}
        string output=await stdout,error=await stderr;if(p.ExitCode!=0)throw new IOException("ADB: "+error.Trim());return output;
    }
    async Task Refresh()
    {
        if(busy||refreshing)return;refreshing=true;
        try{
            var old=(devices.SelectedItem as Device)?.Serial??selectedSerial;
            var output=await Adb("devices","-l");var list=new List<Device>();bool unauthorized=false;
            foreach(string line in output.Split('\n')){
                var bits=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                if(bits.Length<2)continue;
                if(bits[1]=="unauthorized"){unauthorized=true;continue;}
                if(bits[1]!="device")continue;
                string path=await Adb("-s",bits[0],"get-devpath");
                if(!path.Trim().StartsWith("usb:")) {
                    if(path.Trim()!="unknown"||bits[0].Contains(':'))continue;
                    string serial=(await Adb("-s",bits[0],"shell","getprop","ro.serialno")).Trim();
                    string usb=(await Adb("-s",bits[0],"shell","getprop","sys.usb.state")).Trim();
                    if(serial!=bits[0]||!usb.Split(',').Contains("adb"))continue;
                }
                string model=bits.FirstOrDefault(b=>b.StartsWith("model:"))?.Substring(6)??"Android";list.Add(new Device(bits[0],model));
            }
            devices.ItemsSource=list;devices.SelectedItem=list.FirstOrDefault(d=>d.Serial==old)??list.FirstOrDefault();
            if(engine!=null&&!list.Any(d=>d.Serial==selectedSerial)){await Disconnect();Note("USB cihazı ayrıldı; yalnızca TetherMax bağlantısı kapatıldı.");}
            if(engine!=null) {
                string forwards=await Adb("forward","--list");
                bool Has(string? endpoint)=>endpoint!=null&&forwards.Split('\n').Any(line=>line.StartsWith(selectedSerial+" ")&&line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Contains(endpoint));
                if(!Has(pairForward)||!Has(dataForward)){await Disconnect();Note("TetherMax USB aktarım kanalı kayboldu; yeniden bağlantı bekleniyor.");}
            }
            if(engine==null&&!busy){
                string next=list.Count>0?"USB telefon hazır · bağlantı kapalı":unauthorized?"Telefon bulundu · ADB izni bekleniyor":"Yetkili USB telefonu bulunamadı";
                if(unauthorized&&list.Count==0&&state.Text!=next)Note("USB hata ayıklama açık olsa da bu bilgisayarın Android RSA izni gerekiyor. Uygulama izinleri veya USB bağlantısını sıfırlamaz.");
                state.Text=next;
            }
        }catch(Exception ex){if(log.Text.Length==0)Note(ex.Message);}
        finally{refreshing=false;}
    }
    static int FreePort(){var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();int p=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();return p;}
    async Task<int> Forward(string serial,string endpoint)
    {
        int port=FreePort();await Adb("-s",serial,"forward","--no-rebind","tcp:"+port,"localabstract:"+endpoint);
        try {
            var listeners=System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Where(p=>p.Port==port).ToArray();
            if(listeners.Length==0||listeners.Any(p=>!IPAddress.IsLoopback(p.Address)))throw new IOException("ADB dinleyicisi yalnızca localhost ile sınırlı değil.");
            return port;
        } catch { await Adb("-s",serial,"forward","--remove","tcp:"+port);throw; }
    }
    async Task Connect()
    {
        if(busy||engine!=null||systemNetwork!=null)return;if(devices.SelectedItem is not Device device){Note("Yetkili USB telefonu bulunamadı. Telefonda USB hata ayıklama onayını kontrol edin.");return;}
        if(!File.Exists(EnginePath))throw new IOException("Go motoru paket içinde bulunamadı.");
        busy=true;selectedSerial=device.Serial;SavePreferences();session=new CancellationTokenSource(TimeSpan.FromSeconds(100));state.Text="Telefon doğrulanıyor…";
        try{
            int pairPort=await Forward(device.Serial,"io.tethermax.pair.v2");pairForward="tcp:"+pairPort;
            string secret=await Pairing.Connect(pairPort,device.Serial,code=>Dispatcher.InvokeAsync(()=>MessageBox.Show(this,"Telefondaki numara aynı mı?\n\n"+code+"\n\nTelefonda da onaylayın.","Güvenli eşleştirme",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes).Task,session.Token);
            int dataPort=await Forward(device.Serial,"io.tethermax.usb.v1");dataForward="tcp:"+dataPort;
            socksPort=System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p=>p.Port==1088)?FreePort():1088;
            var start=new ProcessStartInfo(EnginePath){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"serve","--approve-usb-test","--pairing-stdin","--transport","127.0.0.1:"+dataPort,"--listen","127.0.0.1:"+socksPort})start.ArgumentList.Add(arg);
            foreach(var arg in new[]{"--traffic-control-stdin","--traffic-telemetry","--profile",trafficPanel.Settings.Mode,"--upload-mbps",trafficPanel.Settings.UploadMbps.ToString(CultureInfo.InvariantCulture),"--download-mbps",trafficPanel.Settings.DownloadMbps.ToString(CultureInfo.InvariantCulture)})start.ArgumentList.Add(arg);
            if(trafficPanel.Settings.Adaptive)start.ArgumentList.Add("--adaptive");
            engine=Process.Start(start)??throw new IOException("Go motoru başlatılamadı.");engine.StandardInput.NewLine="\n";await engine.StandardInput.WriteLineAsync(secret);await engine.StandardInput.FlushAsync();secret="";
            _=ReadTelemetry(engine);
            await Task.Delay(250);if(engine.HasExited)throw new IOException("Motor başlatılamadı: "+await engine.StandardError.ReadToEndAsync());
            state.Text="USB eşleşti · SOCKS hazır";Note("127.0.0.1:"+socksPort+" hazır. İnternet testi ile veri yolunu doğrulayın. Sistem geneli yönlendirme kapalı.");
        }catch(Exception ex){await Disconnect();Note("Bağlantı: "+ex.Message);}
        finally{busy=false;}
    }
    async Task Disconnect()
    {
        if(systemNetwork!=null){await systemNetwork.DisposeAsync();systemNetwork=null;}
        session?.Cancel();session?.Dispose();session=null;
        if(engine!=null){try{if(!engine.HasExited){engine.Kill();await engine.WaitForExitAsync();}}catch{}engine.Dispose();engine=null;}
        if(selectedSerial!=null)foreach(var endpoint in new[]{pairForward,dataForward})if(endpoint!=null)try{await Adb("-s",selectedSerial,"forward","--remove",endpoint);}catch(Exception ex){Note("Forward temizliği: "+ex.Message);}
        pairForward=null;dataForward=null;state.Text="Bağlantı kapalı";
        trafficPanel.Reset();
    }
    async Task ConnectSystemNetwork()
    {
        if(busy||systemNetwork!=null)return;
        if(MessageBox.Show(this,"Bu ayrı fiziksel geçiş testidir. En fazla 120 saniye ve toplam 1 MiB relay payload bütçesiyle tüm IPv4 çıkışı TetherMax Wintun'a yönlendirilecek; dış IPv6 ve diğer adaptör çıkışları WFP ile engellenecek. Protokol ek yükleri, yeniden iletim ve Android isim çözümü bu payload sayacına dahil değildir; operatör faturası için kesin üst sınır değildir. TetherMax'e özel IP, iki /1 rota ve geçici DNS kuralı eklenecek. Ethernet 5 kapatılmaz. Resmî Wintun sürücüsü yüklenebilir; paylaşılan sürücü paketi test sonunda silinmez. Bağlantı başarısızsa mevcut internetiniz ve bu sohbet geçici kesilebilir. Ayrı kurtarma süreci ayarları geri alır.\n\nArka plan uygulamaları da bütçeyi kullanabilir. Operatör karşılaştırma deneyi ayrıca onaylanmalıdır.\n\nBu somut ağ değişikliklerine ve belirtilen veri bütçesine şimdi izin veriyor musunuz?","Ayrı sistem geneli ağ onayı",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        if(engine==null)await Connect();
        if(engine==null||selectedSerial==null||pairForward==null||dataForward==null){Note("USB eşleştirmesi kurulamadı; sistem ağı değiştirilmedi.");return;}
        busy=true;
        try {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
            string secret=await Pairing.Connect(int.Parse(pairForward[4..]),selectedSerial,code=>Dispatcher.InvokeAsync(()=>MessageBox.Show(this,"Telefondaki numara aynı mı?\n"+code,"Güvenli eşleştirme",MessageBoxButton.YesNo)==MessageBoxResult.Yes).Task,timeout.Token);
            // Preserve the selected ADB forwards but stop the unbudgeted SOCKS
            // worker/adaptive probes before starting the bounded global test.
            if(engine!=null){if(!engine.HasExited){engine.Kill();await engine.WaitForExitAsync();}engine.Dispose();engine=null;}
            trafficPanel.Reset();
            systemNetwork=new SystemNetwork();
            await systemNetwork.Start(root,"127.0.0.1:"+dataForward[4..],secret,message=>Dispatcher.BeginInvoke(()=>{
                Note(message);
                try{using var record=JsonDocument.Parse(message);string? type=record.RootElement.GetProperty("type").GetString();
                    if(type=="network-active")state.Text="Sistem geneli IPv4 · USB · IPv6 engelli";
                    if(type=="network-stopped")state.Text="Sistem geneli test durdu · kurtarma kaydını kontrol edin";
                    if(type=="network-report"){
                        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","reports");Directory.CreateDirectory(folder);
                        string file=Path.Combine(folder,"native-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".json");
                        using var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.Read);byte[] bytes=Encoding.UTF8.GetBytes(message);output.Write(bytes);output.Flush(true);Note("Windows/Android ayrı sayaç raporu: "+file);
                    }
                }catch(JsonException){}catch(IOException e){Note("Yerel rapor kaydedilemedi: "+e.Message);}
            }));secret="";
            Note("Yönetici ağ yardımcısı başlatıldı; aktif veri yolu onayı bekleniyor. Test 120 saniyede geri alınır.");
        }catch{if(systemNetwork!=null){await systemNetwork.DisposeAsync();systemNetwork=null;}throw;}
        finally{busy=false;}
    }
    async Task ReadTelemetry(Process process)
    {
        try{while(await process.StandardOutput.ReadLineAsync() is string line){if(!ReferenceEquals(engine,process))return;try{using var data=JsonDocument.Parse(line);var value=data.RootElement;if(!value.TryGetProperty("type",out var kind))continue;if(kind.GetString()=="traffic"){string previous=trafficPanel.Settings.Mode;trafficPanel.Accept(value);if(previous!=trafficPanel.Settings.Mode)SavePreferences();}else if(kind.GetString()=="traffic-error")Note("Trafik ayarı: "+value.GetProperty("message").GetString());}catch(JsonException){}}}catch(Exception ex){if(ReferenceEquals(engine,process)&&!closing)Note("Telemetri: "+ex.Message);}
    }
    async Task Diagnose()
    {
        if(engine==null||busy){Note("Önce USB bağlantısı kurun.");return;}
        if(MessageBox.Show(this,"3 küçük HTTPS isteği gönderilecek. Mobil veri kullanır; hız testi değildir. Devam edilsin mi?","İnternet testi",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        busy=true;
        try{
            var tasks=Enumerable.Range(0,3).Select(async i=>{
                var start=new ProcessStartInfo("curl.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(var arg in new[]{"--proxy","","--socks5-hostname","127.0.0.1:"+socksPort,"--connect-timeout","8","--max-time","15","--max-filesize","65536","--fail","--silent","--show-error","--output","NUL","--write-out","HTTP=%{http_code} toplam=%{time_total}s veri=%{size_download}B","https://example.com/"})start.ArgumentList.Add(arg);
                using var p=Process.Start(start)!;var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();return "HTTPS "+(i+1)+": "+await output+(p.ExitCode==0?"":" HATA: "+await error);
            });
            foreach(var result in await Task.WhenAll(tasks))Note(result);
            foreach(var result in await UdpDiagnostic.Run(socksPort))Note(result);
            var report=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","reports");Directory.CreateDirectory(report);
            File.WriteAllText(Path.Combine(report,DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt"),log.Text);
        }finally{busy=false;}
    }
    async Task DiagnoseIP()
    {
        if(engine==null||busy||selectedSerial==null||pairForward==null||dataForward==null){Note("Önce yetkili USB bağlantısı kurun.");return;}
        if(MessageBox.Show(this,"Adaptör, Windows rotası veya DNS oluşturmadan bellek içi IP stack üzerinden telefona test trafiği gönderilecek. 1 küçük HTTPS ve 5 DNS sorgusu; mobil veri tüketir. Devam edilsin mi?","İzole IP motoru testi",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        busy=true;
        try {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
            string secret=await Pairing.Connect(int.Parse(pairForward[4..]),selectedSerial,code=>Dispatcher.InvokeAsync(()=>MessageBox.Show(this,"Telefondaki kısa numara aynı mı?\n"+code,"Güvenli eşleştirme",MessageBoxButton.YesNo)==MessageBoxResult.Yes).Task,timeout.Token);
            var start=new ProcessStartInfo(EnginePath){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            foreach(var arg in new[]{"ip-check","--approve-usb-test","--transport","127.0.0.1:"+dataForward[4..]})start.ArgumentList.Add(arg);
            foreach(var arg in new[]{"--profile",trafficPanel.Settings.Mode,"--upload-mbps",trafficPanel.Settings.UploadMbps.ToString(CultureInfo.InvariantCulture),"--download-mbps",trafficPanel.Settings.DownloadMbps.ToString(CultureInfo.InvariantCulture)})start.ArgumentList.Add(arg);
            if(trafficPanel.Settings.Adaptive)start.ArgumentList.Add("--adaptive");
            using var process=Process.Start(start)??throw new IOException("IP test süreci başlatılamadı.");process.StandardInput.NewLine="\n";await process.StandardInput.WriteLineAsync(secret);process.StandardInput.Close();secret="";
            var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
            try {await process.WaitForExitAsync(timeout.Token);}catch{try{process.Kill();}catch{}throw;}
            string report=await output;Note("IP motoru sonucu: "+report.Trim());if(process.ExitCode!=0)Note("IP motoru testi başarısız: "+await errors);
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","reports");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-ip.json"),report);
        }finally{busy=false;}
    }
}
