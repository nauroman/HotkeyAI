#!/bin/bash
# Archives HotkeyAI with Xcode and uploads it to App Store Connect.
# Needs: Xcode in /Applications, your Apple ID in Xcode → Settings → Accounts,
# and the app record created in App Store Connect (bundle ID com.nauroman.HotkeyAI).
# Usage: TEAM_ID=ABCDE12345 scripts/appstore-upload.sh
# Bump CFBundleVersion in Resources/Info.plist before every new upload.
set -euo pipefail
cd "$(dirname "$0")/.."

: "${TEAM_ID:?Set TEAM_ID to your Team ID (developer.apple.com → Account → Membership details)}"
export DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode.app/Contents/Developer}"

ARCHIVE=build/HotkeyAI.xcarchive
rm -rf "$ARCHIVE" build/export
mkdir -p build

xcodebuild -project HotkeyAI.xcodeproj -scheme HotkeyAI -configuration Release \
  -destination "generic/platform=macOS" -archivePath "$ARCHIVE" \
  DEVELOPMENT_TEAM="$TEAM_ID" -allowProvisioningUpdates archive

sed "s/TEAM_ID_PLACEHOLDER/$TEAM_ID/" scripts/ExportOptions.plist > build/ExportOptions.plist
xcodebuild -exportArchive -archivePath "$ARCHIVE" -exportOptionsPlist build/ExportOptions.plist \
  -exportPath build/export -allowProvisioningUpdates

echo "Uploaded. After processing (10–30 min) the build appears in App Store Connect → your app → TestFlight."
