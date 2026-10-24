using System.Text.Json.Serialization;
namespace TetherMax;
internal sealed record TrafficSettings(
    [property:JsonPropertyName("mode")] string Mode="auto",
    [property:JsonPropertyName("uploadMbps")] double UploadMbps=0,
    [property:JsonPropertyName("downloadMbps")] double DownloadMbps=0,
    [property:JsonPropertyName("adaptive")] bool Adaptive=false,
    [property:JsonPropertyName("dnsProfile")] string DnsProfile="adguard") {
    public bool Acknowledged(System.Text.Json.JsonElement data){
        if(!data.TryGetProperty("phoneSettingsConfirmed",out var ok)||ok.ValueKind!=System.Text.Json.JsonValueKind.True||!data.TryGetProperty("settings",out var settings))return false;
        try{return System.Text.Json.JsonSerializer.Deserialize<TrafficSettings>(settings)==this;}catch(System.Text.Json.JsonException){return false;}
    }
}

