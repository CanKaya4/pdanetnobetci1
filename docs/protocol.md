# TMAX USB flow protocol v1

Transport: one ADB-forwarded stream per TCP connection or fixed-destination UDP
flow. Phone listens on abstract Unix socket `io.tethermax.usb.v1`, not Wi-Fi or
cellular TCP. Windows accepts SOCKS5 on a literal loopback address only.
ADB requires an authorized physical USB debugging host. The application adds a
random 128-bit pairing code, compared in constant time by the phone.
Pairing code is local authentication, not encryption. Trust the USB host and
Android's ADB authorization. Any local desktop process may access loopback
SOCKS while serve runs; multiuser isolation is not provided in this preview.

Handshake, big endian:

| Field | Bytes |
|---|---|
| Magic `TMAX` | 4 |
| Version `1` | 1 |
| Pairing code (lowercase hex ASCII) | 32 |
| Mode (`1` TCP, `2` UDP) | 1 |
| ASCII hostname length (1..253) | 2 |
| Hostname or literal IP | variable |
| Destination port (1..65535) | 2 |

Phone returns one status byte: 0 connected, 1 destination error, 2 authentication
failure, 3 malformed request. Bad magic/version closes without a response.
No payload is sent until success. DNS is resolved by the selected Android
cellular `Network`, not the desktop resolver. TCP uses cellular SocketFactory;
UDP is bound to the same Network before binding a local socket address.

TCP: raw full-duplex bytes, with independent half-close propagation. Connect and
handshake deadlines do not impose a session duration limit. Shutdown closes live
sockets. There is no subscription, expiration timer or PdaNet dependency.

UDP: repeated `uint16 length || payload` frames in each direction. Lengths above
65507 close the flow. Empty packets are preserved. The unconnected Android/Java
socket validates the reply source against the fixed resolved destination.
This also avoids desktop Java's connected-socket zero-length write limitation.
Windows adds/removes standard SOCKS5 UDP headers and rejects fragmented SOCKS
packets. UDP associations live until their SOCKS TCP control closes. Each is
pinned to the first valid loopback UDP source, at most 64 destination flows.
Inactive destination flows expire after 60 seconds; active traffic refreshes
activity. This is a resource timeout, not an application use-duration limit.

Limits: 128 accepted SOCKS control connections; 128 phone relay flows. Excess
flows are rejected/closed. No unbounded application packet queue. TCP provides
backpressure. UDP framed over TCP is not equivalent to loss-tolerant native UDP:
USB/TCP loss may cause head-of-line delay within a flow. No game latency claim.
SOCKS handles TCP CONNECT and UDP ASSOCIATE only; no ICMP, multicast, SOCKS BIND,
UDP fragmentation/reassembly, system TUN or transparent OS-wide forwarding yet.
