package traffic

import (
	"io"
)

// Writer paces TCP payload before the actual socket write. UDP must instead
// Wait once for the whole datagram and Count after sending its intact frame.
type Writer struct {
	Unpaced bool
	Flow    *Flow
	Down    bool
	Writer  io.Writer
}

func (w Writer) Write(b []byte) (int, error) {
	total := 0
	for len(b) > 0 {
		chunk := 16384
		if !w.Unpaced {
			chunk = w.Flow.Chunk()
		}
		n := min(len(b), chunk)
		if !w.Unpaced {
			if err := w.Flow.Wait(w.Down, n); err != nil {
				return total, err
			}
		}
		written, err := w.Writer.Write(b[:n])
		w.Flow.Count(w.Down, written)
		total += written
		b = b[written:]
		if err != nil {
			return total, err
		}
		if written == 0 {
			return total, io.ErrShortWrite
		}
	}
	return total, nil
}
