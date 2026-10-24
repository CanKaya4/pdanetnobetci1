package io.tethermax.engine;
import java.io.*;
import java.net.*;
import java.util.*;
import java.util.concurrent.atomic.*;
public final class DnsOverHttpsTest {
 static byte[] query(){String hex="123401000001000000000000076578616d706c65036e65740000010001";byte[] bytes=new byte[hex.length()/2];for(int n=0;n<bytes.length;n++)bytes[n]=(byte)Integer.parseInt(hex.substring(n*2,n*2+2),16);return bytes;}
 static byte[] response(byte[] q){byte[] a=Arrays.copyOf(q,q.length+16);a[2]=(byte)129;a[3]=(byte)128;a[7]=1;byte[] rr={(byte)192,12,0,1,0,1,0,0,0,60,0,4,(byte)192,0,2,1};System.arraycopy(rr,0,a,q.length,16);return a;}
 static void require(boolean b,String reason){if(!b)throw new AssertionError(reason);}
 static byte[] ask(Socket s,byte[] query)throws IOException{DataOutputStream out=new DataOutputStream(s.getOutputStream());out.writeShort(query.length);out.write(query);out.flush();DataInputStream in=new DataInputStream(s.getInputStream());int n=in.readUnsignedShort();byte[] response=new byte[n];in.readFully(response);return response;}
 public static void main(String[] args)throws Exception{
  AtomicLong now=new AtomicLong();AtomicInteger calls=new AtomicInteger();DnsOverHttps cache=new DnsOverHttps(now::get);
  DnsOverHttps.Exchange exchange=(url,q)->{require(url.equals("https://dns.adguard-dns.com/dns-query"),"wrong encrypted endpoint");calls.incrementAndGet();return response(q);};
  byte[] q=query();cache.resolve("adguard","cellular-fixture",q,exchange);now.set(30000000000L);q[0]=42;byte[] cached=cache.resolve("adguard","cellular-fixture",q,exchange);require(calls.get()==1&&cached[0]==42&&cached[q.length+9]==30,"cache ID/TTL aging failed");
  now.set(61000000000L);cache.resolve("adguard","cellular-fixture",q,exchange);require(calls.get()==2,"expired DNS cached");
  cache.resolve("adguard","different-network",q,exchange);require(calls.get()==3,"network caches mixed");
  byte[] probe=q.clone();probe[21]='c';probe[22]='o';probe[23]='m';int beforeProbe=calls.get();cache.resolve("adguard","cellular-fixture",probe,exchange);cache.resolve("adguard","cellular-fixture",probe,exchange);require(calls.get()==beforeProbe+2,"latency probes were satisfied by local DNS cache");
  AtomicBoolean fail=new AtomicBoolean();AtomicInteger plain=new AtomicInteger();
  Relay.Dialer dialer=new Relay.Dialer(){public Socket tcp(String h,int p)throws IOException{plain.incrementAndGet();throw new IOException("no plaintext");}public Relay.DatagramFlow udp(String h,int p)throws IOException{plain.incrementAndGet();throw new IOException("no plaintext");}public boolean encryptedDns(){return true;}public byte[] dns(String profile,byte[] query)throws IOException{if(fail.get())throw new IOException("fixture encrypted upstream failure");return cache.resolve(profile,"relay-fixture",query,exchange);}};
  try(Relay relay=new Relay(dialer,RelayDnsTest.TOKEN)){
   relay.setDnsProfile("adguard");try(Socket s=RelayDnsTest.flow(relay,53)){byte[] a=ask(s,q);require(a[0]==q[0]&&a[1]==q[1],"encrypted relay ID mismatch");}
   fail.set(true);relay.setDnsProfile("family");try(Socket s=RelayDnsTest.flow(relay,53)){DataOutputStream out=new DataOutputStream(s.getOutputStream());out.writeShort(q.length);out.write(q);out.flush();require(s.getInputStream().read()==-1,"encrypted DNS failure did not close the request");}
   require(plain.get()==0&&relay.dnsError()!=null,"silent plaintext fallback or missing error");
  }
  System.out.println("PASS DoH relay, RAM cache with ID rewrite/TTL expiry, network isolation and no plaintext fallback; HTTPS exchange fixture only");
 }
}
