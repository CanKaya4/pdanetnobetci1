package main

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/bridge"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/traffic"
	"os"
	"time"
)

// Uses the existing authenticated relay control channel. No worker stdin,
// packet framing, adapter, route, DNS or guardian protocol changes.
func runProfile(args []string) error {
	f := flag.NewFlagSet("profile-set", flag.ContinueOnError)
	approved := f.Bool("approve-usb-test", false, "explicit selected USB control")
	address := f.String("transport", "127.0.0.1:38473", "selected existing USB forward")
	mode := f.String("mode", "", "requested scheduler mode")
	if err := f.Parse(args); err != nil {
		return err
	}
	setting := traffic.Settings{Mode: traffic.Mode(*mode)}
	if !*approved || f.NArg() != 0 {
		return errors.New("selected USB profile requires explicit approval")
	}
	if err := setting.Validate(); err != nil {
		return err
	}
	secret, err := readPrivateSecret(os.Stdin)
	if err != nil {
		return err
	}
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	c, err := (bridge.Transport{Address: *address, Token: secret}).OpenControl(ctx)
	secret = ""
	if err != nil {
		return err
	}
	defer c.Close()
	if err = c.SetMode(setting.Mode); err != nil {
		return err
	}
	status, err := c.Status()
	if err != nil {
		return err
	}
	if status.Mode != setting.Mode {
		return errors.New("phone profile acknowledgement mismatch")
	}
	return json.NewEncoder(os.Stdout).Encode(status)
}
