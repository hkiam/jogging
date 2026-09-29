#!/bin/bash
# Runs all self-checks headless in one Unity start (a few seconds). Exit code 0 = all passed.
# Usage: Tools/check.sh            (Unity must not have the project open)
set -u
cd "$(dirname "$0")/.."
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity}"
LOG="$(mktemp -t jogging-checks)"
"$UNITY" -batchmode -nographics -projectPath . -buildTarget OSXUniversal \
  -executeMethod Jogging.EditorTools.AllChecks.RunBatch -logFile "$LOG"
rc=$?
grep -E "error CS|\[AllChecks\]|FAIL" "$LOG" | awk '!seen[$0]++'
[ $rc -eq 0 ] || { echo "Log: $LOG"; exit $rc; }
rm -f "$LOG"

# QR codes: encode in Unity, decode with the macOS barcode detector (Vision)
QR="$(mktemp -d -t jogging-qr)"
"$UNITY" -batchmode -nographics -projectPath . -buildTarget OSXUniversal \
  -executeMethod Jogging.EditorTools.QrExport.RunBatch -qrout "$QR" -logFile "$QR/unity.log" >/dev/null
swift Tools/qrcheck.swift "$QR" | sed 's/^/[QR] /'
rc=${PIPESTATUS[0]}
rm -rf "$QR"
exit $rc
