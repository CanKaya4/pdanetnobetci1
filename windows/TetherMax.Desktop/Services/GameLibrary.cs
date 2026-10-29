using System.IO;
namespace TetherMax;

internal sealed record GameLibrary(bool Enabled,string[] Paths,bool WindowsTuning=false,string[]? BackgroundPaths=null,double BackgroundMbps=2) {
 internal static string FilePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","game-focus.json");
 internal static string Normalize(string path){if(path.StartsWith(@"\\?\",StringComparison.Ordinal))path=path[4..];return Path.GetFullPath(path);}
 internal static string ValidatePath(string path){
  string full=Normalize(path),windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(full.Length>512||full.Length<4||full[1]!=':'||!full.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||full.StartsWith(windows,StringComparison.OrdinalIgnoreCase)||!File.Exists(full)||(File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)throw new IOException("Yerel diskteki oyunun gerçek .exe dosyasını seçin; Windows sistem dosyaları eklenmez.");
  return full;
 }
 public static GameLibrary Read(){var saved=SettingsFile.Read<GameLibrary>(FilePath);if(saved==null)return new(true,[]);return saved with{Paths=Clean(saved.Paths),BackgroundPaths=Clean(saved.BackgroundPaths).Where(BackgroundAllowed).ToArray(),BackgroundMbps=double.IsFinite(saved.BackgroundMbps)?Math.Clamp(saved.BackgroundMbps,.25,10):2};}
 static string[] Clean(string[]? paths){var valid=new List<string>();foreach(string path in paths??[]){try{if(!string.IsNullOrWhiteSpace(path))valid.Add(ValidatePath(path));}catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){ }}return valid.Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();}
 internal static bool BackgroundAllowed(string path){
  string name=Path.GetFileNameWithoutExtension(path);
  // Connectivity, voice chat, launchers and anti-cheat must retain normal service.
  return !new[]{"TetherMax","TetherMax.Network","tethermax-engine","PdaNetPC","PdaNet","MyPublicWiFi","adb","Discord","Teamspeak","ts3client_win64","vgc","vgtray","RiotClientServices","LeagueClient","steam","steamservice","EasyAntiCheat","EasyAntiCheat_EOS","BEService","MsMpEng"}.Contains(name,StringComparer.OrdinalIgnoreCase)&&!name.StartsWith("TetherMax",StringComparison.OrdinalIgnoreCase);
 }
 public GameLibrary AddBackground(string path){string full=ValidatePath(path);if(!BackgroundAllowed(full)||Paths.Contains(full,StringComparer.OrdinalIgnoreCase))throw new IOException("Oyun, bağlantı, sesli sohbet ve anti-cheat uygulamaları arka plan listesine eklenmez.");var paths=BackgroundPaths??[];if(paths.Contains(full,StringComparer.OrdinalIgnoreCase))return this;if(paths.Length>=16)throw new IOException("En fazla 16 arka plan uygulaması eklenebilir.");return this with{BackgroundPaths=[..paths,full]};}
 public void Save()=>SettingsFile.Write(FilePath,this);
 public GameLibrary Add(string path){string full=ValidatePath(path);if(Paths.Contains(full,StringComparer.OrdinalIgnoreCase))return this;if(Paths.Length>=16)throw new IOException("En fazla 16 oyun eklenebilir.");return this with{Paths=[..Paths,full],BackgroundPaths=(BackgroundPaths??[]).Where(p=>!string.Equals(p,full,StringComparison.OrdinalIgnoreCase)).ToArray()};}
 internal static FocusProcess[] Match(IEnumerable<ObservedGame> running,IEnumerable<string> selected)=>running.Where(p=>p.Pid>4&&p.Started>0&&selected.Contains(Normalize(p.Path),StringComparer.OrdinalIgnoreCase)).GroupBy(p=>p.Pid).Select(g=>g.First()).OrderBy(p=>p.Pid).Take(16).Select(p=>new FocusProcess(p.Pid,p.Started)).ToArray();
}
internal sealed record ObservedGame(uint Pid,ulong Started,string Path);
