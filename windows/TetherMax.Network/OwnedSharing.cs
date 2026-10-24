using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace TetherMax.Network;

public sealed record SharingAddress(string IPAddress,int PrefixLength);
public sealed record SharingState(Guid PrivateGuid,uint PrivateIndex,ulong PrivateLuid,Guid ForwardV4,Guid ForwardV6,
    bool Dhcp,SharingAddress[] Addresses,string[] Dns)
{
    public bool NativeManaged {get;init;}
    public void Validate(Plan p)
    {
        p.Validate();
        if(PrivateGuid==Guid.Empty||PrivateGuid==p.AdapterGuid||PrivateIndex==0||PrivateLuid==0||PrivateLuid==p.Luid||
           new[]{p.Id,p.V4Filter,p.V6Filter,ForwardV4,ForwardV6}.Distinct().Count()!=5||ForwardV4==Guid.Empty||ForwardV6==Guid.Empty||Addresses==null||Dns==null||Addresses.Length>16||Dns.Length>8)
            throw new IOException("Invalid hotspot ownership");
        foreach(var a in Addresses)if(!IPAddress.TryParse(a.IPAddress,out var ip)||ip.AddressFamily!=AddressFamily.InterNetwork||a.PrefixLength<1||a.PrefixLength>32)throw new IOException("Invalid saved hotspot address");
        foreach(var value in Dns)if(!IPAddress.TryParse(value,out var ip)||ip.AddressFamily!=AddressFamily.InterNetwork)throw new IOException("Invalid saved hotspot DNS");
    }
}

