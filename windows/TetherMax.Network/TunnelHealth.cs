namespace TetherMax;
public static class TunnelHealth {
 public static bool Healthy(System.Text.Json.JsonElement result,Guid? expected=null) {
  if(!result.TryGetProperty("checks",out var checks)||checks.ValueKind!=System.Text.Json.JsonValueKind.Array||checks.GetArrayLength()!=3)return false;
  var required=new HashSet<string>{"windows-https-no-proxy","windows-system-dns","windows-direct-udp-dns"};
  foreach(var check in checks.EnumerateArray()) {
   if(!check.TryGetProperty("name",out var name)||name.ValueKind!=System.Text.Json.JsonValueKind.String||!required.Remove(name.GetString()!)||!check.TryGetProperty("passed",out var passed)||passed.ValueKind!=System.Text.Json.JsonValueKind.True)return false;
  }
  return required.Count==0 && result.TryGetProperty("adapterGuid",out var adapter) && adapter.ValueKind==System.Text.Json.JsonValueKind.String && Guid.TryParse(adapter.GetString(),out var id) && id!=Guid.Empty && (!expected.HasValue || id==expected.Value)
   && result.TryGetProperty("adapterBytes",out var bytes) && bytes.TryGetProperty("sent",out var sent) && sent.TryGetInt64(out var tx) && tx>0
   && bytes.TryGetProperty("received",out var received) && received.TryGetInt64(out var rx) && rx>0;
 }

}
