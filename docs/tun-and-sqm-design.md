# System-wide networking and SQM — not implemented

The USB SOCKS engine has physical-device evidence. No TUN/SQM completion is
claimed. The next implementation must operate first on isolated Windows hosts,
then present its exact adapter/IP/route/DNS changes and rollback journal for
separate user approval before touching the user's current connection.

[Wintun](https://www.wintun.net/) exposes layer-3 IP packets and distributes
signed DLLs. It does not supply a TCP/IP stack or turn packets into SOCKS flows.
A supported userspace TCP/IP stack and packet-to-flow adapter are needed before
IPv4/TCP/UDP/DNS can route to the existing Android dialer. IPv6 and shared ICS /
MyPublicWiFi paths require independent validation and licenses. No DLL has been
loaded, adapter created or driver installed during this development increment.

Rollback must record only newly created adapter GUIDs, IP assignments, DNS and
routes; use ownership markers and compare current values before restoring them.
Do not use blanket resets, flush all routes, disable Wi-Fi or stop PdaNet. A
watchdog in a separate process must recover owned changes after UI/engine crash.
Dry-run and injected-failure tests must precede real apply. None is delivered
yet, so no system-network approval is being requested now.

[FQ-CoDel, RFC 8290](https://www.rfc-editor.org/rfc/rfc8290) combines flow queues,
deficit scheduling and CoDel AQM on packets. Dropping arbitrary bytes from the
current TCP USB stream would corrupt the protocol; it is not FQ-CoDel. Per-flow
scheduling/backpressure and packet-aware UDP handling belong before socket writes
on Android upload and before delivery/ACK pressure on download. Game identity is
not inferable reliably from a port alone; process ownership and shared-client
classification need verified observation paths after a TUN exists.

[cake-autorate](https://github.com/lynxthecat/cake-autorate) adjusts a real CAKE
qdisc using load and delay feedback on Linux-family/router systems. Running its
script on Windows supplies no CAKE queue. An analogous bounded-rate controller
would need reliable rate/RTT observations, hysteresis and a real controllable
queue. The base station's queue remains outside application control; downstream
TCP can respond to backpressure, while already-arrived UDP cannot recover radio
capacity through application-side drops. No claim of improved 5G ping is made.

Required before default enable: controlled idle/upload/download A/B runs under
fixed budgets, sample counts and radio conditions; P50/P95/P99, goodput, loss,
fairness, recovery and CPU/power. Optimize nothing by default merely because a
mode is selected. No fabricated gaming/streaming switches were added.
