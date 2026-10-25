package traffic

import (
	"testing"
	"time"
)

func TestOutageBreaksAdaptiveCongestionEvidence(t *testing.T) {
	m := manager(t, Settings{Mode: Gaming, UploadMbps: 10, DownloadMbps: 100, Adaptive: true})
	now := time.Now()
	sample := func(ok bool, delay time.Duration) {
		now = now.Add(time.Second)
		m.addBytes(false, 1100000)
		m.Observe(now, delay, ok)
	}
	for range 5 {
		sample(true, 20*time.Millisecond)
	}
	sample(true, 100*time.Millisecond)
	sample(true, 100*time.Millisecond)
	sample(false, 0)
	sample(true, 100*time.Millisecond)
	if m.Snapshot().UploadCeilingMbps != 10 {
		t.Fatal("congestion evidence crossed an outage")
	}
	sample(true, 100*time.Millisecond)
	sample(true, 100*time.Millisecond)
	s := m.Snapshot()
	if s.UploadCeilingMbps >= 10 || s.DownloadCeilingMbps != 100 {
		t.Fatalf("loaded direction backoff failed: %+v", s)
	}
}

func TestAdaptiveRequiresSustainedLoadInTheSameDirection(t *testing.T) {
	m := manager(t, Settings{Mode: Gaming, UploadMbps: 100, DownloadMbps: 100, Adaptive: true})
	now := time.Now()
	for range 5 {
		now = now.Add(time.Second)
		m.Observe(now, 20*time.Millisecond, true)
	}
	for i := 0; i < 6; i++ {
		now = now.Add(time.Second)
		m.addBytes(i%2 == 0, 10000000)
		m.Observe(now, 120*time.Millisecond, true)
	}
	if s := m.Snapshot(); s.UploadCeilingMbps != 100 || s.DownloadCeilingMbps != 100 {
		t.Fatalf("alternating short bursts mistaken for persistent congestion: %+v", s)
	}
	for range 3 {
		now = now.Add(time.Second)
		m.addBytes(true, 10000000)
		m.Observe(now, 120*time.Millisecond, true)
	}
	if s := m.Snapshot(); s.UploadCeilingMbps != 100 || s.DownloadCeilingMbps >= 100 {
		t.Fatalf("sustained downlink was not isolated: %+v", s)
	}
}

func TestInvalidObservationPreservesTheByteWindow(t *testing.T) {
	m := manager(t, Settings{Mode: Daily, Adaptive: true})
	now := m.sampleAt
	m.addBytes(false, 1000000)
	m.Observe(now.Add(time.Second), 0, true)
	m.Observe(now.Add(-time.Second), 20*time.Millisecond, true)
	if !m.sampleAt.Equal(now) || len(m.observations) != 0 {
		t.Fatal("invalid sample consumed measurement window")
	}
	m.Observe(now.Add(2*time.Second), 20*time.Millisecond, true)
	if len(m.observations) != 1 || m.observations[0].upMbps != 4 {
		t.Fatal("valid byte measurement was lost")
	}
}
