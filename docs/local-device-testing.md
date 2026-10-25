# Local Galaxy A56 smoke test

Physical tests require explicit approval for the chosen phone and PC. Neither
script installs drivers, changes adapters/routes/DNS/proxy, stops PdaNet, nor
configures MyPublicWiFi. Never print or save the pairing code in reports.

Prerequisites: the Android APK is installed, its cellular engine is started,
USB debugging is authorized, and the existing Internet route still uses Wi-Fi.
The Windows x64 executable must be built under `build/artifacts/`.

In a separate PowerShell terminal, from this checkout, enter the phone's pairing
code at the hidden prompt. Do not send the code through chat or pass it on the
command line:

```powershell
& .\scripts\connect-device.ps1 -Serial YOUR_DEVICE_SERIAL -ApproveDeviceTest
```

The script refuses occupied ports and uses `adb forward --no-rebind` for a
single selected device. TetherMax listens on `127.0.0.1:1088`; the ADB transport
endpoint is `127.0.0.1:38473`. Keep the terminal open while testing. Ctrl+C closes
TetherMax, removes only this script's forward and clears/restores the process
pairing environment. If the terminal is forcibly terminated, inspect and remove
only its forward manually; never run `adb kill-server` or `forward --remove-all`.
ADB's forward command has no bind-address option, so verify the actual Windows
listener is loopback-only before sending test traffic. If it is not, stop the
test and remove only the created forward.

With separately approved physical testing and the local listener ready:

```powershell
python scripts/device-smoke.py --approve-device-test
```

This uses the Python standard library, verified TLS certificates and no system
proxy. It opens no direct external socket: HTTPS domain names are resolved by
the Android relay. Three HTTPS GETs to example.com are limited to 64 KiB each;
five ordinary A-record DNS queries go through SOCKS UDP to 1.1.1.1:53. This is a
bounded connectivity check, not a bandwidth or load test. It is neither a
general UDP echo/MTU test nor an IPv6 or gaming test.

The JSON report records each failure and successful DNS RTT, successive RTT
variation and nonresponse count. HTTPS setup/response timings and tiny-response
throughput are not sustained link speed. Five responses cannot establish a
reliable long-term packet loss figure. Read Android connectivity/service state
alongside the report to verify CELLULAR binding; the PC Wi-Fi baseline is never
counted as a USB result. Check the Wi-Fi route and baseline HTTPS before/after;
stop TetherMax if the existing Internet stops working.

Offline checks, with no physical traffic:

```powershell
python -m unittest discover -s scripts -p test_device_smoke.py -v
```

System-wide TUN and traffic shaping remain separate development and approval
stages. A successful smoke test does not certify those features.
