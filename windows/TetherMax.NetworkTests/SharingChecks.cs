using TetherMax.Network;

static class SharingChecks
{
    public static void Run(Plan p)
    {
        string healthChecks="[{\"name\":\"windows-https-no-proxy\",\"passed\":true},{\"name\":\"windows-system-dns\",\"passed\":true},{\"name\":\"windows-direct-udp-dns\",\"passed\":true}]";
        using(var health=System.Text.Json.JsonDocument.Parse("{\"checks\":"+healthChecks+",\"adapterGuid\":\""+p.AdapterGuid+"\",\"adapterBytes\":{\"sent\":10,\"received\":20}}"))OwnedSharing.ValidateSource(health.RootElement,p);
        using(var foreign=System.Text.Json.JsonDocument.Parse("{\"checks\":"+healthChecks+",\"adapterGuid\":\""+Guid.NewGuid()+"\",\"adapterBytes\":{\"sent\":10,\"received\":20}}")) {
            bool refused=false;try{OwnedSharing.ValidateSource(foreign.RootElement,p);}catch(IOException){refused=true;}if(!refused)throw new Exception("Sharing accepted another interface's Internet");
        }
        Console.WriteLine("PASS TV source requires the exact owned Wintun identity, HTTPS/DNS and bidirectional traffic");
        var s=new SharingState(Guid.NewGuid(),13,456,Guid.NewGuid(),Guid.NewGuid(),false,
            new[]{new SharingAddress("192.168.5.1",24)},new[]{"192.168.5.1"});
        s.Validate(p);
        string fixture="""
$script:events=[Collections.Generic.List[string]]::new()
$script:addresses=[Collections.Generic.List[object]]::new()
$script:addresses.Add([pscustomobject]@{IPAddress='192.168.5.1';PrefixLength=24})
$script:addresses.Add([pscustomobject]@{IPAddress='192.168.137.1';PrefixLength=24})
$public=[pscustomobject]@{Guid='PUBLIC';SharingEnabled=$false;SharingConnectionType=-1}
$private=[pscustomobject]@{Guid='PRIVATE';SharingEnabled=$false;SharingConnectionType=-1}
$foreign=[pscustomobject]@{Guid='FOREIGN';SharingEnabled=$false;SharingConnectionType=-1}
foreach($item in @($public,$private,$foreign)){
 $item|Add-Member ScriptMethod EnableSharing {param($type) if($script:failPrivate -and $type -eq 1){throw 'injected private activation failure'};$this.SharingEnabled=$true;$this.SharingConnectionType=$type;$script:events.Add('enable'+$type)}
 $item|Add-Member ScriptMethod DisableSharing {$script:events.Add('disable'+$this.SharingConnectionType);$this.SharingEnabled=$false}
}
$script:manager=[pscustomobject]@{EnumEveryConnection=@($public,$private,$foreign)}
$script:manager|Add-Member ScriptMethod NetConnectionProps {param($c) return $c}
$script:manager|Add-Member ScriptMethod INetSharingConfigurationForINetConnection {param($c) return $c}
function New-Object {param($ComObject) if($ComObject -ne 'HNetCfg.HNetShare'){throw 'Unexpected object construction'};return $script:manager}
function Get-NetAdapter {param([switch]$IncludeHidden) [pscustomobject]@{InterfaceGuid='PRIVATE';Name='Fixture hotspot';ifIndex=13;Status='Up';InterfaceDescription='Microsoft Wi-Fi Direct Virtual Adapter'}}
function Get-NetIPInterface {param($InterfaceIndex,$AddressFamily) [pscustomobject]@{Dhcp='Disabled'}}
function Get-DnsClientServerAddress {param($InterfaceIndex,$AddressFamily) [pscustomobject]@{ServerAddresses=@('192.168.5.1')}}
function Get-NetIPAddress {param($InterfaceIndex,$AddressFamily) $script:addresses.ToArray()}
function Remove-NetIPAddress {param([Parameter(ValueFromPipeline=$true)]$InputObject,[switch]$Confirm) process{$script:events.Add('remove-'+$InputObject.IPAddress);[void]$script:addresses.Remove($InputObject)}}
function New-NetIPAddress {param($InterfaceIndex,$IPAddress,$PrefixLength,$PolicyStore) $script:addresses.Add([pscustomobject]@{IPAddress=$IPAddress;PrefixLength=[int]$PrefixLength})}
function Set-NetIPInterface {param($InterfaceIndex,$AddressFamily,$Dhcp) $script:events.Add('restore-dhcp')}
function Set-DnsClientServerAddress {param($InterfaceIndex,$ServerAddresses,[switch]$ResetServerAddresses) $script:events.Add('restore-dns')}
""".Replace("PUBLIC",p.AdapterGuid.ToString()).Replace("PRIVATE",s.PrivateGuid.ToString()).Replace("FOREIGN",Guid.NewGuid().ToString());
        string start=OwnedSharing.StartScript(p,s),stop=OwnedSharing.StopScript(p,s);
        NativeBackend.PowerShell(fixture+start+";"+stop+";if($public.SharingEnabled -or $private.SharingEnabled){throw 'ICS left enabled'};if(($script:events -join ',') -ne 'enable0,enable1,disable1,disable0,remove-192.168.137.1,restore-dhcp,restore-dns'){throw ($script:events -join ',')};if($script:addresses.Count -ne 1 -or $script:addresses[0].IPAddress -ne '192.168.5.1'){throw 'Original IP not preserved'}");
        NativeBackend.PowerShell(fixture+"$script:failPrivate=$true;try{"+start+";throw 'Expected failure'}catch{if($_.Exception.Message -notlike '*injected private activation failure*'){throw}};"+stop+";if($public.SharingEnabled -or $private.SharingEnabled){throw 'Partial activation leaked'}");
        NativeBackend.PowerShell(fixture+start+";$foreign.SharingEnabled=$true;$foreign.SharingConnectionType=0;$count=$script:events.Count;try{"+stop+";throw 'Foreign sharing accepted'}catch{if($_.Exception.Message -notlike '*ICS configuration changed*'){throw}};if($script:events.Count -ne $count -or !$foreign.SharingEnabled){throw 'Foreign sharing mutated'}");
        NativeBackend.PowerShell(fixture+"$foreign.SharingEnabled=$true;$count=$script:events.Count;try{"+start+";throw 'Existing sharing accepted'}catch{if($_.Exception.Message -notlike '*ICS ownership conflict*'){throw}};if($script:events.Count -ne $count){throw 'Pre-existing share overwritten'}");
        // MyPublicWiFi No Internet Sharing can leave only its private ICS side enabled.
        // Accept exactly that selected orphan, never an existing public/foreign share.
        NativeBackend.PowerShell(fixture+"$private.SharingEnabled=$true;$private.SharingConnectionType=1;"+start+";if(!$public.SharingEnabled -or !$private.SharingEnabled){throw 'Selected orphan was not completed'};if(($script:events -join ',') -ne 'disable1,enable0,enable1'){throw 'Orphan private was not reset before public activation'};"+stop);
        NativeBackend.PowerShell(fixture+"$foreign.SharingEnabled=$true;$foreign.SharingConnectionType=1;try{"+start+";throw 'Foreign private accepted'}catch{if($_.Exception.Message -notlike '*ICS ownership conflict*'){throw}};if($script:events.Count){throw 'Foreign private share changed'}");
        Console.WriteLine("PASS selected orphan private ICS is completed before public activation; foreign private ICS refused");
        NativeBackend.PowerShell(fixture+start+";$script:events.Clear();"+start+";if($script:events.Count){throw 'Exact existing ICS pair was restarted'};"+stop);
        Console.WriteLine("PASS exact TetherMax/public and selected/private ICS pair is idempotently adopted; no foreign source admitted");
        string firewallFixture="""
$script:rules=@{'foreign-rule'=[pscustomobject]@{Name='foreign-rule'}}
function Get-NetFirewallRule {param($Name,$PolicyStore,$ErrorAction) if($PolicyStore -ne 'ActiveStore'){throw 'Persistent firewall store used'};if($script:rules.ContainsKey($Name)){$script:rules[$Name]}}
function New-NetFirewallRule {param($Name,$DisplayName,$Group,$PolicyStore,$Direction,$Action,$Profile,$InterfaceAlias,$RemoteAddress,$Protocol,$LocalPort)
 if($Action -ne 'Allow' -or $PolicyStore -ne 'ActiveStore' -or $InterfaceAlias -ne 'Fixture hotspot' -or $RemoteAddress -ne '192.168.137.0/24'){throw 'Firewall scope escaped hotspot'}
 if($Protocol -notin @('TCP','UDP')){throw 'Unbounded protocol'}
 if($Protocol -eq 'TCP' -and (($LocalPort -join ',') -ne '80,443')){throw 'Unexpected TCP scope'}
 if($Protocol -eq 'UDP' -and (($LocalPort -join ',') -ne '53,67,68')){throw 'Unexpected UDP scope'}
 $script:rules[$Name]=[pscustomobject]@{Name=$Name}
}
function Remove-NetFirewallRule {param([Parameter(ValueFromPipeline=$true)]$InputObject) process {if($InputObject.Name -eq 'foreign-rule'){throw 'Foreign rule removed'};$script:rules.Remove($InputObject.Name)}}
""";
        NativeBackend.PowerShell(fixture+firewallFixture+OwnedSharing.FirewallScript(p,s,true)+OwnedSharing.FirewallScript(p,s,true)+"if($script:rules.Count -ne 5){throw 'Firewall rules not idempotent'};"+OwnedSharing.FirewallScript(p,s,false)+"if($script:rules.Count -ne 1 -or !$script:rules.ContainsKey('foreign-rule')){throw 'Firewall ownership cleanup failed'}");
        Console.WriteLine("PASS hotspot-only temporary DNS/web firewall rules: idempotent startup and owned cleanup; mocked cmdlets only");
        bool rejected=false;try{(s with{Addresses=new[]{new SharingAddress("1.2.3.4';throw 'injection",24)}}).Validate(p);}catch(IOException){rejected=true;}if(!rejected)throw new Exception("Untrusted saved address accepted");
        Console.WriteLine("PASS ICS lifecycle: private-before-public shutdown, DHCP/IP restoration, partial activation rollback, foreign-sharing refusal and journal input validation; mocked COM only");
    }
}
