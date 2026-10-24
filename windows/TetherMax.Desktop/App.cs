using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace TetherMax;
internal sealed class App : Application {
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr window,int command);
 static bool RaiseSameVersion(){using var self=Process.GetCurrentProcess();string? version=typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];foreach(var process in Process.GetProcessesByName("TetherMax")){using(process){try{if(process.Id==self.Id||process.SessionId!=self.SessionId||process.MainWindowHandle==IntPtr.Zero||process.MainModule?.FileVersionInfo.ProductVersion?.Split('+')[0]!=version)continue;ShowWindowAsync(process.MainWindowHandle,9);SetForegroundWindow(process.MainWindowHandle);return true;}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}}}return false;}

 [STAThread] public static void Main(string[] args) {
  bool preview=args.Length>=2&&args[0]=="--render-preview";
  using var single=new Mutex(false,@"Local\TetherMax.Desktop.rc3");bool owns=false;
  using var activation=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\TetherMax.Desktop.rc3.Activate");
  if(!preview){if(RaiseSameVersion())return;try{owns=single.WaitOne(0);}catch(AbandonedMutexException){owns=true;}if(!owns){activation.Set();return;}}
  var app=new App();app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("Themes/Dark.xaml",UriKind.Relative) });
  foreach(var item in new[]{("BackgroundBrush",UiTheme.Background),("InkBrush",UiTheme.Ink),("ButtonBrush",UiTheme.Button)})app.Resources[item.Item1]=item.Item2;
  if(args.Length>=2&&args[0]=="--render-preview") {
   UiTheme.SetLight(args.Contains("light"));Window window=args.Contains("hotspot")?new Window{Content=new System.Windows.Controls.ScrollViewer{Content=new SharingPanel(()=>null),VerticalScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility.Auto},Background=UiTheme.Background}:new Dashboard(true);var visual=(FrameworkElement)window.Content;visual.Measure(new Size(1120,850));visual.Arrange(new Rect(0,0,1120,850));visual.UpdateLayout();
   var image=new RenderTargetBitmap(1120,850,96,96,PixelFormats.Pbgra32);image.Render(visual);
   var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(args[1]))encoder.Save(file);window.Close();app.Shutdown();return;
  }
  var dashboard=new Dashboard(false,args.Contains("--manual-start"));
  var wake=ThreadPool.RegisterWaitForSingleObject(activation,(_,_)=>app.Dispatcher.BeginInvoke(()=>{dashboard.Show();if(dashboard.WindowState==WindowState.Minimized)dashboard.WindowState=WindowState.Normal;dashboard.Activate();}),null,Timeout.Infinite,false);
  try{app.Run(dashboard);}finally{wake.Unregister(null);if(owns)single.ReleaseMutex();}
 }
}
