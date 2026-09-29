#!/bin/bash
# Builds the macOS BLE bridge into ~/Applications/JoggingBleScan.app (ad-hoc signed).
# Keep bundle id + path stable: the macOS Bluetooth permission is tied to them.
# First run: allow it under System Settings → Privacy & Security → Bluetooth.
set -e
cd "$(dirname "$0")"
APP="$HOME/Applications/JoggingBleScan.app"
mkdir -p "$APP/Contents/MacOS"
swiftc -O main.swift -o "$APP/Contents/MacOS/JoggingBleBridge"
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>JoggingBleScan</string>
  <key>CFBundleDisplayName</key><string>Jogging Laufband-Bridge</string>
  <key>CFBundleIdentifier</key><string>local.jogging.blescan</string>
  <key>CFBundleExecutable</key><string>JoggingBleBridge</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleVersion</key><string>3.1</string>
  <key>LSUIElement</key><true/>
  <key>NSBluetoothAlwaysUsageDescription</key><string>Verbindet die Jogging-App mit dem Laufband (FitShow/FTMS) und dem Pulsgurt.</string>
</dict></plist>
PLIST
rm -f "$APP/Contents/MacOS/JoggingBleScan" "$APP/Contents/MacOS/run"
rm -rf "$APP/Contents/Resources"
codesign --force --deep -s - "$APP"
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$APP"
echo "Installed: $APP"
