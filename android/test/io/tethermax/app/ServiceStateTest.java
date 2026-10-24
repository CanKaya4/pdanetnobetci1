package io.tethermax.app;

public final class ServiceStateTest {
    private static void require(boolean value) { if (!value) throw new AssertionError("Invalid lifecycle transition"); }
    public static void main(String[] args) {
        ServiceState failed = new ServiceState(); failed.start(); require(failed.fail());
        require(!failed.ready() && !failed.acceptsNetwork());
        failed.stopping(); failed.stopped(); require(failed.get() == ServiceState.State.ERROR);
        require(!failed.fail());
        for (int i=0;i<100;i++) {
            ServiceState session = new ServiceState(); session.start(); require(session.ready());
            require(!session.ready()); session.stopping(); require(!session.acceptsNetwork());
            require(!session.ready() && !session.fail()); session.stopped(); session.stopped();
            require(session.get() == ServiceState.State.STOPPED);
        }
        System.out.println("PASS startup failure survives cleanup; late network readiness cannot revive ERROR/STOPPING/STOPPED; 100 independent restart cycles");
    }
}
