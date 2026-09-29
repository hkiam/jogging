#!/bin/bash
# Builds the public, open-source copy of this project in a separate folder (default ../Jogging-public):
# every tracked file except the Asset Store packages (MapMagic 2, Idyllic Fantasy Nature – their licence
# doesn't allow sharing, see THIRD_PARTY_NOTICES.md) and the seasonal tree textures recoloured from them
# (the build makes them again: Editor/SeasonBuilder), a fresh git repository with one first commit.
# Nothing is pushed; this repository stays as it is.
#   Tools/export-public.sh [target-folder] ["Name <email>"]
set -eu
cd "$(dirname "$0")/.."
SRC="$(pwd)"
DST="${1:-../Jogging-public}"
AUTHOR="${2:-Maik Hofmann <maik.hofmann@gmail.com>}"

if [ -e "$DST" ]; then echo "$DST gibt es schon – bitte löschen oder einen anderen Ordner angeben"; exit 1; fi
if [ -n "$(git status --porcelain -- . ':!Jogging.slnx')" ]; then echo "Es gibt nicht eingecheckte Änderungen – erst committen"; exit 1; fi

mkdir -p "$DST"
DST="$(cd "$DST" && pwd)"
# tracked files only (no Library/, Builds/, local data), without the Asset Store packages and local IDE files
git ls-files -z -- . \
  ':!Assets/MapMagic' ':!Assets/MapMagic.meta' \
  ':!Assets/Idyllic Fantasy Nature' ':!Assets/Idyllic Fantasy Nature.meta' \
  ':!Jogging.slnx' \
  ':!Assets/PhotoReal/Seasons/*.png' ':!Assets/PhotoReal/Seasons/*.png.meta' \
  | (cd "$SRC" && xargs -0 tar -cf -) | (cd "$DST" && tar -xf -)

# the public repo ignores the packages you import yourself
cat >> "$DST/.gitignore" <<'EOF'

# Asset Store packages – import them yourself (docs/Setup.md); their licence doesn't allow sharing
/[Aa]ssets/MapMagic/
/[Aa]ssets/MapMagic.meta
/[Aa]ssets/Idyllic Fantasy Nature/
/[Aa]ssets/Idyllic Fantasy Nature.meta
# seasonal tree textures, recoloured from the Asset Store trees by the build (Editor/SeasonBuilder)
/[Aa]ssets/PhotoReal/Seasons/*.png
/[Aa]ssets/PhotoReal/Seasons/*.png.meta
EOF

# a last look for things that shouldn't go public: this machine's home folder, and the e-mail address and its
# domain that this (private) repository commits with – read here, so they aren't written into the script
LOCAL_MAIL="$(git -C "$SRC" config user.email || true)"
PATTERNS=("$HOME")
if [ -n "$LOCAL_MAIL" ] && [ "$LOCAL_MAIL" != "${AUTHOR##*<}" ] && [ "$LOCAL_MAIL>" != "${AUTHOR##*<}" ]; then
  PATTERNS+=("$LOCAL_MAIL" "${LOCAL_MAIL#*@}")
fi
for p in "${PATTERNS[@]}"; do
  if grep -rIliF -- "$p" "$DST" --exclude-dir=.git >/dev/null 2>&1; then
    echo "Achtung – \"$p\" gefunden in:"; grep -rIliF -- "$p" "$DST" --exclude-dir=.git; exit 1
  fi
done

cd "$DST"
git init -q -b main
git add -A
NAME="${AUTHOR% <*}"; MAIL="${AUTHOR##*<}"; MAIL="${MAIL%>}"
GIT_AUTHOR_NAME="$NAME" GIT_AUTHOR_EMAIL="$MAIL" GIT_COMMITTER_NAME="$NAME" GIT_COMMITTER_EMAIL="$MAIL" \
  git -c user.name="$NAME" -c user.email="$MAIL" commit -q -m "Jogging – run through a photoreal world on your own treadmill"
git config user.name "$NAME"; git config user.email "$MAIL"
echo "Fertig: $DST ($(git ls-files | wc -l | tr -d ' ') Dateien, $(du -sh . | cut -f1))"
echo "Veröffentlichen (öffentliches Repo auf GitHub anlegen und hochladen):"
echo "  cd \"$DST\" && gh repo create jogging --public --source . --push --description \"Run through a photoreal world on your own treadmill (Unity 6, macOS/iPad/Android, Bluetooth FTMS/FitShow)\""
