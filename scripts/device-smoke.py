#!/usr/bin/env python3
"""Bounded physical-device smoke checks through an already running loopback SOCKS.

Never installs, starts ADB, changes OS networking or falls back to direct Internet.
Only three small HTTPS requests and five DNS queries are sent by default.
"""
import argparse
import ipaddress
import json
import secrets
import socket
import ssl
import statistics
import struct
import time
from datetime import datetime, timezone
from pathlib import Path


def read_exact(sock, size):
    result = bytearray()
    while len(result) < size:
        chunk = sock.recv(size - len(result))
        if not chunk:
            raise OSError("SOCKS peer closed the stream")
        result.extend(chunk)
    return bytes(result)


def address(host, port):
    try:
        ip = ipaddress.ip_address(host)
        return bytes([1 if ip.version == 4 else 4]) + ip.packed + struct.pack("!H", port)
    except ValueError:
        encoded = host.encode("idna")
        if not 0 < len(encoded) <= 255:
            raise ValueError("Invalid SOCKS hostname")
        return b"\x03" + bytes([len(encoded)]) + encoded + struct.pack("!H", port)


def read_address(sock):
    kind = read_exact(sock, 1)[0]
    if kind == 1:
        host = socket.inet_ntop(socket.AF_INET, read_exact(sock, 4))
    elif kind == 4:
        host = socket.inet_ntop(socket.AF_INET6, read_exact(sock, 16))
    elif kind == 3:
        host = read_exact(sock, read_exact(sock, 1)[0]).decode("ascii")
    else:
        raise OSError("Invalid SOCKS address type")
    return host, struct.unpack("!H", read_exact(sock, 2))[0]


def socks_request(endpoint, command, host, port):
    # The only TCP connection made by this diagnostic is to the local SOCKS server.
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.settimeout(8)
    try:
        sock.connect(endpoint)
        sock.sendall(b"\x05\x01\x00")
        if read_exact(sock, 2) != b"\x05\x00":
            raise OSError("SOCKS authentication negotiation failed")
        sock.sendall(bytes([5, command, 0]) + address(host, port))
        reply = read_exact(sock, 3)
        if reply != b"\x05\x00\x00":
            raise OSError(f"SOCKS request rejected: {reply.hex()}")
        bound = read_address(sock)
        return sock, bound
    except BaseException:
        sock.close()
        raise


def https_sample(endpoint):
    started = time.perf_counter()
    sock, _ = socks_request(endpoint, 1, "example.com", 443)
    connected = time.perf_counter()
    try:
        with ssl.create_default_context().wrap_socket(sock, server_hostname="example.com") as tls:
            secured = time.perf_counter()
            tls.sendall(b"GET / HTTP/1.1\r\nHost: example.com\r\nConnection: close\r\nAccept-Encoding: identity\r\n\r\n")
            chunks = bytearray()
            first = None
            while True:
                chunk = tls.recv(8192)
                if not chunk:
                    break
                if first is None:
                    first = time.perf_counter()
                chunks.extend(chunk)
                if len(chunks) > 65536:
                    raise OSError("HTTPS response exceeds 64 KiB smoke-test budget")
            finished = time.perf_counter()
        header, body = bytes(chunks).split(b"\r\n\r\n", 1)
        status = int(header.split(b"\r\n", 1)[0].split()[1])
        if status != 200 or b"Example Domain" not in body:
            raise OSError(f"Unexpected HTTPS response: status {status}")
        elapsed = finished - started
        return {"ok": True, "status": status, "response_bytes": len(chunks),
                "socks_connect_ms": round((connected - started) * 1000, 2),
                "tls_ms": round((secured - connected) * 1000, 2),
                "first_byte_ms": round((first - started) * 1000, 2),
                "total_ms": round(elapsed * 1000, 2),
                "small_response_mbps_including_setup": round(len(chunks) * 8 / elapsed / 1e6, 5)}
    finally:
        sock.close()


