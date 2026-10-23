package main

import (
	"context"
	"flag"
	"fmt"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/bridge"
	"io"
	"net"
	"os"
	"os/signal"
)

func run(args []string) error {
	if len(args) > 0 && args[0] == "cellular-status" {
		return runCellularStatus(args[1:])
	}
	if len(args) > 0 && args[0] == "route-plan" {
		return runRoutePlan(args[1:])
	}
	if len(args) > 0 && args[0] == "ip-check" {
		return runIPCheck(args[1:])
	}
	if len(args) > 0 && args[0] == "ip-stdio" {
		return runIP(args[1:], true)
	}
	if len(args) > 0 && args[0] == "ip-worker" {
		return runIP(args[1:], false)
	}
	if len(args) == 0 || args[0] == "status" {
		fmt.Println("TetherMax 0.5.0-engine: inactive. No OS changes. Wintun data worker and separately approved Windows network guardian available; use the GUI for explicit routing consent.")
		return nil
	}
	if args[0] != "serve" {
		return fmt.Errorf("usage: tethermax [status | serve --approve-usb-test --transport 127.0.0.1:38473 --listen 127.0.0.1:1088]")
	}
	flags := flag.NewFlagSet("serve", flag.ContinueOnError)
	approved := flags.Bool("approve-usb-test", false, "explicit operator approval for physical USB traffic test")
	stdinPairing := flags.Bool("pairing-stdin", false, "read pairing secret from inherited private stdin pipe")
	address := flags.String("transport", "127.0.0.1:38473", "existing ADB forward endpoint")
	listen := flags.String("listen", "127.0.0.1:1088", "loopback SOCKS5 address")
	qos := addTrafficFlags(flags)
	if err := flags.Parse(args[1:]); err != nil {
		return err
	}
	if flags.NArg() != 0 {
		return fmt.Errorf("unexpected positional arguments")
	}
	if !*approved {
		return fmt.Errorf("physical testing requires separate approval; pass --approve-usb-test only after obtaining it")
	}
	if *qos.control && !*stdinPairing {
		return fmt.Errorf("live traffic control requires the inherited private pairing pipe")
	}
	manager, err := qos.manager()
	if err != nil {
		return err
	}
	defer manager.Close()
	if err := bridge.LoopbackAddress(*listen); err != nil {
		return err
	}
	token := os.Getenv("TETHERMAX_PAIRING_CODE")
	if *stdinPairing {
		secret, err := readPrivateSecret(os.Stdin)
		if err != nil {
			return fmt.Errorf("invalid pairing input")
		}
		token = secret
	}
	transport := bridge.Transport{Address: *address, Token: token}
	if err := transport.Validate(); err != nil {
		return err
	}
	listener, err := net.Listen("tcp", *listen)
	if err != nil {
		return err
	}
	defer listener.Close()
	ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt)
	defer cancel()
	fmt.Printf("Loopback SOCKS5 listening at %s. No OS network configuration changed.\n", listener.Addr())
	var output io.Writer
	if *qos.telemetry || *qos.control {
		output = os.Stdout
	}
	stop := startTrafficRuntime(ctx, cancel, qos, manager, transport, os.Stdin, output)
	defer stop()
	return (bridge.Server{Transport: transport, Traffic: manager}).Serve(ctx, listener)
}
func main() {
	if err := run(os.Args[1:]); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
