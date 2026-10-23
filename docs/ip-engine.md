# Experimental IPv4 engine — 23 October 2026

This increment adds a packet-to-flow engine to the existing USB relay. It is not
system-wide Internet completion. Windows IP assignments, routes, DNS, ICS and
firewall configuration are not implemented or applied. No native IP adapter was
created during this increment, and no existing network configuration was changed.

## Implemented path

```text
PacketDevice (Wintun ring, or isolated framed IPC)
  -> IPv4 framing/checksum/MTU validation
  -> gVisor netstack via tun2socks/core
  -> independent authenticated TCP/UDP relay streams
  -> existing Android cellular-bound sockets
  -> return flow -> netstack -> complete IPv4 packets -> same PacketDevice
```

Pinned dependencies: tun2socks v2.7.0 (MIT), gVisor commit 69c2d17aea96
(Apache-2.0). The upstream stack owns TCP sequence/window/retransmission/MSS and
IP fragmentation/reassembly. No custom TCP state machine is substituted.
The tun2socks global engine and OS configuration hooks are not called.
All linked module notices are in `docs/licenses`; `go.sum` pins module integrity.

Ingress is IPv4 TCP/UDP only, including fragments. Invalid header checksums,
lengths, zero TTL and packets above link MTU are rejected. ICMP and IPv6 are
explicitly unsupported; a locally generated ICMP reply must not masquerade as
Internet reachability. UDP DNS is carried as ordinary per-target datagrams;
the DNS fixture checks transaction IDs and reply payloads. No Windows DNS setting
is modified. The stack terminates transport flows; original client IPs are not
forwarded as addresses on the mobile network.

Each destination flow gets a separate existing Android relay stream. TCP and UDP
are not serialized into one application bulk queue. UDP still travels through
ADB/TCP underneath: transport head-of-line blocking remains possible. There is
no measured hardware A/B evidence of reduced latency, improved gaming or SQM.

Default bounds: MTU1500 (allowed576..65535), 128 active flows (maximum1024),
UDP inactivity30s, TCP aggregate flow inactivity5min.
Activity in either direction refreshes the timeout; active one-way transfers
are not cut off by a fixed session duration. Counters report
actual relayed payload bytes, admitted/failed flows and active flows. The
Rejected counter combines invalid-packet and flow-admission rejection events;
it is not a packet-loss percentage.
They are not carrier-billed usage or a calibrated throughput measurement.

## Native boundary and shutdown

`ip-worker` refuses adapter access unless both the USB and separate adapter
approval flags are supplied. These flags are guards, not a replacement for human
authorization. The previously approved create/remove probe does not authorize
running this new native data worker or assigning IPs/routes.

The worker requires absolute DLL/journal paths, creates a new ownership JSONL
journal exclusively, and records a fresh `TetherMax-IP-*` name and GUID before
adapter creation. It never opens an existing adapter. The amd64 DLL must have
SHA256 `e5da8447dc2c320edc0fc52fa01885c103de8c118481f683643cacc3220dafce`;
Windows Authenticode chain/revocation verification must also succeed. A read-only
sharing handle prevents replacement between verification and DLL loading.
The official DLL was separately checked read-only: SHA256 matched, Authenticode
status Valid, signer WireGuard LLC. It was not loaded for a new physical test.
The ARM64 executable builds but deliberately refuses this amd64-only native pin.

Wintun receive/release and allocate/send operate on the owned session. Go/native
buffer copies currently use own-process ReadProcessMemory/WriteProcessMemory;
their performance cost is unmeasured. No other process memory is accessed.
Session close wakes blocked receives, then ends the session and closes only the
owned adapter. It does not call WintunDeleteDriver or remove shared drivers.

The parent sends zero-byte heartbeats on a private inherited pipe. Parent EOF,
an invalid heartbeat or a five-second stall cancels both packet pumps, closes
flows and releases session/adapter handles. The watchdog joins its private
reader, including on timeout. The journal records `handles-closed`, explicitly
not proof of asynchronous Windows PnP removal. This is an in-worker safety
mechanism, not the independent privileged rollback watchdog needed for routes.
An abrupt worker/process termination and native PnP removal remain hardware
validation work; the existing limited probe's results remain a separate record.

`ip-stdio` provides uint16-length-framed packet IPC with no native adapter access.
The bearer is read through private stdin before the first frame; it is never
placed in argv, public output or the ownership journal.

## Completed isolated verification

The fresh shared Java relay fixture permits only loopback destinations. An
explicit test-only map converts benchmark address198.18.0.1 into the local fixture;
production never rewrites destinations. Private gVisor NICs/routes exist only in
test objects, not in the Windows adapter/route tables.

- TCP256KiB binary transfer and half-close through raw IP and the Java relay.
- UDP0/1/512/1200/4000-byte boundaries, including fragmentation at MTU1280.
- Concurrent UDP flows; same source port to different destinations, correct replies.
- DNS A reply via raw UDP/IP and the real shared Java relay code.
- IPv4 checksum/header/length/version/MTU rejection.
- TCP data loss/retransmission, reordering and duplicate segments without corruption.
- UDP/TCP idle cleanup; active one-way TCP transfer survives the idle interval.
- Parent cancellation/EOF/truncated packet cleanup and malformed frame rejection.
- Worker heartbeat EOF/invalid/stall/cancel reader cleanup; approval/checksum gates.
- Go vet and freshly compiled Java/Go tests passed natively on Windows.
- Seven offline pairing checks and three asynchronous-removal verifier checks passed.

The local race detector was not run: a compatible C compiler is unavailable.
CI retains race-enabled Linux and Windows jobs; a workflow definition is not a
passing run. APK v2/v3 signature verification and Windows WPF builds passed.

## Phone probe and remaining work

WPF **IP motoru testi** invokes `ip-check` only after an authorized USB connection.
It reuses secure pairing and exercises a private IPv4 client/engine pair, one
certificate-verified HTTPS request to1.1.1.1 and five UDP DNS queries to1.1.1.1:53.
These are remote cellular tests, never Windows direct fallback. It reports the
observed HTTP result, DNS query durations and actual payload counters; it does
not call those DNS durations unloaded network RTT or infer sustained speed/loss.
The bounded test neither loads Wintun nor configures Windows networking.

This new IP path was NOT tested on the Galaxy A56: ADB reports `unauthorized`,
and the user reports that no RSA authorization prompt is visible. Debugging
being enabled is distinct from authorizing this PC. No ADB reset, USB mode change,
permission revocation, phone reboot or unplug was attempted. Previous successful
SOCKS HTTPS/DNS hardware results do not verify this new IP path.

Still required: this phone probe, approved native ring packet tests on an isolated
host, physical crash/unplug/recovery evidence, independently recoverable scoped
IP/route ownership controller, route/DNS safety dry-runs and an exact separate
real-route approval plan. There is no ready global-route switch to enable.
Adaptive shaping, fair packet scheduler, game/process classification, five active
profiles, MyPublicWiFi/ICS forwarding and detailed latency analysis remain
unimplemented. UI does not present them as working. Hardware engine verification
precedes performance optimization, per repository instructions.

No carrier quota bypass or hotspot-off independence is certified. The relay uses
Android cellular-bound sockets without PdaNet, but operator accounting remains
outside application control. No application session time limit is imposed.
