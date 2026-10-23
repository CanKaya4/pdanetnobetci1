//go:build windows

package traffic

import (
	"golang.org/x/sys/windows"
	"unsafe"
)

// RunningGames only enumerates executable names using the documented Toolhelp
// snapshot API. It does not open games, inspect memory or interact with anti-cheat.
// Names are heuristics, not authenticated process identities or packet ownership.
func RunningGames() ([]string, error) {
	h, err := windows.CreateToolhelp32Snapshot(windows.TH32CS_SNAPPROCESS, 0)
	if err != nil {
		return nil, err
	}
	defer windows.CloseHandle(h)
	entry := windows.ProcessEntry32{Size: uint32(unsafe.Sizeof(windows.ProcessEntry32{}))}
	if err = windows.Process32First(h, &entry); err != nil {
		return nil, err
	}
	names := []string{}
	for {
		names = append(names, windows.UTF16ToString(entry.ExeFile[:]))
		err = windows.Process32Next(h, &entry)
		if err == windows.ERROR_NO_MORE_FILES {
			break
		}
		if err != nil {
			return nil, err
		}
	}
	return DetectGames(names), nil
}
