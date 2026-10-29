package io.tethermax.app;

import android.content.Context;
import android.content.SharedPreferences;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.TimeZone;

/** Optional accounting: storage/notification failures must not stop the relay. */
final class DataShield {
    private final SharedPreferences prefs;
    private UsageMeter meter;
    private long savedAt;
    DataShield(Context context) {
        prefs=context.getSharedPreferences("data-shield-v1",Context.MODE_PRIVATE);
        try{meter=new UsageMeter(prefs.getString("month",""),prefs.getLong("used",0),prefs.getLong("budget",0));}
        catch(RuntimeException invalid){meter=null;}
    }
    static String month(){SimpleDateFormat f=new SimpleDateFormat("yyyy-MM",Locale.ROOT);f.setTimeZone(TimeZone.getTimeZone("UTC"));return f.format(new Date());}
    synchronized int sample(long up,long down) {
        if(meter==null)return 0;
        int warning=meter.sample(month(),up,down);
        long now=android.os.SystemClock.elapsedRealtime();
        if(warning>0||now-savedAt>=30000){save();savedAt=now;}
        return warning;
    }
    synchronized long budget(){return meter==null?0:meter.budget;}
    synchronized boolean setBudget(long bytes){
        if(meter==null||bytes<0||bytes>1000000000000000L)return false;
        meter.budget=bytes;save();return true;
    }
    synchronized void save(){if(meter!=null)prefs.edit().putString("month",meter.month).putLong("used",meter.used).putLong("budget",meter.budget).apply();}
    synchronized String summary(){
        if(meter==null)return "Sayaç geçmişi okunamadı; veriyi korumak için sayaç duraklatıldı.";
        return (meter.month.isEmpty()?"Henüz örnek yok":meter.month+" UTC")+String.format(Locale.getDefault()," · %.3f GB gözlendi",meter.used/1e9)+
            (meter.budget>0?String.format(Locale.getDefault()," / %.3f GB",meter.budget/1e9):" · Bütçe uyarısı kapalı")+
            (meter.level()>0?"\nBütçe uyarısı · %"+meter.level()+" eşiği aşıldı.":"");
    }
}
