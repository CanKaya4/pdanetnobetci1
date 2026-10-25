package ipengine

import (
	"context"
	"errors"
	"net"
	"sync"
	"sync/atomic"
)

var ErrPayloadBudget = errors.New("approved relay payload budget exhausted")

// BudgetTransport caps aggregate relay payload/framing bytes across all flows.
// It does not claim to cap carrier billing (TCP/IP/ADB overhead, retransmits and
// Android resolver traffic are outside this application measurement).
type BudgetTransport struct {
	FlowTransport
	remaining atomic.Int64
	once      sync.Once
	Exhausted func()
}

func NewBudgetTransport(base FlowTransport, bytes int64, exhausted func()) *BudgetTransport {
	b := &BudgetTransport{FlowTransport: base, Exhausted: exhausted}
	b.remaining.Store(bytes)
	return b
}
func (b *BudgetTransport) take(wanted int) int {
	for {
		left := b.remaining.Load()
		if left <= 0 {
			return 0
		}
		n := min(int64(wanted), left)
		if b.remaining.CompareAndSwap(left, left-n) {
			return int(n)
		}
	}
}
func (b *BudgetTransport) refund(n int) {
	if n > 0 {
		b.remaining.Add(int64(n))
	}
}
func (b *BudgetTransport) exhausted() {
	b.once.Do(func() {
		if b.Exhausted != nil {
			b.Exhausted()
		}
	})
}
func (b *BudgetTransport) DialContext(ctx context.Context, mode byte, host string, port uint16) (net.Conn, error) {
	if b.remaining.Load() <= 0 {
		b.exhausted()
		return nil, ErrPayloadBudget
	}
	c, err := b.FlowTransport.DialContext(ctx, mode, host, port)
	if err != nil {
		return nil, err
	}
	return budgetConn{Conn: c, b: b}, nil
}

type budgetConn struct {
	net.Conn
	b *BudgetTransport
}

func (c budgetConn) Read(p []byte) (int, error) {
	if len(p) == 0 {
		return 0, nil
	}
	allowed := c.b.take(len(p))
	if allowed == 0 {
		c.b.exhausted()
		return 0, ErrPayloadBudget
	}
	n, err := c.Conn.Read(p[:allowed])
	c.b.refund(allowed - n)
	return n, err
}
func (c budgetConn) Write(p []byte) (int, error) {
	if len(p) == 0 {
		return 0, nil
	}
	allowed := c.b.take(len(p))
	if allowed == 0 {
		c.b.exhausted()
		return 0, ErrPayloadBudget
	}
	n, err := c.Conn.Write(p[:allowed])
	c.b.refund(allowed - n)
	if err == nil && allowed < len(p) {
		c.b.exhausted()
		err = ErrPayloadBudget
	}
	return n, err
}
func (c budgetConn) CloseWrite() error {
	if x, ok := c.Conn.(interface{ CloseWrite() error }); ok {
		return x.CloseWrite()
	}
	return nil
}
