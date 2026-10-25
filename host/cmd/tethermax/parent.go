package main

import (
	"context"
	"encoding/binary"
	"errors"
	"io"
	"time"
)

// watchParent owns only the worker's private heartbeat pipe. Closing it on every
// exit joins the blocked reader, including when a parent stays alive but stalls.
// This does not recover OS routes; this worker never configures any.
func watchParent(ctx context.Context, input io.ReadCloser, timeout time.Duration) error {
	return watchNativeParent(ctx, input, timeout, nil)
}

func watchNativeParent(ctx context.Context, input io.ReadCloser, timeout time.Duration, settings func([]byte) error) error {
	ctx, cancel := context.WithCancel(ctx)
	defer cancel()
	heartbeat := make(chan error)
	done := make(chan struct{})
	go func() {
		defer close(done)
		var b [1]byte
		for {
			_, err := io.ReadFull(input, b[:])
			if err == nil && b[0] == 1 && settings != nil {
				var size [2]byte
				_, err = io.ReadFull(input, size[:])
				n := int(binary.BigEndian.Uint16(size[:]))
				if err == nil && (n < 2 || n > 2048) {
					err = errors.New("invalid settings frame length")
				}
				if err == nil {
					data := make([]byte, n)
					_, err = io.ReadFull(input, data)
					if err == nil {
						err = settings(data)
					}
				}
			} else if err == nil && b[0] != 0 {
				err = errors.New("invalid parent heartbeat")
			}
			select {
			case heartbeat <- err:
			case <-ctx.Done():
				return
			}
			if err != nil {
				return
			}
		}
	}()
	defer func() { cancel(); input.Close(); <-done }()
	timer := time.NewTimer(timeout)
	defer timer.Stop()
	for {
		select {
		case <-ctx.Done():
			return ctx.Err()
		case err := <-heartbeat:
			if err != nil {
				return err
			}
			timer.Reset(timeout)
		case <-timer.C:
			return errors.New("parent heartbeat timed out")
		}
	}
}
