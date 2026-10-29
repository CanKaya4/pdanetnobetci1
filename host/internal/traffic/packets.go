package traffic

// Bounded IPv4 fair queues with CoDel sojourn-time control (RFC 8289).
// Drops happen to complete IP packets, never bytes of a reliable TCP stream.
// Hierarchical service isolates visible source hosts before weighting flows.
// After Windows ICS NAT, host identity may be lost; this layer cannot undo NAT.
import (
	"context"
	"encoding/binary"
	"errors"
	"math"
	"sync"
	"sync/atomic"
	"time"
)

const aqmTarget = 5 * time.Millisecond
const aqmInterval = 100 * time.Millisecond
const packetLimit = 4096
const byteLimit = 4 * 1024 * 1024

type codel struct {
	above, next time.Time
	dropping    bool
	count       uint32
}

func (c *codel) drop(now time.Time, sojourn time.Duration, remaining int) bool {
	return c.dropTarget(now, sojourn, remaining, aqmTarget)
}

func (c *codel) dropTarget(now time.Time, sojourn time.Duration, remaining int, target time.Duration) bool {
	if sojourn < target || remaining <= 1500 {
		c.above = time.Time{}
		c.dropping = false
		return false
	}
	if c.above.IsZero() {
		c.above = now.Add(aqmInterval)
		return false
	}
	if now.Before(c.above) {
		return false
	}
	if !c.dropping {
		c.dropping = true
		c.count = 1
		c.next = now.Add(aqmInterval)
		return true
	}
	if now.Before(c.next) {
		return false
	}
	c.count++
	c.next = c.next.Add(time.Duration(float64(aqmInterval) / math.Sqrt(float64(c.count))))
	return true
}

type ipPacket struct {
	data  []byte
	at    time.Time
	class Class
	base  Class
}
type packetFlow struct {
	packets []ipPacket
	bytes   int
	finish  float64
	aqm     codel
}
type packetHost struct {
	flows           map[string]*packetFlow
	finish, virtual float64
	priority        float64
	updated         time.Time
	bytes           int
}
type PacketQueue struct {
	bgTokens, bgRate, bgBurst float64
	bgRefill                  time.Time
	pending                   atomic.Int64
	m                         *Manager
	down                      bool
	mu                        sync.Mutex
	hosts                     map[uint32]*packetHost
	virtual                   float64
	bytes, packets            int
	tokens, previousRate      float64
	refill                    time.Time
	wake                      chan struct{}
	stop                      chan struct{}
	once                      sync.Once
}

func (m *Manager) PacketQueue(down bool) *PacketQueue {
	q := &PacketQueue{m: m, down: down, hosts: map[uint32]*packetHost{}, wake: make(chan struct{}, 1), stop: make(chan struct{}), refill: time.Now()}
	m.mu.Lock()
	m.packetQueues = append(m.packetQueues, q)
	m.mu.Unlock()
	return q
}
func (q *PacketQueue) notify() {
	select {
	case q.wake <- struct{}{}:
	default:
	}
}
func (q *PacketQueue) Close() {
	q.once.Do(func() {
		close(q.stop)
		q.mu.Lock()
		q.m.packetPending.Add(int64(-q.packets))
		q.pending.Store(0)
		clear(q.hosts)
		q.bytes = 0
		q.packets = 0
		q.mu.Unlock()
	})
}

func packetIdentity(b []byte, down bool) (uint32, string, Class, bool) {
	if len(b) < 20 || b[0]>>4 != 4 {
		return 0, "", Bulk, false
	}
	ihl := int(b[0]&15) * 4
	if ihl < 20 || ihl > len(b) || int(binary.BigEndian.Uint16(b[2:4])) != len(b) {
		return 0, "", Bulk, false
	}
	source, dest := b[12:16], b[16:20]
	ports := []byte{0, 0, 0, 0}
	fragmented := binary.BigEndian.Uint16(b[6:8])&0x3fff != 0
	class := Bulk
	if !fragmented && len(b) >= ihl+4 {
		ports = b[ihl : ihl+4]
		if b[9] == 17 && len(b) <= 512 {
			class = Interactive
		}
		if b[9] == 6 && len(b) >= ihl+20 {
			header := int(b[ihl+12]>>4) * 4
			if header >= 20 && len(b) == ihl+header {
				class = Interactive
			}
		}
	}
	key := append(append(append([]byte{}, source...), dest...), b[9])
	key = append(key, ports...)
	if fragmented {
		key = append(key, b[4:6]...)
	}
	if down {
		source = dest
	}
	return binary.BigEndian.Uint32(source), string(key), class, true
}

