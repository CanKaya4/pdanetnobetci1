package io.tethermax.app;

import android.app.*;
import android.content.*;
import android.os.*;
import android.telephony.*;
import java.util.Locale;

/** Passive service-owned telemetry. No scans, probes, routing or charging writes. */
final class ModemHealth implements AutoCloseable {
    private final TunnelService service;
    private final PowerManager power;
    private final GuardianPolicy policy = new GuardianPolicy();
    volatile boolean reduced, interactive, charging, usbPower;
    volatile int battery = -1, thermal = -1, batteryHealth = BatteryManager.BATTERY_HEALTH_UNKNOWN;
    volatile float batteryC = Float.NaN;
    volatile long connectedAt, reconnects, sampleAt;
    volatile double cpu = Double.NaN;
    volatile int pssKb = -1;
    volatile String signal = "Sinyal bilgisi bekleniyor", radio = "Radyo türü verilmedi";
    volatile long signalAt;
    volatile int signalLevel=-1;
    private volatile boolean closed;
    private boolean connected, receiverRegistered;
    private long previousCpu, previousAt, lastMemory;
    private TelephonyManager telephony;
    private Signals signals;
    private PhoneStateListener legacy;
    private boolean warned;
    private ThermalRegistration thermalRegistration;
    // Keep references to API 29 listener types out of the API 26 constructor.
    private final class ThermalRegistration implements AutoCloseable {
        private final PowerManager.OnThermalStatusChangedListener listener=value->{thermal=value;adapt();};
        ThermalRegistration(){thermal=power.getCurrentThermalStatus();power.addThermalStatusListener(listener);}
        @Override public void close(){power.removeThermalStatusListener(listener);}
    }
    private final BroadcastReceiver receiver = new BroadcastReceiver() {
        @Override public void onReceive(Context context, Intent intent) {
            if(closed)return;
            if("android.intent.action.ACTION_DEFAULT_DATA_SUBSCRIPTION_CHANGED".equals(intent.getAction()))registerSignal();
            interactive = power.isInteractive();
            if (Intent.ACTION_BATTERY_CHANGED.equals(intent.getAction())) {
                int level=intent.getIntExtra(BatteryManager.EXTRA_LEVEL,-1), scale=intent.getIntExtra(BatteryManager.EXTRA_SCALE,-1);
                battery=level>=0&&scale>0?Math.round(level*100f/scale):-1;
                batteryC=intent.hasExtra(BatteryManager.EXTRA_TEMPERATURE)?intent.getIntExtra(BatteryManager.EXTRA_TEMPERATURE,0)/10f:Float.NaN;
                int state=intent.getIntExtra(BatteryManager.EXTRA_STATUS,-1);
                charging=state==BatteryManager.BATTERY_STATUS_CHARGING||state==BatteryManager.BATTERY_STATUS_FULL;
                usbPower=(intent.getIntExtra(BatteryManager.EXTRA_PLUGGED,0)&BatteryManager.BATTERY_PLUGGED_USB)!=0;
                batteryHealth=intent.getIntExtra(BatteryManager.EXTRA_HEALTH,BatteryManager.BATTERY_HEALTH_UNKNOWN);
            }
            adapt(); service.schedulePowerCheck();
        }
    };
    ModemHealth(TunnelService service) { this.service=service; power=service.getSystemService(PowerManager.class); }
    void start() {
        interactive=power.isInteractive();
        IntentFilter filter=new IntentFilter(Intent.ACTION_BATTERY_CHANGED);
        filter.addAction(Intent.ACTION_SCREEN_ON); filter.addAction(Intent.ACTION_SCREEN_OFF);
        filter.addAction("android.intent.action.ACTION_DEFAULT_DATA_SUBSCRIPTION_CHANGED");
        if(Build.VERSION.SDK_INT>=33)service.registerReceiver(receiver,filter,Context.RECEIVER_NOT_EXPORTED);
        else service.registerReceiver(receiver,filter);
        receiverRegistered=true;
        if(Build.VERSION.SDK_INT>=29) {
            try { thermalRegistration=new ThermalRegistration(); }
            catch(RuntimeException unavailable) { thermal=-1; }
        }
        registerSignal(); adapt();
    }
    // Re-register on default data SIM changes without reading subscriber identifiers.
    void registerSignal() {
        unregisterSignal(); signal="Sinyal bilgisi bekleniyor"; signalAt=0;signalLevel=-1; radio="Radyo türü verilmedi";
        try {
            TelephonyManager base=service.getSystemService(TelephonyManager.class);
            int sub=SubscriptionManager.getDefaultDataSubscriptionId();
            telephony=SubscriptionManager.isValidSubscriptionId(sub)?base.createForSubscriptionId(sub):base;
            if(Build.VERSION.SDK_INT>=31) { signals=new Signals(); telephony.registerTelephonyCallback(service.getMainExecutor(),signals); }
            else { legacy=new PhoneStateListener(){@Override public void onSignalStrengthsChanged(SignalStrength s){readSignal(s);}};telephony.listen(legacy,PhoneStateListener.LISTEN_SIGNAL_STRENGTHS); }
        } catch(RuntimeException unavailable) { unregisterSignal();signal="Sinyal bilgisi bu cihazda erişilemiyor"; }
    }
    private void unregisterSignal() {
        if(telephony!=null)try {
            if(Build.VERSION.SDK_INT>=31&&signals!=null)telephony.unregisterTelephonyCallback(signals);
            if(legacy!=null)telephony.listen(legacy,PhoneStateListener.LISTEN_NONE);
        } catch(RuntimeException ignored) { }
        signals=null;legacy=null;telephony=null;
    }
    private final class Signals extends TelephonyCallback implements TelephonyCallback.SignalStrengthsListener, TelephonyCallback.DisplayInfoListener {
        @Override public void onSignalStrengthsChanged(SignalStrength s) { readSignal(s); }
        @Override public void onDisplayInfoChanged(TelephonyDisplayInfo info) {
            if(closed)return;
            int type=info.getNetworkType(), override=info.getOverrideNetworkType();
            radio=type==TelephonyManager.NETWORK_TYPE_NR?"5G NR":type==TelephonyManager.NETWORK_TYPE_LTE?"4G LTE":"Hücresel tür "+type;
            if(override==TelephonyDisplayInfo.OVERRIDE_NETWORK_TYPE_NR_NSA||override==TelephonyDisplayInfo.OVERRIDE_NETWORK_TYPE_NR_ADVANCED)radio+=" · 5G gösterge (NSA/ileri)";
        }
    }
    private void readSignal(SignalStrength strength) {
        if(closed)return;
        StringBuilder result=new StringBuilder("Seviye ").append(strength.getLevel()).append("/4");
        if(Build.VERSION.SDK_INT>=29)for(CellSignalStrength cell:strength.getCellSignalStrengths()) {
            int dbm=cell.getDbm();
            if(dbm!=CellInfo.UNAVAILABLE)result.append(" · ").append(cell instanceof CellSignalStrengthNr?"NR":cell instanceof CellSignalStrengthLte?"LTE":"Hücresel").append(' ').append(dbm).append(" dBm");
        }
        signal=result.toString();signalAt=SystemClock.elapsedRealtime();
        signalLevel=strength.getLevel();
    }
    private void adapt() {
        if(closed)return;
        reduced=policy.update(SystemClock.elapsedRealtime(),thermal,batteryC);
        boolean critical=thermal>=PowerManager.THERMAL_STATUS_CRITICAL;
        if(critical&&!warned) try {
            NotificationManager manager=service.getSystemService(NotificationManager.class);
            manager.createNotificationChannel(new NotificationChannel("thermal","Kritik sıcaklık",NotificationManager.IMPORTANCE_HIGH));
            PendingIntent open=PendingIntent.getActivity(service,2,new Intent(service,MainActivity.class),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
            manager.notify(2,new Notification.Builder(service,"thermal").setSmallIcon(service.getResources().getIdentifier("tethermax_notification","drawable",service.getPackageName())).setContentTitle("Telefon kritik sıcaklık bildiriyor").setContentText("Havalandırmayı artırın; gerekirse aktarımı elle durdurun.").setContentIntent(open).setAutoCancel(true).build());
        } catch(RuntimeException unavailable) { /* Dashboard still reports the critical state. */ }
        if(!critical&&warned)try{service.getSystemService(NotificationManager.class).cancel(2);}catch(RuntimeException ignored){}
        warned=critical;
    }
    void heartbeat(boolean fresh) {
        if(fresh&&!connected) { if(connectedAt!=0)reconnects++; connectedAt=SystemClock.elapsedRealtime(); }
        connected=fresh; adapt();
    }
    boolean connected(){return connected;}
    long connectionMillis(){return connected?SystemClock.elapsedRealtime()-connectedAt:0;}
    // Called only by a visible dashboard, at most once per 15/30 seconds.
    void sampleProcess() {
        long now=SystemClock.elapsedRealtime(),interval=reduced?30000:15000;
        if(now-sampleAt<interval)return;
        long cpuNow=android.os.Process.getElapsedCpuTime();
        cpu=previousAt==0?Double.NaN:GuardianPolicy.cpuPercent(cpuNow-previousCpu,now-previousAt);
        previousCpu=cpuNow;previousAt=now;sampleAt=now;
        if(now-lastMemory>=interval){Debug.MemoryInfo memory=new Debug.MemoryInfo();Debug.getMemoryInfo(memory);pssKb=memory.getTotalPss();lastMemory=now;}
    }
    String thermalLabel() { String[] labels={"Normal","Hafif","Orta","Yüksek","Kritik","Acil","Kapanma eşiği"};return thermal>=0&&thermal<labels.length?labels[thermal]:"Desteklenmiyor"; }
    String recommendation() {
        StringBuilder s=new StringBuilder();
        if(reduced)s.append("Isı koruması: animasyon ve isteğe bağlı analiz azaltıldı.\n");
        if(thermal>=4)s.append("Kritik ısı: telefonu havalandırın; Android aktarımı sınırlayabilir.\n");
        if(usbPower&&battery>=80)s.append("Uzun USB kullanımı: Samsung Pil koruması → Maksimum sınırını değerlendirin.\n");
        if(!power.isIgnoringBatteryOptimizations(service.getPackageName()))s.append("Kesinti yaşanırsa uygulama pil ayarlarını ve Samsung uyku listesini inceleyin.\n");
        if(power.isPowerSaveMode())s.append("Sistem güç tasarrufu açık; arka plan ağını kısıtlayabilir.\n");
        if(signalLevel>=0&&signalLevel<=1&&SystemClock.elapsedRealtime()-signalAt<60000)s.append("Sinyal zayıf: telefonu havalandırılan, daha iyi çeken bir konuma koyun. Mevcut trafik profili korunuyor.\n");
        s.append("Sinyal dBm değeri kapasite değildir. Hız ve DNS RTT/jitter için Analiz ekranındaki gerçek ölçümleri kullanın.");
        return s.toString();
    }
    String summary() {
        String cpuText=Double.isNaN(cpu)?"Örnek bekleniyor":String.format(Locale.ROOT,"%.1f%% (1 çekirdek = %%100)",cpu);
        String health=batteryHealth==BatteryManager.BATTERY_HEALTH_GOOD?"Android: iyi":batteryHealth==BatteryManager.BATTERY_HEALTH_OVERHEAT?"Android: aşırı sıcak":batteryHealth==BatteryManager.BATTERY_HEALTH_DEAD?"Android: arızalı":batteryHealth==BatteryManager.BATTERY_HEALTH_UNKNOWN?"Verilmedi":"Android durum kodu "+batteryHealth;
        return "TetherMax CPU · "+cpuText+"\nUygulama RAM (PSS) · "+(pssKb<0?"Verilmedi":String.format(Locale.ROOT,"%.1f MiB",pssKb/1024.0))+"\nPil · "+(battery<0?"Verilmedi":"%"+battery)+" · "+(Float.isNaN(batteryC)?"Sıcaklık verilmedi":String.format(Locale.ROOT,"%.1f °C",batteryC))+"\nŞarj · "+(charging?"Şarj oluyor / dolu":"Şarj olmuyor")+(usbPower?" · USB güç kaynağı":"")+"\nPil sağlığı · "+health+" (kapasite yüzdesi verilmez)\nTermal · "+thermalLabel()+"\n"+radio+"\n"+signal+(signalAt==0?"":" · son olay "+((SystemClock.elapsedRealtime()-signalAt)/1000)+" sn önce")+"\nUSB kanal sağlığı · "+(connected?"Kimliği doğrulanmış Windows heartbeat güncel":"Heartbeat bekleniyor / USB yolu doğrulanmadı")+"\nAkış hataları · "+service.flowFailures()+" (tek akış; servis çökmesi değildir)\n"+UsbPairing.listenerHealth+"\n"+service.usbRecovery+"\nOturum · "+connectionMillis()/1000+" sn · Yeniden bağlantı · "+reconnects+"\n"+(interactive?"Ekran açık":"Akıllı boşta · ekran kapalı");
    }
    @Override public void close() {
        closed=true;unregisterSignal();
        if(receiverRegistered){try{service.unregisterReceiver(receiver);}catch(RuntimeException ignored){}receiverRegistered=false;}
        if(Build.VERSION.SDK_INT>=29&&thermalRegistration!=null)try{thermalRegistration.close();}catch(RuntimeException ignored){}
        thermalRegistration=null;
        try{service.getSystemService(NotificationManager.class).cancel(2);}catch(RuntimeException ignored){}
    }
}
