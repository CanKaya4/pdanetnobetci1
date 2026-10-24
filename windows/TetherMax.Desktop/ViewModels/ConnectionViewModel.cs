using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
namespace TetherMax;
internal enum ConnectionPhase { Idle,DetectingPhone,PhoneDetected,CheckingAuthorization,PreparingAndroid,Authenticating,OpeningTransport,StartingEngine,AwaitingConsent,ConfiguringWindows,CheckingInternet,Connected,Failed }
internal sealed class ConnectionViewModel : INotifyPropertyChanged {
 readonly Stopwatch elapsed=Stopwatch.StartNew();
 Guid attemptId=Guid.NewGuid();long previousMs;
 public void Begin(){attemptId=Guid.NewGuid();previousMs=0;elapsed.Restart();}
 public ConnectionPhase Phase {get;private set;}
 public void Advance(ConnectionPhase phase,string label) {Phase=phase;Status=label;Trace.WriteLine($"TetherMax phase={phase} elapsedMs={elapsed.ElapsedMilliseconds}");
  try {var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","reports");Directory.CreateDirectory(folder);var path=Path.Combine(folder,"connection-stages.jsonl");if(File.Exists(path)&&new FileInfo(path).Length>1048576)File.Move(path,path+".previous",true);File.AppendAllText(path,JsonSerializer.Serialize(new{timeUtc=DateTime.UtcNow,phase=phase.ToString(),attemptId,elapsedMs=elapsed.ElapsedMilliseconds,stageDurationMs=elapsed.ElapsedMilliseconds-previousMs})+Environment.NewLine);}catch(IOException){}catch(UnauthorizedAccessException){}
  previousMs=elapsed.ElapsedMilliseconds;
  PropertyChanged?.Invoke(this,new(nameof(Phase)));}
 string status="Telefon bekleniyor";
 public string Status { get=>status; set {status=value;PropertyChanged?.Invoke(this,new(nameof(Status)));} }
 public event PropertyChangedEventHandler? PropertyChanged;
}
