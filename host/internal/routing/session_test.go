package routing

import (
	"context"
	"errors"
	"net/netip"
	"testing"
)

type fixture struct {
	events                                    []string
	failAdd, failRemove, failJournal, failArm bool
	armed                                     bool
	installed                                 map[Route]bool
}

func (f *fixture) Intent(Plan) error {
	f.events = append(f.events, "intent")
	if f.failJournal {
		return errors.New("disk full")
	}
	return nil
}
func (f *fixture) Complete() error { f.events = append(f.events, "complete"); return nil }
func (f *fixture) Arm(Plan) error {
	f.events = append(f.events, "arm")
	if f.failArm {
		return errors.New("guard missing")
	}
	f.armed = true
	return nil
}
func (f *fixture) Disarm() error { f.events = append(f.events, "disarm"); f.armed = false; return nil }
func (f *fixture) Add(_ context.Context, _ string, r Route) error {
	f.events = append(f.events, "add")
	f.installed[r] = true
	if f.failAdd {
		return errors.New("ack lost after mutation")
	}
	return nil
}
func (f *fixture) RemoveOwned(ctx context.Context, _ string, r Route) error {
	if ctx.Err() != nil {
		return ctx.Err()
	}
	f.events = append(f.events, "remove")
	if f.failRemove {
		return errors.New("access denied")
	}
	delete(f.installed, r)
	return nil
}
func TestTransaction(t *testing.T) {
	p, err := IsolatedPlan("owned-session", "owned-guid", []netip.Addr{netip.MustParseAddr("1.1.1.1"), netip.MustParseAddr("8.8.8.8")})
	if err != nil {
		t.Fatal(err)
	}
	for _, mode := range []string{"success", "cancel", "ack-lost", "rollback-failed", "journal-failed", "guard-failed"} {
		t.Run(mode, func(t *testing.T) {
			f := &fixture{installed: map[Route]bool{}, failAdd: mode == "ack-lost", failRemove: mode == "rollback-failed", failJournal: mode == "journal-failed", failArm: mode == "guard-failed"}
			ctx, cancel := context.WithCancel(context.Background())
			defer cancel()
			err := Run(ctx, p, f, f, f, func(context.Context) error {
				if mode == "cancel" {
					cancel()
					return ctx.Err()
				}
				return nil
			})
			if (err == nil) != (mode == "success") {
				t.Fatal(mode, err)
			}
			if mode == "rollback-failed" {
				if !f.armed || len(f.installed) != 2 {
					t.Fatal("recovery lost", f)
				}
			} else if f.armed || len(f.installed) != 0 {
				t.Fatal("owned resource leak", f)
			}
			if mode == "success" && (len(f.events) != 8 || f.events[0] != "intent" || f.events[1] != "arm" || f.events[7] != "disarm") {
				t.Fatal(f.events)
			}
		})
	}
}
func TestRejectGlobalAndUnsafeTargets(t *testing.T) {
	for _, s := range []string{"0.0.0.0", "127.0.0.1", "192.168.1.1", "169.254.1.1", "224.0.0.1", "::1"} {
		if _, e := IsolatedPlan("s", "g", []netip.Addr{netip.MustParseAddr(s)}); e == nil {
			t.Fatal(s)
		}
	}
	p, _ := IsolatedPlan("s", "g", []netip.Addr{netip.MustParseAddr("1.1.1.1")})
	p.Routes[0].Prefix = netip.MustParsePrefix("0.0.0.0/1")
	f := &fixture{}
	if Run(context.Background(), p, f, f, f, func(context.Context) error { return nil }) == nil || len(f.events) != 0 {
		t.Fatal("global mutation admitted")
	}
}
