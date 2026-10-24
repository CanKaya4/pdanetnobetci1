package io.tethermax.app;

/** Session-local lifecycle. Network callbacks may never revive a terminal state. */
final class ServiceState {
    enum State { STARTING, RUNNING, STOPPING, STOPPED, ERROR }
    private volatile State state = State.STOPPED;
    State get() { return state; }
    synchronized void start() {
        if (state != State.STOPPED) throw new IllegalStateException("Session already started");
        state = State.STARTING;
    }
    synchronized boolean ready() {
        if (state != State.STARTING) return false;
        state = State.RUNNING; return true;
    }
    boolean acceptsNetwork() { return state == State.STARTING || state == State.RUNNING; }
    synchronized boolean fail() {
        if (!acceptsNetwork()) return false;
        state = State.ERROR; return true;
    }
    synchronized void stopping() { if (state != State.ERROR) state = State.STOPPING; }
    synchronized void stopped() { if (state != State.ERROR) state = State.STOPPED; }
}
