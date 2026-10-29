package io.tethermax.engine;
import java.util.concurrent.*;

/** Deterministic budget/lease tests plus bounded real-thread cancellation. No sockets. */
public final class GamingAssistTest {
    private static int assertions;
    private static void check(boolean value){if(!value)throw new AssertionError("gaming assertion "+assertions);assertions++;}
    public static void main(String[] args)throws Exception {
        GamingAssist g=new GamingAssist();long t=TimeUnit.SECONDS.toNanos(1);
        check(!g.active(t,false));check(g.active(t,true));check(g.delay(t,4096)==0);
        g.update(t,true,1);check(g.active(t,false));check(Math.abs(g.bulkMbps(t)-.85)<.00001);
        check(g.delay(t,4096)==0);check(g.delay(t,4096)>0);
        check(g.delay(t,1200)==0); // Small latency-sensitive TCP cannot wait for bulk.
        g.update(t+1,true,1);check(g.delay(t+1,4096)>0); // Heartbeat grants no new burst.
        check(g.delay(t+TimeUnit.MILLISECONDS.toNanos(50),4096)==0);
        check(!g.active(t+TimeUnit.SECONDS.toNanos(9),false));check(g.delay(t+TimeUnit.SECONDS.toNanos(9),4096)==0);
        g.update(t+TimeUnit.SECONDS.toNanos(10),true,0);check(g.active(t+TimeUnit.SECONDS.toNanos(10),false));check(g.bulkMbps(t+TimeUnit.SECONDS.toNanos(10))==0);
        g.setEnabled(false);check(!g.active(t+TimeUnit.SECONDS.toNanos(10),true));check(g.delay(t+TimeUnit.SECONDS.toNanos(10),4096)==0);
        g.setEnabled(true);g.update(t+TimeUnit.SECONDS.toNanos(11),false,5);check(!g.active(t+TimeUnit.SECONDS.toNanos(11),false));check(g.delay(t+TimeUnit.SECONDS.toNanos(11),4096)==0);
        for(double invalid:new double[]{Double.NaN,Double.POSITIVE_INFINITY,-1,10001}){boolean rejected=false;try{g.update(t,true,invalid);}catch(IllegalArgumentException expected){rejected=true;}check(rejected);}
        g.clear();check(!g.active(t,false));
        GamingAssist cancel=new GamingAssist();cancel.update(System.nanoTime(),true,.001);cancel.await(4096);
        CountDownLatch started=new CountDownLatch(1);java.util.concurrent.atomic.AtomicBoolean interrupted=new java.util.concurrent.atomic.AtomicBoolean();
        Thread worker=new Thread(()->{started.countDown();try{cancel.await(4096);}catch(java.io.InterruptedIOException expected){interrupted.set(Thread.currentThread().isInterrupted());}});
        worker.start();check(started.await(1,TimeUnit.SECONDS));worker.interrupt();worker.join(1000);check(!worker.isAlive()&&interrupted.get());
        CountDownLatch released=new CountDownLatch(1);Thread waiting=new Thread(()->{try{cancel.await(4096);released.countDown();}catch(java.io.InterruptedIOException ignored){}});waiting.start();
        long deadline=System.nanoTime()+TimeUnit.SECONDS.toNanos(1);while(waiting.getState()!=Thread.State.TIMED_WAITING&&System.nanoTime()<deadline)Thread.sleep(1);
        check(waiting.getState()==Thread.State.TIMED_WAITING);cancel.clear();check(released.await(1,TimeUnit.SECONDS));waiting.join(1000);
        check(cancel.waits()==1&&cancel.waitedMicros()>0);
        System.out.println("PASS "+assertions+" Android gaming lease/bulk pacing assertions; no network or device access");
    }
}
