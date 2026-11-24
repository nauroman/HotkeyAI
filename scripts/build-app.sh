#!/bin/bash
# Builds build/HotkeyAI.app with only the Command Line Tools (no Xcode needed).
# Usage: scripts/build-app.sh [debug|release]
# Signing: SIGN_IDENTITY if set; otherwise the local "HotkeyAI Local Signing" certificate
# (create it once with scripts/make-signing-cert.sh); otherwise an ad-hoc signature.
set -euo pipefail
cd "$(dirname "$0")/.."

CONFIG="${1:-release}"
APP="build/HotkeyAI.app"

swift build -c "$CONFIG"
BIN_DIR="$(swift build -c "$CONFIG" --show-bin-path)"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/HotkeyAI" "$APP/Contents/MacOS/HotkeyAI"
cp Resources/Info.plist "$APP/Contents/Info.plist"

cp Resources/AppIcon.icns Resources/PrivacyInfo.xcprivacy "$APP/Contents/Resources/"

# macOS ties permissions (Accessibility, Keychain) to the signature. A certificate keeps
# them across rebuilds; an ad-hoc signature must be re-approved after every rebuild.
if [ -z "${SIGN_IDENTITY:-}" ]; then
  SIGN_IDENTITY="$(security find-identity -p codesigning 2>/dev/null \
    | grep '"HotkeyAI Local Signing"' | head -1 | awk '{print $2}')"
fi
if [ -z "$SIGN_IDENTITY" ]; then
  echo "warning: no signing certificate; using ad-hoc (run scripts/make-signing-cert.sh)" >&2
  SIGN_IDENTITY="-"
fi
codesign --force --sign "$SIGN_IDENTITY" \
  --entitlements Resources/HotkeyAI.entitlements \
  --options runtime \
  "$APP"

echo "Built $APP"
