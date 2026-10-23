# TetherMax development

Develop and validate in Codex Cloud using the existing checkout. Do not create
Git worktrees unless the user explicitly requests one.

## Mandatory safety boundary

The user's Windows PC currently accesses the Internet through PdaNet.
Never modify or stop PdaNet, MyPublicWiFi, network adapters, DNS, routing,
system proxy, firewall, drivers or services on that PC. Do not run ADB against
physical devices, install an APK there or perform real phone/Windows/mobile
network tests without separate explicit user approval for that stage.
Cloud loopback fixtures and GitHub-hosted runner tests are permitted.
No implicit installation/startup, privileged driver loading or auto-connect.

## Build and validation

- `python scripts/bootstrap-cloud.py` installs verified tools in /workspace/toolchains.
- `scripts/test.sh` freshly compiles the shared Java engine and runs Go vet and
  race-enabled tests against a real Java relay over loopback.
- `scripts/build-android.sh` builds and verifies a development-signed APK.
- `scripts/build-host.sh` builds Windows x64/ARM64 CLI binaries and ZIP packages.
- Fixtures must stay loopback-only; never substitute public connectivity tests.
- Preserve TLS, signature and checksum validation. Never store tokens in source,
  command-line arguments, reports or saved cloud settings.

## Delivery and claims

Prove the USB transfer engine on approved hardware before adding performance
optimizations. No made-up measurements for Samsung A56 or Türk Telekom 5G.
No claims to defeat tariffs, quotas, Vanguard/anti-cheat or carrier policy.
Distinguish loopback relay tests, native Windows tests, APK builds, Android
runtime tests and real USB/cellular tests. Only completed checks count as passed.
Changes to application source are authorized by the user's development request;
onboarding alone would not authorize them.
