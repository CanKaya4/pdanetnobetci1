package io.tethermax.app;
import android.content.Context;
import android.graphics.*;
import android.view.View;
import android.view.MotionEvent;
import java.util.ArrayList;
/** Actual samples only. Missing measurements and lifecycle gaps break the line. */
final class TrafficPlot extends View {
 private final ArrayList<float[]> points=new ArrayList<>();private final ArrayList<Long> times=new ArrayList<>();
 private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);private final String units;private final int ink;
 private int minutes=1,touched=-1,visibleFirst;private long from,to;
 TrafficPlot(Context c,String units,int ink){super(c);this.units=units;this.ink=ink;setMinimumHeight(Math.round(130*getResources().getDisplayMetrics().density));setContentDescription("Gerçek zamanlı "+units+" grafiği; değeri görmek için dokunun");}
 void setMinutes(int value){minutes=value;touched=-1;invalidate();}
 void add(float a,float b){points.add(new float[]{a,b});times.add(android.os.SystemClock.elapsedRealtime());if(points.size()>1800){points.remove(0);times.remove(0);if(touched>=0)touched--;}if(isShown())invalidate();}
 @Override public boolean onTouchEvent(MotionEvent event){if(points.isEmpty())return false;long target=from+(long)(Math.max(0,Math.min(1,event.getX()/Math.max(1,getWidth())))*(to-from));int nearest=visibleFirst;for(int i=visibleFirst;i<times.size();i++)if(Math.abs(times.get(i)-target)<Math.abs(times.get(nearest)-target))nearest=i;touched=nearest;if(event.getAction()==MotionEvent.ACTION_UP)performClick();invalidate();return true;}
 @Override public boolean performClick(){super.performClick();return true;}
 private void segment(Canvas c,Path line,float start,float end,float bottom,int color,float density){Path area=new Path(line);area.lineTo(end,bottom);area.lineTo(start,bottom);area.close();paint.setStyle(Paint.Style.FILL);paint.setShader(new LinearGradient(0,28*density,0,bottom,new int[]{(color&0xFFFFFF)|0x33000000,color&0xFFFFFF},null,Shader.TileMode.CLAMP));c.drawPath(area,paint);paint.setShader(null);paint.setColor(color);paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(2*density);c.drawPath(line,paint);paint.setStyle(Paint.Style.FILL);}
 @Override protected void onDraw(Canvas c){super.onDraw(c);float density=getResources().getDisplayMetrics().density,top=28*density,bottom=getHeight()-20*density,max=0;int valid=0;to=android.os.SystemClock.elapsedRealtime();long cutoff=to-minutes*60000L;visibleFirst=0;while(visibleFirst<times.size()&&times.get(visibleFirst)<cutoff)visibleFirst++;from=visibleFirst<times.size()?Math.max(cutoff,times.get(visibleFirst)):cutoff;
 for(int i=visibleFirst;i<points.size();i++)for(float v:points.get(i))if(!Float.isNaN(v)&&!Float.isInfinite(v)&&v>=0){max=Math.max(max,v);valid++;}
 paint.setColor(ink);paint.setAlpha(30);paint.setStrokeWidth(density);for(int i=0;i<4;i++){float y=top+(bottom-top)*i/3;c.drawLine(0,y,getWidth(),y,paint);}paint.setAlpha(255);paint.setTextSize(11*density);
 c.drawText(valid==0?"Ölçüm bekleniyor ♡":String.format(java.util.Locale.getDefault(),"%.2f %s · gerçek örnekler",max,units),0,15*density,paint);if(valid==0)return;max=Math.max(max,0.01f);
 for(int series=0;series<2;series++){int color=series==0?Color.rgb(200,105,145):Color.rgb(152,126,222);Path path=new Path();boolean has=false;float start=0,end=0;
 for(int i=visibleFirst;i<points.size();i++){float v=points.get(i)[series];boolean gap=i>visibleFirst&&times.get(i)-times.get(i-1)>3000;if((gap||Float.isNaN(v)||Float.isInfinite(v)||v<0)&&has){segment(c,path,start,end,bottom,color,density);path.reset();has=false;}if(Float.isNaN(v)||Float.isInfinite(v)||v<0)continue;float x=(float)(times.get(i)-from)/Math.max(1,to-from)*getWidth(),y=bottom-v/max*(bottom-top);if(!has){path.moveTo(x,y);start=x;has=true;}else path.lineTo(x,y);end=x;}if(has)segment(c,path,start,end,bottom,color,density);}
 paint.setColor(ink);paint.setTextSize(10*density);c.drawText("↓ İndirme  /  ↑ Yükleme",0,getHeight()-3*density,paint);
 if(touched>=visibleFirst&&touched<points.size()){float[] v=points.get(touched);String a=Float.isNaN(v[0])?"—":String.format(java.util.Locale.getDefault(),"%.2f",v[0]),b=Float.isNaN(v[1])?"—":String.format(java.util.Locale.getDefault(),"%.2f",v[1]);paint.setColor(ink);c.drawText("↓ "+a+"  ↑ "+b+" "+units,0,26*density,paint);}
 }
}
