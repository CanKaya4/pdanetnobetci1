using System.IO;
using System.Text.Json;
namespace TetherMax;

internal static class SettingsFile {
    public static T? Read<T>(string path) {
        foreach(string candidate in new[]{path,path+".previous"}) {
            if(!File.Exists(candidate))continue;
            try {if(new FileInfo(candidate).Length>1048576)throw new IOException("Settings too large");return JsonSerializer.Deserialize<T>(File.ReadAllText(candidate))??throw new JsonException();}
            catch(Exception error)when(error is IOException or JsonException or UnauthorizedAccessException){Diagnostics.Record("settings-read",error);}
        }
        return default;
    }
    public static void Write<T>(string path,T value) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string staged=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(var stream=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                JsonSerializer.Serialize(stream,value);stream.Flush(true);
            }
            // Only keep a parseable previous document. A corrupt main file must
            // not overwrite the last good fallback after a successful recovery.
            if(File.Exists(path))try{using var valid=JsonDocument.Parse(File.ReadAllText(path));File.Copy(path,path+".previous",true);}catch(JsonException){}
            File.Move(staged,path,true);
        }finally{if(File.Exists(staged))File.Delete(staged);}
    }
}
