package ipengine

import (
	"bufio"
	"bytes"
	"context"
	"crypto/rand"
	"encoding/binary"
	"encoding/hex"
	"fmt"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/bridge"
	"gvisor.dev/gvisor/pkg/buffer"
	"gvisor.dev/gvisor/pkg/tcpip"
	"gvisor.dev/gvisor/pkg/tcpip/adapters/gonet"
	"gvisor.dev/gvisor/pkg/tcpip/header"
	"gvisor.dev/gvisor/pkg/tcpip/link/channel"
	"gvisor.dev/gvisor/pkg/tcpip/network/ipv4"
	"gvisor.dev/gvisor/pkg/tcpip/stack"
	"gvisor.dev/gvisor/pkg/tcpip/transport/tcp"
	"gvisor.dev/gvisor/pkg/tcpip/transport/udp"
	"io"
	"net"
	"os"
	"os/exec"
	"strconv"
	"strings"
	"sync"
	"testing"
	"time"
)

type mappedTransport struct{ bridge.Transport }

func (m mappedTransport) DialContext(ctx context.Context, mode byte, host string, port uint16) (net.Conn, error) {
	if host != "198.18.0.1" {
		return nil, fmt.Errorf("fixture refuses target %s", host)
	}
	return m.Transport.DialContext(ctx, mode, "127.0.0.1", port)
}
func fixture(t *testing.T) bridge.Transport {
	t.Helper()
	classes := os.Getenv("TETHERMAX_TEST_CLASSES")
	if classes == "" {
		t.Fatal("fresh Java engine classes required; run scripts/test.sh")
	}
	token := make([]byte, 16)
	rand.Read(token)
	secret := hex.EncodeToString(token)
	ctx, cancel := context.WithCancel(context.Background())
	p := exec.CommandContext(ctx, "java", "-cp", classes, "io.tethermax.engine.TestServer")
	p.Env = append(os.Environ(), "TETHERMAX_TEST_TOKEN="+secret)
	p.Stderr = os.Stderr
	output, err := p.StdoutPipe()
	if err != nil {
		t.Fatal(err)
	}
	if err = p.Start(); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { cancel(); p.Wait() })
	line, err := bufio.NewReader(output).ReadString('\n')
	if err != nil {
		t.Fatal(err)
	}
	port, err := strconv.Atoi(strings.TrimSpace(line))
	if err != nil {
		t.Fatal(err)
	}
	return bridge.Transport{Address: net.JoinHostPort("127.0.0.1", strconv.Itoa(port)), Token: secret}
}
func peer(t *testing.T, e *Engine, transforms ...func([]byte) [][]byte) *stack.Stack {
	t.Helper()
	link := channel.New(512, e.cfg.MTU, "")
	s := stack.New(stack.Options{NetworkProtocols: []stack.NetworkProtocolFactory{ipv4.NewProtocol}, TransportProtocols: []stack.TransportProtocolFactory{tcp.NewProtocol, udp.NewProtocol}})
	if err := s.CreateNIC(1, link); err != nil {
		t.Fatal(err)
	}
	if err := s.AddProtocolAddress(1, tcpip.ProtocolAddress{Protocol: ipv4.ProtocolNumber, AddressWithPrefix: tcpip.AddressWithPrefix{Address: tcpip.AddrFrom4([4]byte{10, 77, 0, 2}), PrefixLen: 24}}, stack.AddressProperties{}); err != nil {
		t.Fatal(err)
	}
	s.SetRouteTable([]tcpip.Route{{Destination: header.IPv4EmptySubnet, NIC: 1}})
	ctx, cancel := context.WithCancel(context.Background())
	var wg sync.WaitGroup
	wg.Add(3)
	a, b := net.Pipe()
	wire := &FramedDevice{Stream: b}
	go func() { defer wg.Done(); e.Run(ctx, &FramedDevice{Stream: a}) }()
	go func() {
		defer wg.Done()
		for {
			p := link.ReadContext(ctx)
			if p == nil {
				return
			}
			b := p.ToBuffer()
			raw := append([]byte(nil), b.Flatten()...)
			b.Release()
			p.DecRef()
			packets := [][]byte{raw}
			if len(transforms) > 0 {
				packets = transforms[0](raw)
			}
			for _, packet := range packets {
				if err := wire.WritePacket(ctx, packet); err != nil {
					return
				}
			}
		}
	}()
	go func() {
		defer wg.Done()
		for {
			raw, err := wire.ReadPacket(ctx)
			if err != nil {
				return
			}
			if err := ValidateIPv4(raw, e.cfg.MTU); err != nil {
				t.Errorf("invalid returned IPv4 packet: %v", err)
				return
			}
			p := stack.NewPacketBuffer(stack.PacketBufferOptions{Payload: buffer.MakeWithData(raw)})
			link.InjectInbound(ipv4.ProtocolNumber, p)
			p.DecRef()
		}
	}()
	t.Cleanup(func() {
		cancel()
		wire.Close()
		e.Close()
		s.Close()
		link.Close()
		wg.Wait()
	})
	return s
}
func target(t *testing.T, a net.Addr) tcpip.FullAddress {
	t.Helper()
	_, p, err := net.SplitHostPort(a.String())
	if err != nil {
		t.Fatal(err)
	}
	port, _ := strconv.Atoi(p)
	ip := net.ParseIP("198.18.0.1").To4()
	return tcpip.FullAddress{NIC: 1, Addr: tcpip.AddrFrom4([4]byte(ip)), Port: uint16(port)}
}
func TestIPStackJavaTCPUDP(t *testing.T) {
	transport := fixture(t)
	e, err := New(Config{Transport: mappedTransport{transport}, MTU: 1280, UDPIdle: time.Second})
	if err != nil {
		t.Fatal(err)
	}
	client := peer(t, e)
	echo, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer echo.Close()
	go func() {
		for {
			c, err := echo.Accept()
			if err != nil {
				return
			}
			go func() { defer c.Close(); io.Copy(c, c) }()
		}
	}()
	t.Run("TCPBinaryAndHalfClose", func(t *testing.T) {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		c, err := gonet.DialContextTCP(ctx, client, target(t, echo.Addr()), ipv4.ProtocolNumber)
		if err != nil {
			t.Fatal(err)
		}
		defer c.Close()
		c.SetDeadline(time.Now().Add(8 * time.Second))
		payload := make([]byte, 256*1024)
		rand.Read(payload)
		sent := make(chan error, 1)
		go func() {
			_, err := c.Write(payload)
			if err == nil {
				err = c.CloseWrite()
			}
			sent <- err
		}()
		got, err := io.ReadAll(c)
		if err != nil {
			t.Fatal(err)
		}
		if err = <-sent; err != nil {
			t.Fatal(err)
		}
		if !bytes.Equal(payload, got) {
			t.Fatalf("payload corruption %d/%d", len(got), len(payload))
		}
	})
	u, err := net.ListenUDP("udp4", &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1)})
	if err != nil {
		t.Fatal(err)
	}
	defer u.Close()
	go func() {
		b := make([]byte, 65535)
		for {
			n, a, err := u.ReadFromUDP(b)
			if err != nil {
				return
			}
			u.WriteToUDP(b[:n], a)
		}
	}()
	t.Run("UDPDatagramBoundariesAndFragmentation", func(t *testing.T) {
		addr := target(t, u.LocalAddr())
		c, err := gonet.DialUDP(client, nil, &addr, ipv4.ProtocolNumber)
		if err != nil {
			t.Fatal(err)
		}
		defer c.Close()
		c.SetDeadline(time.Now().Add(5 * time.Second))
		for _, size := range []int{0, 1, 512, 1200, 4000} {
			b := make([]byte, size)
			rand.Read(b)
			if _, err = c.Write(b); err != nil {
				t.Fatal(err)
			}
			got := make([]byte, 65535)
			n, err := c.Read(got)
			if err != nil {
				t.Fatalf("size %d: %v", size, err)
			}
			if !bytes.Equal(got[:n], b) {
				t.Fatalf("wrong datagram size=%d", size)
			}
		}
	})
	t.Run("IndependentUDPFlows", func(t *testing.T) {
		var wg sync.WaitGroup
		for i := 0; i < 8; i++ {
			wg.Add(1)
			go func(i int) {
				defer wg.Done()
				a := target(t, u.LocalAddr())
				c, err := gonet.DialUDP(client, nil, &a, ipv4.ProtocolNumber)
				if err != nil {
					t.Error(err)
					return
				}
				defer c.Close()
				c.SetDeadline(time.Now().Add(5 * time.Second))
				b := bytes.Repeat([]byte{byte(i + 1)}, 100)
				c.Write(b)
				got := make([]byte, 1000)
				n, err := c.Read(got)
				if err != nil || !bytes.Equal(got[:n], b) {
					t.Errorf("flow %d: %v", i, err)
				}
			}(i)
		}
		wg.Wait()
	})
	t.Run("InvalidIPv4Rejected", func(t *testing.T) {
		if e.Inject(make([]byte, 20)) == nil {
			t.Fatal("invalid packet accepted")
		}
	})
	t.Run("CancellationClosesFlows", func(t *testing.T) {
		e.Close()
		if e.Counters().Flows < 10 {
			t.Fatal("expected real forwarded flows")
		}
	})
}
func ipv4Packet() []byte {
	b := make([]byte, 28)
	b[0] = 0x45
	b[2] = 0
	b[3] = 28
	b[8] = 64
	b[9] = 17
	copy(b[12:16], []byte{10, 77, 0, 2})
	copy(b[16:20], []byte{127, 0, 0, 1})
	b[24] = 0
	b[25] = 8
	var sum uint32
	for i := 0; i < 20; i += 2 {
		sum += uint32(binary.BigEndian.Uint16(b[i : i+2]))
	}
	for sum>>16 != 0 {
		sum = (sum & 65535) + (sum >> 16)
	}
	binary.BigEndian.PutUint16(b[10:12], ^uint16(sum))
	return b
}
func TestIngressValidation(t *testing.T) {
	good := ipv4Packet()
	if err := ValidateIPv4(good, 1500); err != nil {
		t.Fatal(err)
	}
	for name, mutate := range map[string]func([]byte){"checksum": func(b []byte) { b[10] ^= 1 }, "length": func(b []byte) { b[3]++ }, "ihl": func(b []byte) { b[0] = 0x44 }, "version": func(b []byte) { b[0] = 0x65 }} {
		t.Run(name, func(t *testing.T) {
			b := append([]byte(nil), good...)
			mutate(b)
			if ValidateIPv4(b, 1500) == nil {
				t.Fatal("accepted malformed packet")
			}
		})
	}
	if ValidateIPv4(good, 20) == nil {
		t.Fatal("accepted oversized packet")
	}
}

