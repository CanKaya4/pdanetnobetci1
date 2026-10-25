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
    private boolean animate(){return getSharedPreferences("appearance",MODE_PRIVATE).getInt("motion",1)>0&&android.provider.Settings.Global.getFloat(getContentResolver(),"animator_duration_scale",1)>0;}
    private static String bytes(long value){double v=value;String[] units={"B","KB","MB","GB","TB"};int i=0;while(v>=1000&&i<4){v/=1000;i++;}return String.format(java.util.Locale.getDefault(),"%.2f %s",v,units[i]);}
    private LinearLayout content;
    private final java.util.ArrayList<LinearLayout> cards=new java.util.ArrayList<>();
    private long startedAt;
    private int ink, muted, surface, accent;
    private String shownCode;
    private TextView traffic,homeTraffic;
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
            status.setText(TunnelService.status+"\n"+(TunnelService.instance==null?"Bağlantı bekleniyor":("Servis süresi · "+duration(SystemClock.elapsedRealtime()-TunnelService.startedAt))));
            TunnelService current=TunnelService.instance;
            refreshTraffic(current);
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
            handler.postDelayed(this, 1000);
        }
    };
    private void refreshTraffic(TunnelService current) {
        long now=SystemClock.elapsedRealtime(),up=current==null?0:current.uploaded(),down=current==null?0:current.downloaded();
        float upload=Float.NaN,download=Float.NaN,rtt=Float.NaN,jitter=Float.NaN;
        if(current!=null&&current==sampledService&&now>sampleAt&&up>=lastUp&&down>=lastDown){upload=(up-lastUp)*8f/1000f/(now-sampleAt);download=(down-lastDown)*8f/1000f/(now-sampleAt);}
        sampledService=current;sampleAt=now;lastUp=up;lastDown=down;
        String details="Windows politika onayı / gecikme ölçümü bekleniyor";
        Relay relay=current==null?null:current.currentRelay();
        if(relay!=null){
            if(dns!=null)for(int i=0;i<DNS_IDS.length;i++)if(DNS_IDS[i].equals(relay.dnsProfile())&&dns.getSelectedItemPosition()!=i)dns.setSelection(i);
            if(relay.hostTelemetry!=null&&System.nanoTime()-relay.hostTelemetryNanos<5000000000L){
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
        if(homeTraffic!=null)homeTraffic.setText(TunnelService.networkInfo+"\nAktarılan toplam · "+bytes(up+down)+"\nProfil · "+modeLabel(TunnelService.savedMode(this))+"\n"+(relay!=null&&relay.hostTelemetry!=null&&System.nanoTime()-relay.hostTelemetryNanos<5000000000L?"Güvenilen Windows istemcisi bağlı":"Windows istemcisi bekleniyor"));
        if(current!=null&&current.running()&&relay!=null&&relay.hostTelemetry!=null&&System.nanoTime()-relay.hostTelemetryNanos<5000000000L){if(connectedSince==0)connectedSince=now;status.setText("Windows bağlı\nGüvenli USB · Bu ekranda "+duration(now-connectedSince));}else connectedSince=0;
        for(int i=0;i<modeButtons.length;i++)if(modeButtons[i]!=null)modeButtons[i].setText((MODE_IDS[i].equals(TunnelService.savedMode(this))?"✓ ":"")+MODE_LABELS[i]+"\n"+MODE_DESCRIPTIONS[i]);
        if(traffic!=null)traffic.setText(TunnelService.networkInfo+"\nYükleme: "+bytes(up)+" · İndirme: "+bytes(down)+"\n"+(Float.isNaN(upload)?"Hız örneği bekleniyor":String.format(java.util.Locale.ROOT,"İndirme %.3f / Yükleme %.3f Mbps",download,upload))+"\n"+details+"\nAktarılan uygulama verisi; operatör faturalandırması değildir.");
        if(downValue!=null){downValue.setText(Float.isNaN(download)?"— Mbps":String.format(java.util.Locale.getDefault(),"%.2f Mbps",download));upValue.setText(Float.isNaN(upload)?"— Mbps":String.format(java.util.Locale.getDefault(),"%.2f Mbps",upload));startButton.setEnabled(current==null);stopButton.setEnabled(current!=null);homePlot.add(download,upload);}
        if(speedPlot!=null)speedPlot.add(download,upload);if(delayPlot!=null)delayPlot.add(rtt,jitter);
    }
    private static String modeLabel(String id) { for(int i=0;i<MODE_IDS.length;i++)if(MODE_IDS[i].equals(id))return MODE_LABELS[i];return "Bilinmiyor"; }
    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
    private TextView text(String value, int size, int color) {
        TextView view = new TextView(this); view.setText(value); view.setTextSize(size); view.setTextColor(color);
        view.setPadding(0, dp(6), 0, dp(6)); return view;
    }
    private LinearLayout card(String title) {
        LinearLayout box = new LinearLayout(this); box.setOrientation(LinearLayout.VERTICAL);
        box.setPadding(dp(20), dp(16), dp(20), dp(16));
        GradientDrawable bg = new GradientDrawable(); bg.setColor(surface);bg.setStroke(dp(1),0x33B7A6F6);if(getSharedPreferences("appearance",MODE_PRIVATE).getBoolean("glass",true))bg.setAlpha(245); bg.setCornerRadius(dp(24)); box.setBackground(bg);box.setElevation(dp(2));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, -2); lp.bottomMargin = dp(16); box.setLayoutParams(lp);
        TextView label = text(title, 14, accent); label.setTypeface(null, Typeface.BOLD); box.addView(label); content.addView(box);cards.add(box); return box;
    }
    private Button action(LinearLayout box, String title, Runnable callback) {
        Button button=new Button(this);button.setText(title);button.setAllCaps(false);button.setTextSize(15);button.setMinHeight(dp(56));
        boolean primary=title.startsWith("Hücresel");button.setTextColor(primary?Color.WHITE:ink);
        GradientDrawable shape=new GradientDrawable();shape.setColor(primary?Color.rgb(101,85,203):surface);shape.setCornerRadius(dp(18));shape.setStroke(dp(1),accent);
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
        getWindow().setStatusBarColor(Color.parseColor(dark?"#111217":"#F6F4FF"));getWindow().setNavigationBarColor(Color.parseColor(dark?"#1C1D26":"#F6F4FF"));getWindow().getDecorView().setSystemUiVisibility(dark?0:View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR|View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR);
        ink = Color.parseColor(dark ? "#F6F4FF" : "#252638"); muted = Color.parseColor(dark ? "#A9A6BD" : "#696B80");
        surface = Color.parseColor(dark ? "#1C1D26" : "#FFFFFF"); accent = Color.parseColor(dark ? "#BDB1FF" : "#6555CB");
        ScrollView scroll = new ScrollView(this);pageScroll=scroll; scroll.setFillViewport(true); scroll.setBackgroundColor(Color.parseColor(dark ? "#111217" : "#F5F5F8"));
        content = new LinearLayout(this); content.setOrientation(LinearLayout.VERTICAL); content.setPadding(dp(20), dp(28), dp(20), dp(28)); scroll.addView(content);
        content.addView(text("TETHERMAX  /  STUDIO 2026",11,accent));TextView heading=text("İyi ki bağlıyız.",32,ink);heading.setTypeface(null,Typeface.BOLD);content.addView(heading);
        content.addView(text("Senin için daha güzel bir bağlantı.",14,muted));
        LinearLayout connection = card("BAĞLANTININ EN GÜZEL HALİ");mascot=new RomanceView(this);connection.addView(mascot,new LinearLayout.LayoutParams(-1,dp(165))); status = text(TunnelService.status, 22, ink); connection.addView(status);
        homeTraffic=text("Windows istemcisi bekleniyor",13,muted);connection.addView(homeTraffic);
        startButton=action(connection, "Hücresel motoru başlat", () -> {
            try { PairingStore.get(this); startForegroundService(new Intent(this, TunnelService.class)); }
            catch (Exception e) { error("Güvenli eşleştirme deposu açılamadı. Motor başlatılmadı."); }
        });
        stopButton=action(connection, "Motoru durdur", () -> stopService(new Intent(this, TunnelService.class)));
        LinearLayout speedRow=new LinearLayout(this);speedRow.setPadding(0,dp(14),0,dp(10));
        for(int i=0;i<2;i++){LinearLayout metric=new LinearLayout(this);metric.setOrientation(LinearLayout.VERTICAL);metric.addView(text(i==0?"↓ İNDİRME":"↑ YÜKLEME",11,muted));TextView value=text("— Mbps",24,ink);value.setTypeface(null,Typeface.BOLD);metric.addView(value);if(i==0)downValue=value;else upValue=value;speedRow.addView(metric,new LinearLayout.LayoutParams(0,-2,1));}connection.addView(speedRow);
        homePlot=new TrafficPlot(this,"Mbps",muted);connection.addView(homePlot,new LinearLayout.LayoutParams(-1,dp(130)));
        action(connection,"Eşleştirme ve cihazlar  ↗",()->showPage(1));
        LinearLayout pairing = card("EŞLEŞTİRME");
        pairing.addView(text("Anahtar cihazda korunur", 20, ink));
        pairing.addView(text("Windows uygulamasında Bağlan seçeneğini kullanın. İlk bağlantıda iki ekrandaki kısa numarayı karşılaştırın. Sonraki bağlantılarda güvenilen bilgisayar hatırlanır.", 14, muted));
        action(pairing, "Güvenilen bilgisayarları unut", () -> new AlertDialog.Builder(this).setTitle("Eşleştirmeler silinsin mi?").setMessage("Motor duracak; bilgisayarlara yeniden güven onayı gerekecek.").setPositiveButton("Unut", (d,w) -> { stopService(new Intent(this,TunnelService.class)); UsbPairing.forget(this); }).setNegativeButton("Vazgeç",null).show());
        action(pairing,"Güvenilen cihazlar  ↗",()->showPage(4));
        LinearLayout analysis = card("İNTERNET ANALİZİ");
        traffic = text("Henüz trafik yok", 18, ink); analysis.addView(traffic);
        speedPlot=new TrafficPlot(this,"Mbps",muted);analysis.addView(speedPlot,new LinearLayout.LayoutParams(-1,dp(140)));
        analysis.addView(text("Pembe indirme · lavanta yükleme. Gerçek örnekler.",12,muted));
        delayPlot=new TrafficPlot(this,"ms",muted);analysis.addView(delayPlot,new LinearLayout.LayoutParams(-1,dp(140)));
        analysis.addView(text("Pembe DNS gidiş-dönüş · lavanta jitter. Ölçüm yoksa grafik boş kalır. Adaptif ölçüm Windows'tan açılır; mobil veri kullanır.",12,muted));
        Spinner history=new Spinner(this);history.setAdapter(new ArrayAdapter<String>(this,android.R.layout.simple_spinner_dropdown_item,new String[]{"1 dakika","5 dakika","30 dakika"}));analysis.addView(history);history.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> p){}public void onItemSelected(AdapterView<?> p,View v,int position,long id){int minutes=new int[]{1,5,30}[position];speedPlot.setMinutes(minutes);delayPlot.setMinutes(minutes);}});
        LinearLayout modes=card("TRAFİK PROFİLİ");
        for(int i=0;i<MODE_IDS.length;i++){final int selected=i;modeButtons[i]=action(modes,MODE_LABELS[i]+"\n"+MODE_DESCRIPTIONS[i],()->{TunnelService.requestMode(MainActivity.this,MODE_IDS[selected]);refreshTraffic(TunnelService.instance);});}
        modes.addView(text("Windows bağlandığında seçim aynı USB motorunun zamanlayıcısına iletilir. Manuel seçim otomatik oyun algılamasıyla değiştirilmez. Sistem geneli yönlendirme açılmaz.",14,muted));
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
        scope.addView(text("Ölçümler yalnızca TetherMax aktarımına aittir. Operatör faturalandırması veya oyun performansı için garanti verilmez.", 14, muted));
        action(scope,"Daylight / After Hours",()->{getSharedPreferences("appearance",MODE_PRIVATE).edit().putBoolean("dark",!dark).apply();recreate();});
        Spinner motion=new Spinner(this);motion.setAdapter(new ArrayAdapter<String>(this,android.R.layout.simple_spinner_dropdown_item,new String[]{"Animasyonlar · Kapalı","Animasyonlar · Az","Animasyonlar · Tam"}));motion.setSelection(getSharedPreferences("appearance",MODE_PRIVATE).getInt("motion",1));scope.addView(motion);motion.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> p){}public void onItemSelected(AdapterView<?> p,View v,int i,long id){getSharedPreferences("appearance",MODE_PRIVATE).edit().putInt("motion",i).apply();mascot.pause();mascot.resume();}});
        Switch glass=new Switch(this);glass.setText("Saydam kartlar");glass.setTextColor(ink);glass.setChecked(getSharedPreferences("appearance",MODE_PRIVATE).getBoolean("glass",true));scope.addView(glass);glass.setOnCheckedChangeListener((b,checked)->{getSharedPreferences("appearance",MODE_PRIVATE).edit().putBoolean("glass",checked).apply();recreate();});
        LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);
        root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));
        LinearLayout nav=new LinearLayout(this);nav.setPadding(dp(8),dp(8),dp(8),dp(12));nav.setBackgroundColor(surface);
        navBar=nav;String[] destinations={"⌂\nAna Sayfa","↗\nAnaliz","◇\nProfiller","⚙\nAyarlar"};int[] pageIds={0,2,3,5};
        for(int i=0;i<destinations.length;i++){
            final int destination=pageIds[i];TextView item=text(destinations[i],12,i==0?accent:muted);item.setGravity(Gravity.CENTER);item.setContentDescription(destinations[i].replace('\n',' '));
            nav.addView(item,new LinearLayout.LayoutParams(0,dp(60),1));item.setOnClickListener(v->showPage(destination));
        }
        root.setOnApplyWindowInsetsListener((v,insets)->{if(android.os.Build.VERSION.SDK_INT>=30){android.graphics.Insets safe=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());v.setPadding(safe.left,safe.top,safe.right,safe.bottom);}return insets;});
        root.addView(nav);for(int j=1;j<cards.size();j++)cards.get(j).setVisibility(View.GONE);
        stage=new FrameLayout(this);stage.addView(root);setContentView(stage);
        if(saved!=null){selectedPage=saved.getInt("page",0);showPage(Math.max(0,Math.min(selectedPage,5)));}
        if(saved==null&&animate()){
            LinearLayout intro=new LinearLayout(this);intro.setOrientation(LinearLayout.VERTICAL);intro.setGravity(Gravity.CENTER);intro.setBackgroundColor(Color.parseColor(dark?"#111217":"#F6F4FF"));
            RomanceView art=new RomanceView(this);intro.addView(art,new LinearLayout.LayoutParams(-1,dp(180)));art.resume();intro.addView(text("TetherMax",38,ink));intro.addView(text("Birlikte, hep bağlı ♡",18,muted));Button skip=new Button(this);skip.setText("Atla");skip.setOnClickListener(v->dismissWelcome.run());intro.addView(skip);welcome=intro;stage.addView(intro,new FrameLayout.LayoutParams(-1,-1));handler.postDelayed(dismissWelcome,1400);
        }
    }
    private void showPage(int page){selectedPage=page;for(int i=0;i<cards.size();i++)cards.get(i).setVisibility(i==page?View.VISIBLE:View.GONE);int[] destinations={0,2,3,5};for(int i=0;i<navBar.getChildCount();i++){TextView item=(TextView)navBar.getChildAt(i);boolean active=destinations[i]==page;item.setTextColor(active?accent:muted);GradientDrawable pill=new GradientDrawable();pill.setCornerRadius(dp(16));pill.setColor(active?0x206555CB:Color.TRANSPARENT);item.setBackground(pill);}if(animate()){cards.get(page).setAlpha(0.3f);cards.get(page).setTranslationY(dp(8));cards.get(page).animate().alpha(1).translationY(0).setDuration(200).start();}mascot.pause();if(page==0)mascot.resume();pageScroll.smoothScrollTo(0,0);}
    private static String duration(long millis){long seconds=millis/1000;return String.format(java.util.Locale.ROOT,"%02d:%02d:%02d",seconds/3600,(seconds/60)%60,seconds%60);}
    @Override protected void onSaveInstanceState(Bundle saved){saved.putInt("page",selectedPage);super.onSaveInstanceState(saved);}
    private void error(String message) { new AlertDialog.Builder(this).setTitle("İşlem tamamlanamadı").setMessage(message).setPositiveButton("Tamam", null).show(); }
    @Override public void onResume() { super.onResume(); handler.removeCallbacks(refresh);handler.post(refresh);if(mascot!=null)mascot.resume(); }
    @Override public void onPause() { handler.removeCallbacks(refresh);handler.removeCallbacks(dismissWelcome);dismissWelcome.run();if(mascot!=null)mascot.pause();if(pairingDialog!=null){pairingDialog.dismiss();pairingDialog=null;shownCode=null;}super.onPause(); }
}
