package traffic

import (
	"encoding/binary"
	"errors"
	"net/netip"
)

func addOwner(owners map[ownerKey]uint32, key ownerKey, pid uint32) {
	if old, ok := owners[key]; ok && old != pid {
		owners[key] = 0
	} else if !ok {
		owners[key] = pid
	}
}

// Windows owner-PID rows are DWORD-aligned for both AF_INET and AF_INET6.
func parseOwnerTable(data []byte, v6, tcp bool, owners map[ownerKey]uint32) error {
	row := 12
	if tcp {
		row = 24
	}
	if v6 {
		row = 28
		if tcp {
			row = 56
		}
	}
	if len(data) < 4 {
		return errors.New("truncated socket table")
	}
	count := int(binary.LittleEndian.Uint32(data[:4]))
	if count > 32768 || count > (len(data)-4)/row {
		return errors.New("invalid socket table size")
	}
	for i := 0; i < count; i++ {
		b := data[4+i*row : 4+(i+1)*row]
		k := ownerKey{Protocol: 17}
		var pid uint32
		if !v6 {
			off := 0
			if tcp {
				k.Protocol = 6
				off = 4
				copy(k.Remote[:], b[12:16])
				k.RemotePort = binary.BigEndian.Uint16(b[16:18])
			}
			copy(k.Local[:], b[off:off+4])
			k.LocalPort = binary.BigEndian.Uint16(b[off+4 : off+6])
			pid = binary.LittleEndian.Uint32(b[row-4:])
		} else {
			var local, remote [16]byte
			copy(local[:], b[:16])
			addr := netip.AddrFrom16(local).Unmap()
			if addr.IsUnspecified() {
				k.Local = [4]byte{}
			} else if addr.Is4() {
				k.Local = addr.As4()
			} else {
				continue
			}
			k.LocalPort = binary.BigEndian.Uint16(b[20:22])
			if tcp {
				k.Protocol = 6
				copy(remote[:], b[24:40])
				r := netip.AddrFrom16(remote).Unmap()
				if !r.Is4() {
					continue
				}
				k.Remote = r.As4()
				k.RemotePort = binary.BigEndian.Uint16(b[44:46])
			}
			pid = binary.LittleEndian.Uint32(b[row-4:])
		}
		if pid <= 4 || k.LocalPort == 0 || (tcp && k.RemotePort == 0) {
			continue
		}
		addOwner(owners, k, pid)
	}
	return nil
}
