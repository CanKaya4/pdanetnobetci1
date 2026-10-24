package main

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/bridge"
	"os"
	"strings"
	"time"
)

// Reads relay-local status only. No public DNS/HTTP or OS network changes.
func runCellularStatus(args []string) error {
	f := flag.NewFlagSet("cellular-status", flag.ContinueOnError)
	approved := f.Bool("approve-usb-test", false, "explicit selected USB status approval")
	address := f.String("transport", "127.0.0.1:38473", "existing selected USB forward")
	if err := f.Parse(args); err != nil {
		return err
	}
	if !*approved || f.NArg() != 0 {
		return errors.New("selected USB status requires separate approval")
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
	s, err := c.Status()
	if err != nil {
		return err
	}
	if err = json.NewEncoder(os.Stdout).Encode(s); err != nil {
		return err
	}
	if !strings.HasPrefix(s.Network, "CELLULAR ") {
		return errors.New("cellular socket source is unavailable")
	}
	return nil
}
