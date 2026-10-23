package ipengine

import (
	"bufio"
	"context"
	"crypto/rand"
	"crypto/tls"
	"encoding/binary"
	"fmt"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/bridge"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/traffic"
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
	"net/http"
	"strings"
	"sync"
	"time"
)

type IPProbe struct {
	HTTPStatus        int
	HTTPSMilliseconds float64
	HTTPSBytes        int64
	DNSMilliseconds   []float64
	Counters          Counters
	Errors            []string
	Passed            bool
	Scope             string
	PhoneNetwork      string
	Traffic           *traffic.Snapshot
}

// ProbeUSB uses two memory-only IP NICs. Only bridge.Transport opens an OS TCP
// connection, to the already approved localhost ADB endpoint. No Wintun is loaded.
func ProbeUSB(ctx context.Context, transport bridge.Transport, policies ...*traffic.Manager) IPProbe {
	result := IPProbe{Scope: "in-memory IPv4 -> authenticated USB relay -> cellular sockets; no OS route/DNS/adapter change"}
	var policy *traffic.Manager
	if len(policies) > 0 {
		policy = policies[0]
	}
	e, err := New(Config{Transport: transport, MTU: 1280, Traffic: policy})
	if err != nil {
		result.Errors = append(result.Errors, err.Error())
		return result
	}
	defer e.Close()
	client := stack.New(stack.Options{NetworkProtocols: []stack.NetworkProtocolFactory{ipv4.NewProtocol}, TransportProtocols: []stack.TransportProtocolFactory{tcp.NewProtocol, udp.NewProtocol}})
	defer client.Close()
	link := channel.New(256, 1280, "")
	defer link.Close()
	if err := client.CreateNIC(1, link); err != nil {
		result.Errors = append(result.Errors, err.String())
		return result
	}
	if err := client.AddProtocolAddress(1, tcpip.ProtocolAddress{Protocol: ipv4.ProtocolNumber, AddressWithPrefix: tcpip.AddressWithPrefix{Address: tcpip.AddrFrom4([4]byte{10, 254, 88, 2}), PrefixLen: 30}}, stack.AddressProperties{}); err != nil {
		result.Errors = append(result.Errors, err.String())
		return result
	}
	// This route exists ONLY inside the private gVisor object, never in Windows.
	client.SetRouteTable([]tcpip.Route{{Destination: header.IPv4EmptySubnet, NIC: 1}})
	ctx, cancel := context.WithCancel(ctx)
	defer cancel()
	a, b := net.Pipe()
	wire := &FramedDevice{Stream: b}
	var wg sync.WaitGroup
	wg.Add(3)
	go func() { defer wg.Done(); e.Run(ctx, &FramedDevice{Stream: a}) }()
	go func() {
		defer wg.Done()
		for {
			p := link.ReadContext(ctx)
			if p == nil {
				return
			}
			buf := p.ToBuffer()
			raw := append([]byte(nil), buf.Flatten()...)
			buf.Release()
			p.DecRef()
			if wire.WritePacket(ctx, raw) != nil {
				return
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
			p := stack.NewPacketBuffer(stack.PacketBufferOptions{Payload: buffer.MakeWithData(raw)})
			link.InjectInbound(header.IPv4ProtocolNumber, p)
			p.DecRef()
		}
	}()
	defer func() { cancel(); wire.Close(); e.Close(); link.Close(); wg.Wait() }()
	started := time.Now()
	remote := tcpip.FullAddress{NIC: 1, Addr: tcpip.AddrFrom4([4]byte{1, 1, 1, 1}), Port: 443}
	connection, err := gonet.DialContextTCP(ctx, client, remote, ipv4.ProtocolNumber)
	if err == nil {
		connection.SetDeadline(time.Now().Add(12 * time.Second))
		secure := tls.Client(connection, &tls.Config{ServerName: "1.1.1.1", MinVersion: tls.VersionTLS12})
		err = secure.HandshakeContext(ctx)
		if err == nil {
			_, err = io.WriteString(secure, "GET /cdn-cgi/trace HTTP/1.1\r\nHost: 1.1.1.1\r\nConnection: close\r\n\r\n")
		}
		if err == nil {
			var response *http.Response
			response, err = http.ReadResponse(bufio.NewReader(secure), nil)
			if err == nil {
				result.HTTPStatus = response.StatusCode
				result.HTTPSBytes, err = io.Copy(io.Discard, io.LimitReader(response.Body, 16385))
				response.Body.Close()
				if result.HTTPSBytes > 16384 {
					err = fmt.Errorf("HTTPS budget exceeded")
				}
			}
		}
		secure.Close()
		connection.Close()
	}
	result.HTTPSMilliseconds = float64(time.Since(started).Microseconds()) / 1000
	if err != nil {
		result.Errors = append(result.Errors, "HTTPS: "+err.Error())
	}
	remote.Port = 53
	dns, err := gonet.DialUDP(client, nil, &remote, ipv4.ProtocolNumber)
	if err != nil {
		result.Errors = append(result.Errors, "DNS association: "+err.Error())
	} else {
		question := []byte{7, 'e', 'x', 'a', 'm', 'p', 'l', 'e', 3, 'c', 'o', 'm', 0, 0, 1, 0, 1}
		for range 5 {
			if err := ctx.Err(); err != nil {
				result.Errors = append(result.Errors, "DNS: "+err.Error())
				break
			}
			id := make([]byte, 2)
			rand.Read(id)
			query := append(append(id, []byte{1, 0, 0, 1, 0, 0, 0, 0, 0, 0}...), question...)
			dns.SetDeadline(time.Now().Add(3 * time.Second))
			start := time.Now()
			_, err = dns.Write(query)
			if err == nil {
				buf := make([]byte, 1500)
				var n int
				n, err = dns.Read(buf)
				if err == nil {
					if n < 12+len(question) || binary.BigEndian.Uint16(buf[:2]) != binary.BigEndian.Uint16(id) || buf[2]&128 == 0 || buf[3]&15 != 0 || binary.BigEndian.Uint16(buf[6:8]) == 0 {
						err = fmt.Errorf("invalid DNS response")
					}
				}
			}
			if err != nil {
				result.Errors = append(result.Errors, "DNS: "+err.Error())
			} else {
				result.DNSMilliseconds = append(result.DNSMilliseconds, float64(time.Since(start).Microseconds())/1000)
			}
			if policy != nil {
				policy.Observe(time.Now(), time.Since(start), err == nil)
			}
		}
		dns.Close()
	}
	e.Close()
	result.Counters = e.Counters()
	control, controlErr := transport.OpenControl(ctx)
	if controlErr == nil {
		status, statusErr := control.Status()
		control.Close()
		controlErr = statusErr
		if statusErr == nil {
			result.PhoneNetwork = status.Network
		}
	}
	if controlErr != nil {
		result.Errors = append(result.Errors, "cellular source attestation unavailable")
	}
	if policy != nil {
		s := policy.Snapshot()
		result.Traffic = &s
	}
	result.Passed = result.HTTPStatus == 200 && len(result.DNSMilliseconds) == 5 && len(result.Errors) == 0 && strings.HasPrefix(result.PhoneNetwork, "CELLULAR ")
	return result
}
