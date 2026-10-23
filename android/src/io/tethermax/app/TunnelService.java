package io.tethermax.app;

import android.app.*;
import android.content.*;
import android.net.*;
import android.os.*;
import io.tethermax.engine.Relay;
import java.io.*;
import java.net.*;
import java.util.concurrent.*;

public final class TunnelService extends Service {
    public static volatile String status = "Durduruldu";
    private final ExecutorService acceptor = Executors.newSingleThreadExecutor();
    private volatile Network cellular;
    private volatile Relay relay;
    private volatile LocalServerSocket listener;
    private volatile boolean stopping;
    private ConnectivityManager manager;
    private ConnectivityManager.NetworkCallback callback;
    @Override public void onCreate() {
        super.onCreate();
        NotificationManager notifications = getSystemService(NotificationManager.class);
        notifications.createNotificationChannel(new NotificationChannel("engine", "USB aktarım", NotificationManager.IMPORTANCE_LOW));
        startForeground(1, new Notification.Builder(this, "engine").setContentTitle("TetherMax USB motoru").setContentText("Hücresel bağlantı bekleniyor").setSmallIcon(android.R.drawable.stat_sys_upload_done).build());
        manager = getSystemService(ConnectivityManager.class);
        callback = new ConnectivityManager.NetworkCallback() {
            @Override public void onAvailable(Network network) { cellular = network; status = "Hücresel bağlantı hazır; USB istemcisi bekleniyor"; }
            @Override public void onLost(Network network) {
                if (network.equals(cellular)) { cellular = null; status = "Hücresel bağlantı kesildi; yeniden başlatın"; stopSelf(); }
            }
            @Override public void onUnavailable() { status = "Hücresel bağlantı kullanılamıyor"; stopSelf(); }
        };
        try {
            manager.requestNetwork(new NetworkRequest.Builder().addTransportType(NetworkCapabilities.TRANSPORT_CELLULAR).addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET).build(), callback);
            String code = getSharedPreferences("pairing", MODE_PRIVATE).getString("code", "");
            relay = new Relay(new Relay.Dialer() {
                private Network network() throws IOException { Network n = cellular; if (n == null) throw new IOException("cellular unavailable"); return n; }
                public Socket tcp(String host, int port) throws IOException {
                    Network n = network(); InetAddress[] addresses = n.getAllByName(host); IOException last = null;
                    for (InetAddress address : addresses) {
                        Socket s = n.getSocketFactory().createSocket();
                        try { s.connect(new InetSocketAddress(address, port), 10000); s.setTcpNoDelay(true); return s; }
                        catch (IOException e) { s.close(); last = e; }
                    }
                    throw last == null ? new IOException("no addresses") : last;
                }
                public Relay.DatagramFlow udp(String host, int port) throws IOException {
                    Network n = network(); InetAddress address = n.getAllByName(host)[0];
                    DatagramSocket s = new DatagramSocket(null);
                    try { n.bindSocket(s); s.bind(new InetSocketAddress(0)); return new Relay.DatagramFlow(s, new InetSocketAddress(address, port)); }
                    catch (IOException | RuntimeException e) { s.close(); throw e; }
                }
            }, code);
            listener = new LocalServerSocket("io.tethermax.usb.v1");
            acceptor.execute(() -> {
                while (!stopping) {
                    try {
                        LocalSocket socket = listener.accept(); socket.setSoTimeout(15000);
                        relay.accept(new Relay.Endpoint() {
                            public InputStream input() throws IOException { return socket.getInputStream(); }
                            public OutputStream output() throws IOException { return socket.getOutputStream(); }
                            public void setReadTimeout(int millis) throws IOException { socket.setSoTimeout(millis); }
                            public void shutdownOutput() throws IOException { socket.shutdownOutput(); }
                            public void close() throws IOException { socket.close(); }
                        });
                    } catch (IOException e) { if (!stopping) { status = "USB dinleyicisi durdu"; stopSelf(); } break; }
                }
            });
            status = "Hücresel bağlantı bekleniyor";
        } catch (IOException | RuntimeException e) { status = "Motor başlatılamadı: " + e.getClass().getSimpleName(); stopSelf(); }
    }
    @Override public int onStartCommand(Intent intent, int flags, int startId) { return START_NOT_STICKY; }
    @Override public void onDestroy() {
        stopping = true;
        if (listener != null) try { listener.close(); } catch (IOException ignored) {}
        if (relay != null) relay.close(); acceptor.shutdownNow();
        if (manager != null && callback != null) try { manager.unregisterNetworkCallback(callback); } catch (IllegalArgumentException ignored) {}
        cellular = null;
        if (!status.contains("kesildi") && !status.contains("başlatılamadı") && !status.contains("kullanılamıyor")) status = "Durduruldu";
        super.onDestroy();
    }
    @Override public IBinder onBind(Intent intent) { return null; }
}
