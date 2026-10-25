using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
class Launcher:Application {
 [STAThread]public static void Main()=>new Launcher().Run(new TestWindow());
}
class TestWindow:Window {
 readonly TextBlock status=new(){Text="Test henüz başlamadı.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,18,0,0)};
 public TestWindow(){
  Title="TetherMax · Onaylı sınırlı Wintun testi";Width=650;Height=420;ResizeMode=ResizeMode.CanMinimize;WindowStartupLocation=WindowStartupLocation.CenterScreen;
  var panel=new StackPanel{Margin=new Thickness(28)};Content=panel;
  panel.Children.Add(new TextBlock{Text="Sınırlı adaptör testi",FontSize=27,FontWeight=FontWeights.Bold});
  panel.Children.Add(new TextBlock{Text="Yalnızca TetherMax-Test-* adaptörü oluşturulur; Wintun oturumu açılıp kapatılır ve oluşturulan adaptör kaldırılır. IP, rota, DNS, Wi-Fi, PdaNet, MyPublicWiFi, ICS ve firewall yapılandırılmaz. İmzalı sürücü paketi sistemde kalabilir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,18,0,18)});
  var button=new Button{Content="Onayladığım sınırlı testi başlat",Padding=new Thickness(16)};panel.Children.Add(button);panel.Children.Add(status);
  button.Click+=async(_,_)=>{
   button.IsEnabled=false;
   try{
    string exe=Path.Combine(AppContext.BaseDirectory,"probe","TetherMax.WintunProbe.exe");
    status.Text="Windows yönetici onayı bekleniyor. UAC ekranını yalnızca siz onaylayabilirsiniz.";
    var info=new ProcessStartInfo(exe){UseShellExecute=true,Verb="runas",Arguments="--apply --approve-adapter-test",WorkingDirectory=Path.GetDirectoryName(exe)!,WindowStyle=ProcessWindowStyle.Hidden,ErrorDialog=true,ErrorDialogParentHandle=new WindowInteropHelper(this).Handle};
    using var p=Process.Start(info)??throw new IOException("Test süreci açılamadı.");
    await p.WaitForExitAsync();
    status.Text="Test süreci tamamlandı. Çıkış kodu: "+p.ExitCode+". Gerçek ağ durumu ve temizlik kayıtları ayrıca doğrulanacak.";
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"launcher-result.txt"),"Exit="+p.ExitCode);
   }catch(Exception ex){status.Text="Başlatılamadı: "+ex.Message;File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"launcher-result.txt"),"Launch failed: "+ex.Message);}
   finally{button.IsEnabled=true;}
  };
 }
}
