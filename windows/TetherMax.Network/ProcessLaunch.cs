using System.Runtime.InteropServices;

namespace TetherMax.Network;

public static class ProcessLaunch
{
    [DllImport("kernel32.dll")]static extern IntPtr GetStdHandle(int kind);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetHandleInformation(IntPtr handle,uint mask,uint flags);
    // Process.Start on Windows inherits handles, even when new standard pipes
    // are supplied. Do not let grandchildren retain the guardian/CI output
    // handles or its parent-EOF channel after the actual owner has exited.
    // This changes only this process's handle-inheritance flags, not OS policy.
    public static void DetachStandardHandleInheritance()
    {
        foreach(int kind in new[]{-10,-11,-12}) {
            IntPtr handle=GetStdHandle(kind);
            if(handle!=IntPtr.Zero&&handle!=new IntPtr(-1))
                if(!SetHandleInformation(handle,1,0))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
