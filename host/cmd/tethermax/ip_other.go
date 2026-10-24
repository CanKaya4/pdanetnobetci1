//go:build !windows

package main

import (
	"errors"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/ipengine"
)

func nativeIPDevice(string, bool, func(any) error) (ipengine.PacketDevice, any, error) {
	return nil, nil, errors.New("native Wintun backend requires Windows; use isolated ip-stdio instead")
}
