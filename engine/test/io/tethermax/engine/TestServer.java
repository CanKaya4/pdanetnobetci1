package io.tethermax.engine;

import java.io.*;
import java.net.*;

/** Cloud integration fixture, loopback-only. Never used in the Android APK. */
public final class TestServer {
    public static void main(String[] args) throws Exception {
        String token = System.getenv("TETHERMAX_TEST_TOKEN");
        Relay.Dialer dialer = new Relay.Dialer() {
            private InetAddress address(String host) throws IOException {
                InetAddress address = InetAddress.getByName(host);
                if (!address.isLoopbackAddress()) throw new IOException("fixture restricts destinations to loopback");
                return address;
            }
            public Socket tcp(String host, int port) throws IOException {
                Socket s = new Socket(); s.connect(new InetSocketAddress(address(host), port), 5000); s.setTcpNoDelay(true); return s;
            }
            public Relay.DatagramFlow udp(String host, int port) throws IOException {
                InetSocketAddress peer = new InetSocketAddress(address(host), port);
                return new Relay.DatagramFlow(new DatagramSocket(), peer);
            }
        };
        try (ServerSocket server = new ServerSocket(0, 50, InetAddress.getLoopbackAddress()); Relay relay = new Relay(dialer, token)) {
            System.out.println(server.getLocalPort()); System.out.flush();
            while (true) {
                Socket s = server.accept(); s.setSoTimeout(15000); s.setTcpNoDelay(true);
                relay.accept(new Relay.Endpoint() {
                    public InputStream input() throws IOException { return s.getInputStream(); }
                    public OutputStream output() throws IOException { return s.getOutputStream(); }
                    public void setReadTimeout(int millis) throws IOException { s.setSoTimeout(millis); }
                            public void shutdownOutput() throws IOException { s.shutdownOutput(); }
                    public void close() throws IOException { s.close(); }
                });
            }
        }
    }
}
