package io.tethermax.engine;

import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicLong;

/** Versioned, authenticated TCP/UDP flow relay. No route, DNS or adapter configuration. */
public final class Relay implements AutoCloseable {
    public static final int MAX_DATAGRAM = 65507;
    private static final int MAX_FLOWS = 128;
    public interface Endpoint extends Closeable {
        InputStream input() throws IOException;
        OutputStream output() throws IOException;
        void shutdownOutput() throws IOException;
        void setReadTimeout(int millis) throws IOException;
    }
    /** Android implementation MUST resolve and open sockets on the selected cellular Network. */
    public interface Dialer {
        Socket tcp(String host, int port) throws IOException;
        DatagramFlow udp(String host, int port) throws IOException;
        default String networkInfo() { return "fixture"; }
        default void onMode(String mode) { }
    }
    /** Unconnected socket preserves zero-length datagrams on desktop Java as well as Android.
     * Incoming peer validation is performed by the relay before forwarding each reply. */
    public static final class DatagramFlow implements Closeable {
        public final DatagramSocket socket;
        public final InetSocketAddress peer;
        public DatagramFlow(DatagramSocket socket, InetSocketAddress peer) {
            this.socket = socket; this.peer = peer;
        }
        @Override public void close() { socket.close(); }
    }
    private final Dialer dialer;
    private final byte[] token;
    private final Semaphore slots = new Semaphore(MAX_FLOWS);
    private final Set<Closeable> live = ConcurrentHashMap.newKeySet();
    private final ExecutorService workers = Executors.newCachedThreadPool();
    private volatile boolean stopped;
    public final AtomicLong uploaded = new AtomicLong();
    public final AtomicLong downloaded = new AtomicLong();
    private volatile String requestedMode = "auto";
    private final AtomicLong modeRevision = new AtomicLong(1);
    public volatile String hostTelemetry;
    public volatile long hostTelemetryNanos;
    public String requestedMode() { return requestedMode; }
    public static boolean validMode(String mode) { return Arrays.asList("auto","gaming","streaming","daily","maximum").contains(mode); }
    public synchronized boolean setMode(String mode) {
        if (!validMode(mode)) return false;
        if (!mode.equals(requestedMode)) { requestedMode=mode;modeRevision.incrementAndGet();dialer.onMode(mode); }
        return true;
    }
    private static String quote(String value) {
        StringBuilder result=new StringBuilder("\"");
        for (char c:value.toCharArray()) { if(c=='"'||c=='\\')result.append('\\').append(c);else if(c<32)result.append(String.format(Locale.ROOT,"\\u%04x",(int)c));else result.append(c); }
        return result.append('"').toString();
    }
    public synchronized String controlStatus() {
        long age=hostTelemetryNanos==0?-1:TimeUnit.NANOSECONDS.toMillis(System.nanoTime()-hostTelemetryNanos);
        return "{\"mode\":"+quote(requestedMode)+",\"revision\":"+modeRevision.get()+",\"uploaded\":"+uploaded.get()+",\"downloaded\":"+downloaded.get()+",\"network\":"+quote(dialer.networkInfo())+",\"hostAgeMs\":"+age+"}";
    }
    private void control(Endpoint endpoint,DataInputStream in,DataOutputStream out) throws IOException {
        endpoint.setReadTimeout(15000);out.writeByte(0);out.flush();
        while(!stopped) {
            int command=in.readUnsignedByte();
            if(command==1) { byte[] status=controlStatus().getBytes(StandardCharsets.UTF_8);out.writeInt(status.length);out.write(status);out.flush(); }
            else if(command==2) {
                int size=in.readUnsignedByte();if(size<1||size>16)throw new IOException("invalid mode length");
                byte[] bytes=new byte[size];in.readFully(bytes);out.writeByte(setMode(new String(bytes,StandardCharsets.US_ASCII))?0:2);out.flush();
            } else if(command==3) {
                int size=in.readUnsignedShort();if(size<2||size>4096)throw new IOException("invalid telemetry length");
                byte[] bytes=new byte[size];in.readFully(bytes);
                if(bytes[0]!='{'||bytes[size-1]!='}')throw new IOException("invalid telemetry framing");
                hostTelemetry=new String(bytes,StandardCharsets.UTF_8);hostTelemetryNanos=System.nanoTime();out.writeByte(0);out.flush();
            } else throw new IOException("unknown control command");
        }
    }

