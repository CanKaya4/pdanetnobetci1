package io.tethermax.app;

import android.app.*;
import android.content.*;
import android.net.*;
import android.os.*;
import io.tethermax.engine.Relay;
import io.tethermax.engine.DnsOverHttps;
import javax.net.ssl.HttpsURLConnection;
import java.io.*;
import java.net.*;
import java.util.concurrent.*;

public final class TunnelService extends Service {
    private final ServiceState lifecycle = new ServiceState();
    public boolean running() { return lifecycle.get()==ServiceState.State.RUNNING; }
    private String stage = "foreground";
    private synchronized void available(Network network) {
        if (stopping || !lifecycle.acceptsNetwork() || instance != this) return;
        boolean changed=!network.equals(cellular);
        cellular = network;
        if(changed)powerHandler.post(()->{if(!stopping&&health!=null)health.registerSignal();});
        if (running()) status = "Hücresel bağlantı hazır; USB istemcisi bekleniyor";
        LinkProperties properties=manager.getLinkProperties(network);
        networkInfo="CELLULAR · Network "+network+" · "+(properties==null?"Arayüz bilgisi yok":properties.getInterfaceName());
    }
    private synchronized void fail(String step, Throwable error) {
        if (stopping) return;
        if(!lifecycle.fail())return;
        StringBuilder detail = new StringBuilder();
        // Socket/OS failures are actionable; pairing storage messages can contain secrets.
        for (Throwable cause=error;cause!=null;cause=cause.getCause()) {
            if(detail.length()>0)detail.append(" → ");
            detail.append(cause.getClass().getSimpleName());
            if(!step.equals("pairing storage")) detail.append(": ").append(cause.getMessage());
        }
        status="Motor başlatılamadı ["+step+"]: "+detail;
        releaseResources(); stopSelf();
    }
    public static volatile String status = "Durduruldu";
    public static volatile TunnelService instance;
    public static volatile long startedAt;
    public static volatile String networkInfo = "Hücresel ağ henüz seçilmedi";
    public long uploaded() { Relay r=relay; return r==null?0:r.uploaded.get(); }
    public long downloaded() { Relay r=relay; return r==null?0:r.downloaded.get(); }
    public Relay currentRelay() { return relay; }
    public static String savedMode(Context context) { return context.getSharedPreferences("traffic",MODE_PRIVATE).getString("mode","auto"); }
    public static void requestMode(Context context,String mode) {
        if(!Relay.validMode(mode))return;
        context.getSharedPreferences("traffic",MODE_PRIVATE).edit().putString("mode",mode).apply();
        TunnelService current=instance;if(current!=null&&current.relay!=null)current.relay.setMode(mode);
    }
    public static String savedDns(Context context){return context.getSharedPreferences("traffic",MODE_PRIVATE).getString("dns","adguard");}
    public static void requestDns(Context context,String profile){if(!Relay.validDnsProfile(profile))return;context.getSharedPreferences("traffic",MODE_PRIVATE).edit().putString("dns",profile).apply();TunnelService current=instance;if(current!=null&&current.relay!=null)current.relay.setDnsProfile(profile);}
    private final ExecutorService acceptor = Executors.newSingleThreadExecutor();
    private volatile Network cellular;
    private volatile Relay relay;
    private final DnsOverHttps encryptedDns=new DnsOverHttps();
    private volatile LocalServerSocket listener;
    private UsbPairing pairing;
    private volatile boolean stopping;
    private PowerManager.WakeLock transferWake;
    private volatile ModemHealth health;
    ModemHealth health(){return health;}
    static boolean reducedWork(){TunnelService s=instance;ModemHealth h=s==null?null:s.health;return h!=null&&h.reduced;}
    void schedulePowerCheck(){powerHandler.removeCallbacks(maintainPower);if(!stopping)powerHandler.post(maintainPower);}
    private long wakeRenewAt;
    private volatile long usbAcceptedAt;
    volatile String usbRecovery="Yeni USB bağlantılarına hazır";
    private final java.util.concurrent.atomic.AtomicBoolean powerCheckQueued=new java.util.concurrent.atomic.AtomicBoolean();
    private final Runnable powerNudge=()->{powerCheckQueued.set(false);schedulePowerCheck();};
    private final Handler powerHandler=new Handler(Looper.getMainLooper());
    private final Runnable maintainPower=new Runnable(){public void run(){
        if(stopping)return;
        Relay current=relay;
        boolean connected=current!=null&&HostLease.fresh(System.nanoTime(),current.hostTelemetryNanos,stopping);
        if(connected){if(transferWake==null){transferWake=getSystemService(PowerManager.class).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK,"TetherMax:UsbTransfer");transferWake.setReferenceCounted(false);}long now=SystemClock.elapsedRealtime();if(!transferWake.isHeld()||now>=wakeRenewAt){transferWake.acquire(20000L);wakeRenewAt=now+10000;}}
        else if(transferWake!=null&&transferWake.isHeld())transferWake.release();
        ModemHealth currentHealth=health;if(currentHealth!=null)currentHealth.heartbeat(connected);
        boolean recentAccept=SystemClock.elapsedRealtime()-usbAcceptedAt<30000;
        powerHandler.postDelayed(this,GuardianPolicy.maintenanceDelay(connected||recentAccept,getSystemService(PowerManager.class).isInteractive()));
    }};
    public Network measurementNetwork(){return cellular;}
    private ConnectivityManager manager;
    private ConnectivityManager.NetworkCallback callback;
    @Override public synchronized void onCreate() {
        super.onCreate();
        instance = this; lifecycle.start(); status="Motor başlatılıyor"; startedAt=android.os.SystemClock.elapsedRealtime();
        try {
        NotificationManager notifications = getSystemService(NotificationManager.class);
        notifications.createNotificationChannel(new NotificationChannel("engine", "USB aktarım", NotificationManager.IMPORTANCE_LOW));
        startForeground(1, new Notification.Builder(this, "engine").setContentTitle("TetherMax USB motoru").setContentText("Hücresel bağlantı bekleniyor").setSmallIcon(getResources().getIdentifier("tethermax_notification", "drawable", getPackageName())).build());
        // Optional telemetry failure must never prevent the transfer engine starting.
        health=new ModemHealth(this);
        try { health.start(); } catch(RuntimeException unavailable) { health.close();health=null; }
        manager = getSystemService(ConnectivityManager.class);
        callback = new ConnectivityManager.NetworkCallback() {
            @Override public void onAvailable(Network network) {
                available(network);
            }
            @Override public void onLost(Network network) {
                synchronized(TunnelService.this) { if (!stopping && lifecycle.acceptsNetwork() && network.equals(cellular)) { cellular = null; status = "Hücresel ağ kesildi; yeniden bağlantı bekleniyor"; networkInfo="Hücresel ağ yok"; } }
            }
            @Override public void onUnavailable() { fail("cellular request",new IOException("Cellular unavailable")); }
        };
            stage="cellular request";
            manager.requestNetwork(new NetworkRequest.Builder().addTransportType(NetworkCapabilities.TRANSPORT_CELLULAR).addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET).build(), callback);
            String code;
            stage="pairing storage";
            try { code = PairingStore.get(this); }
            catch (Exception e) { throw new IOException("Pairing storage unavailable", e); }
            stage="relay";
            relay = new Relay(new Relay.Dialer() {
                private Network network() throws IOException { Network n = cellular; NetworkCapabilities caps=n==null?null:manager.getNetworkCapabilities(n); if (n == null||caps==null||!caps.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR)) throw new IOException("cellular unavailable"); return n; }
                public String networkInfo() {
                    try { Network n=network();LinkProperties properties=manager.getLinkProperties(n);return "CELLULAR Network "+n.toString()+" / "+(properties==null?"unknown":properties.getInterfaceName()); }
                    catch(IOException e){return "UNAVAILABLE";}
                }
                public void onMode(String mode) { getSharedPreferences("traffic",MODE_PRIVATE).edit().putString("mode",mode).apply(); }
                public boolean encryptedDns(){return true;}
                public byte[] dns(String profile,byte[] query)throws IOException {
                    Network selected=network();
                    return encryptedDns.resolve(profile,Long.toString(selected.getNetworkHandle()),query,(endpoint,body)->{
                        URLConnection connection=selected.openConnection(new URL(endpoint));if(!(connection instanceof HttpsURLConnection))throw new IOException("HTTPS required");
                        HttpsURLConnection http=(HttpsURLConnection)connection;
                        // Platform trust store and hostname verification remain enabled.
                        http.setConnectTimeout(4000);http.setReadTimeout(4000);http.setInstanceFollowRedirects(false);http.setRequestMethod("POST");http.setRequestProperty("Content-Type","application/dns-message");http.setRequestProperty("Accept","application/dns-message");http.setDoOutput(true);http.setFixedLengthStreamingMode(body.length);
                        try{try(OutputStream output=http.getOutputStream()){output.write(body);}if(http.getResponseCode()!=200)throw new IOException("DNS-over-HTTPS HTTP status "+http.getResponseCode());String type=http.getContentType();if(type==null||!type.toLowerCase(java.util.Locale.ROOT).startsWith("application/dns-message"))throw new IOException("DNS-over-HTTPS content type");try(InputStream input=http.getInputStream();ByteArrayOutputStream response=new ByteArrayOutputStream()){byte[] buffer=new byte[4096];int count;while((count=input.read(buffer))!=-1){if(response.size()+count>65535)throw new IOException("DNS-over-HTTPS response too large");response.write(buffer,0,count);}return response.toByteArray();}}finally{http.disconnect();}
                    });
                }
                public void onDnsProfile(String profile){getSharedPreferences("traffic",MODE_PRIVATE).edit().putString("dns",profile).apply();}
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
            relay.setMode(savedMode(this));relay.setDnsProfile(savedDns(this));
            stage="USB listener";
            listener = new LocalServerSocket("io.tethermax.usb.v1");
            stage="pairing listener";
            pairing = new UsbPairing(this);
            schedulePowerCheck();
            stage="accept worker";
            final LocalServerSocket ownedListener=listener;
            final Relay ownedRelay=relay;
            acceptor.execute(() -> {
                LocalServerSocket activeListener=ownedListener;
                int failures=0;
                while (!stopping) {
                    LocalSocket accepted;
                    try {
                        if(activeListener==null) {
                            synchronized(TunnelService.this) {
                                if(stopping)return;
                                activeListener=new LocalServerSocket("io.tethermax.usb.v1");listener=activeListener;usbRecovery="USB dinleyicisi yeniden hazır";
                            }
                        }
                        accepted=activeListener.accept();failures=0;
                    } catch(IOException | RuntimeException error) {
                        if(stopping)return;
                        if(activeListener!=null)try{activeListener.close();}catch(IOException ignored){}
                        activeListener=null;
                        long delay=GuardianPolicy.retryDelay(++failures);
                        if(delay<0){usbRecovery="Yeni USB bağlantıları alınamıyor. Mevcut akışlar korunuyor; aktarım bitince motoru elle yeniden başlatın.";status=usbRecovery;return;}
                        usbRecovery="USB dinleyicisi kurtarılıyor · deneme "+failures+"/6; mevcut akışlar korunuyor";status=usbRecovery;
                        try{Thread.sleep(delay);}catch(InterruptedException stopped){Thread.currentThread().interrupt();return;}
                        continue;
                    }
                    final LocalSocket socket=accepted;
                    try {
                        usbAcceptedAt=SystemClock.elapsedRealtime();
                        if(powerCheckQueued.compareAndSet(false,true))powerHandler.postDelayed(powerNudge,1000);
                        try { socket.setSoTimeout(15000); }
                        catch(IOException e) { socket.close(); continue; }
                        ownedRelay.accept(new Relay.Endpoint() {
                            public InputStream input() throws IOException { return socket.getInputStream(); }
                            public OutputStream output() throws IOException { return socket.getOutputStream(); }
                            public void setReadTimeout(int millis) throws IOException { socket.setSoTimeout(millis); }
                            public void shutdownOutput() throws IOException { socket.shutdownOutput(); }
                            public void close() throws IOException { socket.close(); }
                        });
                    } catch (IOException | RuntimeException e) {
                        // One rejected/failed client must not stop other streams or the listener.
                        try{socket.close();}catch(IOException ignored){}
                    }
                }
            });
            synchronized(this) { if(!stopping && lifecycle.ready()) {status=cellular==null?"Hücresel bağlantı bekleniyor":"Hücresel bağlantı hazır; USB istemcisi bekleniyor";} }
        } catch (IOException | RuntimeException e) { fail(stage,e); }
    }
    // A user-started foreground session may be recreated after an OS memory kill.
    // stopService/stopSelf remain explicit stops; Android force-stop is respected.
    @Override public int onStartCommand(Intent intent, int flags, int startId) { return lifecycle.acceptsNetwork() ? START_STICKY : START_NOT_STICKY; }
    private synchronized void releaseResources() {
        stopping = true;
        powerHandler.removeCallbacksAndMessages(null);
        if(transferWake!=null&&transferWake.isHeld())transferWake.release();
        if(health!=null){health.close();health=null;}
        if (pairing != null) pairing.close();
        pairing=null;
        if (listener != null) try { listener.close(); } catch (IOException ignored) {}
        listener=null;
        if (relay != null) relay.close(); acceptor.shutdownNow();
        relay=null;
        if (manager != null && callback != null) try { manager.unregisterNetworkCallback(callback); } catch (IllegalArgumentException ignored) {}
        cellular = null;
        networkInfo="TetherMax motoru kapalı · Sistem USB tethering bağımsızdır";
    }
    @Override public synchronized void onDestroy() {
        boolean failed=lifecycle.get()==ServiceState.State.ERROR;
        lifecycle.stopping();
        releaseResources();
        if(instance==this)instance=null;
        lifecycle.stopped();
        if(!failed){status="Durduruldu · Sistem USB tethering/RNDIS bağımsızdır";}
        super.onDestroy();
    }
    @Override public IBinder onBind(Intent intent) { return null; }
}

