using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TetherMax.Network;

// Win32 ABI from Microsoft's fwpmtypes.h/fwptypes.h. Only this class opens BFE.
// Persistent filters intentionally survive guardian/watchdog/process crashes.
public sealed class Wfp:IDisposable
{
    [StructLayout(LayoutKind.Sequential)] public struct Display { public IntPtr Name,Description; }
    [StructLayout(LayoutKind.Sequential)] public struct Blob { public uint Size;public IntPtr Data; }
    [StructLayout(LayoutKind.Explicit,Size=16)] public struct Value {
        [FieldOffset(0)]public uint Type;[FieldOffset(8)]public ulong Data;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Condition {public Guid Key;public uint Match;public Value Value;}
    [StructLayout(LayoutKind.Sequential)] public struct ActionType {public uint Type;public Guid Key;}
    [StructLayout(LayoutKind.Explicit,Size=16)] public struct ContextValue {[FieldOffset(0)]public ulong Raw;[FieldOffset(0)]public Guid Key;}
    [StructLayout(LayoutKind.Sequential)] public struct Filter {
        public Guid Key;public Display Display;public uint Flags;public IntPtr Provider;public Blob Data;
        public Guid Layer,Sublayer;public Value Weight;public uint Count;public IntPtr Conditions;
        public ActionType Action;public ContextValue Context;public IntPtr Reserved;public ulong Id;public Value EffectiveWeight;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Sublayer {
        public Guid Key;public Display Display;public uint Flags;public IntPtr Provider;public Blob Data;public ushort Weight;
    }
    [DllImport("fwpuclnt.dll",CharSet=CharSet.Unicode)]static extern uint FwpmEngineOpen0(string? server,uint auth,IntPtr identity,IntPtr session,out IntPtr engine);
    [DllImport("fwpuclnt.dll")]static extern uint FwpmEngineClose0(IntPtr engine);
    [DllImport("fwpuclnt.dll")]static extern uint FwpmTransactionBegin0(IntPtr engine,uint flags);
    [DllImport("fwpuclnt.dll")]static extern uint FwpmTransactionCommit0(IntPtr engine);
    [DllImport("fwpuclnt.dll")]static extern uint FwpmTransactionAbort0(IntPtr engine);
    [DllImport("fwpuclnt.dll")]static extern uint FwpmSubLayerAdd0(IntPtr engine,ref Sublayer layer,IntPtr security);
    [DllImport("fwpuclnt.dll")]static extern uint FwpmSubLayerDeleteByKey0(IntPtr engine,ref Guid key);
    [DllImport("fwpuclnt.dll")]static extern uint FwpmFilterAdd0(IntPtr engine,ref Filter filter,IntPtr security,out ulong id);
    [DllImport("fwpuclnt.dll")]static extern uint FwpmFilterDeleteByKey0(IntPtr engine,ref Guid key);
    [DllImport("fwpuclnt.dll")]static extern uint FwpmFilterGetByKey0(IntPtr engine,ref Guid key,out IntPtr filter);
    [DllImport("fwpuclnt.dll")]static extern void FwpmFreeMemory0(ref IntPtr memory);
    [DllImport("iphlpapi.dll")]static extern uint ConvertInterfaceGuidToLuid(ref Guid guid,out ulong luid);
    readonly IntPtr engine;
    public Wfp(){Check(FwpmEngineOpen0(null,10,IntPtr.Zero,IntPtr.Zero,out engine));}
    public static ulong Luid(Guid guid){Check(ConvertInterfaceGuidToLuid(ref guid,out ulong luid));return luid;}
    public static void Check(uint code){if(code!=0)throw new Win32Exception(unchecked((int)code));}
    public bool Exists(Guid key){uint result=FwpmFilterGetByKey0(engine,ref key,out IntPtr filter);if(result==0){FwpmFreeMemory0(ref filter);return true;}if(result==0x80320003)return false;Check(result);return false;}
    static void Deleted(uint code,uint missing){if(code!=0&&code!=missing)Check(code);}
    public void Install(Plan p)
    {
        Check(FwpmTransactionBegin0(engine,0));
        IntPtr name=Marshal.StringToHGlobalUni(p.Tag);
        try {
            var sub=new Sublayer{Key=p.Id,Display=new Display{Name=name},Flags=1,Weight=ushort.MaxValue};
            Check(FwpmSubLayerAdd0(engine,ref sub,IntPtr.Zero));
            Add(p,p.V4Filter,new Guid("1e5c9fae-8a84-4135-a331-950b54229ecd"),true,name);
            Add(p,p.V6Filter,new Guid("a3b3ab6b-3564-488c-9117-f34e82142763"),false,name);
            Check(FwpmTransactionCommit0(engine));
        }catch{FwpmTransactionAbort0(engine);throw;}finally{Marshal.FreeHGlobal(name);}
    }
    void Add(Plan p,Guid key,Guid layer,bool ipv4,IntPtr name,uint privateIndex=0)
    {
        // FLAGS_NONE_SET(IS_LOOPBACK): ADB/private IPC stays on loopback.
        // NOT_EQUAL owned LUID: all other IPv4 egress is blocked, including
        // existing sockets and more-specific LAN/RNDIS routes. IPv6 is blocked.
        var list=new List<Condition>{new(){Key=new Guid("632ce23b-5167-435c-86d7-e903684aa80c"),Match=8,Value=new Value{Type=3,Data=1}}};
        IntPtr luid=Marshal.AllocHGlobal(8);Marshal.WriteInt64(luid,unchecked((long)p.Luid));
        IntPtr loopback=Marshal.AllocHGlobal(16);Marshal.Copy(new byte[]{0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1},0,loopback,16);
        // The authenticated USB transport uses 127.0.0.1. Exclude it explicitly
        // as well as the loopback flag; transport liveness must not depend on
        // how a Windows version marks an outbound packet's loopback flag.
        list.Add(new Condition{Key=new Guid("b235ae9a-1d64-49b8-a44c-5ff3d9095045"),Match=10,
            Value=ipv4?new Value{Type=3,Data=0x7f000001}:new Value{Type=11,Data=unchecked((ulong)loopback.ToInt64())}});
        if(ipv4)list.Add(new Condition{Key=new Guid("4cd62a49-59c3-4969-b7f3-bda5d32890a4"),Match=10,Value=new Value{Type=4,Data=unchecked((ulong)luid.ToInt64())}});
        // Different fields deliberately express AND, avoiding WFP's OR handling
        // of consecutive conditions for the same field. Only the selected local
        // hotspot interface is exempt for DHCP/DNS/replies to its clients.
        if(ipv4&&privateIndex!=0)list.Add(new Condition{Key=new Guid("667fd755-d695-434a-8af5-d3835a1259bc"),Match=10,Value=new Value{Type=3,Data=privateIndex}});
        IntPtr conditions=Marshal.AllocHGlobal(Marshal.SizeOf<Condition>()*list.Count);
        try {
            for(int i=0;i<list.Count;i++)Marshal.StructureToPtr(list[i],conditions+i*Marshal.SizeOf<Condition>(),false);
            var f=new Filter{Key=key,Display=new Display{Name=name},Flags=1,Layer=layer,Sublayer=p.Id,
                Weight=new Value{Type=1,Data=15},Count=(uint)list.Count,Conditions=conditions,Action=new ActionType{Type=0x1001}};
            Check(FwpmFilterAdd0(engine,ref f,IntPtr.Zero,out _));
        }finally{Marshal.FreeHGlobal(conditions);Marshal.FreeHGlobal(luid);Marshal.FreeHGlobal(loopback);}
    }
    public void Sharing(Plan p,SharingState s,bool enabled)
    {
        s.Validate(p);Check(FwpmTransactionBegin0(engine,0));
        IntPtr name=Marshal.StringToHGlobalUni(p.Tag+"-TV");
        try {
            UpdateSharing(enabled,Exists(p.V4Filter),Exists(p.V6Filter),
                ProcessIdentity.Alive(p.GuardianPid,p.GuardianStart)&&ProcessIdentity.Alive(p.WorkerPid,p.WorkerStart),
                ()=>{Guid v4=p.V4Filter;Check(FwpmFilterDeleteByKey0(engine,ref v4));},
                ()=>Add(p,p.V4Filter,new Guid("1e5c9fae-8a84-4135-a331-950b54229ecd"),true,name,enabled?s.PrivateIndex:0),
                ()=>{foreach(Guid key in new[]{s.ForwardV4,s.ForwardV6}){var id=key;Deleted(FwpmFilterDeleteByKey0(engine,ref id),0x80320003);}},
                ()=>{Forward(p,s,s.ForwardV4,new Guid("a82acc24-4ee1-4ee1-b465-fd1d25cb10a4"),true,name);Forward(p,s,s.ForwardV6,new Guid("7b964818-19c7-493a-b71f-832c3684d28c"),false,name);});
            Check(FwpmTransactionCommit0(engine));
        }catch{FwpmTransactionAbort0(engine);throw;}finally{Marshal.FreeHGlobal(name);}
    }
    // Called inside the native transaction. Cleanup may follow a partial or
    // already-completed guard rollback; absence must not trap the UI in recovery.
    // A live session keeps its base IPv4 guard, and sharing never starts without
    // both base guards. Delegate tests exercise this production decision path.
    public static void UpdateSharing(bool enabled,bool v4,bool v6,bool live,
        Action deleteV4,Action addV4,Action deleteForward,Action addForward)
    {
        if(enabled&&(!v4||!v6))throw new IOException("USB ağ koruması eksik; bağlantıyı kesip yeniden bağlanın.");
        if(!enabled&&live&&!v6)throw new IOException("USB IPv6 koruması eksik; bağlantıyı kesip yeniden bağlanın.");
        if(v4)deleteV4();
        if(v4||live)addV4();
        deleteForward();
        if(enabled)addForward();
    }
    void Forward(Plan p,SharingState s,Guid key,Guid layer,bool ipv4,IntPtr name)
    {
        IntPtr source=Marshal.AllocHGlobal(8),destination=Marshal.AllocHGlobal(8),conditions=IntPtr.Zero;
        try{
            Marshal.WriteInt64(source,unchecked((long)s.PrivateLuid));Marshal.WriteInt64(destination,unchecked((long)p.Luid));
            // Match the arrival interface explicitly; return packets arrive on Wintun.
            var list=new List<Condition>{new(){Key=new Guid("2311334d-c92d-45bf-9496-edf447820e2d"),Match=0,Value=new Value{Type=3,Data=s.PrivateIndex}}};
            if(ipv4)list.Add(new(){Key=new Guid("1076b8a5-6323-4c5e-9810-e8d3fc9e6136"),Match=10,Value=new Value{Type=4,Data=unchecked((ulong)destination.ToInt64())}});
            conditions=Marshal.AllocHGlobal(Marshal.SizeOf<Condition>()*list.Count);
            for(int i=0;i<list.Count;i++)Marshal.StructureToPtr(list[i],conditions+i*Marshal.SizeOf<Condition>(),false);
            var f=new Filter{Key=key,Display=new Display{Name=name},Flags=1,Layer=layer,Sublayer=p.Id,Weight=new Value{Type=1,Data=15},Count=(uint)list.Count,Conditions=conditions,Action=new ActionType{Type=0x1001}};
            Check(FwpmFilterAdd0(engine,ref f,IntPtr.Zero,out _));
        }finally{if(conditions!=IntPtr.Zero)Marshal.FreeHGlobal(conditions);Marshal.FreeHGlobal(source);Marshal.FreeHGlobal(destination);}
    }
    public void Remove(Plan p,SharingState? sharing=null)
    {
        Check(FwpmTransactionBegin0(engine,0));
        try {
            Guid a=p.V4Filter,b=p.V6Filter,c=p.Id;
            if(sharing!=null){sharing.Validate(p);foreach(Guid key in new[]{sharing.ForwardV4,sharing.ForwardV6}){var id=key;Deleted(FwpmFilterDeleteByKey0(engine,ref id),0x80320003);}}
            Deleted(FwpmFilterDeleteByKey0(engine,ref a),0x80320003);
            Deleted(FwpmFilterDeleteByKey0(engine,ref b),0x80320003);
            Deleted(FwpmSubLayerDeleteByKey0(engine,ref c),0x80320007);
            Check(FwpmTransactionCommit0(engine));
        }catch{FwpmTransactionAbort0(engine);throw;}
    }
    public void Dispose()=>FwpmEngineClose0(engine);
}
