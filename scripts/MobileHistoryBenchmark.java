package io.tethermax.app;
import java.lang.management.ManagementFactory;
import java.util.*;

/** Desktop-JVM microbenchmark only: this is NOT Android CPU/RAM/temperature evidence. */
public final class MobileHistoryBenchmark {
    static volatile long sink;
    static final int COUNT=1000000;
    static final com.sun.management.ThreadMXBean BEAN=(com.sun.management.ThreadMXBean)ManagementFactory.getThreadMXBean();
    static long[] run(boolean before) {
        ArrayList<float[]> points=new ArrayList<>();ArrayList<Long> times=new ArrayList<>();
        SampleHistory ring=new SampleHistory(1800);
        long id=Thread.currentThread().threadId(),allocation=BEAN.getThreadAllocatedBytes(id),cpu=BEAN.getCurrentThreadCpuTime(),wall=System.nanoTime();
        if(before){
            for(int i=0;i<COUNT;i++){points.add(new float[]{i,-i});times.add((long)i);if(points.size()>1800){points.remove(0);times.remove(0);}}
            sink=times.get(0)+(long)points.get(1799)[0];
        }else{
            for(int i=0;i<COUNT;i++)ring.add(i,i,-i);
            sink=ring.time(0)+(long)ring.value(1799,0);
        }
        return new long[]{BEAN.getCurrentThreadCpuTime()-cpu,BEAN.getThreadAllocatedBytes(id)-allocation,System.nanoTime()-wall};
    }
    public static void main(String[] args){
        BEAN.setThreadAllocatedMemoryEnabled(true);BEAN.setThreadCpuTimeEnabled(true);
        run(true);run(false);long[] before=new long[7],after=new long[7],oldAlloc=new long[7],newAlloc=new long[7],oldWall=new long[7],newWall=new long[7];
        for(int i=0;i<7;i++){
            long[] a,b;if(i%2==0){a=run(true);b=run(false);}else{b=run(false);a=run(true);}
            before[i]=a[0];oldAlloc[i]=a[1];after[i]=b[0];newAlloc[i]=b[1];
            oldWall[i]=a[2];newWall[i]=b[2];
        }
        Arrays.sort(before);Arrays.sort(after);Arrays.sort(oldAlloc);Arrays.sort(newAlloc);
        Arrays.sort(oldWall);Arrays.sort(newWall);
        System.out.println("Desktop JVM synthetic storage benchmark; median of 7, 1,000,000 insertions; excludes initialization/drawing/UI/relay.");
        System.out.printf(Locale.ROOT,"Before CPU: %.3f ms; allocated: %d bytes%nAfter CPU: %.3f ms; allocated: %d bytes%n",before[3]/1e6,oldAlloc[3],after[3]/1e6,newAlloc[3]);
        System.out.printf(Locale.ROOT,"Before wall time: %.3f ms; after wall time: %.3f ms%n",oldWall[3]/1e6,newWall[3]/1e6);
        System.out.println("A zero CPU timer result is below timer resolution, NOT zero CPU. Allocation includes the benchmark result array (40 bytes); initialization is excluded.");
        System.out.println("Not phone CPU, retained RAM, battery temperature or USB continuity. These require separately approved hardware tests.");
    }
}
