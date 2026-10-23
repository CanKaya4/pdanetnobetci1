package main

import (
	"context"
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
