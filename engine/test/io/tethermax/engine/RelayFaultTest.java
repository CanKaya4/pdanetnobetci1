package io.tethermax.engine;
import java.io.*;
import java.net.*;
import java.util.concurrent.*;

/** Fault injection, no Android or external network dependency. */
public final class RelayFaultTest {
    private static final String TOKEN="0123456789abcdef0123456789abcdef";
    private static void check(boolean value){if(!value)throw new AssertionError();}
    private static final class Endpoint implements Relay.Endpoint {
        final CountDownLatch closed=new CountDownLatch(1);
        final ByteArrayOutputStream output=new ByteArrayOutputStream();
        final InputStream input;
        Endpoint(InputStream input){this.input=input;}
        public InputStream input(){return input;}
        public OutputStream output(){return output;}
        public void setReadTimeout(int timeout){}
        public void shutdownOutput(){}
        public void close(){closed.countDown();}
    }
    private static byte[] control() throws IOException {
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();DataOutputStream out=new DataOutputStream(bytes);
        out.writeBytes("TMAX");out.writeByte(1);out.writeBytes(TOKEN);out.writeByte(3);out.writeShort(7);out.writeBytes("control");out.writeShort(1);out.writeByte(1);return bytes.toByteArray();
    }
    public static void main(String[] args)throws Exception {
        Relay relay=new Relay(new Relay.Dialer(){public Socket tcp(String h,int p)throws IOException{throw new IOException();}public Relay.DatagramFlow udp(String h,int p)throws IOException{throw new IOException();}},TOKEN);
        try {
            for(int i=0;i<200;i++) {
                final boolean runtime=i%2==0;
                Endpoint broken=new Endpoint(new InputStream(){public int read()throws IOException{if(runtime)throw new IllegalStateException("fixture");throw new IOException("fixture");}});
                relay.accept(broken);check(broken.closed.await(3,TimeUnit.SECONDS));
            }
            Endpoint healthy=new Endpoint(new ByteArrayInputStream(control()));relay.accept(healthy);check(healthy.closed.await(3,TimeUnit.SECONDS));
            check(healthy.output.size()>5&&healthy.output.toByteArray()[0]==0);
            check(relay.runtimeFailures.get()==100&&relay.flowFailures.get()==200);
            check(!relay.lastFailureKind.contains("fixture"));
            System.out.println("PASS 200 injected IO/runtime flow failures: no slot leak, authenticated control survives, diagnostic contains only class");
        }finally{relay.close();}
        Endpoint late=new Endpoint(new ByteArrayInputStream(control()));relay.accept(late);check(late.closed.await(1,TimeUnit.SECONDS));
        System.out.println("PASS late endpoint after shutdown closes without restarting relay");
    }
}
