#!/usr/bin/env bash
# Source from repository scripts. Overrides support CI and other cloud instances.
TETHERMAX_TOOLS="${TETHERMAX_TOOLS:-/workspace/toolchains}"
export JAVA_HOME="${JAVA_HOME:-$TETHERMAX_TOOLS/jdk21}"
export ANDROID_SDK_ROOT="${ANDROID_SDK_ROOT:-$TETHERMAX_TOOLS/android-sdk}"
export ANDROID_USER_HOME="${ANDROID_USER_HOME:-$TETHERMAX_TOOLS/android-user}"
export GOCACHE="${GOCACHE:-$TETHERMAX_TOOLS/go-cache}"
export PATH="$JAVA_HOME/bin:$TETHERMAX_TOOLS/go/bin:$ANDROID_SDK_ROOT/platform-tools:$PATH"
