// Package ipengine terminates IPv4 TCP/UDP in gVisor and relays flows through
// the existing authenticated Java/Android transport. It never configures OS networking.
package ipengine

import (
	"context"
	"encoding/binary"
	"errors"
	"fmt"
	"io"
	"net"
	"net/netip"
	"sync"
	"sync/atomic"
	"time"

	"github.com/CanKaya4/pdanetnobetci1/host/internal/bridge"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/traffic"
	"github.com/xjasonlyu/tun2socks/v2/core"
	"github.com/xjasonlyu/tun2socks/v2/core/adapter"
	"gvisor.dev/gvisor/pkg/buffer"
	"gvisor.dev/gvisor/pkg/tcpip/header"
	"gvisor.dev/gvisor/pkg/tcpip/link/channel"
	"gvisor.dev/gvisor/pkg/tcpip/stack"
)

type FlowTransport interface {
	Validate() error
	DialContext(context.Context, byte, string, uint16) (net.Conn, error)
}
type Config struct {
	Transport     FlowTransport
	MTU           uint32
	MaxFlows      int
	UDPIdle       time.Duration
	TCPIdle       time.Duration
	Traffic       *traffic.Manager
	SharedClients []netip.Prefix
}
type Counters struct {
	Uploaded, Downloaded, Flows, Rejected, Failed uint64
	Active                                        int
}
type measuredWriter struct {
	writer  io.Writer
	counter *atomic.Uint64
}

func (w measuredWriter) Write(b []byte) (int, error) {
	n, err := w.writer.Write(b)
	w.counter.Add(uint64(n))
	return n, err
}

type flow struct {
	local, remote net.Conn
	schedule      *traffic.Flow
}

// idleConn refreshes a shared inactivity deadline on successful I/O. Activity in
// either direction keeps a long one-way download/upload alive; this is not a
// session duration limit. It wraps only this engine's connections.
type idleConn struct {
	net.Conn
	touch func()
}

func (c idleConn) Read(b []byte) (int, error) {
	n, err := c.Conn.Read(b)
	if n > 0 {
		c.touch()
	}
	return n, err
}
func (c idleConn) Write(b []byte) (int, error) {
	n, err := c.Conn.Write(b)
	if n > 0 {
		c.touch()
	}
	return n, err
}
func (c idleConn) CloseWrite() error {
	if half, ok := c.Conn.(interface{ CloseWrite() error }); ok {
		return half.CloseWrite()
	}
	return nil
}

type Engine struct {
	cfg                               Config
	ctx                               context.Context
	cancel                            context.CancelFunc
	link                              *channel.Endpoint
	stack                             *stack.Stack
	mu                                sync.Mutex
	ingress                           sync.RWMutex
	closed                            bool
	live                              map[*flow]struct{}
	wg                                sync.WaitGroup
	up, down, total, rejected, failed atomic.Uint64
}

func New(cfg Config) (*Engine, error) {
	if cfg.Transport == nil {
		return nil, errors.New("missing flow transport")
	}
	if err := cfg.Transport.Validate(); err != nil {
		return nil, err
	}
	if cfg.MTU == 0 {
		cfg.MTU = 1500
	}
	if cfg.MTU < 576 || cfg.MTU > 65535 {
		return nil, errors.New("IPv4 MTU outside 576..65535")
	}
	if cfg.MaxFlows == 0 {
		cfg.MaxFlows = 128
	}
	if cfg.MaxFlows < 1 || cfg.MaxFlows > 1024 {
		return nil, errors.New("invalid flow limit")
	}
	if cfg.UDPIdle == 0 {
		cfg.UDPIdle = 30 * time.Second
	}
	if cfg.UDPIdle < time.Millisecond {
		return nil, errors.New("invalid UDP idle timeout")
	}
	if cfg.TCPIdle == 0 {
		cfg.TCPIdle = 5 * time.Minute
	}
	if cfg.TCPIdle < time.Millisecond {
		return nil, errors.New("invalid TCP idle timeout")
	}
	ctx, cancel := context.WithCancel(context.Background())
	e := &Engine{cfg: cfg, ctx: ctx, cancel: cancel, live: make(map[*flow]struct{}), link: channel.New(256, cfg.MTU, "")}
	s, err := core.CreateStack(&core.Config{LinkEndpoint: e.link, TransportHandler: e})
	if err != nil {
		cancel()
		e.link.Close()
		return nil, err
	}
	e.stack = s
	return e, nil
}

