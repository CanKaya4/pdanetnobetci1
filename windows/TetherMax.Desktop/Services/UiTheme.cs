using System.Windows;
using System.Windows.Media;
namespace TetherMax;
internal static class UiTheme {
 public static readonly SolidColorBrush Background=new(),Surface=new(),Ink=new(),Muted=new(),Accent=new(),Button=new();
 public static bool Light {get;private set;}=true;
 public static int Motion {get;set;}=1;
 public static bool GameSession {get;set;}
 public static bool Transparency {get;set;}=true;
 public static bool Animate=>!GameSession&&Motion>0&&SystemParameters.ClientAreaAnimation&&!SystemParameters.HighContrast;
 static UiTheme(){SetLight(true);}
 public static void SetLight(bool light){Light=light;
  var brushes=new[]{Background,Surface,Ink,Muted,Accent,Button};
  var colors=light?new[]{"#FBF7F5","#FFFCFA","#3B2336","#786670","#A64F68","#F5E5E8"}:new[]{"#20191F","#2C222B","#FFF4F4","#C5ADB9","#ECA5B9","#4A303F"};
  for(int i=0;i<brushes.Length;i++){var color=(Color)ColorConverter.ConvertFromString(colors[i]);brushes[i].BeginAnimation(SolidColorBrush.ColorProperty,null);brushes[i].Color=color;brushes[i].BeginAnimation(SolidColorBrush.ColorProperty,new System.Windows.Media.Animation.ColorAnimation(color,color,TimeSpan.Zero));}
  Surface.Opacity=Transparency?0.96:1;
 }
 public static string Bytes(double bytes){string[] units={"B","KB","MB","GB","TB"};int i=0;while(bytes>=1000&&i<4){bytes/=1000;i++;}return $"{bytes:0.##} {units[i]}";}
}