func TestTCPSequenceRetransmissionAndReordering(t *testing.T) {
	transport := fixture(t)
	for _, mode := range []string{"drop-first-data", "reorder-and-duplicate"} {
		t.Run(mode, func(t *testing.T) {
			e, err := New(Config{Transport: mappedTransport{transport}, MTU: 1280})
			if err != nil {
				t.Fatal(err)
			}
			var held []byte
			done := false
			transform := func(b []byte) [][]byte {
				ihl := int(b[0]&15) * 4
				if b[9] != 6 || len(b) < ihl+20 {
					return [][]byte{b}
				}
				tcpLen := int(b[ihl+12]>>4) * 4
				if len(b) <= ihl+tcpLen || done {
					return [][]byte{b}
				}
				if mode == "drop-first-data" {
					done = true
					return nil
				}
				if held == nil {
					held = append([]byte(nil), b...)
					return nil
				}
				done = true
				return [][]byte{b, held, b}
			}
			client := peer(t, e, transform)
			echo, err := net.Listen("tcp", "127.0.0.1:0")
			if err != nil {
				t.Fatal(err)
			}
			defer echo.Close()
			go func() {
				c, err := echo.Accept()
				if err != nil {
					return
				}
				defer c.Close()
				io.Copy(c, c)
			}()
			ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
			defer cancel()
			c, err := gonet.DialContextTCP(ctx, client, target(t, echo.Addr()), ipv4.ProtocolNumber)
			if err != nil {
				t.Fatal(err)
			}
			defer c.Close()
			c.SetDeadline(time.Now().Add(8 * time.Second))
			payload := make([]byte, 32000)
			rand.Read(payload)
			sent := make(chan error, 1)
			go func() {
				_, err := c.Write(payload)
				if err == nil {
					err = c.CloseWrite()
				}
				sent <- err
			}()
			got, err := io.ReadAll(c)
			if err != nil {
				t.Fatal(err)
			}
			if err = <-sent; err != nil {
				t.Fatal(err)
			}
			if !bytes.Equal(got, payload) {
				t.Fatal("sequence processing corrupted data")
			}
			if !done {
				t.Fatal("loss/reordering fixture never executed")
			}
		})
	}
}
func TestDNSResponseOverRawIPAndJava(t *testing.T) {
	transport := fixture(t)
	e, err := New(Config{Transport: mappedTransport{transport}})
	if err != nil {
		t.Fatal(err)
	}
	client := peer(t, e)
	server, err := net.ListenUDP("udp4", &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1)})
	if err != nil {
		t.Fatal(err)
	}
	defer server.Close()
	question := []byte{4, 't', 'e', 's', 't', 0, 0, 1, 0, 1}
	go func() {
		b := make([]byte, 512)
		n, a, err := server.ReadFromUDP(b)
		if err != nil {
			return
		}
		if n != 12+len(question) {
			return
		}
		answer := append([]byte(nil), b[:n]...)
		answer[2] = 0x81
		answer[3] = 0x80
		answer[7] = 1
		answer = append(answer, []byte{0xc0, 0x0c, 0, 1, 0, 1, 0, 0, 0, 30, 0, 4, 203, 0, 113, 17}...)
		server.WriteToUDP(answer, a)
	}()
	addr := target(t, server.LocalAddr())
	c, err := gonet.DialUDP(client, nil, &addr, ipv4.ProtocolNumber)
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	c.SetDeadline(time.Now().Add(3 * time.Second))
	query := append([]byte{0x12, 0x34, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0}, question...)
	c.Write(query)
	b := make([]byte, 512)
	n, err := c.Read(b)
	if err != nil {
		t.Fatal(err)
	}
	if n != len(query)+16 || !bytes.Equal(b[:2], query[:2]) || b[2] != 0x81 || !bytes.Equal(b[n-4:n], []byte{203, 0, 113, 17}) {
		t.Fatal("DNS response or transaction mismatched")
	}
}
func TestUDPSameSourcePortDistinctTargets(t *testing.T) {
	transport := fixture(t)
	e, err := New(Config{Transport: mappedTransport{transport}})
	if err != nil {
		t.Fatal(err)
	}
	client := peer(t, e)
	servers := make([]*net.UDPConn, 2)
	for i := range servers {
		servers[i], err = net.ListenUDP("udp4", &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1)})
		if err != nil {
			t.Fatal(err)
		}
		defer servers[i].Close()
		go func(s *net.UDPConn) {
			b := make([]byte, 64)
			n, a, err := s.ReadFromUDP(b)
			if err == nil {
				s.WriteToUDP(b[:n], a)
			}
		}(servers[i])
	}
	c, err := gonet.DialUDP(client, &tcpip.FullAddress{NIC: 1, Addr: tcpip.AddrFrom4([4]byte{10, 77, 0, 2}), Port: 45000}, nil, ipv4.ProtocolNumber)
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	c.SetDeadline(time.Now().Add(3 * time.Second))
	for i, s := range servers {
		a := target(t, s.LocalAddr())
		c.WriteTo([]byte{byte(i)}, &net.UDPAddr{IP: net.IP(a.Addr.AsSlice()), Port: int(a.Port)})
	}
	seen := map[int]bool{}
	for range servers {
		b := make([]byte, 64)
		n, a, err := c.ReadFrom(b)
		if err != nil {
			t.Fatal(err)
		}
		if n != 1 || int(b[0]) > 1 {
			t.Fatal("wrong payload")
		}
		idx := int(b[0])
		want := servers[idx].LocalAddr().(*net.UDPAddr).Port
		if a.(*net.UDPAddr).Port != want {
			t.Fatal("reply delivered under wrong target flow")
		}
		seen[idx] = true
	}
	if len(seen) != 2 {
		t.Fatal("flow cross-talk")
	}
}
func TestUDPIdleReleasesFlowState(t *testing.T) {
	transport := fixture(t)
	e, err := New(Config{Transport: mappedTransport{transport}, UDPIdle: 50 * time.Millisecond})
	if err != nil {
		t.Fatal(err)
	}
	client := peer(t, e)
	s, err := net.ListenUDP("udp4", &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1)})
	if err != nil {
		t.Fatal(err)
	}
	defer s.Close()
	a := target(t, s.LocalAddr())
	c, err := gonet.DialUDP(client, nil, &a, ipv4.ProtocolNumber)
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	c.Write([]byte("idle"))
	deadline := time.Now().Add(2 * time.Second)
	for e.Counters().Flows == 0 && time.Now().Before(deadline) {
		time.Sleep(time.Millisecond)
	}
	for time.Now().Before(deadline) {
		e.mu.Lock()
		active := len(e.live)
		e.mu.Unlock()
		if active == 0 && e.Counters().Flows > 0 {
			return
		}
		time.Sleep(5 * time.Millisecond)
	}
	t.Fatal("UDP idle state leaked")
}