// ValidateIPv4 performs ingress framing/header validation, before gVisor checks
// transport checksums, reassembles fragments and manages TCP sequences/windows.
func ValidateIPv4(packet []byte, mtu uint32) error {
	if len(packet) < 20 || packet[0]>>4 != 4 {
		return errors.New("only complete IPv4 packets accepted")
	}
	ihl := int(packet[0]&15) * 4
	length := int(binary.BigEndian.Uint16(packet[2:4]))
	if ihl < 20 || ihl > len(packet) || length != len(packet) || length < ihl || uint32(length) > mtu {
		return errors.New("invalid IPv4 framing or MTU")
	}
	var sum uint32
	for i := 0; i < ihl; i += 2 {
		sum += uint32(binary.BigEndian.Uint16(packet[i : i+2]))
	}
	for sum>>16 != 0 {
		sum = (sum & 65535) + (sum >> 16)
	}
	if sum != 65535 {
		return errors.New("invalid IPv4 checksum")
	}
	if packet[8] == 0 {
		return errors.New("expired IPv4 TTL")
	}
	if packet[9] != 6 && packet[9] != 17 {
		return errors.New("only TCP and UDP are relayed; ICMP is not Internet ping")
	}
	return nil
}
func (e *Engine) Inject(packet []byte) error {
	if err := ValidateIPv4(packet, e.cfg.MTU); err != nil {
		e.rejected.Add(1)
		return err
	}
	e.ingress.RLock()
	defer e.ingress.RUnlock()
	e.mu.Lock()
	closed := e.closed
	e.mu.Unlock()
	if closed {
		return net.ErrClosed
	}
	p := stack.NewPacketBuffer(stack.PacketBufferOptions{Payload: buffer.MakeWithData(append([]byte(nil), packet...))})
	defer p.DecRef()
	e.link.InjectInbound(header.IPv4ProtocolNumber, p)
	return nil
}

