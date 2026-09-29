#!/bin/bash
# Runs the end-to-end test (see Tools/e2e.sh) inside the iPad simulator or the Android emulator.
#   Tools/e2e-mobile.sh ios [device-udid]     (app built with Tools/ios-sim.sh)
#   Tools/e2e-mobile.sh android               (Builds/Android/Jogging.apk, emulator running)
# The app gets its start options like on the Mac (-e2e -datadir … -beltsim -hrsim -timescale 20);
# the data folder is inside the app's own storage. The result is read from e2e-result.txt.
set -u
cd "$(dirname "$0")/.."
ARGS="-e2e -beltsim -hrsim -timescale 20 ${E2E_ARGS:-}" # E2E_ARGS: extra options, e.g. "-lang en -units imperial"
case "${1:-}" in
ios)
  D="${2:-$(xcrun simctl list devices | grep -m1 'iPad.*(Booted)' | grep -o '[0-9A-F-]\{36\}')}"
  APP=$(ls -d Builds/iOS-DerivedData/Build/Products/Release-iphonesimulator/*.app | head -1)
  xcrun simctl install "$D" "$APP"
  C=$(xcrun simctl get_app_container "$D" local.jogging.app data)
  DATA="$C/Documents/e2e"; rm -rf "$DATA"; mkdir -p "$DATA"
  xcrun simctl terminate "$D" local.jogging.app 2>/dev/null
  # Unity's iOS player passes no command line on: the app reads JOGGING_ARGS (Core/Args.cs)
  SIMCTL_CHILD_JOGGING_ARGS="$ARGS -datadir $DATA" xcrun simctl launch --console-pty "$D" local.jogging.app > "$DATA/../e2e-console.log" 2>&1 &
  RES="$DATA/e2e-result.txt"; LOG="$DATA/../e2e-console.log"
  for i in $(seq 1 1800); do [ -f "$RES" ] && break; sleep 1; done
  sleep 2; xcrun simctl terminate "$D" local.jogging.app 2>/dev/null
  grep -aE "\[E2E\] (OK|FAIL|ERGEBNIS|--)" "$LOG" | tr -d '\r'
  [ -f "$RES" ] || { echo "kein Ergebnis (Log: $LOG)"; exit 1; }
  head -1 "$RES" | grep -q "^OK" ;;
android)
  SDK=/Applications/Unity/Hub/Editor/6000.3.24f1/PlaybackEngines/AndroidPlayer/SDK
  ADB=$SDK/platform-tools/adb
  $ADB install -r Builds/Android/Jogging.apk >/dev/null
  $ADB shell am force-stop local.jogging.app
  DATA=/sdcard/Android/data/local.jogging.app/files/e2e
  $ADB shell rm -rf $DATA; $ADB logcat -c
  # Unity reads its command line from the intent extra "unity"
  $ADB shell am start -n local.jogging.app/com.unity3d.player.UnityPlayerGameActivity -e unity "\"$ARGS -datadir $DATA\"" >/dev/null
  for i in $(seq 1 1800); do $ADB shell "[ -f $DATA/e2e-result.txt ]" && break; sleep 1; done
  sleep 2; $ADB shell am force-stop local.jogging.app
  $ADB logcat -d -s Unity | grep -aE "\[E2E\] (OK|FAIL|ERGEBNIS|--)" | sed 's/.*\[E2E\]/[E2E]/'
  $ADB shell cat $DATA/e2e-result.txt | head -1 | grep -q "^OK" ;;
*) echo "Aufruf: $0 ios|android"; exit 2 ;;
esac
