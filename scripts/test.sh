#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/env.sh
rm -rf build/engine-classes
mkdir -p build/engine-classes build/reports
# Compile both engine and the loopback-only fixture afresh; fixture is excluded from APK.
find engine/src engine/test -name '*.java' -print0 | xargs -0 javac --release 8 -Xlint:all,-options -Werror -d build/engine-classes
java -cp build/engine-classes io.tethermax.engine.RelayDnsTest
java -cp build/engine-classes io.tethermax.engine.DnsOverHttpsTest
export TETHERMAX_TEST_CLASSES="$PWD/build/engine-classes"
javac --release 8 -Xlint:all,-options -Werror -d build/engine-classes android/src/io/tethermax/app/ServiceState.java android/test/io/tethermax/app/ServiceStateTest.java
java -cp build/engine-classes io.tethermax.app.ServiceStateTest
if command -v cygpath >/dev/null; then
 export TETHERMAX_TEST_CLASSES="$(cygpath -w "$PWD/build/engine-classes")"
fi
cd host
go vet ./...
go test -race -count=1 -v ./... 2>&1 | tee ../build/reports/go-test.txt
