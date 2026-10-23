using System.Diagnostics;
using System.Text;

namespace TetherMax.Network;

// Actual Windows NetTCPIP/DnsClient configuration, never executed by offline tests.
// Commands contain only validated GUIDs, fixed prefixes and generated ownership tags.
public sealed class NativeBackend:IBackend
{
    const string OptionalQueries="function OptionalIPv4([int]$index){try{Get-NetIPAddress -InterfaceIndex $index -AddressFamily IPv4 -ErrorAction Stop}catch{if($_.FullyQualifiedErrorId -notlike 'CmdletizationQuery_NotFound*'){throw}}}; "+
        "function OptionalRoute([int]$index,[string]$prefix){try{Get-NetRoute -InterfaceIndex $index -DestinationPrefix $prefix -PolicyStore ActiveStore -ErrorAction Stop}catch{if($_.FullyQualifiedErrorId -notlike 'CmdletizationQuery_NotFound*'){throw}}}; ";
    readonly Journal journal;
    public NativeBackend(Journal journal){this.journal=journal;}
    void ExecuteOwned(string commands,bool applying)
    {
        // The OS interpreter itself holds this lock. If the guardian dies while
        // CIM is running, recovery waits for the in-flight command to finish.
        // A delayed interpreter cannot apply after recovery has completed.
        string path=journal.PathFor("os.lock").Replace("'","''"),complete=journal.PathFor("complete").Replace("'","''");
        string meta=journal.PathFor("os-process.json").Replace("'","''"),temp=journal.PathFor("os-process.tmp").Replace("'","''");
        string record="$self=Get-Process -Id $PID;$meta=[Text.Encoding]::UTF8.GetBytes(([pscustomobject]@{pid=$PID;start=$self.StartTime.ToUniversalTime().Ticks}|ConvertTo-Json -Compress));"+
            "$f=[IO.File]::Open('"+temp+"','Create','Write','Read');try{$f.Write($meta,0,$meta.Length);$f.Flush($true)}finally{$f.Dispose()};"+
            "if(Test-Path -LiteralPath '"+meta+"'){[IO.File]::Replace('"+temp+"','"+meta+"',$null)}else{[IO.File]::Move('"+temp+"','"+meta+"')};";
        string script="$lock=$null;$deadline=[DateTime]::UtcNow.AddSeconds(25);while($null -eq $lock){try{$lock=[IO.File]::Open('"+path+"','OpenOrCreate','ReadWrite','None')}catch{if([DateTime]::UtcNow -ge $deadline){throw};Start-Sleep -Milliseconds 100}};try{"+
            (applying?"if(Test-Path -LiteralPath '"+complete+"'){throw 'Session already recovered'};":"")+record+commands+"}finally{$lock.Dispose()}";
        PowerShell(script);
    }
    public static string Adapter(Plan p)=>"$a=@(Get-NetAdapter -IncludeHidden | Where-Object { [guid]$_.InterfaceGuid -eq [guid]'"+p.AdapterGuid+"' }); "+
        "if($a.Count -gt 1){throw 'Ambiguous adapter'}; if($a.Count -eq 1 -and $a[0].Name -ne '"+p.AdapterName+"'){throw 'Adapter identity changed'}; ";
    public static string ConfigureScript(Plan p)
    {
        p.Validate();
        return OptionalQueries+Adapter(p)+"if($a.Count -ne 1){throw 'Owned adapter missing'}; $i=$a[0].ifIndex; "+
        "if(@(Get-NetIPAddress -AddressFamily IPv4 | Where-Object {$_.IPAddress -eq '10.254.88.2'}).Count){throw 'Tunnel address already in use'}; "+
        "if(@(OptionalIPv4 $i | Where-Object {$_.IPAddress -notlike '169.254.*'}).Count){throw 'Owned adapter has existing IPv4 state'}; "+
        "if(@(Get-DnsClientNrptRule | Where-Object {$_.DisplayName -eq '"+p.Tag+"'}).Count){throw 'NRPT ownership conflict'}; "+
        "Set-NetIPInterface -InterfaceIndex $i -AddressFamily IPv4 -Dhcp Disabled -AutomaticMetric Disabled -InterfaceMetric 1 -NlMtuBytes 1500 -DadTransmits 0; "+
        "New-NetIPAddress -InterfaceIndex $i -AddressFamily IPv4 -IPAddress 10.254.88.2 -PrefixLength 30 -PolicyStore ActiveStore | Out-Null; "+
        "$until=[DateTime]::UtcNow.AddSeconds(5);do{$assigned=Get-NetIPAddress -InterfaceIndex $i -IPAddress 10.254.88.2;if($assigned.AddressState -eq 'Preferred'){break};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $until);if($assigned.AddressState -ne 'Preferred'){throw 'Owned IPv4 address not ready'}; "+
        "Set-DnsClientServerAddress -InterfaceIndex $i -ServerAddresses '1.1.1.1'; "+
        "Add-DnsClientNrptRule -Namespace '.' -NameServers '1.1.1.1' -DisplayName '"+p.Tag+"' -Comment '"+p.Id+"' | Out-Null; "+
        "foreach($prefix in @('0.0.0.0/1','128.0.0.0/1')) { New-NetRoute -InterfaceIndex $i -DestinationPrefix $prefix -NextHop 0.0.0.0 -RouteMetric 1 -PolicyStore ActiveStore | Out-Null }; "+
        "Clear-DnsClientCache; "+
        "foreach($ip in @('1.1.1.1','8.8.8.8')) { $r=Find-NetRoute -RemoteIPAddress $ip; if(@($r|Where-Object {$_.InterfaceIndex -ne $i}).Count){throw 'Windows route selection did not select TetherMax'} }";
    }
    public static string RemoveScript(Plan p)
    {
        p.Validate();
        return OptionalQueries+"$rules=@(Get-DnsClientNrptRule | Where-Object {$_.DisplayName -eq '"+p.Tag+"'}); "+
        "if($rules.Count -gt 1){throw 'Ambiguous owned DNS rule'}; foreach($r in $rules) { if($r.Comment -ne '"+p.Id+"' -or @($r.Namespace).Count -ne 1 -or @($r.Namespace)[0] -ne '.' -or [string]$r.NameServers -ne '1.1.1.1'){throw 'Owned DNS rule replaced'}; Remove-DnsClientNrptRule -Name $r.Name -Force }; "+
        Adapter(p)+"if($a.Count -eq 1) { $i=$a[0].ifIndex; "+
        "foreach($prefix in @('128.0.0.0/1','0.0.0.0/1')) { $routes=@(OptionalRoute $i $prefix); foreach($r in $routes) { if($r.NextHop -ne '0.0.0.0' -or $r.RouteMetric -ne 1){throw 'Owned route replaced'}; $r | Remove-NetRoute -Confirm:$false } }; "+
        "Set-DnsClientServerAddress -InterfaceIndex $i -ResetServerAddresses; "+
        "$ips=@(OptionalIPv4 $i | Where-Object {$_.IPAddress -eq '10.254.88.2'}); foreach($ip in $ips) {if($ip.PrefixLength -ne 30){throw 'Owned IP replaced'}; $ip|Remove-NetIPAddress -Confirm:$false} }; Clear-DnsClientCache";
    }
    public static string PowerShell(string commands)
    {
        string script="$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);"+commands;
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"))
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        const string launcher="[Console]::InputEncoding=[Text.UTF8Encoding]::new($false); & ([scriptblock]::Create([Console]::In.ReadToEnd()))";
        foreach(string arg in new[]{"-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(launcher))})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("Windows backend did not start");
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(script);process.StandardInput.Close();
        if(!process.WaitForExit(40000)){process.Kill();process.WaitForExit();throw new IOException("Windows backend timed out; recovery intent retained");}
        Task.WaitAll(stdout,stderr);
        if(process.ExitCode!=0)throw new IOException("Windows network backend: "+stderr.Result.Trim());
        return stdout.Result;
    }
    public void Guard(Plan p){using var wfp=new Wfp();wfp.Install(p);}
    public void Configure(Plan p)=>ExecuteOwned(ConfigureScript(p),true);
    public void RemoveConfiguration(Plan p)=>ExecuteOwned(RemoveScript(p),false);
    public void RemoveGuard(Plan p){using var wfp=new Wfp();wfp.Remove(p);}
}
