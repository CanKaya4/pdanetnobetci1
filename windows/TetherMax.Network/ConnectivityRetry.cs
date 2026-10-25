using System.Text.Json;
namespace TetherMax.Network;

public static class ConnectivityRetry {
    public static async Task<string> Run(Func<CancellationToken,Task<string>> probe,Guid adapter,CancellationToken cancellation,
        Func<int,CancellationToken,Task>? delay=null) {
        // Same adapter/worker/route throughout. No service restart, re-pair or
        // fallback network; only retry the existing bounded verification batch.
        for(int attempt=0;;attempt++) {
            cancellation.ThrowIfCancellationRequested();
            string result=await probe(cancellation);cancellation.ThrowIfCancellationRequested();
            using var parsed=JsonDocument.Parse(result);
            if(TetherMax.TunnelHealth.Healthy(parsed.RootElement,adapter)||attempt==2)return result;
            if(delay!=null)await delay(attempt,cancellation);
            else await Task.Delay(TimeSpan.FromSeconds(attempt+1),cancellation);
        }
    }
}