// ReadPacket returns an IP packet for the same TUN interface which supplied ingress.
// The returned buffer is owned by the caller. Cancellation never sends fallback traffic.
func (e *Engine) ReadPacket(ctx context.Context) ([]byte, error) {
	p := e.link.ReadContext(ctx)
	if p == nil {
		return nil, io.EOF
	}
	defer p.DecRef()
	b := p.ToBuffer()
	defer b.Release()
	return append([]byte(nil), b.Flatten()...), nil
}
func (e *Engine) begin(c net.Conn, source string) *flow {
	e.mu.Lock()
	defer e.mu.Unlock()
	if e.closed || len(e.live) >= e.cfg.MaxFlows {
		c.Close()
		e.rejected.Add(1)
		return nil
	}
	f := &flow{local: c}
	if e.cfg.Traffic != nil {
		class := traffic.Bulk
		if ip, err := netip.ParseAddr(source); err == nil {
			for _, prefix := range e.cfg.SharedClients {
				if prefix.Contains(ip) {
					class = traffic.Shared
					break
				}
			}
		}
		f.schedule = e.cfg.Traffic.Flow(e.ctx, class)
	}
	e.live[f] = struct{}{}
	e.wg.Add(1)
	e.total.Add(1)
	return f
}
func (e *Engine) finish(f *flow) {
	if f.schedule != nil {
		f.schedule.Close()
	}
	e.mu.Lock()
	delete(e.live, f)
	e.mu.Unlock()
	f.local.Close()
	if f.remote != nil {
		f.remote.Close()
	}
	e.wg.Done()
}
func (e *Engine) dial(f *flow, mode byte, host string, port uint16) error {
	remote, err := e.cfg.Transport.DialContext(e.ctx, mode, host, port)
	if err != nil {
		e.failed.Add(1)
		return err
	}
	e.mu.Lock()
	defer e.mu.Unlock()
	if e.closed {
		remote.Close()
		return net.ErrClosed
	}
	f.remote = remote
	return nil
}
func (e *Engine) HandleTCP(c adapter.TCPConn) {
	id := c.ID()
	f := e.begin(c, id.RemoteAddress.String())
	if f == nil {
		return
	}
	go func() {
		defer e.finish(f)
		if e.dial(f, 1, id.LocalAddress.String(), id.LocalPort) != nil {
			return
		}
		touch := func() {
			deadline := time.Now().Add(e.cfg.TCPIdle)
			f.local.SetDeadline(deadline)
			f.remote.SetDeadline(deadline)
		}
		touch()
		local := idleConn{f.local, touch}
		remote := idleConn{f.remote, touch}
		var upload io.Writer = remote
		var download io.Writer = local
		if f.schedule != nil {
			upload = traffic.Writer{Flow: f.schedule, Writer: remote}
			download = traffic.Writer{Flow: f.schedule, Down: true, Writer: local}
		}
		done := make(chan struct{})
		go func() {
			defer close(done)
			_, err := io.Copy(measuredWriter{upload, &e.up}, local)
			remote.CloseWrite()
			if err != nil {
				if f.schedule != nil {
					f.schedule.Close()
				}
				f.remote.Close()
				f.local.Close()
			}
		}()
		_, err := io.Copy(measuredWriter{download, &e.down}, remote)
		local.CloseWrite()
		if err != nil {
			if f.schedule != nil {
				f.schedule.Close()
			}
			f.remote.Close()
			f.local.Close()
		}
		<-done
	}()
}
func (e *Engine) HandleUDP(c adapter.UDPConn) {
	id := c.ID()
	f := e.begin(c, id.RemoteAddress.String())
	if f == nil {
		return
	}
	go func() {
		defer e.finish(f)
		if e.dial(f, 2, id.LocalAddress.String(), id.LocalPort) != nil {
			return
		}
		refresh := func() {
			deadline := time.Now().Add(e.cfg.UDPIdle)
			f.local.SetDeadline(deadline)
			f.remote.SetDeadline(deadline)
		}
		refresh()
		done := make(chan struct{})
		go func() {
			defer close(done)
			defer f.remote.Close()
			defer f.local.Close()
			buf := make([]byte, bridge.MaxDatagram)
			for {
				n, err := f.local.Read(buf)
				if err != nil {
					return
				}
				refresh()
				if f.schedule != nil {
					if err := f.schedule.Datagram(false, n); err != nil {
						return
					}
				}
				frame := make([]byte, n+2)
				binary.BigEndian.PutUint16(frame, uint16(n))
				copy(frame[2:], buf[:n])
				if err := writeAll(f.remote, frame); err != nil {
					return
				}
				e.up.Add(uint64(n))
				if f.schedule != nil {
					f.schedule.Count(false, n)
				}
			}
		}()
		for {
			var size [2]byte
			if _, err := io.ReadFull(f.remote, size[:]); err != nil {
				break
			}
			n := int(binary.BigEndian.Uint16(size[:]))
			if n > bridge.MaxDatagram {
				break
			}
			data := make([]byte, n)
			if _, err := io.ReadFull(f.remote, data); err != nil {
				break
			}
			refresh()
			if f.schedule != nil {
				if err := f.schedule.Datagram(true, n); err != nil {
					break
				}
			}
			if _, err := f.local.Write(data); err != nil {
				break
			}
			e.down.Add(uint64(n))
			if f.schedule != nil {
				f.schedule.Count(true, n)
			}
		}
		f.local.Close()
		f.remote.Close()
		if f.schedule != nil {
			f.schedule.Close()
		}
		<-done
	}()
}
func writeAll(w io.Writer, b []byte) error {
	for len(b) > 0 {
		n, err := w.Write(b)
		if err != nil {
			return err
		}
		if n == 0 {
			return io.ErrShortWrite
		}
		b = b[n:]
	}
	return nil
}
func (e *Engine) Counters() Counters {
	e.mu.Lock()
	active := len(e.live)
	e.mu.Unlock()
	return Counters{e.up.Load(), e.down.Load(), e.total.Load(), e.rejected.Load(), e.failed.Load(), active}
}
func (e *Engine) Close() error {
	e.mu.Lock()
	if e.closed {
		e.mu.Unlock()
		return nil
	}
	e.closed = true
	e.cancel()
	connections := make([]net.Conn, 0, len(e.live)*2)
	for f := range e.live {
		connections = append(connections, f.local)
		if f.remote != nil {
			connections = append(connections, f.remote)
		}
	}
	e.mu.Unlock()
	// Closing a netstack connection can invoke callbacks. Never hold the flow
	// registry lock while closing a connection or waiting for its workers.
	for _, c := range connections {
		c.Close()
	}
	e.ingress.Lock()
	e.stack.Close()
	e.link.Close()
	e.ingress.Unlock()
	e.wg.Wait()
	return nil
}
func (e *Engine) String() string {
	return fmt.Sprintf("IPv4 netstack MTU=%d; OS networking unchanged", e.cfg.MTU)
}
