package io.tethermax.app;
public final class SampleHistoryTest {
    private static void check(boolean pass){if(!pass)throw new AssertionError();}
    public static void main(String[] args){
        SampleHistory h=new SampleHistory(1800);
        for(int i=0;i<1000000;i++)h.add(i,i,-i);
        check(h.size()==1800);check(h.time(0)==998200);check(h.time(1799)==999999);
        check(h.value(1799,0)==999999);check(h.value(1799,1)==-999999);
        h.add(1000000,Float.NaN,Float.POSITIVE_INFINITY);
        check(Float.isNaN(h.value(1799,0)));check(Float.isInfinite(h.value(1799,1)));
        System.out.println("PASS 1,000,000 samples: bounded 1800 slots, chronological wrap, missing samples preserved");
    }
}
