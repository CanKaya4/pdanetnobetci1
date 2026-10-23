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
    private int ink, muted, surface, accent;
    private String shownCode;
    private TextView traffic;
    private AlertDialog pairingDialog;
    private TrafficPlot speedPlot,delayPlot;
    private Spinner profile;
    private static final String[] MODE_IDS={"auto","gaming","streaming","daily","maximum"};
    private static final String[] MODE_LABELS={"Otomatik","Oyun","Film / Dizi","Günlük","Maksimum Hız"};
    private TunnelService sampledService;
    private long sampleAt,lastUp,lastDown;
    private final Runnable refresh = new Runnable() {
        public void run() {
            status.setText(TunnelService.status);
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
                    .setOnCancelListener(d -> UsbPairing.confirm(code,false)).show();
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
            for(int i=0;i<MODE_IDS.length;i++)if(MODE_IDS[i].equals(relay.requestedMode())&&profile.getSelectedItemPosition()!=i)profile.setSelection(i);
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
        TextView label = text(title, 14, accent); label.setTypeface(null, Typeface.BOLD); box.addView(label); content.addView(box); return box;
    }
    private void action(LinearLayout box, String title, Runnable callback) {
        Button button = new Button(this); button.setText(title); button.setAllCaps(false);
        button.setTextColor(ink); button.setOnClickListener(v -> callback.run()); box.addView(button, new LinearLayout.LayoutParams(-1, dp(56)));
    }
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
        boolean dark = (getResources().getConfiguration().uiMode & Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES;
        ink = Color.parseColor(dark ? "#F1F5FC" : "#12213A"); muted = Color.parseColor(dark ? "#ADBBD0" : "#52637B");
        surface = Color.parseColor(dark ? "#19263B" : "#FFFFFF"); accent = Color.parseColor(dark ? "#6DDDC1" : "#007D69");
        ScrollView scroll = new ScrollView(this); scroll.setFillViewport(true); scroll.setBackgroundColor(Color.parseColor(dark ? "#101827" : "#EFF3F8"));
        content = new LinearLayout(this); content.setOrientation(LinearLayout.VERTICAL); content.setPadding(dp(20), dp(28), dp(20), dp(28)); scroll.addView(content);
        TextView title = text("TetherMax", 34, ink); title.setTypeface(null, Typeface.BOLD); content.addView(title);
        content.addView(text("USB aktarım motoru · Geliştirme önizlemesi", 14, muted));
        LinearLayout connection = card("BAĞLANTI"); status = text(TunnelService.status, 22, ink); connection.addView(status);
        connection.addView(text("Hücresel servis durumu, Windows bağlantısının veya internet aktarımının doğrulandığı anlamına gelmez.", 14, muted));
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
        analysis.addView(text("Mavi indirme · yeşil yükleme. Son 60 gerçek örnek.",12,muted));
        delayPlot=new TrafficPlot(this,"ms",muted);analysis.addView(delayPlot,new LinearLayout.LayoutParams(-1,dp(140)));
        analysis.addView(text("Mavi DNS gidiş-dönüş · yeşil jitter. Ölçüm yoksa grafik boş kalır. Adaptif ölçüm Windows'tan açılır; mobil veri kullanır.",12,muted));
        LinearLayout modes=card("TRAFİK PROFİLİ");
        profile=new Spinner(this);profile.setAdapter(new ArrayAdapter<String>(this,android.R.layout.simple_spinner_dropdown_item,MODE_LABELS));
        String savedMode=TunnelService.savedMode(this);for(int i=0;i<MODE_IDS.length;i++)if(MODE_IDS[i].equals(savedMode))profile.setSelection(i);
        modes.addView(profile);
        profile.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> parent){}public void onItemSelected(AdapterView<?> parent,View view,int position,long id){TunnelService.requestMode(MainActivity.this,MODE_IDS[position]);}});
        modes.addView(text("Windows bağlandığında seçim aynı USB motorunun zamanlayıcısına iletilir. Manuel seçim otomatik oyun algılamasıyla değiştirilmez. Sistem geneli yönlendirme açılmaz.",14,muted));
        LinearLayout scope = card("BU SÜRÜMÜN KAPSAMI");
        scope.addView(text("TCP / UDP aktarım motoru", 20, ink));
        scope.addView(text("Windows'tan TetherMax'e gönderilen TCP/UDP akışları taşınır. Deneysel akış önceliği ve hız tavanı vardır; oyun gecikmesi iyileşmesi henüz kanıtlanmadı. Sistem geneli rota ve MyPublicWiFi aktarımı ayrı doğrulama bekliyor.", 14, muted));
        setContentView(scroll);
    }
    private void error(String message) { new AlertDialog.Builder(this).setTitle("İşlem tamamlanamadı").setMessage(message).setPositiveButton("Tamam", null).show(); }
    @Override public void onResume() { super.onResume(); handler.post(refresh); }
    @Override public void onPause() { handler.removeCallbacks(refresh); super.onPause(); }
}