// Windows' documented INetSharingConfiguration is the NAT/DHCP provider.
// The desktop owns its Wi-Fi Direct publisher; no third-party packet driver is required.
// The existing protected intent, watcher and SYSTEM rescue also own this lease.
public static class OwnedSharing
{
    public const string Connections="$m=New-Object -ComObject HNetCfg.HNetShare; $connections=@($m.EnumEveryConnection); if(!$connections.Count -or $null -eq $connections[0]){throw 'ICS enumeration unavailable'}; "+
        "function ByGuid([guid]$guid){$found=@($connections|Where-Object {[guid]$m.NetConnectionProps($_).Guid -eq $guid});if($found.Count -ne 1){throw 'ICS interface absent or ambiguous'};return $found[0]}; ";
    public static SharingState? Read(Journal journal,Plan p)
    {
        if(!File.Exists(journal.PathFor("sharing.json")))return null;
        var s=JsonSerializer.Deserialize<SharingState>(journal.ReadPublished("sharing.json"))??throw new IOException("Sharing intent missing");s.Validate(p);return s;
    }
    public static string SnapshotScript(Guid privateGuid,Guid? publicGuid=null)=>Connections+
        "$private=ByGuid ([guid]'"+privateGuid+"'); foreach($c in $connections){$cfg=$m.INetSharingConfigurationForINetConnection($c);if($cfg.SharingEnabled -and ([guid]$m.NetConnectionProps($c).Guid -ne [guid]'"+privateGuid+"' -or $cfg.SharingConnectionType -ne 1) -and ([guid]$m.NetConnectionProps($c).Guid -ne [guid]'"+(publicGuid??Guid.Empty)+"' -or $cfg.SharingConnectionType -ne 0)){throw 'Existing ICS configuration must not be replaced'}}; "+
        "$a=@(Get-NetAdapter -IncludeHidden|Where-Object {[guid]$_.InterfaceGuid -eq [guid]'"+privateGuid+"'}); "+
        "if($a.Count -ne 1 -or $a[0].Status -ne 'Up' -or $a[0].InterfaceDescription -notlike '*Wi-Fi Direct*'){throw 'Select the active Microsoft Wi-Fi Direct hotspot adapter'}; $i=$a[0].ifIndex; "+
        "[pscustomobject]@{PrivateIndex=$i;Dhcp=([string](Get-NetIPInterface -InterfaceIndex $i -AddressFamily IPv4).Dhcp -eq 'Enabled');"+
        "Addresses=@(Get-NetIPAddress -InterfaceIndex $i -AddressFamily IPv4|Where-Object {$_.IPAddress -notlike '169.254.*'}|Select-Object IPAddress,PrefixLength);"+
        "Dns=@((Get-DnsClientServerAddress -InterfaceIndex $i -AddressFamily IPv4).ServerAddresses)}|ConvertTo-Json -Depth 5 -Compress";
    public static string FirewallScript(Plan p,SharingState s,bool enabled) {
        s.Validate(p);
        string[] names={p.Tag+"-Hotspot-TCP-Inbound",p.Tag+"-Hotspot-TCP-Outbound",p.Tag+"-Hotspot-UDP-Inbound",p.Tag+"-Hotspot-UDP-Outbound"};
        if(!enabled)return "foreach($name in @("+string.Join(",",names.Select(n=>"'"+n+"'"))+")){Get-NetFirewallRule -Name $name -PolicyStore ActiveStore -ErrorAction SilentlyContinue|Remove-NetFirewallRule -ErrorAction Stop};";
        string script="$ap=@(Get-NetAdapter -IncludeHidden|Where-Object {[guid]$_.InterfaceGuid -eq [guid]'"+s.PrivateGuid+"'});if($ap.Count -ne 1){throw 'Owned hotspot adapter missing'};";
        foreach(string protocol in new[]{"TCP","UDP"})foreach(string direction in new[]{"Inbound","Outbound"}) {
            string name=p.Tag+"-Hotspot-"+protocol+"-"+direction;
            script+="if(!@(Get-NetFirewallRule -Name '"+name+"' -PolicyStore ActiveStore -ErrorAction SilentlyContinue).Count){New-NetFirewallRule -Name '"+name+"' -DisplayName 'TetherMax hotspot "+protocol+"' -Group 'TetherMax Hotspot Session' -PolicyStore ActiveStore -Direction "+direction+" -Action Allow -Profile Any -InterfaceAlias $ap[0].Name -RemoteAddress '192.168.137.0/24' -Protocol "+protocol+" -LocalPort "+(protocol=="TCP"?"80,443":"53,67,68")+"|Out-Null};";
        }
        return script;
    }
    public static string StartScript(Plan p,SharingState s)
    {
        s.Validate(p);
        return Connections+"$public=ByGuid ([guid]'"+p.AdapterGuid+"');$private=ByGuid ([guid]'"+s.PrivateGuid+"'); "+
            "foreach($c in $connections){$cfg=$m.INetSharingConfigurationForINetConnection($c);if($cfg.SharingEnabled -and ([guid]$m.NetConnectionProps($c).Guid -ne [guid]'"+s.PrivateGuid+"' -or $cfg.SharingConnectionType -ne 1) -and ([guid]$m.NetConnectionProps($c).Guid -ne [guid]'"+p.AdapterGuid+"' -or $cfg.SharingConnectionType -ne 0)){throw 'ICS ownership conflict'}}; "+
            "$privateConfig=$m.INetSharingConfigurationForINetConnection($private);$publicConfig=$m.INetSharingConfigurationForINetConnection($public);if(!$publicConfig.SharingEnabled){if($privateConfig.SharingEnabled){$privateConfig.DisableSharing()};$publicConfig.EnableSharing(0)};if(!$privateConfig.SharingEnabled){$privateConfig.EnableSharing(1)}; "+
            "if(!$m.INetSharingConfigurationForINetConnection($public).SharingEnabled -or $m.INetSharingConfigurationForINetConnection($public).SharingConnectionType -ne 0 -or !$m.INetSharingConfigurationForINetConnection($private).SharingEnabled -or $m.INetSharingConfigurationForINetConnection($private).SharingConnectionType -ne 1){throw 'ICS activation not acknowledged'}";
    }
    public static string StopScript(Plan p,SharingState s)
    {
        s.Validate(p);
        string restore=string.Join("",s.Addresses.Select(a=>"if(!@(Get-NetIPAddress -InterfaceIndex $i -AddressFamily IPv4|Where-Object {$_.IPAddress -eq '"+a.IPAddress+"' -and $_.PrefixLength -eq "+a.PrefixLength+"}).Count){New-NetIPAddress -InterfaceIndex $i -IPAddress '"+a.IPAddress+"' -PrefixLength "+a.PrefixLength+" -PolicyStore ActiveStore|Out-Null};"));
        string keep=string.Join(",",s.Addresses.Select(a=>"'"+a.IPAddress+"/"+a.PrefixLength+"'"));
        return Connections+
            // Validate every enabled share before changing anything, including a
            // partial activation after the public side alone was enabled.
            "foreach($c in $connections){$config=$m.INetSharingConfigurationForINetConnection($c);if(!$config.SharingEnabled){continue};$guid=[guid]$m.NetConnectionProps($c).Guid;"+
            "if(($guid -eq [guid]'"+p.AdapterGuid+"' -and $config.SharingConnectionType -eq 0) -or ($guid -eq [guid]'"+s.PrivateGuid+"' -and $config.SharingConnectionType -eq 1)){continue};throw 'ICS configuration changed; guard retained'}; "+
            "foreach($id in @('"+s.PrivateGuid+"','"+p.AdapterGuid+"')){foreach($c in $connections){if([guid]$m.NetConnectionProps($c).Guid -eq [guid]$id){$config=$m.INetSharingConfigurationForINetConnection($c);if($config.SharingEnabled){$config.DisableSharing()}}}}; "+
            "if(@($connections|Where-Object {$m.INetSharingConfigurationForINetConnection($_).SharingEnabled}).Count){throw 'ICS did not stop; guard retained'}; "+
            "$a=@(Get-NetAdapter -IncludeHidden|Where-Object {[guid]$_.InterfaceGuid -eq [guid]'"+s.PrivateGuid+"'});if($a.Count -eq 1){$i=$a[0].ifIndex; "+
            "$keep=@("+keep+");foreach($ip in @(Get-NetIPAddress -InterfaceIndex $i -AddressFamily IPv4|Where-Object {$_.IPAddress -notlike '169.254.*'})){if(($ip.IPAddress+'/'+$ip.PrefixLength) -notin $keep){if($ip.IPAddress -ne '192.168.137.1' -or $ip.PrefixLength -ne 24){throw 'Private address changed; guard retained'};$ip|Remove-NetIPAddress -Confirm:$false}};"+
            restore+"Set-NetIPInterface -InterfaceIndex $i -AddressFamily IPv4 -Dhcp "+(s.Dhcp?"Enabled":"Disabled")+"; "+
            (s.Dns.Length==0?"Set-DnsClientServerAddress -InterfaceIndex $i -ResetServerAddresses":"Set-DnsClientServerAddress -InterfaceIndex $i -ServerAddresses @("+string.Join(",",s.Dns.Select(d=>"'"+d+"'"))+")")+"; }";
    }
    public static void ValidateSource(JsonElement health,Plan p) {p.Validate();if(!TunnelHealth.Healthy(health,p.AdapterGuid))throw new IOException("Verified TetherMax Wintun HTTPS/DNS traffic required before sharing");}
    public static void Start(Journal journal,Plan p,Guid privateGuid,NativeBackend backend,bool nativeManaged=false)
    {
        if(journal.Completed||!ProcessIdentity.Alive(p.GuardianPid,p.GuardianStart)||!ProcessIdentity.Alive(p.WorkerPid,p.WorkerStart))throw new IOException("No active TetherMax session");
        using(var health=JsonDocument.Parse(journal.ReadPublished("windows-connectivity.json")))ValidateSource(health.RootElement,p);
        SharingState? s=Read(journal,p);
        if(s!=null&&s.NativeManaged!=nativeManaged)throw new IOException("Reconnect before changing hotspot providers");
        if(s!=null&&s.PrivateGuid!=privateGuid)throw new IOException("Reconnect before selecting another hotspot interface");

        if(s!=null){using var fresh=JsonDocument.Parse(NativeBackend.PowerShell(SnapshotScript(privateGuid,p.AdapterGuid)));if(fresh.RootElement.GetProperty("PrivateIndex").GetUInt32()!=s.PrivateIndex||Wfp.Luid(privateGuid)!=s.PrivateLuid)throw new IOException("Hotspot identity changed; reconnect TetherMax before sharing");}
        if(s==null){
            using var snapshot=JsonDocument.Parse(NativeBackend.PowerShell(SnapshotScript(privateGuid,p.AdapterGuid)));var r=snapshot.RootElement;
            s=new(privateGuid,r.GetProperty("PrivateIndex").GetUInt32(),Wfp.Luid(privateGuid),Guid.NewGuid(),Guid.NewGuid(),r.GetProperty("Dhcp").GetBoolean(),
                JsonSerializer.Deserialize<SharingAddress[]>(r.GetProperty("Addresses"))!,JsonSerializer.Deserialize<string[]>(r.GetProperty("Dns"))!){NativeManaged=nativeManaged};
            s.Validate(p);journal.WriteNew("sharing.json",JsonSerializer.Serialize(s));
        }
        File.Delete(journal.PathFor("sharing-disabled"));
        using var wfp=new Wfp();wfp.Sharing(p,s,true);
        try{backend.SharingCommands((nativeManaged?"":StartScript(p,s)+";")+FirewallScript(p,s,true),true);}
        catch{Stop(journal,p,backend);throw;}
        journal.WriteNew("sharing-start-"+Guid.NewGuid().ToString("N")+".json",JsonSerializer.Serialize(new{observedAt=DateTimeOffset.UtcNow,publicGuid=p.AdapterGuid,privateGuid,scope="ICS configured; TV runtime acceptance required"}));
    }
    public static object Observe(Journal journal,Plan p)
    {
        using var lease=journal.Lock();
        var s=Read(journal,p);
        if(s==null||File.Exists(journal.PathFor("sharing-disabled")))return new{type="sharing-state",configured=false};
        if(s.NativeManaged)NativeMobileHotspot.Observe(journal,p);
        else NativeBackend.PowerShell(Connections+"$public=ByGuid ([guid]'"+p.AdapterGuid+"');$private=ByGuid ([guid]'"+s.PrivateGuid+"');"+
            "if(!$m.INetSharingConfigurationForINetConnection($public).SharingEnabled -or $m.INetSharingConfigurationForINetConnection($public).SharingConnectionType -ne 0 -or !$m.INetSharingConfigurationForINetConnection($private).SharingEnabled -or $m.INetSharingConfigurationForINetConnection($private).SharingConnectionType -ne 1){throw 'ICS source or private interface changed'}");
        using var wfp=new Wfp();if(!wfp.Exists(s.ForwardV4)||!wfp.Exists(s.ForwardV6))throw new IOException("TV forwarding guard missing");
        object[] clients=Array.Empty<object>();string? clientsError=null;if(s.NativeManaged)try{clients=NativeMobileHotspot.Clients(journal,p,s.PrivateIndex);}catch(Exception e){clientsError=e.Message;}
        return new{type="sharing-state",clients,clientsError,configured=true,publicGuid=p.AdapterGuid,privateGuid=s.PrivateGuid,scope="ICS and forwarding guard observed; TV internet not certified"};
    }
    public static void Stop(Journal journal,Plan p,NativeBackend backend)
    {
        if(File.Exists(journal.PathFor("sharing-disabled")))return;
        var s=Read(journal,p);
        NativeMobileHotspot.Stop(journal,p);
        if(s==null)return;
        backend.SharingCommands((s.NativeManaged?"":StopScript(p,s)+";")+FirewallScript(p,s,false),false);
        using var wfp=new Wfp();wfp.Sharing(p,s,false);
        journal.WriteNew("sharing-disabled","ICS disabled before tunnel recovery");
    }
}
