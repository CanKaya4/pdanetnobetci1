package io.tethermax.app;
import android.net.Network;
import android.os.SystemClock;
import java.net.*;
import java.io.IOException;
import java.util.concurrent.atomic.AtomicBoolean;

final class PositionTest {
 interface Progress {void update(int done);}
 interface NetworkCheck {boolean same();}
 static String run(Network network,AtomicBoolean cancel,NetworkCheck check,Progress progress)throws IOException,InterruptedException {
  String[] hosts={"1.1.1.1","8.8.8.8"};double[][] samples=new double[2][10];
  for(int i=0;i<10;i++)for(int e=0;e<2;e++){
   if(cancel.get()||Thread.currentThread().isInterrupted())throw new InterruptedException();
   if(!check.same())throw new IOException("Hücresel ağ değişti; bu konumu yeniden test edin.");
   long start=SystemClock.elapsedRealtimeNanos();
   try(Socket socket=network.getSocketFactory().createSocket()){socket.connect(new InetSocketAddress(InetAddress.getByName(hosts[e]),443),2000);samples[e][i]=(SystemClock.elapsedRealtimeNanos()-start)/1000000.0;}
   catch(IOException error){samples[e][i]=-1;}
   if(!check.same())throw new IOException("Hücresel ağ değişti; bu konumu yeniden test edin.");
   progress.update(i*2+e+1);Thread.sleep(500);
  }
  if(cancel.get())throw new InterruptedException();
  return PositionQuality.summarize(samples);
 }
}
