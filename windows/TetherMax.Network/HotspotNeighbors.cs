using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
namespace TetherMax.Network;
// Read-only IP Helper ARP table. MIB_IPNETROW is six 4-byte words on x86/x64.
public static class HotspotNeighbors {
 [DllImport("iphlpapi.dll")]static extern uint GetIpNetTable(IntPtr table,ref uint size,bool sorted);
 public static Dictionary<string,string[]> Read(uint index){uint size=0;uint result=GetIpNetTable(IntPtr.Zero,ref size,false);if(result!=122)throw new Win32Exception((int)result);if(size<4||size>1048576)throw new IOException("Neighbor table size invalid");IntPtr memory=Marshal.AllocHGlobal((int)size);try{result=GetIpNetTable(memory,ref size,false);if(result!=0)throw new Win32Exception((int)result);byte[] data=new byte[size];Marshal.Copy(memory,data,0,(int)size);return Parse(data,index);}finally{Marshal.FreeHGlobal(memory);}}
 public static Dictionary<string,string[]> Parse(byte[] data,uint index){if(data.Length<4)throw new IOException("Neighbor table truncated");uint count=BitConverter.ToUInt32(data,0);if(count>(data.Length-4)/24)throw new IOException("Neighbor rows truncated");var rows=new List<(string Mac,string IP)>();for(int n=0;n<count;n++){int offset=4+24*n;uint length=BitConverter.ToUInt32(data,offset+4);if(BitConverter.ToUInt32(data,offset)!=index||length!=6||BitConverter.ToUInt32(data,offset+20) is <3 or >4)continue;string ip=new IPAddress(data.AsSpan(offset+16,4)).ToString();rows.Add((Convert.ToHexString(data.AsSpan(offset+8,6)),ip));}return rows.GroupBy(x=>x.Mac).ToDictionary(x=>x.Key,x=>x.Select(v=>v.IP).Distinct().ToArray());}
}
