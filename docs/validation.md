# Validation record — engine preview

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

GitHub-hosted native Windows and independent Linux checks are recorded by the
repository's Actions workflow. Their results must be read from a completed run;
workflow definitions alone are not passing evidence.

Not executed: Android emulator/device runtime, physical ADB/USB, actual cellular
DNS/Internet, Samsung A56, Türk Telekom 5G, PdaNet or MyPublicWiFi interoperability,
game/anti-cheat compatibility, Windows system-wide routing, bufferbloat, bitrate,
latency improvement or sustained mobile sessions. Cloud fixtures simulate the USB
stream transport, not the physical phone. Required physical checks remain gated
by separate approval and device access; this record does not certify a complete
end-to-end USB tethering product.
