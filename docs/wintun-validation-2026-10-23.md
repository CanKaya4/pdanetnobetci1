# Limited Wintun lifecycle validation — 23 October 2026

User authorization covered only creating/removing an isolated temporary adapter
and starting/ending a Wintun session. No full TUN Internet traffic was authorized.

## Actual execution

- Starting HEAD:48673fe, branch tethermax-foundation, clean worktree.
- Baseline selected route: existing Wi-Fi. Direct certificate-verified HTTPS
  baseline returned HTTP200. Actual Windows adapter, IP-interface, route, DNS,
  system-driver and network-driver-store snapshots were saved before the test.
- Official amd64 Wintun DLL SHA256 matched
  E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE.
  Authenticode status Valid, signer WireGuard LLC.
- Initial background UAC launch was cancelled by Windows without a visible
  prompt. No test ran in that attempt. A local WPF launcher was built so the
  human could initiate the elevated process directly. The human confirmed UAC.
- Created ONLY TetherMax-Test-df5832f6,
  requested GUID d007231f-7028-4d54-8b03-c2ee5e6c8325.
- WintunCreateAdapter succeeded; WintunStartSession with4MiB succeeded.
  The active journal independently enumerated the adapter through Windows IP
  Helper while the native session was active. No packets were sent or received.
- WintunEndSession and WintunCloseAdapter executed. The immediate post-close
  enumeration still contained the device, so the original tool returned EXIT4.
  This result is retained; it was not silently relabelled a clean exit.

## Cleanup verified independently after return

- Get-NetAdapter -IncludeHidden: zero matches for the exact created name/GUID.
- Win32_PnPEntity: zero matching name/GUID devices.
- Actual Windows comparisons: adapters, IP interfaces, routes, selected route,
  DNS and relevant system-driver inventory identical to baseline.
- pnputil network-driver-store inventory identical, zero changed lines.
  No new retained driver package is visible in that comparison. No shared driver
  was removed; this is not a claim that Windows left no internal cache/log entry.
- Post-test direct Wi-Fi HTTPS returned HTTP200 (0.330517s at that observation).
- No Wi-Fi/hotspot/PdaNet/MyPublicWiFi/ICS/firewall/global DNS/default route
  configuration was authored. No unrelated adapter or driver was removed.

Conclusion: native creation/session/closure occurred and delayed device removal
was verified. The original automatic cleanup assertion was premature and failed;
the complete original run must not be described as an all-green tool execution.
No additional physical adapter trial was run after that error.

## Safe code fix and regression checks

RemovalVerifier now observes only the owned name/GUID for at most40 polls250ms
apart before deciding cleanup failed. It never deletes any additional resource.
The report distinguishes initially pending removal from a persistent device.
Three offline cases passed: already absent, delayed removal, and persistent
device bounded failure. The updated probe compiled and default dry-run passed;
its apply path has NOT been re-run against hardware.

Raw local reports: build/reports/wintun-approved/, including original native
active/after journals, verification JSON, Windows snapshots, signature result,
driver-store diff, UAC launch error and regression output. App-private network
details are not included in this public source report.

This test does NOT validate packet forwarding, TCP/UDP TUN operation, DNS
routing, IPv6, full rollback on crash/power loss, MyPublicWiFi interoperability,
adaptive shaping or latency improvements. Those remain separate development
and isolation tests, with new permission before any real global network change.
