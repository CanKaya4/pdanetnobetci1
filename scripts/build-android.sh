#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/env.sh
SDK_BUILD_TOOLS="$ANDROID_SDK_ROOT/build-tools/35.0.0"
ANDROID_JAR="$ANDROID_SDK_ROOT/platforms/android-35/android.jar"
rm -rf build/android/classes build/android/dex
mkdir -p build/android/classes build/android/dex build/artifacts build/reports
# No Gradle/plugin/dependency downloads; pinned SDK tools and Android framework only.
"$SDK_BUILD_TOOLS/aapt2" link -o build/android/base.apk --manifest android/AndroidManifest.xml -I "$ANDROID_JAR" --min-sdk-version 26 --target-sdk-version 35
find engine/src android/src -name '*.java' -print0 | xargs -0 javac --release 8 -Xlint:all,-options -classpath "$ANDROID_JAR" -d build/android/classes
find build/android/classes -name '*.class' -print > build/android/classes.list
"$SDK_BUILD_TOOLS/d8" --lib "$ANDROID_JAR" --min-api 26 --output build/android/dex @build/android/classes.list
cp build/android/base.apk build/android/unsigned.apk
(cd build/android/dex && zip -q -u ../unsigned.apk classes.dex)
"$SDK_BUILD_TOOLS/zipalign" -f -p 4 build/android/unsigned.apk build/android/aligned.apk
# Development key is generated locally and ignored. Not a production signing identity.
if [[ ! -f build/android/debug.keystore ]]; then
 keytool -genkeypair -keystore build/android/debug.keystore -storepass android -keypass android -alias androiddebugkey -keyalg RSA -keysize 3072 -validity 3650 -dname 'CN=TetherMax Development' -noprompt
fi
"$SDK_BUILD_TOOLS/apksigner" sign --ks build/android/debug.keystore --ks-pass pass:android --ks-key-alias androiddebugkey --out build/artifacts/tethermax-android-debug.apk build/android/aligned.apk
"$SDK_BUILD_TOOLS/apksigner" verify --verbose build/artifacts/tethermax-android-debug.apk
"$SDK_BUILD_TOOLS/aapt2" dump badging build/artifacts/tethermax-android-debug.apk > build/reports/apk-badging.txt
