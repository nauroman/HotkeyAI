#!/bin/bash
# Builds build/release/HotkeyAI-<version>.dmg for GitHub releases: the same archive as the
# App Store build, signed with Developer ID and notarized by Apple.
# Xcode signs with the team's cloud-managed Developer ID certificate and submits the app to
# the notary service with the Apple ID from Xcode → Settings → Accounts, so no notarytool
# password or API key is needed. Uses build/HotkeyAI.xcarchive (made by appstore-upload.sh),
# or archives first when it is missing.
# Usage: TEAM_ID=ABCDE12345 scripts/developer-id-release.sh
set -euo pipefail
cd "$(dirname "$0")/.."

: "${TEAM_ID:?Set TEAM_ID to your Team ID (developer.apple.com → Account → Membership details)}"
export DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode.app/Contents/Developer}"

ARCHIVE=build/HotkeyAI.xcarchive
OUT=build/devid
if [ ! -d "$ARCHIVE" ]; then
  xcodebuild -project HotkeyAI.xcodeproj -scheme HotkeyAI -configuration Release \
    -destination "generic/platform=macOS" -archivePath "$ARCHIVE" \
    DEVELOPMENT_TEAM="$TEAM_ID" -allowProvisioningUpdates archive
fi
VERSION="$(/usr/libexec/PlistBuddy -c 'Print ApplicationProperties:CFBundleShortVersionString' "$ARCHIVE/Info.plist")"

rm -rf "$OUT" && mkdir -p "$OUT"
sed "s/TEAM_ID_PLACEHOLDER/$TEAM_ID/" scripts/ExportOptions-DeveloperID.plist > "$OUT/ExportOptions.plist"
xcodebuild -exportArchive -archivePath "$ARCHIVE" -exportOptionsPlist "$OUT/ExportOptions.plist" \
  -exportPath "$OUT/upload" -allowProvisioningUpdates

echo "Waiting for Apple's notary service (usually 2–15 minutes)…"
for _ in $(seq 1 90); do
  rm -rf "$OUT/notarized"
  if result="$(xcodebuild -exportNotarizedApp -archivePath "$ARCHIVE" -exportPath "$OUT/notarized" 2>&1)"; then
    break
  fi
  echo "$result" | grep -q 'processing' || { echo "$result" >&2; exit 1; }
  sleep 20
done
APP="$OUT/notarized/HotkeyAI.app"
[ -d "$APP" ] || { echo "Notarization did not finish in 30 minutes; run the script again later." >&2; exit 1; }
xcrun stapler validate "$APP"
spctl --assess --type execute --verbose=2 "$APP"

# Disk image: the app next to a link to /Applications.
mkdir -p build/release
DMG="build/release/HotkeyAI-$VERSION.dmg"
STAGE="$OUT/dmg"
rm -rf "$STAGE" "$DMG" && mkdir -p "$STAGE"
ditto "$APP" "$STAGE/HotkeyAI.app"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "HotkeyAI $VERSION" -srcfolder "$STAGE" -fs HFS+ -format UDZO -ov "$DMG"
(cd build/release && shasum -a 256 "$(basename "$DMG")" > SHA256SUMS.txt && cat SHA256SUMS.txt)
echo "Built $DMG"
