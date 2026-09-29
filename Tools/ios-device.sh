#!/bin/bash
# Signs, installs and starts the app on a connected iPad (Xcode project from MobileBuild.BuildIosDeviceBatch).
# Needs: an Apple ID in Xcode (Settings → Accounts) and developer mode on the iPad.
#   Tools/ios-device.sh <TEAM_ID> [device-udid]
# The team id is in Xcode → Settings → Accounts → the team (10 characters), or: security find-identity -v -p codesigning
set -eu
cd "$(dirname "$0")/.."
TEAM="$1"
DEV="${2:-$(xcrun devicectl list devices 2>/dev/null | awk '/physical/ && /iPad/ {for(i=1;i<=NF;i++) if ($i ~ /^[0-9A-F]{8}-[0-9A-F]{16}$/) print $i}' | head -1)}"
DD=Builds/iOS-Device-DerivedData
xcodebuild -project Builds/iOS-Device/Unity-iPhone.xcodeproj -scheme Unity-iPhone -configuration Release \
  -destination "id=$DEV" -derivedDataPath "$DD" -allowProvisioningUpdates \
  DEVELOPMENT_TEAM="$TEAM" CODE_SIGN_STYLE=Automatic build | tail -3
APP=$(ls -d "$DD"/Build/Products/Release-iphoneos/*.app | head -1)
xcrun devicectl device install app --device "$DEV" "$APP"
xcrun devicectl device process launch --device "$DEV" local.jogging.app || echo "Start: auf dem iPad antippen (beim ersten Mal: Einstellungen → Allgemein → VPN & Geräteverwaltung → Entwickler vertrauen)"
