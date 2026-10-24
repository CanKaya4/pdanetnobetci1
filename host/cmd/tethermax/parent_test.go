package main

import (
	"context"
	"encoding/binary"
	"io"
	"strings"
	"testing"
	"time"
)

func TestParentWatchdog(t *testing.T) {
	for _, mode := range []string{"crash", "invalid", "stalled", "cancel"} {
		t.Run(mode, func(t *testing.T) {
			r, w := io.Pipe()
			defer w.Close()
			ctx, cancel := context.WithCancel(context.Background())
			defer cancel()
			done := make(chan error, 1)
			go func() { done <- watchParent(ctx, r, 40*time.Millisecond) }()
			switch mode {
			case "crash":
				w.Close()
			case "invalid":
				w.Write([]byte{7})
			case "cancel":
				cancel()
			}
			select {
			case err := <-done:
				if err == nil {
					t.Fatal("missing watchdog failure")
				}
				if mode == "stalled" && !strings.Contains(err.Error(), "timed out") {
					t.Fatal(err)
				}
			case <-time.After(time.Second):
				t.Fatal("heartbeat reader leaked")
			}
			if _, err := w.Write([]byte{0}); err == nil {
				t.Fatal("private pipe not closed")
			}
		})
	}
}

func TestNativeSettingsFramesPreserveHeartbeatAndRejectPartial(t *testing.T) {
	r, w := io.Pipe()
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	defer w.Close()
	changed := make(chan string, 1)
	done := make(chan error, 1)
	go func() {
		done <- watchNativeParent(ctx, r, 200*time.Millisecond, func(data []byte) error {
			_, err := decodeTrafficSettings(data)
			if err == nil {
				changed <- string(data)
			}
			return err
		})
	}()
	data := []byte(`{"mode":"gaming","uploadMbps":12,"downloadMbps":50,"adaptive":true,"dnsProfile":"adguard"}`)
	frame := append(binary.BigEndian.AppendUint16([]byte{0, 1}, uint16(len(data))), data...)
	frame = append(frame, 0)
	if _, err := w.Write(frame); err != nil {
		t.Fatal(err)
	}
	select {
	case <-changed:
	case <-time.After(time.Second):
		t.Fatal("live settings not delivered")
	}
	if _, err := w.Write([]byte{1, 8, 1}); err != nil {
		t.Fatal(err)
	}
	select {
	case err := <-done:
		if err == nil {
			t.Fatal("oversize frame accepted")
		}
	case <-time.After(time.Second):
		t.Fatal("reader leaked")
	}
}
