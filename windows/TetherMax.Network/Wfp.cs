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
    void Add(Plan p,Guid key,Guid layer,bool ipv4,IntPtr name)
    {
        // FLAGS_NONE_SET(IS_LOOPBACK): ADB/private IPC stays on loopback.
        // NOT_EQUAL owned LUID: all other IPv4 egress is blocked, including
        // existing sockets and more-specific LAN/RNDIS routes. IPv6 is blocked.
        var list=new List<Condition>{new(){Key=new Guid("632ce23b-5167-435c-86d7-e903684aa80c"),Match=8,Value=new Value{Type=3,Data=1}}};
        IntPtr luid=Marshal.AllocHGlobal(8);Marshal.WriteInt64(luid,unchecked((long)p.Luid));
        if(ipv4)list.Add(new Condition{Key=new Guid("4cd62a49-59c3-4969-b7f3-bda5d32890a4"),Match=10,Value=new Value{Type=4,Data=unchecked((ulong)luid.ToInt64())}});
        IntPtr conditions=Marshal.AllocHGlobal(Marshal.SizeOf<Condition>()*list.Count);
        try {
            for(int i=0;i<list.Count;i++)Marshal.StructureToPtr(list[i],conditions+i*Marshal.SizeOf<Condition>(),false);
            var f=new Filter{Key=key,Display=new Display{Name=name},Flags=1,Layer=layer,Sublayer=p.Id,
                Weight=new Value{Type=1,Data=15},Count=(uint)list.Count,Conditions=conditions,Action=new ActionType{Type=0x1001}};
            Check(FwpmFilterAdd0(engine,ref f,IntPtr.Zero,out _));
        }finally{Marshal.FreeHGlobal(conditions);Marshal.FreeHGlobal(luid);}
    }
    public void Remove(Plan p)
    {
        Check(FwpmTransactionBegin0(engine,0));
        try {
            Guid a=p.V4Filter,b=p.V6Filter,c=p.Id;
            Deleted(FwpmFilterDeleteByKey0(engine,ref a),0x80320003);
            Deleted(FwpmFilterDeleteByKey0(engine,ref b),0x80320003);
            Deleted(FwpmSubLayerDeleteByKey0(engine,ref c),0x80320007);
            Check(FwpmTransactionCommit0(engine));
        }catch{FwpmTransactionAbort0(engine);throw;}
    }
    public void Dispose()=>FwpmEngineClose0(engine);
}
