using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace TetherMax;
internal static class ButtonMotion {
 public static void Register(){EventManager.RegisterClassHandler(typeof(Button),UIElement.PreviewMouseDownEvent,new MouseButtonEventHandler((sender,_)=>Animate((Button)sender,0.975)));EventManager.RegisterClassHandler(typeof(Button),UIElement.PreviewMouseUpEvent,new MouseButtonEventHandler((sender,_)=>Animate((Button)sender,1)));EventManager.RegisterClassHandler(typeof(Button),UIElement.MouseLeaveEvent,new MouseEventHandler((sender,_)=>Animate((Button)sender,1)));}
 static void Animate(Button button,double target){if(!UiTheme.Animate)return;if(button.RenderTransform is not ScaleTransform){button.RenderTransformOrigin=new Point(0.5,0.5);button.RenderTransform=new ScaleTransform(1,1);}var scale=(ScaleTransform)button.RenderTransform;var animation=new DoubleAnimation(target,TimeSpan.FromMilliseconds(150)){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut}};scale.BeginAnimation(ScaleTransform.ScaleXProperty,animation);scale.BeginAnimation(ScaleTransform.ScaleYProperty,animation);}
}
