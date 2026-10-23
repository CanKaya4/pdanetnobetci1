# Validation record — engine preview

## Local Galaxy A56 preparation — 2026-10-23

On the user's Windows 11 PC, the selected Internet route was confirmed to use
Wi-Fi and adapter/route/DNS snapshots were saved locally under ignored
`build/reports/device-stage1/`. A normal Wi-Fi HTTPS baseline returned HTTP 200;
this is not a USB test result. PdaNet's adapter was disconnected.

ADB found the authorized USB Galaxy A56 SM-A566B, Android 16 / API 36.
The previously locally built development APK installed successfully. Its activity
started and its foreground service was observed running. The UI reported cellular
ready; Android connectivity state recorded the app's CELLULAR-only network request
on the mobile Internet network (reported LTE at observation time, not a measured
5G session). The source explicitly uses that Network's DNS/TCP socket factory and
bindSocket for UDP.

An automatic pairing-code extraction/start command was rejected by the local
execution policy. No pairing, ADB forward, Windows SOCKS listener or cellular
TCP/UDP Internet result was completed in this stage. The user chose to enter the
code privately in a local terminal instead. `scripts/connect-device.ps1` prepares
that manual step; `scripts/device-smoke.py` prepares bounded HTTPS/UDP DNS probes.
Five offline safety checks for the diagnostic passed, and the PowerShell script
parsed successfully. Actual physical latency/loss/throughput results are pending.
See [local instructions](local-device-testing.md). No adapter, global proxy, DNS,
default route, hotspot, PdaNet, MyPublicWiFi or USB mode was changed.

## Prior cloud and hosted-runner evidence

Current-instance checks in Codex Cloud on 2026-10-23:

- Verified Go and Temurin archives against upstream SHA256; Android command-line
  tools against Google's repository checksum before extraction, with a pinned
  SHA256 retained in bootstrap. No verification bypasses.
- `python scripts/bootstrap-cloud.py`: completed using writable cloud SDK cache;
  installed Android API 35 revision 2 and build-tools 35.0.0. SDK manager initially
  could not write its default home cache; setting ANDROID_USER_HOME fixed setup.
- `scripts/test.sh`: fresh Java compile, `go vet`, actual Go→Java TCP/UDP relay
  integration and race detector passed. Tests exercise binary transfer (2 MiB),
  16 parallel flows, phone-side DNS/HTTP with loopback fixture, both TCP half-close
  directions, UDP empty/binary/48,000-byte packets, packet boundaries, malformed
  protocol, oversized frame rejection, authentication rejection, connection
  cancellation and inactive CLI defaults. Full raw report is shipped separately.
- A failing empty-UDP test exposed desktop Java dropping empty writes on connected
  datagram sockets. The engine now uses a fixed-destination unconnected socket
  with reply-source validation; the original failing test now passes.
- `scripts/build-android.sh`: APK built with aapt2/javac/d8/zipalign; APK Signature
  Scheme v2 and v3 verified with apksigner. Development signing key, not release
  production signing. App ID io.tethermax.app; min API 26, target API 35.
- `scripts/build-host.sh`: Windows PE x64 and ARM64 builds succeeded. The cloud
  Linux CLI's default exits inactive and serve refuses absent explicit approval.
  Windows ZIPs contain runtime license notices.

Independent GitHub-hosted native Windows and Linux checks both passed for engine
commit `5479609541b18087292db514b080537eb1f1ff47`:
https://github.com/CanKaya4/pdanetnobetci1/actions/runs/39969507113
This includes 27 passing cases when counting named subtests (7 top-level tests),
no failures and no skips. The Windows job executed the actual Java/Go loopback
suite natively and verified the Windows executable's inactive default and opt-in
safety gate. The Linux job independently built and verified the APK and compiled
the Windows distributions. CI used Temurin 21.0.12, while the prepared Codex Cloud
instance used Temurin 21.0.13. Workflow definitions alone are not evidence; the
linked completed run supplies that evidence. Preview tag builds repeat these
checks before releasing artifacts.

Not executed: Android emulator/device runtime, physical ADB/USB, actual cellular
DNS/Internet, Samsung A56, Türk Telekom 5G, PdaNet or MyPublicWiFi interoperability,
game/anti-cheat compatibility, Windows system-wide routing, bufferbloat, bitrate,
latency improvement or sustained mobile sessions. Cloud fixtures simulate the USB
stream transport, not the physical phone. Required physical checks remain gated
by separate approval and device access; this record does not certify a complete
end-to-end USB tethering product.
