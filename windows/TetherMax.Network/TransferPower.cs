using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace TetherMax.Network;

// Process-scoped power request: works across async continuations and is released
// by Windows on process termination. Display timeout and explicit sleep still work.
internal sealed class TransferPower : IDisposable {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
 struct Reason {public uint Version,Flags;[MarshalAs(UnmanagedType.LPWStr)]public string Text;}
 [DllImport("kernel32.dll",SetLastError=true)]static extern SafeFileHandle PowerCreateRequest(ref Reason reason);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool PowerSetRequest(SafeFileHandle handle,int type);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool PowerClearRequest(SafeFileHandle handle,int type);
 readonly SafeFileHandle handle;
 public TransferPower(){var reason=new Reason{Version=0,Flags=1,Text="TetherMax USB internet ve hotspot aktarımı"};handle=PowerCreateRequest(ref reason);if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());if(!PowerSetRequest(handle,1)){int error=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(error);}}
 public void Dispose(){if(!handle.IsClosed){PowerClearRequest(handle,1);handle.Dispose();}}
}
