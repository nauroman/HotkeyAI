#!/bin/bash
# Builds build/HotkeyAI.app with only the Command Line Tools (no Xcode needed).
# Usage: scripts/build-app.sh [debug|release]
# Signing: SIGN_IDENTITY if set; otherwise your "Apple Development" certificate; otherwise
# the local "HotkeyAI Local Signing" certificate (scripts/make-signing-cert.sh); otherwise
# an ad-hoc signature.
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

# macOS ties the Accessibility permission and the saved keys/sign-in to the signature.
# - Apple Development: has your team ID, so a rebuild keeps both. Keychain items belong to
#   the team ("teamid:" partition), not to one binary.
# - HotkeyAI Local Signing: keeps Accessibility, but a self-signed certificate has no team
#   ID, so each keychain item belongs to the exact binary that saved it ("cdhash:"
#   partition). The next build can't read it: ChatGPT sign-in and API keys must be entered again.
# - Ad-hoc: both must be granted again after every rebuild.
if [ -z "${SIGN_IDENTITY:-}" ]; then
  SIGN_IDENTITY="$(security find-identity -v -p codesigning 2>/dev/null \
    | grep '"Apple Development: ' | head -1 | awk '{print $2}')"
fi
if [ -z "$SIGN_IDENTITY" ]; then
  SIGN_IDENTITY="$(security find-identity -p codesigning 2>/dev/null \
    | grep '"HotkeyAI Local Signing"' | head -1 | awk '{print $2}')"
  if [ -n "$SIGN_IDENTITY" ]; then
    echo "warning: no Apple Development certificate; with HotkeyAI Local Signing" \
      "each rebuild loses the saved ChatGPT sign-in and API keys" >&2
  fi
fi
if [ -z "$SIGN_IDENTITY" ]; then
  echo "warning: no signing certificate; using ad-hoc (run scripts/make-signing-cert.sh)" >&2
  SIGN_IDENTITY="-"
fi
codesign --force --sign "$SIGN_IDENTITY" \
  --entitlements Resources/HotkeyAI.entitlements \
  --options runtime \
  "$APP"

echo "Built $APP ($(codesign -dvv "$APP" 2>&1 | sed -n 's/^Authority=//p' | head -1 || true))"
