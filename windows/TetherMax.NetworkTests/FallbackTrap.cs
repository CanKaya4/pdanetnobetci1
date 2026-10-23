using System.Diagnostics;
using System.Text.Json;
using TetherMax.Network;

// A second owned Wintun is the only fallback target in the disposable VM.
// Negative probes cannot emit packets onto a public physical network.
sealed class FallbackTrap:IAsyncDisposable
{
    readonly Process process;
    readonly CancellationTokenSource stop=new();
    readonly Task heartbeat,telemetry;
    public string Name {get;}
    readonly Guid guid;
    long rejected;
    public long Rejected=>Interlocked.Read(ref rejected);
    FallbackTrap(Process process,string name,Guid guid)
    {
        this.process=process;Name=name;this.guid=guid;
        heartbeat=Task.Run(async()=>{try{while(!stop.IsCancellationRequested){await process.StandardInput.BaseStream.WriteAsync(new byte[]{0},stop.Token);await process.StandardInput.BaseStream.FlushAsync(stop.Token);await Task.Delay(500,stop.Token);}}catch{}});
        telemetry=Task.Run(async()=>{try{while(await process.StandardOutput.ReadLineAsync(stop.Token) is string line){using var doc=JsonDocument.Parse(line);if(doc.RootElement.TryGetProperty("counters",out var counters))Interlocked.Exchange(ref rejected,counters.GetProperty("Rejected").GetInt64());}}catch{}});
    }
    public static async Task<FallbackTrap> Start(string bundle,string transport,string token)
    {
        var info=new ProcessStartInfo(Path.Combine(bundle,"tethermax-engine.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        string journal=Path.Combine(Path.GetTempPath(),"TetherMax-trap-"+Guid.NewGuid().ToString("N")+".jsonl");
        foreach(string arg in new[]{"ip-worker","--approve-adapter-change","--approve-usb-test","--transport",transport,"--wintun-dll",Path.Combine(bundle,"wintun.dll"),"--journal",journal})info.ArgumentList.Add(arg);
        var p=Process.Start(info)??throw new Exception("fallback trap worker failed");
        p.StandardInput.NewLine="\n";await p.StandardInput.WriteLineAsync(token);await p.StandardInput.FlushAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        string identity=await p.StandardOutput.ReadLineAsync(timeout.Token)??throw new Exception("fallback trap not ready: "+await p.StandardError.ReadToEndAsync());
        using var data=JsonDocument.Parse(identity);Guid guid=Guid.Parse(data.RootElement.GetProperty("guid").GetString()!);string name=data.RootElement.GetProperty("adapter").GetString()!;
        var trap=new FallbackTrap(p,name,guid);
        try {
            string select="$a=Get-NetAdapter -IncludeHidden | Where-Object {[guid]$_.InterfaceGuid -eq [guid]'"+guid+"'};if(@($a).Count -ne 1){throw 'trap missing'};$i=$a.ifIndex;";
            NativeBackend.PowerShell(select+
                "Set-NetIPInterface -InterfaceIndex $i -AddressFamily IPv4 -Dhcp Disabled -NlMtuBytes 1500 -DadTransmits 0;"+
                "Set-NetIPInterface -InterfaceIndex $i -AddressFamily IPv6 -DadTransmits 0;"+
                "New-NetIPAddress -InterfaceIndex $i -IPAddress 10.254.89.2 -PrefixLength 30 -PolicyStore ActiveStore | Out-Null;"+
                "New-NetRoute -InterfaceIndex $i -DestinationPrefix 198.18.0.2/32 -NextHop 0.0.0.0 -RouteMetric 1 -PolicyStore ActiveStore | Out-Null;"+
                "New-NetIPAddress -InterfaceIndex $i -IPAddress 2001:db8:89::2 -PrefixLength 64 -PolicyStore ActiveStore | Out-Null;"+
                "New-NetRoute -InterfaceIndex $i -DestinationPrefix 2001:db8:ffff::1/128 -NextHop :: -RouteMetric 1 -PolicyStore ActiveStore | Out-Null;"+
                "$until=[DateTime]::UtcNow.AddSeconds(5);do{$ip=Get-NetIPAddress -InterfaceIndex $i -IPAddress 10.254.89.2;if($ip.AddressState -eq 'Preferred'){break};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $until);if($ip.AddressState -ne 'Preferred'){throw 'Trap address not ready'};"+
                "Get-NetIPAddress -InterfaceIndex $i | Select-Object IPAddress,AddressState,InterfaceIndex | ConvertTo-Json -Compress");
            Console.WriteLine("Native fallback adapter ready: "+name);
            return trap;
        }catch{await trap.DisposeAsync();throw;}
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();process.StandardInput.Close();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited){process.Kill();await process.WaitForExitAsync();}}
        await heartbeat;await telemetry;process.Dispose();stop.Dispose();
    }
}
