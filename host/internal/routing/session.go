// Package routing contains the ownership transaction for future native routing.
// It deliberately has no OS backend: callers cannot activate system routing yet.
package routing

import (
	"context"
	"errors"
	"fmt"
	"net/netip"
	"time"
)

type Route struct {
	Prefix        netip.Prefix
	InterfaceGUID string
	NextHop       netip.Addr
	Metric        uint32
}
type Plan struct {
	Session     string
	AdapterGUID string
	Routes      []Route
}

// IsolatedPlan is limited to explicitly chosen remote IPv4 /32 test addresses.
// Default/split-default routes and DNS changes must wait for a native leak guard.
func IsolatedPlan(session, guid string, targets []netip.Addr) (Plan, error) {
	p := Plan{Session: session, AdapterGUID: guid}
	if session == "" || guid == "" || len(targets) == 0 || len(targets) > 16 {
		return p, errors.New("session, owned adapter and 1..16 targets required")
	}
	seen := map[netip.Addr]bool{}
	for _, ip := range targets {
		if !ip.Is4() || !ip.IsGlobalUnicast() || ip.IsPrivate() || ip.IsLinkLocalUnicast() || seen[ip] {
			return Plan{}, errors.New("targets must be distinct public IPv4 addresses")
		}
		seen[ip] = true
		p.Routes = append(p.Routes, Route{netip.PrefixFrom(ip, 32), guid, netip.IPv4Unspecified(), 1})
	}
	return p, nil
}

// Backend must use exact GUID/prefix/next-hop/metric identity, never wildcard
// deletion. Add must refuse pre-existing entries. RemoveOwned must be idempotent,
// must refuse a replaced entry, and must never delete an unowned route.
type Backend interface {
	Add(context.Context, string, Route) error
	RemoveOwned(context.Context, string, Route) error
}

// Journal records intent durably BEFORE any mutation. Recovery removes all
// intents, including an Add which succeeded before its acknowledgement was lost.
type Journal interface {
	Intent(Plan) error
	Complete() error
}
type Guard interface {
	// Arm starts an independent supervisor and acknowledges durable recovery state.
	// The supervisor must survive the traffic worker and parent GUI exiting.
	Arm(Plan) error
	Disarm() error
}

func validate(p Plan) error {
	targets := make([]netip.Addr, len(p.Routes))
	for i, r := range p.Routes {
		if r.Prefix.Bits() != 32 || r.InterfaceGUID != p.AdapterGUID || r.NextHop != netip.IPv4Unspecified() || r.Metric != 1 {
			return errors.New("only scoped isolated routes allowed; global routing unavailable")
		}
		targets[i] = r.Prefix.Addr()
	}
	_, err := IsolatedPlan(p.Session, p.AdapterGUID, targets)
	return err
}

// Run owns only the provided routes. Cancellation/heartbeat loss must cancel
// ctx. A separate recovery deadline permits rollback after ctx cancellation.
// Rollback failures leave the independent guard armed and journal incomplete.
func Run(ctx context.Context, p Plan, b Backend, j Journal, g Guard, work func(context.Context) error) (err error) {
	if err = validate(p); err != nil {
		return err
	}
	if b == nil || j == nil || g == nil || work == nil {
		return errors.New("missing safety component")
	}
	if err = j.Intent(p); err != nil {
		return err
	}
	if err = g.Arm(p); err != nil {
		return err
	}
	defer func() {
		recovery, cancel := context.WithTimeout(context.Background(), 15*time.Second)
		defer cancel()
		var failures error
		for i := len(p.Routes) - 1; i >= 0; i-- {
			failures = errors.Join(failures, b.RemoveOwned(recovery, p.Session, p.Routes[i]))
		}
		if failures == nil {
			failures = j.Complete()
		}
		if failures == nil {
			failures = g.Disarm()
		}
		if failures != nil {
			err = errors.Join(err, fmt.Errorf("rollback incomplete; independent recovery retained: %w", failures))
		}
	}()
	for _, r := range p.Routes {
		if err = ctx.Err(); err != nil {
			return err
		}
		if err = b.Add(ctx, p.Session, r); err != nil {
			return err
		}
	}
	return work(ctx)
}
