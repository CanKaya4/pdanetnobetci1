using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace TetherMax;

internal sealed class WindowsGameTuning:IGameTuning {
 [StructLayout(LayoutKind.Sequential)]struct Power {public uint Version,Control,State;}
 [DllImport("kernel32.dll",SetLastError=true)]static extern SafeProcessHandle OpenProcess(uint access,bool inherit,uint id);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(SafeProcessHandle h,out long created,out long exited,out long kernel,out long user);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(SafeProcessHandle h,uint flags,StringBuilder path,ref int length);
 [DllImport("kernel32.dll",SetLastError=true)]static extern uint GetPriorityClass(SafeProcessHandle h);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetPriorityClass(SafeProcessHandle h,uint priority);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessInformation(SafeProcessHandle h,int kind,ref Power power,uint size);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetProcessInformation(SafeProcessHandle h,int kind,ref Power power,uint size);
 static SafeProcessHandle? Open(ObservedGame game,bool write) {
  var h=OpenProcess(0x1000u|(write?0x200u:0u),false,game.Pid);
  if(h.IsInvalid){int error=Marshal.GetLastWin32Error();h.Dispose();if(error==87)return null;throw new Win32Exception(error);}
  try {
   if(!GetProcessTimes(h,out long created,out long exited,out _,out _))throw new Win32Exception(Marshal.GetLastWin32Error());
   if((ulong)created!=game.Started||exited!=0){h.Dispose();return null;}
   var path=new StringBuilder(32768);int length=path.Capacity;
   if(!QueryFullProcessImageName(h,0,path,ref length))throw new Win32Exception(Marshal.GetLastWin32Error());
   if(!string.Equals(GameLibrary.Normalize(path.ToString()),GameLibrary.Normalize(game.Path),StringComparison.OrdinalIgnoreCase)){h.Dispose();return null;}
   return h;
  }catch{h.Dispose();throw;}
 }
 public ProcessTuning? Read(ObservedGame game) {
  using var h=Open(game,false);if(h==null)return null;
  uint priority=GetPriorityClass(h);if(priority==0)throw new Win32Exception(Marshal.GetLastWin32Error());
  var p=new Power{Version=1};if(!GetProcessInformation(h,4,ref p,12))throw new Win32Exception(Marshal.GetLastWin32Error());
  return new(priority,p.Control,p.State);
 }
 public void Write(ObservedGame game,ProcessTuning tuning) {
  using var h=Open(game,true);if(h==null)return;
  // A failed second operation remains recoverable through the write-ahead journal.
  if(!SetPriorityClass(h,tuning.Priority))throw new Win32Exception(Marshal.GetLastWin32Error());
  var p=new Power{Version=1,Control=tuning.Control,State=tuning.State};
  if(!SetProcessInformation(h,4,ref p,12))throw new Win32Exception(Marshal.GetLastWin32Error());
 }
}
