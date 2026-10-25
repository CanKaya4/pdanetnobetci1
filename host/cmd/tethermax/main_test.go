package main

import (
	"os"
	"strings"
	"testing"
)

func TestDefaultIsInactive(t *testing.T) {
	if err := run(nil); err != nil {
		t.Fatal(err)
	}
	if err := run([]string{"status"}); err != nil {
		t.Fatal(err)
	}
}
func TestServeRequiresExplicitApproval(t *testing.T) {
	if err := run([]string{"serve"}); err == nil {
		t.Fatal("serve must refuse before starting any listener")
	}
	if err := run([]string{"serve", "--pairing-stdin"}); err == nil {
		t.Fatal("private pipe must not bypass approval")
	}
}
func TestPipeRejectsMalformedRecords(t *testing.T) {
	for _, input := range []string{"", "short\n", strings.Repeat("a", 33) + "\n", strings.Repeat("z", 32) + "\n"} {
		reader, writer, err := os.Pipe()
		if err != nil {
			t.Fatal(err)
		}
		writer.WriteString(input)
		writer.Close()
		previous := os.Stdin
		os.Stdin = reader
		result := run([]string{"serve", "--approve-usb-test", "--pairing-stdin"})
		os.Stdin = previous
		reader.Close()
		if result == nil {
			t.Fatal("malformed secret accepted")
		}
	}
}
func TestUnknownCommandFails(t *testing.T) {
	if err := run([]string{"connect"}); err == nil {
		t.Fatal("unknown command accepted")
	}
}
