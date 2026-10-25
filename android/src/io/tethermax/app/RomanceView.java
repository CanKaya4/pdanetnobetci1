package io.tethermax.app;
import android.content.Context;
import android.graphics.*;
import android.view.View;
/** Original offline vector artwork, paced and suspended with the Activity. */
final class RomanceView extends View {
 private final Paint p=new Paint(3);private float phase;private boolean resumed;
 private final Runnable frame=new Runnable(){public void run(){if(!resumed||!isShown())return;phase+=0.12f;invalidate();if(motion())postDelayed(this,full()?120:250);}};
 RomanceView(Context c){super(c);setContentDescription("Pembe ve lavanta aksesuarları olan iki kedi, kalpler ve gül");}
 private boolean full(){return getContext().getSharedPreferences("appearance",0).getInt("motion",1)==2;}
 private boolean motion(){return getContext().getSharedPreferences("appearance",0).getInt("motion",1)>0&&android.provider.Settings.Global.getFloat(getContext().getContentResolver(),"animator_duration_scale",1)>0;}
 void resume(){resumed=true;removeCallbacks(frame);post(frame);}void pause(){resumed=false;removeCallbacks(frame);}
 @Override protected void onDetachedFromWindow(){pause();super.onDetachedFromWindow();}
 private void oval(Canvas c,int color,float x,float y,float rx,float ry){p.setStyle(Paint.Style.FILL);p.setColor(color);c.drawOval(x-rx,y-ry,x+rx,y+ry,p);}
 @Override protected void onDraw(Canvas c){super.onDraw(c);float scale=Math.min(getWidth()/360f,getHeight()/165f);c.save();c.translate((getWidth()-360*scale)/2,0);c.scale(scale,scale);
 int pink=Color.rgb(239,163,189),lavender=Color.rgb(183,166,246),white=Color.rgb(255,248,244),ink=Color.rgb(75,53,78);
 oval(c,lavender,180,149,128,4);
 for(int n=0;n<2;n++){float x=n==0?109:247;int ribbon=n==0?pink:lavender;
 p.setColor(ribbon);p.setStyle(Paint.Style.STROKE);p.setStrokeWidth(8);Path tail=new Path();tail.moveTo(x+30,134);tail.cubicTo(x+76,144,x+63,103+(full()?(float)Math.sin(phase)*3:0),x+46,114);c.drawPath(tail,p);
 oval(c,white,x,120,35,29);p.setColor(white);Path ears=new Path();ears.moveTo(x-34,67);ears.lineTo(x-28,27);ears.lineTo(x-8,49);ears.close();ears.moveTo(x+8,49);ears.lineTo(x+28,27);ears.lineTo(x+34,67);ears.close();c.drawPath(ears,p);oval(c,white,x,75,39,32);
 for(int side=-1;side<=1;side+=2){if(TunnelService.instance==null||phase%5>4.65){p.setColor(ink);p.setStrokeWidth(2);c.drawLine(x+side*14-4,73,x+side*14+4,73,p);}else oval(c,ink,x+side*14,73,2.5f,4);oval(c,pink,x+side*24,84,6,3);}
 oval(c,ribbon,x,83,3,2);p.setColor(ribbon);c.drawRoundRect(x-25,100,x+25,110,5,5,p);oval(c,white,x-16,142,12,6);oval(c,white,x+16,142,12,6);
 }p.setColor(Color.rgb(95,155,132));p.setStrokeWidth(3);c.drawLine(180,145,180,104,p);oval(c,Color.rgb(166,232,213),187,127,10,4);
 for(int i=0;i<5;i++){double a=i*Math.PI*2/5;oval(c,i%2==0?pink:Color.rgb(248,202,219),180+(float)Math.cos(a)*7,103+(float)Math.sin(a)*7,8,7);}oval(c,ink,180,103,3,3);
 for(int i=0;i<3;i++){float x=163+i*18,y=35-(full()?(float)Math.sin(phase+i)*6:0);Path h=new Path();h.moveTo(x,y+3);h.cubicTo(x-8,y-5,x-12,y+7,x,y+14);h.cubicTo(x+12,y+7,x+8,y-5,x,y+3);p.setColor(i==1?lavender:pink);c.drawPath(h,p);}c.restore();}
}
