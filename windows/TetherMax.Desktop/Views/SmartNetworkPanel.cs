using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace TetherMax;

internal sealed class SmartNetworkPanel : Border {
    readonly TextBlock usage=new(),doctor=new(),hosts=new(),storage=new();
    readonly TextBox budget=new(){Width=110};
    readonly string? path;
    UsageLedger ledger=new();
    UsageState? persisted;
    readonly SemaphoreSlim writes=new(1,1);
    DateTime lastSample=DateTime.MinValue;
    bool enabled=true;
    public event Action<string>? Warning;
    public SmartNetworkPanel(bool preview) {
        CornerRadius=new(18);Padding=new(20);Margin=new(0,0,0,16);Background=UiTheme.Surface;BorderBrush=UiTheme.Button;BorderThickness=new(1);
        var content=new StackPanel();Child=content;
        foreach(var t in new[]{usage,doctor,hosts,storage}){t.TextWrapping=TextWrapping.Wrap;t.Foreground=UiTheme.Muted;t.Margin=new(0,8,0,8);}
        content.Children.Add(new TextBlock{Text="DataShield · Network Doctor",FontSize=22,Foreground=UiTheme.Ink});
        content.Children.Add(usage);
        var row=new WrapPanel();row.Children.Add(new TextBlock{Text="Aylık bütçe · GB  ",VerticalAlignment=VerticalAlignment.Center});row.Children.Add(budget);
        var apply=new Button{Content="Bütçeyi kaydet",Margin=new(12,0,0,0)};row.Children.Add(apply);content.Children.Add(row);
        content.Children.Add(new TextBlock{Text="0 = uyarı kapalı. Takvim ayı UTC; 1 GB = 1 milyar bayt. Yalnızca gözlenen TetherMax relay verisi; operatör faturası veya telefonun diğer uygulamaları değildir. Windows ve Android sayaçlarını toplamayın. Uyarı interneti kesmez.",TextWrapping=TextWrapping.Wrap,Foreground=UiTheme.Muted,Margin=new(0,8,0,8)});
        content.Children.Add(storage);content.Children.Add(doctor);content.Children.Add(hosts);
        if(!preview){
            path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","usage-v1.json");
            try{var saved=SettingsFile.Read<UsageState>(path);if(saved==null&&(File.Exists(path)||File.Exists(path+".previous")))throw new IOException("Sayaç kaydı okunamadı.");ledger=new(saved);}
            catch(Exception error)when(error is IOException or ArgumentException or NullReferenceException){enabled=false;storage.Text="Veri geçmişi okunamadı; sayaç korunmak için duraklatıldı. "+error.Message;}
        }
        budget.Text=(ledger.State.Budget/1e9).ToString("0.###",CultureInfo.CurrentCulture);
        apply.IsEnabled=enabled;apply.Click+=async(_,_)=>{
            if(!double.TryParse(budget.Text,NumberStyles.Number,CultureInfo.CurrentCulture,out double gb)||!double.IsFinite(gb)||gb<0||gb>1_000_000){storage.Text="Bütçe 0–1.000.000 GB arasında olmalı.";return;}
            ledger.SetBudget((long)(gb*1e9));ShowUsage();await Flush();
        };
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};int ticks=0;
        timer.Tick+=async(_,_)=>{if(DateTime.UtcNow-lastSample>TimeSpan.FromSeconds(8))doctor.Text="Güncel motor telemetrisi yok; teşhis için bağlantı ve yeni ölçüm bekleniyor.";if(++ticks%6==0)await Flush(false);};
        if(!preview)timer.Start();
        ShowUsage();Disconnected();
    }
    void ShowUsage(){usage.Text=$"{(ledger.State.Month==""?"Henüz örnek yok":ledger.State.Month+" UTC")} · {ledger.State.Used/1e9:0.###} GB gözlendi"+(ledger.State.Budget>0?$" / {ledger.State.Budget/1e9:0.###} GB · %{100.0*ledger.State.Used/ledger.State.Budget:0.#}":"");if(ledger.Level>0)usage.Text+=$"\nBütçe uyarısı · %{ledger.Level} eşiği aşıldı.";}
    public void Disconnected(){lastSample=DateTime.MinValue;doctor.Text="Teşhis için güncel motor telemetrisi bekleniyor.";hosts.Text="Kaynak IP sayaçları · motor oturumu bekleniyor.";}
    static bool Counter(JsonElement data,string name,out ulong value){value=0;return data.TryGetProperty(name,out var item)&&item.ValueKind==JsonValueKind.Number&&item.TryGetUInt64(out value);}
    public void Accept(JsonElement data) {
        if(data.ValueKind!=JsonValueKind.Object)return;
        lastSample=DateTime.UtcNow;doctor.Text=NetworkDoctor.Explain(data);
        if(enabled&&data.TryGetProperty("sessionId",out var id)&&id.ValueKind==JsonValueKind.String&&Counter(data,"sequence",out var sequence)&&Counter(data,"Uploaded",out var uploaded)&&Counter(data,"Downloaded",out var downloaded)){
            int old=ledger.Level;string month=ledger.State.Month;
            if(ledger.Accept(id.GetString()!,sequence,uploaded,downloaded,DateTime.UtcNow)){ShowUsage();if(ledger.Level>0&&(month!=ledger.State.Month||ledger.Level>old))Warning?.Invoke($"DataShield · aylık bütçenin %{ledger.Level} eşiği aşıldı.");}
        }
        hosts.Text="Kaynak IP sayaçları · bu motor oturumunda kuyruktan geçirilen IPv4 baytları (başlıklar ve yeniden iletim dahil; relay sayacıyla toplanmaz):";
        if(data.TryGetProperty("hostUsage",out var items)&&items.ValueKind==JsonValueKind.Array){
            foreach(var item in items.EnumerateArray().Take(16))if(item.ValueKind==JsonValueKind.Object&&item.TryGetProperty("address",out var address)&&address.ValueKind==JsonValueKind.String&&Counter(item,"uploaded",out var ub)&&Counter(item,"downloaded",out var db))hosts.Text+=$"\n{address.GetString()}  ↑ {ub/1e6:0.###} / ↓ {db/1e6:0.###} MB";
            if(items.GetArrayLength()>16)hosts.Text+=$"\nİlk 16 / {items.GetArrayLength()} adres gösteriliyor.";
        }
        if(Counter(data,"otherHostBytes",out var ob)&&ob>0)hosts.Text+=$"\nAdres tablosu sınırı dışındaki trafik: {ob/1e6:0.###} MB";
    }
    internal void VerifyOffline(){
        if(path!=null)throw new InvalidOperationException("Offline fixture requires preview storage.");
        ledger=new();ledger.SetBudget(1000);int warnings=0;Action<string> count=_=>warnings++;Warning+=count;
        try{
            foreach(string sample in new[]{"{\"sessionId\":\"fixture\",\"sequence\":1,\"Uploaded\":400,\"Downloaded\":400}","{\"sessionId\":\"fixture\",\"sequence\":1,\"Uploaded\":400,\"Downloaded\":400}","{\"sessionId\":\"fixture\",\"sequence\":2,\"Uploaded\":500,\"Downloaded\":500}","{\"sessionId\":\"fixture\",\"sequence\":\"bad\",\"Uploaded\":500,\"Downloaded\":500,\"hostUsage\":[null,{\"address\":3}],\"otherHostBytes\":\"bad\"}","null"}){using var doc=JsonDocument.Parse(sample);Accept(doc.RootElement);}
            if(ledger.State.Used!=1000||warnings!=2)throw new InvalidOperationException("Usage UI duplication/notification regression");
        }finally{Warning-=count;ledger=new();ShowUsage();Disconnected();}
    }
    public async Task Flush(bool wait=true){
        if(path==null||!enabled)return;
        if(wait)await writes.WaitAsync();else if(!await writes.WaitAsync(0))return;
        try{var state=ledger.State;if(ReferenceEquals(state,persisted))return;await Task.Run(()=>SettingsFile.Write(path,state));persisted=state;storage.Text="Yerel sayaç kaydedildi. Ani kapanışta son 30 saniyenin henüz kaydedilmemiş örnekleri eksik kalabilir; telemetri boşlukları tahmin edilmez.";}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){storage.Text="Sayaç diske kaydedilemedi; bağlantı devam ediyor. "+error.Message;}
        finally{writes.Release();}
    }
}
