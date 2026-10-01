# Jogging – Entwicklerdoku (Deutsch)

> Die englische Projektvorstellung steht in der [README](../README.md).

## Überblick

Eine Lauf-App für das Laufband zu Hause: Man läuft auf dem echten Band und bewegt sich dabei durch eine
fotorealistisch erzeugte 3D-Landschaft. Die Strecke stellt die Steigung am Band ein, Workouts und
Trainingspläne geben das Training vor, und jede Person der Familie hat ihr eigenes Profil mit Logbuch,
Statistik und Erfolgen.

- **Bedienung für Läuferinnen und Läufer:** [docs/Bedienungsanleitung.md](Bedienungsanleitung.md)
- **Planung und Stand der Plattform:** [docs/Plattform-Plan.md](Plattform-Plan.md)
- **Streckengenerator:** [docs/Streckeneditor-Plan.md](Streckeneditor-Plan.md)

## Stand

| Bereich | Was es gibt |
|---|---|
| Welt | MapMagic-2-Terrain, geformter Laufweg nach Höhenprofil, Bäume/Unterholz, Seen, Bäche, Jahreszeiten, Tageszeit |
| Strecken | Parametrischer Generator (Länge, Anstiege, Vegetation …), `.jogroute` = Rezept + Seed, Werkstatt, Teilen per Code; Quick Run mit neuer Landschaft, „Strecke speichern“ |
| Läufer | Mehrere Läufer, Profil (Figur, Geburtsjahr, Gewicht, max. Puls, Wochenziele, Geist an/aus) |
| Lauf | Zustandsautomat (Countdown, Pause, Band gestoppt, Verbindung weg), Pausendialog, Ansagen, Geist-Läufer |
| Training | Workouts (Editor, `.jogworkout`), Trainingspläne über mehrere Wochen, Pulszonen |
| Laufband | Sportstech F37 (FitShow über BLE-Bridge), Standard-FTMS; eine Sicherheitsschicht für alle Befehle ans Band, Grenzen je Band (Profil); nur lesend: iConsole+, LifeSpan, KingSmith WalkingPad, Laufsensoren (RSC) |
| Puls | jeder Bluetooth-Pulsmesser (Heart Rate), Handsensoren am Band (FTMS, FitShow, Pulsdienst des Bands) |
| Pulsgurt | Standard-Herzfrequenzsensor über dieselbe Bridge, pro Läufer gekoppelt |
| Auswertung | Logbuch mit 1-Hz-Messpunkten, Statistik, 12-Wochen-Verlauf, 24 Erfolge, Wochenziele, TCX-Export |
| Daten | Absturzsicheres Speichern (`.bak`), Datensicherung als ZIP |

## Technik

- **Unity 6.3 LTS (6000.3.24f1)** + URP, Linear Colour, C#. *Nicht* 6.5/6.6 verwenden (bricht APIs).
- Eine Szene: `Assets/Scenes/PhotoRun.unity`. Menüs sind Seiten im `StartMenuUI` (partial class, eine Datei je Seite).
- Die generierten Teile der Szene (Terrain, Weg, Bäume, Licht …) baut `Editor/PhotoRunSceneBuilder.cs`;
  er verwendet vorhandene Objekte und Assets wieder, ein Build ändert also keine Dateien.
- Plattform-Logik ist reines C# und hat Selbsttests: Session (`Core/SessionStateMachine`), Bandsicherheit
  (`Locomotion/Treadmill/BeltSafety`), Workouts/Pläne/Puls (`Training/`), Statistik/Logbuch (`Profile/`).
- macOS hat in Unity kein Bluetooth: `Tools/MacBleBridge` ist eine kleine Swift-App (CoreBluetooth), die
  per UDP (localhost 47520/47521) mit der App spricht. Der Build bettet sie ein und installiert sie nach
  `~/Applications/JoggingBleScan.app` (einmal Bluetooth erlauben).

### Ordner

```
Assets/Scripts/
  Core/        Session-Zustände, Einstellungen, SafeFile, Datensicherung
  Locomotion/  Tastatur, Laufband (FitShow, FTMS, Bridge, Simulator, Sicherheitsschicht)
  Profile/     Läufer, Logbuch, Statistik, Erfolge, Wochenziele, Geist, TCX
  Route/       Streckengenerator, .jogroute, Teilen
  Training/    Workouts, Trainingspläne, Puls, Ansagetexte
  World/       Terrain, Weg, Vegetation, Mitläufer, Zuschauer
  UI/          HUD, Menüseiten, Ziel-Screen, Werkstatt
  Editor/      Szenenbauer, Build, Selbsttests
Tools/         MacBleBridge (Swift), check.sh, e2e.sh, qrcheck.swift
```

