using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
namespace TetherMax;
internal static class NativeNavigation {
 public static void Apply(TabControl navigation){
  string[] labels=["Genel Bakış","Bağlantı","Oyun","Cihazlar","Analiz","Hotspot","Ayarlar","Tanılama"];
  string[] icons=["M3,11 L12,3 21,11 M5,10 L5,21 10,21 10,15 14,15 14,21 19,21 19,10","M3,7 L20,7 M16,3 L20,7 16,11 M21,17 L4,17 M8,13 L4,17 8,21","M7,7 C2,7 0,20 4,20 L9,16 15,16 20,20 C24,20 22,7 17,7 Z M5,11 L11,11 M8,8 L8,14 M16,11 L16,11.2 M19,13 L19,13.2","M6,2 L18,2 18,22 6,22 Z M10,19 L14,19","M4,20 L4,13 8,13 8,20 Z M10,20 L10,8 14,8 14,20 Z M16,20 L16,3 20,3 20,20 Z","M2,9 C7,3 17,3 22,9 M5,13 C9,9 15,9 19,13 M9,17 C11,15 13,15 15,17 M12,20 L12,20.2","M12,7 A5,5 0 1 1 11.99,7 M12,1 L12,4 M12,20 L12,23 M1,12 L4,12 M20,12 L23,12 M4,4 L6,6 M18,18 L20,20 M4,20 L6,18 M18,6 L20,4","M5,3 L19,3 19,22 5,22 Z M9,1 L15,1 15,5 9,5 Z M8,10 L16,10 M8,14 L16,14 M8,18 L13,18"];
  for(int i=0;i<Math.Min(labels.Length,navigation.Items.Count);i++)if(navigation.Items[i] is TabItem item){
   var row=new StackPanel{Orientation=Orientation.Horizontal};var icon=new Path{Data=Geometry.Parse(icons[i]),Width=18,Height=18,Stretch=Stretch.Uniform,StrokeThickness=1.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Margin=new Thickness(0,0,12,0)};icon.SetBinding(Shape.StrokeProperty,new Binding("Foreground"){Source=item});var title=new TextBlock{Text=labels[i],VerticalAlignment=VerticalAlignment.Center,FontSize=13};title.SetBinding(TextBlock.ForegroundProperty,new Binding("Foreground"){Source=item});row.Children.Add(icon);row.Children.Add(title);item.Header=row;System.Windows.Automation.AutomationProperties.SetName(item,labels[i]);
  }
 }
}
