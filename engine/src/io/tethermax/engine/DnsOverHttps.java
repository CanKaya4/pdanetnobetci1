package io.tethermax.engine;
import java.io.*;
import java.util.*;
import java.util.function.LongSupplier;
/** Bounded RAM-only DNS cache. HTTPS/TLS transport is supplied by the selected Android Network. */
public final class DnsOverHttps {
 public interface Exchange { byte[] post(String url,byte[] query)throws IOException; }
 private static final class Entry {final byte[] reply;final long until,at;Entry(byte[] r,long u,long a){reply=r;until=u;at=a;}}
 private final LinkedHashMap<String,Entry> cache=new LinkedHashMap<String,Entry>(32,0.75f,true);
 private final LongSupplier clock;
 public DnsOverHttps(){this(System::nanoTime);} public DnsOverHttps(LongSupplier clock){this.clock=clock;}
 public static String endpoint(String profile)throws IOException {
  if("adguard".equals(profile))return "https://dns.adguard-dns.com/dns-query";
  if("family".equals(profile))return "https://family.adguard-dns.com/dns-query";
  if("cloudflare".equals(profile))return "https://cloudflare-dns.com/dns-query";
  throw new IOException("Encrypted DNS profile required");
 }
 public byte[] resolve(String profile,String network,byte[] query,Exchange exchange)throws IOException {
  String url=endpoint(profile);if(query.length<12||query.length>65535||(query[2]&128)!=0)throw new IOException("Invalid DNS query");
  byte[] normalized=query.clone();normalized[0]=normalized[1]=0;
  String key=profile+":"+network+":"+Base64.getEncoder().encodeToString(normalized);
  boolean probe=latencyProbe(query);
  synchronized(cache){Entry e=probe?null:cache.get(key);if(e!=null){if(clock.getAsLong()<e.until){byte[] answer=e.reply.clone();age(answer,(clock.getAsLong()-e.at)/1000000000L);answer[0]=query[0];answer[1]=query[1];return answer;}cache.remove(key);}}
  byte[] response=exchange.post(url,query);
  if(response==null||response.length<12||response.length>65535||response[0]!=query[0]||response[1]!=query[1]||(response[2]&128)==0||response[4]!=query[4]||response[5]!=query[5])throw new IOException("Invalid DNS-over-HTTPS response");
  long ttl=ttl(response);if(ttl>0&&!probe){byte[] saved=response.clone();saved[0]=saved[1]=0;synchronized(cache){cache.put(key,new Entry(saved,clock.getAsLong()+ttl*1000000000L,clock.getAsLong()));while(cache.size()>256)cache.remove(cache.keySet().iterator().next());}}
  return response;
 }
 // The existing engine uses this exact question for live cellular delay probes.
 // Probe replies must cross HTTPS even while ordinary client DNS is cached.
 private static boolean latencyProbe(byte[] q){byte[] name={7,'e','x','a','m','p','l','e',3,'c','o','m',0,0,1,0,1};if(q.length!=29||q[4]!=0||q[5]!=1)return false;for(int i=0;i<name.length;i++)if(q[12+i]!=name[i])return false;return true;}
 private static int word(byte[] p,int offset)throws IOException{if(offset<0||offset+2>p.length)throw new IOException("DNS truncated");return ((p[offset]&255)<<8)|(p[offset+1]&255);}
 private static int name(byte[] p,int offset)throws IOException{for(int labels=0;labels<128;labels++){if(offset>=p.length)throw new IOException("DNS name truncated");int size=p[offset++]&255;if(size==0)return offset;if((size&192)==192){if(offset>=p.length)throw new IOException("DNS pointer truncated");return offset+1;}if(size>63||offset+size>p.length)throw new IOException("DNS label invalid");offset+=size;}throw new IOException("DNS name too long");}
 private static void age(byte[] p,long seconds)throws IOException{int offset=12;for(int q=0;q<word(p,4);q++)offset=name(p,offset)+4;int records=word(p,6)+word(p,8)+word(p,10);for(int n=0;n<records;n++){offset=name(p,offset);if(offset+10>p.length)throw new IOException("DNS record truncated");int type=word(p,offset);if(type!=41){long ttl=((p[offset+4]&255L)<<24)|((p[offset+5]&255L)<<16)|((p[offset+6]&255L)<<8)|(p[offset+7]&255L);ttl=Math.max(0,ttl-seconds);for(int b=0;b<4;b++)p[offset+4+b]=(byte)(ttl>>(24-8*b));}offset+=10+word(p,offset+8);if(offset>p.length)throw new IOException("DNS record truncated");}}
 private static long ttl(byte[] p){try{if((p[2]&2)!=0||(p[3]&15)!=0||word(p,6)==0)return 0;int offset=12;for(int q=0;q<word(p,4);q++){offset=name(p,offset)+4;if(offset>p.length)return 0;}long minimum=3600;for(int n=0;n<word(p,6);n++){offset=name(p,offset);if(offset+10>p.length)return 0;long value=((p[offset+4]&255L)<<24)|((p[offset+5]&255L)<<16)|((p[offset+6]&255L)<<8)|(p[offset+7]&255L);minimum=Math.min(minimum,value);int bytes=word(p,offset+8);offset+=10+bytes;if(offset>p.length)return 0;}return minimum;}catch(IOException e){return 0;}}
}
