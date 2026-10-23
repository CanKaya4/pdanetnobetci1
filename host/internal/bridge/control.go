package bridge

import (
	"context"
	"crypto/rand"
	"encoding/binary"
	"encoding/json"
	"errors"
	"io"
	"net"
	"sync"
	"time"

	"github.com/CanKaya4/pdanetnobetci1/host/internal/traffic"
)

type RemoteStatus struct {
	Mode       traffic.Mode `json:"mode"`
	Revision   uint64       `json:"revision"`
	Uploaded   uint64       `json:"uploaded"`
	Downloaded uint64       `json:"downloaded"`
	Network    string       `json:"network"`
	HostAgeMs  int64        `json:"hostAgeMs"`
}
type Control struct {
	net.Conn
	mu sync.Mutex
}

func (t Transport) OpenControl(ctx context.Context) (*Control, error) {
	c, err := t.dialContext(ctx, 3, "control", 1)
	if err != nil {
		return nil, err
	}
	return &Control{Conn: c}, nil
}
func (c *Control) Status() (RemoteStatus, error) {
	c.mu.Lock()
	defer c.mu.Unlock()
	var result RemoteStatus
	c.SetDeadline(time.Now().Add(2 * time.Second))
	if _, err := c.Write([]byte{1}); err != nil {
		return result, err
	}
	var size [4]byte
	if _, err := io.ReadFull(c, size[:]); err != nil {
		return result, err
	}
	n := binary.BigEndian.Uint32(size[:])
	if n < 2 || n > 8192 {
		return result, errors.New("invalid remote status length")
	}
	b := make([]byte, n)
	if _, err := io.ReadFull(c, b); err != nil {
		return result, err
	}
	err := json.Unmarshal(b, &result)
	return result, err
}
func (c *Control) SetMode(mode traffic.Mode) error {
	if err := (traffic.Settings{Mode: mode}).Validate(); err != nil {
		return err
	}
	c.mu.Lock()
	defer c.mu.Unlock()
	c.SetDeadline(time.Now().Add(2 * time.Second))
	if _, err := c.Write(append([]byte{2, byte(len(mode))}, []byte(mode)...)); err != nil {
		return err
	}
	var status [1]byte
	_, err := io.ReadFull(c, status[:])
	if err == nil && status[0] != 0 {
		return errors.New("phone rejected mode")
	}
	return err
}
func (c *Control) Publish(snapshot traffic.Snapshot) error {
	b, err := json.Marshal(snapshot)
	if err != nil {
		return err
	}
	if len(b) > 4096 {
		return errors.New("telemetry record too large")
	}
	c.mu.Lock()
	defer c.mu.Unlock()
	c.SetDeadline(time.Now().Add(2 * time.Second))
	frame := binary.BigEndian.AppendUint16([]byte{3}, uint16(len(b)))
	frame = append(frame, b...)
	if _, err = c.Write(frame); err != nil {
		return err
	}
	var status [1]byte
	_, err = io.ReadFull(c, status[:])
	if err == nil && status[0] != 0 {
		return errors.New("phone rejected telemetry")
	}
	return err
}

// QueryRTT measures one real DNS transaction through the authenticated relay.
// No host DNS resolver or direct-Windows connectivity fallback is used.
func (t Transport) QueryRTT(ctx context.Context) (time.Duration, error) {
	ctx, cancel := context.WithTimeout(ctx, 2*time.Second)
	defer cancel()
	c, err := t.DialContext(ctx, 2, "1.1.1.1", 53)
	if err != nil {
		return 0, err
	}
	defer c.Close()
	stop := context.AfterFunc(ctx, func() { c.Close() })
	defer stop()
	c.SetDeadline(time.Now().Add(2 * time.Second))
	query := []byte{0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 7, 'e', 'x', 'a', 'm', 'p', 'l', 'e', 3, 'c', 'o', 'm', 0, 0, 1, 0, 1}
	if _, err = rand.Read(query[:2]); err != nil {
		return 0, err
	}
	start := time.Now()
	frame := binary.BigEndian.AppendUint16(nil, uint16(len(query)))
	frame = append(frame, query...)
	if _, err = c.Write(frame); err != nil {
		return 0, err
	}
	var size [2]byte
	if _, err = io.ReadFull(c, size[:]); err != nil {
		return 0, err
	}
	n := int(binary.BigEndian.Uint16(size[:]))
	if n < 12 || n > 4096 {
		return 0, errors.New("invalid DNS probe frame")
	}
	b := make([]byte, n)
	if _, err = io.ReadFull(c, b); err != nil {
		return 0, err
	}
	if b[0] != query[0] || b[1] != query[1] || b[2]&128 == 0 || b[3]&15 != 0 || binary.BigEndian.Uint16(b[6:8]) == 0 {
		return 0, errors.New("invalid DNS probe reply")
	}
	return time.Since(start), nil
}
