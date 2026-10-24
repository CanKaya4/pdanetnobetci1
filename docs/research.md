# Upstream research — 2026-10-23

Sources were inspected through GitHub HTTPS/Git, including README, implementation
layout and license files. No upstream implementation source is incorporated yet.

| Project | Inspected revision | License | Finding / decision |
|---|---|---|---|
| [OpenTether](https://github.com/HelaFaye/opentether) | `3f9dbe12f14b6fb7efc2a38d715871db3370cfdf` | MIT | OpenWrt service scripts use ADB forward → Android SOCKS → hev-socks5-tunnel → TUN. USB-forward architecture is usable; router firewall/default-route scripts are inappropriate for the protected Windows host. |
| [CAKE-autorate](https://github.com/lynxthecat/cake-autorate) | `ac75f49348c2ce697e698270a340d42e499cd953` | GPL v2 text in LICENCE.md | Bash controller adjusts an existing Linux CAKE qdisc using measured RTT/OWD and load, with min/base/max rates. It is not a portable Windows packet scheduler. Inspected development HEAD, not a claimed stable dependency. |
| [sqm-autorate](https://github.com/sqm-autorate/sqm-autorate) | `87e31e2a80927481f19dfcb0c59eaa0a94a1482e` | MPL 2.0 | Lua/OpenWrt controller, baseliner, reflector selection and traffic stats. Useful reference for measured feedback and stale/failed reflector handling; cannot provide Windows CAKE by merely launching Lua. |

## Consequences

First establish a real TCP/UDP data plane, then validate it on approved devices.
A Linux CAKE controller without a controlled bottleneck cannot deliver Windows
SQM. Rootless Android cannot be assumed to expose Linux `tc` or CAKE.

A later feedback controller needs multiple measured reflectors, baseline latency,
loaded latency, loss, upload/download counters, rate bounds, hysteresis and a real
shaping actuator. Units, timestamps and validity must accompany each sample.
Synthetic traces can test decisions; they cannot establish improvements on 5G.
One flow's game traffic must not be delayed behind streaming traffic in a shared
unbounded queue. UDP over ADB/TCP has head-of-line costs and requires measurement.

Windows system-wide capture requires a separately reviewed TUN or filtering
integration, routing/DNS rollback and compatibility tests. Kernel driver or
anti-cheat compatibility cannot be established by cross-compilation. Never inject
into games or alter anti-cheat. MyPublicWiFi integration requires approved tests
of its actual adapter/ICS path; do not assume SOCKS proxy settings cover ICS.

No TTL, APN, quota or carrier-policy bypass is implemented or promised.
