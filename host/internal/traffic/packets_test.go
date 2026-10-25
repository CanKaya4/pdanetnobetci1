package traffic

import (
	"context"
	"encoding/binary"
	"testing"
	"time"
)

func packet(host byte, port uint16, size int) []byte {
	b := make([]byte, size)
	b[0] = 0x45
	b[8] = 64
	b[9] = 17
	binary.BigEndian.PutUint16(b[2:4], uint16(size))
	b[12] = 10
	b[15] = host
	b[16] = 1
	b[19] = 1
	binary.BigEndian.PutUint16(b[20:22], port)
	binary.BigEndian.PutUint16(b[22:24], 443)
	return b
}
func TestCoDelPersistentDelayNotTransientBurst(t *testing.T) {
	var c codel
	now := time.Now()
	if c.drop(now, 20*time.Millisecond, 4000) || c.drop(now.Add(99*time.Millisecond), 20*time.Millisecond, 4000) {
		t.Fatal("transient burst dropped")
	}
	if !c.drop(now.Add(101*time.Millisecond), 20*time.Millisecond, 4000) {
		t.Fatal("persistent queue not signalled")
	}
	if c.drop(now.Add(150*time.Millisecond), 20*time.Millisecond, 4000) {
		t.Fatal("drop interval ignored")
	}
	if !c.drop(now.Add(202*time.Millisecond), 20*time.Millisecond, 4000) || c.count != 2 {
		t.Fatal("inverse sqrt control did not advance")
	}
	if c.drop(now.Add(time.Second), time.Millisecond, 4000) || c.dropping {
		t.Fatal("low delay did not leave dropping state")
	}
	if c.drop(now.Add(2*time.Second), time.Second, 1500) {
		t.Fatal("last MTU starved")
	}
}
func TestPacketHostFairnessResistsManyConnections(t *testing.T) {
	m := manager(t, Settings{Mode: Gaming})
	q := m.PacketQueue(false)
	for n := 0; n < 100; n++ {
		for port := 0; port < 16; port++ {
			q.Enqueue(packet(1, uint16(port+100), 1500))
		}
		q.Enqueue(packet(2, 400, 1500))
	}
	counts := map[byte]int{}
	for n := 0; n < 100; n++ {
		b, err := q.Read(context.Background())
		if err != nil {
			t.Fatal(err)
		}
		counts[b[15]]++
	}
	if counts[1] < 45 || counts[2] < 45 {
		t.Fatalf("connection count defeated host fairness: %v", counts)
	}
}

func TestPacketSmallUDPServiceAndBulkProgress(t *testing.T) {
	m := manager(t, Settings{Mode: Gaming})
	q := m.PacketQueue(false)
	for range 100 {
		q.Enqueue(packet(1, 100, 1500))
	}
	q.Enqueue(packet(1, 200, 100))
	b, err := q.Read(context.Background())
	if err != nil || len(b) != 100 {
		t.Fatal("sparse small UDP waited behind bulk", err)
	}
	b, err = q.Read(context.Background())
	if err != nil || len(b) != 1500 {
		t.Fatal("bulk service starved", err)
	}
}

func TestCoDelQueueActuallyDropsIPPacketWithoutStreamError(t *testing.T) {
	m := manager(t, Settings{Mode: Gaming})
	q := m.PacketQueue(false)
	for range 5 {
		q.Enqueue(packet(1, 100, 1500))
	}
	q.mu.Lock()
	for _, h := range q.hosts {
		for _, f := range h.flows {
			for i := range f.packets {
				f.packets[i].at = time.Now().Add(-time.Second)
			}
			f.aqm.above = time.Now().Add(-time.Second)
		}
	}
	q.mu.Unlock()
	b, err := q.Read(context.Background())
	if err != nil || len(b) != 1500 || m.Snapshot().CoDelDrops != 1 {
		t.Fatalf("AQM did not drop one whole packet and continue: %d %v %+v", len(b), err, m.Snapshot())
	}
}
func TestPacketBudgetDropsWholePacketsAndKeepsSparseFlow(t *testing.T) {
	m := manager(t, Settings{Mode: Gaming})
	q := m.PacketQueue(false)
	for n := 0; n < 5000; n++ {
		q.Enqueue(packet(1, 100, 1500))
	}
	sparse := packet(2, 200, 100)
	q.Enqueue(sparse)
	if q.bytes > byteLimit || q.packets > packetLimit || m.Snapshot().OverlimitDrops == 0 {
		t.Fatal("queue not bounded")
	}
	found := false
	for n := 0; n < 4; n++ {
		b, err := q.Read(context.Background())
		if err != nil {
			t.Fatal(err)
		}
		if len(b) != 1500 && len(b) != 100 {
			t.Fatal("packet truncated")
		}
		if b[15] == 2 {
			found = true
			break
		}
	}
	if !found {
		t.Fatal("bulk overflow evicted sparse host")
	}
	q.Close()
	if m.packetPending.Load() != 0 {
		t.Fatal("queue memory not released")
	}
}
func TestPacketLiveCeilingCancellationAndDirection(t *testing.T) {
	m := manager(t, Settings{Mode: Gaming, UploadMbps: .001})
	q := m.PacketQueue(false)
	q.Enqueue(packet(1, 1, 1500))
	q.Enqueue(packet(1, 1, 1500))
	q.Enqueue(packet(1, 1, 1500))
	for range 2 {
		if _, err := q.Read(context.Background()); err != nil {
			t.Fatal(err)
		}
	}
	done := make(chan error, 1)
	go func() { _, err := q.Read(context.Background()); done <- err }()
	if err := m.Configure(Settings{Mode: Maximum}); err != nil {
		t.Fatal(err)
	}
	select {
	case err := <-done:
		if err != nil {
			t.Fatal(err)
		}
	case <-time.After(time.Second):
		t.Fatal("old pacing debt survived profile switch")
	}
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	if _, err := q.Read(ctx); err == nil {
		t.Fatal("cancellation ignored")
	}
	up, _, _, _ := packetIdentity(packet(7, 100, 1500), false)
	down, _, _, _ := packetIdentity(packet(7, 100, 1500), true)
	if up == down {
		t.Fatal("download host not destination")
	}
}

