package io.tethermax.engine;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.*;
public final class RelayDnsTest {
 static final String TOKEN="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
 static void require(boolean yes,String message){if(!yes)throw new AssertionError(message);}
 static Socket flow(Relay relay,int port) throws Exception {
  try(ServerSocket accept=new ServerSocket(0,4,InetAddress.getLoopbackAddress())){
   Socket client=new Socket(InetAddress.getLoopbackAddress(),accept.getLocalPort());Socket server=accept.accept();server.setSoTimeout(2000);client.setSoTimeout(2000);
   relay.accept(new Relay.Endpoint(){public InputStream input()throws IOException{return server.getInputStream();}public OutputStream output()throws IOException{return server.getOutputStream();}public void shutdownOutput()throws IOException{server.shutdownOutput();}public void setReadTimeout(int ms)throws IOException{server.setSoTimeout(ms);}public void close()throws IOException{server.close();}});
   DataOutputStream out=new DataOutputStream(client.getOutputStream());out.write(new byte[]{'T','M','A','X',1});out.write(TOKEN.getBytes(StandardCharsets.US_ASCII));out.writeByte(2);byte[] host="127.0.0.1".getBytes(StandardCharsets.US_ASCII);out.writeShort(host.length);out.write(host);out.writeShort(port);out.flush();require(client.getInputStream().read()==0,"relay rejected fixture flow");return client;
  }
 }
 public static void main(String[] args)throws Exception {
  BlockingQueue<String> hosts=new LinkedBlockingQueue<>();
  try(DatagramSocket echo=new DatagramSocket(new InetSocketAddress(InetAddress.getLoopbackAddress(),0))){
   Relay.Dialer dialer=new Relay.Dialer(){public Socket tcp(String h,int p)throws IOException{throw new IOException("unused");}public Relay.DatagramFlow udp(String h,int p)throws IOException{hosts.add(h+":"+p);return new Relay.DatagramFlow(new DatagramSocket(new InetSocketAddress(InetAddress.getLoopbackAddress(),0)),new InetSocketAddress(InetAddress.getLoopbackAddress(),echo.getLocalPort()));}};
   try(Relay relay=new Relay(dialer,TOKEN)){
    require(!relay.setDnsProfile("invalid"),"invalid DNS accepted");
    relay.setDnsProfile("adguard");try(Socket dns=flow(relay,53);Socket ordinary=flow(relay,5000)){
     require("94.140.14.14:53".equals(hosts.poll(1,TimeUnit.SECONDS)),"AdGuard not applied to actual DNS dial");
     require("127.0.0.1:5000".equals(hosts.poll(1,TimeUnit.SECONDS)),"non-DNS redirected");
     relay.setDnsProfile("family");require(dns.getInputStream().read()==-1,"old DNS flow survived change");
     DataOutputStream out=new DataOutputStream(ordinary.getOutputStream());out.writeShort(1);out.writeByte(7);out.flush();
     echo.setSoTimeout(2000);DatagramPacket packet=new DatagramPacket(new byte[8],8);echo.receive(packet);require(packet.getLength()==1&&packet.getData()[0]==7,"non-DNS flow interrupted by DNS change");
     try(Socket next=flow(relay,53)){require("94.140.14.15:53".equals(hosts.poll(1,TimeUnit.SECONDS)),"family profile not applied");require(next.isConnected(),"new DNS flow not connected");}
    }
    require(relay.controlStatus().contains("\"dnsProfile\":\"family\""),"DNS acknowledgement missing");
   }
  }
  System.out.println("PASS real loopback relay: DNS destination, live DNS-only replacement, non-DNS continuity and acknowledgement");
 }
}
