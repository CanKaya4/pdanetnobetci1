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
        if (length < 1 || length > 253 || (mode != 1 && mode != 2)) { out.writeByte(3); out.flush(); return; }
        byte[] hostname = new byte[length]; in.readFully(hostname);
        String host = new String(hostname, StandardCharsets.US_ASCII);
        if (!host.matches("[A-Za-z0-9.:%_-]+")) { out.writeByte(3); out.flush(); return; }
        int port = in.readUnsignedShort();
        if (port == 0) { out.writeByte(3); out.flush(); return; }
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
            try { copy(in, remote.getOutputStream()); remote.shutdownOutput(); }
            catch (IOException e) { closeQuietly(remote); closeQuietly(endpoint); }
        });
        try { copy(remote.getInputStream(), out); endpoint.shutdownOutput(); await(upload); }
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
            }
        } finally { closeQuietly(remote); closeQuietly(endpoint); upload.cancel(true); }
    }
    private static void await(Future<?> f) throws IOException {
        try { f.get(); }
        catch (InterruptedException e) { Thread.currentThread().interrupt(); throw new IOException(e); }
        catch (ExecutionException e) { throw new IOException(e.getCause()); }
    }
    private static void copy(InputStream in, OutputStream out) throws IOException {
        byte[] b = new byte[16384]; int n;
        while ((n = in.read(b)) != -1) { out.write(b, 0, n); out.flush(); }
    }
    private static void closeQuietly(Closeable c) { try { c.close(); } catch (IOException ignored) {} }
    @Override public synchronized void close() {
        stopped = true;
        for (Closeable c : live) closeQuietly(c);
        workers.shutdownNow();
    }
}
