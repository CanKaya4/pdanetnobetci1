using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace TetherMax;
internal sealed class App : Application {
 [STAThread] public static void Main(string[] args) {
  var app=new App();app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("Themes/Dark.xaml",UriKind.Relative) });
  foreach(var item in new[]{("BackgroundBrush",UiTheme.Background),("InkBrush",UiTheme.Ink),("ButtonBrush",UiTheme.Button)})app.Resources[item.Item1]=item.Item2;
  if(args.Length>=2&&args[0]=="--render-preview") {
   UiTheme.SetLight(args.Contains("light"));Window window=args.Contains("hotspot")?new Window{Content=new System.Windows.Controls.ScrollViewer{Content=new SharingPanel(()=>null),VerticalScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility.Auto},Background=UiTheme.Background}:new Dashboard(true);var visual=(FrameworkElement)window.Content;visual.Measure(new Size(1120,850));visual.Arrange(new Rect(0,0,1120,850));visual.UpdateLayout();
   var image=new RenderTargetBitmap(1120,850,96,96,PixelFormats.Pbgra32);image.Render(visual);
   var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(args[1]))encoder.Save(file);window.Close();app.Shutdown();return;
  }
  app.Run(new Dashboard(false,args.Contains("--manual-start")));
 }
}
