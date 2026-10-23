package main

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/bridge"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/ipengine"
	"os"
	"time"
)

func runIPCheck(args []string) error {
	flags := flag.NewFlagSet("ip-check", flag.ContinueOnError)
	approved := flags.Bool("approve-usb-test", false, "separate authorization for bounded real cellular test")
	address := flags.String("transport", "127.0.0.1:38473", "selected USB localhost forward")
	if err := flags.Parse(args); err != nil {
		return err
	}
	if !*approved {
		return errors.New("physical IP diagnostic requires USB test approval")
	}
	if flags.NArg() != 0 {
		return errors.New("unexpected diagnostic argument")
	}
	secret, err := readPrivateSecret(os.Stdin)
	if err != nil {
		return err
	}
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()
	result := ipengine.ProbeUSB(ctx, bridge.Transport{Address: *address, Token: secret})
	secret = ""
	json.NewEncoder(os.Stdout).Encode(result)
	if !result.Passed {
		return errors.New("IP diagnostic failed; see structured results")
	}
	return nil
}
