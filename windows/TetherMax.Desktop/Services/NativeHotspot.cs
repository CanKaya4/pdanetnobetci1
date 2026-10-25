using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
namespace TetherMax;
// Credentials cross only a private, authenticated pipe. The elevated guardian
// owns the radio/ICS lease and recovers it even if this desktop process exits.
internal sealed class NativeHotspot : IDisposable {
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe,out uint pid);
 public Guid AdapterGuid {get;private set;}
 public string? Failure=>null;
 public string? ClientError=>null;
 bool disposed;
 public NativeHotspot(Dispatcher dispatcher){}
 public async Task Start(string ssid,string password,int band,string session) {
  if(disposed||AdapterGuid!=Guid.Empty)throw new IOException("Hotspot zaten açık.");
  string name="TetherMax-Hotspot-"+Guid.NewGuid().ToString("N");
  var acl=new PipeSecurity();acl.SetAccessRuleProtection(true,false);
  using var identity=WindowsIdentity.GetCurrent();
  acl.AddAccessRule(new PipeAccessRule(identity.User!,PipeAccessRights.FullControl,AccessControlType.Allow));
  acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),PipeAccessRights.FullControl,AccessControlType.Allow));
  using var pipe=NamedPipeServerStreamAcl.Create(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,acl);
  var info=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"TetherMax.Network.exe")){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
  foreach(string arg in new[]{"hotspot-start",session,name,"--approve-windows-sharing"})info.ArgumentList.Add(arg);
  using var child=Process.Start(info)??throw new IOException("Hotspot yardımcısı başlatılamadı.");
  using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(90));
  var connected=pipe.WaitForConnectionAsync(deadline.Token);var exited=child.WaitForExitAsync(deadline.Token);
  if(await Task.WhenAny(connected,exited)!=connected)throw new IOException("Hotspot yardımcısı bağlanmadan kapandı.");await connected;
  if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out uint pid)||pid!=child.Id)throw new IOException("Hotspot yardımcısının kimliği eşleşmiyor.");
  using var output=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
  await output.WriteLineAsync(JsonSerializer.Serialize(new{Ssid=ssid,Password=password,Band=band}));password="";
  using var input=new StreamReader(pipe,Encoding.UTF8,false,4096,true);
  string line=await input.ReadLineAsync(deadline.Token)??throw new IOException("Hotspot sonucu alınamadı.");
  using var result=JsonDocument.Parse(line);
  if(result.RootElement.TryGetProperty("error",out var error))throw new IOException(error.GetString());
  AdapterGuid=result.RootElement.GetProperty("adapter").GetGuid();
  await exited;if(child.ExitCode!=0)throw new IOException("Hotspot yardımcısı işlemi tamamlayamadı.");
 }
 public void Dispose(){disposed=true;}
}
