package main

import (
	"context"
	"flag"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/bridge"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/traffic"
	"io"
	"testing"
	"time"
)

func TestPrivateTrafficSettingsAndSharedClassifierValidation(t *testing.T) {
	for _, record := range []string{`{"mode":"gaming","uploadMbps":10,"downloadMbps":50,"adaptive":true}`, `{"mode":"auto"}`} {
		if _, err := decodeTrafficSettings([]byte(record)); err != nil {
			t.Fatal(err)
		}
	}
	for _, record := range []string{`{"mode":"gaming","uploadMbps":0,"adaptive":true}`, `{"mode":"daily","extra":1}`, `{"mode":"daily"}{"mode":"gaming"}`} {
		if _, err := decodeTrafficSettings([]byte(record)); err == nil {
			t.Fatal("invalid private record accepted", record)
		}
	}
	f := addTrafficFlags(flag.NewFlagSet("fixture", flag.ContinueOnError))
	for _, prefix := range []string{"0.0.0.0/0", "1.1.1.0/24", "bad", "::/0"} {
		*f.shared = prefix
		if _, err := f.prefixes(); err == nil {
			t.Fatal("invalid shared classifier", prefix)
		}
	}
	*f.shared = "192.168.137.0/24,10.77.0.0/24"
	if p, err := f.prefixes(); err != nil || len(p) != 2 {
		t.Fatal(p, err)
	}
}

func TestLivePrivateProfileUpdateAndParentEOF(t *testing.T) {
	f := addTrafficFlags(flag.NewFlagSet("fixture", flag.ContinueOnError))
	*f.control = true
	m, err := f.manager()
	if err != nil {
		t.Fatal(err)
	}
	defer m.Close()
	r, w := io.Pipe()
	defer w.Close()
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	stop := startTrafficRuntime(ctx, cancel, f, m, bridge.Transport{Address: "127.0.0.1:1", Token: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}, r, nil)
	defer stop()
	if _, err = io.WriteString(w, "{\"mode\":\"streaming\",\"uploadMbps\":5,\"adaptive\":false}\n"); err != nil {
		t.Fatal(err)
	}
	deadline := time.Now().Add(time.Second)
	for m.Settings().Mode != traffic.Streaming && time.Now().Before(deadline) {
		time.Sleep(time.Millisecond)
	}
	if m.Settings().Mode != traffic.Streaming {
		t.Fatal("live profile command not applied")
	}
	w.Close()
	select {
	case <-ctx.Done():
	case <-time.After(time.Second):
		t.Fatal("parent EOF failed to stop child")
	}
}
