Ready-to-run builds, no Unity needed.

## Downloads

| File | For |
|---|---|
| **Jogging-macOS.zip** | macOS {{MIN_MAC}}+ (Apple silicon and Intel) |
| **Jogging-Windows.zip** | Windows 10/11, 64-bit – Bluetooth (treadmill, heart-rate strap) is **experimental and untested** |
| **Jogging-Android.apk** | Android tablets (landscape), Android 8+ |

The iPad version needs to be built and signed yourself (see [docs/Entwicklung.md](https://github.com/hkiam/jogging/blob/main/docs/Entwicklung.md)).

### macOS: first start

The app isn't notarized by Apple (a hobby project without a paid developer account), so macOS will refuse
to open it at first. Unzip it, move `Jogging.app` to *Applications*, then **right-click → Open → Open**.
If macOS says the app is damaged, run once in Terminal:

```bash
xattr -dr com.apple.quarantine /Applications/Jogging.app
```

On the first run the app installs a small helper for Bluetooth (`JoggingBleScan.app`); allow Bluetooth when asked.

### Windows: first start

Unzip the folder anywhere and start `Jogging.exe`. Windows SmartScreen may warn about an unknown app (it isn't
signed): *More info → Run anyway*. `JoggingBleBridge.exe` next to it handles Bluetooth and the spoken
announcements; allow it when Windows asks. The Windows Bluetooth helper hasn't been tested on a real treadmill
yet – feedback welcome in the issues.

### Android: install

Allow installing apps from unknown sources for your browser or file manager, open the APK, install.
Allow "Nearby devices" (or "Location" on older Android) so the app can find the treadmill and heart-rate strap.

## Try it without a treadmill

Run with the keyboard (↑/↓) on the Mac or the − / + buttons on the tablet. On the Mac you can also start a
simulated treadmill:

```bash
open /Applications/Jogging.app --args -beltsim
```

## What's in it

Endless generated worlds with a real sky (sun, moon phase, clouds and their shadows, stars), changing trail
surfaces and a believable wayside, fellow runners and a ghost of your best run, workouts and training
plans, heart rate, family profiles with logbook and achievements, route editor and workshop, sounds of the
way, night runs with a head torch – in English and German, km/h or mph. See the [README](https://github.com/hkiam/jogging#readme).

⚠️ The app can change your treadmill's incline (and, if you switch it on, its speed). Use it at your own
risk, always wear the safety clip.

Built with MapMagic 2 and Idyllic Fantasy Nature from the Unity Asset Store (included in the app as the
Asset Store licence allows), Microsoft Rocketbox (MIT) and Poly Haven (CC0) – see
[THIRD_PARTY_NOTICES.md](https://github.com/hkiam/jogging/blob/main/THIRD_PARTY_NOTICES.md).
