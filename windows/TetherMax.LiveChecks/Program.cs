using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using TetherMax.Network;
if(args.Length==8&&args[0]=="--approved-profile-check") {
 using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(12));
 string before=NativeBackend.PowerShell("Find-NetRoute -RemoteIPAddress 1.1.1.1|Select-Object InterfaceAlias,InterfaceIndex|ConvertTo-Json -Compress");
 string token=await TetherMax.Pairing.Connect(int.Parse(args[2]),args[1],_=>Task.FromResult(false),deadline.Token);
 var start=new ProcessStartInfo(args[4]){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
 foreach(var arg in new[]{"profile-set","--approve-usb-test","--transport","127.0.0.1:"+args[3],"--mode",args[5]})start.ArgumentList.Add(arg);
 using var child=Process.Start(start)!;child.StandardInput.NewLine="\n";await child.StandardInput.WriteLineAsync(token);token="";child.StandardInput.Close();
 var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();await child.WaitForExitAsync(deadline.Token);
 if(child.ExitCode!=0)throw new IOException(await error);
 string status=await output;string after=NativeBackend.PowerShell("Find-NetRoute -RemoteIPAddress 1.1.1.1|Select-Object InterfaceAlias,InterfaceIndex|ConvertTo-Json -Compress");
 var result=JsonSerializer.Serialize(new{profile=args[5],phoneStatus=JsonSerializer.Deserialize<JsonElement>(status),sameSelectedRoute=before==after,observedAt=DateTimeOffset.UtcNow});await File.WriteAllTextAsync(args[6],result);Console.WriteLine(result);return;
}
if(args.Length!=3||args[0]!="--approved-physical-test")throw new ArgumentException("Explicit physical-test flag, trigger file and output required");
using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(100));var ct=timeout.Token;
while(!File.Exists(args[1]))await Task.Delay(100,ct);
var trigger=DateTimeOffset.Parse(await File.ReadAllTextAsync(args[1],ct));
while(true){
 ct.ThrowIfCancellationRequested();
 var adapter=NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n=>n.Name.StartsWith("TetherMax-IP-")&&n.OperationalStatus==OperationalStatus.Up);
 if(adapter!=null){
  string selected=NativeBackend.PowerShell("$r=Find-NetRoute -RemoteIPAddress 1.1.1.1; $r|Select-Object InterfaceAlias,InterfaceIndex|ConvertTo-Json -Compress");
  if(selected.Contains(adapter.Name)){
   double routeMs=(DateTimeOffset.UtcNow-trigger).TotalMilliseconds;
   string health=await ConnectivityChecks.Run(Guid.Parse(adapter.Id),ct);
   using var checks=JsonDocument.Parse(health);string result=JsonSerializer.Serialize(new{trigger,routeReadyMs=routeMs,finishedMs=(DateTimeOffset.UtcNow-trigger).TotalMilliseconds,route=adapter.Name,health=checks.RootElement.Clone(),scope="Single physical observation; includes UAC/user delay; not a performance distribution"},new JsonSerializerOptions{WriteIndented=true});
   await File.WriteAllTextAsync(args[2],result,ct);Console.WriteLine(result);return;
  }
 }
 await Task.Delay(250,ct);
}
