#!/bin/bash
# Captures HotkeyAI's own windows and composes 2880x1800 App Store screenshots
# into AppStore/screenshots/. Uses a temporary unsandboxed copy of the app so the
# raw captures can be written outside the app container; the store build is unchanged.
set -euo pipefail
cd "$(dirname "$0")/.."

scripts/build-app.sh release >/dev/null
RAW="$PWD/build/screenshots-raw"
COPY="$PWD/build/screenshot-app/HotkeyAI.app"
rm -rf "$RAW" "$(dirname "$COPY")" && mkdir -p "$RAW" "$(dirname "$COPY")"
cp -R build/HotkeyAI.app "$COPY"
codesign --force --sign - "$COPY" >/dev/null 2>&1

osascript -e 'tell application id "com.nauroman.HotkeyAI" to quit' >/dev/null 2>&1 || true
sleep 1
"$COPY/Contents/MacOS/HotkeyAI" -HKScreenshots YES -HKScreenshotDir "$RAW" >/dev/null 2>&1

swiftc -O scripts/screenshots/main.swift Sources/HotkeyAI/IconArt.swift -o build/compose-screenshots
mkdir -p AppStore/screenshots
build/compose-screenshots "$RAW" AppStore/screenshots
echo "Done: AppStore/screenshots"
