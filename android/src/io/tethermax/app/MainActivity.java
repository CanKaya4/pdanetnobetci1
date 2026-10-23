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

public final class MainActivity extends Activity {
    private final Handler handler = new Handler(Looper.getMainLooper());
    private TextView status;
    private LinearLayout content;
    private int ink, muted, surface, accent;
    private final Runnable refresh = new Runnable() {
        public void run() { status.setText(TunnelService.status); handler.postDelayed(this, 1000); }
    };
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
        pairing.addView(text("Bu önizleme eski protokolü kullanır. Kısa numarayla karşılıklı onay henüz hazır değil. Gizli kod yalnızca isteğinizle gösterilir.", 14, muted));
        action(pairing, "Mevcut eşleştirme kodunu göster", () -> {
            try {
                TextView secret = text(PairingStore.get(this), 18, ink); secret.setPadding(dp(24), dp(20), dp(24), dp(20)); secret.setTextIsSelectable(true);
                new AlertDialog.Builder(this).setTitle("Gizli eşleştirme kodu").setView(secret).setPositiveButton("Kapat", (d,w) -> secret.setText("")).setOnDismissListener(d -> secret.setText("")).show();
            } catch (Exception e) { error("Eşleştirme kaydı açılamadı. Güvenlik nedeniyle yeni anahtar otomatik üretilmedi."); }
        });
        LinearLayout analysis = card("İNTERNET ANALİZİ");
        analysis.addView(text("Henüz ölçüm yok", 22, ink));
        analysis.addView(text("Hız, RTT, paket kaybı ve USB aktarım durumu ölçülmedi. Bu ekran mobil veri tüketen bir test başlatmaz.", 14, muted));
        LinearLayout scope = card("BU SÜRÜMÜN KAPSAMI");
        scope.addView(text("TCP / UDP aktarım motoru", 20, ink));
        scope.addView(text("Windows SOCKS istemcisi gerekir. Sistem geneli internet, oyun/film profilleri ve adaptif trafik yönetimi henüz uygulanmadı.", 14, muted));
        setContentView(scroll);
    }
    private void error(String message) { new AlertDialog.Builder(this).setTitle("İşlem tamamlanamadı").setMessage(message).setPositiveButton("Tamam", null).show(); }
    @Override public void onResume() { super.onResume(); handler.post(refresh); }
    @Override public void onPause() { handler.removeCallbacks(refresh); super.onPause(); }
}
