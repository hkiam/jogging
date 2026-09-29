#!/bin/bash
# End-to-end run through the whole app with the simulated F37 and simulated pulse, in a throwaway
# data folder (real data is never touched). Needs a build (Builds/macOS/Jogging.app). ~3–4 min.
# Exit code 0 = all checks passed; the log lists every check ("[E2E] OK/FAIL").
set -u
cd "$(dirname "$0")/.."
APP=Builds/macOS/Jogging.app/Contents/MacOS/Jogging
# With the screen locked (or the display off) macOS gives the app no frames: it would stand still at the
# first scene load. Say so instead of waiting for the hang watchdog.
if ioreg -n Root -d1 | grep -q '"CGSSessionScreenIsLocked"=Yes'; then
  echo "Bildschirm gesperrt – der Durchlauf braucht einen entsperrten Mac (die App bekommt sonst keine Bilder)"; exit 2
fi
DATA="$(mktemp -d -t jogging-e2e)"
LOG="$DATA/player.log"
"$APP" -e2e -datadir "$DATA" -beltsim -hrsim -timescale 20 -logFile "$LOG" "$@" &
PID=$!
last=0; still=0
for i in $(seq 1 1800); do
  kill -0 $PID 2>/dev/null || break
  # the log stands still for 90 s → the app hangs: record the stacks, then stop it
  sz=$(stat -f %z "$LOG" 2>/dev/null || echo 0)
  if [ "$sz" = "$last" ]; then still=$((still+1)); else still=0; last=$sz; fi
  if [ $still -ge 90 ]; then ioreg -n Root -d1 | grep -q '"CGSSessionScreenIsLocked"=Yes' && echo "(Bildschirm wurde währenddessen gesperrt)"; echo "Hänger – Stack-Aufnahme: $DATA/hang.txt"; sample $PID 5 -file "$DATA/hang.txt" >/dev/null 2>&1; kill -9 $PID; break; fi
  # result written → give the app 15 s to quit (the player sometimes hangs in its shutdown)
  if [ -f "$DATA/e2e-result.txt" ]; then for j in $(seq 1 15); do kill -0 $PID 2>/dev/null || break; sleep 1; done; kill $PID 2>/dev/null; sleep 3; kill -9 $PID 2>/dev/null; break; fi
  sleep 1
done
kill -0 $PID 2>/dev/null && { echo "Zeitüberschreitung – beende"; kill -9 $PID; }
wait $PID; rc=$?
grep -E "\[E2E\] (OK|FAIL|ERGEBNIS|Abbruch)" "$LOG"
[ -f "$DATA/e2e-result.txt" ] || { echo "kein Ergebnis (Exit $rc, Log: $LOG)"; exit 1; }
# The player's exit code isn't reliable on macOS — the result file decides.
# KEEP=1 keeps the log and data folder of a passed run too (e.g. for -diag output)
if head -1 "$DATA/e2e-result.txt" | grep -q "^OK"; then [ -n "${KEEP:-}" ] && echo "Log und Daten: $DATA" || rm -rf "$DATA"; exit 0; fi
echo "Log und Daten: $DATA"
exit 1
