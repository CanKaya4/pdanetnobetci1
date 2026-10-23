# Galaxy A56 / Windows 11 — 23 October 2026

This is a tested USB SOCKS preview (0.2.0), NOT completion of TetherMax 1.0.
No system-wide adapter/routing, gaming/streaming shaping or bufferbloat improvement
is claimed. Android radio state was reported LTE when inspected; this is not a
certified 5G session.

## Actual device and desktop checks

- Windows 11 existing selected IPv4 Internet route remained Wi-Fi throughout.
- Authorized USB Galaxy A56 SM-A566B, Android 16/API36; APK installed/updated.
- First P-256 ECDH/SAS pairing reached both actual UI consent dialogs. The human
  confirmed matching codes on both screens. The encrypted bearer transfer passed.
- Windows identity persisted across application restarts. Subsequent real Android
  pairing succeeded without entering any long code or repeating the consent.
- Android Keystore identity/trust persisted across APK updates and service restart;
  later actual Windows reconnections also succeeded without a pairing prompt.
- WPF owned its Go subprocess; secret passed via stdin, no secret argument or global
  environment setting. An existing 1088 listener and old ADB forward were preserved;
  the UI selected its own free localhost SOCKS/forward ports instead.
- First WPF Internet test sent THREE CONCURRENT HTTPS requests to example.com:
  all HTTP200, each 577 response body bytes; totals 3.219116s, 3.521989s, 0.693407s.
- Same WPF test used SOCKS UDP to query 1.1.1.1:53: 5/5 validated DNS replies,
  query RTT min24.2 / median28.0 / max42.4 ms.
- Independent bounded diagnostic after an Android update: 3/3 certificate-verified
  HTTPS requests, 5/5 DNS replies; DNS median49.77ms, range44.55–71.06ms. HTTPS
  totals6.71368s,0.31292s,0.29764s. The slow first TLS exchange is retained in the
  report, not concealed or represented as low stable latency.
- Android live UI reported CELLULAR, Network212, rmnet0. Its actual relay counters
  showed uploaded5254 / downloaded19863 bytes after the test. Counters count
  relayed payload (including TLS records), not carrier-billed volume.
- Source resolves domains through selected cellular Network.getAllByName and
  uses its TCP SocketFactory; UDP binds the socket to the same Network. Windows
  diagnostics connected only to the new loopback SOCKS endpoint, using remote
  SOCKS hostname resolution; baseline Wi-Fi results were not counted as USB.
- Controlled channel-failure test: removed ONLY this WPF instance's owned data
  forward. Within its five-second poll it detected the missing channel, stopped
  its own engine, recreated owned channels and reauthenticated without consent.
  A missing-forward cleanup message was logged, and recovery succeeded.
- Final installed UI test AFTER recovery: 3 concurrent HTTP200 results (577 bytes
  each), totals2.297701s,0.861230s,0.859707s; UDP DNS5/5, min25.4 / median30.5 /
  max70.3ms. This is recovery from channel loss, NOT a physical unplug test.
- Final direct Wi-Fi baseline HTTP200. Final DNS snapshot identical to the stage1
  starting snapshot. PdaNet, MyPublicWiFi and other adapters unchanged by this code.

## Builds and automated checks

- APK0.2.0-usb-preview built with API35; v2/v3 signature verification passed.
- Windows WPF build and self-contained x64 publish passed. .NET is included.
- Go vet and28 passing named Java/Go cases (8 top-level +20 subtests).
- Seven offline DPAPI/pairing checks passed: consent, repeat connection, changed
  identity rejection, user rejection, modified commitment rejection, GCM tamper
  rejection and forget requiring consent. This fake-peer suite is separate from
  the actual cross-language Android/Windows device pairing described above.
- Five Python diagnostic safety tests passed. Total40 named automated cases.
- Race detector remains unavailable locally: compatible C compiler not installed.
- New CI Windows WPF/pairing step added; its definition is not evidence of a run.
- Per-user MSI built with WiX6.0.2; no CustomAction table. Install exit0,
  uninstall exit0, app removal verified, DPAPI identity retained, reinstall exit0.
  Installed Start-menu shortcut and actual installed WPF executable verified.
- Signed official Wintun0.14.1 archive SHA256 and WireGuard LLC DLL Authenticode
  signature verified. Separate engineering probe compiled and default dry-run
  passed as a non-elevated user. NO Wintun DLL loaded or adapter/driver created.

Raw private/local reports: build/reports/device-stage1/, go-final.txt,
pairing-offline.txt, python-safety.txt, msi-*.log, wintun-dry-run.txt. The installed
UI also saves test reports under LocalAppData/TetherMax/reports. No private keys,
bearer codes or live SAS values are committed in this record.

## Not completed

Physical cable unplug/replug, full sustained throughput/packet-loss testing,
IPv6, complete Android background-service lifecycle, multiple-phone use,
automatic ADB installation/version coexistence, hostile authorized ADB host
isolation and independent cryptographic audit. Existing persistent ECDH has no
forward secrecy. Full TUN/IP stack, rollback watchdog, gaming/process mapping,
fair packet scheduling, adaptive SQM, loaded-latency A/B, shared TV/ICS routing,
real-time Windows traffic graphs, tray/startup support and light Windows theme
are not implemented. USB transfer works while the hotspot is on; hotspot-off
behavior was not physically tested, so complete hotspot independence is not
certified by this run.
