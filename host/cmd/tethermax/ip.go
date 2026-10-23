package main

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/bridge"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/ipengine"
	"io"
	"os"
	"os/signal"
	"path/filepath"
	"sync"
	"time"
)

type stdioStream struct {
	input  io.ReadCloser
	output io.WriteCloser
	once   sync.Once
}

func (s *stdioStream) Read(b []byte) (int, error)  { return s.input.Read(b) }
func (s *stdioStream) Write(b []byte) (int, error) { return s.output.Write(b) }
func (s *stdioStream) Close() error {
	s.once.Do(func() { s.input.Close(); s.output.Close() })
	return nil
}
func readPrivateSecret(r io.Reader) (string, error) {
	b := make([]byte, 33)
	if _, err := io.ReadFull(r, b); err != nil {
		return "", errors.New("private secret pipe is incomplete")
	}
	if b[32] != '\n' {
		return "", errors.New("invalid private secret framing")
	}
	return string(b[:32]), nil
}

// runIP never applies IP/DNS/routes. Adapter creation is an independent, denied
// by default boundary; parent heartbeat EOF/timeout tears down owned resources.
func runIP(args []string, framed bool) error {
	flags := flag.NewFlagSet("ip-worker", flag.ContinueOnError)
	approve := flags.Bool("approve-adapter-change", false, "separate user approval to create this worker's owned IP adapter")
	usbApproved := flags.Bool("approve-usb-test", false, "explicit approval for selected USB transport use")
	transport := flags.String("transport", "127.0.0.1:38473", "existing selected-device USB forward")
	mtu := flags.Uint("mtu", 1500, "IPv4 link MTU")
	dll := flags.String("wintun-dll", "", "absolute path to verified Wintun DLL")
	journal := flags.String("journal", "", "new ownership journal path (required for native adapter mode)")
	qos := addTrafficFlags(flags)
	if err := flags.Parse(args); err != nil {
		return err
	}
	if flags.NArg() != 0 {
		return errors.New("unexpected IP worker argument")
	}
	if !framed && !*approve {
		return errors.New("IP adapter creation requires separate explicit approval; no adapter loaded")
	}
	if !*usbApproved {
		return errors.New("IP transport use requires explicit USB test approval")
	}
	if *qos.control {
		return errors.New("IP IPC reserves stdin for packets or heartbeat; use initial profile flags")
	}
	prefixes, err := qos.prefixes()
	if err != nil {
		return err
	}
	manager, err := qos.manager()
	if err != nil {
		return err
	}
	defer manager.Close()
	if !framed && (*journal == "" || !filepath.IsAbs(*journal) || !filepath.IsAbs(*dll)) {
		return errors.New("native IP worker requires absolute DLL and new journal paths")
	}
	if *mtu < 576 || *mtu > 65535 {
		return errors.New("invalid IPv4 MTU")
	}
	secret, err := readPrivateSecret(os.Stdin)
	if err != nil {
		return err
	}
	link := bridge.Transport{Address: *transport, Token: secret}
	e, err := ipengine.New(ipengine.Config{Transport: link, MTU: uint32(*mtu), Traffic: manager, SharedClients: prefixes})
	secret = ""
	if err != nil {
		return err
	}
	defer e.Close()
	ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt)
	defer cancel()
	var output io.Writer
	if *qos.telemetry {
		output = os.Stdout
		if framed {
			output = os.Stderr
		}
	}
	stop := startTrafficRuntime(ctx, cancel, qos, manager, link, os.Stdin, output)
	defer stop()
	if framed {
		return e.Run(ctx, &ipengine.FramedDevice{Stream: &stdioStream{input: os.Stdin, output: os.Stdout}})
	}
	// Reserve an immutable, previously nonexistent ownership journal before mutation.
	f, err := os.OpenFile(*journal, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
	if err != nil {
		return err
	}
	defer f.Close()
	commit := func(value any) error {
		if err := json.NewEncoder(f).Encode(value); err != nil {
			return err
		}
		return f.Sync()
	}
	device, identity, err := nativeIPDevice(*dll, *approve, commit)
	if err != nil {
		json.NewEncoder(f).Encode(map[string]any{"error": err.Error()})
		return err
	}
	defer func() {
		device.Close()
		// Native close is not proof of completed asynchronous PnP removal.
		// Preserve that distinction for a future privileged recovery controller.
		commit(map[string]any{"phase": "handles-closed", "identity": identity, "pnpRemovalVerified": false})
	}()
	if err = json.NewEncoder(f).Encode(identity); err != nil {
		device.Close()
		return err
	}
	if err = f.Sync(); err != nil {
		return err
	}
	json.NewEncoder(os.Stdout).Encode(identity)
	go func() {
		ticker := time.NewTicker(time.Second)
		defer ticker.Stop()
		for {
			select {
			case <-ctx.Done():
				return
			case <-ticker.C:
				json.NewEncoder(os.Stdout).Encode(map[string]any{"type": "ip-counters", "counters": e.Counters()})
			}
		}
	}()
	watchDone := make(chan struct{})
	go func() {
		defer close(watchDone)
		watchParent(ctx, os.Stdin, 5*time.Second)
		cancel()
	}()
	defer func() { cancel(); <-watchDone }()
	if err = e.Run(ctx, device); err != nil && !errors.Is(err, context.Canceled) {
		return fmt.Errorf("IP worker: %w", err)
	}
	return nil
}
