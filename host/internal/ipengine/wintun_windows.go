//go:build windows

package ipengine

import (
	"context"
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"golang.org/x/sys/windows"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"sync"
	"sync/atomic"

	"unsafe"
)

const WintunSHA256 = "e5da8447dc2c320edc0fc52fa01885c103de8c118481f683643cacc3220dafce"

type WintunDevice struct {
	Name                                                string
	GUID                                                windows.GUID
	dll                                                 *windows.DLL
	adapter, session                                    uintptr
	receive, release, allocate, send, end, closeAdapter *windows.Proc
	wake, cancel                                        windows.Handle
	mu                                                  sync.RWMutex
	once                                                sync.Once
	closed                                              atomic.Bool
}

// CreateWintun is an explicit privileged boundary. It is NEVER called by status,
// ordinary SOCKS serve, New, or any isolated packet test. It authors no routes/IP/DNS.
func CreateWintun(dllPath string, approved bool, prepared ...func(string, string) error) (*WintunDevice, error) {
	if !approved {
		return nil, errors.New("separate explicit IP-adapter test approval required")
	}
	if runtime.GOARCH != "amd64" {
		return nil, errors.New("native IP backend currently pins only the official amd64 Wintun DLL")
	}
	absolute, err := filepath.Abs(dllPath)
	if err != nil {
		return nil, err
	}
	path, err := windows.UTF16PtrFromString(absolute)
	if err != nil {
		return nil, err
	}
	// Keep a read-only sharing handle open through verification and LoadDLL so
	// another process cannot replace or write the verified file in between.
	handle, err := windows.CreateFile(path, windows.GENERIC_READ, windows.FILE_SHARE_READ, nil, windows.OPEN_EXISTING, windows.FILE_ATTRIBUTE_NORMAL, 0)
	if err != nil {
		return nil, err
	}
	file := os.NewFile(uintptr(handle), absolute)
	defer file.Close()
	hasher := sha256.New()
	_, err = io.Copy(hasher, file)
	if err != nil {
		return nil, err
	}
	if hex.EncodeToString(hasher.Sum(nil)) != WintunSHA256 {
		return nil, errors.New("Wintun checksum mismatch")
	}
	trust := &windows.WinTrustData{
		Size: uint32(unsafe.Sizeof(windows.WinTrustData{})), UIChoice: windows.WTD_UI_NONE,
		RevocationChecks: windows.WTD_REVOKE_WHOLECHAIN, UnionChoice: windows.WTD_CHOICE_FILE,
		StateAction: windows.WTD_STATEACTION_VERIFY, ProvFlags: windows.WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT,
		FileOrCatalogOrBlobOrSgnrOrCert: unsafe.Pointer(&windows.WinTrustFileInfo{Size: uint32(unsafe.Sizeof(windows.WinTrustFileInfo{})), FilePath: path, File: handle}),
	}
	verifyErr := windows.WinVerifyTrustEx(windows.InvalidHWND, &windows.WINTRUST_ACTION_GENERIC_VERIFY_V2, trust)
	trust.StateAction = windows.WTD_STATEACTION_CLOSE
	closeErr := windows.WinVerifyTrustEx(windows.InvalidHWND, &windows.WINTRUST_ACTION_GENERIC_VERIFY_V2, trust)
	if verifyErr != nil {
		return nil, fmt.Errorf("Wintun Authenticode verification: %w", verifyErr)
	}
	if closeErr != nil {
		return nil, fmt.Errorf("release signature verification: %w", closeErr)
	}
	salt := make([]byte, 16)
	if _, err = rand.Read(salt); err != nil {
		return nil, err
	}
	d := &WintunDevice{Name: "TetherMax-IP-" + hex.EncodeToString(salt[:4])}
	copy(unsafe.Slice((*byte)(unsafe.Pointer(&d.GUID)), 16), salt)
	if len(prepared) > 0 {
		if err := prepared[0](d.Name, d.GUID.String()); err != nil {
			return nil, err
		}
	}
	d.dll, err = windows.LoadDLL(absolute)
	if err != nil {
		return nil, err
	}
	find := func(name string) (*windows.Proc, error) { return d.dll.FindProc(name) }
	var create, start, getEvent *windows.Proc
	for name, dest := range map[string]**windows.Proc{"WintunCreateAdapter": &create, "WintunStartSession": &start, "WintunGetReadWaitEvent": &getEvent, "WintunReceivePacket": &d.receive, "WintunReleaseReceivePacket": &d.release, "WintunAllocateSendPacket": &d.allocate, "WintunSendPacket": &d.send, "WintunEndSession": &d.end, "WintunCloseAdapter": &d.closeAdapter} {
		*dest, err = find(name)
		if err != nil {
			d.dll.Release()
			return nil, err
		}
	}
	d.cancel, err = windows.CreateEvent(nil, 1, 0, nil)
	if err != nil {
		d.dll.Release()
		return nil, err
	}
	name, _ := windows.UTF16PtrFromString(d.Name)
	kind, _ := windows.UTF16PtrFromString("TetherMax")
	d.adapter, _, err = create.Call(uintptr(unsafe.Pointer(name)), uintptr(unsafe.Pointer(kind)), uintptr(unsafe.Pointer(&d.GUID)))
	if d.adapter == 0 {
		d.Close()
		return nil, fmt.Errorf("create owned adapter: %w", err)
	}
	d.session, _, err = start.Call(d.adapter, 0x400000)
	if d.session == 0 {
		d.Close()
		return nil, fmt.Errorf("start Wintun session: %w", err)
	}
	event, _, _ := getEvent.Call(d.session)
	d.wake = windows.Handle(event)
	if d.wake == 0 {
		d.Close()
		return nil, errors.New("missing Wintun read event")
	}
	return d, nil
}
func (d *WintunDevice) ReadPacket(ctx context.Context) ([]byte, error) {
	for {
		if err := ctx.Err(); err != nil {
			return nil, err
		}
		d.mu.RLock()
		if d.closed.Load() {
			d.mu.RUnlock()
			return nil, os.ErrClosed
		}
		var size uint32
		ptr, _, err := d.receive.Call(d.session, uintptr(unsafe.Pointer(&size)))
		if ptr != 0 {
			if size < 20 || size > 65535 {
				d.release.Call(d.session, ptr)
				d.mu.RUnlock()
				return nil, errors.New("invalid Wintun packet size")
			}
			b := make([]byte, int(size))
			var read uintptr
			copyErr := windows.ReadProcessMemory(windows.CurrentProcess(), ptr, &b[0], uintptr(len(b)), &read)
			d.release.Call(d.session, ptr)
			d.mu.RUnlock()
			if copyErr != nil {
				return nil, copyErr
			}
			if read != uintptr(len(b)) {
				return nil, io.ErrUnexpectedEOF
			}
			return b, nil
		}
		if err != windows.ERROR_NO_MORE_ITEMS {
			d.mu.RUnlock()
			return nil, err
		}
		_, waitErr := windows.WaitForMultipleObjects([]windows.Handle{d.cancel, d.wake}, false, 100)
		d.mu.RUnlock()
		if waitErr != nil {
			return nil, waitErr
		}
	}
}
func (d *WintunDevice) WritePacket(ctx context.Context, b []byte) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	if len(b) < 20 || len(b) > 65535 {
		return errors.New("invalid IP packet length")
	}
	d.mu.RLock()
	defer d.mu.RUnlock()
	if d.closed.Load() {
		return os.ErrClosed
	}
	ptr, _, err := d.allocate.Call(d.session, uintptr(len(b)))
	if ptr == 0 {
		return err
	}
	var written uintptr
	if err := windows.WriteProcessMemory(windows.CurrentProcess(), ptr, &b[0], uintptr(len(b)), &written); err != nil {
		return err
	}
	if written != uintptr(len(b)) {
		return io.ErrShortWrite
	}
	d.send.Call(d.session, ptr)
	return nil
}
func (d *WintunDevice) Close() error {
	d.once.Do(func() {
		d.closed.Store(true)
		if d.cancel != 0 {
			windows.SetEvent(d.cancel)
		}
		d.mu.Lock()
		defer d.mu.Unlock()
		if d.session != 0 {
			d.end.Call(d.session)
		}
		if d.adapter != 0 {
			d.closeAdapter.Call(d.adapter)
		}
		if d.cancel != 0 {
			windows.CloseHandle(d.cancel)
		}
		if d.dll != nil {
			d.dll.Release()
		}
	})
	return nil
}
