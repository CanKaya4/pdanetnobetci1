# Separate approval boundary: isolated adapter lifecycle probe

Prepared tool: `build/wintun-probe/TetherMax.WintunProbe.exe`.
Default execution is dry-run only: it hashes the DLL, records a plan and current
adapter/gateway/DNS information, and exits without loading the DLL. This passed
with Elevated=false. This is a separate engineering tool, not the installed app.

The next proposed action, ONLY after human approval and administrator elevation:

1. Save adapter/gateway/DNS state and a fresh unique adapter name/GUID journal.
2. Load the pinned official signed amd64 Wintun0.14.1 DLL from an absolute path.
3. Create only `TetherMax-Test-<random>` and start a4MiB Wintun session.
4. End the session and close the created adapter in a finally block, including
   normal exception paths. Official WintunCloseAdapter removes an adapter created
   by WintunCreateAdapter. Compare snapshots and record whether it remains.

No IP assignment, default route, DNS, firewall, sharing, Wi-Fi, PdaNet or
MyPublicWiFi configuration is authored. It is an isolated adapter lifecycle
test on the current Windows host, NOT a VM, full tunnel, Internet takeover or
packet-forwarding verification. Driver installation can cause Windows network
device events; preservation of the working Internet must be observed.

Material limits: installing Wintun may leave a signed driver-store package after
the test adapter is removed. The tool deliberately never deletes a global driver
that other software might use. Power-failure/crash rollback is not yet certified;
normal/exception cleanup is implemented but cannot be called hardware-tested
until approved apply runs. Do not claim the full network rollback requirement
is complete. An independent test Windows VM/PC is preferable if available.

Archive SHA256:
`07c256185d6ee3652e09fa55c0b673e2624b565e02c4b9091c79ca7d2f24ef51`.
Verified amd64 DLL SHA256:
`E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE`.
Authenticode status: Valid, WireGuard LLC. The tool refuses a different hash.

References: [official download and checksum](https://www.wintun.net/),
[official API and close semantics](https://git.zx2c4.com/wintun/about/).
No apply or elevation was performed in preparing this plan.
