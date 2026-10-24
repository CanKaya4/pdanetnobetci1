"""Offline safety and wire-format checks. No physical device or Internet needed."""
import importlib.util
import socket
import struct
import subprocess
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("device_smoke", Path(__file__).with_name("device-smoke.py"))
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)


class FakeSocket:
    def __init__(self, response):
        self.response = response
        self.connected = None
        self.sent = bytearray()
        self.closed = False

    def settimeout(self, value):
        pass

    def connect(self, endpoint):
        self.connected = endpoint

    def sendall(self, data):
        self.sent.extend(data)

    def recv(self, size):
        chunk, self.response = self.response[:size], self.response[size:]
        return chunk

    def close(self):
        self.closed = True


class FakeUDP:
    def __init__(self, wrong_source=False, wrong_transaction=False):
        self.wrong_source = wrong_source
        self.wrong_transaction = wrong_transaction

    def send(self, packet):
        self.query = packet[10:]

    def recv(self, size):
        source = "8.8.8.8" if self.wrong_source else "1.1.1.1"
        transaction = b"xx" if self.wrong_transaction else self.query[:2]
        response = transaction + struct.pack("!HHHHH", 0x8180, 1, 1, 0, 0) + self.query[12:]
        response += b"\xc0\x0c\x00\x01\x00\x01\x00\x00\x00\x3c\x00\x04\x7f\x00\x00\x01"
        return b"\x00\x00\x00" + smoke.address(source, 53) + response


class DeviceSmokeSafetyTests(unittest.TestCase):
    def test_socks_forwards_hostname_without_direct_dns_or_connect(self):
        fake = FakeSocket(b"\x05\x00\x05\x00\x00\x01\x7f\x00\x00\x01\x12\x34")
        with patch.object(socket, "socket", return_value=fake), patch.object(socket, "getaddrinfo", side_effect=AssertionError("local DNS forbidden")):
            sock, bound = smoke.socks_request(("127.0.0.1", 1088), 1, "example.com", 443)
        self.assertIs(sock, fake)
        self.assertEqual(fake.connected, ("127.0.0.1", 1088))
        self.assertIn(b"\x03\x0bexample.com\x01\xbb", fake.sent)
        self.assertEqual(bound, ("127.0.0.1", 4660))

    def test_proxy_rejection_closes_socket_without_fallback(self):
        fake = FakeSocket(b"\x05\xff")
        with patch.object(socket, "socket", return_value=fake) as factory:
            with self.assertRaises(OSError):
                smoke.socks_request(("127.0.0.1", 1088), 1, "example.com", 443)
        factory.assert_called_once()
        self.assertTrue(fake.closed)

    def test_udp_dns_accepts_matching_response(self):
        self.assertTrue(smoke.dns_sample(FakeUDP(), "1.1.1.1")["ok"])

    def test_udp_dns_rejects_wrong_endpoint_or_transaction(self):
        for fixture in (FakeUDP(wrong_source=True), FakeUDP(wrong_transaction=True)):
            with self.subTest(fixture=vars(fixture)), self.assertRaises(OSError):
                smoke.dns_sample(fixture, "1.1.1.1")

    def test_cli_refuses_without_explicit_device_flag(self):
        result = subprocess.run([sys.executable, str(Path(__file__).with_name("device-smoke.py"))], capture_output=True, text=True)
        self.assertEqual(result.returncode, 2)
        self.assertIn("Separate physical test approval", result.stderr)


if __name__ == "__main__":
    unittest.main()
