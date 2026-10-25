using System.IO;
namespace TetherMax;

internal static class Diagnostics {
    private static readonly object Gate=new();
    // Deliberately omit messages, IPC data, settings and credentials.
    public static void Record(string operation,Exception error) {
        try {lock(Gate){
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TetherMax","reports");Directory.CreateDirectory(folder);
            string file=Path.Combine(folder,"stability.log");
            if(File.Exists(file)&&new FileInfo(file).Length>262144)File.Move(file,file+".previous",true);
            File.AppendAllText(file,$"{DateTimeOffset.UtcNow:O} {operation} {error.GetType().FullName} 0x{error.HResult:X8}{Environment.NewLine}{error.StackTrace}{Environment.NewLine}");
        }}catch(Exception) { /* A diagnostic write must never cause a second failure. */ }
    }
}
