using System.Text.Json;
namespace TetherMax;

// A damaged diagnostic sample must not masquerade as an IPC transport failure.
internal static class NetworkReport {
    public static bool TryParse(string line,out JsonDocument? document) {
        document=null;
        if(line.Length>262144)return false;
        try {
            var parsed=JsonDocument.Parse(line);var root=parsed.RootElement;
            bool Has(string name,JsonValueKind kind)=>root.TryGetProperty(name,out var value)&&value.ValueKind==kind;
            if(root.ValueKind!=JsonValueKind.Object||!Has("type",JsonValueKind.String)){parsed.Dispose();return false;}
            string type=root.GetProperty("type").GetString()!;
            bool valid=type switch {
                "network-active"=>Has("guid",JsonValueKind.String)&&root.GetProperty("guid").TryGetGuid(out var guid)&&guid!=Guid.Empty&&Has("session",JsonValueKind.String),
                "windows-connectivity"=>Has("result",JsonValueKind.Object),
                "network-stopped"=>root.TryGetProperty("recovered",out var flag)&&flag.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "sharing-state"=>root.TryGetProperty("configured",out var flag)&&flag.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "sharing-error" or "network-error"=>Has("message",JsonValueKind.String),
                "ip-counters"=>Has("payload",JsonValueKind.Object),
                _=>true
            };
            if(!valid){parsed.Dispose();return false;}document=parsed;return true;
        }catch(JsonException){return false;}
    }
}
