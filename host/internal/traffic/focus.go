package traffic

import (
	"encoding/binary"
	"errors"
	"math"
	"slices"
	"sync/atomic"
	"time"
)

type FocusProcess struct {
	PID     uint32 `json:"pid"`
	Started uint64 `json:"started"`
}
type FocusSettings struct {
	Enabled        bool           `json:"enabled"`
	Processes      []FocusProcess `json:"processes"`
	BackgroundMbps float64        `json:"backgroundMbps"`
}

func (f *FocusSettings) validate() error {
	if f == nil {
		return nil
	}
	if len(f.Processes) > 16 || math.IsNaN(f.BackgroundMbps) || math.IsInf(f.BackgroundMbps, 0) || f.BackgroundMbps < .25 || f.BackgroundMbps > 10 {
		return errors.New("invalid game focus budget")
	}
	seen := map[uint32]bool{}
	for _, p := range f.Processes {
		if p.PID <= 4 || p.Started == 0 || seen[p.PID] {
			return errors.New("invalid game identity")
		}
		seen[p.PID] = true
	}
	return nil
}
func sameFocus(a, b *FocusSettings) bool {
	if a == nil || b == nil {
		return a == b
	}
	return a.Enabled == b.Enabled && a.BackgroundMbps == b.BackgroundMbps && slices.Equal(a.Processes, b.Processes)
}
func cloneFocus(f *FocusSettings) *FocusSettings {
	if f == nil {
		return nil
	}
	c := *f
	c.Processes = append([]FocusProcess(nil), f.Processes...)
	return &c
}

type focusLease struct {
	Config     *FocusSettings
	At         time.Time
	Generation uint64
}
type ownerKey struct {
	Protocol              byte
	Local, Remote         [4]byte
	LocalPort, RemotePort uint16
}
type ownerSnapshot struct {
	At         time.Time
	Generation uint64
	Games      map[uint32]bool
	Owners     map[ownerKey]uint32
	Status     string
}
type focusRuntime struct {
	lease                          atomic.Pointer[focusLease]
	owners                         atomic.Pointer[ownerSnapshot]
	generation                     uint64
	gamePackets, backgroundPackets atomic.Uint64
}

// Caller holds m.mu. Renewing a focus lease never resets learned radio capacity.
func (m *Manager) configureFocus(f *FocusSettings) {
	old := m.focus.lease.Load()
	if old == nil || !sameFocus(old.Config, f) {
		m.focus.generation++
	}
	m.focus.lease.Store(&focusLease{cloneFocus(f), time.Now(), m.focus.generation})
}
func (m *Manager) focusState(now time.Time) (*focusLease, *ownerSnapshot, bool) {
	l, o := m.focus.lease.Load(), m.focus.owners.Load()
	active := l != nil && l.Config != nil && l.Config.Enabled && len(l.Config.Processes) > 0 && now.Sub(l.At) < 8*time.Second && o != nil && o.Generation == l.Generation && now.Sub(o.At) < 5*time.Second && len(o.Games) > 0
	return l, o, active
}
func (m *Manager) RefreshFocusOwners() {
	l := m.focus.lease.Load()
	if l == nil || l.Config == nil || !l.Config.Enabled || len(l.Config.Processes) == 0 {
		m.focus.owners.Store(nil)
		return
	}
	o, err := collectFocusOwners(l.Config.Processes)
	if err != nil {
		o = &ownerSnapshot{Status: "Süreç/akış sahipliği erişilemiyor"}
	}
	o.At = time.Now()
	o.Generation = l.Generation
	m.focus.owners.Store(o)
}
func focusPacketKey(b []byte, down bool) (ownerKey, bool) {
	var k ownerKey
	if len(b) < 20 || b[0]>>4 != 4 {
		return k, false
	}
	h := int(b[0]&15) * 4
	if h < 20 || len(b) < h+4 || binary.BigEndian.Uint16(b[6:8])&0x3fff != 0 || (b[9] != 6 && b[9] != 17) {
		return k, false
	}
	k.Protocol = b[9]
	copy(k.Local[:], b[12:16])
	copy(k.Remote[:], b[16:20])
	k.LocalPort = binary.BigEndian.Uint16(b[h : h+2])
	k.RemotePort = binary.BigEndian.Uint16(b[h+2 : h+4])
	if down {
		k.Local, k.Remote = k.Remote, k.Local
		k.LocalPort, k.RemotePort = k.RemotePort, k.LocalPort
	}
	return k, true
}
func (m *Manager) focusClass(b []byte, down bool, base Class) Class {
	_, o, active := m.focusState(time.Now())
	if !active {
		return base
	}
	key, ok := focusPacketKey(b, down)
	if !ok {
		return base
	}
	pid := o.Owners[key]
	if key.Protocol == 17 {
		key.Remote = [4]byte{}
		key.RemotePort = 0
		var found bool
		pid, found = o.Owners[key]
		if !found {
			key.Local = [4]byte{}
			pid = o.Owners[key]
		}
	}
	if pid == 0 {
		return base
	}
	if o.Games[pid] {
		return Game
	}
	if base == Interactive {
		return base
	}
	return Background
}
func (m *Manager) backgroundRate(down bool) float64 {
	l, _, active := m.focusState(time.Now())
	if !active {
		return 0
	}
	m.mu.Lock()
	rate := m.upRate
	if down {
		rate = m.downRate
	}
	m.mu.Unlock()
	budget := l.Config.BackgroundMbps
	if rate > 0 {
		budget = math.Min(budget, math.Max(.128, rate*.15))
	}
	return budget * 1e6 / 8
}

// Phone telemetry must never contain local Windows process IDs/start times.
func (s Snapshot) PhoneSnapshot() Snapshot { s.Settings.Focus = nil; s.HostUsage = nil; return s }
