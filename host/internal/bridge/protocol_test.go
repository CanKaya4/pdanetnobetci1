package bridge

import (
	"bytes"
	"testing"
)

func TestAddressRoundTrip(t *testing.T) {
	for _, host := range []string{"127.0.0.1", "::1", "a", "game", "sixteenlettersxx", "example.org"} {
		t.Run(host, func(t *testing.T) {
			got, port, err := readAddress(bytes.NewReader(encodeAddress(host, 443)))
			if err != nil || got != host || port != 443 {
				t.Fatalf("got %q:%d %v", got, port, err)
			}
		})
	}
}
func TestSafetyValidation(t *testing.T) {
	for _, address := range []string{"0.0.0.0:1088", "192.168.1.1:1088", "localhost:1088", "[::]:1088", "127.0.0.1:65536", "127.0.0.1:-1"} {
		if LoopbackAddress(address) == nil {
			t.Errorf("accepted %q", address)
		}
	}
	for _, token := range []string{"", "short", "0000000000000000000000000000000G"} {
		if (Transport{Address: "127.0.0.1:1", Token: token}).Validate() == nil {
			t.Error("accepted invalid token")
		}
	}
}
func TestInvalidAddress(t *testing.T) {
	for _, b := range [][]byte{nil, {2}, {3, 0}, {1, 127}, {4, 0}, {3, 3, 'a'}} {
		if _, _, err := readAddress(bytes.NewReader(b)); err == nil {
			t.Errorf("accepted %v", b)
		}
	}
}