func TestTCPStalledPeerReleasesFlowState(t *testing.T) {
	transport := fixture(t)
	e, err := New(Config{Transport: mappedTransport{transport}, TCPIdle: 100 * time.Millisecond})
	if err != nil {
		t.Fatal(err)
	}
	client := peer(t, e)
	server, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer server.Close()
	accepted := make(chan net.Conn, 1)
	go func() {
		c, err := server.Accept()
		if err == nil {
			accepted <- c
		}
	}()
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	c, err := gonet.DialContextTCP(ctx, client, target(t, server.Addr()), ipv4.ProtocolNumber)
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	select {
	case remote := <-accepted:
		defer remote.Close()
	case <-ctx.Done():
		t.Fatal("Java relay did not connect")
	}
	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) {
		if e.Counters().Active == 0 && e.Counters().Flows > 0 {
			return
		}
		time.Sleep(5 * time.Millisecond)
	}
	t.Fatal("stalled TCP flow state leaked")
}

func TestTCPActiveOneWayTransferHasNoSessionTimeLimit(t *testing.T) {
	transport := fixture(t)
	e, err := New(Config{Transport: mappedTransport{transport}, TCPIdle: 300 * time.Millisecond})
	if err != nil {
		t.Fatal(err)
	}
	client := peer(t, e)
	server, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer server.Close()
	sent := make(chan error, 1)
	go func() {
		c, err := server.Accept()
		if err != nil {
			sent <- err
			return
		}
		defer c.Close()
		for i := 0; i < 12; i++ {
			if _, err = c.Write([]byte{byte(i)}); err != nil {
				sent <- err
				return
			}
			time.Sleep(50 * time.Millisecond)
		}
		sent <- nil
	}()
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	c, err := gonet.DialContextTCP(ctx, client, target(t, server.Addr()), ipv4.ProtocolNumber)
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	c.SetDeadline(time.Now().Add(2 * time.Second))
	got, err := io.ReadAll(c)
	if err != nil {
		t.Fatal(err)
	}
	if err = <-sent; err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(got, []byte{0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11}) {
		t.Fatalf("active download cut off: %v", got)
	}
}
