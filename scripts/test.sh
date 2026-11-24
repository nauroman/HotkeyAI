#!/bin/bash
# Runs the unit tests with the Command Line Tools.
# The CLT's build engine sometimes doesn't hand the Swift Testing macro plugin
# to the compiler ("plugin for module 'TestingMacros' not found"), so pass its
# folder explicitly. Xcode doesn't need this.
set -euo pipefail
cd "$(dirname "$0")/.."

PLUGINS="$(dirname "$(xcrun --find swift)")/../lib/swift/host/plugins/testing"
if [ -d "$PLUGINS" ]; then
  exec swift test -Xswiftc -plugin-path -Xswiftc "$PLUGINS" "$@"
else
  exec swift test "$@"
fi
