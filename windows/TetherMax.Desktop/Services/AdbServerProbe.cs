using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Globalization;
using System.Text;
namespace TetherMax;

internal enum AdbServerState { Missing, Compatible, Conflict }
internal static class AdbServerProbe {
    public static async Task<AdbServerState> Inspect(int port=5037) {
        // Windows can retry a refused connect longer than the probe deadline.
        // An empty listener table is a prompt, read-only missing-server result.
        try{if(!IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p=>p.Port==port))return AdbServerState.Missing;}
        catch(NetworkInformationException){return AdbServerState.Conflict;}
        using var deadline=new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
        using var client=new TcpClient(AddressFamily.InterNetwork);
        try {
            await client.ConnectAsync(IPAddress.Loopback,port,deadline.Token);
            var stream=client.GetStream();await stream.WriteAsync(Encoding.ASCII.GetBytes("000chost:version"),deadline.Token);
            byte[] header=new byte[8];await stream.ReadExactlyAsync(header,deadline.Token);
            if(Encoding.ASCII.GetString(header,0,4)!="OKAY"||!int.TryParse(Encoding.ASCII.GetString(header,4,4),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out int size)||size<1||size>16)return AdbServerState.Conflict;
            byte[] version=new byte[size];await stream.ReadExactlyAsync(version,deadline.Token);
            return int.TryParse(Encoding.ASCII.GetString(version),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out int protocol)&&protocol==41?AdbServerState.Compatible:AdbServerState.Conflict;
        }catch(SocketException error)when(error.SocketErrorCode==SocketError.ConnectionRefused){return AdbServerState.Missing;}
        catch(Exception error)when(error is IOException or SocketException or OperationCanceledException){return AdbServerState.Conflict;}
    }
}
