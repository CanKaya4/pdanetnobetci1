package bridge

import (
	"bytes"
	"context"
	"encoding/binary"
	"errors"
	"io"
	"net"
	"strconv"
	"sync"
	"time"
)

type Server struct{ Transport Transport }

func (s Server) Serve(ctx context.Context, listener net.Listener) error {
	if err := s.Transport.Validate(); err != nil {
		return err
	}
	if err := LoopbackAddress(listener.Addr().String()); err != nil {
		return err
	}
	var wg sync.WaitGroup
	slots := make(chan struct{}, 128)
	done := make(chan struct{})
	defer close(done)
	go func() {
		select {
		case <-ctx.Done():
			listener.Close()
		case <-done:
		}
	}()
	defer wg.Wait()
	for {
		c, err := listener.Accept()
		if err != nil {
			if ctx.Err() != nil {
				return nil
			}
			return err
		}
		select {
		case slots <- struct{}{}:
		default:
			c.Close()
			continue
		}
		wg.Add(1)
		go func() {
			defer wg.Done()
			defer func() { <-slots }()
			defer c.Close()
			finished := make(chan struct{})
			defer close(finished)
			go func() {
				select {
				case <-ctx.Done():
					c.Close()
				case <-finished:
				}
			}()
			_ = s.handle(ctx, c)
		}()
	}
}

func reply(c net.Conn, code byte, address net.Addr) error {
	host := "127.0.0.1"
	port := uint16(0)
	if address != nil {
		h, p, e := net.SplitHostPort(address.String())
		if e != nil {
			return e
		}
		host = h
		n, e := strconv.Atoi(p)
		if e != nil {
			return e
		}
		port = uint16(n)
	}
	_, err := c.Write(append([]byte{5, code, 0}, encodeAddress(host, port)...))
	return err
}

func (s Server) handle(ctx context.Context, c net.Conn) error {
	c.SetDeadline(time.Now().Add(10 * time.Second))
	head := make([]byte, 2)
	if _, err := io.ReadFull(c, head); err != nil {
		return err
	}
	if head[0] != 5 || head[1] == 0 {
		return errors.New("invalid SOCKS greeting")
	}
	methods := make([]byte, int(head[1]))
	if _, err := io.ReadFull(c, methods); err != nil {
		return err
	}
	if !bytes.Contains(methods, []byte{0}) {
		c.Write([]byte{5, 255})
		return errors.New("no supported authentication method")
	}
	if _, err := c.Write([]byte{5, 0}); err != nil {
		return err
	}
	request := make([]byte, 3)
	if _, err := io.ReadFull(c, request); err != nil {
		return err
	}
	if request[0] != 5 || request[2] != 0 {
		reply(c, 1, nil)
		return errors.New("invalid SOCKS request")
	}
	host, port, err := readAddress(c)
	if err != nil {
		reply(c, 8, nil)
		return err
	}
	switch request[1] {
	case 1:
		remote, err := s.Transport.DialContext(ctx, 1, host, port)
		if err != nil {
			reply(c, 5, nil)
			return err
		}
		defer remote.Close()
		if err = reply(c, 0, nil); err != nil {
			return err
		}
		c.SetDeadline(time.Time{})
		ended := make(chan struct{})
		defer close(ended)
		go func() {
			select {
			case <-ctx.Done():
				remote.Close()
			case <-ended:
			}
		}()
		upload := make(chan struct{})
		go func() {
			defer close(upload)
			_, e := io.Copy(remote, c)
			if e != nil {
				remote.Close()
				c.Close()
				return
			}
			if tcp, ok := remote.(*net.TCPConn); ok {
				tcp.CloseWrite()
			}
		}()
		_, err = io.Copy(c, remote)
		if tcp, ok := c.(*net.TCPConn); ok {
			tcp.CloseWrite()
		}
		// Preserve a remote half-close: the client may still be uploading.
		// Context cancellation closes both sockets if the remaining half stays idle.
		if err != nil {
			c.Close()
			remote.Close()
		}
		<-upload
		return err
	case 3:
		c.SetDeadline(time.Time{})
		return s.udp(ctx, c)
	default:
		reply(c, 7, nil)
		return errors.New("unsupported SOCKS command")
	}
}

func (s Server) udp(ctx context.Context, control net.Conn) error {
	socket, err := net.ListenUDP("udp4", &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1)})
	if err != nil {
		return err
	}
	defer socket.Close()
	if err = reply(control, 0, socket.LocalAddr()); err != nil {
		return err
	}
	var mu sync.Mutex
	flows := map[string]net.Conn{}
	stopped := false
	done := make(chan struct{})
	go func() {
		io.Copy(io.Discard, control)
		mu.Lock()
		stopped = true
		for _, f := range flows {
			f.Close()
		}
		mu.Unlock()
		socket.Close()
		close(done)
	}()
	defer func() { control.Close(); <-done }()
	var peer *net.UDPAddr
	buffer := make([]byte, 65535)
	for {
		n, from, err := socket.ReadFromUDP(buffer)
		if err != nil {
			return nil
		}
		if !from.IP.IsLoopback() {
			continue
		}
		if peer != nil && (peer.Port != from.Port || !peer.IP.Equal(from.IP)) {
			continue
		}
		if n < 4 || buffer[0] != 0 || buffer[1] != 0 || buffer[2] != 0 {
			continue
		}
		r := bytes.NewReader(buffer[3:n])
		host, port, err := readAddress(r)
		if err != nil || port == 0 {
			continue
		}
		length := r.Len()
		if length > MaxDatagram {
			continue
		}
		if peer == nil {
			peer = from
		}
		payload := buffer[n-length : n]
		key := net.JoinHostPort(host, strconv.Itoa(int(port)))
		mu.Lock()
		remote := flows[key]
		full := len(flows) >= 64
		closed := stopped
		mu.Unlock()
		if closed {
			return nil
		}
		if remote == nil {
			if full {
				continue
			}
			remote, err = s.Transport.DialContext(ctx, 2, host, port)
			if err != nil {
				continue
			}
			mu.Lock()
			if stopped {
				mu.Unlock()
				remote.Close()
				return nil
			}
			flows[key] = remote
			mu.Unlock()
			address := encodeAddress(host, port)
			go func(key string, remote net.Conn, address []byte, peer net.UDPAddr) {
				defer remote.Close()
				defer func() {
					mu.Lock()
					if flows[key] == remote {
						delete(flows, key)
					}
					mu.Unlock()
				}()
				for {
					remote.SetReadDeadline(time.Now().Add(60 * time.Second))
					b := make([]byte, 2)
					if _, err := io.ReadFull(remote, b); err != nil {
						return
					}
					size := int(binary.BigEndian.Uint16(b))
					if size > MaxDatagram {
						return
					}
					data := make([]byte, size)
					if _, err := io.ReadFull(remote, data); err != nil {
						return
					}
					packet := append(append([]byte{0, 0, 0}, address...), data...)
					if _, err := socket.WriteToUDP(packet, &peer); err != nil {
						return
					}
				}
			}(key, remote, address, *peer)
		}
		remote.SetWriteDeadline(time.Now().Add(5 * time.Second))
		frame := binary.BigEndian.AppendUint16(nil, uint16(length))
		frame = append(frame, payload...)
		if _, err := remote.Write(frame); err != nil {
			remote.Close()
		} else {
			remote.SetReadDeadline(time.Now().Add(60 * time.Second))
		}
	}
}
