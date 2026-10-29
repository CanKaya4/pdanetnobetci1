package io.tethermax.app;

/** One relay lifetime; persisted totals are independent of session counters. */
final class UsageMeter {
    String month;
    long used,budget;
    private long up,down;
    UsageMeter(String month,long used,long budget) {
        if(month==null||(!month.isEmpty()&&!month.matches("[0-9]{4}-(0[1-9]|1[0-2])"))||used<0||budget<0||budget>1000000000000000L)
            throw new IllegalArgumentException("Invalid usage record");
        this.month=month;this.used=used;this.budget=budget;
    }
    int level(){return budget==0?0:used>=budget?100:used>=budget*.8?80:0;}
    int sample(String currentMonth,long uploaded,long downloaded) {
        if(uploaded<up||downloaded<down||uploaded<0||downloaded<0)return 0;
        long du=uploaded-up,dd=downloaded-down;
        if(du>Long.MAX_VALUE-dd)return 0;
        int old=level();
        if(currentMonth.compareTo(month)>0){month=currentMonth;used=0;old=0;}
        long delta=du+dd;used=delta>Long.MAX_VALUE-used?Long.MAX_VALUE:used+delta;
        up=uploaded;down=downloaded;
        return level()>old?level():0;
    }
}
