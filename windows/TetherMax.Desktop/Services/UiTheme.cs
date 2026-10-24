using System.Windows.Media;
using System.Windows.Media.Animation;
namespace TetherMax;
internal static class UiTheme {
 public static readonly SolidColorBrush Background=Make(Color.FromRgb(12,20,36));
 public static readonly SolidColorBrush Surface=Make(Color.FromRgb(25,38,59));
 public static readonly SolidColorBrush Ink=Make(Color.FromRgb(229,239,250));
 public static readonly SolidColorBrush Muted=Make(Color.FromRgb(173,187,208));
 public static readonly SolidColorBrush Accent=Make(Color.FromRgb(85,220,231));
 public static readonly SolidColorBrush Button=Make(Color.FromRgb(32,64,90));
 static SolidColorBrush Make(Color color){var brush=new SolidColorBrush(color);Change(brush,color);return brush;}
 static void Change(SolidColorBrush brush,Color color){brush.BeginAnimation(SolidColorBrush.ColorProperty,null);brush.Color=color;brush.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(color,color,TimeSpan.Zero));}
 public static bool Light {get;private set;}
 public static void SetLight(bool light){Light=light;Change(Background,(Color)ColorConverter.ConvertFromString(light?"#EFF3F8":"#0C1424"));Change(Surface,(Color)ColorConverter.ConvertFromString(light?"#FFFFFF":"#19263B"));Change(Ink,(Color)ColorConverter.ConvertFromString(light?"#12213A":"#E5EFFA"));Change(Muted,(Color)ColorConverter.ConvertFromString(light?"#52637B":"#ADBBD0"));Change(Accent,(Color)ColorConverter.ConvertFromString(light?"#007D85":"#55DCE7"));Change(Button,(Color)ColorConverter.ConvertFromString(light?"#D7E8F0":"#20405A"));}
}

