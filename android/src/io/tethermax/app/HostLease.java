package io.tethermax.app;
/** A fresh authenticated host heartbeat authorizes a short CPU-only lease. */
public final class HostLease {
 private HostLease(){}
 public static boolean fresh(long now,long heartbeat,boolean stopping){return !stopping&&heartbeat!=0&&now>=heartbeat&&now-heartbeat<15000000000L;}
}
