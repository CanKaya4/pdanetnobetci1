package io.tethermax.app;

import android.app.Activity;
import android.content.*;
import android.os.*;
import android.widget.*;
import java.security.SecureRandom;

public final class MainActivity extends Activity {
    private final Handler handler = new Handler(Looper.getMainLooper());
    private TextView status;
    private final Runnable refresh = new Runnable() {
        public void run() { status.setText(TunnelService.status); handler.postDelayed(this, 1000); }
    };
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        android.content.SharedPreferences prefs = getSharedPreferences("pairing", MODE_PRIVATE);
        String code = prefs.getString("code", null);
        if (code == null) {
            byte[] bytes = new byte[16]; new SecureRandom().nextBytes(bytes);
            StringBuilder s = new StringBuilder(); for (byte b : bytes) s.append(String.format(java.util.Locale.ROOT, "%02x", b & 255));
            code = s.toString(); prefs.edit().putString("code", code).apply();
        }
        LinearLayout layout = new LinearLayout(this); layout.setOrientation(LinearLayout.VERTICAL); layout.setPadding(32, 32, 32, 32);
        TextView title = new TextView(this); title.setText("TetherMax — USB aktarım motoru\n\nBu deneysel sürüm TCP/UDP SOCKS aktarımı sağlar. Windows rota/DNS ayarlarını değiştirmez. Gerçek cihaz testi ayrıca onay gerektirir.\n\nEşleştirme kodu (yalnızca kendi bilgisayarınıza girin):\n" + code); title.setTextIsSelectable(true); layout.addView(title);
        status = new TextView(this); layout.addView(status);
        Button start = new Button(this); start.setText("Hücresel motoru başlat"); start.setOnClickListener(v -> startForegroundService(new Intent(this, TunnelService.class))); layout.addView(start);
        Button stop = new Button(this); stop.setText("Durdur"); stop.setOnClickListener(v -> stopService(new Intent(this, TunnelService.class))); layout.addView(stop);
        setContentView(layout);
    }
    @Override public void onResume() { super.onResume(); handler.post(refresh); }
    @Override public void onPause() { handler.removeCallbacks(refresh); super.onPause(); }
}
