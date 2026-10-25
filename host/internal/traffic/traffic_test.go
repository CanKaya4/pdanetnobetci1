package traffic

import (
	"bytes"
	"context"
	"math"
	"testing"
	"time"
)

func manager(t *testing.T, s Settings) *Manager {
	t.Helper()
	m, err := New(s)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(m.Close)
	return m
}
func TestModePolicyManualAndAutomatic(t *testing.T) {
	m := manager(t, Settings{Mode: Auto})
	if m.Snapshot().Active != Daily {
		t.Fatal("idle auto mode")
	}
	f := m.Flow(context.Background(), Shared)
	if m.Snapshot().Active != Streaming {
		t.Fatal("shared clients not protected")
	}
	m.SetGames(DetectGames([]string{"PROJECT8.EXE", "GenshinImpact.exe", "VALORANT-Win64-Shipping.exe", "VALORANT.exe", "notepad.exe"}))
	if s := m.Snapshot(); s.Active != Gaming || len(s.Games) != 3 {
		t.Fatal(s)
	}
	for _, mode := range []Mode{Gaming, Streaming, Daily, Maximum} {
		if err := m.Configure(Settings{Mode: mode}); err != nil {
			t.Fatal(err)
		}
		m.SetGames([]string{"Valorant"})
		if m.Snapshot().Active != mode {
			t.Fatal("automatic detection overrode manual selection")
		}
	}
	f.Close()
}
func TestSettingsRejectInvalidRates(t *testing.T) {
	for _, s := range []Settings{{Mode: "invalid"},  {Mode: Daily, UploadMbps: math.NaN()}, {Mode: Daily, DownloadMbps: -1}} {
		if _, err := New(s); err == nil {
			t.Fatal("invalid settings accepted", s)
		}
	}
}
func TestGamingUDPBypassesQueuedBulkWithoutStarvation(t *testing.T) {
	m := manager(t, Settings{Mode: Gaming, UploadMbps: 0.08})
	seed := m.Flow(context.Background(), Bulk)
	defer seed.Close()
	if err := seed.Wait(false, 5000); err != nil {
		t.Fatal(err)
	}
	bulk := m.Flow(context.Background(), Bulk)
	defer bulk.Close()
	game := m.Flow(context.Background(), Bulk)
	defer game.Close()
	order := make(chan string, 2)
	go func() {
		if err := bulk.Wait(false, 1200); err == nil {
			order <- "bulk"
		}
	}()
	deadline := time.Now().Add(time.Second)
	for m.up.pending.Load() < 1 && time.Now().Before(deadline) {
		time.Sleep(time.Millisecond)
	}
	go func() {
		if err := game.Datagram(false, 1200); err == nil {
			order <- "udp"
		}
	}()
	for _, want := range []string{"udp", "bulk"} {
		select {
		case got := <-order:
			if got != want {
				t.Fatalf("got %s, want %s", got, want)
			}
		case <-time.After(2 * time.Second):
			t.Fatal("flow starved")
		}
	}
}
func TestPacingCancellationAndClosedDispatcher(t *testing.T) {
	m := manager(t, Settings{Mode: Daily, UploadMbps: 0.0001})
	f := m.Flow(context.Background(), Bulk)
	defer f.Close()
	if err := f.Wait(false, 1500); err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithCancel(context.Background())
	other := m.Flow(ctx, Bulk)
	defer other.Close()
	done := make(chan error, 1)
	go func() { done <- other.Wait(false, 1500) }()
	cancel()
	select {
	case err := <-done:
		if err == nil {
			t.Fatal("cancel ignored")
		}
	case <-time.After(time.Second):
		t.Fatal("paced wait leaked")
	}
	m.Close()
	if err := f.Wait(false, 1); err == nil {
		t.Fatal("closed dispatcher granted traffic")
	}
}
func TestTCPWriterActuallyPacesAndCountsPayload(t *testing.T) {
	m := manager(t, Settings{Mode: Gaming, UploadMbps: 0.8})
	f := m.Flow(context.Background(), Bulk)
	defer f.Close()
	var b bytes.Buffer
	payload := bytes.Repeat([]byte{0x5a}, 7200)
	start := time.Now()
	n, err := (Writer{Flow: f, Writer: &b}).Write(payload)
	if err != nil || n != len(payload) || !bytes.Equal(b.Bytes(), payload) {
		t.Fatal(n, err)
	}
	if time.Since(start) < 45*time.Millisecond {
		t.Fatal("rate ceiling had no effect")
	}
	s := m.Snapshot()
	if s.Uploaded != uint64(n) || s.Downloaded != 0 || s.Served < 6 || s.QueueP95Ms <= 0 {
		t.Fatal(s)
	}
}
func TestAdaptiveBackoffHysteresisAndIdleProtection(t *testing.T) {
	m := manager(t, Settings{Mode: Gaming, UploadMbps: 10, DownloadMbps: 100, Adaptive: true})
	now := time.Now()
	sample := func(rtt time.Duration, busy bool) {
		now = now.Add(time.Second)
		if busy {
			m.addBytes(false, 1100000)
		}
		m.Observe(now, rtt, true)
	}
	for range 5 {
		sample(20*time.Millisecond, false)
	}
	initial := m.Snapshot().UploadCeilingMbps
	for range 4 {
		sample(100*time.Millisecond, false)
	}
	if m.Snapshot().UploadCeilingMbps != initial {
		t.Fatal("idle server delay cut the rate")
	}
	for range 3 {
		sample(100*time.Millisecond, true)
	}
	backed := m.Snapshot().UploadCeilingMbps
	if backed >= initial || backed < 1 {
		t.Fatal("missing bounded congestion backoff", backed)
	}
	for range 9 {
		sample(20*time.Millisecond, false)
	}
	if m.Snapshot().UploadCeilingMbps != backed {
		t.Fatal("recovery hysteresis missing")
	}
	sample(20*time.Millisecond, false)
	if m.Snapshot().UploadCeilingMbps <= backed {
		t.Fatal("no slow recovery")
	}
	if err := m.Configure(Settings{Mode: Maximum, UploadMbps: 10, Adaptive: true}); err != nil {
		t.Fatal(err)
	}
	if m.Snapshot().UploadCeilingMbps != 10 {
		t.Fatal("maximum mode retained adaptive discount")
	}
}
func TestLossIsUnavailableUntilEnoughRealObservations(t *testing.T) {
	m := manager(t, Settings{Mode: Daily})
	now := time.Now()
	for range 19 {
		m.Observe(now, 20*time.Millisecond, true)
	}
	if m.Snapshot().ProbeLossPercent != nil {
		t.Fatal("invented short-window loss")
	}
	m.Observe(now, 0, false)
	s := m.Snapshot()
	if s.ProbeLossPercent == nil || *s.ProbeLossPercent != 5 {
		t.Fatal(s)
	}
}

