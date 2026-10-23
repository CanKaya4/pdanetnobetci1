package ipengine

import (
	"context"
	"encoding/binary"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/bridge"
	"io"
	"net"
	"strings"
	"testing"
	"time"
)

func TestFramedPacketDeviceBoundaries(t *testing.T) {
	a, b := net.Pipe()
	defer a.Close()
	defer b.Close()
	sender := &FramedDevice{Stream: a}
	receiver := &FramedDevice{Stream: b}
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	payload := ipv4Packet()
	done := make(chan error, 1)
	go func() { done <- sender.WritePacket(ctx, payload) }()
	got, err := receiver.ReadPacket(ctx)
	if err != nil || string(got) != string(payload) {
		t.Fatalf("frame mismatch %v", err)
	}
	if err = <-done; err != nil {
		t.Fatal(err)
	}
}
func TestPacketPumpCancellationAndParentCrash(t *testing.T) {
	for _, mode := range []string{"cancel", "parent-eof", "truncated-packet"} {
		t.Run(mode, func(t *testing.T) {
			e, err := New(Config{Transport: bridge.Transport{Address: "127.0.0.1:1", Token: strings.Repeat("a", 32)}})
			if err != nil {
				t.Fatal(err)
			}
			a, b := net.Pipe()
			defer b.Close()
			ctx, cancel := context.WithCancel(context.Background())
			defer cancel()
			done := make(chan error, 1)
			go func() { done <- e.Run(ctx, &FramedDevice{Stream: a}) }()
			switch mode {
			case "cancel":
				cancel()
			case "parent-eof":
				b.Close()
			case "truncated-packet":
				go func() {
					var size [2]byte
					binary.BigEndian.PutUint16(size[:], 40)
					b.Write(size[:])
					b.Write([]byte{1, 2})
					b.Close()
				}()
			}
			select {
			case <-done:
			case <-time.After(time.Second):
				t.Fatal("packet pump or flow goroutine leaked")
			}
			if e.Counters().Active != 0 {
				t.Fatal("flow state survived teardown")
			}
			if err = e.Close(); err != nil {
				t.Fatal(err)
			}
		})
	}
}
func TestInvalidPacketFrameRefused(t *testing.T) {
	a, b := net.Pipe()
	defer a.Close()
	defer b.Close()
	go func() { b.Write([]byte{0, 1, 0}); b.Close() }()
	_, err := (&FramedDevice{Stream: a}).ReadPacket(context.Background())
	if err == nil || err == io.EOF {
		t.Fatal("short frame should be rejected explicitly")
	}
}
