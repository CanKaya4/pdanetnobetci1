using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

// Separate engineering probe. Default dry-run never loads a DLL or creates an adapter.
internal static class Program {
 const string ExpectedSha256="E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE";
 [UnmanagedFunctionPointer(CallingConvention.Winapi,CharSet=CharSet.Unicode,SetLastError=true)]
 delegate nint Create([MarshalAs(UnmanagedType.LPWStr)]string name,[MarshalAs(UnmanagedType.LPWStr)]string type,ref Guid guid);
 [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Close(nint adapter);
 [UnmanagedFunctionPointer(CallingConvention.Winapi,SetLastError=true)] delegate nint Start(nint adapter,uint capacity);
 [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void End(nint session);
 static object Snapshot()=>NetworkInterface.GetAllNetworkInterfaces().Select(n=>new {n.Id,n.Name,Status=n.OperationalStatus.ToString(),Gateways=n.GetIPProperties().GatewayAddresses.Select(a=>a.Address.ToString()).ToArray(),Dns=n.GetIPProperties().DnsAddresses.Select(a=>a.ToString()).ToArray()}).ToArray();
 static int Main(string[] args) {
  string folder=Path.Combine(AppContext.BaseDirectory,"reports");Directory.CreateDirectory(folder);
  string dll=Path.Combine(AppContext.BaseDirectory,"wintun.dll");
  if(!File.Exists(dll)||Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll)))!=ExpectedSha256){Console.Error.WriteLine("Verified Wintun 0.14.1 amd64 DLL is required; nothing loaded.");return 1;}
  bool apply=args.Contains("--apply"),approved=args.Contains("--approve-adapter-test");
  bool admin=new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
  string name="TetherMax-Test-"+Guid.NewGuid().ToString("N")[..8];Guid guid=Guid.NewGuid();
  var plan=new {Mode=apply?"apply":"dry-run",AdapterName=name,AdapterGuid=guid,DllSha256=ExpectedSha256,Elevated=admin,
   Mutations=new[]{"WintunCreateAdapter (may install signed driver)","WintunStartSession 4 MiB", "WintunEndSession", "WintunCloseAdapter removes only created adapter"},
   ExplicitlyNotConfigured=new[]{"IP addresses","routes","DNS","firewall","Wi-Fi","PdaNet","MyPublicWiFi"},
   Limits=new[]{"Not a TUN internet bridge or packet test", "Driver-store package may remain; no global driver deletion", "Normal/error cleanup uses finally; crash/power-failure rollback not yet certified"},Before=Snapshot()};
  string journal=Path.Combine(folder,DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".json");
  File.WriteAllText(journal,JsonSerializer.Serialize(plan,new JsonSerializerOptions{WriteIndented=true}));
  Console.WriteLine(File.ReadAllText(journal));
  if(!apply){Console.WriteLine("DRY RUN ONLY: no DLL loaded, no adapter/session/driver created.");return 0;}
  if(!approved||!admin){Console.Error.WriteLine("Explicit approval AND an elevated test process are required. No DLL loaded.");return 2;}
  if(NetworkInterface.GetAllNetworkInterfaces().Any(n=>n.Name==name)){Console.Error.WriteLine("Name collision; nothing changed.");return 3;}
  nint library=0,adapter=0,session=0; Close? close=null;End? end=null;int result=0;
  bool adapterCreated=false,sessionStarted=false;string? failure=null;
  try {
   library=NativeLibrary.Load(dll);
   T Export<T>(string export)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,export));
   var create=Export<Create>("WintunCreateAdapter");close=Export<Close>("WintunCloseAdapter");var start=Export<Start>("WintunStartSession");end=Export<End>("WintunEndSession");
   adapter=create(name,"TetherMax-Test",ref guid);if(adapter==0)throw new Win32Exception(Marshal.GetLastWin32Error());
   adapterCreated=true;
   session=start(adapter,0x400000);if(session==0)throw new Win32Exception(Marshal.GetLastWin32Error());
   sessionStarted=true;
   // IP Helper reads actual Windows interfaces while the session is active.
   File.WriteAllText(journal+".active.json",JsonSerializer.Serialize(new{AdapterName=name,AdapterGuid=guid,SessionStarted=sessionStarted,WindowsInterfaces=Snapshot()},new JsonSerializerOptions{WriteIndented=true}));
   Console.WriteLine("Owned test adapter and session created. No IP, route or DNS configuration performed.");
  }catch(Exception ex){failure=ex.ToString();Console.Error.WriteLine(ex.Message);result=1;}
  finally{
   if(session!=0)end!(session);if(adapter!=0)close!(adapter);if(library!=0)NativeLibrary.Free(library);
   var cleanup=RemovalVerifier.Wait(()=>NetworkInterface.GetAllNetworkInterfaces().Any(n=>n.Id.Equals(guid.ToString("B"),StringComparison.OrdinalIgnoreCase)||n.Name==name));
   bool remains=cleanup.StillPresent;
   File.WriteAllText(journal+".after.json",JsonSerializer.Serialize(new{AdapterName=name,AdapterGuid=guid,AdapterCreated=adapterCreated,SessionStarted=sessionStarted,AdapterStillPresent=remains,CleanupInitiallyPending=cleanup.InitiallyPresent,CleanupPolls=cleanup.Polls,Failure=failure,After=Snapshot()},new JsonSerializerOptions{WriteIndented=true}));
   Console.WriteLine("Owned adapter still present: "+remains);if(remains)result=4;
  }
  return result;
 }
}
