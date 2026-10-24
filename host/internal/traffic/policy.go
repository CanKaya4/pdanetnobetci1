// Package traffic provides experimental per-flow byte scheduling and pacing.
// It changes only TetherMax socket delivery, never the OS network configuration.
// This is not CoDel or CAKE and cannot control the mobile base station's queue.
package traffic

import (
	"errors"
	"math"
	"slices"
	"sort"
	"strings"
	"sync"
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
	if s.Adaptive && s.UploadMbps == 0 {
		return errors.New("adaptive pacing requires an explicit upload ceiling")
	}
	return nil
}

type Snapshot struct {
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
	at  time.Time
	rtt time.Duration
	ok  bool
}
type Manager struct {
	mu                   sync.Mutex
	settings             Settings
	games                []string
	shared               int
	upRate, downRate     float64
	observations         []observation
	lastRTT, jitter      float64
	lastSuccess          time.Time
	high, low            int
	lastAdjustment       time.Time
	sampleAt             time.Time
	sampleUp, sampleDown uint64
	up, down             *scheduler
	next                 uint64
	active               int
	upBytes, downBytes   uint64
	lastUp, lastDown     uint64
	lastSnapshot         time.Time
	waits                [512]float64
	waitCount            int
	served, rejected     uint64
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
	if m.settings.Adaptive {
		m.upRate *= 0.9
		m.downRate *= 0.9
	}
	m.high = 0
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
	seconds := now.Sub(m.sampleAt).Seconds()
	load := 0.0
	if seconds > 0 {
		if m.upRate > 0 {
			load = float64(m.upBytes-m.sampleUp) * 8 / 1e6 / seconds / m.upRate
		}
		if m.downRate > 0 {
			load = math.Max(load, float64(m.downBytes-m.sampleDown)*8/1e6/seconds/m.downRate)
		}
	}
	m.sampleAt = now
	m.sampleUp = m.upBytes
	m.sampleDown = m.downBytes
	if ok && (rtt <= 0 || rtt > 30*time.Second) {
		return
	}
	m.observations = append(m.observations, observation{now, rtt, ok})
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
	base := rtt
	valid := 0
	for _, o := range m.observations {
		if o.ok {
			valid++
			if o.rtt < base {
				base = o.rtt
			}
		}
	}
	if valid < 5 {
		return
	}
	if rtt > base+20*time.Millisecond && load >= 0.6 {
		m.high++
		m.low = 0
	} else if rtt < base+10*time.Millisecond {
		m.low++
		m.high = 0
	} else {
		m.low = 0
		m.high = 0
	}
	if now.Sub(m.lastAdjustment) < 2*time.Second {
		return
	}
	if m.high >= 3 {
		m.upRate = math.Max(m.settings.UploadMbps*0.1, m.upRate*0.85)
		if m.downRate > 0 {
			m.downRate = math.Max(m.settings.DownloadMbps*0.1, m.downRate*0.9)
		}
		m.high = 0
		m.lastAdjustment = now
	} else if m.low >= 10 {
		m.upRate = math.Min(m.settings.UploadMbps, m.upRate+m.settings.UploadMbps*0.02)
		if m.downRate > 0 {
			m.downRate = math.Min(m.settings.DownloadMbps, m.downRate+m.settings.DownloadMbps*0.02)
		}
		m.low = 0
		m.lastAdjustment = now
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
	s := Snapshot{Type: "traffic", Requested: m.settings.Mode, Active: m.activeMode(), Settings: m.settings, Games: append([]string{}, m.games...), Uploaded: m.upBytes, Downloaded: m.downBytes, UploadMbps: up, DownloadMbps: down, UploadCeilingMbps: m.upRate, DownloadCeilingMbps: m.downRate, ActiveFlows: m.active, Pending: int(m.up.pending.Load() + m.down.pending.Load()), Served: m.served, Rejected: m.rejected}
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
func (m *Manager) Close()             { m.up.close(); m.down.close() }
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
