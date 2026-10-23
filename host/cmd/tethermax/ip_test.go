package main

import (
	"bytes"
	"strings"
	"testing"
)

func TestIPWorkerDoesNotCreateAdapterWithoutApproval(t *testing.T) {
	if err := run([]string{"ip-worker"}); err == nil || !strings.Contains(err.Error(), "approval") {
		t.Fatal("adapter guard missing")
	}
	if err := run([]string{"ip-stdio"}); err == nil || !strings.Contains(err.Error(), "USB test approval") {
		t.Fatal("USB guard missing")
	}
}
func TestPrivateSecretDoesNotConsumePacketFrame(t *testing.T) {
	source := bytes.NewBufferString(strings.Repeat("a", 32) + "\nXYZ")
	secret, err := readPrivateSecret(source)
	if err != nil || len(secret) != 32 {
		t.Fatal(err)
	}
	if source.String() != "XYZ" {
		t.Fatal("packet prefix consumed by secret reader")
	}
	for _, value := range []string{"short", strings.Repeat("a", 33)} {
		if _, err := readPrivateSecret(strings.NewReader(value)); err == nil {
			t.Fatal("bad secret framing accepted")
		}
	}
}
