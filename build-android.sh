#!/usr/bin/env bash
# Builds the MoVibeSkua APK. Override ANDROID_SDK / JAVA_SDK / DOTNET for your machine.
set -euo pipefail
cd "$(dirname "$0")"
if [ -z "${DOTNET:-}" ]; then
  if [ -x "$LOCALAPPDATA/dotnet10/dotnet.exe" ]; then
    DOTNET="$LOCALAPPDATA/dotnet10/dotnet.exe"
  else
    DOTNET="dotnet"
  fi
fi
if [ -z "${JAVA_SDK:-}" ]; then
  JDK21_DIR=$(ls -d "$LOCALAPPDATA"/jdk21/jdk-* 2>/dev/null | head -n 1 || true)
  if [ -n "$JDK21_DIR" ]; then
    JAVA_SDK="$JDK21_DIR"
  else
    JAVA_SDK="C:/Program Files/Java/jdk-24"
  fi
fi
ANDROID_SDK="${ANDROID_SDK:-$LOCALAPPDATA/Android/Sdk}"
CONFIG="${1:-Release}"
shift || true
# The bundled plugins are plain net10.0 libraries the APK carries as assets.
for plugin in Skua.Plugin.DailyTracker Skua.Plugin.ButlerV5 Skua.Plugin.Cosmetics; do
  "$DOTNET" build "$plugin" -c "$CONFIG" "$@"
done
"$DOTNET" build MoVibeSkua.Android -c "$CONFIG" -f net10.0-android \
  -p:AndroidSdkDirectory="$ANDROID_SDK" -p:JavaSdkDirectory="$JAVA_SDK" "$@"
