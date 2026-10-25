using System.Diagnostics;
using System.IO;
namespace TetherMax;

internal static class SessionLifecycle
{
    // A failed automatic cleanup must wait for an explicit retry. Otherwise
    // every UI timer tick can launch another elevated helper.
    public static bool ShouldStopHotspot(bool active,bool hasHotspot,bool changing,bool recoveryPending)
        => !active && hasHotspot && !changing && !recoveryPending;

    public static async Task WaitForHelper(Task connected,Process helper,CancellationToken cancellation)
    {
        using var waiting=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var exited=helper.WaitForExitAsync(waiting.Token);
        try {
            await Task.WhenAny(connected,exited).WaitAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            // Prefer an established pipe when the helper exits immediately after
            // publishing its result. Otherwise fail promptly, not after 45 s.
            if(!connected.IsCompletedSuccessfully && helper.HasExited)
                throw new IOException("Ağ yardımcısı bağlanmadan kapandı (kod "+helper.ExitCode+"). Başka bir oturum veya kurtarma işlemi sürüyor olabilir.");
            await connected;
        } finally {waiting.Cancel();try{await exited;}catch(OperationCanceledException){}}
    }
}
