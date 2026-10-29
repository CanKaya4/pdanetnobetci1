//go:build !windows

package traffic

import "errors"

func collectFocusOwners(_ []FocusProcess) (*ownerSnapshot, error) {
	return nil, errors.New("process flow ownership requires Windows")
}
