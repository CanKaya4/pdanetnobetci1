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
internal sealed partial class Dashboard : Window
{
    readonly ComboBox devices = new() { MinWidth=260,Margin=new Thickness(0,12,0,12) };
    readonly TextBlock state = new() { Text="Bağlantı kapalı",TextWrapping=TextWrapping.Wrap,FontSize=24,Foreground=UiTheme.Ink,Margin=new Thickness(0,10,0,10) };
    readonly TextBox log = new() { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=220,Background=UiTheme.Background,Foreground=UiTheme.Muted,BorderThickness=new Thickness(0) };
    readonly CheckBox reconnect = new() { Content="USB yeniden geldiğinde bağlan",Foreground=UiTheme.Ink,Margin=new Thickness(0,12,0,12) };
    readonly TrafficPanel trafficPanel=new();
    readonly SemaphoreSlim operations=new(1,1);
    readonly ConnectionViewModel connection=new();
    bool connecting,authorizationRequired,userCanceled;
    bool exitRequested,hideNoticeSeen,allowClose;
    readonly RomanceScene mascot=new();
    readonly LiveTrafficChart homeChart=new();
    readonly DispatcherTimer visualClock=new(){Interval=TimeSpan.FromSeconds(1)};
    SharingPanel sharing=null!;
    TaskCompletionSource<bool>? refreshFinished;
    Button? connectControl,disconnectControl,cancelControl;
    void UpdateControls(){bool active=connection.Phase==ConnectionPhase.Connected&&systemNetwork?.Active==true;bool pending=connecting||connection.Phase is ConnectionPhase.DetectingPhone or ConnectionPhase.PhoneDetected or ConnectionPhase.CheckingAuthorization or ConnectionPhase.PreparingAndroid or ConnectionPhase.Authenticating or ConnectionPhase.OpeningTransport or ConnectionPhase.StartingEngine or ConnectionPhase.AwaitingConsent or ConnectionPhase.ConfiguringWindows or ConnectionPhase.CheckingInternet;if(connectControl!=null){connectControl.Visibility=active?Visibility.Collapsed:Visibility.Visible;connectControl.IsEnabled=!pending;}if(disconnectControl!=null)disconnectControl.Visibility=active?Visibility.Visible:Visibility.Collapsed;if(cancelControl!=null)cancelControl.Visibility=pending?Visibility.Visible:Visibility.Collapsed;}
    DateTime? connectedAt;
    readonly TextBlock sessionDetails=new(){FontSize=13,Foreground=UiTheme.Muted,Margin=new Thickness(0,10,0,0),TextWrapping=TextWrapping.Wrap};
    readonly CellularAvailability cellularAvailability=new();
    TaskCompletionSource<bool>? profileApplied;TrafficSettings? pendingProfile; int reconnectAttempts;
    void Stage(string label) { connection.Status=label; state.Text=label; Note("Aşama · "+label);UpdateControls(); }
    void Phase(ConnectionPhase phase,string label) { connection.Advance(phase,label);Stage(label); }
    Process? engine; CancellationTokenSource? session; bool busy, closing, refreshing; string? selectedSerial; string? pairForward, dataForward;
    int socksPort=1088;
    SystemNetwork? systemNetwork;
    bool wantsDaily;
    DateTime nextReconnect=DateTime.MinValue;
    System.Windows.Forms.NotifyIcon? tray;
    bool DailyReady {
        get { try {
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","reports");
            if(!Directory.Exists(folder))return false;
            foreach(string path in Directory.EnumerateFiles(folder,"native-*.json").OrderDescending().Take(20)) {
                using var doc=JsonDocument.Parse(File.ReadAllText(path));var r=doc.RootElement;
                if(r.TryGetProperty("recovered",out var recovered)&&recovered.ValueKind==JsonValueKind.True&&r.TryGetProperty("windowsConnectivity",out var checks)&&checks.ValueKind==JsonValueKind.Object&&checks.GetProperty("checks").GetArrayLength()==3&&checks.GetProperty("checks").EnumerateArray().All(c=>c.GetProperty("passed").GetBoolean()))return true;
            }
        }catch{}return false; }
    }
    readonly DispatcherTimer timer = new() { Interval=TimeSpan.FromSeconds(5) };
    readonly string adb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Android","Sdk","platform-tools","adb.exe");
    readonly string root = AppContext.BaseDirectory;
    static readonly string SettingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","preferences.json");
    string EnginePath => Path.Combine(root,"tethermax-engine.exe");
    public Dashboard(bool preview=false,bool manualStart=false,bool lifecycleTest=false)
    {
        InitializeComponent(); DataContext=connection; sharing=new SharingPanel(()=>systemNetwork,trafficPanel.SelectDns,()=>trafficPanel.Settings);SharingHost.Content=sharing;
        Title="TetherMax · Studio Edition"; Width=1120;Height=880;MinWidth=760;MinHeight=650;
        Background=UiTheme.Background;
        var panel=new StackPanel { Margin=new Thickness(28) }; HomeHost.Content=panel;
        panel.Children.Add(new TextBlock{Text="BAĞLANTININ EN GÜZEL HALİ",FontSize=10,FontWeight=FontWeights.SemiBold,Foreground=UiTheme.Accent,Margin=new Thickness(0,0,0,8)});
        panel.Children.Add(new TextBlock{Text="İyi ki bağlıyız.",FontSize=32,FontWeight=FontWeights.SemiBold,Foreground=UiTheme.Ink});
        panel.Children.Add(new TextBlock{Text="Telefonun, bilgisayarın ve sen. Tek bir güzel bağlantı.",FontSize=13,Foreground=UiTheme.Muted,Margin=new Thickness(0,8,0,12),TextWrapping=TextWrapping.Wrap});
        var hero=new Border{Background=UiTheme.Surface,CornerRadius=new CornerRadius(24),Padding=new Thickness(24),Margin=new Thickness(0,12,0,20)};
        var heroGrid=new Grid();heroGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});heroGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0.85,GridUnitType.Star)});hero.Child=heroGrid;
        var heroContent=new StackPanel{VerticalAlignment=VerticalAlignment.Center};heroGrid.Children.Add(heroContent);mascot.MinWidth=0;mascot.Height=180;Grid.SetColumn(mascot,1);heroGrid.Children.Add(mascot);
        hero.SizeChanged+=(_,_)=>{bool compact=hero.ActualWidth<450;mascot.Visibility=compact?Visibility.Collapsed:Visibility.Visible;heroGrid.ColumnDefinitions[1].Width=compact?new GridLength(0):new GridLength(0.85,GridUnitType.Star);};heroContent.Children.Add(new TextBlock{Text="BAĞLANTI DURUMU",FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=UiTheme.Accent,Margin=new Thickness(0,12,0,0)});heroContent.Children.Add(state);heroContent.Children.Add(sessionDetails);panel.Children.Add(hero);
        DevicesHost.Children.Add(new TextBlock{Text="Güvenilen USB telefonu",FontSize=28});DevicesHost.Children.Add(devices);
        heroContent.Children.Add(new TextBlock{Text="USB · Güvenli eşleştirme · Hücresel veri yolu",Foreground=UiTheme.Accent,TextWrapping=TextWrapping.Wrap});
        var summary=new MetricsDashboard();panel.Children.Add(summary);panel.Children.Add(new Border{Background=UiTheme.Surface,CornerRadius=new CornerRadius(20),Padding=new Thickness(20),Margin=new Thickness(0,0,0,16),Child=homeChart});trafficPanel.MetricsReset=()=>{summary.Clear();homeChart.Clear();};trafficPanel.MetricsUpdated=data=>{summary.Accept(data);homeChart.Accept(data);if(pendingProfile!=null&&pendingProfile.Acknowledged(data))profileApplied?.TrySetResult(true);};
        var modeFeedback=new TextBlock{Foreground=UiTheme.Accent,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18)};ModesHost.Children.Add(modeFeedback);
        var modeChoices=new List<(string Id,Button Button)>();
        trafficPanel.StatusChanged+=()=>{modeFeedback.Text=trafficPanel.StatusText;foreach(var item in modeChoices){item.Button.IsEnabled=!trafficPanel.IsApplying;item.Button.Content=item.Id==trafficPanel.Settings.Mode?"✓ Seçili":"Profili seç";}};
        foreach(var mode in new[]{("auto","Otomatik","Akıllı profil seçimi"),("gaming","Oyun","Gecikme odaklı trafik öncelikleri"),("streaming","Film / Dizi","Video ve paylaşılan bağlantı"),("daily","Günlük","Dengeli kullanım"),("maximum","Maksimum Hız","Aktarım odaklı profil")}) {
            var box=new Border{CornerRadius=new CornerRadius(16),Background=UiTheme.Surface,Padding=new Thickness(20),Margin=new Thickness(0,0,0,14)};
            var row=new StackPanel();box.Child=row;row.Children.Add(new TextBlock{Text=mode.Item2,FontSize=22});row.Children.Add(new TextBlock{Text=mode.Item3,Margin=new Thickness(0,8,0,12)});var select=Button(row,"Profili seç",async()=>await trafficPanel.SelectMode(mode.Item1));modeChoices.Add((mode.Item1,select));ModesHost.Children.Add(box);
        }
        var actions=new WrapPanel{Margin=new Thickness(0,16,0,0)};heroContent.Children.Add(actions);
        var diagnostics=new WrapPanel(); DiagnosticsHost.Content=new StackPanel(); ((StackPanel)DiagnosticsHost.Content).Children.Add(diagnostics);
        Button(DevicesHost,"Telefonları yenile",async()=>await Refresh());
        cancelControl=Button(actions,"İptal",async()=>{userCanceled=true;session?.Cancel();await Disconnect();});
        connectControl=Button(actions,"Bağlan",async()=>await ConnectSystemNetwork(false)); disconnectControl=Button(actions,"Bağlantıyı kes",async()=>{reconnect.IsChecked=false;await Disconnect();});
        connectControl.Background=new SolidColorBrush(Color.FromRgb(101,85,203));connectControl.Foreground=Brushes.White;disconnectControl.Background=new SolidColorBrush(Color.FromRgb(172,70,100));disconnectControl.Foreground=Brushes.White;
        foreach(var control in new[]{connectControl,disconnectControl}){control.FontSize=18;control.MinWidth=190;control.Padding=new Thickness(24,16,24,16);}UpdateControls();
        Button(diagnostics,"İnternet testi",async()=>await Diagnose());
        Button(diagnostics,"IP motoru testi",async()=>await DiagnoseIP());
        Button(diagnostics,"Windows ağ yolu raporu",async()=>await AuditNetwork());
        Button(diagnostics,"Kurtar / teşhis",async()=>{
            await Disconnect();
            var launch=new ProcessStartInfo(Path.Combine(root,"TetherMax.Network.exe")){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
            launch.ArgumentList.Add("recover-stale");launch.ArgumentList.Add("--approve-network-recovery");
            using var recovery=Process.Start(launch)??throw new IOException("Kurtarma yardımcısı başlatılamadı");
            await recovery.WaitForExitAsync();Note(recovery.ExitCode==0?"Kalmış TetherMax oturumları temizlendi. Mevcut bağlantıyı ağ yolu raporuyla kontrol edebilirsiniz.":"Kurtarma tamamlanamadı. Telefon USB tethering ayarını değiştirmeyin; ağ raporunu kontrol edin.");
        });
        Button(diagnostics,"Sistem geneli USB testi",async()=>await ConnectSystemNetwork());
        Button(diagnostics,"Eşleştirmeyi unut",async()=>{await Disconnect();if(devices.SelectedItem is Device d){Pairing.Forget(d.Serial);Note("Windows eşleştirmesi silindi. Telefonda da Güvenilen bilgisayarları unut seçeneğini kullanın.");}});
        panel.Children.Add(reconnect);
        var startup=new CheckBox{Content="Windows açılışında TetherMax’i aç",Foreground=UiTheme.Ink,Margin=new Thickness(0,4,0,12)};
        using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))startup.IsChecked=key?.GetValue("TetherMax") is string;
        startup.Checked+=(_,_)=>{using var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");key.SetValue("TetherMax","\""+Environment.ProcessPath+"\"");};
        startup.Unchecked+=(_,_)=>{using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true);key?.DeleteValue("TetherMax",false);};
        SettingsHost.Content=new StackPanel(); ((StackPanel)SettingsHost.Content).Children.Add(startup);
        var help=new TextBlock{Foreground=UiTheme.Muted,TextWrapping=TextWrapping.Wrap,Text="Telefon: TetherMax → Hücresel motoru başlat. Windows: Bağlan → UAC onayı. X veya simge durumuna küçültme ile sistem tepsisinde çalışır. Mevcut tek interneti sağlayan USB tethering’i güvenli alternatif olmadan kapatmayın. Kota bağımsızlığı doğrulanmadı."};((StackPanel)SettingsHost.Content).Children.Add(help);
        var trayMenu=new System.Windows.Forms.ContextMenuStrip();
        void Open(int page=0){Show();WindowState=WindowState.Normal;Navigation.SelectedIndex=page;Activate();}
        trayMenu.Items.Add("TetherMax'ı Aç",null,(_,_)=>Dispatcher.Invoke(()=>Open()));
        var connectionItem=trayMenu.Items.Add("Bağlantı Durumu");var hotspotItem=trayMenu.Items.Add("Hotspot Durumu");
        var toggle=trayMenu.Items.Add("Bağlan / Bağlantıyı Kes",null,(_,_)=>Dispatcher.BeginInvoke(async()=>{try{if(systemNetwork?.Active==true||engine!=null){reconnect.IsChecked=false;await Disconnect();}else await ConnectSystemNetwork(false);}catch(Exception e){Note(e.Message);}}));
        trayMenu.Items.Add("Hotspot Aç / Kapat",null,(_,_)=>Dispatcher.BeginInvoke(async()=>await sharing.ToggleFromTray()));
        trayMenu.Items.Add("Ayarlar",null,(_,_)=>Dispatcher.Invoke(()=>Open(6)));
        trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        trayMenu.Items.Add("Çıkış",null,(_,_)=>Dispatcher.Invoke(()=>{if(closing)return;if(MessageBox.Show(this,"TetherMax kapatılacak. Etkin USB interneti ve hotspot bağlantıları kesilebilir. Çıkılsın mı?","Güvenli çıkış",MessageBoxButton.OKCancel,MessageBoxImage.Question)!=MessageBoxResult.OK){RequestExit(false);return;}RequestExit(true);}));
        trayMenu.Opening+=(_,_)=>{connectionItem.Text="Bağlantı · "+state.Text;hotspotItem.Text="Hotspot · "+sharing.StatusText;toggle.Enabled=!connecting&&!closing;};
        tray=new System.Windows.Forms.NotifyIcon{Text="TetherMax · Romantic Edition",Icon=new System.Drawing.Icon(Path.Combine(root,"Assets","tethermax.ico")),Visible=!preview,ContextMenuStrip=trayMenu};
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(()=>Open());
        StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized)Hide();};
        Closed+=(_,_)=>{visualClock.Stop();timer.Stop();tray.Visible=false;tray.Dispose();trayMenu.Dispose();};
        try {
            if(!preview&&File.Exists(SettingsPath)) { var saved=System.Text.Json.JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsPath));UiTheme.Motion=saved?.Motion??1;UiTheme.Transparency=saved?.Transparency??true;hideNoticeSeen=saved?.HideNoticeSeen??false;UiTheme.SetLight(saved?.LightTheme??true);selectedSerial=saved?.Serial;reconnect.IsChecked=saved?.Reconnect==true;trafficPanel.Load(saved?.Traffic??new()); }
        } catch { Note("Tercihler okunamadı; otomatik bağlantı kapalı tutuldu."); }
        var light=new CheckBox{Content="Daylight · açık tema (kapalı: After Hours)",IsChecked=UiTheme.Light,Foreground=UiTheme.Ink,Margin=new Thickness(0,16,0,16)};((StackPanel)SettingsHost.Content).Children.Add(light);light.Checked+=(_,_)=>{UiTheme.SetLight(true);SavePreferences();};light.Unchecked+=(_,_)=>{UiTheme.SetLight(false);SavePreferences();};
        var motion=new ComboBox{ItemsSource=new[]{"Animasyonlar · Kapalı","Animasyonlar · Az","Animasyonlar · Tam"},SelectedIndex=UiTheme.Motion,Margin=new Thickness(0,0,0,16)};
        motion.SelectionChanged+=(_,_)=>{UiTheme.Motion=motion.SelectedIndex;SavePreferences();};((StackPanel)SettingsHost.Content).Children.Add(motion);
        var glass=new CheckBox{Content="Saydam kart yüzeyleri",IsChecked=UiTheme.Transparency,Margin=new Thickness(0,0,0,16)};
        glass.Click+=(_,_)=>{UiTheme.Transparency=glass.IsChecked==true;UiTheme.SetLight(UiTheme.Light);SavePreferences();};((StackPanel)SettingsHost.Content).Children.Add(glass);
        var notice=new CheckBox{Content="Tepsiye gizleme bilgisini tekrar gösterme",IsChecked=hideNoticeSeen};notice.Click+=(_,_)=>{hideNoticeSeen=notice.IsChecked==true;SavePreferences();};((StackPanel)SettingsHost.Content).Children.Add(notice);
        visualClock.Tick+=(_,_)=>{if(!IsVisible)return;mascot.Mood=connecting?"connecting":connectedAt.HasValue?"connected":connection.Phase==ConnectionPhase.Idle?"idle":"error";mascot.InvalidateVisual();if(connectedAt.HasValue){var elapsed=DateTime.UtcNow-connectedAt.Value;sessionDetails.Text=((devices.SelectedItem as Device)?.Model??"Telefon")+" · USB · "+$"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";}};
        if(!preview)visualClock.Start();
        reconnect.Checked+=(_,_)=>SavePreferences();reconnect.Unchecked+=(_,_)=>SavePreferences();
        ((StackPanel)DiagnosticsHost.Content).Children.Add(new TextBlock{Text="Bağlan: Windows IPv4 internetini USB üzerinden aktarır. IPv6 kaçak koruması etkin. Günlük modda uygulama süre/veri sınırı yoktur.",Foreground=UiTheme.Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,20)});
        trafficPanel.ApplySettings=async settings=>{if(systemNetwork?.Active==true)await ApplyLiveProfile(settings);else if(engine!=null&&!engine.HasExited){await engine.StandardInput.WriteLineAsync(JsonSerializer.Serialize(settings));await engine.StandardInput.FlushAsync();}SavePreferences(settings);};
        AnalysisHost.Content=trafficPanel;
        ((StackPanel)DiagnosticsHost.Content).Children.Add(new TextBlock{Text="Standart USB tethering kapatma işlemi yalnızca doğrulanmış bağlantı ve güvenli geri dönüşle yapılmalı. Profil değişikliği canlı telemetriyle doğrulanır; hız tavanı ve adaptif ayar sonraki bağlantıda uygulanır. MyPublicWiFi/ICS otomatik yapılandırılmaz.",Foreground=UiTheme.Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
        ((StackPanel)DiagnosticsHost.Content).Children.Add(log);
        Loaded+=async(_,_)=>{if(preview)return;wantsDaily=!manualStart&&DailyReady&&reconnect.IsChecked==true;await Refresh();timer.Start();};
        timer.Tick+=async(_,_)=>{if(busy||connecting||refreshing||closing)return;sessionDetails.Text=((devices.SelectedItem as Device)?.Model??"Telefon bekleniyor")+" · USB · Profil: "+trafficPanel.Settings.Mode+(connectedAt.HasValue?" · Süre: "+(DateTime.UtcNow-connectedAt.Value).ToString(@"hh\:mm\:ss"):"");await Refresh();if(engine?.HasExited==true){Note("Motor durdu.");await Disconnect();}if(wantsDaily&&reconnect.IsChecked==true&&DateTime.UtcNow>=nextReconnect&&engine==null&&systemNetwork==null&&selectedSerial!=null&&devices.Items.Cast<Device>().Any(d=>d.Serial==selectedSerial))await ConnectSystemNetwork(false,true);};
        Closing+=async(_,e)=>{
            if(preview&&!lifecycleTest||allowClose)return;
            e.Cancel=true;if(closing)return;
            if(!exitRequested){Hide();if(!preview&&!hideNoticeSeen){tray.ShowBalloonTip(4000,"TetherMax","TetherMax arka planda çalışmaya devam ediyor 💗",System.Windows.Forms.ToolTipIcon.Info);hideNoticeSeen=true;SavePreferences();}return;}
            closing=true;timer.Stop();visualClock.Stop();
            try{await sharing.StopForExit();await Disconnect();allowClose=true;_=Dispatcher.BeginInvoke(()=>Close());}catch(Exception ex){closing=false;exitRequested=false;timer.Start();visualClock.Start();Show();Note("Güvenli çıkış tamamlanamadı: "+ex.Message);}
        };
        Navigation.SelectionChanged+=(_,e)=>{if(e.Source!=Navigation||!UiTheme.Animate)return;if(Navigation.SelectedContent is FrameworkElement page){page.BeginAnimation(OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(0.3,1,TimeSpan.FromMilliseconds(180)));}};
        if(!preview)Loaded+=(_,_)=>ShowWelcome();
    }
    void RequestExit(bool confirmed){if(!confirmed)return;exitRequested=true;Close();}
    internal void RunOfflineUiChecks(){
        void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        hideNoticeSeen=true;Show();Close();Require(!IsVisible&&!closing&&!exitRequested,"X must hide without beginning cleanup");
        Show();WindowState=WindowState.Normal;Require(IsVisible,"same window must reopen");
        for(int i=0;i<Navigation.Items.Count;i++){Navigation.SelectedIndex=i;UpdateLayout();}
        foreach(bool light in new[]{true,false,true}){UiTheme.SetLight(light);UpdateLayout();Require(UiTheme.Light==light,"theme switch");}
        foreach(var size in new[]{new Size(760,650),new Size(1120,850),new Size(1440,1000)}){Width=size.Width;Height=size.Height;UpdateLayout();Require(ActualWidth>=760,"responsive layout");}
        Require(UiTheme.Bytes(1_500_000)=="1.5 MB"||UiTheme.Bytes(1_500_000)=="1,5 MB","byte units");
        RequestExit(false);Require(IsVisible&&!exitRequested,"cancel must keep the app running");
        RequestExit(true);Require(closing&&allowClose,"confirmed exit must complete existing cleanup");
        var frame=new System.Windows.Threading.DispatcherFrame();Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
        Require(!IsVisible,"confirmed exit must close");
    }
    void ShowWelcome(){
        if(!UiTheme.Animate)return;
        var body=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
        body.Children.Add(new RomanceScene{Width=360,Height=180});body.Children.Add(new TextBlock{Text="TetherMax",FontSize=40,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center});body.Children.Add(new TextBlock{Text="Birlikte, hep bağlı ♡",FontSize=18,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,12,0,24)});
        var overlay=new Border{Background=UiTheme.Background,Child=body};System.Windows.Controls.Grid.SetColumnSpan(overlay,2);Shell.Children.Add(overlay);
        var skip=new Button{Content="Atla",Padding=new Thickness(24,12,24,12)};body.Children.Add(skip);
        var duration=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(UiTheme.Motion==2?1800:900)};
        void Dismiss(){duration.Stop();Shell.Children.Remove(overlay);}skip.Click+=(_,_)=>Dismiss();duration.Tick+=(_,_)=>Dismiss();Closed+=(_,_)=>duration.Stop();duration.Start();
        overlay.BeginAnimation(OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(0,1,TimeSpan.FromMilliseconds(200)));
    }
    sealed record Device(string Serial,string Model) { public override string ToString()=>Model+" · "+Serial; }
    sealed record Preferences(string? Serial,bool Reconnect,TrafficSettings? Traffic=null,bool LightTheme=true,int Motion=1,bool Transparency=true,bool HideNoticeSeen=false);
    void SavePreferences(TrafficSettings? settings=null){try{Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);File.WriteAllText(SettingsPath,System.Text.Json.JsonSerializer.Serialize(new Preferences(selectedSerial,reconnect.IsChecked==true,settings??trafficPanel.Settings,UiTheme.Light,UiTheme.Motion,UiTheme.Transparency,hideNoticeSeen)));}catch{Note("Tercihler kaydedilemedi.");}}
    Button Button(Panel parent,string label,Func<Task> handler)
    {
        var button=new Button{Content=label,Padding=new Thickness(16,10,16,10),Margin=new Thickness(0,0,8,8)};
        button.Click+=async(_,_)=>{button.IsEnabled=false;try{await handler();}catch(Exception ex){Note(ex.Message);}finally{button.IsEnabled=true;UpdateControls();}};parent.Children.Add(button);return button;
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
    async Task Refresh(bool duringConnect=false)
    {
        if(busy||(!duringConnect&&connecting)||refreshing)return;refreshing=true;refreshFinished=new(TaskCreationOptions.RunContinuationsAsynchronously);
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
                string model=bits.FirstOrDefault(b=>b.StartsWith("model:"))?.Substring(6)??"Android";model=model=="SM_A566B"?"Galaxy A56":model.Replace('_',' ');list.Add(new Device(bits[0],model));
            }
            authorizationRequired=unauthorized&&list.Count==0;
            devices.ItemsSource=list;devices.SelectedItem=list.FirstOrDefault(d=>d.Serial==old)??list.FirstOrDefault();
            if((engine!=null||systemNetwork!=null)&&!list.Any(d=>d.Serial==selectedSerial)){await Disconnect(true);Note("USB cihazı ayrıldı; yalnızca TetherMax bağlantısı kapatıldı.");}
            if(engine!=null||systemNetwork!=null) {
                string forwards=await Adb("forward","--list");
                bool Has(string? endpoint)=>endpoint!=null&&forwards.Split('\n').Any(line=>line.StartsWith(selectedSerial+" ")&&line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Contains(endpoint));
                if(!Has(pairForward)||!Has(dataForward)){await Disconnect(true);Note("TetherMax USB aktarım kanalı kayboldu; yeniden bağlantı bekleniyor.");}
            }
            if(engine==null&&systemNetwork==null&&!busy){
                string next=list.Count>0?"USB telefon hazır · bağlantı kapalı":unauthorized?"Telefon bulundu · ADB izni bekleniyor":"Yetkili USB telefonu bulunamadı";
                if(unauthorized&&list.Count==0&&state.Text!=next)Note("USB hata ayıklama açık olsa da bu bilgisayarın Android RSA izni gerekiyor. Uygulama izinleri veya USB bağlantısını sıfırlamaz.");
                state.Text=next;
            }
        }catch(Exception ex){if(log.Text.Length==0)Note(ex.Message);}
        finally{refreshing=false;refreshFinished?.TrySetResult(true);}
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
    async Task ConnectUsb(bool prepareAndroid=true)
    {
        if(busy||engine!=null||systemNetwork!=null)return;if(devices.SelectedItem is not Device device){Note("Yetkili USB telefonu bulunamadı. Telefonda USB hata ayıklama onayını kontrol edin.");return;}
        if(!File.Exists(EnginePath))throw new IOException("Go motoru paket içinde bulunamadı.");
        busy=true;Phase(ConnectionPhase.PhoneDetected,"Telefon algılandı · güvenli USB hazırlanıyor");selectedSerial=device.Serial;SavePreferences();session??=new CancellationTokenSource(TimeSpan.FromSeconds(100));state.Text="Telefon doğrulanıyor…";
        try{
            Phase(ConnectionPhase.CheckingAuthorization,"USB yetkisi doğrulandı");
            Phase(ConnectionPhase.PreparingAndroid,"Android servisinin hazır olması bekleniyor");
            string serviceState=await Adb("-s",device.Serial,"shell","dumpsys","activity","services","io.tethermax.app");
            if(prepareAndroid&&!serviceState.Contains(".TunnelService"))await Adb("-s",device.Serial,"shell","am","start","-n","io.tethermax.app/.MainActivity","--ez","prepare_connection","true");
            await Readiness.WaitFor(async()=>{string services=await Adb("-s",device.Serial,"shell","dumpsys","activity","services","io.tethermax.app");return services.Contains(".TunnelService");},session.Token);
            Phase(ConnectionPhase.Authenticating,"Güvenli eşleştirme doğrulanıyor");
            int pairPort=await Forward(device.Serial,"io.tethermax.pair.v2");pairForward="tcp:"+pairPort;
            string secret=await Readiness.Pair(pairPort,device.Serial,code=>Dispatcher.InvokeAsync(()=>MessageBox.Show(this,"Telefondaki numara aynı mı?\n\n"+code+"\n\nTelefonda da onaylayın.","Güvenli eşleştirme",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes).Task,session.Token);
            Phase(ConnectionPhase.OpeningTransport,"USB aktarım kanalı açılıyor");
            int dataPort=await Forward(device.Serial,"io.tethermax.usb.v1");dataForward="tcp:"+dataPort;
            socksPort=System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p=>p.Port==1088)?FreePort():1088;
            var start=new ProcessStartInfo(EnginePath){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"serve","--approve-usb-test","--pairing-stdin","--transport","127.0.0.1:"+dataPort,"--listen","127.0.0.1:"+socksPort})start.ArgumentList.Add(arg);
            foreach(var arg in new[]{"--traffic-control-stdin","--traffic-telemetry","--dns-profile",trafficPanel.Settings.DnsProfile,"--profile",trafficPanel.Settings.Mode,"--upload-mbps",trafficPanel.Settings.UploadMbps.ToString(CultureInfo.InvariantCulture),"--download-mbps",trafficPanel.Settings.DownloadMbps.ToString(CultureInfo.InvariantCulture)})start.ArgumentList.Add(arg);
            if(trafficPanel.Settings.Adaptive)start.ArgumentList.Add("--adaptive");
            Phase(ConnectionPhase.StartingEngine,"Ağ motoru hazırlanıyor");
            engine=Process.Start(start)??throw new IOException("Go motoru başlatılamadı.");engine.StandardInput.NewLine="\n";await engine.StandardInput.WriteLineAsync(secret);await engine.StandardInput.FlushAsync();secret="";
            _=ReadTelemetry(engine);
            await Readiness.WaitForListener(socksPort,()=>engine.HasExited,session.Token);
            state.Text="USB eşleşti · SOCKS hazır";Note("127.0.0.1:"+socksPort+" hazır. İnternet testi ile veri yolunu doğrulayın. Sistem geneli yönlendirme kapalı.");
        }catch(Exception ex){await Cleanup(wantsDaily);Note("Bağlantı: "+ex.Message);throw;}
        finally{busy=false;}
    }
    async Task Disconnect(bool preserveDaily=false)
    {
        if(!preserveDaily)userCanceled=true;session?.Cancel();
        await operations.WaitAsync();
        try { await Cleanup(preserveDaily); } finally { operations.Release(); }
    }
    async Task Cleanup(bool preserveDaily=false)
    {
        if(!preserveDaily)wantsDaily=false;
        if(systemNetwork!=null){await systemNetwork.DisposeAsync();systemNetwork=null;}
        session?.Cancel();session?.Dispose();session=null;
        if(engine!=null){try{if(!engine.HasExited){engine.Kill();await engine.WaitForExitAsync();}}catch{}engine.Dispose();engine=null;}
        if(selectedSerial!=null)foreach(var endpoint in new[]{pairForward,dataForward})if(endpoint!=null)try{await Adb("-s",selectedSerial,"forward","--remove",endpoint);}catch(Exception ex){Note("Forward temizliği: "+ex.Message);}
        connectedAt=null;sessionDetails.Text="Bağlantı kapalı";pairForward=null;dataForward=null;connection.Advance(ConnectionPhase.Idle,"Bağlantı kapalı");state.Text="Bağlantı kapalı";if(tray!=null)tray.Icon=new System.Drawing.Icon(Path.Combine(root,"Assets","waiting.ico"));
        trafficPanel.Reset();UpdateControls();
    }
    async Task ReleaseStoppedNetwork()
    {
        var stopped=systemNetwork;
        await operations.WaitAsync();
        try { if(stopped==null||!ReferenceEquals(systemNetwork,stopped))return;await Cleanup(wantsDaily); }
        finally { operations.Release(); }
    }
    async Task ConnectSystemNetwork(bool diagnostic=true,bool automatic=false)
    {
        if(closing){Stage("Uygulama kapanıyor; bağlantı başlatılamaz.");return;}
        if(connecting||busy){Stage("Bağlantı işlemi sürüyor · tamamlanmasını bekleyin veya İptal seçin.");return;}
        if(systemNetwork!=null){Stage(systemNetwork.Active?"TetherMax oturumu etkin · durum doğrulaması sürüyor.":"Önceki oturum kurtarılıyor · tamamlanınca yeniden deneyin.");return;}
        if(!automatic)reconnectAttempts=0;
        if(reconnectAttempts>=5){wantsDaily=false;reconnectAttempts=0;Stage("Otomatik denemeler durdu · yeniden Bağlan seçin");return;}
        connecting=true;userCanceled=false;connection.Begin();bool ownsOperation=false;
        session??=new CancellationTokenSource(TimeSpan.FromSeconds(100));
        try {
            Phase(ConnectionPhase.DetectingPhone,"Telefon algılanıyor");
            // Refresh may itself be awaiting cleanup. Never hold the operation
            // lock while waiting for that refresh to finish.
            if(refreshing&&refreshFinished!=null)await refreshFinished.Task.WaitAsync(session.Token);
            await operations.WaitAsync(session.Token);ownsOperation=true;
            await Readiness.WaitFor(async()=>{
                await Refresh(true);
                if(authorizationRequired){wantsDaily=false;throw new UnauthorizedAccessException("Telefondaki USB hata ayıklama yetkisini onaylayın.");}
                return devices.SelectedItem is Device;
            },session.Token);
            await ConnectSystemNetworkCore(diagnostic,automatic);
        }
        catch(Exception e) {
            if(e is UnauthorizedAccessException)wantsDaily=false;
            if(ownsOperation)await Cleanup(wantsDaily);else {session?.Cancel();session?.Dispose();session=null;}
            var phase=e is OperationCanceledException&&userCanceled?ConnectionPhase.Idle:ConnectionPhase.Failed;
            connection.Advance(phase,e.Message);
            Stage(e is OperationCanceledException?(userCanceled?"Bağlantı iptal edildi":"Hazır olma süresi aşıldı · yeniden deneyin"):"Bağlantı hatası · "+e.Message);
        }
        finally {connecting=false;UpdateControls();if(ownsOperation)operations.Release();}
    }
    async Task ConnectSystemNetworkCore(bool diagnostic,bool automatic=false)
    {
        if(busy||systemNetwork!=null)throw new IOException("Önceki bağlantı işlemi henüz tamamlanmadı.");
        if(diagnostic&&MessageBox.Show(this,"Bu ayrı fiziksel geçiş testidir. En fazla 120 saniye ve toplam 1 MiB relay payload bütçesiyle tüm IPv4 çıkışı TetherMax Wintun'a yönlendirilecek; dış IPv6 ve diğer adaptör çıkışları WFP ile engellenecek. Protokol ek yükleri, yeniden iletim ve Android isim çözümü bu payload sayacına dahil değildir; operatör faturası için kesin üst sınır değildir. TetherMax'e özel IP, iki /1 rota ve geçici DNS kuralı eklenecek. Ethernet 5 kapatılmaz. Resmî Wintun sürücüsü yüklenebilir; paylaşılan sürücü paketi test sonunda silinmez. Bağlantı başarısızsa mevcut internetiniz ve bu sohbet geçici kesilebilir. Ayrı kurtarma süreci ayarları geri alır.\n\nArka plan uygulamaları da bütçeyi kullanabilir. Operatör karşılaştırma deneyi ayrıca onaylanmalıdır.\n\nBu somut ağ değişikliklerine ve belirtilen veri bütçesine şimdi izin veriyor musunuz?","Ayrı sistem geneli ağ onayı",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes){userCanceled=true;throw new OperationCanceledException("Ağ değişikliği onayı verilmedi.");}
        wantsDaily=!diagnostic;nextReconnect=DateTime.UtcNow.AddSeconds(Readiness.BackoffSeconds(reconnectAttempts++));
        if(engine==null)await ConnectUsb(true);
        if(engine==null||selectedSerial==null||pairForward==null||dataForward==null)throw new IOException("USB eşleştirmesi kurulamadı; sistem ağı değiştirilmedi.");
        busy=true;wantsDaily=!diagnostic;
        try {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(session!.Token);timeout.CancelAfter(TimeSpan.FromSeconds(45));
            Phase(ConnectionPhase.AwaitingConsent,"Windows yönetici onayı bekleniyor");
            string secret=await Pairing.Connect(int.Parse(pairForward[4..]),selectedSerial,code=>Dispatcher.InvokeAsync(()=>MessageBox.Show(this,"Telefondaki numara aynı mı?\n"+code,"Güvenli eşleştirme",MessageBoxButton.YesNo)==MessageBoxResult.Yes).Task,timeout.Token);
            // Preserve the selected ADB forwards but stop the unbudgeted SOCKS
            // worker/adaptive probes before starting the bounded global test.
            if(engine!=null){if(!engine.HasExited){engine.Kill();await engine.WaitForExitAsync();}engine.Dispose();engine=null;}
            trafficPanel.Reset();
            cellularAvailability.Reset();var ownedNetwork=systemNetwork=new SystemNetwork();
            await systemNetwork.Start(root,"127.0.0.1:"+dataForward[4..],secret,message=>Dispatcher.BeginInvoke(()=>{
                Note(message);
                try{using var record=JsonDocument.Parse(message);string? type=record.RootElement.GetProperty("type").GetString();
                    if(type!="network-report"&&!ReferenceEquals(systemNetwork,ownedNetwork))return;
                    if(type=="network-channel-closed"){Phase(ConnectionPhase.Failed,"Ağ yardımcısı durdu · güvenli kurtarma bekleniyor");_ = ReleaseStoppedNetwork();}
                    if(type=="network-error"){Phase(ConnectionPhase.Failed,"Bağlantı hatası · "+record.RootElement.GetProperty("message").GetString());_ = ReleaseStoppedNetwork();}
                    if(type=="worker-ready")Phase(ConnectionPhase.ConfiguringWindows,"Wintun hazır · Windows ağı yapılandırılıyor");
                    if(type=="network-active")Phase(ConnectionPhase.CheckingInternet,"Wintun/USB etkin · internet doğrulanıyor");
                    if(type=="windows-connectivity") {
                        bool healthy=Readiness.Healthy(record.RootElement.GetProperty("result"));
                        if(healthy){reconnectAttempts=0;connectedAt=DateTime.UtcNow;Phase(ConnectionPhase.Connected,((devices.SelectedItem as Device)?.Model??"Telefon")+" bağlandı");}
                        else {Phase(ConnectionPhase.Failed,"İnternet doğrulanamadı · güvenli kurtarma bekleniyor");nextReconnect=DateTime.UtcNow.AddSeconds(Readiness.BackoffSeconds(reconnectAttempts++));_ = ReleaseStoppedNetwork();}
                        if(tray!=null)tray.Icon=new System.Drawing.Icon(Path.Combine(root,"Assets",healthy?"connected.ico":"error.ico"));
                    }
                    if(type=="ip-counters"&&record.RootElement.TryGetProperty("payload",out var payload)&&payload.TryGetProperty("type",out var payloadType)&&payloadType.GetString()=="traffic"){
                        var previousSettings=trafficPanel.Settings;trafficPanel.Accept(payload);if(previousSettings!=trafficPanel.Settings)SavePreferences();
                        string? cellular=payload.TryGetProperty("phoneNetwork",out var source)?source.GetString():null;
                        if(cellularAvailability.Observe(cellular)){Phase(ConnectionPhase.Failed,"Hücresel servis yanıt vermiyor · güvenli yeniden bağlantı bekleniyor");nextReconnect=DateTime.UtcNow.AddSeconds(Readiness.BackoffSeconds(reconnectAttempts++));_ = ReleaseStoppedNetwork();}
                    }
                    if(type=="network-stopped"){state.Text=record.RootElement.GetProperty("recovered").GetBoolean()?"Sistem geneli oturum durdu · ağ kurtarıldı":"Ağ kurtarması bekliyor · kurtarma gerekli";_ = ReleaseStoppedNetwork();}
                    if(type=="network-report"){
                        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","reports");Directory.CreateDirectory(folder);
                        string file=Path.Combine(folder,"native-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".json");
                        using var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.Read);byte[] bytes=Encoding.UTF8.GetBytes(message);output.Write(bytes);output.Flush(true);Note("Windows/Android ayrı sayaç raporu: "+file);
                    }
                }catch(JsonException){}catch(IOException e){Note("Yerel rapor kaydedilemedi: "+e.Message);}
            }),diagnostic?120:0,trafficPanel.Settings,session!.Token);secret="";
            Note(diagnostic?"Tanılama başladı · 120 saniye / 1 MiB · otomatik geri alma":"Günlük bağlantı başlatıldı · süre ve relay veri sınırı yok · durdurmak için Bağlantıyı kes");
        }catch(Exception error){if(error is System.ComponentModel.Win32Exception canceled&&canceled.NativeErrorCode==1223)wantsDaily=false;if(systemNetwork!=null){await systemNetwork.DisposeAsync();systemNetwork=null;}throw;}
        finally{busy=false;}
    }
    async Task ApplyLiveProfile(TrafficSettings settings)
    {
        if(!await operations.WaitAsync(0))throw new IOException("Bağlantı işlemi bitince profili tekrar seçin.");
        try {
            if(systemNetwork?.Active!=true)throw new IOException("Etkin güvenli bağlantı bekleniyor.");
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            profileApplied=new(TaskCreationOptions.RunContinuationsAsynchronously);pendingProfile=settings;
            await systemNetwork.UpdateTraffic(settings);
            await profileApplied.Task.WaitAsync(deadline.Token);
            Note("Mod, DNS ve hız ayarları canlı motor ve telefon tarafından doğrulandı.");
        } finally {pendingProfile=null;profileApplied=null;operations.Release();}
    }
    async Task ReadTelemetry(Process process)
    {
        try{while(await process.StandardOutput.ReadLineAsync() is string line){if(!ReferenceEquals(engine,process))return;try{using var data=JsonDocument.Parse(line);var value=data.RootElement;if(!value.TryGetProperty("type",out var kind))continue;if(kind.GetString()=="traffic"){string previous=trafficPanel.Settings.Mode;trafficPanel.Accept(value);if(previous!=trafficPanel.Settings.Mode)SavePreferences();}else if(kind.GetString()=="traffic-error")Note("Trafik ayarı: "+value.GetProperty("message").GetString());}catch(JsonException){}}}catch(Exception ex){if(ReferenceEquals(engine,process)&&!closing)Note("Telemetri: "+ex.Message);}
    }
    async Task Diagnose()
    {
        if(engine==null||busy||connecting){Note("Önce USB bağlantısı kurun.");return;}
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
        if(engine==null||busy||connecting||selectedSerial==null||pairForward==null||dataForward==null){Note("Önce yetkili USB bağlantısı kurun.");return;}
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
