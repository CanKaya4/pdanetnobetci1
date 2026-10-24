//go:build windows

package main

import "github.com/CanKaya4/pdanetnobetci1/host/internal/ipengine"

func nativeIPDevice(dll string, approved bool, commit func(any) error) (ipengine.PacketDevice, any, error) {
	d, err := ipengine.CreateWintun(dll, approved, func(name, guid string) error {
		return commit(map[string]any{"phase": "prepared", "adapter": name, "guid": guid, "routesConfigured": false, "dnsConfigured": false})
	})
	if err != nil {
		return nil, nil, err
	}
	return d, map[string]any{"adapter": d.Name, "guid": d.GUID.String(), "routesConfigured": false, "dnsConfigured": false}, nil
}
