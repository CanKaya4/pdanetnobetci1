package io.tethermax.app;

public final class UsageMeterTest {
    static int passed;
    static void check(boolean ok,String name){if(!ok)throw new AssertionError(name);passed++;}
    public static void main(String[] args){
        UsageMeter m=new UsageMeter("",0,1000);
        check(m.sample("2026-10",300,400)==0&&m.used==700,"first cumulative sample");
        check(m.sample("2026-10",300,400)==0&&m.used==700,"duplicate sample");
        check(m.sample("2026-10",400,400)==80&&m.used==800,"80 percent notification");
        check(m.sample("2026-10",450,450)==0,"no repeat 80 notification");
        check(m.sample("2026-10",500,500)==100,"100 percent notification");
        check(m.sample("2026-10",1,600)==0&&m.used==1000,"counter rollback ignored");
        check(m.sample("2026-11",600,600)==0&&m.used==200,"new month only delta");
        m.sample("2026-10",650,650);check(m.used==300&&m.month.equals("2026-11"),"clock rollback cannot erase usage");
        UsageMeter restarted=new UsageMeter(m.month,m.used,m.budget);
        restarted.sample("2026-11",20,30);check(restarted.used==350,"new relay lifetime retains history");
        UsageMeter overflow=new UsageMeter("2026-11",Long.MAX_VALUE-1,0);overflow.sample("2026-11",4,4);check(overflow.used==Long.MAX_VALUE,"saturating total");
        overflow.sample("2026-11",Long.MAX_VALUE,Long.MAX_VALUE);check(overflow.used==Long.MAX_VALUE,"overflow sample rejected");
        for(int i=0;i<100000;i++)restarted.sample("2026-11",20+i,30+i);
        check(restarted.used==200348,"long sample sequence exact");
        boolean rejected=false;try{new UsageMeter("corrupt",1,0);}catch(IllegalArgumentException expected){rejected=true;}check(rejected,"corrupt storage rejected");
        System.out.println("PASS "+passed+" UsageMeter assertions; no Android runtime or phone used.");
    }
}
