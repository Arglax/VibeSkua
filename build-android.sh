#!/usr/bin/env bash
# Builds the MoVibeSkua APK. Override ANDROID_SDK / JAVA_SDK / DOTNET for your machine.
set -euo pipefail
cd "$(dirname "$0")"
DOTNET="${DOTNET:-dotnet}"
ANDROID_SDK="${ANDROID_SDK:-$LOCALAPPDATA/Android/Sdk}"
JAVA_SDK="${JAVA_SDK:-C:/Program Files/Java/jdk-24}"
CONFIG="${1:-Release}"
shift || true
"$DOTNET" build MoVibeSkua.Android -c "$CONFIG" -f net10.0-android \
  -p:AndroidSdkDirectory="$ANDROID_SDK" -p:JavaSdkDirectory="$JAVA_SDK" "$@"
