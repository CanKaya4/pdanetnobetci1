using System.IO;
using System.Text.Json.Serialization;
namespace TetherMax;

public sealed record FocusProcess([property:JsonPropertyName("pid")]uint Pid,[property:JsonPropertyName("started")]ulong Started);
public sealed record GameFocusSettings(
 [property:JsonPropertyName("enabled")]bool Enabled=false,
 [property:JsonPropertyName("processes")]FocusProcess[]? Processes=null,
 [property:JsonPropertyName("backgroundMbps")]double BackgroundMbps=2) {
 public void Validate(){var ids=Processes??[];if(ids.Length>16||!double.IsFinite(BackgroundMbps)||BackgroundMbps<.25||BackgroundMbps>10||ids.Any(p=>p.Pid<=4||p.Started==0)||ids.Select(p=>p.Pid).Distinct().Count()!=ids.Length)throw new IOException("Geçersiz oyun odağı kimliği veya bütçesi.");}
 public bool Equals(GameFocusSettings? other)=>other!=null&&Enabled==other.Enabled&&BackgroundMbps==other.BackgroundMbps&&(Processes??[]).SequenceEqual(other.Processes??[]);
 public override int GetHashCode(){var hash=new HashCode();hash.Add(Enabled);hash.Add(BackgroundMbps);foreach(var p in Processes??[])hash.Add(p);return hash.ToHashCode();}
}