func TestPacketLargerThanPacingBurstStillProgresses(t *testing.T) {
	m := manager(t, Settings{Mode: Daily, UploadMbps: .001})
	q := m.PacketQueue(false)
	q.Enqueue(packet(1, 100, 9000))
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	b, err := q.Read(ctx)
	if err != nil || len(b) != 9000 {
		t.Fatal("valid large IP packet starved", err)
	}
}
func TestAutomaticCapacityLearningAndUnlimitedIdle(t *testing.T) {
	m := manager(t, Settings{Mode: Gaming, Adaptive: true})
	now := time.Now()
	sample := func(delay time.Duration, bytes int) {
		now = now.Add(time.Second)
		m.addBytes(true, bytes)
		m.Observe(now, delay, true)
	}
	for range 5 {
		sample(20*time.Millisecond, 0)
	}
	if m.Snapshot().DownloadCeilingMbps != 0 {
		t.Fatal("idle started with arbitrary limit")
	}
	for range 3 {
		sample(100*time.Millisecond, 1250000)
	}
	s := m.Snapshot()
	if s.DownloadCeilingMbps <= 0 || s.DownloadCeilingMbps >= 10 || s.UploadCeilingMbps != 0 {
		t.Fatalf("congested download not independently learned: %+v", s)
	}
	for range 10 {
		sample(20*time.Millisecond, 0)
	}
	if m.Snapshot().DownloadCeilingMbps != 0 {
		t.Fatal("quiet link retained learned limit")
	}
}

func TestCapacityCollapseUsesBacklogWithoutChangingIdleDirection(t *testing.T) {
	for _, down := range []bool{false, true} {
		m := manager(t, Settings{Mode: Gaming, UploadMbps: 100, DownloadMbps: 100, Adaptive: true})
		q := m.PacketQueue(down)
		now := time.Now()
		for range 5 {
			now = now.Add(time.Second)
			m.Observe(now, 20*time.Millisecond, true)
		}
		for range 16 {
			q.Enqueue(packet(1, 100, 1500))
		}
		// 4 Mbps is far below the old 100 Mbps ceiling, but packets are queued.
		for range 3 {
			now = now.Add(time.Second)
			m.addBytes(down, 500000)
			m.Observe(now, 120*time.Millisecond, true)
		}
		snapshot := m.Snapshot()
		reduced, other := snapshot.UploadCeilingMbps, snapshot.DownloadCeilingMbps
		if down {
			reduced, other = other, reduced
		}
		if reduced <= 0 || reduced >= 4 || other != 100 {
			t.Fatalf("capacity collapse not isolated to loaded direction: %+v", snapshot)
		}
		q.Close()
		if q.pending.Load() != 0 || m.packetPending.Load() != 0 {
			t.Fatal("closed pressure counter leaked")
		}
	}
}
func TestLowDemandAndRemoteDelayDoNotInventCapacityCollapse(t *testing.T) {
	m := manager(t, Settings{Mode: Gaming, UploadMbps: 100, DownloadMbps: 100, Adaptive: true})
	q := m.PacketQueue(true)
	now := time.Now()
	for range 5 {
		now = now.Add(time.Second)
		m.Observe(now, 20*time.Millisecond, true)
	}
	for range 5 {
		now = now.Add(time.Second)
		m.addBytes(true, 500000)
		m.Observe(now, 120*time.Millisecond, true)
	}
	if m.Snapshot().DownloadCeilingMbps != 100 {
		t.Fatal("low application demand mistaken for congestion")
	}
	for range 16 {
		q.Enqueue(packet(1, 100, 1500))
	}
	for range 5 {
		now = now.Add(time.Second)
		m.Observe(now, 120*time.Millisecond, true)
	}
	if m.Snapshot().DownloadCeilingMbps != 100 {
		t.Fatal("zero traffic mistaken for cellular capacity")
	}
}
