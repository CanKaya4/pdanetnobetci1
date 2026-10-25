package main

import "testing"

func TestProfileCommandRejectsUnapprovedAndInvalidBeforeReadingSecret(t *testing.T) {
	for _, args := range [][]string{{"--mode", "gaming"}, {"--approve-usb-test", "--mode", "bad"}, {"--approve-usb-test", "--mode", "gaming", "extra"}} {
		if runProfile(args) == nil {
			t.Fatal("invalid/unapproved profile accepted", args)
		}
	}
}
