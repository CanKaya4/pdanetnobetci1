package traffic

import (
	"context"
	"encoding/binary"
	"encoding/json"
	"strings"
	"sync"
	"testing"
	"time"
)

func TestCoDelLowRateTargetAllowsSerialization(t *testing.T) {
	var c codel
	now := time.Now()
	// 1500 bytes at 100 kbit/s need 120ms; 20ms is not queue bloat there.
	for i := 0; i < 100; i++ {
		if c.dropTarget(now.Add(time.Duration(i)*time.Second), 20*time.Millisecond, 9000, 120*time.Millisecond) {
			t.Fatal("serialization time treated as congestion")
		}
	}
	if c.dropTarget(now, 150*time.Millisecond, 9000, 120*time.Millisecond) {
		t.Fatal("transient queue dropped")
	}
	if !c.dropTarget(now.Add(101*time.Millisecond), 150*time.Millisecond, 9000, 120*time.Millisecond) {
		t.Fatal("persistent delay above low-rate target not signalled")
	}
}

func BenchmarkUsageAccounting(b *testing.B) {
	m, _ := New(Settings{Mode: Daily})
	defer m.Close()
	p := packet(7, 100, 1500)
	m.recordHostPacket(p, false)
	b.ReportAllocs()
	b.ResetTimer()
	for i := 0; i < b.N; i++ {
		m.recordHostPacket(p, false)
	}
}

func TestUsageOnlyReleasedPacketsAndIndependentSnapshot(t *testing.T) {
	m := manager(t, Settings{Mode: Daily})
	q := m.PacketQueue(false)
	q.Enqueue(packet(7, 100, 1500))
	if len(m.Snapshot().HostUsage) != 0 {
		t.Fatal("queued bytes billed before release")
	}
	if _, err := q.Read(context.Background()); err != nil {
		t.Fatal(err)
	}
	down := m.PacketQueue(true)
	b := packet(7, 100, 1000)
	copy(b[16:20], []byte{10, 0, 0, 7})
	down.Enqueue(b)
	if _, err := down.Read(context.Background()); err != nil {
		t.Fatal(err)
	}
	s := m.Snapshot()
	if len(s.HostUsage) != 1 || s.HostUsage[0].Address != "10.0.0.7" || s.HostUsage[0].Uploaded != 1500 || s.HostUsage[0].Downloaded != 1000 {
		t.Fatal(s.HostUsage)
	}
	s.HostUsage[0].Uploaded = 0
	if m.Snapshot().HostUsage[0].Uploaded != 1500 {
		t.Fatal("snapshot mutated accounting")
	}
	q.Enqueue(packet(7, 100, 500))
	q.Close()
	if m.Snapshot().HostUsage[0].Uploaded != 1500 {
		t.Fatal("close counted discarded packets")
	}
}

func TestUsageBoundedPrivateAndConcurrent(t *testing.T) {
	m := manager(t, Settings{Mode: Daily})
	var wg sync.WaitGroup
	for i := 0; i < 8; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for n := uint32(1); n <= 200; n++ {
				b := packet(1, 100, 1500)
				binary.BigEndian.PutUint32(b[12:16], n)
				m.recordHostPacket(b, false)
				m.Snapshot()
			}
		}()
	}
	wg.Wait()
	s := m.Snapshot()
	var total uint64
	for _, h := range s.HostUsage {
		total += h.Uploaded + h.Downloaded
	}
	if len(s.HostUsage) != 128 || total+s.OtherHostBytes != 8*200*1500 {
		t.Fatal(len(s.HostUsage), total, s.OtherHostBytes)
	}
	encoded, err := json.Marshal(s.PhoneSnapshot())
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(string(encoded), "address") || strings.Contains(string(encoded), "hostUsage") {
		t.Fatal("local source addresses exposed to phone")
	}
	next := m.Snapshot()
	other := manager(t, Settings{Mode: Daily}).Snapshot()
	if next.Sequence <= s.Sequence || next.SessionID != s.SessionID || other.SessionID == s.SessionID {
		t.Fatal("session/sequence identity")
	}
}
