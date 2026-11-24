#!/bin/bash
# Regenerates the Windows icon and MSIX logo images from Sources/HotkeyAI/IconArt.swift
# (the same vector drawing as the Mac icon). Needs a Mac with the Command Line Tools.
set -euo pipefail
cd "$(dirname "$0")/../.."

mkdir -p build
swiftc -O windows/packaging/assets-generator/main.swift Sources/HotkeyAI/IconArt.swift -o build/make-windows-assets
build/make-windows-assets windows/src/HotkeyAI/Assets/HotkeyAI.ico windows/packaging/Assets windows/store/images
