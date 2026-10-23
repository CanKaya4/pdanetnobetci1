using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

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
    Process? engine; CancellationTokenSource? session; bool busy, closing, refreshing; string? selectedSerial; string? pairForward, dataForward;
    int socksPort=1088;
    readonly DispatcherTimer timer = new() { Interval=TimeSpan.FromSeconds(5) };
    readonly string adb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Android","Sdk","platform-tools","adb.exe");
    readonly string root = AppContext.BaseDirectory;
    static readonly string SettingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","preferences.json");
    string EnginePath => Path.Combine(root,"tethermax-engine.exe");
    public Dashboard()
    {
        Title="TetherMax · USB bağlantısı"; Width=880;Height=800;MinWidth=680;MinHeight=650;
        Background=new SolidColorBrush(Color.FromRgb(16,24,39));
        var panel=new StackPanel { Margin=new Thickness(32) }; Content=new ScrollViewer { Content=panel };
        panel.Children.Add(new TextBlock{Text="TetherMax",FontSize=36,FontWeight=FontWeights.Bold,Foreground=Brushes.White});
        panel.Children.Add(new TextBlock{Text="USB motoru · Güvenli eşleştirme önizlemesi",Foreground=Brushes.MediumAquamarine,Margin=new Thickness(0,4,0,12)});
        panel.Children.Add(state); panel.Children.Add(devices);
        var actions=new WrapPanel();panel.Children.Add(actions);
        Button(actions,"Telefonları yenile",async()=>await Refresh());
        Button(actions,"Bağlan",async()=>await Connect()); Button(actions,"Bağlantıyı kes",async()=>{reconnect.IsChecked=false;await Disconnect();});
        Button(actions,"İnternet testi",async()=>await Diagnose());
        Button(actions,"Eşleştirmeyi unut",async()=>{await Disconnect();if(devices.SelectedItem is Device d){Pairing.Forget(d.Serial);Note("Windows eşleştirmesi silindi. Telefonda da Güvenilen bilgisayarları unut seçeneğini kullanın.");}});
        panel.Children.Add(reconnect);
        try {
            if(File.Exists(SettingsPath)) { var saved=System.Text.Json.JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsPath));selectedSerial=saved?.Serial;reconnect.IsChecked=saved?.Reconnect==true; }
        } catch { Note("Tercihler okunamadı; otomatik bağlantı kapalı tutuldu."); }
        reconnect.Checked+=(_,_)=>SavePreferences();reconnect.Unchecked+=(_,_)=>SavePreferences();
        panel.Children.Add(new TextBlock{Text="Yalnızca localhost SOCKS · Windows varsayılan interneti değiştirilmez",Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,20)});
        var info=new TextBlock{Text="Profiller / sistem geneli internet\nHenüz uygulanmadı. Oyun, film ve adaptif şekillendirme etkin değildir.\n\nAnaliz\nTest başlatılmadı; hız veya gecikme sonucu yok. İnternet testi küçük miktarda mobil veri kullanır.",Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,FontSize=15,Margin=new Thickness(0,0,0,20)};panel.Children.Add(info);
        panel.Children.Add(new TextBlock{Text="TANILAMA",Foreground=Brushes.MediumAquamarine,Margin=new Thickness(0,0,0,8)});panel.Children.Add(log);
        Loaded+=async(_,_)=>{await Refresh();timer.Start();};
        timer.Tick+=async(_,_)=>{if(busy||closing)return;await Refresh();if(engine?.HasExited==true){Note("Motor durdu.");await Disconnect();}if(reconnect.IsChecked==true&&engine==null&&selectedSerial!=null&&devices.Items.Cast<Device>().Any(d=>d.Serial==selectedSerial))await Connect();};
        Closing+=async(_,e)=>{if(closing)return;e.Cancel=true;closing=true;timer.Stop();await Disconnect();Close();};
    }
    sealed record Device(string Serial,string Model) { public override string ToString()=>Model+" · "+Serial; }
    sealed record Preferences(string? Serial,bool Reconnect);
    void SavePreferences(){try{Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);File.WriteAllText(SettingsPath,System.Text.Json.JsonSerializer.Serialize(new Preferences(selectedSerial,reconnect.IsChecked==true)));}catch{Note("Tercihler kaydedilemedi.");}}
    void Button(Panel parent,string label,Func<Task> handler)
    {
        var button=new Button{Content=label,Padding=new Thickness(16,10,16,10),Margin=new Thickness(0,0,8,8)};
        button.Click+=async(_,_)=>{try{await handler();}catch(Exception ex){Note(ex.Message);}};parent.Children.Add(button);
    }
    void Note(string message){log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine);log.ScrollToEnd();}
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
            var output=await Adb("devices","-l");var list=new List<Device>();
            foreach(string line in output.Split('\n')){
                var bits=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                if(bits.Length<2||bits[1]!="device")continue;
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
        if(busy||engine!=null)return;if(devices.SelectedItem is not Device device){Note("Yetkili USB telefonu bulunamadı. Telefonda USB hata ayıklama onayını kontrol edin.");return;}
        if(!File.Exists(EnginePath))throw new IOException("Go motoru paket içinde bulunamadı.");
        busy=true;selectedSerial=device.Serial;SavePreferences();session=new CancellationTokenSource(TimeSpan.FromSeconds(100));state.Text="Telefon doğrulanıyor…";
        try{
            int pairPort=await Forward(device.Serial,"io.tethermax.pair.v2");pairForward="tcp:"+pairPort;
            string secret=await Pairing.Connect(pairPort,device.Serial,code=>Dispatcher.InvokeAsync(()=>MessageBox.Show(this,"Telefondaki numara aynı mı?\n\n"+code+"\n\nTelefonda da onaylayın.","Güvenli eşleştirme",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes).Task,session.Token);
            int dataPort=await Forward(device.Serial,"io.tethermax.usb.v1");dataForward="tcp:"+dataPort;
            socksPort=System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p=>p.Port==1088)?FreePort():1088;
            var start=new ProcessStartInfo(EnginePath){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"serve","--approve-usb-test","--pairing-stdin","--transport","127.0.0.1:"+dataPort,"--listen","127.0.0.1:"+socksPort})start.ArgumentList.Add(arg);
            engine=Process.Start(start)??throw new IOException("Go motoru başlatılamadı.");engine.StandardInput.NewLine="\n";await engine.StandardInput.WriteLineAsync(secret);engine.StandardInput.Close();secret="";
            await Task.Delay(250);if(engine.HasExited)throw new IOException("Motor başlatılamadı: "+await engine.StandardError.ReadToEndAsync());
            state.Text="USB eşleşti · SOCKS hazır";Note("127.0.0.1:"+socksPort+" hazır. İnternet testi ile veri yolunu doğrulayın. Sistem geneli yönlendirme kapalı.");
        }catch(Exception ex){await Disconnect();Note("Bağlantı: "+ex.Message);}
        finally{busy=false;}
    }
    async Task Disconnect()
    {
        session?.Cancel();session?.Dispose();session=null;
        if(engine!=null){try{if(!engine.HasExited){engine.Kill();await engine.WaitForExitAsync();}}catch{}engine.Dispose();engine=null;}
        if(selectedSerial!=null)foreach(var endpoint in new[]{pairForward,dataForward})if(endpoint!=null)try{await Adb("-s",selectedSerial,"forward","--remove",endpoint);}catch(Exception ex){Note("Forward temizliği: "+ex.Message);}
        pairForward=null;dataForward=null;state.Text="Bağlantı kapalı";
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
}
