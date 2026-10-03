#!/bin/bash
# The companion dog (optional): "German Shepherd 3D Dog Model" by RetroStyle Games, free on Fab
# (https://www.fab.com/listings/5ffcabde-3356-4d75-b98e-580825f15e47 – Fab Standard License, so it is not in the
# public repository). Download the FBX zip from Fab, then:
#   Tools/dogs/import-germanshepherd.sh ~/Downloads/rsg_dogspack_germanshepherd_fbx.zip
# and in Unity: Jogging → Build → Hunde aufbereiten (or -executeMethod Jogging.EditorTools.DogAssets.RunBatch).
set -eu
cd "$(dirname "$0")/../.."
ZIP="${1:?Aufruf: Tools/dogs/import-germanshepherd.sh <rsg_dogspack_germanshepherd_fbx.zip>}"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
unzip -q -o "$ZIP" -d "$TMP"
D=Assets/PhotoReal/Dogs/germanshepherd
mkdir -p "$D/Animations" "$D/Textures"
cp "$(find "$TMP" -name 'SK_GermanShepherd_01.fbx' | head -1)" "$D/germanshepherd.fbx"
find "$TMP" -path '*Animations*' -name '*.fbx' ! -name '*test*' | while read -r f; do
  n="$(basename "$f" .fbx | sed -e 's/^1 type_//' -e 's/_v01//' -e 's/ /_/g')"
  cp "$f" "$D/Animations/$n.fbx"
done
cp "$(dirname "$(find "$TMP" -name 'T_GermanShepherd_B.png' | head -1)")"/T_GermanShepherd_*.png "$D/Textures/"
echo "Fertig: $D"
