package bridge

import (
	"context"
	"encoding/binary"
	"encoding/hex"
	"errors"
	"fmt"
	"io"
	"net"
	"strconv"
	"strings"
	"time"
)

const MaxDatagram = 65507

type Transport struct{ Address, Token string }

func LoopbackAddress(address string) error {
	host, port, err := net.SplitHostPort(address)
	if err != nil {
		return err
	}
	ip := net.ParseIP(host)
	if ip == nil || !ip.IsLoopback() {
		return errors.New("only literal loopback addresses are allowed")
	}
	n, err := strconv.Atoi(port)
	if err != nil || n < 0 || n > 65535 {
		return errors.New("invalid port")
	}
	return nil
}

func (t Transport) Validate() error {
	if err := LoopbackAddress(t.Address); err != nil {
		return fmt.Errorf("USB endpoint: %w", err)
	}
	decoded, err := hex.DecodeString(t.Token)
	if err != nil || len(decoded) != 16 || strings.ToLower(t.Token) != t.Token {
		return errors.New("pairing code must be 32 lowercase hex characters")
	}
	return nil
}

func (t Transport) Dial(mode byte, host string, port uint16) (net.Conn, error) {
	return t.DialContext(context.Background(), mode, host, port)
}

func (t Transport) DialContext(ctx context.Context, mode byte, host string, port uint16) (net.Conn, error) {
	if mode != 1 && mode != 2 {
		return nil, errors.New("invalid flow mode")
	}
	return t.dialContext(ctx, mode, host, port)
}
func (t Transport) dialContext(ctx context.Context, mode byte, host string, port uint16) (net.Conn, error) {
	if err := t.Validate(); err != nil {
		return nil, err
	}
	if (mode != 1 && mode != 2 && mode != 3) || len(host) < 1 || len(host) > 253 || port == 0 {
		return nil, errors.New("invalid target")
	}
	for _, c := range host {
		if !strings.ContainsRune("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.:%_-", c) {
			return nil, errors.New("invalid hostname")
		}
	}
	c, err := (&net.Dialer{Timeout: 5 * time.Second}).DialContext(ctx, "tcp", t.Address)
	if err != nil {
		return nil, err
	}
	stopCancel := context.AfterFunc(ctx, func() { c.Close() })
	defer stopCancel()
	ok := false
	defer func() {
		if !ok {
			c.Close()
		}
	}()
	c.SetDeadline(time.Now().Add(15 * time.Second))
	header := append([]byte{'T', 'M', 'A', 'X', 1}, []byte(t.Token)...)
	header = append(header, mode, byte(len(host)>>8), byte(len(host)))
	header = append(header, []byte(host)...)
	header = binary.BigEndian.AppendUint16(header, port)
	if _, err = c.Write(header); err != nil {
		return nil, err
	}
	status := []byte{0}
	if _, err = io.ReadFull(c, status); err != nil {
		return nil, err
	}
	if status[0] != 0 {
		return nil, fmt.Errorf("phone rejected flow (status %d)", status[0])
	}
	c.SetDeadline(time.Time{})
	ok = true
	return c, nil
}

func readAddress(r io.Reader) (string, uint16, error) {
	b := []byte{0}
	if _, err := io.ReadFull(r, b); err != nil {
		return "", 0, err
	}
	kind := b[0]
	var n int
	switch kind {
	case 1:
		n = 4
	case 4:
		n = 16
	case 3:
		if _, err := io.ReadFull(r, b); err != nil {
			return "", 0, err
		}
		n = int(b[0])
		if n == 0 {
			return "", 0, errors.New("empty hostname")
		}
	default:
		return "", 0, errors.New("unsupported address type")
	}
	data := make([]byte, n+2)
	if _, err := io.ReadFull(r, data); err != nil {
		return "", 0, err
	}
	host := string(data[:n])
	if kind == 1 || kind == 4 {
		host = net.IP(data[:n]).String()
	}
	return host, binary.BigEndian.Uint16(data[n:]), nil
}

func encodeAddress(host string, port uint16) []byte {
	var b []byte
	ip := net.ParseIP(host)
	if ip4 := ip.To4(); ip4 != nil {
		b = append([]byte{1}, ip4...)
	} else if ip != nil {
		b = append([]byte{4}, ip.To16()...)
	} else {
		b = append([]byte{3, byte(len(host))}, []byte(host)...)
	}
	return binary.BigEndian.AppendUint16(b, port)
}