func TestLiveCeilingChangeReleasesOldPacingDeadline(t *testing.T) {
	m := manager(t, Settings{Mode: Daily, UploadMbps: 0.0001})
	f := m.Flow(context.Background(), Bulk)
	defer f.Close()
	if err := f.Wait(false, 1500); err != nil {
		t.Fatal(err)
	}
	done := make(chan error, 1)
	go func() { done <- f.Wait(false, 1500) }()
	deadline := time.Now().Add(time.Second)
	for m.up.pending.Load() == 0 && time.Now().Before(deadline) {
		time.Sleep(time.Millisecond)
	}
	if err := m.Configure(Settings{Mode: Maximum}); err != nil {
		t.Fatal(err)
	}
	select {
	case err := <-done:
		if err != nil {
			t.Fatal(err)
		}
	case <-time.After(time.Second):
		t.Fatal("profile change retained the 120-second pacing debt")
	}
}

func TestStaleSuccessAndNetworkResetDoNotInventLiveDelay(t *testing.T) {
	m := manager(t, Settings{Mode: Daily, UploadMbps: 10, Adaptive: true})
	m.Observe(time.Now().Add(-6*time.Second), 20*time.Millisecond, true)
	m.Observe(time.Now(), 0, false)
	if m.Snapshot().QueryRTTMs != nil {
		t.Fatal("failure refreshed stale successful RTT")
	}
	m.ResetMeasurements()
	s := m.Snapshot()
	if s.ProbeSamples != 0 || s.QueryRTTMs != nil || s.JitterMs != nil || s.UploadCeilingMbps != 10 {
		t.Fatal(s)
	}
}
