package io.tethermax.app;
import android.content.Context;
import android.graphics.*;
import android.view.View;
import android.os.SystemClock;

/** Cached Imagegen poses with native, lifecycle-bound motion. No network IO. */
final class RomanceView extends View {
 private static Bitmap artwork,sleeping;
 private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG|Paint.FILTER_BITMAP_FLAG);
 private final RectF bounds=new RectF();private boolean resumed,connected;private float blend,from,target,phase;private long transitionAt;
 private boolean moving(){return !TunnelService.reducedWork()&&getContext().getSharedPreferences("appearance",0).getInt("motion",1)>0&&android.provider.Settings.Global.getFloat(getContext().getContentResolver(),"animator_duration_scale",1)>0;}
 private final Runnable frame=new Runnable(){public void run(){if(!resumed||!isShown()||!moving()){blend=target;invalidate();return;}long now=SystemClock.elapsedRealtime();float t=Math.max(0,Math.min(1,(now-transitionAt)/850f));blend=from+(target-from)*(t*t*(3-2*t));phase=now/1000f;invalidate();int motion=getContext().getSharedPreferences("appearance",0).getInt("motion",1);postDelayed(this,motion==2?33:100);}};
 RomanceView(Context context){super(context);setContentDescription("Kahverengi gözlü gri kedi ve yeşil gözlü sarı kedi");if(artwork==null){artwork=load("romance_cats_rc12");sleeping=load("romance_cats_sleep_rc12");}}
 private Bitmap load(String name){BitmapFactory.Options options=new BitmapFactory.Options();options.inSampleSize=2;options.inScaled=false;return BitmapFactory.decodeResource(getResources(),getResources().getIdentifier(name,"drawable",getContext().getPackageName()),options);}
 void setConnected(boolean value){if(connected==value)return;connected=value;from=blend;target=value?1:0;transitionAt=SystemClock.elapsedRealtime();if(!resumed||!moving())blend=target;invalidate();}
 void resume(){if(resumed)return;resumed=true;removeCallbacks(frame);post(frame);}
 void pause(){resumed=false;removeCallbacks(frame);blend=target;invalidate();}
 @Override protected void onDetachedFromWindow(){pause();super.onDetachedFromWindow();}
 @Override protected void onDraw(Canvas canvas){super.onDraw(canvas);if(artwork==null||sleeping==null)return;float scale=Math.min(getWidth()/(float)artwork.getWidth(),getHeight()/(float)artwork.getHeight());float width=artwork.getWidth()*scale,height=artwork.getHeight()*scale;float wave=resumed&&moving()?(float)Math.sin(phase*1.4):0;bounds.set((getWidth()-width)/2,(getHeight()-height)/2+wave*getResources().getDisplayMetrics().density,(getWidth()+width)/2,(getHeight()+height)/2+wave*getResources().getDisplayMetrics().density);if(blend<1){paint.setAlpha(Math.round(255*(1-blend)));canvas.drawBitmap(sleeping,null,bounds,paint);}if(blend>0){paint.setAlpha(Math.round(255*blend));canvas.drawBitmap(artwork,null,bounds,paint);}paint.setAlpha(255);
  float density=getResources().getDisplayMetrics().density;for(float x:new float[]{bounds.left+width*.06f,bounds.right-width*.05f}){float y=bounds.bottom-height*.2f;canvas.save();canvas.rotate(wave*2,x,y);paint.setColor(Color.rgb(139,148,119));paint.setStrokeWidth(density*.6f);canvas.drawLine(x,y,x-2*density,y-14*density,paint);paint.setColor(Color.rgb(168,143,188));for(int i=0;i<4;i++){float px=x-2*density+(i%2==0?-1:1)*density,py=y-(5+i*3)*density;canvas.drawOval(px-density,py-2*density,px+density,py+2*density,paint);}canvas.restore();}paint.setColor(Color.WHITE);
 }
}
