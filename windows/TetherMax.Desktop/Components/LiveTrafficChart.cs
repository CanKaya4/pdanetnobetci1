using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace TetherMax;
internal sealed class LiveTrafficChart : FrameworkElement {
 readonly List<(double? Down,double? Up)> samples=[];int hover=-1;
 public LiveTrafficChart(){Height=140;Margin=new Thickness(0,12,0,12);MouseMove+=(_,e)=>{hover=Math.Clamp((int)(e.GetPosition(this).X/Math.Max(ActualWidth,1)*samples.Count),0,Math.Max(0,samples.Count-1));InvalidateVisual();};MouseLeave+=(_,_)=>{hover=-1;InvalidateVisual();};SizeChanged+=(_,_)=>InvalidateVisual();}
 public void Clear(){samples.Clear();InvalidateVisual();}
 public void Accept(JsonElement data){double? N(string key)=>data.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&double.IsFinite(v.GetDouble())?v.GetDouble():null;samples.Add((N("DownloadMbps"),N("UploadMbps")));if(samples.Count>120)samples.RemoveAt(0);if(IsVisible)InvalidateVisual();}
 void Label(DrawingContext d,string text,Point point)=>d.DrawText(new FormattedText(text,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,UiTheme.Muted,VisualTreeHelper.GetDpi(this).PixelsPerDip),point);
 protected override void OnRender(DrawingContext d){base.OnRender(d);double width=ActualWidth,height=ActualHeight-24,max=Math.Max(0.01,samples.SelectMany(s=>new[]{s.Down??0,s.Up??0}).DefaultIfEmpty().Max());
 for(int i=0;i<4;i++)d.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(35,137,118,142)),1),new Point(0,24+i*height/3),new Point(width,24+i*height/3));
 if(!samples.Any(s=>s.Down.HasValue||s.Up.HasValue)){Label(d,"Canlı internet · Ölçüm bekleniyor ♡",new Point(0,0));return;}
 Label(d,$"{max:0.##} Mbps  ·  ↓ İndirme / ↑ Yükleme",new Point(0,0));
 for(int series=0;series<2;series++){var color=(Color)ColorConverter.ConvertFromString(series==0?"#C86991":"#987EDE");var points=new List<Point>();
 void Flush(){if(points.Count<2){points.Clear();return;}var shape=new StreamGeometry();using(var c=shape.Open()){c.BeginFigure(points[0],false,false);c.PolyLineTo(points.Skip(1).ToArray(),true,false);}d.DrawGeometry(null,new Pen(new SolidColorBrush(color),2.5),shape);var fill=new StreamGeometry();using(var c=fill.Open()){c.BeginFigure(new Point(points[0].X,ActualHeight),true,true);c.PolyLineTo(points.ToArray(),true,false);c.LineTo(new Point(points[^1].X,ActualHeight),true,false);}var gradient=new LinearGradientBrush(Color.FromArgb(48,color.R,color.G,color.B),Colors.Transparent,90);d.DrawGeometry(gradient,null,fill);points.Clear();}
 for(int i=0;i<samples.Count;i++){double? value=series==0?samples[i].Down:samples[i].Up;if(!value.HasValue){Flush();continue;}points.Add(new Point(samples.Count<2?0:i*width/(samples.Count-1),ActualHeight-value.Value/max*(height-8)));}Flush();}
 if(hover>=0&&hover<samples.Count){var point=samples[hover];ToolTip=$"Örnek {hover+1} · ↓ {point.Down:0.###} / ↑ {point.Up:0.###} Mbps";double x=samples.Count<2?0:hover*width/(samples.Count-1);d.DrawLine(new Pen(UiTheme.Muted,1),new Point(x,24),new Point(x,ActualHeight));}
 }
}
