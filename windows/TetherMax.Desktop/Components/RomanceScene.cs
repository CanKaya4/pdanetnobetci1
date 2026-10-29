using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Diagnostics;
namespace TetherMax;

// Cached Imagegen poses + bounded native transitions; no frame timer during games.
internal sealed class RomanceScene : FrameworkElement {
 static readonly BitmapImage art=Load("TetherMax.Romance"),sleep=Load("TetherMax.Romance.Sleep");
 static readonly SolidColorBrush lavender=new(Color.FromRgb(168,143,188)),stem=new(Color.FromRgb(139,148,119));
 readonly DispatcherTimer timer=new();readonly Stopwatch clock=Stopwatch.StartNew();
 string mood="idle";bool paused;double blend,from,target,transitionAt,phase;
 public string Mood{get=>mood;set{if(mood==value)return;mood=value;from=blend;target=value=="connected"||value=="hotspot"?1:0;transitionAt=clock.Elapsed.TotalSeconds;if(!CanAnimate){blend=target;InvalidateVisual();}UpdateTimer();}}
 bool CanAnimate=>!paused&&IsLoaded&&IsVisible&&UiTheme.Animate;
 internal void PauseForGame(bool value){paused=value;if(value){blend=target;InvalidateVisual();}UpdateTimer();}
 void UpdateTimer(){if(CanAnimate){timer.Interval=TimeSpan.FromMilliseconds(UiTheme.Motion==2?33:100);timer.Start();}else timer.Stop();}
 public RomanceScene(){Height=180;IsHitTestVisible=false;System.Windows.Automation.AutomationProperties.SetName(this,"Kahverengi gözlü gri kedi ve yeşil gözlü sarı kedi");timer.Tick+=(_,_)=>{if(!CanAnimate){timer.Stop();blend=target;InvalidateVisual();return;}phase=clock.Elapsed.TotalSeconds;double t=Math.Clamp((phase-transitionAt)/.85,0,1);blend=from+(target-from)*(t*t*(3-2*t));InvalidateVisual();};Loaded+=(_,_)=>UpdateTimer();Unloaded+=(_,_)=>timer.Stop();IsVisibleChanged+=(_,_)=>UpdateTimer();}
 static BitmapImage Load(string name){using var source=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)??throw new InvalidOperationException("Romantik arayüz görseli bulunamadı.");var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=640;bitmap.StreamSource=source;bitmap.EndInit();bitmap.Freeze();return bitmap;}
 protected override void OnRender(DrawingContext drawing){base.OnRender(drawing);double width=Math.Min(ActualWidth,ActualHeight*art.PixelWidth/art.PixelHeight),height=width*art.PixelHeight/art.PixelWidth;double sway=CanAnimate?Math.Sin(phase*1.4):0;var rect=new Rect((ActualWidth-width)/2,(ActualHeight-height)/2+sway*1.4,width,height);if(blend<1){drawing.PushOpacity(1-blend);drawing.DrawImage(sleep,rect);drawing.Pop();}if(blend>0){drawing.PushOpacity(blend);drawing.DrawImage(art,rect);drawing.Pop();}if(width<140)return;foreach(double x in new[]{rect.Left+width*.07,rect.Right-width*.055}){double y=rect.Bottom-height*.2;drawing.PushTransform(new RotateTransform(sway*2,x,y));drawing.DrawLine(new Pen(stem,1),new Point(x,y),new Point(x-4,y-25));for(int i=0;i<4;i++)drawing.DrawEllipse(lavender,null,new Point(x-4+(i%2==0?-2:2),y-10-i*5),2.5,4);drawing.Pop();}}
 internal static void VerifyOfflineRendering(){
  foreach(string name in new[]{"tr-TR","en-US","de-DE"}){
   var culture=System.Globalization.CultureInfo.CurrentCulture;
   try{System.Globalization.CultureInfo.CurrentCulture=new(name);foreach(string mood in new[]{"idle","connecting","connected","error"}){var scene=new RomanceScene{Mood=mood};scene.Measure(new Size(360,190));scene.Arrange(new Rect(0,0,360,190));scene.UpdateLayout();var bitmap=new RenderTargetBitmap(360,190,96,96,PixelFormats.Pbgra32);bitmap.Render(scene);scene.PauseForGame(true);if(scene.timer.IsEnabled||scene.blend!=scene.target)throw new InvalidOperationException("Game mode animation did not stop");}if(art.PixelWidth!=640||sleep.PixelWidth!=640)throw new InvalidOperationException("Unexpected artwork decode size");}
   finally{System.Globalization.CultureInfo.CurrentCulture=culture;}
  }
 }
}
