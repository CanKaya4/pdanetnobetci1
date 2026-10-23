package traffic

import (
	"context"
	"errors"
	"sync"
	"sync/atomic"
	"time"
)

type request struct {
	ctx    context.Context
	id     uint64
	class  Class
	cost   int
	at     time.Time
	done   chan error
	finish float64
}
type scheduler struct {
	m          *Manager
	down       bool
	requests   chan *request
	releases   chan uint64
	changed    chan struct{}
	stop, done chan struct{}
	once       sync.Once
	pending    atomic.Int64
}

func newScheduler(m *Manager, down bool) *scheduler {
	s := &scheduler{m: m, down: down, requests: make(chan *request, 256), releases: make(chan uint64, 256), changed: make(chan struct{}, 1), stop: make(chan struct{}), done: make(chan struct{})}
	go s.run()
	return s
}
func (s *scheduler) wake() {
	select {
	case s.changed <- struct{}{}:
	default:
	}
}
func (s *scheduler) close() { s.once.Do(func() { close(s.stop) }); <-s.done }
func (s *scheduler) wait(ctx context.Context, id uint64, class Class, cost int) error {
	if cost < 1 || cost > 65537 {
		return errors.New("invalid scheduling cost")
	}
	r := &request{ctx: ctx, id: id, class: class, cost: cost, at: time.Now(), done: make(chan error, 1)}
	select {
	case s.requests <- r:
	case <-ctx.Done():
		return ctx.Err()
	case <-s.stop:
		return errors.New("scheduler closed")
	}
	select {
	case err := <-r.done:
		return err
	case <-ctx.Done():
		s.wake()
		return ctx.Err()
	case <-s.stop:
		return errors.New("scheduler closed")
	}
}

// Weighted fair byte service: finish times retain per-flow credits while a flow
// lives. No network write runs inside the dispatcher; a blocked bulk socket
// cannot block unrelated UDP sockets. Pacing permits at most one chunk burst.
func (s *scheduler) run() {
	defer close(s.done)
	queue := []*request{}
	last := map[uint64]float64{}
	virtual := 0.0
	due := time.Now()
	var grantedAt time.Time
	grantedCost := 0
	timer := time.NewTimer(time.Hour)
	defer timer.Stop()
	add := func(r *request) {
		if r.ctx.Err() != nil {
			r.done <- r.ctx.Err()
			return
		}
		if len(queue) >= 512 {
			s.m.rejectedRequest()
			r.done <- errors.New("scheduler queue full")
			return
		}
		_, w, _ := s.m.policy(s.down, r.class)
		r.finish = max(last[r.id], virtual) + float64(r.cost)/w
		last[r.id] = r.finish
		queue = append(queue, r)
		s.pending.Add(1)
	}
	defer func() {
		for _, r := range queue {
			r.done <- errors.New("scheduler closed")
		}
		s.pending.Store(0)
	}()
	for {
		select {
		case <-s.stop:
			return
		default:
		}
		select {
		case id := <-s.releases:
			delete(last, id)
		default:
		}
		// Admit a bounded batch before selection, so competing flows are visible.
		for i := 0; i < 256; i++ {
			select {
			case r := <-s.requests:
				add(r)
			default:
				i = 256
			}
		}
		kept := queue[:0]
		for _, r := range queue {
			if r.ctx.Err() != nil {
				r.done <- r.ctx.Err()
				s.pending.Add(-1)
			} else {
				kept = append(kept, r)
			}
		}
		queue = kept
		if len(queue) > 0 && !time.Now().Before(due) {
			best := 0
			for i := 1; i < len(queue); i++ {
				if queue[i].finish < queue[best].finish {
					best = i
				}
			}
			r := queue[best]
			queue = append(queue[:best], queue[best+1:]...)
			s.pending.Add(-1)
			virtual = max(virtual, r.finish)
			rate, _, _ := s.m.policy(s.down, r.class)
			due = time.Now()
			grantedAt, grantedCost = due, r.cost
			if rate > 0 {
				due = due.Add(time.Duration(float64(r.cost) * float64(time.Second) / rate))
			}
			s.m.recordWait(time.Since(r.at))
			r.done <- nil
			continue
		}
		wait := time.Hour
		if len(queue) > 0 {
			wait = max(time.Until(due), time.Nanosecond)
		}
		timer.Reset(wait)
		select {
		case <-s.stop:
			return
		case r := <-s.requests:
			add(r)
		case id := <-s.releases:
			delete(last, id)
		case <-s.changed:
			// Reprice the outstanding grant when a live ceiling changes. Keeping
			// the old deadline could stall an unlimited profile for minutes.
			rate, _, _ := s.m.policy(s.down, Bulk)
			due = time.Now()
			if rate > 0 && !grantedAt.IsZero() {
				due = grantedAt.Add(time.Duration(float64(grantedCost) * float64(time.Second) / rate))
			}
			// Recompute queued weights after a live profile switch.
			clear(last)
			for _, r := range queue {
				_, w, _ := s.m.policy(s.down, r.class)
				r.finish = max(last[r.id], virtual) + float64(r.cost)/w
				last[r.id] = r.finish
			}
		case <-timer.C:
		}
	}
}

type Flow struct {
	manager *Manager
	id      uint64
	class   Class
	ctx     context.Context
	cancel  context.CancelFunc
	once    sync.Once
}

func (m *Manager) Flow(ctx context.Context, class Class) *Flow {
	m.mu.Lock()
	m.next++
	id := m.next
	m.active++
	if class == Shared {
		m.shared++
	}
	m.mu.Unlock()
	ctx, cancel := context.WithCancel(ctx)
	return &Flow{manager: m, id: id, class: class, ctx: ctx, cancel: cancel}
}
func (f *Flow) Close() {
	f.once.Do(func() {
		f.cancel()
		m := f.manager
		m.mu.Lock()
		m.active--
		if f.class == Shared {
			m.shared--
		}
		m.mu.Unlock()
		for _, s := range []*scheduler{m.up, m.down} {
			select {
			case s.releases <- f.id:
			case <-s.stop:
			}
		}
	})
}
func (f *Flow) Wait(down bool, size int) error {
	if size == 0 {
		size = 1
	}
	s := f.manager.up
	if down {
		s = f.manager.down
	}
	return s.wait(f.ctx, f.id, f.class, size)
}
func (f *Flow) Datagram(down bool, size int) error {
	class := f.class
	if size <= 1200 {
		class = Interactive
	}
	s := f.manager.up
	if down {
		s = f.manager.down
	}
	return s.wait(f.ctx, f.id, class, max(size, 1))
}
func (f *Flow) Chunk() int { _, _, n := f.manager.policy(false, f.class); return n }
func (f *Flow) Count(down bool, n int) {
	if n > 0 {
		f.manager.addBytes(down, n)
	}
}
