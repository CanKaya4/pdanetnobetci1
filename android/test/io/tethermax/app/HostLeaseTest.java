package io.tethermax.app;
public final class HostLeaseTest {
 static void require(boolean value){if(!value)throw new AssertionError();}
 public static void main(String[] args){
  require(!HostLease.fresh(100,0,false));require(HostLease.fresh(100,90,false));
  require(!HostLease.fresh(15000000100L,100,false));require(!HostLease.fresh(100,101,false));require(!HostLease.fresh(100,90,true));
  System.out.println("PASS CPU lease: authenticated heartbeat required, expires at 15s, rejects future timestamp and stopping service");
 }
}
