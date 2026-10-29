package io.tethermax.engine;

import java.io.*;
import java.net.*;
import java.util.Arrays;
import java.util.concurrent.*;

/** Real loopback TCP/UDP isolation under an intentionally tiny synthetic cap. */
public final class RelayGamingTest {
    private static final String TOKEN="0123456789abcdef0123456789abcdef";
    private static void check(boolean value,String label){if(!value)throw new AssertionError(label);}
    private static Socket open(ServerSocket local,int mode,int port)throws IOException {
        Socket s=new Socket(InetAddress.getLoopbackAddress(),local.getLocalPort());s.setSoTimeout(5000);s.setTcpNoDelay(true);
        DataOutputStream out=new DataOutputStream(s.getOutputStream());out.writeBytes("TMAX");out.writeByte(1);out.writeBytes(TOKEN);out.writeByte(mode);out.writeShort(9);out.writeBytes("127.0.0.1");out.writeShort(port);out.flush();
        if(s.getInputStream().read()!=0){s.close();throw new IOException("fixture handshake failed");}return s;
    }
    private static Thread daemon(Runnable action){Thread t=new Thread(action);t.setDaemon(true);t.start();return t;}
    public static void main(String[] args)throws Exception {
        CountDownLatch bulkStarted=new CountDownLatch(1);
        InetAddress loop=InetAddress.getLoopbackAddress();
        try(ServerSocket echo=new ServerSocket(0,10,loop);DatagramSocket udp=new DatagramSocket(new InetSocketAddress(loop,0));ServerSocket local=new ServerSocket(0,10,loop)) {
            daemon(()->{try(Socket peer=echo.accept()){byte[] buffer=new byte[4096];int n;while((n=peer.getInputStream().read(buffer))!=-1){bulkStarted.countDown();peer.getOutputStream().write(buffer,0,n);peer.getOutputStream().flush();}peer.shutdownOutput();}catch(IOException ignored){}});
            daemon(()->{try{byte[] buffer=new byte[2048];DatagramPacket p=new DatagramPacket(buffer,buffer.length);while(!udp.isClosed()){p.setLength(buffer.length);udp.receive(p);udp.send(p);}}catch(IOException ignored){}});
            try(Relay relay=new Relay(new Relay.Dialer(){
                public Socket tcp(String host,int port)throws IOException{if(!"127.0.0.1".equals(host)||port!=echo.getLocalPort())throw new IOException("loopback fixture only");Socket s=new Socket(loop,port);s.setTcpNoDelay(true);return s;}
                public Relay.DatagramFlow udp(String host,int port)throws IOException{if(!"127.0.0.1".equals(host)||port!=udp.getLocalPort())throw new IOException("loopback fixture only");return new Relay.DatagramFlow(new DatagramSocket(new InetSocketAddress(loop,0)),new InetSocketAddress(loop,port));}
            },TOKEN)) {
                daemon(()->{try{while(!local.isClosed()){final Socket s=local.accept();s.setTcpNoDelay(true);relay.accept(new Relay.Endpoint(){public InputStream input()throws IOException{return s.getInputStream();}public OutputStream output()throws IOException{return s.getOutputStream();}public void shutdownOutput()throws IOException{s.shutdownOutput();}public void setReadTimeout(int millis)throws IOException{s.setSoTimeout(millis);}public void close()throws IOException{s.close();}});}}catch(IOException ignored){}});
                relay.updateGamingSession(true,.1);
                try(Socket bulk=open(local,1,echo.getLocalPort());Socket latency=open(local,2,udp.getLocalPort())) {
                    byte[] payload=new byte[32768];new java.util.Random(42).nextBytes(payload);
                    CompletableFuture<byte[]> received=new CompletableFuture<>();
                    daemon(()->{try{bulk.getOutputStream().write(payload);bulk.shutdownOutput();byte[] reply=new byte[payload.length];new DataInputStream(bulk.getInputStream()).readFully(reply);received.complete(reply);}catch(Exception error){received.completeExceptionally(error);}});
                    check(bulkStarted.await(2,TimeUnit.SECONDS),"bulk did not start");check(!received.isDone(),"synthetic cap did not backpressure bulk");
                    latency.setSoTimeout(1500);DataOutputStream out=new DataOutputStream(latency.getOutputStream());DataInputStream in=new DataInputStream(latency.getInputStream());
                    for(int length:new int[]{0,32,1200}){byte[] packet=new byte[length];Arrays.fill(packet,(byte)7);out.writeShort(length);out.write(packet);out.flush();check(in.readUnsignedShort()==length,"UDP frame boundary");byte[] reply=new byte[length];in.readFully(reply);check(Arrays.equals(packet,reply),"UDP contents");}
                    check(!received.isDone(),"UDP waited behind the entire bulk transfer");
                    check(Arrays.equals(payload,received.get(6,TimeUnit.SECONDS)),"TCP byte integrity");
                    check(relay.gamingStatus().contains("bekletilen yazma"),"pacing telemetry missing");
                    relay.clearGamingSession();check(!relay.gamingActive(),"lease clear");
                }
            }
        }
        System.out.println("PASS real loopback: paced bulk TCP integrity, concurrent 0/32/1200-byte UDP isolation, live lease release; synthetic cap only");
    }
}
