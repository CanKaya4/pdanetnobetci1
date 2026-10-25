package ipengine

import (
	"context"
	"errors"
	"net"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

type countConn struct{ count *atomic.Int64 }

func (c countConn) Write(p []byte) (int, error)    { c.count.Add(int64(len(p))); return len(p), nil }
func (c countConn) Read(p []byte) (int, error)     { c.count.Add(int64(len(p))); return len(p), nil }
func (countConn) Close() error                     { return nil }
func (countConn) LocalAddr() net.Addr              { return nil }
func (countConn) RemoteAddr() net.Addr             { return nil }
func (countConn) SetDeadline(time.Time) error      { return nil }
func (countConn) SetReadDeadline(time.Time) error  { return nil }
func (countConn) SetWriteDeadline(time.Time) error { return nil }

type countTransport struct{ count atomic.Int64 }

func (*countTransport) Validate() error { return nil }
func (t *countTransport) DialContext(context.Context, byte, string, uint16) (net.Conn, error) {
	return countConn{&t.count}, nil
}
func TestConcurrentBudgetNeverOverspends(t *testing.T) {
	base := &countTransport{}
	var notifications atomic.Int64
	b := NewBudgetTransport(base, 10000, func() { notifications.Add(1) })
	var wg sync.WaitGroup
	for i := 0; i < 128; i++ {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			c, err := b.DialContext(context.Background(), 1, "fixture", 1)
			if err != nil {
				return
			}
			defer c.Close()
			for range 10 {
				if i%2 == 0 {
					_, err = c.Write(make([]byte, 2048))
				} else {
					_, err = c.Read(make([]byte, 2048))
				}
				if err != nil {
					return
				}
			}
		}(i)
	}
	wg.Wait()
	if base.count.Load() != 10000 || notifications.Load() != 1 {
		t.Fatal(base.count.Load(), notifications.Load())
	}
	if _, err := b.DialContext(context.Background(), 1, "fixture", 1); !errors.Is(err, ErrPayloadBudget) {
		t.Fatal(err)
	}
}
