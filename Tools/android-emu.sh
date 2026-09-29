#!/bin/bash
# Installs Builds/Android/Jogging.apk in the Android emulator (AVD "JoggingTablet") and starts it.
# Starts the emulator if none is running. Uses the Android SDK that ships with Unity.
# Log: adb logcat -s Unity
set -eu
cd "$(dirname "$0")/.."
SDK=/Applications/Unity/Hub/Editor/6000.3.24f1/PlaybackEngines/AndroidPlayer/SDK
export ANDROID_SDK_ROOT=$SDK ANDROID_HOME=$SDK
ADB=$SDK/platform-tools/adb
if ! $ADB devices | grep -q "emulator-"; then
  nohup $SDK/emulator/emulator -avd JoggingTablet -no-snapshot-save -no-boot-anim -gpu host >/dev/null 2>&1 &
  $ADB wait-for-device
  until [ "$($ADB shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; do sleep 2; done
fi
$ADB install -r Builds/Android/Jogging.apk
$ADB logcat -c
$ADB shell monkey -p local.jogging.app -c android.intent.category.LAUNCHER 1 >/dev/null
echo "gestartet – Log: $ADB logcat -s Unity"
