using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
namespace TetherMax;
// Original resolution-independent artwork; no assets or remote requests.
internal sealed class RomanceScene : FrameworkElement {
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
 double phase; public string Mood {get;set;}="idle";
 public RomanceScene(){Height=165;MinWidth=260;System.Windows.Automation.AutomationProperties.SetName(this,"Pembe ve lavanta aksesuarları olan iki kedi ve gül");
 timer.Tick+=(_,_)=>{if(IsVisible&&UiTheme.Animate){timer.Interval=TimeSpan.FromMilliseconds(UiTheme.Motion==2?100:250);phase+=UiTheme.Motion==2?0.1:0.25;InvalidateVisual();}};
 Loaded+=(_,_)=>timer.Start();Unloaded+=(_,_)=>timer.Stop();IsVisibleChanged+=(_,_)=>{if(IsVisible)timer.Start();else timer.Stop();};}
 static SolidColorBrush B(string color)=>new((Color)ColorConverter.ConvertFromString(color));
 protected override void OnRender(DrawingContext d){base.OnRender(d);double scale=Math.Min(ActualWidth/360,ActualHeight/165);d.PushTransform(new TranslateTransform((ActualWidth-360*scale)/2,0));d.PushTransform(new ScaleTransform(scale,scale));
 var outline=new Pen(B("#89768E"),1.8);double wave=UiTheme.Animate&&UiTheme.Motion==2?Math.Sin(phase*2)*3:0;
 d.DrawEllipse(B("#B7A6F6"),null,new Point(180,148),129,5);
 for(int n=0;n<2;n++){double x=n==0?109:247;if(Mood=="connected"&&UiTheme.Animate&&UiTheme.Motion==2)x+=(n==0?1:-1)*(4+Math.Sin(phase)*3);var ribbon=B(n==0?"#EFA3BD":"#B7A6F6");
 var tail=new PathGeometry();var f=new PathFigure{StartPoint=new Point(x+30,135)};f.Segments.Add(new BezierSegment(new Point(x+75,140),new Point(x+64,100+wave),new Point(x+46,112+wave),true));tail.Figures.Add(f);d.DrawGeometry(null,new Pen(ribbon,9),tail);
 d.DrawEllipse(B("#FFF8F4"),outline,new Point(x,119),35,29);
 var ears=Geometry.Parse($"M {x-34},67 L {x-28},27 L {x-8},48 M {x+8},48 L {x+28},27 L {x+34},67");d.DrawGeometry(B("#FFF8F4"),outline,ears);
 d.DrawEllipse(B("#FFFDFC"),outline,new Point(x,74),39,32);
 bool blink=UiTheme.Animate&&phase%5>4.7;for(int side=-1;side<=1;side+=2){if(blink||Mood=="idle")d.DrawLine(outline,new Point(x+side*14-4,73),new Point(x+side*14+4,73));else d.DrawEllipse(B("#35273F"),null,new Point(x+side*14,72),2.8,4);d.DrawEllipse(B("#F8CADB"),null,new Point(x+side*24,83),6,3);}
 d.DrawEllipse(ribbon,null,new Point(x,82),3,2);d.DrawLine(outline,new Point(x,85),new Point(x+4,88));d.DrawRoundedRectangle(ribbon,null,new Rect(x-25,100,50,10),5,5);
 d.DrawEllipse(B("#FFF8F4"),outline,new Point(x-16,141),12,6);d.DrawEllipse(B("#FFF8F4"),outline,new Point(x+16,141),12,6);
 }
 d.DrawLine(new Pen(B("#5F9B84"),3),new Point(180,144),new Point(180,105));d.DrawEllipse(B("#A6E8D5"),null,new Point(187,126),10,4);
 for(int i=0;i<5;i++){double a=i*Math.PI*2/5;d.DrawEllipse(B(i%2==0?"#EFA3BD":"#F8CADB"),null,new Point(180+Math.Cos(a)*7,103+Math.Sin(a)*7),8,7);}d.DrawEllipse(B("#99506D"),null,new Point(180,103),4,4);
 for(int i=0;i<3;i++){double x=163+i*18,y=37-(UiTheme.Motion==2?Math.Sin(phase+i)*6:0);var h=Geometry.Parse("M 0,3 C -8,-5 -12,7 0,14 C 12,7 8,-5 0,3 Z");d.PushTransform(new TranslateTransform(x,y));d.DrawGeometry(B(i==1?"#B7A6F6":"#EFA3BD"),null,h);d.Pop();}
 if(Mood=="hotspot"){for(int i=0;i<3;i++){double size=16+i*10;d.DrawEllipse(null,new Pen(B("#B7A6F6"),1.5),new Point(180,67),size,size/2);}}
 if(Mood=="error"){d.DrawLine(outline,new Point(90,63),new Point(100,67));d.DrawLine(outline,new Point(118,67),new Point(128,63));}
 if(Mood=="connecting")d.DrawLine(new Pen(B("#B7A6F6"),3),new Point(135,113),new Point(222,113));
 d.Pop();d.Pop();}
}
