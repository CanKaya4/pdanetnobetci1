using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TetherMax;
internal sealed class TrafficPanel : Border
{
    sealed record ModeChoice(string Id,string Label){public override string ToString()=>Label;}
    static readonly ModeChoice[] Modes=[new("auto","Otomatik"),new("gaming","Oyun"),new("streaming","Film / Dizi"),new("daily","Günlük"),new("maximum","Maksimum Hız")];
    readonly ComboBox mode=new(){Width=160,Margin=new Thickness(0,0,12,8)};
    readonly TextBox upload=new(){Text="0",Width=70,Margin=new Thickness(0,0,12,8)};
    readonly TextBox download=new(){Text="0",Width=70,Margin=new Thickness(0,0,12,8)};
    readonly CheckBox adaptive=new(){Content="Adaptif ölçüm ve şekillendirme",Foreground=UiTheme.Ink,Margin=new Thickness(0,8,12,8)};
    readonly TextBlock status=new(){Foreground=UiTheme.Accent,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
    readonly TextBlock metrics=new(){Foreground=UiTheme.Ink,TextWrapping=TextWrapping.Wrap,FontSize=15};
    readonly Canvas speed=new(){Height=110,Margin=new Thickness(0,12,0,0)};
    readonly Canvas delay=new(){Height=90,Margin=new Thickness(0,8,0,0)};
    readonly List<(double? First,double? Second)> rates=[],delays=[];
    bool loading,applying;
    readonly ComboBox dns=new(){ItemsSource=new[]{"AdGuard · reklam / izleyici filtresi","AdGuard Aile","Cloudflare","Özgün DNS"},SelectedIndex=0,MinWidth=240,Margin=new Thickness(0,0,12,8)};
    static readonly string[] DnsIds={"adguard","family","cloudflare","system"};
    public event Action? StatusChanged;
    public bool IsApplying=>applying;
    public string StatusText=>status.Text;
    void Status(string text){status.Text=text;StatusChanged?.Invoke();}
    public async Task SelectDns(string id){if(applying)return;loading=true;try{dns.SelectedIndex=Array.IndexOf(DnsIds,id);}finally{loading=false;}await Apply();}
    int historyMinutes=1;readonly List<DateTime> samples=[];
    List<(double? First,double? Second)> Recent(List<(double? First,double? Second)> values) {var cutoff=DateTime.UtcNow.AddMinutes(-historyMinutes);return values.Where((_,i)=>i<samples.Count&&samples[i]>=cutoff).ToList();}
    public TrafficSettings Settings{get;private set;}=new();
    public Action? MetricsReset {get;set;}
    public Action<JsonElement>? MetricsUpdated {get;set;}
    public async Task SelectMode(string id) { if(applying)return; loading=true;try{mode.SelectedItem=Modes.First(m=>m.Id==id);}finally{loading=false;}await Apply(); }
    public Func<TrafficSettings,Task>? ApplySettings{get;set;}
    public TrafficPanel()
    {
        CornerRadius=new CornerRadius(18);Padding=new Thickness(20);Margin=new Thickness(0,0,0,20);Background=UiTheme.Surface;
        var panel=new StackPanel();Child=panel;
        panel.Children.Add(new TextBlock{Text="TRAFİK VE CANLI ANALİZ",Foreground=UiTheme.Accent,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,12)});
        var row=new WrapPanel();panel.Children.Add(row);
        void Field(string label,FrameworkElement control){var col=new StackPanel{Margin=new Thickness(0,0,16,8),MinWidth=135};col.Children.Add(new TextBlock{Text=label,Foreground=UiTheme.Muted,TextWrapping=TextWrapping.Wrap,MaxWidth=155,Margin=new Thickness(0,0,0,4)});col.Children.Add(control);row.Children.Add(col);}
        mode.ItemsSource=Modes;mode.SelectedIndex=0;
        Field("Profil",mode);Field("DNS",dns);Field("Yükleme tavanı · Mbps",upload);Field("İndirme tavanı · Mbps",download);
        row.Children.Add(adaptive);var apply=new Button{Content="Uygula",Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,0,0,8)};row.Children.Add(apply);
        panel.Children.Add(new TextBlock{Text="0 = tavan yok. Bufferbloat kontrolü için ölçtüğünüz hızların yaklaşık %90’ını girip Adaptif’i açın. Hücresel ölçümler mobil veri kullanır. Seçim yalnızca TetherMax'ten geçen trafiği etkiler.",Foreground=UiTheme.Muted,TextWrapping=TextWrapping.Wrap,FontSize=12});
        var range=new ComboBox{ItemsSource=new[]{"1 dakika","5 dakika","30 dakika"},SelectedIndex=0,Width=150,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,8)};
        range.SelectionChanged+=(_,_)=>{historyMinutes=new[]{1,5,30}[range.SelectedIndex];Draw(speed,Recent(rates));Draw(delay,Recent(delays));};panel.Children.Add(range);
        panel.Children.Add(status);panel.Children.Add(metrics);panel.Children.Add(speed);
        panel.Children.Add(new TextBlock{Text="Mavi indirme · yeşil yükleme / Mbps · seçilen zaman aralığındaki gerçek örnekler",Foreground=UiTheme.Muted,FontSize=12});
        panel.Children.Add(delay);panel.Children.Add(new TextBlock{Text="Mavi DNS gidiş-dönüş · yeşil jitter / ms. Yanıtsız DNS oranı en az 20 örnekle gösterilir. Kuyruk yüzdelikleri TetherMax bekleme süresidir.",Foreground=UiTheme.Muted,TextWrapping=TextWrapping.Wrap,FontSize=12});
        apply.Click+=async(_,_)=>await Apply();mode.SelectionChanged+=async(_,_)=>{if(!loading)await Apply();};dns.SelectionChanged+=async(_,_)=>{if(!loading)await Apply();};
        speed.SizeChanged+=(_,_)=>Draw(speed,Recent(rates));delay.SizeChanged+=(_,_)=>Draw(delay,Recent(delays));Reset();
    }
    async Task Apply()
    {
        if(applying)return;
        bool Parse(TextBox box,out double value)=>double.TryParse(box.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out value)||double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out value);
        if(!Parse(upload,out double up)||!Parse(download,out double down)||!double.IsFinite(up)||!double.IsFinite(down)||up<0||down<0||up>10000||down>10000){Status("Tavanlar 0–10000 Mbps arasında olmalı.");return;}
        if(adaptive.IsChecked==true&&up==0){Status("Adaptif kontrol için ölçtüğünüz yükleme hızının yaklaşık %90’ını tavan olarak girin.");return;}
        if(mode.SelectedItem is not ModeChoice choice)return;
        var next=new TrafficSettings(choice.Id,up,down,adaptive.IsChecked==true,DnsIds[Math.Max(0,dns.SelectedIndex)]);
        applying=true;mode.IsEnabled=dns.IsEnabled=upload.IsEnabled=download.IsEnabled=adaptive.IsEnabled=false;Status("Ayarlar uygulanıyor…");
        try{if(ApplySettings!=null)await ApplySettings(next);Settings=next;Status("Seçim uygulandı · "+choice.Label);}
        catch(Exception ex){Load(Settings);Status("Uygulanamadı: "+(ex is OperationCanceledException?"Telefon onayı zaman aşımına uğradı. Bağlantı durumunu kontrol edin.":ex.Message));}
        finally{applying=false;mode.IsEnabled=dns.IsEnabled=upload.IsEnabled=download.IsEnabled=adaptive.IsEnabled=true;StatusChanged?.Invoke();}
    }
    public void Load(TrafficSettings settings)
    {
        loading=true;try{Settings=settings;mode.SelectedItem=Modes.FirstOrDefault(m=>m.Id==settings.Mode)??Modes[0];upload.Text=settings.UploadMbps.ToString(CultureInfo.CurrentCulture);download.Text=settings.DownloadMbps.ToString(CultureInfo.CurrentCulture);adaptive.IsChecked=settings.Adaptive;dns.SelectedIndex=Math.Max(0,Array.IndexOf(DnsIds,settings.DnsProfile));}finally{loading=false;}
    }
    public void Reset(){MetricsReset?.Invoke();rates.Clear();delays.Clear();samples.Clear();status.Text="Motor bağlantısı bekleniyor. Sistem geneli yönlendirme kapalı.";metrics.Text="Henüz hız veya gecikme ölçümü yok.";Draw(speed,Recent(rates));Draw(delay,Recent(delays));}
    static double? Number(JsonElement data,string name)=>data.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.Number?v.GetDouble():null;
    static string F(double? value,string format="F2")=>value.HasValue?value.Value.ToString(format,CultureInfo.CurrentCulture):"—";
    public void Accept(JsonElement data)
    {
        MetricsUpdated?.Invoke(data);
        string requested=data.GetProperty("requested").GetString()??"auto",active=data.GetProperty("active").GetString()??"daily";
        if(!applying&&data.TryGetProperty("settings",out var observed)){var current=JsonSerializer.Deserialize<TrafficSettings>(observed);if(current!=null&&current!=Settings)Load(current);}
        string label=Modes.FirstOrDefault(m=>m.Id==active)?.Label??active;
        string network=data.TryGetProperty("phoneNetwork",out var n)?n.GetString()??"unavailable":"unavailable";
        if(!applying)status.Text=$"Etkin: {label} · Telefon: {network}\nHız tavanı ↑ {F(Number(data,"UploadCeilingMbps"))} / ↓ {F(Number(data,"DownloadCeilingMbps"))} Mbps (0 = tavan yok)";
        if(!applying&&active=="maximum")status.Text+=" · Adaptif azaltma kapalı";
        metrics.Text=$"↓ {F(Number(data,"DownloadMbps"),"F3")} / ↑ {F(Number(data,"UploadMbps"),"F3")} Mbps · Akış {F(Number(data,"ActiveFlows"),"F0")} · Bekleyen {F(Number(data,"Pending"),"F0")}\n"+
            $"Aktarılan ↓ {F(Number(data,"Downloaded"),"F0")} / ↑ {F(Number(data,"Uploaded"),"F0")} B · DNS RTT {F(Number(data,"QueryRTTMs"))} ms · Jitter {F(Number(data,"JitterMs"))} ms\n"+
            $"DNS yanıtsız %{F(Number(data,"ProbeLossPercent"))} / {F(Number(data,"ProbeSamples"),"F0")} örnek · Kuyruk P50/P95/P99: {F(Number(data,"QueueP50Ms"))}/{F(Number(data,"QueueP95Ms"))}/{F(Number(data,"QueueP99Ms"))} ms";
        StatusChanged?.Invoke();
        samples.Add(DateTime.UtcNow);
        rates.Add((Number(data,"DownloadMbps"),Number(data,"UploadMbps")));delays.Add((Number(data,"QueryRTTMs"),Number(data,"JitterMs")));
        if(rates.Count>1800){rates.RemoveAt(0);delays.RemoveAt(0);samples.RemoveAt(0);}Draw(speed,Recent(rates));Draw(delay,Recent(delays));
    }
    static void Draw(Canvas canvas,List<(double? First,double? Second)> points)
    {
        canvas.Children.Clear();double width=canvas.ActualWidth;if(width<=0||points.Count==0)return;
        double[] values=points.SelectMany(p=>new[]{p.First,p.Second}).Where(v=>v.HasValue&&double.IsFinite(v.Value)).Select(v=>v!.Value).ToArray();
        if(values.Length==0){canvas.Children.Add(new TextBlock{Text="Ölçüm bekleniyor",Foreground=UiTheme.Muted,FontSize=11});return;}
        double observedMax=values.Max(),max=Math.Max(observedMax,0.01);
        var scale=new TextBlock{Text=$"0 – {observedMax:F2}",Foreground=UiTheme.Muted,FontSize=11};canvas.Children.Add(scale);
        for(int series=0;series<2;series++){
            Polyline? line=null;
            for(int i=0;i<points.Count;i++){
                double? value=series==0?points[i].First:points[i].Second;
                if(!value.HasValue||!double.IsFinite(value.Value)){line=null;continue;}
                if(line==null){line=new Polyline{Stroke=series==0?Brushes.DeepSkyBlue:UiTheme.Accent,StrokeThickness=2};canvas.Children.Add(line);}
                line.Points.Add(new Point(points.Count<2?0:(double)i/(points.Count-1)*width,canvas.Height-8-value.Value/max*(canvas.Height-30)));
            }
        }
    }
}
