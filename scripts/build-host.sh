#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/env.sh
mkdir -p build/artifacts
cd host
CGO_ENABLED=0 GOOS=windows GOARCH=amd64 go build -trimpath -buildvcs=false -o ../build/artifacts/tethermax-windows-amd64.exe ./cmd/tethermax
CGO_ENABLED=0 GOOS=windows GOARCH=arm64 go build -trimpath -buildvcs=false -o ../build/artifacts/tethermax-windows-arm64.exe ./cmd/tethermax
CGO_ENABLED=0 go build -trimpath -buildvcs=false -o ../build/tethermax-cloud ./cmd/tethermax
../build/tethermax-cloud status
# CLI must refuse physical testing without the explicit opt-in.
if ../build/tethermax-cloud serve; then echo 'ERROR: safety gate failed' >&2; exit 1; fi
python3 ../scripts/package.py
