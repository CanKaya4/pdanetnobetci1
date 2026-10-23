package ipengine

import (
	"context"
	"testing"
	"time"
)

func TestProbeRejectsNonCellularBeforeTraffic(t *testing.T) {
	transport := fixture(t)
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	result := ProbeUSB(ctx, transport)
	if result.Passed || len(result.Errors) == 0 || result.Counters.Flows != 0 || result.AndroidBefore != nil || result.SystemWideVerified || result.HotspotOffVerified {
		t.Fatal("non-cellular fixture was treated as phone evidence", result)
	}
	control, err := transport.OpenControl(ctx)
	if err != nil {
		t.Fatal(err)
	}
	defer control.Close()
	status, err := control.Status()
	if err != nil {
		t.Fatal(err)
	}
	if status.Uploaded != 0 || status.Downloaded != 0 {
		t.Fatal("probe sent data before cellular attestation", status)
	}
}
