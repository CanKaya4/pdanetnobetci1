using System.IO;
namespace TetherMax;

internal sealed record ProcessTuning(uint Priority, uint Control, uint State);
internal sealed record TuningLease(ObservedGame Game, ProcessTuning Before, ProcessTuning Applied);
internal interface IGameTuning {
 ProcessTuning? Read(ObservedGame game);
 void Write(ObservedGame game, ProcessTuning tuning);
}

// Journal before mutation. Identity includes creation time and full image path;
// a recycled PID must never inherit another process's restore operation.
internal sealed class GameOptimization {
 readonly IGameTuning native;
 readonly Action<TuningLease[]> persist;
 readonly List<TuningLease> leases;
 public int Pending => leases.Count;
 public GameOptimization(IGameTuning native, TuningLease[] saved, Action<TuningLease[]> persist) {
  if(saved.Length>256||saved.Any(l=>l==null||l.Game==null||l.Before==null||l.Applied==null||l.Game.Pid<=4||l.Game.Started==0||string.IsNullOrWhiteSpace(l.Game.Path)))throw new IOException("Geçersiz oyun optimizasyonu kurtarma günlüğü.");
  this.native=native;this.persist=persist;leases=saved.ToList();
 }
 internal static TuningLease[] ReadJournal(){
  var saved=SettingsFile.Read<TuningLease[]>(JournalPath);
  if(saved==null&&(File.Exists(JournalPath)||File.Exists(JournalPath+".previous")))throw new IOException("Oyun kurtarma günlüğü okunamadı; yeni süreç ayarı uygulanmayacak.");
  return saved??[];
 }
 void Commit()=>persist(leases.ToArray());
 public string Update(ObservedGame[] games, ObservedGame[] background) {
  var desired=new Dictionary<ObservedGame,bool>();
  foreach(var game in background)desired[game]=false;
  foreach(var game in games)desired[game]=true;
  int denied=0;
  foreach(var lease in leases.ToArray()) {
   if(desired.TryGetValue(lease.Game,out bool gaming)&&Target(lease.Before,gaming)==lease.Applied)continue;
   if(!Restore(lease))denied++;
  }
  foreach(var entry in desired) {
   if(leases.Any(l=>l.Game==entry.Key))continue;
   try {
    var before=native.Read(entry.Key);if(before==null)continue;
    var applied=Target(before,entry.Value);if(before==applied)continue;
    var lease=new TuningLease(entry.Key,before,applied);leases.Add(lease);
    try {Commit();}catch {leases.Remove(lease);throw;}
    native.Write(entry.Key,applied);
   }catch(Exception error){denied++;Diagnostics.Record("game-tuning",error);}
  }
  int verified=0;
  foreach(var lease in leases)try{if(native.Read(lease.Game)==lease.Applied)verified++;}catch{ }
  return $"Windows: {verified} doğrulanmış süreç ayarı · {leases.Count-verified} bekleyen · erişim/geri alma sorunu: {denied}";
 }
 internal static ProcessTuning Target(ProcessTuning before,bool gaming)=>before with {
  // Preserve an existing High/Realtime class; never escalate to either.
  Priority=gaming ? (before.Priority is 0x80 or 0x100 ? before.Priority : 0x8000u) : (before.Priority==0x40?0x40u:0x4000u),
  Control=before.Control|1u,State=gaming ? before.State&~1u : before.State|1u
 };
 bool Restore(TuningLease lease) {
  try {
   var current=native.Read(lease.Game);
   if(current!=null) {
    // Respect independent changes made by the game, user or another tool.
    var restored=current with {
     Priority=current.Priority==lease.Applied.Priority?lease.Before.Priority:current.Priority,
     Control=(current.Control&1)==(lease.Applied.Control&1)&&(current.State&1)==(lease.Applied.State&1)?(current.Control&~1u)|(lease.Before.Control&1):current.Control,
     State=(current.Control&1)==(lease.Applied.Control&1)&&(current.State&1)==(lease.Applied.State&1)?(current.State&~1u)|(lease.Before.State&1):current.State
    };
    if(restored!=current)native.Write(lease.Game,restored);
   }
   leases.Remove(lease);
   try{Commit();}catch{leases.Add(lease);throw;}
   return true;
  }catch(Exception error){Diagnostics.Record("game-tuning-restore",error);return false;}
 }
 public void RestoreAll(){foreach(var lease in leases.ToArray())Restore(lease);}
 internal static string JournalPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","game-tuning-journal.json");
}
