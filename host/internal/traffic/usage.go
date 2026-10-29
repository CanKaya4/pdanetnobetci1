package traffic

import (
	"encoding/binary"
	"net/netip"
	"sort"
)

// HostUsage counts IPv4 bytes released by the local packet scheduler, including
// IP headers and retransmissions. It does not measure cellular billing or prove
// socket delivery. NAT can merge multiple physical devices into one address.
type HostUsage struct {
	Address    string `json:"address"`
	Uploaded   uint64 `json:"uploaded"`
	Downloaded uint64 `json:"downloaded"`
}

// Called only after AQM has accepted a packet for release. No packet payload,
// destination address, process identity or browsing history is retained.
func (m *Manager) recordHostPacket(b []byte, down bool) {
	// Enqueue already validated the full packet. Avoid allocating a flow key
	// again on this hot path just to read its local address.
	if len(b) < 20 || b[0]>>4 != 4 {
		return
	}
	offset := 12
	if down {
		offset = 16
	}
	host := binary.BigEndian.Uint32(b[offset : offset+4])
	m.mu.Lock()
	defer m.mu.Unlock()
	item := m.hostUsage[host]
	if item == nil {
		if len(m.hostUsage) >= 128 {
			m.otherHostBytes += uint64(len(b))
			return
		}
		var address [4]byte
		binary.BigEndian.PutUint32(address[:], host)
		item = &HostUsage{Address: netip.AddrFrom4(address).String()}
		m.hostUsage[host] = item
	}
	if down {
		item.Downloaded += uint64(len(b))
	} else {
		item.Uploaded += uint64(len(b))
	}
}

// Caller holds m.mu; deterministic output and independent values protect readers.
func (m *Manager) hostUsageSnapshot() []HostUsage {
	out := make([]HostUsage, 0, len(m.hostUsage))
	for _, item := range m.hostUsage {
		out = append(out, *item)
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Address < out[j].Address })
	return out
}
