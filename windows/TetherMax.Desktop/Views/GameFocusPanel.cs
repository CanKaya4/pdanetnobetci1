using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace TetherMax;

internal sealed class GameFocusPanel:Border {
 readonly ListBox games=new(){MinHeight=75,MaxHeight=190,Margin=new Thickness(0,12,0,12)};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Foreground=UiTheme.Accent,Margin=new Thickness(0,10,0,0)};
 readonly TextBlock counters=new(){TextWrapping=TextWrapping.Wrap,Foreground=UiTheme.Muted};
 readonly TextBlock windowsStatus=new(){TextWrapping=TextWrapping.Wrap,Foreground=UiTheme.Accent,Margin=new Thickness(0,8,0,8)};
 readonly ListBox backgroundApps=new(){MinHeight=45,MaxHeight=120,Margin=new Thickness(0,8,0,8)};
 public GameLibrary Library{get;private set;}
 public bool Detected{get;private set;}
 public GameFocusPanel(bool preview=false){
  try{Library=preview?new(true,[]):GameLibrary.Read();}catch(Exception error){Diagnostics.Record("game-library-read",error);Library=new(false,[]);}
  Background=UiTheme.Surface;CornerRadius=new CornerRadius(20);Padding=new Thickness(20);Margin=new Thickness(0,0,0,20);
  foreach(var list in new[]{games,backgroundApps}){list.Background=UiTheme.Background;list.Foreground=UiTheme.Ink;list.BorderBrush=UiTheme.Muted;list.BorderThickness=new Thickness(.5);}
  var body=new StackPanel();Child=body;
  body.Children.Add(new TextBlock{Text="Oyun modu",Foreground=UiTheme.Ink,FontFamily=new System.Windows.Media.FontFamily("Georgia"),FontSize=28});
  body.Children.Add(new TextBlock{Text="Oyunlarını seç. Açıldıklarında otomatik etkinleşsin.",Foreground=UiTheme.Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,12)});
  var enabled=new CheckBox{Content="Seçtiğim oyunlarda otomatik etkinleştir",IsChecked=Library.Enabled};body.Children.Add(enabled);
  enabled.Click+=(_,_)=>{Library=Library with{Enabled=enabled.IsChecked==true};Save();};
  body.Children.Add(games);RefreshList();
  var actions=new WrapPanel();body.Children.Add(actions);
  void Action(string label,Action action,WrapPanel? host=null){var button=new Button{Content=label,Padding=new Thickness(14,9,14,9),Margin=new Thickness(0,0,10,8)};button.Click+=(_,_)=>{try{action();}catch(Exception error){status.Text=error.Message;Diagnostics.Record("game-library-action",error);}};(host??actions).Children.Add(button);}
  Action("Oyun .exe dosyası ekle",()=>{var picker=new Microsoft.Win32.OpenFileDialog{Filter="Oyun uygulaması (*.exe)|*.exe",CheckFileExists=true,Multiselect=false,Title="Oyunun gerçek çalışan .exe dosyasını seç"};if(picker.ShowDialog()==true){Library=Library.Add(picker.FileName);Save();}});
  Action("Seçileni kaldır",()=>{if(games.SelectedItem is string path){Library=Library with{Paths=Library.Paths.Where(p=>p!=path).ToArray()};Save();}});
  Action("Windows Oyun Modu",()=>Process.Start(new ProcessStartInfo("ms-settings:gaming-gamemode"){UseShellExecute=true}));
  Action("Grafik / GPU tercihleri",()=>Process.Start(new ProcessStartInfo("ms-settings:display-advancedgraphics"){UseShellExecute=true}));
  Action("Arka plan oyun kaydı",()=>Process.Start(new ProcessStartInfo("ms-settings:gaming-gamedvr"){UseShellExecute=true}));
  body.Children.Add(new TextBlock{Text="Başlatıcı yerine oyunun gerçek işlemini seçin. Örnek: VALORANT-Win64-Shipping.exe. Aynı isimli başka klasördeki program oyun sayılmaz.",TextWrapping=TextWrapping.Wrap,Foreground=UiTheme.Muted,Margin=new Thickness(0,8,0,8)});
  var tune=new CheckBox{Content="Windows süreç optimizasyonu · oyun açıkken otomatik",IsChecked=Library.WindowsTuning,Margin=new Thickness(0,12,0,8)};
  body.Children.Add(tune);tune.Click+=(_,_)=>{Library=Library with{WindowsTuning=tune.IsChecked==true};Save();};
  body.Children.Add(new TextBlock{Text="Oyun: normal üstü CPU önceliği ve güç kısıtlamasından çıkış. Aşağıda seçtiğin uygulamalar: düşük CPU önceliği ve EcoQoS. İşlem kapatılmaz. Oyun bitince veya seçenek kapatılınca eski ayarlar geri alınır. Erişim engellenirse atlanır; anti-cheat'e müdahale edilmez.",TextWrapping=TextWrapping.Wrap,Foreground=UiTheme.Muted});
  body.Children.Add(backgroundApps);
  var bgActions=new WrapPanel();body.Children.Add(bgActions);
  Action("Arka plan .exe ekle",()=>{var picker=new Microsoft.Win32.OpenFileDialog{Filter="Arka plan uygulaması (*.exe)|*.exe",CheckFileExists=true,Title="Oyun sırasında kaynak önceliği azaltılacak uygulama"};if(picker.ShowDialog()==true){Library=Library.AddBackground(picker.FileName);Save();}},bgActions);
  Action("Arka plan seçimini kaldır",()=>{if(backgroundApps.SelectedItem is string path){Library=Library with{BackgroundPaths=(Library.BackgroundPaths??[]).Where(p=>p!=path).ToArray()};Save();}},bgActions);
  var budgetLabel=new TextBlock{Foreground=UiTheme.Muted,Margin=new Thickness(0,10,0,4)};body.Children.Add(budgetLabel);
  var budget=new Slider{Minimum=.25,Maximum=10,Value=Library.BackgroundMbps,TickFrequency=.25,IsSnapToTickEnabled=true,Width=250,HorizontalAlignment=HorizontalAlignment.Left};body.Children.Add(budget);
  void BudgetLabel()=>budgetLabel.Text=$"Büyük arka plan aktarımlarının ortak tavanı: {budget.Value:0.##} Mbps / yön";
  BudgetLabel();budget.ValueChanged+=(_,_)=>{Library=Library with{BackgroundMbps=budget.Value};BudgetLabel();Save();};
  body.Children.Add(new TextBlock{Text="Trafik önceliği yalnızca TetherMax IP motorundan geçen ve sahibi doğrulanabilen akışlarda çalışır. Hız sınırı biliniyorsa arka plan bütçesi yaklaşık %15'e kadar düşürülür (en az 0,128 Mbps); DNS ve küçük paketler korunur. Başka bağlantı kullanan uygulamanın internet tüketimi bu ayarla sınırlanmaz.",TextWrapping=TextWrapping.Wrap,Foreground=UiTheme.Muted,Margin=new Thickness(0,8,0,8)});
  body.Children.Add(new TextBlock{Text="LoL: League of Legends.exe · VALORANT: VALORANT-Win64-Shipping.exe · Rust: RustClient.exe · Deadlock: gerçek çalışan oyun .exe'si. Oyun içi Reflex destekleniyorsa aç; FPS tavanını yoğun sahnelerde sürdürebildiğin değere göre ölç. Sürücü, sıcaklık, sunucu ve hücresel sinyal kaynaklı sıçramalar ayrıca incelenmelidir.",TextWrapping=TextWrapping.Wrap,Foreground=UiTheme.Muted});
  body.Children.Add(windowsStatus);
  body.Children.Add(status);body.Children.Add(counters);
  var help=new StackPanel();foreach(var block in body.Children.OfType<TextBlock>().Where(t=>t.Text.Length>100).ToArray()){body.Children.Remove(block);help.Children.Add(block);}
  body.Children.Add(new Expander{Header="Nasıl çalışır?",Content=help,Margin=new Thickness(0,16,0,0),Foreground=UiTheme.Muted});LocalScan([],0);
 }
 void RefreshList(){games.ItemsSource=Library.Paths;backgroundApps.ItemsSource=Library.BackgroundPaths??[];}
 public void WindowsStatus(string message)=>windowsStatus.Text=message;
 void Save(){Library.Save();RefreshList();LocalScan([],0);}
 public void LocalScan(FocusProcess[] processes,int unavailable){Detected=Library.Enabled&&processes.Length>0;status.Text=!Library.Enabled?"Oyun odağı kapalı":Library.Paths.Length==0?"Önce bir oyun .exe dosyası ekleyin":Detected?$"{processes.Length} oyun işlemi doğrulandı · aktarım motorunun onayı bekleniyor":unavailable>0?"Süreç bilgisine erişilemedi; öncelik uygulandığı varsayılmadı":"Oyun bekleniyor · normal trafik düzeni";}
 public void Accept(JsonElement data){if(!Library.Enabled||!Detected)return;bool active=data.TryGetProperty("gameFocusActive",out var value)&&value.ValueKind==JsonValueKind.True;status.Text=active?"Oyun odağı etkin · doğrulanmış süreç akışları öncelikli":"Oyun algılandı · akış sahipliği / IP motoru onayı bekleniyor";if(data.TryGetProperty("GamePackets",out var game)&&game.TryGetUInt64(out ulong g)&&data.TryGetProperty("BackgroundPackets",out var background)&&background.TryGetUInt64(out ulong b))counters.Text=$"Oturumda öncelikli oyun paketi: {g:N0} · bütçeden geçen arka plan paketi: {b:N0}";}
 public void DeliveryFailed(){status.Text="Oyun algılandı; politika motoruna iletilemedi. Genel bağlantı korunuyor.";}
}