## Bauen, prüfen, starten

Unity darf das Projekt dabei nicht geöffnet haben.

```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath . -buildTarget OSXUniversal -executeMethod Jogging.EditorTools.BuildTools.BuildMacBatch -logFile build.log
```

Ergebnis: `Builds/macOS/Jogging.app`. Alle Selbsttests (11 Prüfungen plus QR-Code, gelesen mit der
Texterkennung von macOS; etwa 1 min):

```bash
Tools/check.sh
```

Gesamt-Durchlauf durch die gebaute App mit simuliertem Band und Puls, in einem Wegwerf-Datenordner
(die echten Daten bleiben unberührt; etwa 3–4 min): Läufer anlegen, Quick Run, Pause bei Bandstopp und
Verbindungsabbruch, Strecke speichern und mit Geist laufen, Trainingsplan-Einheit, Statistik,
Sicherung/Wiederherstellung, Hügel-Workout mit Zurückfahren der Steigung, Strecke per QR-Bild, Werkstatt
(Seen, Zuschauer), zwei Läufer mit eigenen Gurten, Workout stellt das Tempo (und Hand am Band hat Vorrang),
große Workout-Anzeige, TCX-Export. Im Test startet keine Bridge und nichts geht an Bluetooth.
Nur einen Teil: `Tools/e2e.sh -e2eonly hill` bzw. `extras`.

```bash
Tools/e2e.sh
```

Für den ersten Lauf am echten Band: [Laufband-Testplan](Laufband-Testplan.md).

### iPad und Android

Dieselbe App läuft auf dem iPad und auf Android-Tablets (Querformat, Touch). Ohne Laufband Tempo mit
den Knöpfen − / + unten links. Laufband und Pulsgurt über ein natives Bluetooth-Plugin, das genau die
Mac-Bridge nachbildet (`Assets/Plugins/iOS/JoggingBle.swift`, `Assets/Plugins/Android/JoggingBle.java`,
dieselben Nachrichten, dieselbe Sicherheitsschicht) – im Simulator/Emulator gibt es kein Bluetooth; am
echten F37 getestet mit dem Fire HD 10 (Verbinden, Steigung, Zurückfahren). Ansagen über die Systemstimme
(`JoggingSpeech.swift/.java`), QR-Bilder lesen nur am Mac. Grafik startet auf „mittel“; Geräte unter 4 GB
RAM (z. B. Fire HD 10) automatisch „minimal“ (halbe Texturen, kleineres Geländeraster, kleine
Grundtexturen) – dort gemessen 30 FPS bei 0,7 GB Speicher. Tablets zielen auf 60 FPS.

```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath . -buildTarget iOS -executeMethod Jogging.EditorTools.MobileBuild.BuildIosSimulatorBatch -logFile ios.log
```

```bash
Tools/ios-sim.sh
```

```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod Jogging.EditorTools.MobileBuild.BuildAndroidBatch -logFile android.log
```

```bash
Tools/android-emu.sh
```

`Tools/ios-sim.sh` baut das Xcode-Projekt für den iPad-Simulator und startet es; `Tools/android-emu.sh`
installiert die APK im Emulator „JoggingTablet“ (Android 15, Tablet) und startet sie.

**Echtes iPad** (per USB): einmalig Apple-ID in Xcode (Einstellungen → Accounts), Entwicklermodus am iPad,
im Xcode-Projekt `Builds/iOS-Device` unter Signing & Capabilities das Team wählen; nach der ersten
Installation am iPad dem Entwickler vertrauen (Einstellungen → Allgemein → VPN & Geräteverwaltung). Danach:

```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath . -buildTarget iOS -executeMethod Jogging.EditorTools.MobileBuild.BuildIosDeviceBatch -logFile ios-device.log
```

```bash
Tools/ios-device.sh <TEAM-ID>
```

`Tools/ios-device.sh` signiert (automatisch), installiert und startet die App auf dem angeschlossenen iPad.
Mit einer kostenlosen Apple-ID läuft die App 7 Tage, dann einfach neu installieren.

