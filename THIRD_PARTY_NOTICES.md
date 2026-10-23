# TetherMax 0.1 engine notices

Original TetherMax source is MIT licensed; see LICENSE.
Windows binaries contain the Go runtime and standard library, including vendored
Go x/crypto, x/net, x/sys and x/text code as selected by the Go compiler.
Their BSD licenses and patent grants are retained under docs/licenses and shipped
in the Windows ZIP distributions. No third-party Go module dependency is used.

Research references (not bundled, linked, translated or copied into this release):

- OpenTether, HelaFaye/opentether: MIT, Copyright 2026 Alisha Faye.
- CAKE-autorate, lynxthecat/cake-autorate: GPL v2 license text.
- sqm-autorate, sqm-autorate/sqm-autorate: MPL 2.0.

See docs/research.md for pinned revisions and platform limitations. The ADB
forwarding approach is a design reference; upstream shell/Lua code is not
incorporated. In particular, TetherMax does not contain the CAKE scheduler or
claim to implement it. Any future incorporation must retain the relevant notices,
source availability and license terms; it requires a separate dependency review.

Build-only tools: Eclipse Temurin OpenJDK (GPLv2 with Classpath Exception),
Android SDK/build tools (their respective SDK terms), Go (BSD), GitHub Actions
(their upstream licenses). They are not included in application downloads.
ADB/platform-tools and Windows USB drivers are NOT bundled.
