using TetherMax.Network;

static class MockCmdlets
{
    public static void Run(Plan p)
    {
        // Every network command is a function backed by memory. AST admission
        // rejects future commands before the production script is evaluated.
        string body=NativeBackend.ConfigureScript(p)+";"+NativeBackend.RemoveScript(p);
        string encoded=Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(body));
        string mocks="""
        $script:addresses=@();$script:routes=@();$script:rule=$null
        function Get-NetAdapter {[CmdletBinding()]param([switch]$IncludeHidden) [pscustomobject]@{InterfaceGuid='{EXPECTED-GUID}';Name='TetherMax-IP-aabbccdd';ifIndex=42}}
        function Get-NetIPAddress {[CmdletBinding()]param($InterfaceIndex,$AddressFamily)
          if($null -ne $InterfaceIndex -and $InterfaceIndex -ne 42){throw 'wrong interface'}
          if($null -ne $InterfaceIndex -and $script:addresses.Count -eq 0){Write-Error 'empty fresh interface' -ErrorId CmdletizationQuery_NotFound;return}
          $script:addresses
        }
        function Set-NetIPInterface {[CmdletBinding()]param($InterfaceIndex,$AddressFamily,$Dhcp,$AutomaticMetric,$InterfaceMetric,$NlMtuBytes) if($InterfaceIndex -ne 42){throw 'wrong interface'}}
        function New-NetIPAddress {[CmdletBinding()]param($InterfaceIndex,$AddressFamily,$IPAddress,$PrefixLength,$PolicyStore)
          if($InterfaceIndex -ne 42 -or $PolicyStore -ne 'ActiveStore'){throw 'wrong IP scope'}
          $script:addresses+= [pscustomobject]@{IPAddress=$IPAddress;PrefixLength=$PrefixLength}
        }
        function Set-DnsClientServerAddress {[CmdletBinding()]param($InterfaceIndex,$ServerAddresses,[switch]$ResetServerAddresses) if($InterfaceIndex -ne 42){throw 'wrong DNS interface'}}
        function Get-DnsClientNrptRule {[CmdletBinding()]param() if($null -ne $script:rule){$script:rule}}
        function Add-DnsClientNrptRule {[CmdletBinding()]param($Namespace,$NameServers,$DisplayName,$Comment)
          $script:rule=[pscustomobject]@{Namespace=@($Namespace);NameServers=[string]$NameServers;DisplayName=$DisplayName;Comment=$Comment;Name='mock-rule'}
        }
        function Remove-DnsClientNrptRule {[CmdletBinding()]param($Name,[switch]$Force) if($Name -ne 'mock-rule'){throw 'wrong rule'};$script:rule=$null}
        function New-NetRoute {[CmdletBinding()]param($InterfaceIndex,$DestinationPrefix,$NextHop,$RouteMetric,$PolicyStore)
          if($InterfaceIndex -ne 42 -or $PolicyStore -ne 'ActiveStore'){throw 'wrong route scope'}
          $script:routes+=[pscustomobject]@{DestinationPrefix=$DestinationPrefix;NextHop=$NextHop;RouteMetric=$RouteMetric}
        }
        function Get-NetRoute {[CmdletBinding()]param($InterfaceIndex,$DestinationPrefix,$PolicyStore)
          if($InterfaceIndex -ne 42){throw 'wrong query'}
          $script:routes|Where-Object {$_.DestinationPrefix -eq $DestinationPrefix}
        }
        function Remove-NetRoute {[CmdletBinding(SupportsShouldProcess)]param([Parameter(ValueFromPipeline)]$InputObject) process {
          $script:routes=@($script:routes|Where-Object {$_.DestinationPrefix -ne $InputObject.DestinationPrefix})
        }}
        function Remove-NetIPAddress {[CmdletBinding(SupportsShouldProcess)]param([Parameter(ValueFromPipeline)]$InputObject) process {
          $script:addresses=@($script:addresses|Where-Object {$_.IPAddress -ne $InputObject.IPAddress})
        }}
        function Clear-DnsClientCache {[CmdletBinding()]param()}
        function Find-NetRoute {[CmdletBinding()]param($RemoteIPAddress) [pscustomobject]@{InterfaceIndex=42}}
        """.Replace("EXPECTED-GUID",p.AdapterGuid.ToString());
        string check="""
        $body=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('ENCODED'))
        $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseInput($body,[ref]$tokens,[ref]$errors)
        if($errors.Count){throw 'configuration syntax failed'}
        $allowed=@('Get-NetAdapter','Get-NetIPAddress','Set-NetIPInterface','New-NetIPAddress','Set-DnsClientServerAddress','Get-DnsClientNrptRule','Add-DnsClientNrptRule','Remove-DnsClientNrptRule','New-NetRoute','Get-NetRoute','Remove-NetRoute','Remove-NetIPAddress','Clear-DnsClientCache','Find-NetRoute','OptionalIPv4','OptionalRoute','Where-Object','Out-Null')
        foreach($node in $ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst]},$true)) {
          if($node.GetCommandName() -notin $allowed){throw ('Unmocked command refused: '+$node.GetCommandName())}
        }
        & ([scriptblock]::Create($body))
        if($script:addresses.Count -ne 0 -or $script:routes.Count -ne 0 -or $null -ne $script:rule){throw 'Mock rollback left state behind'}
        """.Replace("ENCODED",encoded);
        NativeBackend.PowerShell(mocks+check);
        Console.WriteLine("PASS real PowerShell script parsing, fresh empty CIM state, string GUID and scalar DNS rollback (all network cmdlets mocked)");
    }
}
