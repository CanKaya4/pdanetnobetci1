package main

import (
	"bytes"
	"io"
	"strings"
	"testing"
	"time"
)

func TestIPWorkerDoesNotCreateAdapterWithoutApproval(t *testing.T) {
	if err := run([]string{"ip-worker"}); err == nil || !strings.Contains(err.Error(), "approval") {
		t.Fatal("adapter guard missing")
	}
	if err := run([]string{"ip-stdio"}); err == nil || !strings.Contains(err.Error(), "USB test approval") {
		t.Fatal("USB guard missing")
	}
}

func TestStdioIPCShutdownUnblocksReaderAndWriter(t *testing.T) {
	in, inputPeer := io.Pipe()
	outputPeer, out := io.Pipe()
	defer inputPeer.Close()
	defer outputPeer.Close()
	stream := &stdioStream{input: in, output: out}
	done := make(chan error, 2)
	go func() { _, err := stream.Read(make([]byte, 1)); done <- err }()
	go func() { _, err := stream.Write([]byte{1}); done <- err }()
	stream.Close()
	stream.Close()
	for range 2 {
		select {
		case err := <-done:
			if err == nil {
				t.Fatal("IPC direction stayed open")
			}
		case <-time.After(time.Second):
			t.Fatal("blocked packet IPC worker leaked")
		}
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
