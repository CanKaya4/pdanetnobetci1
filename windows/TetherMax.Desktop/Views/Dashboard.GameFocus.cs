using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;
namespace TetherMax;

internal sealed partial class Dashboard {
 readonly DispatcherTimer gameClock=new(){Interval=TimeSpan.FromSeconds(2)};
 GameFocusPanel gamePanel=null!;
 GameFocusSettings currentFocus=new(true,[]);
 bool pollingFocus;
 GameOptimization? gameOptimization;
 void InitializeGameFocus(bool preview){
  gamePanel=new GameFocusPanel(preview);ModesHost.Children.Insert(0,gamePanel);
  gameClock.Tick+=async(_,_)=>await PollGameFocus();
  if(!preview)Loaded+=(_,_)=>{
   try{gameOptimization=new GameOptimization(new WindowsGameTuning(),GameOptimization.ReadJournal(),leases=>SettingsFile.Write(GameOptimization.JournalPath,leases));gameOptimization.RestoreAll();}
   catch(Exception error){Diagnostics.Record("game-tuning-recovery",error);gamePanel.WindowsStatus("Windows optimizasyonu başlatılamadı; kurtarma günlüğünü kontrol edin.");}
   gameClock.Start();
  };
  Closed+=(_,_)=>{gameClock.Stop();gameOptimization?.RestoreAll();};
 }
 async Task PollGameFocus(){
  if(pollingFocus||closing)return;pollingFocus=true;
  try {
   var selected=gamePanel.Library;
   var observed=selected.Enabled&&selected.Paths.Length>0?await Task.Run(()=>GameProcessReader.ScanObserved(selected.Paths)):(Array.Empty<ObservedGame>(),0);
   if(!ReferenceEquals(selected,gamePanel.Library)||closing)return;
   var scan=(GameLibrary.Match(observed.Item1,selected.Paths),observed.Item2);
   if(gameOptimization!=null){
    bool tune=selected.Enabled&&selected.WindowsTuning&&scan.Item1.Length>0;
    var background=tune?await Task.Run(()=>GameProcessReader.ScanObserved((selected.BackgroundPaths??[]).Where(GameLibrary.BackgroundAllowed).Except(selected.Paths,StringComparer.OrdinalIgnoreCase).ToArray())):(Array.Empty<ObservedGame>(),0);
    if(!ReferenceEquals(selected,gamePanel.Library)||closing)return;
    gamePanel.WindowsStatus(gameOptimization.Update(tune?observed.Item1:[],background.Item1)+(background.Item2>0?$" · {background.Item2} arka plan süreci okunamadı":""));
   }
   bool wasActive=(currentFocus.Processes?.Length??0)>0;
   var next=new GameFocusSettings(selected.Enabled,scan.Item1,selected.BackgroundMbps);
   bool changed=next!=currentFocus;currentFocus=next;gamePanel.LocalScan(scan.Item1,scan.Item2);
   UiTheme.GameSession=gamePanel.Detected;trafficPanel.VisualsPaused=gamePanel.Detected;mascot.PauseForGame(gamePanel.Detected);
   if(!changed&&!wasActive&&scan.Item1.Length==0)return;
   if(connecting||busy||trafficPanel.IsApplying||pendingProfile!=null||!await operations.WaitAsync(0))return;
   try {
    var settings=trafficPanel.Settings with{Focus=currentFocus};
    if(systemNetwork?.Active==true)await systemNetwork.UpdateTraffic(settings);
    else if(engine!=null&&!engine.HasExited){using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(2));await engine.StandardInput.WriteLineAsync(JsonSerializer.Serialize(settings).AsMemory(),deadline.Token);}
   }finally{operations.Release();}
  }catch(Exception error){Diagnostics.Record("game-focus-poll",error);gamePanel.DeliveryFailed();}
  finally{pollingFocus=false;}
 }
}