**Echtes Android-Tablet** (per USB, Entwickleroptionen/USB-Debugging an):
`adb -s <Seriennummer> install -r Builds/Android/Jogging.apk`. Sind Emulator und Tablet zugleich
angeschlossen, `ANDROID_SERIAL` setzen (auch für `Tools/e2e-mobile.sh android`).
Gesamt-Durchlauf im Simulator/Emulator: `Tools/e2e-mobile.sh ios` bzw. `Tools/e2e-mobile.sh android`.

### Windows

Die Windows-Version wird am Mac gebaut (Modul „Windows Build Support (Mono)“ im Unity Hub; IL2CPP für Windows
bräuchte einen Windows-Rechner):

```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath . -buildTarget StandaloneWindows64 -executeMethod Jogging.EditorTools.BuildTools.BuildWindowsBatch -logFile windows.log
```

Ergebnis: `Builds/Windows/` mit `Jogging.exe` und `JoggingBleBridge.exe`. Die Bridge (`Tools/WinBleBridge`,
C#/.NET 10, WinRT-Bluetooth) spricht dasselbe UDP-Protokoll wie die Mac-Bridge und liest zusätzlich die Ansagen
vor (`'V'+"de|Text"`); der Build baut sie mit `Tools/WinBleBridge/build.sh`, wenn ihr Quelltext neuer ist (braucht das
.NET-SDK; Pakete kommen nur von nuget.org, siehe `Tools/WinBleBridge/nuget.config`). **Noch nicht auf einem echten
Windows-Rechner getestet.**

### Startoptionen (für Tests)

| Option | Wirkung |
|---|---|
| `-beltsim` | simulierter F37 statt Bluetooth (Tasten: B Start/Stopp, ↑/↓ Tempo, Bild↑/↓ Steigung, S Clip, D Verbindung) |
| `-perf` / `-mem` | Leistung (CPU/GPU-Zeit, Draw Calls, Dreiecke) bzw. Speicher beim Laden ins Log |
| `-e2eonly perf` | gleichmäßiger Lauf mit 8 km/h zum Messen (mit `-perf`, ohne `-timescale`) |
| `-hrsim` | simulierter Puls (folgt Tempo und Steigung) |
| `-autostart` | Menü überspringen, sofort laufen |
| `-routefile <pfad>` / `-preset <name> [-seed n]` | bestimmte Strecke laufen |
| `-timescale <n>` | Lauf n-fach beschleunigt (Workouts/Pläne testen) |
| `-starts <m>` | mitten auf der Strecke beginnen |
| `-diag` | Diagnose im Log (`~/Library/Logs/DefaultCompany/Jogging/Player.log`), u. a. Bildrate alle 10 s |
| `-lang de\|en` | Sprache für diesen Start (sonst Einstellung bzw. Systemsprache); mit `-diag` erscheinen fehlende Übersetzungen im Log |
| `-units metric\|imperial` | Einheiten für diesen Start (km/h, km bzw. mph, Meilen) |
| `-timeofday <h>`, `-lightpollution <0..1>`, `-moonage <Tage>` | Himmel: Uhrzeit, Licht der Orte, Mondalter (0 Neumond … 14,8 Vollmond; sonst das heutige) |
| `-waysideshots <ordner> -beltsim -timescale 6` | Testbilder des Wegrands: läuft die Strecke und fotografiert je Objektart das erste Vorkommen |
| `-animaltest` | je ein Hase, Kaninchen, Fuchs und zwei Hirsche gleich am Start; mit `-skyshots` zusätzlich Nahaufnahmen `tier-*.png` |
| `-pageshots <ordner> [-pages stats,export,import,gpxeditor,profile,…]` | Bilder der Menüseiten (Standard: main, editor, settings, profile), danach beendet sich die App; `gpxeditor` öffnet die erste GPX-Datei aus Downloads im Editor |
| `-nowayside` | ohne Wegrand-Objekte (Vergleich der Bildrate) |
| `-skyshots <ordner>` | Testbilder des Himmels (15 Stimmungen × 2 Blickrichtungen), danach beendet sich die App |
| `-fps` | nur die Bildrate im Log (`[FPS] Ø … · 1% low …`) |
| `-graphics low\|medium\|high` | Grafikstufe für diesen Start |
| `-datadir <ordner>` | anderer Datenordner (Tests) |
| `-workout <id>` | mit diesem Workout starten (z. B. `builtin:intervalle-30`, mit `-autostart`) |
| `-nospare` / `-erosionref` | Vergleich: ohne Ersatzkacheln / mit MapMagics Original-Erosion |
| `-pace <km/h>` | Starttempo ohne Laufband (Tests) |
| `-hitch` | jedes Bild über 40 ms im Log, mit Ursache (MapMagic-Schritt, eigene Skripte, GC) |

Beispiel:

```bash
open Builds/macOS/Jogging.app --args -beltsim
```

### Daten

`~/Library/Application Support/DefaultCompany/Jogging/`: `profiles/`, `sessions/<läufer>/`, `routes/`,
`workouts/`, `settings.json`, `Sicherungen/`. Jede Datei hat eine `.bak`-Vorgängerversion.

## Start und Bildrate

Gelände bereit nach etwa 3–4 s ab App-Start (vorher 12–15 s); danach praktisch ohne Ruckler.
`-diag` zeigt den Startablauf (`[Startup] …`, langsamste Knoten des Geländegraphen), `-hitch` jedes Bild
über 40 ms mit Ursache (MapMagic-Schritt, Kachelraster, eigene Skripte, GC).

Was geholfen hat:
- **Erosion ohne Speicherverbrauch** (`Den.Tools.Erosion`): MapMagics C#-Ersatz für das native Plugin
  (unter IL2CPP aktiv) legte für jeden Punkt neue Arrays an – ~7 s pro Kachel und ständige
  Speicherbereinigung, die auch das Bild anhielt. Jetzt ~5× schneller und **bitgleich**
  (`ErosionCheck`; im Build mit `-erosionref` vergleichbar).
- Die Bluetooth-Bridge wird im Hintergrund gestartet (`open` hielt den ersten Frame bis zu 4 s auf).
- Terrain-Kacheln einmal pro Bild lesen (`TerrainGround.Tiles` statt `Terrain.activeTerrains` je Abfrage).
- Bäume mit Zeitbudget pro Bild setzen; Ersatzkacheln in ruhigen Bildern vorbauen (das Kachelraster
  verschiebt sich alle 250 m und baute dafür 9 Kacheln in einem Bild).
- Generator-Threads mit niedrigerer Priorität; beim Szenenwechsel kooperativ gestoppt statt
  `Thread.Abort` (das konnte unter IL2CPP den Speicher beschädigen).

Lokale Änderungen an MapMagic: `Tools/Erosion.cs`, `Tools/TileManager.cs`, `Tools/ThreadManager/*`,
`Terrains/TerrainTile(Manager).cs`, `Nodes/Graph.cs`/`Generator.cs` (Knotenzeiten) – jeweils mit „Jogging“ kommentiert.

## Veröffentlichen (GitHub)

- Code: `Tools/export-public.sh --push` – bringt die öffentliche Kopie (`../Jogging-public`, ohne Asset-Store-Pakete)
  auf den Stand und lädt einen Commit „Release <Datum>“ hoch.
- Release mit Apps: `Tools/release.sh 1.1.0` – Code hochladen, Mac-App und APK in einem neutralen Ordner bauen
  (`/tmp/Jogging`, damit keine eigenen Pfade in den Apps stehen), auf persönliche Spuren prüfen, E2E mit der
  Release-App, GitHub-Release mit `Tools/release-notes.md` (+ `docs/release-<version>.md` für die Neuerungen).
  Unity darf dabei nicht offen sein; Dauer etwa 40–60 min.

## App-Icon

Motiv: `Assets/Art/Icon/Source~/Vorlage.png`. `Tools/make-icon.py` erzeugt daraus das randlose Quadrat
(iPad, Android, Standard) und die Mac-Kachel; `Editor/AppIcon.cs` setzt sie bei jedem Build.

## Sicherheit am Laufband

Alle Befehle an das Band laufen durch `BeltSafety`: nur bei laufendem Band und laufendem Lauf, Steigung
0–12 % in 1-%-Schritten, Tempo nur wenn in den Einstellungen freigegeben (0,5 km/h pro Schritt, höchstens
16 km/h), nach einer Verstellung am Band hält sich die App 30 s zurück, das Band wird nie gestartet. Nach
dem Lauf fährt die App nur die Steigung zurück, die sie selbst gesetzt hat. Getestet in `BeltSafetyCheck`
und im geschlossenen Regelkreis mit dem F37-Simulator (`BeltEmulatorCheck`).
