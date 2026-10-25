// Package traffic provides experimental per-flow byte scheduling and pacing.
// It changes only TetherMax socket delivery, never the OS network configuration.
// Packet queues use CoDel; the mobile base station queue remains outside our control.
package traffic

import (
	"errors"
	"math"
	"slices"
	"sort"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

type Mode string

const (
	Auto      Mode = "auto"
	Gaming    Mode = "gaming"
	Streaming Mode = "streaming"
	Daily     Mode = "daily"
	Maximum   Mode = "maximum"
)

type Class uint8

const (
	Bulk Class = iota
	Interactive
	Shared
)

type Settings struct {
	DNSProfile   string  `json:"dnsProfile"`
	Mode         Mode    `json:"mode"`
	UploadMbps   float64 `json:"uploadMbps"`
	DownloadMbps float64 `json:"downloadMbps"`
	Adaptive     bool    `json:"adaptive"`
}

func (s Settings) Validate() error {
	switch s.DNSProfile {
	case "", "system", "adguard", "family", "cloudflare":
	default:
		return errors.New("invalid DNS profile")
	}
	switch s.Mode {
	case Auto, Gaming, Streaming, Daily, Maximum:
	default:
		return errors.New("invalid traffic mode")
	}
	for _, rate := range []float64{s.UploadMbps, s.DownloadMbps} {
		if math.IsNaN(rate) || math.IsInf(rate, 0) || rate < 0 || rate > 10000 {
			return errors.New("rate ceiling must be 0..10000 Mbps")
		}
	}

	return nil
}

type Snapshot struct {
	AQM                                    string `json:"aqm"`
	VisibleHosts                           int
	CoDelDrops, OverlimitDrops             uint64
	PacketPending                          int64
	Type                                   string   `json:"type"`
	Requested                              Mode     `json:"requested"`
	Active                                 Mode     `json:"active"`
	Settings                               Settings `json:"settings"`
	Games                                  []string `json:"games"`
	Uploaded, Downloaded                   uint64
	UploadMbps, DownloadMbps               float64
	UploadCeilingMbps, DownloadCeilingMbps float64
	QueryRTTMs, JitterMs, ProbeLossPercent *float64
	ProbeSamples                           int
	QueueP50Ms, QueueP95Ms, QueueP99Ms     float64
	ActiveFlows, Pending                   int
	Served, Rejected                       uint64
}
type observation struct {
	at               time.Time
	rtt              time.Duration
	ok               bool
	upMbps, downMbps float64
}
type Manager struct {
	visibleHosts               map[uint32]struct{}
	packetQueues               []*PacketQueue
	packetPending              atomic.Int64
	codelDrops, overlimitDrops atomic.Uint64
	mu                         sync.Mutex
	settings                   Settings
	games                      []string
	shared                     int
	upRate, downRate           float64
	observations               []observation
	lastRTT, jitter            float64
	lastSuccess                time.Time
	high, low                  int
	highUp, highDown           int
	lastAdjustment             time.Time
	sampleAt                   time.Time
	sampleUp, sampleDown       uint64
	up, down                   *scheduler
	next                       uint64
	active                     int
	upBytes, downBytes         uint64
	lastUp, lastDown           uint64
	lastSnapshot               time.Time
	waits                      [512]float64
	waitCount                  int
	served, rejected           uint64
}

func New(s Settings) (*Manager, error) {
	if err := s.Validate(); err != nil {
		return nil, err
	}
	m := &Manager{settings: s, lastSnapshot: time.Now()}
	m.resetRates()
	m.up = newScheduler(m, false)
	m.down = newScheduler(m, true)
	return m, nil
}
func (m *Manager) resetRates() {
	m.upRate = m.settings.UploadMbps
	m.downRate = m.settings.DownloadMbps
	m.high = 0
	m.highUp, m.highDown = 0, 0
	m.low = 0
	m.lastAdjustment = time.Time{}
	m.sampleAt = time.Now()
	m.sampleUp = m.upBytes
	m.sampleDown = m.downBytes
}
func (m *Manager) Configure(s Settings) error {
	if err := s.Validate(); err != nil {
		return err
	}
	m.mu.Lock()
	m.settings = s
	m.resetRates()
	m.mu.Unlock()
	m.up.wake()
	m.down.wake()
	m.wakePackets()
	return nil
}
func (m *Manager) SetGames(names []string) {
	m.mu.Lock()
	if slices.Equal(m.games, names) {
		m.mu.Unlock()
		return
	}
	m.games = append([]string(nil), names...)
	m.mu.Unlock()
	m.up.wake()
	m.down.wake()
}
func DetectGames(names []string) []string {
	known := map[string]string{"valorant-win64-shipping.exe": "Valorant", "valorant.exe": "Valorant", "project8.exe": "Deadlock", "genshinimpact.exe": "Genshin Impact", "yuanshen.exe": "Genshin Impact"}
	seen := map[string]bool{}
	out := []string{}
	for _, name := range names {
		if game, ok := known[strings.ToLower(name)]; ok && !seen[game] {
			seen[game] = true
			out = append(out, game)
		}
	}
	sort.Strings(out)
	return out
}
func (m *Manager) activeMode() Mode {
	if m.settings.Mode != Auto {
		return m.settings.Mode
	}
	if len(m.games) > 0 {
		return Gaming
	}
	if m.shared > 0 {
		return Streaming
	}
	return Daily
}
func weight(mode Mode, class Class) float64 {
	if mode == Maximum {
		return 1
	}
	if class == Interactive {
		if mode == Gaming {
			return 6
		}
		return 3
	}
	if class == Shared {
		if mode == Streaming {
			return 4
		}
		if mode == Gaming {
			return 2
		}
		return 2
	}
	return 1
}
func (m *Manager) policy(down bool, class Class) (float64, float64, int) {
	m.mu.Lock()
	defer m.mu.Unlock()
	rate := m.upRate
	if down {
		rate = m.downRate
	}
	mode := m.activeMode()
	if mode == Maximum {
		rate = m.settings.UploadMbps
		if down {
			rate = m.settings.DownloadMbps
		}
	}
	chunk := 1200
	if mode == Maximum {
		chunk = 16384
	} else if mode == Streaming {
		chunk = 4096
	}
	return rate * 1e6 / 8, weight(mode, class), chunk
}
func (m *Manager) recordWait(d time.Duration) {
	m.mu.Lock()
	m.waits[m.waitCount%len(m.waits)] = float64(d.Microseconds()) / 1000
	m.waitCount++
	m.served++
	m.mu.Unlock()
}
func (m *Manager) addBytes(down bool, n int) {
	m.mu.Lock()
	if down {
		m.downBytes += uint64(n)
	} else {
		m.upBytes += uint64(n)
	}
	m.mu.Unlock()
}
func (m *Manager) rejectedRequest() { m.mu.Lock(); m.rejected++; m.mu.Unlock() }

// Observe consumes an actual transaction duration. Tests supply labeled fixture
// observations; production feeds authenticated USB/cellular DNS transactions.
// RTT is service query RTT, not ICMP ping. Sparse windows are not loss estimates.
func (m *Manager) Observe(now time.Time, rtt time.Duration, ok bool) {
	m.mu.Lock()
	defer m.mu.Unlock()
	// Invalid or out-of-order samples must not consume the byte measurement
	// window or combine congestion evidence across an outage.
	if now.Before(m.sampleAt) || (ok && (rtt <= 0 || rtt > 30*time.Second)) {
		return
	}
	if !ok {
		m.high, m.low, m.highUp, m.highDown = 0, 0, 0, 0
	}
	seconds := now.Sub(m.sampleAt).Seconds()
	upMbps, downMbps := 0.0, 0.0
	if seconds > 0 {
		upMbps = float64(m.upBytes-m.sampleUp) * 8 / 1e6 / seconds
		downMbps = float64(m.downBytes-m.sampleDown) * 8 / 1e6 / seconds
	}
	loaded := func(measured, rate float64) bool {
		if rate == 0 {
			return measured >= 1
		}
		return measured >= rate*.6
	}
	upLoaded, downLoaded := loaded(upMbps, m.upRate), loaded(downMbps, m.downRate)
	// Mobile capacity can fall well below the configured ceiling. A sustained
	// local backlog with real traffic is load evidence even below 60% of that
	// old ceiling. Read atomics only: packet dequeue takes q.mu then m.mu.
	upBacklog, downBacklog := false, false
	for _, q := range m.packetQueues {
		if q.pending.Load() >= 8 {
			if q.down {
				downBacklog = true
			} else {
				upBacklog = true
			}
		}
	}
	upPressure := upBacklog && upMbps >= .25
	downPressure := downBacklog && downMbps >= .25
	upLoaded, downLoaded = upLoaded || upPressure, downLoaded || downPressure
	m.sampleAt = now
	m.sampleUp = m.upBytes
	m.sampleDown = m.downBytes
	if ok && (rtt <= 0 || rtt > 30*time.Second) {
		return
	}
	m.observations = append(m.observations, observation{now, rtt, ok, upMbps, downMbps})
	cut := now.Add(-120 * time.Second)
	i := 0
	for i < len(m.observations) && m.observations[i].at.Before(cut) {
		i++
	}
	m.observations = m.observations[i:]
	if len(m.observations) > 120 {
		m.observations = m.observations[len(m.observations)-120:]
	}
	if ok {
		m.lastSuccess = now
		v := float64(rtt.Microseconds()) / 1000
		if m.lastRTT > 0 {
			m.jitter += (math.Abs(v-m.lastRTT) - m.jitter) / 8
		}
		m.lastRTT = v
	}
	if !m.settings.Adaptive || m.activeMode() == Maximum || !ok {
		return
	}
	baseline := []time.Duration{}
	for _, o := range m.observations {
		if o.ok {
			baseline = append(baseline, o.rtt)
		}
	}
	valid := len(baseline)
	sort.Slice(baseline, func(i, j int) bool { return baseline[i] < baseline[j] })
	base := rtt
	if valid > 0 {
		base = baseline[(valid-1)/5]
	}
	target := max(20*time.Millisecond, base/4)
	if valid < 5 {
		return
	}
	if rtt > base+target && (upLoaded || downLoaded) {
		m.high++
		if upLoaded {
			m.highUp++
		} else {
			m.highUp = 0
		}
		if downLoaded {
			m.highDown++
		} else {
			m.highDown = 0
		}
		m.low = 0
	} else if rtt < base+10*time.Millisecond {
		m.low++
		m.high = 0
		m.highUp, m.highDown = 0, 0
	} else {
		m.low = 0
		m.high = 0
		m.highUp, m.highDown = 0, 0
	}
	if now.Sub(m.lastAdjustment) < 2*time.Second {
		return
	}
	// Rate estimates come from actual bytes, not advertised 5G signal strength.
	// A zero user ceiling starts unlimited; congestion discovers a working rate.
	if m.highUp >= 3 || m.highDown >= 3 {
		reduce := func(current, ceiling, measured float64, down bool) float64 {
			pressure := upPressure
			if down {
				pressure = downPressure
			}
			if current == 0 || pressure {
				// Learn from a recent window rather than one quiet transfer interval.
				rates := []float64{measured}
				for _, o := range m.observations {
					if now.Sub(o.at) <= 10*time.Second {
						v := o.upMbps
						if down {
							v = o.downMbps
						}
						if v >= 1 {
							rates = append(rates, v)
						}
					}
				}
				sort.Float64s(rates)
				learned := rates[(len(rates)-1)*4/5]
				if current == 0 {
					current = learned
				} else {
					current = math.Min(current, learned)
				}
			}
			next := math.Min(10000, math.Max(.1, current*.85))
			if ceiling > 0 {
				next = math.Min(ceiling, next)
			}
			return next
		}
		if m.highUp >= 3 {
			m.upRate = reduce(m.upRate, m.settings.UploadMbps, upMbps, false)
		}
		if m.highDown >= 3 {
			m.downRate = reduce(m.downRate, m.settings.DownloadMbps, downMbps, true)
		}
		m.high = 0
		m.highUp, m.highDown = 0, 0
		m.lastAdjustment = now
		// Wake blocked packet reads as soon as capacity changes.
		for _, q := range m.packetQueues {
			q.notify()
		}
	} else if m.low >= 10 {
		increase := func(current, ceiling float64) float64 {
			if current == 0 {
				return ceiling
			}
			next := math.Min(10000, current*1.08+.1)
			if ceiling > 0 {
				next = math.Min(ceiling, next)
			}
			return next
		}
		// Quiet recovery discards learned limits, but preserves explicit user caps.
		if upMbps < .05 && downMbps < .05 {
			m.upRate = m.settings.UploadMbps
			m.downRate = m.settings.DownloadMbps
		} else {
			m.upRate = increase(m.upRate, m.settings.UploadMbps)
			m.downRate = increase(m.downRate, m.settings.DownloadMbps)
		}
		m.low = 0
		m.lastAdjustment = now
		for _, q := range m.packetQueues {
			q.notify()
		}
	}

}
func (m *Manager) Snapshot() Snapshot {
	m.mu.Lock()
	defer m.mu.Unlock()
	now := time.Now()
	seconds := now.Sub(m.lastSnapshot).Seconds()
	up, down := 0.0, 0.0
	if seconds > 0 {
		up = float64(m.upBytes-m.lastUp) * 8 / 1e6 / seconds
		down = float64(m.downBytes-m.lastDown) * 8 / 1e6 / seconds
	}
	m.lastUp = m.upBytes
	m.lastDown = m.downBytes
	m.lastSnapshot = now
	s := Snapshot{AQM: "socket-pacing", VisibleHosts: len(m.visibleHosts), CoDelDrops: m.codelDrops.Load(), OverlimitDrops: m.overlimitDrops.Load(), PacketPending: m.packetPending.Load(), Type: "traffic", Requested: m.settings.Mode, Active: m.activeMode(), Settings: m.settings, Games: append([]string{}, m.games...), Uploaded: m.upBytes, Downloaded: m.downBytes, UploadMbps: up, DownloadMbps: down, UploadCeilingMbps: m.upRate, DownloadCeilingMbps: m.downRate, ActiveFlows: m.active, Pending: int(m.up.pending.Load() + m.down.pending.Load()), Served: m.served, Rejected: m.rejected}
	if len(m.packetQueues) > 0 {
		s.AQM = "host-fair CoDel IPv4"
	}
	if s.Active == Maximum {
		s.UploadCeilingMbps = m.settings.UploadMbps
		s.DownloadCeilingMbps = m.settings.DownloadMbps
	}
	valid, lost := 0, 0
	for _, o := range m.observations {
		if now.Sub(o.at) > 120*time.Second {
			continue
		}
		if o.ok {
			valid++
		} else {
			lost++
		}
	}
	s.ProbeSamples = valid + lost
	if valid > 0 && now.Sub(m.lastSuccess) < 5*time.Second {
		r, j := m.lastRTT, m.jitter
		s.QueryRTTMs = &r
		if valid >= 2 {
			s.JitterMs = &j
		}
	}
	if valid+lost >= 20 {
		loss := float64(lost) * 100 / float64(valid+lost)
		s.ProbeLossPercent = &loss
	}
	n := min(m.waitCount, len(m.waits))
	v := append([]float64(nil), m.waits[:n]...)
	sort.Float64s(v)
	if n > 0 {
		s.QueueP50Ms = v[(n-1)*50/100]
		s.QueueP95Ms = v[(n-1)*95/100]
		s.QueueP99Ms = v[(n-1)*99/100]
	}
	return s
}
func (m *Manager) Close() {
	m.up.close()
	m.down.close()
	m.mu.Lock()
	queues := append([]*PacketQueue(nil), m.packetQueues...)
	m.mu.Unlock()
	for _, q := range queues {
		q.Close()
	}
}
func (m *Manager) Settings() Settings { m.mu.Lock(); defer m.mu.Unlock(); return m.settings }

func (m *Manager) ResetMeasurements() {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.observations = nil
	m.lastRTT = 0
	m.jitter = 0
	m.lastSuccess = time.Time{}
	m.resetRates()
}

func (m *Manager) wakePackets() {
	m.mu.Lock()
	defer m.mu.Unlock()
	for _, q := range m.packetQueues {
		q.notify()
	}
}

func (m *Manager) visibleHost(host uint32) {
	m.mu.Lock()
	defer m.mu.Unlock()
	if m.visibleHosts == nil {
		m.visibleHosts = map[uint32]struct{}{}
	}
	if len(m.visibleHosts) < 1024 {
		m.visibleHosts[host] = struct{}{}
	}
}
