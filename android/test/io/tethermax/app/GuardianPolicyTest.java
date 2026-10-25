package io.tethermax.app;
public final class GuardianPolicyTest {
    private static void check(boolean pass){if(!pass)throw new AssertionError();}
    public static void main(String[] args){
        GuardianPolicy p=new GuardianPolicy();
        check(!p.update(0,-1,Float.NaN));check(p.update(1,2,30));
        check(p.update(2,0,37));check(p.update(60001,0,37));check(!p.update(60002,0,37));
        check(p.update(60003,0,40));check(p.update(60004,0,38));
        check(p.update(60005,0,37));check(p.update(120004,0,37));check(!p.update(120005,0,37));
        check(GuardianPolicy.maintenanceDelay(true,false)==5000);
        check(GuardianPolicy.maintenanceDelay(false,false)==30000);
        check(GuardianPolicy.maintenanceDelay(false,true)==15000);
        check(GuardianPolicy.cpuPercent(300,100)==300);
        check(Double.isNaN(GuardianPolicy.cpuPercent(-1,100)));
        check(Double.isNaN(GuardianPolicy.cpuPercent(1,0)));
        check(GuardianPolicy.retryDelay(1)==1000);check(GuardianPolicy.retryDelay(6)==30000);
        check(GuardianPolicy.retryDelay(7)==60000);check(GuardianPolicy.retryDelay(Integer.MAX_VALUE)==60000);check(GuardianPolicy.retryDelay(0)==-1);
        System.out.println("PASS Guardian hysteresis, unsupported values, maintenance cadence, multicore CPU");
    }
}
