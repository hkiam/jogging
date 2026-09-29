#!/bin/bash
# Builds the Xcode project from Builds/iOS for the iPad simulator and starts the app there.
# Needs a set-up Xcode (once: sudo xcodebuild -runFirstLaunch). Device: $DEVICE or the first iPad.
set -eu
cd "$(dirname "$0")/.."
DD=Builds/iOS-DerivedData
xcodebuild -project Builds/iOS/Unity-iPhone.xcodeproj -scheme Unity-iPhone -configuration Release \
  -sdk iphonesimulator -destination "generic/platform=iOS Simulator" -derivedDataPath "$DD" \
  CODE_SIGNING_ALLOWED=NO build | tail -3
APP=$(ls -d "$DD"/Build/Products/Release-iphonesimulator/*.app | head -1)
DEVICE="${DEVICE:-$(xcrun simctl list devices available | grep -m1 -o 'iPad[^(]*([0-9A-F-]*)' | grep -o '[0-9A-F-]\{36\}')}"
xcrun simctl boot "$DEVICE" 2>/dev/null || true
open "$(xcode-select -p)/Applications/Simulator.app" 2>/dev/null || true
xcrun simctl install "$DEVICE" "$APP"
xcrun simctl launch "$DEVICE" local.jogging.app
echo "gestartet auf $DEVICE ($APP)"
