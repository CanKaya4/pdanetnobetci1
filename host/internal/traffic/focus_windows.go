//go:build windows

package traffic

import (
	"errors"
	"golang.org/x/sys/windows"
	"unsafe"
)

var ipHelper = windows.NewLazySystemDLL("iphlpapi.dll")
var tcpOwners = ipHelper.NewProc("GetExtendedTcpTable")
var udpOwners = ipHelper.NewProc("GetExtendedUdpTable")

func socketOwnerTable(v6, tcp bool) ([]byte, error) {
	proc, class := udpOwners, uintptr(1)
	if tcp {
		proc, class = tcpOwners, 5
	}
	family := uintptr(2)
	if v6 {
		family = 23
	}
	var size uint32
	result, _, _ := proc.Call(0, uintptr(unsafe.Pointer(&size)), 0, family, class, 0)
	if result != 0 && result != 122 {
		return nil, windows.Errno(result)
	}
	for attempt := 0; attempt < 3; attempt++ {
		if size < 4 || size > 8*1024*1024 {
			return nil, errors.New("socket table exceeds bound")
		}
		data := make([]byte, size)
		result, _, _ = proc.Call(uintptr(unsafe.Pointer(&data[0])), uintptr(unsafe.Pointer(&size)), 0, family, class, 0)
		if result == 0 {
			if int(size) > len(data) {
				return nil, errors.New("socket table size changed")
			}
			return data[:size], nil
		}
		if result != 122 {
			return nil, windows.Errno(result)
		}
	}
	return nil, errors.New("socket table kept changing")
}
func collectFocusOwners(processes []FocusProcess) (*ownerSnapshot, error) {
	result := &ownerSnapshot{Games: map[uint32]bool{}, Owners: map[ownerKey]uint32{}, Status: "Seçilen oyun çalışmıyor veya süreç doğrulanamadı"}
	for _, p := range processes {
		handle, err := windows.OpenProcess(windows.PROCESS_QUERY_LIMITED_INFORMATION, false, p.PID)
		if err != nil {
			continue
		}
		var created, exited, kernel, user windows.Filetime
		err = windows.GetProcessTimes(handle, &created, &exited, &kernel, &user)
		windows.CloseHandle(handle)
		actual := uint64(created.HighDateTime)<<32 | uint64(created.LowDateTime)
		if err == nil && actual == p.Started {
			result.Games[p.PID] = true
		}
	}
	if len(result.Games) == 0 {
		return result, nil
	}
	for _, spec := range [][2]bool{{false, true}, {false, false}, {true, true}, {true, false}} {
		data, err := socketOwnerTable(spec[0], spec[1])
		if err != nil {
			return nil, err
		}
		if err = parseOwnerTable(data, spec[0], spec[1], result.Owners); err != nil {
			return nil, err
		}
	}
	result.Status = "Windows TCP/UDP sahipliği doğrulandı"
	return result, nil
}