def dns_sample(udp, server):
    transaction = secrets.token_bytes(2)
    question = b"\x07example\x03com\x00\x00\x01\x00\x01"
    query = transaction + b"\x01\x00\x00\x01\x00\x00\x00\x00\x00\x00" + question
    packet = b"\x00\x00\x00" + address(server, 53) + query
    started = time.perf_counter()
    udp.send(packet)
    packet = udp.recv(65535)
    elapsed = (time.perf_counter() - started) * 1000
    if len(packet) < 10 or packet[:4] != b"\x00\x00\x00\x01":
        raise OSError("Expected unfragmented IPv4 SOCKS UDP response")
    if packet[4:8] != socket.inet_aton(server) or packet[8:10] != b"\x00\x35":
        raise OSError("DNS response came from a different endpoint")
    response = packet[10:]
    if len(response) < 12 + len(question) or response[:2] != transaction:
        raise OSError("Invalid DNS response transaction or length")
    flags, questions, answers = struct.unpack("!HHH", response[2:8])
    if not flags & 0x8000 or flags & 0x020F or questions != 1 or answers == 0:
        raise OSError("DNS error, truncation or empty answer")
    if response[12:12 + len(question)] != question:
        raise OSError("DNS question mismatch")
    return {"ok": True, "rtt_ms": round(elapsed, 2), "answer_count": answers,
            "response_bytes": len(response)}


def run(endpoint, dns_server):
    report = {"time_utc": datetime.now(timezone.utc).isoformat(),
              "socks": f"{endpoint[0]}:{endpoint[1]}", "https_host": "example.com",
              "dns_server": dns_server, "https": [], "udp_dns": [],
              "limits": ["Tiny smoke test; throughput is not link capacity.",
                         "DNS nonresponse rate is not general network packet loss.",
                         "TCP/HTTPS times are not ICMP ping or unloaded radio RTT.",
                         "Cellular binding must also be verified on Android."]}
    for _ in range(3):
        try:
            report["https"].append(https_sample(endpoint))
        except Exception as error:
            report["https"].append({"ok": False, "error": str(error)})
    control = udp = None
    try:
        control, bound = socks_request(endpoint, 3, "0.0.0.0", 0)
        if bound[0] != "127.0.0.1" or not 0 < bound[1] <= 65535:
            raise OSError("SOCKS UDP relay must use numeric IPv4 loopback")
        udp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        udp.bind(("127.0.0.1", 0))
        udp.connect(bound)
        udp.settimeout(3)
        for _ in range(5):
            try:
                report["udp_dns"].append(dns_sample(udp, dns_server))
            except Exception as error:
                report["udp_dns"].append({"ok": False, "error": str(error)})
            time.sleep(0.25)
    except Exception as error:
        report["udp_association_error"] = str(error)
    finally:
        if udp:
            udp.close()
        if control:
            control.close()
    samples = report["udp_dns"]
    times = [item["rtt_ms"] for item in samples if item["ok"]]
    report["udp_summary"] = {"attempted": len(samples), "received": len(times),
                             "nonresponse_percent": round(100 * (len(samples) - len(times)) / len(samples), 2) if samples else None,
                             "rtt_median_ms": round(statistics.median(times), 2) if times else None,
                             "rtt_min_ms": min(times) if times else None,
                             "rtt_max_ms": max(times) if times else None,
                             "mean_successive_rtt_difference_ms": round(statistics.mean(abs(a - b) for a, b in zip(times, times[1:])), 2) if len(times) > 1 else None}
    report["passed"] = (all(item["ok"] for item in report["https"])
                        and len(samples) == 5 and len(times) == 5)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--approve-device-test", action="store_true")
    parser.add_argument("--socks-port", type=int, default=1088)
    parser.add_argument("--dns-server", default="1.1.1.1")
    parser.add_argument("--output", type=Path, default=Path("build/reports/device-stage1/smoke.json"))
    args = parser.parse_args()
    if not args.approve_device_test:
        parser.error("Separate physical test approval is required: --approve-device-test")
    if not 1024 <= args.socks_port <= 65535:
        parser.error("Invalid SOCKS port")
    try:
        ipaddress.IPv4Address(args.dns_server)
    except ValueError:
        parser.error("DNS server must be an explicit IPv4 address")
    report = run(("127.0.0.1", args.socks_port), args.dns_server)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
