package io.tethermax.app;

/** Pure policy: only optional local work is reduced; the relay is never throttled. */
public final class GuardianPolicy {
    private boolean reduced;
    private long coolSince = -1;
    public boolean update(long now, int thermal, float batteryC) {
        boolean hot = thermal >= 2 || (!Float.isNaN(batteryC) && batteryC >= 40);
        boolean cool = thermal < 2 && (Float.isNaN(batteryC) || batteryC < 38);
        if (hot) { reduced = true; coolSince = -1; }
        else if (reduced && cool) {
            if (coolSince < 0) coolSince = now;
            if (now - coolSince >= 60000) { reduced = false; coolSince = -1; }
        } else coolSince = -1;
        return reduced;
    }
    public static long maintenanceDelay(boolean connected, boolean interactive) {
        return connected ? 5000 : interactive ? 15000 : 30000;
    }
    public static double cpuPercent(long cpuDelta, long elapsedDelta) {
        return cpuDelta < 0 || elapsedDelta <= 0 ? Double.NaN : cpuDelta * 100.0 / elapsedDelta;
    }
    public static long retryDelay(int failure) {
        return failure<1||failure>6 ? -1 : Math.min(30000L,1000L << (failure-1));
    }
}
