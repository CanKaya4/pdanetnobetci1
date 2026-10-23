//go:build windows

package ipengine

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestNativeAdapterGuardsBeforeLoadingDLL(t *testing.T) {
	if _, err := CreateWintun("does-not-exist.dll", false); err == nil || !strings.Contains(err.Error(), "approval") {
		t.Fatal("adapter approval gate missing")
	}
	if testing.Short() {
		return
	}
	file := filepath.Join(t.TempDir(), "wintun.dll")
	if err := os.WriteFile(file, []byte("unsigned, incorrect DLL fixture"), 0600); err != nil {
		t.Fatal(err)
	}
	prepared := false
	_, err := CreateWintun(file, true, func(string, string) error { prepared = true; return nil })
	if err == nil {
		t.Fatal("tampered DLL accepted")
	}
	if prepared {
		t.Fatal("adapter ownership prepared before DLL validation")
	}
}
