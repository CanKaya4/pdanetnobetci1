using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace TetherMax;
internal sealed class MetricsDashboard : WrapPanel {
 readonly Dictionary<string,TextBlock> values=new();
 public MetricsDashboard() {
  foreach(var item in new[]{("DownloadMbps","↓ İndirme","Mbps"),("UploadMbps","↑ Yükleme","Mbps"),("QueryRTTMs","RTT","ms"),("JitterMs","Jitter","ms"),("Downloaded","İndirilen veri","B"),("ActiveFlows","Aktif akış","akış")}) {
   var panel=new StackPanel();panel.Children.Add(new TextBlock{Text=item.Item2,Foreground=UiTheme.Muted,FontSize=13});
   var value=new TextBlock{Text="Ölçülmedi",FontSize=25,Margin=new Thickness(0,12,0,6)};values[item.Item1]=value;panel.Children.Add(value);panel.Children.Add(new TextBlock{Text=item.Item3,Foreground=UiTheme.Accent,FontSize=12});
   Children.Add(new Border{Child=panel,Width=195,Padding=new Thickness(20),Margin=new Thickness(0,0,12,12),CornerRadius=new CornerRadius(18),Background=UiTheme.Surface});
  }
 }
 public void Clear(){foreach(var text in values.Values)text.Text="Ölçülmedi";}
 public void Accept(JsonElement data) {foreach(var pair in values)pair.Value.Text=data.TryGetProperty(pair.Key,out var value)&&value.ValueKind==JsonValueKind.Number?value.GetDouble().ToString("0.##"):"Ölçülmedi";}
}
