using System.Text.Json.Serialization;
namespace TetherMax.Network;
public sealed record LiveTrafficSettings(
 [property:JsonPropertyName("mode")]string Mode="auto",
 [property:JsonPropertyName("uploadMbps")]double UploadMbps=0,
 [property:JsonPropertyName("downloadMbps")]double DownloadMbps=0,
 [property:JsonPropertyName("adaptive")]bool Adaptive=false,
 [property:JsonPropertyName("dnsProfile")]string DnsProfile="adguard") {
 public void Validate(){if(!new[]{"auto","gaming","streaming","daily","maximum"}.Contains(Mode)||!new[]{"system","adguard","family","cloudflare"}.Contains(DnsProfile)||!double.IsFinite(UploadMbps)||!double.IsFinite(DownloadMbps)||UploadMbps<0||UploadMbps>10000||DownloadMbps<0||DownloadMbps>10000||(Adaptive&&UploadMbps==0))throw new IOException("Geçersiz canlı trafik ayarı.");}
 public byte[] Frame(){Validate();byte[] json=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(this);if(json.Length>2048)throw new IOException("Settings frame too large");var frame=new byte[json.Length+3];frame[0]=1;System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(1,2),(ushort)json.Length);json.CopyTo(frame,3);return frame;}
}
