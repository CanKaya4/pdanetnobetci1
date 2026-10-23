//go:build !windows

package traffic

func RunningGames() ([]string, error) { return []string{}, nil }
