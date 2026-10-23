package io.tethermax.app;

import android.content.Context;
import android.graphics.*;
import android.view.View;
import java.util.ArrayList;

/** Actual sampled values only; NaN breaks the line when measurements are absent. */
final class TrafficPlot extends View {
    private final ArrayList<float[]> points=new ArrayList<>();
    private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);
    private final String units;
    private final int ink;
    TrafficPlot(Context context,String units,int ink){super(context);this.units=units;this.ink=ink;setMinimumHeight(Math.round(130*getResources().getDisplayMetrics().density));}
    void add(float first,float second){points.add(new float[]{first,second});if(points.size()>60)points.remove(0);invalidate();}
    @Override protected void onDraw(Canvas canvas){
        super.onDraw(canvas);float density=getResources().getDisplayMetrics().density;float top=26*density,bottom=getHeight()-12*density,max=0;int valid=0;
        for(float[] point:points)for(float v:point)if(!Float.isNaN(v)&&!Float.isInfinite(v)&&v>=0){max=Math.max(max,v);valid++;}
        paint.setColor(ink);paint.setTextSize(12*density);canvas.drawText(valid==0?"Ölçüm bekleniyor":String.format(java.util.Locale.ROOT,"%.2f %s · son 60 örnek",max,units),0,16*density,paint);
        if(valid==0)return;max=Math.max(max,0.01f);
        for(int series=0;series<2;series++){
            paint.setColor(series==0?Color.rgb(40,170,220):Color.rgb(50,190,140));paint.setStrokeWidth(2*density);
            float px=0,py=0;boolean previous=false;
            for(int i=0;i<points.size();i++){float v=points.get(i)[series];float x=points.size()<2?0:(float)i/(points.size()-1)*getWidth();if(Float.isNaN(v)||v<0){previous=false;continue;}float y=bottom-v/max*(bottom-top);if(previous)canvas.drawLine(px,py,x,y,paint);px=x;py=y;previous=true;}
        }
    }
}
