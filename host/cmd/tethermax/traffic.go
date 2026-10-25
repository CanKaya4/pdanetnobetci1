package main

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"flag"
	"io"
	"net/netip"
	"strings"
	"time"

	"github.com/CanKaya4/pdanetnobetci1/host/internal/bridge"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/traffic"
)

type trafficFlags struct {
	mode, shared, dns            *string
	up, down                     *float64
	adaptive, control, telemetry *bool
}

func addTrafficFlags(f *flag.FlagSet) trafficFlags {
	return trafficFlags{
		dns:       f.String("dns-profile", "system", "system/adguard/family/cloudflare"),
		mode:      f.String("profile", "auto", "auto/gaming/streaming/daily/maximum"),
		up:        f.Float64("upload-mbps", 0, "explicit measured upload ceiling; 0 leaves pacing uncalibrated"),
		down:      f.Float64("download-mbps", 0, "delivery ceiling; does not control cellular UDP radio capacity"),
		adaptive:  f.Bool("adaptive", false, "enable cellular DNS delay probes and adaptive pacing; consumes mobile data"),
		control:   f.Bool("traffic-control-stdin", false, "private live settings pipe; EOF stops only this worker"),
		telemetry: f.Bool("traffic-telemetry", false, "emit measured JSON traffic telemetry"),
		shared:    f.String("shared-clients", "", "explicit IPv4 source CIDRs for already forwarded shared-client traffic; no ICS configuration"),
	}
}
func (f trafficFlags) manager() (*traffic.Manager, error) {
	return traffic.New(traffic.Settings{DNSProfile: *f.dns, Mode: traffic.Mode(*f.mode), UploadMbps: *f.up, DownloadMbps: *f.down, Adaptive: *f.adaptive})
}
func (f trafficFlags) prefixes() ([]netip.Prefix, error) {
	var out []netip.Prefix
	if *f.shared == "" {
		return out, nil
	}
	for _, raw := range strings.Split(*f.shared, ",") {
		p, err := netip.ParsePrefix(strings.TrimSpace(raw))
		if err != nil || !p.Addr().Is4() || !p.Addr().IsPrivate() || p.Bits() < 8 {
			return nil, errors.New("shared-client classifiers require explicit private IPv4 CIDRs")
		}
		out = append(out, p.Masked())
	}
	return out, nil
}
func decodeTrafficSettings(line []byte) (traffic.Settings, error) {
	var s traffic.Settings
	decoder := json.NewDecoder(bytes.NewReader(line))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(&s); err != nil {
		return s, errors.New("invalid private traffic settings")
	}
	if err := decoder.Decode(new(any)); err != io.EOF {
		return s, errors.New("multiple private settings records")
	}
	return s, s.Validate()
}
func startTrafficRuntime(parent context.Context, cancelParent context.CancelFunc, f trafficFlags, m *traffic.Manager, t bridge.Transport, input io.ReadCloser, output io.Writer) func() {
	ctx, cancel := context.WithCancel(parent)
	done := make(chan struct{})
	commands := make(chan []byte, 16)
	readerDone := make(chan struct{})
	if *f.control {
		go func() {
			defer close(readerDone)
			defer cancelParent()
			scanner := bufio.NewScanner(input)
			scanner.Buffer(make([]byte, 1024), 8192)
			for scanner.Scan() {
				line := append([]byte(nil), scanner.Bytes()...)
				select {
				case commands <- line:
				case <-ctx.Done():
					return
				}
			}
		}()
	} else {
		close(readerDone)
	}
	go func() {
		defer close(done)
		ticker := time.NewTicker(time.Second)
		defer ticker.Stop()
		var remote *bridge.Control
		defer func() {
			if remote != nil {
				remote.Close()
			}
		}()
		pending := m.Settings().Mode
		pendingDNS := m.Settings().DNSProfile
		if pendingDNS == "" {
			pendingDNS = "system"
		}
		confirmed := false
		var seen uint64
		phone := "unavailable"
		lastNetwork := ""
		ticks := 0
		emit := func(v any) {
			if output != nil {
				json.NewEncoder(output).Encode(v)
			}
		}
		for {
			select {
			case <-ctx.Done():
				return
			case line := <-commands:
				s, err := decodeTrafficSettings(line)
				if err == nil {
					err = m.Configure(s)
				}
				if err != nil {
					emit(map[string]any{"type": "traffic-error", "message": err.Error()})
				} else {
					pending = s.Mode
					pendingDNS = s.DNSProfile
					if pendingDNS == "" {
						pendingDNS = "system"
					}
					confirmed = false
				}
			case <-ticker.C:
				ticks++
				if ticks%2 == 1 {
					if names, err := traffic.RunningGames(); err == nil {
						m.SetGames(names)
					}
				}
				settings := m.Settings()
				if *f.control || *f.telemetry || settings.Adaptive {
					if remote == nil {
						dialCtx, stop := context.WithTimeout(ctx, 2*time.Second)
						remote, _ = t.OpenControl(dialCtx)
						stop()
					}
					if remote != nil {
						var err error
						if pending != "" {
							err = remote.SetMode(pending)
							if err == nil {
								pending = ""
							}
						}
						if err == nil && pendingDNS != "" {
							err = remote.SetDNS(pendingDNS)
							if err == nil {
								pendingDNS = ""
							}
						}
						if err == nil {
							var status bridge.RemoteStatus
							status, err = remote.Status()
							if err == nil {
								phone = status.Network
								if strings.HasPrefix(phone, "CELLULAR ") && phone != lastNetwork {
									if lastNetwork != "" {
										m.ResetMeasurements()
									}
									lastNetwork = phone
								}
								if status.Revision != seen && (status.Mode != m.Settings().Mode || status.DNSProfile != m.Settings().DNSProfile) {
									change := m.Settings()
									change.Mode = status.Mode
									change.DNSProfile = status.DNSProfile
									err = m.Configure(change)
								}
								seen = status.Revision
								confirmed = pending == "" && pendingDNS == "" && status.Mode == m.Settings().Mode && status.DNSProfile == m.Settings().DNSProfile
							}
						}
						if err != nil {
							remote.Close()
							remote = nil
							phone = "control-unavailable"
							confirmed = false
						}
					}
				}
				if m.Settings().Adaptive {
					rtt, err := t.QueryRTT(ctx)
					m.Observe(time.Now(), rtt, err == nil)
				}
				snapshot := m.Snapshot()
				if remote != nil {
					if err := remote.Publish(snapshot); err != nil {
						remote.Close()
						remote = nil
						phone = "control-unavailable"
						confirmed = false
					}
				}
				emit(struct {
					traffic.Snapshot
					PhoneNetwork           string `json:"phoneNetwork"`
					PhoneSettingsConfirmed bool   `json:"phoneSettingsConfirmed"`
				}{snapshot, phone, confirmed})
			}
		}
	}()
	return func() {
		cancel()
		if *f.control {
			input.Close()
		}
		<-readerDone
		<-done
	}
}
