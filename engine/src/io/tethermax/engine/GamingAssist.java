package io.tethermax.engine;

import java.io.InterruptedIOException;
import java.util.concurrent.TimeUnit;

/** Short authenticated session lease; bulk TCP backpressure before cellular send.
 * Small writes and UDP never queue behind this limiter. No radio/QoS guarantee. */
public final class GamingAssist {
    private static final long LEASE=TimeUnit.SECONDS.toNanos(8);
    private static final int BURST=4096;
    private boolean enabled=true, selected;
    private long heartbeat, refill, waitedNanos, waits;
    private double ceiling, tokens=BURST;
    public synchronized void setEnabled(boolean value){enabled=value;notifyAll();}
    public synchronized boolean enabled(){return enabled;}
    public synchronized void update(long now,boolean gaming,double uploadMbps){
        if(!Double.isFinite(uploadMbps)||uploadMbps<0||uploadMbps>10000)throw new IllegalArgumentException("invalid upload ceiling");
        refill(now);
        if(!fresh(now)||selected!=gaming)tokens=BURST;
        selected=gaming;heartbeat=now;ceiling=uploadMbps;refill=now;notifyAll();
    }
    public synchronized void clear(){selected=false;heartbeat=0;ceiling=0;tokens=BURST;notifyAll();}
    private boolean fresh(long now){return heartbeat!=0&&now>=heartbeat&&now-heartbeat<LEASE;}
    public synchronized boolean active(long now,boolean manual){return enabled&&(manual||(fresh(now)&&selected));}
    private double rate(long now){return enabled&&selected&&fresh(now)&&ceiling>0?ceiling*.85*1e6/8:0;}
    private void refill(long now){
        if(refill!=0&&now>=refill)tokens=Math.min(BURST,tokens+(now-refill)/1e9*rate(now));
        refill=now;
    }
    // Pure step for deterministic tests. Never reserves future time: changing or
    // expiring a lease releases all waiters without lingering scheduling debt.
    synchronized long delay(long now,int size){
        if(size<0||size>BURST)throw new IllegalArgumentException("invalid TCP chunk");
        refill(now);double bytesPerSecond=rate(now);
        if(size<=1200||bytesPerSecond==0)return 0;
        if(tokens>=size){tokens-=size;return 0;}
        return Math.max(1,(long)Math.ceil((size-tokens)*1e9/bytesPerSecond));
    }
    public synchronized void await(int size)throws InterruptedIOException {
        long start=System.nanoTime();boolean waited=false;
        for(;;){
            long delay=delay(System.nanoTime(),size);
            if(delay==0){if(waited){waits++;waitedNanos+=System.nanoTime()-start;}return;}
            waited=true;
            try{TimeUnit.NANOSECONDS.timedWait(this,Math.min(delay,TimeUnit.MILLISECONDS.toNanos(20)));}
            catch(InterruptedException canceled){Thread.currentThread().interrupt();throw new InterruptedIOException("bulk pacing interrupted");}
        }
    }
    public synchronized double bulkMbps(long now){return rate(now)*8/1e6;}
    public synchronized long waits(){return waits;}
    public synchronized long waitedMicros(){return waitedNanos/1000;}
}