    public Relay(Dialer dialer, String token) {
        if (!token.matches("[a-f0-9]{32}")) throw new IllegalArgumentException("32 hex character pairing code required");
        this.dialer = dialer;
        this.token = token.getBytes(StandardCharsets.US_ASCII);
    }
    public synchronized void accept(Endpoint endpoint) throws IOException {
        if (stopped || !slots.tryAcquire()) { endpoint.close(); return; }
        live.add(endpoint);
        workers.execute(() -> {
            try { handle(endpoint); } catch (IOException ignored) {
                // Per-flow failures close that flow; no global network mutation or retry loop.
            } finally { live.remove(endpoint); closeQuietly(endpoint); slots.release(); }
        });
    }
    private void handle(Endpoint endpoint) throws IOException {
        DataInputStream in = new DataInputStream(endpoint.input());
        DataOutputStream out = new DataOutputStream(endpoint.output());
        byte[] magic = new byte[4]; in.readFully(magic);
        if (!Arrays.equals(magic, new byte[]{'T','M','A','X'}) || in.readUnsignedByte() != 1) return;
        byte[] received = new byte[32]; in.readFully(received);
        if (!MessageDigest.isEqual(token, received)) { out.writeByte(2); out.flush(); return; }
        int mode = in.readUnsignedByte();
        int length = in.readUnsignedShort();
        if (length < 1 || length > 253 || (mode != 1 && mode != 2 && mode!=3)) { out.writeByte(3); out.flush(); return; }
        byte[] hostname = new byte[length]; in.readFully(hostname);
        String host = new String(hostname, StandardCharsets.US_ASCII);
        if (!host.matches("[A-Za-z0-9.:%_-]+")) { out.writeByte(3); out.flush(); return; }
        int port = in.readUnsignedShort();
        if (port == 0) { out.writeByte(3); out.flush(); return; }
        if(mode==3) { if(!host.equals("control")||port!=1){out.writeByte(3);out.flush();return;}control(endpoint,in,out);return; }
        Closeable remote;
        try { remote = mode == 1 ? dialer.tcp(host, port) : dialer.udp(host, port); }
        catch (IOException e) { out.writeByte(1); out.flush(); return; }
        synchronized (this) {
            if (stopped) { remote.close(); return; }
            live.add(remote);
        }
        try {
            out.writeByte(0); out.flush();
            endpoint.setReadTimeout(0);
            if (mode == 1) tcp(endpoint, (Socket)remote, in, out);
            else udp(endpoint, (DatagramFlow)remote, in, out);
        } finally { live.remove(remote); closeQuietly(remote); }
    }
    private void tcp(Endpoint endpoint, Socket remote, InputStream in, OutputStream out) throws IOException {
        Future<?> upload = workers.submit(() -> {
            try { copy(in, remote.getOutputStream(), uploaded); remote.shutdownOutput(); }
            catch (IOException e) { closeQuietly(remote); closeQuietly(endpoint); }
        });
        try { copy(remote.getInputStream(), out, downloaded); endpoint.shutdownOutput(); await(upload); }
        finally { closeQuietly(remote); closeQuietly(endpoint); upload.cancel(true); }
    }
    private void udp(Endpoint endpoint, DatagramFlow flow, DataInputStream in, DataOutputStream out) throws IOException {
        DatagramSocket remote = flow.socket;
        remote.setSoTimeout(5000);
        AtomicLong lastActivity = new AtomicLong(System.nanoTime());
        Future<?> upload = workers.submit(() -> {
            try {
                while (!stopped) {
                    int length = in.readUnsignedShort();
                    if (length > MAX_DATAGRAM) throw new IOException("oversized datagram");
                    byte[] packet = new byte[length]; in.readFully(packet);
                    remote.send(new DatagramPacket(packet, packet.length, flow.peer));
                    uploaded.addAndGet(packet.length);
                    lastActivity.set(System.nanoTime());
                }
            } catch (IOException e) { closeQuietly(remote); }
        });
        try {
            byte[] bytes = new byte[MAX_DATAGRAM];
            while (!stopped) {
                DatagramPacket packet = new DatagramPacket(bytes, bytes.length);
                try { remote.receive(packet); }
                catch (SocketTimeoutException e) {
                    if (System.nanoTime() - lastActivity.get() > TimeUnit.SECONDS.toNanos(60)) throw e;
                    continue;
                }
                if (!flow.peer.equals(packet.getSocketAddress())) continue;
                lastActivity.set(System.nanoTime());
                out.writeShort(packet.getLength()); out.write(packet.getData(), packet.getOffset(), packet.getLength()); out.flush();
                downloaded.addAndGet(packet.getLength());
            }
        } finally { closeQuietly(remote); closeQuietly(endpoint); upload.cancel(true); }
    }
    private static void await(Future<?> f) throws IOException {
        try { f.get(); }
        catch (InterruptedException e) { Thread.currentThread().interrupt(); throw new IOException(e); }
        catch (ExecutionException e) { throw new IOException(e.getCause()); }
    }
    private static void copy(InputStream in, OutputStream out, AtomicLong counter) throws IOException {
        byte[] b = new byte[16384]; int n;
        while ((n = in.read(b)) != -1) { out.write(b, 0, n); out.flush(); counter.addAndGet(n); }
    }
    private static void closeQuietly(Closeable c) { try { c.close(); } catch (IOException ignored) {} }
    @Override public synchronized void close() {
        stopped = true;
        for (Closeable c : live) closeQuietly(c);
        workers.shutdownNow();
    }
}
