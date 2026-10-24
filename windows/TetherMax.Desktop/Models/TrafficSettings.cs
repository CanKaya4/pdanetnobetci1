using System.Text.Json.Serialization;
namespace TetherMax;
internal sealed record TrafficSettings(
    [property:JsonPropertyName("mode")] string Mode="auto",
    [property:JsonPropertyName("uploadMbps")] double UploadMbps=0,
    [property:JsonPropertyName("downloadMbps")] double DownloadMbps=0,
    [property:JsonPropertyName("adaptive")] bool Adaptive=false);

