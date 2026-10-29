using System.Text.Json;
namespace TetherMax;

internal static class NetworkDoctor {
    static double? Number(JsonElement data,string key)=>data.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out double n)&&double.IsFinite(n)?n:null;
    public static string Explain(JsonElement data) {
        var lines=new List<string>();
        if(data.TryGetProperty("phoneNetwork",out var network)&&network.ValueKind==JsonValueKind.String&&
           (network.GetString()??"").Contains("UNAVAILABLE",StringComparison.OrdinalIgnoreCase))
            lines.Add("Telefon hücresel ağın kullanılamadığını bildiriyor. Sinyal, veri izni ve telefon ağ durumunu kontrol edin.");
        if(Number(data,"QueryRTTMs")==null)
            lines.Add("Güncel DNS gidiş-dönüş ölçümü yok. Ölçüm kapalı veya yanıt alınamıyor olabilir; bu tek başına DNS arızası kanıtı değildir.");
        if(Number(data,"ProbeSamples")>=20&&Number(data,"ProbeLossPercent")>=10)
            lines.Add("DNS denemelerinde yanıtsızlık var. USB, hücresel ağ veya DNS hizmetini ayırmak için mevcut İnternet / ağ yolu testlerini kullanın.");
        if(Number(data,"QueueP95Ms")>20)
            lines.Add("TetherMax kuyruğunda bekleme gözlendi. Aktarım yükünü ve hız tavanını kontrol edin; bu ölçüm oyun pingi değildir.");
        if(Number(data,"OverlimitDrops")>0)
            lines.Add("Bu motor oturumunda yerel kuyruk sınırına ulaşıldı. Sayaç geçmişi gösterir; şu anda kayıp sürdüğünü kanıtlamaz.");
        if(Number(data,"VisibleHosts")<=1)
            lines.Add("En fazla tek kaynak IP görülüyor. ICS/NAT arkasındaki cihazların ayrı tüketimi bu noktada ölçülemeyebilir.");
        lines.Add("Bu açıklama mevcut motor telemetrisine dayanır. USB kablo arızası, operatör sorunu veya uçtan uca internet sağlığı tek başına bu kayıttan kesinleştirilemez.");
        return string.Join("\n\n",lines);
    }
}
