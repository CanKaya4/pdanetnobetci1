# Android security/UI increment — 2026-10-23

This is a development increment, NOT TetherMax 1.0 or completion of the product request.

Implemented:
- AndroidKeystore AES-GCM wrapping of the existing 128-bit pairing secret.
- Atomic migration from legacy plaintext preferences; the existing secret is retained.
- Fail closed on missing wrapping keys, invalid records or authentication failure.
- Pairing secret hidden by default; explicit reveal dialog clears text on dismissal.
- FLAG_SECURE prevents screenshots/recent-task capture of the secret screen.
- Turkish card-based screen using the system light/dark theme, functional service
  start/stop and explicit unavailable-analysis/product-scope descriptions.
- Working shared Java/Go relay retained. No simulated metrics or mode toggles added.

Validation:
- Android API 35 compilation and dex generation succeeded using local JBR 25.
- APK Signature Scheme v2/v3 verified.
- Update installed successfully on Galaxy A56 Android 16.
- Activity launch and foreground service start observed after upgrade.
- Android Keystore read/migration was invoked by the start button before the
  service started. Persistence across process death and corruption recovery have
  not been independently tested. No claim of complete pairing-security coverage.
- Refreshed Java compile, go vet and all 27 Go/Java test cases passed locally.
- Windows route remained Wi-Fi. No adapters, global DNS/proxy/routes changed.

Not delivered:
- Six-digit ECDH/SAS handshake, per-computer trust/revocation and Windows DPAPI.
- Windows GUI/installer and automatic connection management.
- Physical end-to-end USB HTTPS/UDP measurements.
- System-wide TUN, gaming/streaming modes, adaptive shaping and telemetry.
- Compose/Material 3 migration, UI automation coverage and complete UX review.

Artifact: build/artifacts/tethermax-android-secure-preview.apk (development signed).
The old long-code protocol remains in effect; this change is storage hardening,
not an implementation of automatic pairing. The APK must not be labelled 1.0.
