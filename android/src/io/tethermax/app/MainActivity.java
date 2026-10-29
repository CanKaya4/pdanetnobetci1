package io.tethermax.app;

import android.app.*;
import android.content.*;
import android.content.res.Configuration;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.*;
import android.view.*;
import android.widget.*;
import org.json.*;
import io.tethermax.engine.Relay;

public final class MainActivity extends Activity {
    private final Handler handler = new Handler(Looper.getMainLooper());
    private TextView status;
    private Thread positionWorker;
    private final java.util.concurrent.atomic.AtomicBoolean positionCanceled=new java.util.concurrent.atomic.AtomicBoolean();
    private void addPositionTest(LinearLayout analysis){
        analysis.addView(text("Telefon konumu testi · RC7",22,ink));
        analysis.addView(text("Telefonu sabit koyun. Hücresel ağ üzerinden Cloudflare ve Google'a 20 kısa TCP bağlantısı ölçülür; mobil veri kullanır. Farklı yerleri aynı saatlerde karşılaştırın. Test sırasında indirmeleri duraklatın.",14,muted));
        TextView result=text(getSharedPreferences("position",MODE_PRIVATE).getString("last","Henüz konum testi yapılmadı."),15,muted);analysis.addView(result);
        Button run=action(analysis,"Bu konumu test et",()->{});
        Button cancel=action(analysis,"Testi iptal et",()->{positionCanceled.set(true);if(positionWorker!=null)positionWorker.interrupt();});cancel.setEnabled(false);
        run.setOnClickListener(v->{
            TunnelService service=TunnelService.instance;android.net.Network selected=service==null?null:service.measurementNetwork();
            if(selected==null){result.setText("Önce hücresel motoru başlatın ve mobil veriyi açın.");return;}
            if(TunnelService.reducedWork()){result.setText("Isı koruması veya oyun desteği etkin; konum testini oyun bittikten ve telefon soğuduktan sonra çalıştırın.");return;}
            run.setEnabled(false);cancel.setEnabled(true);positionCanceled.set(false);getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
            positionWorker=new Thread(()->{String report;
                try{report=PositionTest.run(selected,positionCanceled,()->TunnelService.instance==service&&selected.equals(service.measurementNetwork()),done->handler.post(()->result.setText("Konum ölçülüyor · "+done+"/20")));
                    if(!positionCanceled.get()){String stamp=java.text.DateFormat.getDateTimeInstance().format(new java.util.Date());report=stamp+"\n"+report;getSharedPreferences("position",MODE_PRIVATE).edit().putString("last",report).apply();}}
                catch(InterruptedException error){report="Test iptal edildi; sonuç kaydedilmedi.";}
                catch(Exception error){report="Test tamamlanamadı: "+error.getMessage();}
                final String display=report;handler.post(()->{if(!isDestroyed()){result.setText(display);run.setEnabled(true);cancel.setEnabled(false);getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);}});
            },"TetherMax-position");positionWorker.start();
        });
    }
    @Override protected void onDestroy(){positionCanceled.set(true);if(positionWorker!=null)positionWorker.interrupt();super.onDestroy();}
    private RomanceView mascot;
    private TrafficPlot homePlot;
    private TextView downValue,upValue;
    private Button startButton,stopButton;
    private LinearLayout navBar;
    private ScrollView pageScroll;
    private int selectedPage;
    private FrameLayout stage;
    private View welcome;
    private final Runnable dismissWelcome=()->{if(welcome!=null){stage.removeView(welcome);welcome=null;}};
    private boolean animate(){return !TunnelService.reducedWork()&&getSharedPreferences("appearance",MODE_PRIVATE).getInt("motion",1)>0&&android.provider.Settings.Global.getFloat(getContentResolver(),"animator_duration_scale",1)>0;}
    private TextView healthValue,healthAdvice;
    private boolean visible;
    private void refreshHealth(TunnelService service){
        if(selectedPage!=6||healthValue==null)return;
        ModemHealth health=service==null?null:service.health();
        if(health==null){healthValue.setText("Sağlık izlemesi için hücresel motoru başlatın.");healthAdvice.setText("Telefonun vermediği değerler gösterilmez.");return;}
        health.sampleProcess();healthValue.setText(health.summary()+"\n↓ "+bytes(service.downloaded())+" · ↑ "+bytes(service.uploaded()));healthAdvice.setText(health.recommendation());
    }
    private void openSettings(String action,boolean app){
        Intent intent=new Intent(action);if(app)intent.setData(android.net.Uri.parse("package:"+getPackageName()));
        try{startActivity(intent);}catch(ActivityNotFoundException unavailable){startActivity(new Intent(android.provider.Settings.ACTION_SETTINGS));}
    }
    private static String bytes(long value){double v=value;String[] units={"B","KB","MB","GB","TB"};int i=0;while(v>=1000&&i<4){v/=1000;i++;}return String.format(java.util.Locale.getDefault(),"%.2f %s",v,units[i]);}
    private LinearLayout content;
    private final java.util.ArrayList<LinearLayout> cards=new java.util.ArrayList<>();
    private long startedAt;
    private int ink, muted, surface, accent;
    private String shownCode;
    private TextView traffic,homeTraffic;
    private TextView gameAssistStatus;
    private TextView usageSummary;
    private DataShield fallbackUsage;
    private DataShield usageStore(){TunnelService s=TunnelService.instance;return s!=null&&s.dataShield!=null?s.dataShield:fallbackUsage;}
    private void addDataShield(LinearLayout analysis){
        fallbackUsage=new DataShield(this);
        analysis.addView(text("DataShield · aylık bütçe",22,ink));
        usageSummary=text("Sayaç bekleniyor",15,accent);analysis.addView(usageSummary);
        analysis.addView(text("Yalnızca TetherMax relay uygulama verisi. Telefonun diğer uygulamaları ve operatör faturası değildir. Windows sayacıyla toplamayın. UTC takvim ayı; 1 GB = 1 milyar bayt. %80 / %100 uyarıları interneti kesmez. Ani kapanışta son 30 saniye ve telemetri boşlukları eksik kalabilir.",13,muted));
        EditText amount=new EditText(this);amount.setTextColor(ink);amount.setHint("Aylık GB · 0 = uyarı kapalı");amount.setHintTextColor(muted);amount.setInputType(android.text.InputType.TYPE_CLASS_NUMBER|android.text.InputType.TYPE_NUMBER_FLAG_DECIMAL);amount.setText(Double.toString(usageStore().budget()/1e9));analysis.addView(amount);
        action(analysis,"Bütçeyi kaydet",()->{
            try{double gb=Double.parseDouble(amount.getText().toString().replace(',','.'));if(!Double.isFinite(gb)||gb<0||gb>1000000)throw new NumberFormatException();
                if(!usageStore().setBudget((long)(gb*1e9))){Toast.makeText(this,"Sayaç kaydı okunamıyor; bütçe kaydedilmedi.",Toast.LENGTH_LONG).show();return;}
                fallbackUsage=new DataShield(this);usageSummary.setText(usageStore().summary());
            }catch(RuntimeException invalid){Toast.makeText(this,"0–1.000.000 GB arasında bir bütçe girin.",Toast.LENGTH_LONG).show();}
        });
    }
    private TextView gameAssistDetails;
    private AlertDialog pairingDialog;
    private TrafficPlot speedPlot,delayPlot;
    private Spinner dns;
    private static final String[] DNS_IDS={"adguard","family","cloudflare","system"};
    private final Button[] modeButtons=new Button[5];
    private static final String[] MODE_DESCRIPTIONS={"Akıllı profil seçimi","Gecikme odaklı öncelikler","Video ve paylaşılan bağlantı","Dengeli kullanım","Aktarım odaklı profil"};
    private static final String[] MODE_IDS={"auto","gaming","streaming","daily","maximum"};
    private static final String[] MODE_LABELS={"Otomatik","Oyun","Film / Dizi","Günlük","Maksimum Hız"};
    private TunnelService sampledService;
    private long sampleAt,lastUp,lastDown,connectedSince;
    private final Runnable refresh = new Runnable() {
        public void run() {
            if(!visible)return;
            status.setText(TunnelService.status+"\n"+(TunnelService.instance==null?"Bağlantı bekleniyor":("Servis süresi · "+duration(SystemClock.elapsedRealtime()-TunnelService.startedAt))));
            TunnelService current=TunnelService.instance;
            refreshTraffic(current);
            refreshHealth(current);
            if(mascot!=null){if(TunnelService.reducedWork()||selectedPage!=0)mascot.pause();else mascot.resume();}
            if(TunnelService.reducedWork()&&positionWorker!=null){positionCanceled.set(true);positionWorker.interrupt();}
            String code = UsbPairing.pendingCode;
            if (code == null) { shownCode = null; if (pairingDialog != null) { pairingDialog.dismiss(); pairingDialog = null; } }
            if (code != null && !code.equals(shownCode)) {
                shownCode = code;
                if (pairingDialog != null) pairingDialog.dismiss();
                pairingDialog = new AlertDialog.Builder(MainActivity.this).setTitle("Bilgisayara güvenilsin mi?")
                    .setMessage("Windows ekranındaki numara aynı mı?\n\n" + code + "\n\nYalnızca kendiniz başlattığınız eşleştirmeyi onaylayın.")
                    .setPositiveButton("Aynı, güven", (d,w) -> UsbPairing.confirm(code,true))
                    .setNegativeButton("Reddet", (d,w) -> UsbPairing.confirm(code,false))
                    .setOnCancelListener(d -> UsbPairing.confirm(code,false)).create();
                pairingDialog.getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
                pairingDialog.show();
            }
            handler.postDelayed(this, TunnelService.reducedWork()?5000:selectedPage==6?3000:1000);
        }
    };
    private void refreshTraffic(TunnelService current) {
        if(current==null&&sampledService!=null&&fallbackUsage!=null)fallbackUsage=new DataShield(this);
        if(usageSummary!=null)usageSummary.setText(usageStore().summary());
        long now=SystemClock.elapsedRealtime(),up=current==null?0:current.uploaded(),down=current==null?0:current.downloaded();
        float upload=Float.NaN,download=Float.NaN,rtt=Float.NaN,jitter=Float.NaN;
        if(current!=null&&current==sampledService&&now>sampleAt&&up>=lastUp&&down>=lastDown){upload=(up-lastUp)*8f/1000f/(now-sampleAt);download=(down-lastDown)*8f/1000f/(now-sampleAt);}
        sampledService=current;sampleAt=now;lastUp=up;lastDown=down;
        String details="Windows politika onayı / gecikme ölçümü bekleniyor";
        Relay relay=current==null?null:current.currentRelay();
        if(relay!=null){
            if(dns!=null)for(int i=0;i<DNS_IDS.length;i++)if(DNS_IDS[i].equals(relay.dnsProfile())&&dns.getSelectedItemPosition()!=i)dns.setSelection(i);
            if(selectedPage==2&&relay.hostTelemetry!=null&&System.nanoTime()-relay.hostTelemetryNanos<5000000000L){
                try {
                    JSONObject value=new JSONObject(relay.hostTelemetry);
                    String active=value.optString("active","?");
                    details="Windows'ta etkin profil: "+modeLabel(active)+"\nAkış: "+value.optInt("ActiveFlows")+" · Kuyruk P95: "+value.optDouble("QueueP95Ms",0)+" ms";
                    if(!value.isNull("QueryRTTMs")){rtt=(float)value.getDouble("QueryRTTMs");if(!value.isNull("JitterMs"))jitter=(float)value.getDouble("JitterMs");details+="\nDNS gidiş-dönüş: "+rtt+" ms · Jitter: "+(Float.isNaN(jitter)?"—":Float.toString(jitter))+" ms";}
                    if(!value.isNull("ProbeLossPercent"))details+="\nDNS yanıtsız örnekler: %"+value.getDouble("ProbeLossPercent")+" / "+value.optInt("ProbeSamples")+" örnek";
                }catch(JSONException ignored){details="Windows ölçüm kaydı okunamadı";}
            }
        }
        if(relay!=null&&relay.dnsError()!=null)details="DNS-over-HTTPS hatası: "+relay.dnsError()+" · Korumasız DNS’e geçilmedi.\n"+details;
        if(gameAssistStatus!=null)gameAssistStatus.setText(!TunnelService.savedGamingAssist(this)?"Oyun desteği kapalı":relay!=null&&relay.gamingActive()?"Oyun desteği etkin":"Oyun bekleniyor");
        if(gameAssistDetails!=null)gameAssistDetails.setText(current==null?"Motor kapalı":current.gamingStatus());
        if(homeTraffic!=null)homeTraffic.setText(TunnelService.status+"\n"+TunnelService.networkInfo+"\nAktarılan toplam · "+bytes(up+down)+"\nProfil · "+modeLabel(TunnelService.savedMode(this))+"\n"+(current==null?"":current.gamingStatus()));
        boolean hostFresh=current!=null&&current.running()&&relay!=null&&relay.hostTelemetry!=null&&System.nanoTime()-relay.hostTelemetryNanos<5000000000L;
        if(mascot!=null)mascot.setConnected(hostFresh);
        status.setText(current==null?(TunnelService.status.startsWith("Motor başlatılamadı")?"Başlatılamadı":"Motor kapalı"):hostFresh?"Windows bağlı":current.running()?"Windows bekleniyor":"Hazırlanıyor");
        if(hostFresh){if(connectedSince==0)connectedSince=now;}else connectedSince=0;
        if(selectedPage==3){String selectedMode=TunnelService.savedMode(this);for(int i=0;i<modeButtons.length;i++)if(modeButtons[i]!=null){String label=(MODE_IDS[i].equals(selectedMode)?"✓  ":"")+MODE_LABELS[i];if(!label.contentEquals(modeButtons[i].getText()))modeButtons[i].setText(label);}}
        if(traffic!=null)traffic.setText(TunnelService.networkInfo+"\nYükleme: "+bytes(up)+" · İndirme: "+bytes(down)+"\n"+(Float.isNaN(upload)?"Hız örneği bekleniyor":String.format(java.util.Locale.ROOT,"İndirme %.3f / Yükleme %.3f Mbps",download,upload))+"\n"+details+"\nAktarılan uygulama verisi; operatör faturalandırması değildir.");
        if(downValue!=null){downValue.setText(Float.isNaN(download)?"—":String.format(java.util.Locale.getDefault(),"%.2f",download));upValue.setText(Float.isNaN(upload)?"—":String.format(java.util.Locale.getDefault(),"%.2f",upload));startButton.setEnabled(current==null);stopButton.setEnabled(current!=null);startButton.setVisibility(current==null?View.VISIBLE:View.GONE);stopButton.setVisibility(current!=null?View.VISIBLE:View.GONE);if(selectedPage==0&&!TunnelService.reducedWork())homePlot.add(download,upload);}
        if(selectedPage==2&&!TunnelService.reducedWork()){if(speedPlot!=null)speedPlot.add(download,upload);if(delayPlot!=null)delayPlot.add(rtt,jitter);}
    }
    private static String modeLabel(String id) { for(int i=0;i<MODE_IDS.length;i++)if(MODE_IDS[i].equals(id))return MODE_LABELS[i];return "Bilinmiyor"; }
    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
    private TextView text(String value, int size, int color) {
        TextView view = new TextView(this); view.setText(value); view.setTextSize(size); view.setTextColor(color);
        view.setIncludeFontPadding(false);view.setLineSpacing(dp(2),1);view.setPadding(0, dp(6), 0, dp(6)); return view;
    }
    private void addDetails(LinearLayout parent,String title,String copy){TextView body=text(copy,13,muted);body.setVisibility(View.GONE);action(parent,title,()->body.setVisibility(body.getVisibility()==View.GONE?View.VISIBLE:View.GONE));parent.addView(body);}
    private LinearLayout card(String title) {
        LinearLayout box = new LinearLayout(this); box.setOrientation(LinearLayout.VERTICAL);
        box.setPadding(dp(18), dp(16), dp(18), dp(16));
        GradientDrawable bg = new GradientDrawable(); bg.setColor(surface);bg.setStroke(dp(1),0x40B97B8D);bg.setCornerRadius(dp(20)); box.setBackground(bg);box.setElevation(0);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, -2); lp.bottomMargin = dp(16); box.setLayoutParams(lp);
        TextView label = text(title, 18, ink); label.setTypeface(Typeface.create("serif",Typeface.NORMAL)); box.addView(label); content.addView(box);cards.add(box); return box;
    }
    private Button action(LinearLayout box, String title, Runnable callback) {
        Button button=new Button(this);button.setText(title);button.setAllCaps(false);button.setTextSize(15);button.setMinHeight(dp(56));
        boolean primary=title.equals("Başlat");button.setTextColor(primary?Color.WHITE:ink);
        GradientDrawable shape=new GradientDrawable();shape.setColor(primary?Color.rgb(166,79,104):surface);shape.setCornerRadius(dp(14));shape.setStroke(dp(1),0x50B97B8D);
        button.setBackground(new android.graphics.drawable.RippleDrawable(android.content.res.ColorStateList.valueOf(0x33EFA3BD),shape,null));button.setElevation(dp(1));button.setStateListAnimator(null);
        button.setOnTouchListener((v,event)->{if(animate()){float scale=event.getAction()==MotionEvent.ACTION_DOWN?0.975f:1f;v.animate().scaleX(scale).scaleY(scale).setDuration(140).start();}return false;});
        button.setOnClickListener(v->{v.performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP);callback.run();});LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,-2);lp.topMargin=dp(10);box.addView(button,lp);return button;
    }
    private void prepareConnection(Intent intent) {
        if(!intent.getBooleanExtra("prepare_connection",false))return;
        intent.removeExtra("prepare_connection");
        if(TunnelService.instance!=null)return;
        try {PairingStore.get(this);startForegroundService(new Intent(this,TunnelService.class));}
        catch(Exception e){android.util.Log.e("TetherMax","Servis hazırlanamadı",e);}
    }
    @Override protected void onNewIntent(Intent intent){super.onNewIntent(intent);setIntent(intent);prepareConnection(intent);}
    @Override public void onCreate(Bundle saved) {
        boolean preferDark=getSharedPreferences("appearance",MODE_PRIVATE).getBoolean("dark",false);setTheme(preferDark?android.R.style.Theme_Material_NoActionBar:android.R.style.Theme_Material_Light_NoActionBar);
        super.onCreate(saved);
        prepareConnection(getIntent());

        boolean dark = getSharedPreferences("appearance",MODE_PRIVATE).getBoolean("dark",false);
        getWindow().setStatusBarColor(Color.parseColor(dark?"#20191F":"#FBF7F5"));getWindow().setNavigationBarColor(Color.parseColor(dark?"#2C222B":"#FFFCFA"));getWindow().getDecorView().setSystemUiVisibility(dark?0:View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR|View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR);
        ink = Color.parseColor(dark ? "#FFF4F4" : "#3B2336"); muted = Color.parseColor(dark ? "#C5ADB9" : "#786670");
        surface = Color.parseColor(dark ? "#2C222B" : "#FFFCFA"); accent = Color.parseColor(dark ? "#ECA5B9" : "#A64F68");
        ScrollView scroll = new ScrollView(this);pageScroll=scroll; scroll.setFillViewport(true); scroll.setBackgroundColor(Color.parseColor(dark ? "#20191F" : "#FBF7F5"));
        content = new LinearLayout(this); content.setOrientation(LinearLayout.VERTICAL); content.setPadding(dp(16), dp(18), dp(16), dp(20)); scroll.addView(content);
        TextView heading=text("TetherMax",30,ink);heading.setTypeface(Typeface.create("serif",Typeface.NORMAL));heading.setPadding(dp(4),dp(4),0,dp(18));content.addView(heading);
        LinearLayout connection = card("USB bağlantısı");
        LinearLayout connectionRow=new LinearLayout(this);connectionRow.setGravity(Gravity.CENTER_VERTICAL);status=text("Motor kapalı",20,ink);connectionRow.addView(status,new LinearLayout.LayoutParams(0,-2,1));mascot=new RomanceView(this);connectionRow.addView(mascot,new LinearLayout.LayoutParams(dp(150),dp(110)));connection.addView(connectionRow);
        startButton=action(connection, "Başlat", () -> {
            try { PairingStore.get(this); startForegroundService(new Intent(this, TunnelService.class)); }
            catch (Exception e) { error("Güvenli eşleştirme deposu açılamadı. Motor başlatılmadı."); }
        });
        stopButton=action(connection, "Motoru durdur", () -> stopService(new Intent(this, TunnelService.class)));
        LinearLayout speedRow=new LinearLayout(this);speedRow.setPadding(0,dp(14),0,dp(10));
        for(int i=0;i<2;i++){LinearLayout metric=new LinearLayout(this);metric.setOrientation(LinearLayout.VERTICAL);metric.addView(text(i==0?"↓ İndirme":"↑ Yükleme",12,muted));TextView value=text("—",28,ink);value.setTypeface(null,Typeface.BOLD);metric.addView(value);metric.addView(text("Mbps",11,muted));if(i==0)downValue=value;else upValue=value;speedRow.addView(metric,new LinearLayout.LayoutParams(0,-2,1));}connection.addView(speedRow);
        homePlot=new TrafficPlot(this,"Mbps",muted);connection.addView(homePlot,new LinearLayout.LayoutParams(-1,dp(110)));
        homeTraffic=text("Windows istemcisi bekleniyor",13,muted);homeTraffic.setVisibility(View.GONE);Button detail=action(connection,"Bağlantı ayrıntıları",()->homeTraffic.setVisibility(homeTraffic.getVisibility()==View.GONE?View.VISIBLE:View.GONE));connection.addView(homeTraffic);
        action(connection,"Eşleştirme ve cihazlar  ↗",()->showPage(1));
        action(connection,"Telefon sağlığı  ↗",()->showPage(6));
        LinearLayout pairing = card("EŞLEŞTİRME");
        pairing.addView(text("Anahtar cihazda korunur", 20, ink));
        pairing.addView(text("Windows uygulamasında Bağlan seçeneğini kullanın. İlk bağlantıda iki ekrandaki kısa numarayı karşılaştırın. Sonraki bağlantılarda güvenilen bilgisayar hatırlanır.", 14, muted));
        action(pairing, "Güvenilen bilgisayarları unut", () -> new AlertDialog.Builder(this).setTitle("Eşleştirmeler silinsin mi?").setMessage("Motor duracak; bilgisayarlara yeniden güven onayı gerekecek.").setPositiveButton("Unut", (d,w) -> { stopService(new Intent(this,TunnelService.class)); UsbPairing.forget(this); }).setNegativeButton("Vazgeç",null).show());
        action(pairing,"Güvenilen cihazlar  ↗",()->showPage(4));
        LinearLayout analysis = card("İNTERNET ANALİZİ");
        addDataShield(analysis);
        addPositionTest(analysis);
        traffic = text("Henüz trafik yok", 18, ink); analysis.addView(traffic);
        speedPlot=new TrafficPlot(this,"Mbps",muted);analysis.addView(speedPlot,new LinearLayout.LayoutParams(-1,dp(140)));
        analysis.addView(text("Pembe indirme · lavanta yükleme. Gerçek örnekler.",12,muted));
        delayPlot=new TrafficPlot(this,"ms",muted);analysis.addView(delayPlot,new LinearLayout.LayoutParams(-1,dp(140)));
        analysis.addView(text("Pembe DNS gidiş-dönüş · lavanta jitter. Ölçüm yoksa grafik boş kalır. Adaptif ölçüm Windows'tan açılır; mobil veri kullanır.",12,muted));
        Spinner history=new Spinner(this);history.setAdapter(new ArrayAdapter<String>(this,android.R.layout.simple_spinner_dropdown_item,new String[]{"1 dakika","5 dakika","30 dakika"}));analysis.addView(history);history.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> p){}public void onItemSelected(AdapterView<?> p,View v,int position,long id){int minutes=new int[]{1,5,30}[position];speedPlot.setMinutes(minutes);delayPlot.setMinutes(minutes);}});
        LinearLayout modes=card("TRAFİK PROFİLİ");
        for(int i=0;i<MODE_IDS.length;i++){final int selected=i;modeButtons[i]=action(modes,MODE_LABELS[i],()->{TunnelService.requestMode(MainActivity.this,MODE_IDS[selected]);refreshTraffic(TunnelService.instance);});modeButtons[i].setContentDescription(MODE_LABELS[i]+". "+MODE_DESCRIPTIONS[i]);}
        Switch assist=new Switch(this);assist.setText("Otomatik oyun desteği");assist.setTextColor(ink);assist.setChecked(TunnelService.savedGamingAssist(this));modes.addView(assist);assist.setOnCheckedChangeListener((button,checked)->{TunnelService.requestGamingAssist(this,checked);refreshTraffic(TunnelService.instance);});
        gameAssistStatus=text("Oyun desteği durumu bekleniyor",14,accent);modes.addView(gameAssistStatus);
        gameAssistDetails=text("Motor bekleniyor",13,muted);gameAssistDetails.setVisibility(View.GONE);action(modes,"Oyun desteği ayrıntıları",()->gameAssistDetails.setVisibility(gameAssistDetails.getVisibility()==View.GONE?View.VISIBLE:View.GONE));modes.addView(gameAssistDetails);
        addDetails(modes,"Nasıl çalışır?","PC'deki seçili oyun veya Oyun profili ile etkinleşir. Büyük TCP yüklemeleri güncel Windows tavanının %85'ini paylaşır; UDP ve küçük TCP yazmaları bu kuyruğa girmez. Tavan yoksa hız kısıtlanmaz. Oyun seçimi Windows'ta yapılır. Telefonun diğer uygulamalarını kapsamaz.");
        LinearLayout devicesPage=card("CİHAZLAR");devicesPage.addView(text("Güvenilen Windows bilgisayarı",22,ink));devicesPage.addView(text("Windows bağlantısının durumu Ana Sayfa üzerinde gerçek telemetriyle gösterilir. Hotspot cihazları Windows uygulamasından yönetilir; telefonda cihaz başına ölçüm yok.",15,muted));
        action(devicesPage,"Eşleştirme ayarları  ↗",()->showPage(1));
        LinearLayout scope = card("AYARLAR");
        action(scope,"Güvenilen cihazlar  ↗",()->showPage(4));
        scope.addView(text("DNS filtresi · tüm paylaşılan normal DNS sorguları",18,ink));
        dns=new Spinner(this);dns.setAdapter(new ArrayAdapter<String>(this,android.R.layout.simple_spinner_dropdown_item,new String[]{"AdGuard DoH · reklam / izleyici filtresi","AdGuard Aile DoH","Cloudflare DoH","Özgün DNS"}));
        String savedDns=TunnelService.savedDns(this);for(int i=0;i<DNS_IDS.length;i++)if(DNS_IDS[i].equals(savedDns))dns.setSelection(i);scope.addView(dns);
        dns.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> p){}public void onItemSelected(AdapterView<?> p,View v,int i,long id){TunnelService.requestDns(MainActivity.this,DNS_IDS[i]);}});
        scope.addView(text("Şifreli DNS kullanan uygulamalar bu filtreden ayrı çalışabilir. YouTube reklamlarının tamamı DNS ile engellenmez.",13,muted));
        scope.addView(text("TCP / UDP aktarım motoru", 20, ink));
        action(scope,"Telefon sağlığı  ↗",()->showPage(6));
        scope.addView(text("Ölçümler yalnızca TetherMax aktarımına aittir. Operatör faturalandırması veya oyun performansı için garanti verilmez.", 14, muted));
        action(scope,"Açık / koyu tema",()->{getSharedPreferences("appearance",MODE_PRIVATE).edit().putBoolean("dark",!dark).apply();recreate();});
        Spinner motion=new Spinner(this);motion.setAdapter(new ArrayAdapter<String>(this,android.R.layout.simple_spinner_dropdown_item,new String[]{"Animasyonlar · Kapalı","Animasyonlar · Az","Animasyonlar · Tam"}));motion.setSelection(getSharedPreferences("appearance",MODE_PRIVATE).getInt("motion",1));scope.addView(motion);motion.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> p){}public void onItemSelected(AdapterView<?> p,View v,int i,long id){getSharedPreferences("appearance",MODE_PRIVATE).edit().putInt("motion",i).apply();mascot.pause();mascot.resume();}});
        Switch glass=new Switch(this);glass.setText("Saydam kartlar");glass.setTextColor(ink);glass.setChecked(getSharedPreferences("appearance",MODE_PRIVATE).getBoolean("glass",true));scope.addView(glass);glass.setOnCheckedChangeListener((b,checked)->{getSharedPreferences("appearance",MODE_PRIVATE).edit().putBoolean("glass",checked).apply();recreate();});
        LinearLayout healthCard=card("Telefon sağlığı");
        healthValue=text("Sağlık verileri bekleniyor",16,ink);healthCard.addView(healthValue);
        healthAdvice=text("",14,muted);healthCard.addView(healthAdvice);
        healthCard.addView(text("CPU yalnızca TetherMax işlemine aittir; çok çekirdekte %100'ü aşabilir. RAM: Java + yerel + paylaşımlı bellek PSS. Pil sıcaklığı CPU sıcaklığı değildir. USB güç kaynağı, veri kanalının kanıtı değildir.",13,muted));
        healthCard.addView(text("Otomatik koruma isteğe bağlı görselleri ve analizi azaltır; mevcut aktarım hızını, DNS'i veya ağ seçimini değiştirmez. Isı normale döndükten 60 saniye sonra geri alınır. Android'in zorunlu termal kapanmasını önleyemez.",13,muted));
        action(healthCard,"Pil ayarlarını aç",()->openSettings("android.intent.action.POWER_USAGE_SUMMARY",false));
        healthCard.addView(text("Samsung: Ayarlar → Pil → Pil koruması → Maksimum. One UI 7 ve sonrasında desteklenen cihazlarda %80/85/90/95 sınırları bulunur. Bu düğme genel pil ekranını açar; şarj sınırını Samsung ayarından seçin. TetherMax şarjı kesmez.",13,muted));
        action(healthCard,"Uygulama pil / arka plan ayarları",()->openSettings(android.provider.Settings.ACTION_APPLICATION_DETAILS_SETTINGS,true));
        action(healthCard,"Pil optimizasyon listesini aç",()->openSettings(android.provider.Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS,false));
        healthCard.addView(text("Kesinti varsa Samsung Pil → Arka plan kullanım sınırları → Asla uyumayan uygulamalar listesini kontrol edin. Ayarlar sürüme göre değişebilir. Güç tasarrufunu otomatik açmayız.",13,muted));
        if(Build.VERSION.SDK_INT>=33)action(healthCard,"Kritik ısı bildirimlerine izin ver",()->requestPermissions(new String[]{android.Manifest.permission.POST_NOTIFICATIONS},42));
        LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);
        root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));
        LinearLayout nav=new LinearLayout(this);nav.setPadding(dp(8),dp(8),dp(8),dp(12));nav.setBackgroundColor(surface);
        navBar=nav;String[] destinations={"Ana Sayfa","Analiz","Oyun","Ayarlar"};String[] iconIds={"ic_home","ic_analysis","ic_game","ic_settings"};int[] pageIds={0,2,3,5};
        for(int i=0;i<destinations.length;i++){
            final int destination=pageIds[i];TextView item=text(destinations[i],12,i==0?accent:muted);item.setGravity(Gravity.CENTER);item.setContentDescription(destinations[i].replace('\n',' '));
            android.graphics.drawable.Drawable icon=getDrawable(getResources().getIdentifier(iconIds[i],"drawable",getPackageName())).mutate();icon.setTint(i==0?accent:muted);icon.setBounds(0,0,dp(22),dp(22));item.setCompoundDrawables(null,icon,null,null);item.setCompoundDrawablePadding(dp(4));
            nav.addView(item,new LinearLayout.LayoutParams(0,dp(60),1));item.setOnClickListener(v->showPage(destination));
        }
        root.setOnApplyWindowInsetsListener((v,insets)->{if(android.os.Build.VERSION.SDK_INT>=30){android.graphics.Insets safe=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());v.setPadding(safe.left,safe.top,safe.right,safe.bottom);}return insets;});
        root.addView(nav);for(int j=1;j<cards.size();j++)cards.get(j).setVisibility(View.GONE);
        stage=new FrameLayout(this);stage.addView(root);setContentView(stage);
        if(saved!=null){selectedPage=saved.getInt("page",0);showPage(Math.max(0,Math.min(selectedPage,6)));}
    }
    private void showPage(int page){selectedPage=page;for(int i=0;i<cards.size();i++)cards.get(i).setVisibility(i==page?View.VISIBLE:View.GONE);int[] destinations={0,2,3,5};for(int i=0;i<navBar.getChildCount();i++){TextView item=(TextView)navBar.getChildAt(i);boolean active=destinations[i]==page;item.setTextColor(active?accent:muted);if(item.getCompoundDrawables()[1]!=null)item.getCompoundDrawables()[1].setTint(active?accent:muted);GradientDrawable pill=new GradientDrawable();pill.setCornerRadius(dp(16));pill.setColor(active?0x20A64F68:Color.TRANSPARENT);item.setBackground(pill);}if(animate()){cards.get(page).setAlpha(0.3f);cards.get(page).setTranslationY(dp(8));cards.get(page).animate().alpha(1).translationY(0).setDuration(200).start();}mascot.pause();if(page==0)mascot.resume();pageScroll.smoothScrollTo(0,0);}
    private static String duration(long millis){long seconds=millis/1000;return String.format(java.util.Locale.ROOT,"%02d:%02d:%02d",seconds/3600,(seconds/60)%60,seconds%60);}
    @Override protected void onSaveInstanceState(Bundle saved){saved.putInt("page",selectedPage);super.onSaveInstanceState(saved);}
    private void error(String message) { new AlertDialog.Builder(this).setTitle("İşlem tamamlanamadı").setMessage(message).setPositiveButton("Tamam", null).show(); }
    @Override public void onResume() { super.onResume();visible=true; handler.removeCallbacks(refresh);handler.post(refresh);if(mascot!=null&&selectedPage==0)mascot.resume(); }
    @Override public void onPause() { visible=false;positionCanceled.set(true);if(positionWorker!=null)positionWorker.interrupt();sampledService=null;handler.removeCallbacks(refresh);handler.removeCallbacks(dismissWelcome);dismissWelcome.run();if(mascot!=null)mascot.pause();if(pairingDialog!=null){pairingDialog.dismiss();pairingDialog=null;shownCode=null;}super.onPause(); }
}
