using TetherMax.Network;

static class SharingChecks
{
    public static void Run(Plan p)
    {
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
function Get-NetAdapter {param([switch]$IncludeHidden) [pscustomobject]@{InterfaceGuid='PRIVATE';ifIndex=13;Status='Up';InterfaceDescription='Microsoft Wi-Fi Direct Virtual Adapter'}}
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
        bool rejected=false;try{(s with{Addresses=new[]{new SharingAddress("1.2.3.4';throw 'injection",24)}}).Validate(p);}catch(IOException){rejected=true;}if(!rejected)throw new Exception("Untrusted saved address accepted");
        Console.WriteLine("PASS ICS lifecycle: private-before-public shutdown, DHCP/IP restoration, partial activation rollback, foreign-sharing refusal and journal input validation; mocked COM only");
    }
}
