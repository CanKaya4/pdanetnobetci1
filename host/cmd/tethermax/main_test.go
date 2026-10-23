package main

import "testing"

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
}
func TestUnknownCommandFails(t *testing.T) {
	if err := run([]string{"connect"}); err == nil {
		t.Fatal("unknown command accepted")
	}
}