// Enqueue owns a copy; overflow drops from the largest host/flow first, so a
// many-connection downloader cannot evict a sparse game's entire queue.
func (q *PacketQueue) Enqueue(b []byte) bool {
	host, key, class, valid := packetIdentity(b, q.down)
	if !valid {
		return false
	}
	q.mu.Lock()
	defer q.mu.Unlock()
	select {
	case <-q.stop:
		return false
	default:
	}
	now := time.Now()
	if !q.down {
		q.m.visibleHost(host)
	}
	h := q.hosts[host]
	if h == nil {
		h = &packetHost{flows: map[string]*packetFlow{}, finish: q.virtual, priority: 8192, updated: now}
		q.hosts[host] = h
	}
	h.priority = math.Min(8192, h.priority+now.Sub(h.updated).Seconds()*32768)
	h.updated = now
	if class == Interactive {
		if h.priority >= float64(len(b)) {
			h.priority -= float64(len(b))
		} else {
			class = Bulk
		}
	}
	f := h.flows[key]
	if f == nil {
		f = &packetFlow{finish: h.virtual}
		h.flows[key] = f
	}
	f.packets = append(f.packets, ipPacket{data: append([]byte(nil), b...), at: now, class: class, base: class})
	f.bytes += len(b)
	h.bytes += len(b)
	q.bytes += len(b)
	q.packets++
	q.m.packetPending.Add(1)
	q.pending.Add(1)
	if q.packets > packetLimit || q.bytes > byteLimit {
		var largest *packetHost
		var fat *packetFlow
		for _, candidate := range q.hosts {
			if largest == nil || candidate.bytes > largest.bytes {
				largest = candidate
			}
		}
		for _, candidate := range largest.flows {
			if fat == nil || candidate.bytes > fat.bytes {
				fat = candidate
			}
		}
		q.remove(largest, fat)
		if len(fat.packets) == 0 {
			for key, candidate := range largest.flows {
				if candidate == fat {
					delete(largest.flows, key)
					break
				}
			}
			if len(largest.flows) == 0 {
				for key, candidate := range q.hosts {
					if candidate == largest {
						delete(q.hosts, key)
						break
					}
				}
			}
		}
		q.m.overlimitDrops.Add(1)
	}
	q.notify()
	return true
}
func (q *PacketQueue) remove(h *packetHost, f *packetFlow) ipPacket {
	p := f.packets[0]
	f.packets[0] = ipPacket{}
	f.packets = f.packets[1:]
	f.bytes -= len(p.data)
	h.bytes -= len(p.data)
	q.bytes -= len(p.data)
	q.packets--
	q.m.packetPending.Add(-1)
	q.pending.Add(-1)
	return p
}
func (q *PacketQueue) selectFlow() (*packetHost, *packetFlow, time.Duration) {
	var host *packetHost
	var flow *packetFlow
	wait := time.Hour
	for id, h := range q.hosts {
		var candidate *packetFlow
		var best float64
		for key, f := range h.flows {
			if len(f.packets) == 0 {
				delete(h.flows, key)
				continue
			}
			p := &f.packets[0]
			p.class = q.m.focusClass(p.data, q.down, p.base)
			if q.bgRate > 0 && p.class == Background {
				n := float64(len(p.data))
				if n > q.bgBurst {
					q.bgTokens += n - q.bgBurst
					q.bgBurst = n
				}
				if n > q.bgTokens {
					wait = min(wait, time.Duration((n-q.bgTokens)/q.bgRate*float64(time.Second)))
					continue
				}
			}
			_, weight, _ := q.m.policy(q.down, p.class)
			score := math.Max(f.finish, h.virtual) + float64(len(p.data))/weight
			if candidate == nil || score < best {
				candidate = f
				best = score
			}
		}
		if len(h.flows) == 0 {
			delete(q.hosts, id)
			continue
		}
		if candidate != nil && (host == nil || h.finish < host.finish) {
			host = h
			flow = candidate
		}
	}
	return host, flow, wait
}
func (q *PacketQueue) Read(ctx context.Context) ([]byte, error) {
	timer := time.NewTimer(time.Hour)
	defer timer.Stop()
	for {
		if err := ctx.Err(); err != nil {
			return nil, err
		}
		select {
		case <-q.stop:
			return nil, errors.New("packet queue closed")
		default:
		}
		q.mu.Lock()
		now := time.Now()
		bgRate := q.m.backgroundRate(q.down)
		if bgRate != q.bgRate {
			if q.bgRate == 0 {
				q.bgBurst = 4096
				q.bgTokens = q.bgBurst
			}
			q.bgRate = bgRate
			q.bgRefill = now
		}
		if q.bgRate > 0 {
			q.bgTokens = math.Min(q.bgBurst, q.bgTokens+now.Sub(q.bgRefill).Seconds()*q.bgRate)
		}
		q.bgRefill = now
		h, f, wait := q.selectFlow()
		if f != nil {
			now := time.Now()
			p := f.packets[0]
			rate, weight, _ := q.m.policy(q.down, p.class)
			// One millisecond burst amortises OS timer resolution at high throughput;
			// unused idle credit is bounded, never accumulated for a large later burst.
			burst := math.Max(float64(len(p.data)), math.Max(3000, math.Min(65536, rate*.001)))
			if rate != q.previousRate {
				q.tokens = math.Min(q.tokens, burst)
				if q.previousRate == 0 {
					q.tokens = burst
				}
				q.previousRate = rate
			}
			q.tokens = math.Min(burst, q.tokens+now.Sub(q.refill).Seconds()*rate)
			q.refill = now
			if rate > 0 && q.tokens < float64(len(p.data)) {
				wait = time.Duration((float64(len(p.data)) - q.tokens) / rate * float64(time.Second))
			} else {
				p = q.remove(h, f)
				// RFC 8290 section 5.2.2: target must allow at least one MTU
				// transmission at the configured egress rate. A fixed 5 ms target
				// needlessly signals congestion on slow mobile links/user caps.
				target := aqmTarget
				if rate > 0 {
					target = max(target, time.Duration(float64(max(1500, len(p.data)))/rate*float64(time.Second)))
				}
				if f.aqm.dropTarget(now, now.Sub(p.at), f.bytes, target) {
					q.m.codelDrops.Add(1)
					q.mu.Unlock()
					continue
				}
				if rate > 0 {
					q.tokens -= float64(len(p.data))
				}
				if p.class == Background && q.bgRate > 0 {
					q.bgTokens -= float64(len(p.data))
					q.m.focus.backgroundPackets.Add(1)
				}
				if p.class == Game {
					q.m.focus.gamePackets.Add(1)
				}
				q.virtual = math.Max(q.virtual, h.finish)
				h.finish = q.virtual + float64(len(p.data))
				h.virtual = math.Max(h.virtual, f.finish)
				f.finish = h.virtual + float64(len(p.data))/weight
				q.mu.Unlock()
				q.m.recordWait(now.Sub(p.at))
				q.m.recordHostPacket(p.data, q.down)
				return p.data, nil
			}
		}
		q.mu.Unlock()
		timer.Reset(max(wait, time.Microsecond))
		select {
		case <-ctx.Done():
			return nil, ctx.Err()
		case <-q.stop:
			return nil, errors.New("packet queue closed")
		case <-q.wake:
		case <-timer.C:
		}
	}
}
