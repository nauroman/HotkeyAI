#!/bin/bash
# Regenerates Resources/AppIcon.icns, Resources/AppIcon-1024.png and the
# menu bar template glyphs from Sources/HotkeyAI/IconArt.swift.
set -euo pipefail
cd "$(dirname "$0")/.."

mkdir -p build
swiftc -O scripts/icons/main.swift Sources/HotkeyAI/IconArt.swift -o build/make-icons
build/make-icons build/icons
iconutil -c icns build/icons/AppIcon.iconset -o Resources/AppIcon.icns
cp build/icons/AppIcon-1024.png Resources/AppIcon-1024.png
mkdir -p Resources/MenuBarIcon
cp build/icons/MenuBarIconTemplate*.png build/icons/MenuBarIconTemplate.pdf Resources/MenuBarIcon/
echo "Icons updated. Preview: build/icons/preview.png"

# Asset catalog for the Xcode / App Store build.
SET=Resources/Assets.xcassets/AppIcon.appiconset
rm -rf Resources/Assets.xcassets && mkdir -p "$SET"
printf '{\n  "info" : { "author" : "xcode", "version" : 1 }\n}\n' > Resources/Assets.xcassets/Contents.json
IMAGES=""
for s in 16 32 128 256 512; do
  for scale in 1 2; do
    suffix=""; [ $scale = 2 ] && suffix="@2x"
    file="icon_${s}x${s}${suffix}.png"
    cp "build/icons/AppIcon.iconset/$file" "$SET/$file"
    IMAGES="$IMAGES    { \"filename\" : \"$file\", \"idiom\" : \"mac\", \"scale\" : \"${scale}x\", \"size\" : \"${s}x${s}\" },\n"
  done
done
printf '{\n  "images" : [\n%b  ],\n  "info" : { "author" : "xcode", "version" : 1 }\n}\n' "${IMAGES%,\\n}\n" > "$SET/Contents.json"
echo "Asset catalog updated: $SET"
