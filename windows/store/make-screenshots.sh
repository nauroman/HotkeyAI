#!/bin/bash
# Builds the Microsoft Store screenshots (1920x1080) from the window captures in the CI
# self-test output (the HotkeyAI-tests artifact). Usage on a Mac:
#   windows/store/make-screenshots.sh <unzipped HotkeyAI-tests>/self-test
set -euo pipefail
cd "$(dirname "$0")/../.."
mkdir -p build
swiftc -O windows/store/screenshots-generator/main.swift -o build/make-store-screenshots
build/make-store-screenshots "$1" windows/store/screenshots
