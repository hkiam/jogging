#!/bin/bash
# Preview pictures for the route collection (routes/<name>.jpg): starts the built Mac app once per
# routes/*.jogroute in the route's own mood (-skyown) and keeps the view along the trail. Needs a build
# (Builds/macOS/Jogging.app) and an unlocked Mac. ~45 s per route.
set -eu
cd "$(dirname "$0")/.."
APP=Builds/macOS/Jogging.app/Contents/MacOS/Jogging
for f in routes/*.jogroute; do
  n="$(basename "$f" .jogroute)"
  T="$(mktemp -d -t jogging-preview)"
  "$APP" -datadir "$T/data" -lang en -graphics high -routefile "$(pwd)/$f" -skyown -skyshots "$T/shots" \
    -logFile "$T/player.log" -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 & P=$!
  for i in $(seq 1 120); do kill -0 $P 2>/dev/null || break; grep -aq "SkyShots\] fertig" "$T/player.log" 2>/dev/null && { sleep 3; break; }; sleep 1; done
  kill -9 $P 2>/dev/null || true
  if [ -f "$T/shots/route-a.png" ]; then
    python3 -c "from PIL import Image; im=Image.open('$T/shots/route-a.png').convert('RGB'); w=1200; im.resize((w,int(im.height*w/im.width)), Image.LANCZOS).save('routes/$n.jpg', quality=82, optimize=True, progressive=True)"
    echo "$n: ok"
  else echo "$n: kein Bild ($T/player.log)"; fi
  rm -rf "$T"
done
