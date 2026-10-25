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
    private long sampleAt,lastUp,lastDown;
    private final Runnable refresh = new Runnable() {
        public void run() {
            status.setText(TunnelService.status+"\n"+(TunnelService.instance==null?"Bağlantı bekleniyor":("Servis süresi · "+((SystemClock.elapsedRealtime()-TunnelService.startedAt)/1000)+" sn")));
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
        if(homeTraffic!=null)homeTraffic.setText(TunnelService.networkInfo+"\n"+(Float.isNaN(download)?"Hız ölçümü bekleniyor":String.format(java.util.Locale.ROOT,"↓ %.2f Mbps     ↑ %.2f Mbps",download,upload))+"\nAktarılan toplam · "+(up+down)+" B\nProfil · "+modeLabel(TunnelService.savedMode(this))+"\n"+(relay!=null&&relay.hostTelemetry!=null&&System.nanoTime()-relay.hostTelemetryNanos<5000000000L?"Güvenilen Windows istemcisi bağlı":"Windows istemcisi bekleniyor"));
        if(current!=null&&current.running()&&relay!=null&&relay.hostTelemetry!=null&&System.nanoTime()-relay.hostTelemetryNanos<5000000000L)status.setText("Windows bağlı\nGüvenli USB aktarımı etkin");
        for(int i=0;i<modeButtons.length;i++)if(modeButtons[i]!=null)modeButtons[i].setText((MODE_IDS[i].equals(TunnelService.savedMode(this))?"✓ ":"")+MODE_LABELS[i]+"\n"+MODE_DESCRIPTIONS[i]);
        if(traffic!=null)traffic.setText(TunnelService.networkInfo+"\nYükleme: "+up+" B · İndirme: "+down+" B\n"+(Float.isNaN(upload)?"Hız örneği bekleniyor":String.format(java.util.Locale.ROOT,"İndirme %.3f / Yükleme %.3f Mbps",download,upload))+"\n"+details+"\nAktarılan uygulama verisi; operatör faturalandırması değildir.");
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
        GradientDrawable bg = new GradientDrawable(); bg.setColor(surface); bg.setCornerRadius(dp(24)); box.setBackground(bg);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, -2); lp.bottomMargin = dp(16); box.setLayoutParams(lp);
        TextView label = text(title, 14, accent); label.setTypeface(null, Typeface.BOLD); box.addView(label); content.addView(box);cards.add(box); return box;
    }
    private Button action(LinearLayout box, String title, Runnable callback) {
        Button button=new Button(this);button.setText(title);button.setAllCaps(false);button.setTextSize(15);
        boolean primary=title.startsWith("Hücresel");button.setTextColor(primary?(ink==Color.parseColor("#F1F5FC")?Color.parseColor("#082C35"):Color.WHITE):ink);
        GradientDrawable shape=new GradientDrawable();shape.setColor(primary?accent:Color.parseColor(ink==Color.parseColor("#F1F5FC")?"#28425C":"#DCECF2"));shape.setCornerRadius(dp(18));
        button.setBackground(new android.graphics.drawable.RippleDrawable(android.content.res.ColorStateList.valueOf(0x3355DCE7),shape,null));button.setElevation(0);button.setStateListAnimator(null);
        button.setOnClickListener(v->callback.run());LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,dp(56));lp.topMargin=dp(10);box.addView(button,lp);return button;
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
        super.onCreate(saved);
        prepareConnection(getIntent());

        boolean dark = getSharedPreferences("appearance",MODE_PRIVATE).getBoolean("dark",true);
        ink = Color.parseColor(dark ? "#F1F5FC" : "#12213A"); muted = Color.parseColor(dark ? "#ADBBD0" : "#52637B");
        surface = Color.parseColor(dark ? "#19263B" : "#FFFFFF"); accent = Color.parseColor(dark ? "#6DDDC1" : "#007D69");
        ScrollView scroll = new ScrollView(this); scroll.setFillViewport(true); scroll.setBackgroundColor(Color.parseColor(dark ? "#101827" : "#EFF3F8"));
        content = new LinearLayout(this); content.setOrientation(LinearLayout.VERTICAL); content.setPadding(dp(20), dp(28), dp(20), dp(28)); scroll.addView(content);
        LinearLayout brand=new LinearLayout(this);brand.setGravity(Gravity.CENTER_VERTICAL);ImageView logo=new ImageView(this);logo.setImageResource(getResources().getIdentifier("tethermax","drawable",getPackageName()));brand.addView(logo,new LinearLayout.LayoutParams(dp(52),dp(52)));TextView title=text("  TetherMax",32,ink);title.setTypeface(null,Typeface.BOLD);brand.addView(title);content.addView(brand);
        content.addView(text("Güvenli USB internet · 1.0.0-rc.3", 14, muted));
        LinearLayout connection = card("BAĞLANTI"); status = text(TunnelService.status, 22, ink); connection.addView(status);
        homeTraffic=text("Windows istemcisi bekleniyor",16,muted);connection.addView(homeTraffic);
        action(connection, "Hücresel motoru başlat", () -> {
            try { PairingStore.get(this); startForegroundService(new Intent(this, TunnelService.class)); }
            catch (Exception e) { error("Güvenli eşleştirme deposu açılamadı. Motor başlatılmadı."); }
        });
        action(connection, "Motoru durdur", () -> stopService(new Intent(this, TunnelService.class)));
        LinearLayout pairing = card("EŞLEŞTİRME");
        pairing.addView(text("Anahtar cihazda korunur", 20, ink));
        pairing.addView(text("Windows uygulamasında Bağlan seçeneğini kullanın. İlk bağlantıda iki ekrandaki kısa numarayı karşılaştırın. Sonraki bağlantılarda güvenilen bilgisayar hatırlanır.", 14, muted));
        action(pairing, "Güvenilen bilgisayarları unut", () -> new AlertDialog.Builder(this).setTitle("Eşleştirmeler silinsin mi?").setMessage("Motor duracak; bilgisayarlara yeniden güven onayı gerekecek.").setPositiveButton("Unut", (d,w) -> { stopService(new Intent(this,TunnelService.class)); UsbPairing.forget(this); }).setNegativeButton("Vazgeç",null).show());
        LinearLayout analysis = card("İNTERNET ANALİZİ");
        traffic = text("Henüz trafik yok", 18, ink); analysis.addView(traffic);
        speedPlot=new TrafficPlot(this,"Mbps",muted);analysis.addView(speedPlot,new LinearLayout.LayoutParams(-1,dp(140)));
        analysis.addView(text("Mavi indirme · yeşil yükleme. Gerçek örnekler.",12,muted));
        delayPlot=new TrafficPlot(this,"ms",muted);analysis.addView(delayPlot,new LinearLayout.LayoutParams(-1,dp(140)));
        analysis.addView(text("Mavi DNS gidiş-dönüş · yeşil jitter. Ölçüm yoksa grafik boş kalır. Adaptif ölçüm Windows'tan açılır; mobil veri kullanır.",12,muted));
        Spinner history=new Spinner(this);history.setAdapter(new ArrayAdapter<String>(this,android.R.layout.simple_spinner_dropdown_item,new String[]{"1 dakika","5 dakika","30 dakika"}));analysis.addView(history);history.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> p){}public void onItemSelected(AdapterView<?> p,View v,int position,long id){int minutes=new int[]{1,5,30}[position];speedPlot.setMinutes(minutes);delayPlot.setMinutes(minutes);}});
        LinearLayout modes=card("TRAFİK PROFİLİ");
        for(int i=0;i<MODE_IDS.length;i++){final int selected=i;modeButtons[i]=action(modes,MODE_LABELS[i]+"\n"+MODE_DESCRIPTIONS[i],()->{TunnelService.requestMode(MainActivity.this,MODE_IDS[selected]);refreshTraffic(TunnelService.instance);});}
        modes.addView(text("Windows bağlandığında seçim aynı USB motorunun zamanlayıcısına iletilir. Manuel seçim otomatik oyun algılamasıyla değiştirilmez. Sistem geneli yönlendirme açılmaz.",14,muted));
        LinearLayout scope = card("AYARLAR");
        scope.addView(text("DNS filtresi · tüm paylaşılan normal DNS sorguları",18,ink));
        dns=new Spinner(this);dns.setAdapter(new ArrayAdapter<String>(this,android.R.layout.simple_spinner_dropdown_item,new String[]{"AdGuard DoH · reklam / izleyici filtresi","AdGuard Aile DoH","Cloudflare DoH","Özgün DNS"}));
        String savedDns=TunnelService.savedDns(this);for(int i=0;i<DNS_IDS.length;i++)if(DNS_IDS[i].equals(savedDns))dns.setSelection(i);scope.addView(dns);
        dns.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> p){}public void onItemSelected(AdapterView<?> p,View v,int i,long id){TunnelService.requestDns(MainActivity.this,DNS_IDS[i]);}});
        scope.addView(text("Şifreli DNS kullanan uygulamalar bu filtreden ayrı çalışabilir. YouTube reklamlarının tamamı DNS ile engellenmez.",13,muted));
        scope.addView(text("TCP / UDP aktarım motoru", 20, ink));
        scope.addView(text("Ölçümler yalnızca TetherMax aktarımına aittir. Operatör faturalandırması veya oyun performansı için garanti verilmez.", 14, muted));
        action(scope,"Koyu / açık tema",()->{getSharedPreferences("appearance",MODE_PRIVATE).edit().putBoolean("dark",!dark).apply();recreate();});
        LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);
        root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));
        LinearLayout nav=new LinearLayout(this);nav.setPadding(dp(8),dp(8),dp(8),dp(12));nav.setBackgroundColor(surface);
        String[] destinations={"◉\nAna Sayfa","◇\nModlar","↗\nAnaliz","▣\nCihazlar","⚙\nAyarlar"};int[] pageIds={0,3,2,1,4};
        for(int i=0;i<destinations.length;i++){
            final int selected=i,destination=pageIds[i];TextView item=text(destinations[i],12,i==0?accent:muted);item.setGravity(Gravity.CENTER);item.setContentDescription(destinations[i].replace('\n',' '));
            nav.addView(item,new LinearLayout.LayoutParams(0,dp(64),1));item.setOnClickListener(v->{
                for(int j=0;j<cards.size();j++)cards.get(j).setVisibility(j==destination?View.VISIBLE:View.GONE);
                for(int j=0;j<nav.getChildCount();j++)((TextView)nav.getChildAt(j)).setTextColor(j==selected?accent:muted);
                scroll.smoothScrollTo(0,0);
            });
        }
        root.setOnApplyWindowInsetsListener((v,insets)->{if(android.os.Build.VERSION.SDK_INT>=30){android.graphics.Insets safe=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());v.setPadding(safe.left,safe.top,safe.right,safe.bottom);}return insets;});
        root.addView(nav);for(int j=1;j<cards.size();j++)cards.get(j).setVisibility(View.GONE);
        setContentView(root);
    }
    private void error(String message) { new AlertDialog.Builder(this).setTitle("İşlem tamamlanamadı").setMessage(message).setPositiveButton("Tamam", null).show(); }
    @Override public void onResume() { super.onResume(); handler.post(refresh); }
    @Override public void onPause() { handler.removeCallbacks(refresh); super.onPause(); }
}
