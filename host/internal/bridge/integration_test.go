package bridge

import (
	"bufio"
	"bytes"
	"context"
	"crypto/rand"
	"encoding/hex"
	"fmt"
	"github.com/CanKaya4/pdanetnobetci1/host/internal/traffic"
	"io"
	"net"
	"net/http"
	"net/http/httptest"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
	"testing"
	"time"
)

func TestJavaEngineEndToEnd(t *testing.T) {
	classes := os.Getenv("TETHERMAX_TEST_CLASSES")
	if classes == "" {
		t.Fatal("run scripts/test.sh: TETHERMAX_TEST_CLASSES must point to freshly compiled Java engine classes")
	}
	b := make([]byte, 16)
	if _, err := rand.Read(b); err != nil {
		t.Fatal(err)
	}
	token := hex.EncodeToString(b)
	ctx, cancel := context.WithTimeout(context.Background(), 90*time.Second)
	defer cancel()
	// Windows PATH may resolve an Oracle launcher shim which spawns a child
	// outside Process.Kill ownership, keeping inherited test output handles open.
	javaPath := "java"
	if home := os.Getenv("JAVA_HOME"); home != "" {
		javaPath = filepath.Join(home, "bin", "java")
	}
	java := exec.CommandContext(ctx, javaPath, "-cp", classes, "io.tethermax.engine.TestServer")
	java.Env = append(os.Environ(), "TETHERMAX_TEST_TOKEN="+token)
	output, err := java.StdoutPipe()
	if err != nil {
		t.Fatal(err)
	}
	java.Stderr = os.Stderr
	if err = java.Start(); err != nil {
		t.Fatal(err)
	}
	defer func() { java.Process.Kill(); java.Wait() }()
	line, err := bufio.NewReader(output).ReadString('\n')
	if err != nil {
		t.Fatal(err)
	}
	port, err := strconv.Atoi(strings.TrimSpace(line))
	if err != nil {
		t.Fatal(line)
	}
	transport := Transport{Address: fmt.Sprintf("127.0.0.1:%d", port), Token: token}
	t.Run("AuthenticatedProfileAndTelemetryControl", func(t *testing.T) {
		c, err := transport.OpenControl(ctx)
		if err != nil {
			t.Fatal(err)
		}
		defer c.Close()
		before, err := c.Status()
		if err != nil || before.Mode != traffic.Auto || before.Network != "fixture" {
			t.Fatal(before, err)
		}
		if err = c.SetMode(traffic.Streaming); err != nil {
			t.Fatal(err)
		}
		if err = c.Publish(traffic.Snapshot{Active: traffic.Streaming, UploadMbps: 1.25}); err != nil {
			t.Fatal(err)
		}
		after, err := c.Status()
		if err != nil || after.Mode != traffic.Streaming || after.Revision <= before.Revision || after.HostAgeMs < 0 {
			t.Fatal(after, err)
		}
		if err = c.SetDNS("adguard"); err != nil {
			t.Fatal(err)
		}
		dnsStatus, err := c.Status()
		if err != nil || dnsStatus.DNSProfile != "adguard" || dnsStatus.Revision <= after.Revision {
			t.Fatal("DNS profile acknowledgement", dnsStatus, err)
		}
		if err = c.SetDNS("invalid"); err == nil {
			t.Fatal("invalid DNS accepted")
		}
		if err = c.SetDNS("system"); err != nil {
			t.Fatal(err)
		}

		if err = c.SetMode("invalid"); err == nil {
			t.Fatal("invalid remote profile accepted")
		}
		bad := transport
		bad.Token = strings.Repeat("0", 32)
		if unauthorized, err := bad.OpenControl(ctx); err == nil {
			unauthorized.Close()
			t.Fatal("wrong-token control accepted")
		}
	})
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	gatewayCtx, stop := context.WithCancel(ctx)
	serverDone := make(chan error, 1)
	go func() { serverDone <- (Server{Transport: transport}).Serve(gatewayCtx, listener) }()
	defer func() {
		stop()
		select {
		case err := <-serverDone:
			if err != nil {
				t.Error(err)
			}
		case <-time.After(8 * time.Second):
			t.Error("gateway shutdown hung")
		}
	}()
	socks := listener.Addr().String()
	echo, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer echo.Close()
	go func() {
		for {
			c, e := echo.Accept()
			if e != nil {
				return
			}
			go func(c net.Conn) { defer c.Close(); io.Copy(c, c) }(c)
		}
	}()
	echoHost, echoPort := split(t, echo.Addr().String())
	udp, err := net.ListenUDP("udp4", &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1)})
	if err != nil {
		t.Fatal(err)
	}
	defer udp.Close()
	go func() {
		buf := make([]byte, 65535)
		for {
			n, p, e := udp.ReadFromUDP(buf)
			if e != nil {
				return
			}
			udp.WriteToUDP(buf[:n], p)
		}
	}()
	udpHost, udpPort := split(t, udp.LocalAddr().String())

	t.Run("TCPBulkBinaryOverSOCKSAndJava", func(t *testing.T) {
		c := socksConnect(t, socks, 1, echoHost, echoPort)
		defer c.Close()
		payload := make([]byte, 2*1024*1024)
		rand.Read(payload)
		sent := make(chan error, 1)
		go func() {
			_, e := c.Write(payload)
			if e == nil {
				e = c.(*net.TCPConn).CloseWrite()
			}
			sent <- e
		}()
		got, e := io.ReadAll(c)
		if e != nil {
			t.Fatal(e)
		}
		if e = <-sent; e != nil {
			t.Fatal(e)
		}
		if !bytes.Equal(got, payload) {
			t.Fatalf("binary corruption: received %d of %d", len(got), len(payload))
		}
	})
	t.Run("RemoteDNSAndHTTP", func(t *testing.T) {
		web := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			if r.URL.Path != "/engine" {
				http.Error(w, "path", 400)
				return
			}
			w.Write([]byte("tethermax-relayed-response"))
		}))
		defer web.Close()
		_, p := split(t, strings.TrimPrefix(web.URL, "http://"))
		c := socksConnect(t, socks, 1, "localhost", p)
		defer c.Close()
		fmt.Fprint(c, "GET /engine HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n")
		response, e := http.ReadResponse(bufio.NewReader(c), nil)
		if e != nil {
			t.Fatal(e)
		}
		defer response.Body.Close()
		body, e := io.ReadAll(response.Body)
		if e != nil || response.StatusCode != 200 || string(body) != "tethermax-relayed-response" {
			t.Fatalf("bad response %s %q %v", response.Status, body, e)
		}
	})
	t.Run("TCPHalfClosePreservesResponse", func(t *testing.T) {
		l, e := net.Listen("tcp", "127.0.0.1:0")
		if e != nil {
			t.Fatal(e)
		}
		defer l.Close()
		go func() {
			c, e := l.Accept()
			if e != nil {
				return
			}
			defer c.Close()
			b, e := io.ReadAll(c)
			if e == nil {
				c.Write(append([]byte("after-eof:"), b...))
			}
		}()
		h, p := split(t, l.Addr().String())
		c := socksConnect(t, socks, 1, h, p)
		defer c.Close()
		c.Write([]byte("request"))
		c.(*net.TCPConn).CloseWrite()
		got, e := io.ReadAll(c)
		if e != nil || string(got) != "after-eof:request" {
			t.Fatalf("%q %v", got, e)
		}
	})
	t.Run("RemoteHalfCloseStillAllowsUpload", func(t *testing.T) {
		l, e := net.Listen("tcp", "127.0.0.1:0")
		if e != nil {
			t.Fatal(e)
		}
		defer l.Close()
		received := make(chan []byte, 1)
		go func() {
			c, e := l.Accept()
			if e != nil {
				return
			}
			defer c.Close()
			c.SetDeadline(time.Now().Add(5 * time.Second))
			c.Write([]byte("ready"))
			c.(*net.TCPConn).CloseWrite()
			b, _ := io.ReadAll(c)
			received <- b
		}()
		h, p := split(t, l.Addr().String())
		c := socksConnect(t, socks, 1, h, p)
		defer c.Close()
		got, e := io.ReadAll(c)
		if e != nil || string(got) != "ready" {
			t.Fatalf("%q %v", got, e)
		}
		if _, e = c.Write([]byte("upload-after-remote-eof")); e != nil {
			t.Fatal(e)
		}
		c.(*net.TCPConn).CloseWrite()
		select {
		case b := <-received:
			if string(b) != "upload-after-remote-eof" {
				t.Fatalf("upload lost: %q", b)
			}
		case <-time.After(6 * time.Second):
			t.Fatal("remote half-close stranded upload")
		}
	})
	t.Run("ConcurrentFlows", func(t *testing.T) {
		var wg sync.WaitGroup
		for i := 0; i < 16; i++ {
			wg.Add(1)
			go func(i int) {
				defer wg.Done()
				c, e := transport.Dial(1, echoHost, echoPort)
				if e != nil {
					t.Error(e)
					return
				}
				defer c.Close()
				c.SetDeadline(time.Now().Add(8 * time.Second))
				want := bytes.Repeat([]byte{byte(i)}, 32768)
				done := make(chan error, 1)
				go func() { _, e := c.Write(want); done <- e }()
				got := make([]byte, len(want))
				_, e = io.ReadFull(c, got)
				if e != nil || !bytes.Equal(got, want) {
					t.Errorf("flow %d failed: %v", i, e)
				}
				if e = <-done; e != nil {
					t.Error(e)
				}
			}(i)
		}
		wg.Wait()
	})
	t.Run("WrongPairingCodeRejected", func(t *testing.T) {
		wrong := transport
		wrong.Token = strings.Repeat("f", 32)
		if _, e := wrong.Dial(1, echoHost, echoPort); e == nil {
			t.Fatal("unauthenticated relay accepted")
		}
	})
	t.Run("DestinationFailureReported", func(t *testing.T) {
		// Java cloud fixture forbids non-loopback before connecting: no external traffic.
		if _, e := transport.Dial(1, "192.0.2.1", 443); e == nil {
			t.Fatal("fixture accepted non-loopback target")
		}
	})
	t.Run("InvalidWireVersionRejected", func(t *testing.T) {
		c, e := net.DialTimeout("tcp", transport.Address, time.Second)
		if e != nil {
			t.Fatal(e)
		}
		defer c.Close()
		c.SetDeadline(time.Now().Add(2 * time.Second))
		c.Write([]byte{'T', 'M', 'A', 'X', 99})
		b := make([]byte, 1)
		if _, e = c.Read(b); e == nil {
			t.Fatal("bad version accepted")
		}
	})
	t.Run("OversizedUDPWireFrameRejected", func(t *testing.T) {
		c, e := transport.Dial(2, udpHost, udpPort)
		if e != nil {
			t.Fatal(e)
		}
		defer c.Close()
		c.SetDeadline(time.Now().Add(3 * time.Second))
		c.Write([]byte{255, 255})
		b := make([]byte, 1)
		_, e = c.Read(b)
		if e == nil {
			t.Fatal("oversized UDP frame accepted")
		}
		if ne, ok := e.(net.Error); ok && ne.Timeout() {
			t.Fatal("oversized frame stranded the relay")
		}
	})
	t.Run("UDPDatagramBoundariesBinaryAndZeroLength", func(t *testing.T) {
		control, u := socksUDP(t, socks)
		defer control.Close()
		defer u.Close()
		for _, size := range []int{0, 1, 1200, 4096, 48000} {
			payload := make([]byte, size)
			rand.Read(payload)
			packet := append(append([]byte{0, 0, 0}, encodeAddress(udpHost, udpPort)...), payload...)
			u.SetDeadline(time.Now().Add(5 * time.Second))
			if _, e := u.Write(packet); e != nil {
				t.Fatal(e)
			}
			buf := make([]byte, 65535)
			n, e := u.Read(buf)
			if e != nil {
				t.Fatal(size, e)
			}
			if !bytes.Equal(buf[:n], packet) {
				t.Fatalf("datagram mismatch size %d", size)
			}
		}
	})
	t.Run("UDPFragmentRejectedWithoutBreakingAssociation", func(t *testing.T) {
		control, u := socksUDP(t, socks)
		defer control.Close()
		defer u.Close()
		bad := append(append([]byte{0, 0, 1}, encodeAddress(udpHost, udpPort)...), []byte("fragment")...)
		u.Write(bad)
		u.SetReadDeadline(time.Now().Add(150 * time.Millisecond))
		buf := make([]byte, 1024)
		if _, e := u.Read(buf); e == nil {
			t.Fatal("fragment accepted")
		}
		good := append(append([]byte{0, 0, 0}, encodeAddress(udpHost, udpPort)...), []byte("whole")...)
		u.SetDeadline(time.Now().Add(4 * time.Second))
		u.Write(good)
		n, e := u.Read(buf)
		if e != nil || !bytes.Equal(buf[:n], good) {
			t.Fatalf("association lost: %v", e)
		}
	})
	t.Run("UnsupportedSOCKSAuthenticationRejected", func(t *testing.T) {
		c, e := net.DialTimeout("tcp", socks, time.Second)
		if e != nil {
			t.Fatal(e)
		}
		defer c.Close()
		c.SetDeadline(time.Now().Add(2 * time.Second))
		c.Write([]byte{5, 1, 2})
		b := make([]byte, 2)
		_, e = io.ReadFull(c, b)
		if e != nil || !bytes.Equal(b, []byte{5, 255}) {
			t.Fatalf("%v %v", b, e)
		}
	})
	t.Run("ListenerFailureClosesActiveConnections", func(t *testing.T) {
		l, e := net.Listen("tcp", "127.0.0.1:0")
		if e != nil {
			t.Fatal(e)
		}
		stopped := make(chan error, 1)
		go func() { stopped <- (Server{Transport: transport}).Serve(ctx, l) }()
		c := socksConnect(t, l.Addr().String(), 1, echoHost, echoPort)
		defer c.Close()
		l.Close()
		select {
		case e := <-stopped:
			if e == nil {
				t.Fatal("listener failure was lost")
			}
		case <-time.After(3 * time.Second):
			t.Fatal("listener failure stranded active relay")
		}
		c.SetReadDeadline(time.Now().Add(time.Second))
		b := make([]byte, 1)
		_, e = c.Read(b)
		if e == nil {
			t.Fatal("active connection survived listener failure")
		}
	})
	t.Run("CancellationClosesActiveConnections", func(t *testing.T) {
		c := socksConnect(t, socks, 1, echoHost, echoPort)
		defer c.Close()
		stop()
		c.SetReadDeadline(time.Now().Add(3 * time.Second))
		b := make([]byte, 1)
		_, e := c.Read(b)
		if e == nil {
			t.Fatal("active flow survived cancellation")
		}
		if ne, ok := e.(net.Error); ok && ne.Timeout() {
			t.Fatal("cancellation left active connection stranded")
		}
	})
}
func split(t *testing.T, address string) (string, uint16) {
	t.Helper()
	h, p, e := net.SplitHostPort(address)
	if e != nil {
		t.Fatal(e)
	}
	n, e := strconv.Atoi(p)
	if e != nil {
		t.Fatal(e)
	}
	return h, uint16(n)
}
func socksConnect(t *testing.T, address string, mode byte, host string, port uint16) net.Conn {
	t.Helper()
	c, e := net.DialTimeout("tcp", address, time.Second)
	if e != nil {
		t.Fatal(e)
	}
	c.SetDeadline(time.Now().Add(15 * time.Second))
	c.Write([]byte{5, 1, 0})
	b := make([]byte, 2)
	if _, e = io.ReadFull(c, b); e != nil || !bytes.Equal(b, []byte{5, 0}) {
		c.Close()
		t.Fatalf("SOCKS greeting: %v %v", b, e)
	}
	c.Write(append([]byte{5, mode, 0}, encodeAddress(host, port)...))
	head := make([]byte, 3)
	if _, e = io.ReadFull(c, head); e != nil || !bytes.Equal(head, []byte{5, 0, 0}) {
		c.Close()
		t.Fatalf("SOCKS reply: %v %v", head, e)
	}
	if mode == 3 {
		return c
	} // UDP helper reads the remaining bound address.
	if _, _, e = readAddress(c); e != nil {
		c.Close()
		t.Fatal(e)
	}
	return c
}
func socksUDP(t *testing.T, address string) (net.Conn, *net.UDPConn) {
	t.Helper()
	c := socksConnect(t, address, 3, "0.0.0.0", 0)
	host, port, e := readAddress(c)
	if e != nil {
		c.Close()
		t.Fatal(e)
	}
	c.SetDeadline(time.Time{})
	u, e := net.DialUDP("udp4", nil, &net.UDPAddr{IP: net.ParseIP(host), Port: int(port)})
	if e != nil {
		c.Close()
		t.Fatal(e)
	}
	return c, u
}
