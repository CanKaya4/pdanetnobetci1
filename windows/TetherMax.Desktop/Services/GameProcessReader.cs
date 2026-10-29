using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace TetherMax;

internal static class GameProcessReader {
 [DllImport("kernel32.dll",SetLastError=true)]static extern SafeProcessHandle OpenProcess(uint access,bool inherit,uint id);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(SafeProcessHandle process,uint flags,StringBuilder path,ref int length);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(SafeProcessHandle process,out long created,out long exited,out long kernel,out long user);
 internal static (FocusProcess[] Processes,int Unavailable) Scan(string[] selected){
  var scan=ScanObserved(selected);return (GameLibrary.Match(scan.Processes,selected),scan.Unavailable);
 }
 internal static (ObservedGame[] Processes,int Unavailable) ScanObserved(string[] selected){
  var found=new List<ObservedGame>();int unavailable=0;
  foreach(string name in selected.Select(p=>Path.GetFileNameWithoutExtension(p)).Distinct(StringComparer.OrdinalIgnoreCase))foreach(var process in Process.GetProcessesByName(name))using(process){
   try{using var handle=OpenProcess(0x1000,false,(uint)process.Id);if(handle.IsInvalid){unavailable++;continue;}
    var path=new StringBuilder(32768);int length=path.Capacity;
    if(!QueryFullProcessImageName(handle,0,path,ref length)||!GetProcessTimes(handle,out long created,out long exited,out _,out _)||created<=0||exited!=0){unavailable++;continue;}
    found.Add(new((uint)process.Id,(ulong)created,path.ToString()));
   }catch(Exception error)when(error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException){unavailable++;}
  }
  return (found.Where(p=>selected.Contains(GameLibrary.Normalize(p.Path),StringComparer.OrdinalIgnoreCase)).DistinctBy(p=>p.Pid).ToArray(),unavailable);
 }
}
