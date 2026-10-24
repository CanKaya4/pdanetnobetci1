package ipengine

import (
	"context"
	"encoding/binary"
	"errors"
	"io"
	"sync"
)

// PacketDevice keeps packet boundaries; Close must unblock pending reads/writes.
// A device may be a Wintun ring or an entirely in-memory isolated test link.
type PacketDevice interface {
	ReadPacket(context.Context) ([]byte, error)
	WritePacket(context.Context, []byte) error
	Close() error
}

// Run owns only the supplied packet device and this engine. No IP, DNS or route
// configuration occurs here. A failed direction cancels and joins both pumps.
func (e *Engine) Run(ctx context.Context, device PacketDevice) error {
	ctx, cancel := context.WithCancel(ctx)
	defer cancel()
	defer device.Close()
	defer e.Close()
	done := make(chan error, 2)
	var once sync.Once
	stop := func() { once.Do(func() { cancel(); device.Close(); e.Close() }) }
	go func() {
		for {
			packet, err := device.ReadPacket(ctx)
			if err != nil {
				done <- err
				return
			}
			if err = e.Inject(packet); err != nil {
				if errors.Is(err, io.EOF) {
					done <- err
					return
				}
				continue
			}
		}
	}()
	go func() {
		for {
			packet, err := e.ReadPacket(ctx)
			if err != nil {
				done <- err
				return
			}
			if err = device.WritePacket(ctx, packet); err != nil {
				done <- err
				return
			}
		}
	}()
	var first error
	select {
	case first = <-done:
		stop()
		<-done
	case <-ctx.Done():
		first = ctx.Err()
		stop()
		<-done
		<-done
	}
	return first
}

// FramedDevice is a non-privileged packet IPC channel. It cannot create adapters.
// Each frame has a network-order uint16 length followed by one complete IP packet.
type FramedDevice struct {
	Stream  io.ReadWriteCloser
	writeMu sync.Mutex
	once    sync.Once
}

func (d *FramedDevice) ReadPacket(ctx context.Context) ([]byte, error) {
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	var size [2]byte
	if _, err := io.ReadFull(d.Stream, size[:]); err != nil {
		return nil, err
	}
	n := int(binary.BigEndian.Uint16(size[:]))
	if n < 20 {
		return nil, errors.New("invalid packet frame length")
	}
	b := make([]byte, n)
	_, err := io.ReadFull(d.Stream, b)
	return b, err
}
func (d *FramedDevice) WritePacket(ctx context.Context, b []byte) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	if len(b) < 20 || len(b) > 65535 {
		return errors.New("invalid packet frame length")
	}
	d.writeMu.Lock()
	defer d.writeMu.Unlock()
	frame := make([]byte, len(b)+2)
	binary.BigEndian.PutUint16(frame, uint16(len(b)))
	copy(frame[2:], b)
	return writeAll(d.Stream, frame)
}
func (d *FramedDevice) Close() error {
	var err error
	d.once.Do(func() { err = d.Stream.Close() })
	return err
}
