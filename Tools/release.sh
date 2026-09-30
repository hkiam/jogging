#!/bin/bash
# A release on GitHub with ready-made apps (github.com/hkiam/jogging):
#   1. brings the public repository up to date (Tools/export-public.sh --push),
#   2. builds the Mac app, the Windows app and the Android APK from a neutral folder (/tmp/Jogging, a git worktree of HEAD) –
#      a build keeps its source paths for error messages, so the apps don't show this machine's user name,
#   3. checks both apps for this machine's user name, home folder and the local commit address/domain,
#   4. runs the end-to-end test on the release Mac app,
#   5. creates the GitHub release with both files and the notes from Tools/release-notes.md.
#   Tools/release.sh <version> [--draft]        e.g. Tools/release.sh 1.1.0
# Unity must not have this project open. Takes about 40–60 minutes (fresh import in the neutral folder).
set -eu
cd "$(dirname "$0")/.."
SRC="$(pwd)"
VERSION="${1:?Aufruf: Tools/release.sh <version> [--draft]  (z. B. 1.1.0)}"
DRAFT=""; [ "${2:-}" = "--draft" ] && DRAFT="--draft"
TAG="v$VERSION"
REPO="hkiam/jogging"
UNITY=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity
WT=/tmp/Jogging
OUT="$(mktemp -d -t jogging-release)"
LOGS="$OUT/logs"; mkdir -p "$LOGS"

if gh release view "$TAG" --repo "$REPO" >/dev/null 2>&1; then echo "$TAG gibt es schon auf GitHub"; exit 1; fi
if ioreg -n Root -d1 | grep -q '"CGSSessionScreenIsLocked"=Yes'; then echo "Bildschirm gesperrt – der Test braucht einen entsperrten Mac"; exit 1; fi

echo "== 1/5 Öffentliches Repository aktualisieren"
Tools/export-public.sh --push

echo "== 2/5 Bauen in $WT (neutraler Ordner)"
git worktree remove --force "$WT" 2>/dev/null || rm -rf "$WT"
git worktree add -q --detach "$WT" HEAD
trap 'git -C "$SRC" worktree remove --force "$WT" 2>/dev/null || true' EXIT
(cd "$WT" && "$UNITY" -batchmode -quit -projectPath . -buildTarget OSXUniversal -executeMethod Jogging.EditorTools.BuildTools.BuildMacBatch -logFile "$LOGS/mac.log") \
  || { echo "Mac-Build fehlgeschlagen: $LOGS/mac.log"; exit 1; }
(cd "$WT" && "$UNITY" -batchmode -quit -projectPath . -buildTarget StandaloneWindows64 -executeMethod Jogging.EditorTools.BuildTools.BuildWindowsBatch -logFile "$LOGS/windows.log") \
  || { echo "Windows-Build fehlgeschlagen: $LOGS/windows.log"; exit 1; }
[ -f "$WT/Builds/Windows/JoggingBleBridge.exe" ] || { echo "Windows-Bridge fehlt (.NET-SDK installiert?): $LOGS/windows.log"; exit 1; }
(cd "$WT" && "$UNITY" -batchmode -quit -projectPath . -buildTarget Android -executeMethod Jogging.EditorTools.MobileBuild.BuildAndroidBatch -logFile "$LOGS/android.log") \
  || { echo "Android-Build fehlgeschlagen: $LOGS/android.log"; exit 1; }
MIN_MAC="$(/usr/libexec/PlistBuddy -c "Print :LSMinimumSystemVersion" "$WT/Builds/macOS/Jogging.app/Contents/Info.plist" | cut -d. -f1)"

echo "== 3/5 Apps auf persönliche Spuren prüfen"
LOCAL_MAIL="$(git config user.email || true)"
PATTERNS=("$USER" "$HOME")
[ -n "$LOCAL_MAIL" ] && [ "$LOCAL_MAIL" != "maik.hofmann@gmail.com" ] && PATTERNS+=("$LOCAL_MAIL" "${LOCAL_MAIL#*@}")
APKX="$OUT/apk"; mkdir -p "$APKX"; unzip -q -o "$WT/Builds/Android/Jogging.apk" -d "$APKX"
bad=0
for p in "${PATTERNS[@]}"; do
  [ -z "$p" ] && continue
  # text in the apps (strings of 5+ characters: random bytes in images or meshes don't count)
  for f in $(find "$WT/Builds/macOS/Jogging.app" "$WT/Builds/Windows" "$APKX" -type f -size +0); do
    if strings -n 5 "$f" 2>/dev/null | grep -qiF -- "$p"; then echo "  „$p“ in ${f#$OUT/}"; bad=1; fi
  done
done
[ $bad = 0 ] || { echo "Persönliche Spuren gefunden – kein Release"; exit 1; }

echo "== 4/5 Gesamt-Durchlauf mit der Release-App"
(cd "$WT" && Tools/e2e.sh -lang en > "$LOGS/e2e.txt" 2>&1) || { grep -E "FAIL|ERGEBNIS" "$LOGS/e2e.txt"; echo "E2E fehlgeschlagen – kein Release ($LOGS/e2e.txt)"; exit 1; }
grep "ERGEBNIS" "$LOGS/e2e.txt"

echo "== 5/5 Release $TAG auf GitHub"
ditto -c -k --keepParent "$WT/Builds/macOS/Jogging.app" "$OUT/Jogging-macOS.zip"
cp "$WT/Builds/Android/Jogging.apk" "$OUT/Jogging-Android.apk"
(cd "$WT/Builds" && ditto -c -k --keepParent Windows "$OUT/Jogging-Windows.zip")
sed -e "s/{{MIN_MAC}}/$MIN_MAC/g" Tools/release-notes.md > "$OUT/notes.md"
if [ -f "docs/release-$VERSION.md" ]; then { printf '\n## New in %s\n\n' "$VERSION"; cat "docs/release-$VERSION.md"; } >> "$OUT/notes.md"; fi
gh release create "$TAG" "$OUT/Jogging-macOS.zip" "$OUT/Jogging-Windows.zip" "$OUT/Jogging-Android.apk" --repo "$REPO" --target main \
  --title "Jogging $VERSION" --notes-file "$OUT/notes.md" $DRAFT
echo "Fertig: https://github.com/$REPO/releases/tag/$TAG  (Protokolle: $LOGS)"
