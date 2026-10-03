#!/bin/bash
# The public, open-source copy of this project (default ../Jogging-public, published as github.com/hkiam/jogging):
# every tracked file except the Asset Store packages (MapMagic 2, Idyllic Fantasy Nature – their licence
# doesn't allow sharing, see THIRD_PARTY_NOTICES.md) and the seasonal tree textures recoloured from them
# (the build makes them again: Editor/SeasonBuilder).
#   - first time: a new git repository with one first commit
#   - afterwards: the copy is brought up to date (new, changed and removed files) and gets one commit
#     "Release <date>" on top of the published history; --push also uploads it
#   Tools/export-public.sh [--push] [target-folder] ["Name <email>"]
# Nothing of this repository's own history goes public.
set -eu
cd "$(dirname "$0")/.."
SRC="$(pwd)"
PUSH=0
if [ "${1:-}" = "--push" ]; then PUSH=1; shift; fi
DST="${1:-../Jogging-public}"
AUTHOR="${2:-Maik Hofmann <maik.hofmann@gmail.com>}"
NAME="${AUTHOR% <*}"; MAIL="${AUTHOR##*<}"; MAIL="${MAIL%>}"

if [ -n "$(git status --porcelain -- . ':!Jogging.slnx')" ]; then echo "Es gibt nicht eingecheckte Änderungen – erst committen"; exit 1; fi
if [ -e "$DST" ] && [ ! -d "$DST/.git" ]; then echo "$DST ist kein git-Repository – bitte löschen oder einen anderen Ordner angeben"; exit 1; fi
if [ -d "$DST/.git" ] && [ -n "$(git -C "$DST" status --porcelain)" ]; then echo "$DST hat eigene, nicht eingecheckte Änderungen – erst klären"; exit 1; fi

# 1. the files, freshly exported into a temporary folder
TMP="$(mktemp -d -t jogging-export)"
trap 'rm -rf "$TMP"' EXIT
# tracked files only (no Library/, Builds/, local data), without the Asset Store packages and local IDE files
git ls-files -z -- . \
  ':!Assets/MapMagic' ':!Assets/MapMagic.meta' \
  ':!Assets/Idyllic Fantasy Nature' ':!Assets/Idyllic Fantasy Nature.meta' \
  ':!Assets/PhotoReal/Dogs' ':!Assets/PhotoReal/Dogs.meta' \
  ':!Jogging.slnx' \
  ':!Assets/PhotoReal/Seasons/*.png' ':!Assets/PhotoReal/Seasons/*.png.meta' \
  | xargs -0 tar -cf - | (cd "$TMP" && tar -xf -)

# the public repo ignores the packages you import yourself
cat >> "$TMP/.gitignore" <<'EOF'

# Asset Store packages – import them yourself (docs/Setup.md); their licence doesn't allow sharing
/[Aa]ssets/MapMagic/
/[Aa]ssets/MapMagic.meta
/[Aa]ssets/Idyllic Fantasy Nature/
/[Aa]ssets/Idyllic Fantasy Nature.meta
/[Aa]ssets/PhotoReal/Dogs/
/[Aa]ssets/PhotoReal/Dogs.meta
# seasonal tree textures, recoloured from the Asset Store trees by the build (Editor/SeasonBuilder)
/[Aa]ssets/PhotoReal/Seasons/*.png
/[Aa]ssets/PhotoReal/Seasons/*.png.meta
EOF

# 2. a last look for things that shouldn't go public: this machine's home folder, and the e-mail address and
# its domain that this (private) repository commits with – read here, so they aren't written into the script
LOCAL_MAIL="$(git -C "$SRC" config user.email || true)"
PATTERNS=("$HOME")
if [ -n "$LOCAL_MAIL" ] && [ "$LOCAL_MAIL" != "$MAIL" ]; then PATTERNS+=("$LOCAL_MAIL" "${LOCAL_MAIL#*@}"); fi
for p in "${PATTERNS[@]}"; do
  if grep -rIliF -- "$p" "$TMP" >/dev/null 2>&1; then
    echo "Achtung – \"$p\" gefunden in:"; grep -rIliF -- "$p" "$TMP" | sed "s|$TMP/||"; exit 1
  fi
done

# 3. into the public repository
mkdir -p "$DST"
DST="$(cd "$DST" && pwd)"
NEW=0
if [ ! -d "$DST/.git" ]; then
  NEW=1
  git -C "$DST" init -q -b main
fi
git -C "$DST" config user.name "$NAME"; git -C "$DST" config user.email "$MAIL"
# the working tree becomes exactly the export (new, changed and removed files), .git stays
rsync -a --delete --exclude .git "$TMP/" "$DST/"
git -C "$DST" add -A
if git -C "$DST" diff --cached --quiet; then echo "Keine Änderungen seit der letzten Veröffentlichung."; exit 0; fi
STAT="$(git -C "$DST" diff --cached --shortstat)"
if [ $NEW = 1 ]; then MSG="Jogging – run through a photoreal world on your own treadmill"
else MSG="Release $(date +%Y-%m-%d)"; fi
GIT_AUTHOR_NAME="$NAME" GIT_AUTHOR_EMAIL="$MAIL" GIT_COMMITTER_NAME="$NAME" GIT_COMMITTER_EMAIL="$MAIL" \
  git -C "$DST" commit -q -m "$MSG"
echo "Fertig: $DST – „$MSG“ ($STAT)"
if [ $PUSH = 1 ]; then git -C "$DST" push -q && echo "Hochgeladen: $(git -C "$DST" remote get-url origin)"
else echo "Hochladen: Tools/export-public.sh --push (oder: git -C \"$DST\" push)"; fi
