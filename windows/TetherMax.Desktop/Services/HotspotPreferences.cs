using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace TetherMax;
internal sealed record HotspotPreferences(string Ssid,int Band,string ProtectedPassword){
 internal static string FilePath {get;set;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","hotspot.json");
 [System.Text.Json.Serialization.JsonIgnore]
 public string Password=>Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedPassword),null,DataProtectionScope.CurrentUser));
 public static HotspotPreferences? Read(){if(!File.Exists(FilePath))return null;var value=JsonSerializer.Deserialize<HotspotPreferences>(File.ReadAllText(FilePath))??throw new IOException("Kayıtlı hotspot ayarı okunamadı.");if(value.Band is <0 or >2||Encoding.UTF8.GetByteCount(value.Ssid) is <1 or >32)throw new IOException("Kayıtlı hotspot ayarı geçersiz.");return value;}
 public static bool Valid(string ssid,string password,int band)=>band is >=0 and <=2 && Encoding.UTF8.GetByteCount(ssid) is >=1 and <=32 && password.Length is >=8 and <=63 && password.All(c=>c is >=(char)32 and <=(char)126);
 public static void Save(string ssid,string password,int band){if(!Valid(ssid,password,band))throw new IOException("Ağ adı, parola veya bant geçersiz.");var value=new HotspotPreferences(ssid,band,Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password),null,DataProtectionScope.CurrentUser)));Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);string staged=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(staged,JsonSerializer.Serialize(value));File.Move(staged,FilePath,true);}
}
