package main

import (
	"context"
	"flag"
	"fmt"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/bridge"
	"net"
	"os"
	"os/signal"
)

func run(args []string) error {
	if len(args) == 0 || args[0] == "status" {
		fmt.Println("TetherMax 0.1.0-engine: inactive. No adapter, route, DNS, PdaNet or MyPublicWiFi changes. USB TCP/UDP SOCKS engine; system-wide TUN and adaptive QoS are not implemented.")
		return nil
	}
	if args[0] != "serve" {
		return fmt.Errorf("usage: tethermax [status | serve --approve-usb-test --transport 127.0.0.1:38473 --listen 127.0.0.1:1088]")
	}
	flags := flag.NewFlagSet("serve", flag.ContinueOnError)
	approved := flags.Bool("approve-usb-test", false, "explicit operator approval for physical USB traffic test")
	address := flags.String("transport", "127.0.0.1:38473", "existing ADB forward endpoint")
	listen := flags.String("listen", "127.0.0.1:1088", "loopback SOCKS5 address")
	if err := flags.Parse(args[1:]); err != nil {
		return err
	}
	if flags.NArg() != 0 {
		return fmt.Errorf("unexpected positional arguments")
	}
	if !*approved {
		return fmt.Errorf("physical testing requires separate approval; pass --approve-usb-test only after obtaining it")
	}
	if err := bridge.LoopbackAddress(*listen); err != nil {
		return err
	}
	transport := bridge.Transport{Address: *address, Token: os.Getenv("TETHERMAX_PAIRING_CODE")}
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
	return (bridge.Server{Transport: transport}).Serve(ctx, listener)
}
func main() {
	if err := run(os.Args[1:]); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
