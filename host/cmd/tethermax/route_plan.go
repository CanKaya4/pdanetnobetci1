package main

import (
	"encoding/json"
	"errors"
	"flag"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/routing"
	"net/netip"
	"os"
	"strings"
)

func runRoutePlan(args []string) error {
	f := flag.NewFlagSet("route-plan", flag.ContinueOnError)
	session := f.String("session", "", "unique proposed session ID")
	guid := f.String("adapter-guid", "", "proposed owned adapter GUID; never an existing RNDIS GUID")
	targets := f.String("targets", "", "comma separated IPv4 addresses for isolated /32 testing")
	if err := f.Parse(args); err != nil {
		return err
	}
	if f.NArg() != 0 {
		return errors.New("unexpected route-plan argument")
	}
	var ips []netip.Addr
	for _, s := range strings.Split(*targets, ",") {
		ip, e := netip.ParseAddr(strings.TrimSpace(s))
		if e != nil {
			return e
		}
		ips = append(ips, ip)
	}
	p, err := routing.IsolatedPlan(*session, *guid, ips)
	if err != nil {
		return err
	}
	return json.NewEncoder(os.Stdout).Encode(struct {
		Plan      routing.Plan
		DryRun    bool
		CanApply  bool
		Remaining []string
	}{p, true, false, []string{"native exact-ownership route backend", "durable journal and independent privileged recovery process", "WFP IPv4/IPv6 and DNS leak guard", "isolated native packet tests", "separate physical route approval"}})
}
