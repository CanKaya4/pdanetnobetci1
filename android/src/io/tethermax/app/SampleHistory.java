package io.tethermax.app;

/** Fixed storage and O(1) insertion; no per-sample object allocation. */
final class SampleHistory {
    private final float[] first, second;
    private final long[] timestamps;
    private int start, size;
    SampleHistory(int capacity) {
        if(capacity<=0)throw new IllegalArgumentException("capacity");
        first=new float[capacity];second=new float[capacity];timestamps=new long[capacity];
    }
    int size(){return size;}
    private int slot(int index){if(index<0||index>=size)throw new IndexOutOfBoundsException();return (start+index)%first.length;}
    float value(int index,int series){int p=slot(index);return series==0?first[p]:second[p];}
    long time(int index){return timestamps[slot(index)];}
    void add(long time,float a,float b){
        int p=(start+size)%first.length;
        if(size==first.length){p=start;start=(start+1)%first.length;}else size++;
        timestamps[p]=time;first[p]=a;second[p]=b;
    }
}
