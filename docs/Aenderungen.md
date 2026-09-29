# Änderungen: Branch `feature/photoreal-mapmagic` → `main`

## Noch nicht in `main`

(nichts)

## Zehnter Merge (29.09.2026) – Himmel, Wegrand, Wegbeläge, Nachtläufe, Veröffentlichung

- **Himmel neu** (eigener Shader statt des Unity-Standardhimmels): Wolken in vier Schichten mit eigener Höhe
  und Geschwindigkeit (Quellwolken, Bänke am Horizont, Cirren, geschlossene Decke bei Regen; bei „bewölkt“
  einzelne dunklere Regenwolken), Sonnenscheibe ohne Lens Flare, Sonnenstand nach Uhrzeit, Jahreszeit und
  50° N, die Uhr läuft während des Laufs weiter. Wolkenschatten wandern über das Land (Licht-Cookie aus denselben
  Wolkenformen, am Läufer deckungsgleich mit der Wolke vor der Sonne). Mond mit der echten Phase von heute,
  tagsüber blass. Nachtläufe (Tageszeit bis 23:30): Sterne nach Helligkeit, weniger in Ortsnähe
  (neuer Regler „Nachthimmel“), Milchstraße nur im Dunkeln ohne Mond, orange Lichtglocke über Orten.
  Dunst: Horizont in Nebelfarbe, Ferne leicht blau; Bodennebel in Senken und morgens über Wiesen.
  Testbilder: `-skyshots <ordner>`.
- **Wegrand** (World/Wayside, WaysideProps, WaysideBirds): je Strecke geplant (aus dem Seed), sparsam
  (auffällige Dinge ≥ 120 m auseinander, ~3 je km) und passend zur Umgebung – Seitenwege und Forstwege mit
  Fahrspuren, Wegweiser, Bänke, Infotafel mit der Karte der Strecke, Grenzsteine, Baummarkierungen, Nistkästen,
  Holzpolter, gefällte Stämme, Schonung, Hochsitze, Stromleitungen, Weidezäune, Heuballen, Feldscheune, Krähen,
  Vogelschwärme, Ortsrand (Laternen, Schild, Haltestelle, Hundekotbeutel, Gartenzaun, Auto), Landmarken (Wegkreuz,
  Kapelle, Ruine, Aussichtspunkt, Schutzhütte), am Weg Farne, Brennnesseln, Äste, Stümpfe, Wurzeln, Laub, Pilze,
  Ameisenhaufen, Wühlstellen, Pfützen und Matsch. Bäume und Gras halten Seitenwege frei. Holzbrücke über Bäche.
- **Wegbeläge** (World/TrailSurface, Shader Jogging/TrailSurface): Asphalt, Schotter, Erde, Nadelboden,
  Wiesenweg abschnittsweise nach der Umgebung, eigene Breite je Belag (2,5–3,4 m), Übergänge nach Texturrelief,
  ausgefranste Ränder (Asphalt gerade), Winter mit Schnee, Regen nass.
- **Assets**: Modelle und Texturen von Poly Haven (CC0) in Assets/PhotoReal/Wayside, aufbereitet mit
  Jogging → Build → Wegrand-Assets aufbereiten (Mesh-LODs, URP-Materialien, Varianten, Flecken, Stammdicken).
  Auf schwachen Tablets (Grafik „minimal“) ohne die schweren Scans direkt am Weg.
- **Veröffentlichung vorbereitet**: englische README mit Bildern (`docs/images`, aufgenommen mit `-skyown -skyshots`
  und `-pageshots`), MIT-Lizenz, `THIRD_PARTY_NOTICES.md`, `docs/Setup.md` (Asset-Store-Pakete, MapMagic-Änderungen),
  `Tools/export-public.sh` (öffentliche Kopie ohne Asset-Store-Pakete, frische Historie); die deutsche
  Entwicklerdoku steht jetzt in `docs/Entwicklung.md`.
- **Klang der Wege**: Schritte je Belag synthetisiert (Schotter, Asphalt, Erde, Nadelboden, Wiese, Schnee),
  Vögel nach Tageszeit (Morgenkonzert, nachts still), Grillen an Sommerabenden, Waldkauz nachts.
- **Nachtlicht**: Stirnlampe des Läufers, Laternen am Ortsrand gehen in der Dämmerung an.
- **Logbuch**: gesehene Landmarken und Meter je Wegbelag, auf dem Ziel-Screen und in der Laufansicht.
- **Werkstatt**: eigene Wegrand-Objekte setzen (Bank, Wegweiser, Hochsitz, Holzpolter, Wegkreuz, Schutzhütte,
  Kapelle, Heuballen, Infotafel, Feldscheune).
- **Quick Run** mit zufälliger Tageszeit, Vorlage **Mondnacht**.
- **Durchsicht** (Code-Review): Rundkurse – Wegrand und Bäume verrutschten je Runde (jetzt ganze Abschnitte je
  Runde); Speicher für Laufzeit-Meshes/-Materialien wird freigegeben; fehlgeschlagene Aufbauten versuchen es
  nicht mehr jedes Bild; Seitenwege mit passendem Belag; Textur-Genauigkeit auf langen Läufen; Rasterliste der
  Sperrflächen für Bäume; Grasprüfung merkt sich saubere Abschnitte je Kachel; Milchstraße ohne Naht.
- **Tests**: Grasprüfung und Rivalen-Prüfung nicht mehr zufällig (Gras: dichtere Proben; Rivale: nach deinem
  Überholen fällt er absichtlich zurück).
- **Fix Einheiten**: die Region des Systems wird jetzt beim System selbst erfragt (Mac/iPad: CoreFoundation,
  Android: `java.util.Locale`) – die .NET-Kultur des Players meldete auf einem deutschen Mac „en-US“, die App
  stellte dann auf mph.

## Neunter Merge (28.09.2026) – Einstellungen, Englisch, mph

- **„Einstellungen“ statt „Geräte“, auf dem Hauptdialog**: die App-weiten Einstellungen (Laufband, Pulsgurt,
  Ansagen, Geräusche, Anzeige, Grafik, Datensicherung) hängen nicht mehr am Läufer-Home, sondern an
  „Wer läuft?“ – der Hauptdialog erscheint jetzt immer beim Start, auch mit nur einem Läufer (vor dem ersten
  Läufer auf der Seite „Leg deinen ersten Läufer an“). Was zum Läufer gehört, steht im Profil: max. Puls und
  jetzt auch sein Pulsgurt („Anderer Gurt“).
- **Englisch und Deutsch**: alle Texte, Ansagen (englische Systemstimme) und Zahlen-/Datumsformate in beiden
  Sprachen; vorgewählt nach der Sprache des Systems, umschaltbar unter Einstellungen → Sprache.
  `-lang de|en` für Tests, `-diag` meldet Texte ohne Übersetzung („[Sprache] ohne Übersetzung: …“).
- **Einheiten km/h ↔ mph**: Einstellungen → Einheiten; vorgewählt nach der Region des Systems (imperial in
  USA, Liberia, Myanmar). Innen bleibt alles in km/h und Metern (Band, Logbuch, Strecken, Workouts); nur
  Anzeige, Eingaben (−/+ in 0,5 mph, Längen in 0,5 mi, Gewicht in lb) und Ansagen („Mile 3 …“, „miles per
  hour“) werden umgerechnet; die Kilometer-Ansage wird zur Meilen-Ansage. `-units metric|imperial` für Tests.

## Achter Merge (27.09.2026)

- **Läuft auf dem echten iPad** (iPad Pro 11" M4): Gerätebuild `MobileBuild.BuildIosDeviceBatch`
  (`Builds/iOS-Device`) und `Tools/ios-device.sh` (signieren, installieren, starten); Anleitung im README.

## Siebter Merge (27.09.2026)

- **Mac-Bridge mit Herzschlag**: die Mac-App meldet sich alle 5 s bei der Bridge; kommt 60 s nichts (App hart
  beendet oder abgestürzt), gibt die Bridge das Laufband frei und beendet sich. Vorher hielt eine liegen
  gebliebene Bridge den F37 verbunden – andere Geräte (das Fire-Tablet) fanden ihn dann nicht. Getestet: App
  hart beendet → Bridge nach 70 s weg; App läuft 98 s → Bridge bleibt; normales Beenden → Bridge sofort weg.

## Sechster Merge (27.09.2026) – aus dem ersten Test am echten F37 und am Fire HD 10

- **Steigung am F37 funktioniert jetzt**: die Bridge schreibt Befehle „ohne Antwort“ (wie FitShow-Apps) und
  fragt den Status erst 1,2 s danach wieder ab – vorher verwarf der F37 jeden Befehl stumm.
- **2-%-Schritte** werden erkannt und je Band gemerkt (einstellbar); reagiert ein Band gar nicht, pausiert die
  App 30 s statt endlos zu senden.
- **Anlaufen/Bremsen**: der F37 meldet das Zieltempo; der Lauf zieht jetzt mit 1 km/h pro s nach (je Band
  einstellbar) und läuft beim Stoppen mit dem Band aus. Der Simulator verhält sich jetzt ebenso.
- Rivale: „dein Tempo“ folgt beim Anlaufen schnell, Überholen in den ersten 20 s zählt nicht.
- Robuster: gespeicherter Steigungsbereich als Rückfall (auch nach Szenenwechsel), die zwei Bereichsabfragen
  nacheinander, Protokoll wird nachgefragt, alte Bridge beendet sich bei neuer Version und wird neu gestartet,
  Bridge wird nur bei Änderung neu gebaut (keine neue Bluetooth-Freigabe).
- Seite „Laufband wählen & einstellen“ neu gedacht (Dein Laufband oben mit Verbinden/Trennen, Suche startet
  selbst, eigenes Band immer in der Liste), „Kein Pulsgurt in der Nähe“ statt endlosem „Suche …“.
- `-diag`: gesendete Befehle, alle Antworten des Bands, Dienste des Bands, `[Band-Weg]` (Band gegen App).
- **Gehen statt Gleiten**: echte Geh-Animation (Rocketbox `walk_neutral_01`) bis ~7,5 km/h, darüber Laufen;
  beide Zyklen laufen passend zum Tempo schneller oder langsamer – bei 1 km/h ein langsamer Schritt statt der
  Stehpose, die übers Band rutschte. (Zwei verschiedene Geh-Clips zu mischen löschte die Beinbewegung aus.)
- Sicherheitsclip: der Lauf stoppt sofort (kein kurzes „Weiter geht's“ mehr), Auslaufen nur nach normalem Stopp.
- **Fire HD 10 läuft jetzt flüssig und stabil** (gemessen am Gerät, gleichmäßiger Lauf mit 8 km/h):
  30 FPS, 1 % low 29, keine langsamen Bilder (vorher 20 FPS, jedes Bild langsam); Speicher-Spitze 0,7 GB
  statt 1,7 GB (vorher beendete Android die App beim Laufstart). Ursachen und Lösungen:
  - Die Grafikstufe galt nur für den Startbildschirm: jeder Laufstart lädt die Szene neu, danach fehlten
    Kachelbereich, kleine Grundtexturen, Gras- und Geländeeinstellungen (81 statt 49 grobe, 25 statt 9 feine
    Kacheln). Jetzt bei jedem Laden der Szene – betrifft auch „niedrig“/„mittel“ auf iPad und Mac.
  - MapMagic legte jede neue Kachel mit großen Vorgaben an (1024er Grundtextur, große Höhen-/Mischkarten),
    ~7 MB Grafikspeicher je Kachel für einen Moment – jetzt klein, bevor sie an die Kachel kommt.
  - Grundtexturen der Kacheln auf schwachen Geräten 256² statt 1024².
  - Beim Neuladen wird die alte Landschaft erst freigegeben (vorher kurz zwei Landschaften im Speicher).
  - Ziel-Bildrate auf Tablets 60 statt 30: mit 30 rastete die Bildtaktung auf 20 FPS ein.
  - Test-Schalter für solche Messungen: `-mem`, `-perf`, `-e2eonly perf|walk`, `-nodetail`, `-notrees`,
    `-nofigures`, `-range`, `-far`, `-pe`, `-bmd`, `-rs`, `-tfr`.
- Test am Fire HD 10 mit dem F37 über das eigene Bluetooth des Tablets: verbinden, Steigung folgt der Strecke,
  nach dem Lauf zurück auf 0 %. Der F37 nimmt auch 1-%-Schritte (die „2-%-Schritte“ am Mac waren ein Fehlschluss
  aus der Zeit, als die Bridge Befehle verlor).
- Kalibrierlauf am F37: Anlaufen/Bremsen mit 1 km/h pro s passt (Band und App liegen nach dem Start 4–5 m
  auseinander, danach gleichauf); B6 (Clip) bestanden.

## Fünfter Merge (26.09.2026)

- **Laufband wählen & einstellen** (Geräte): Gerätesuche mit Liste (was jedes Gerät ist und ob die App es
  kann), und je Laufband ein Profil: gemeldeter Bereich (FitShow SYS_INFO, neu FTMS 2AD4/2AD5) plus eigene
  Grenzen – Steigung bis (auch über 12 % für Steigungstrainer), Gefälle (nur wenn das Band es kann; Strecken
  dann auch bergab, nach dem Lauf zurück auf 0 %), Tempo bis. Ersetzt „Anderes Laufband“ und die festen 0–12 %.
- **Weitere Protokolle, nur lesend**: iConsole+ (Reebok, Taurus, Finnlo, FlowFitness, Maxxus 9.1) und LifeSpan;
  eHealth, SmartTreadmill, Pafers werden erkannt und als „nicht unterstützt“ gezeigt, nie verbunden.
- Kein Gras mehr auf dem Weg (sofort nach dem Laden einer Kachel geräumt, 3 m breit).
- Selbsttests: Bandsicherheit mit Gefälle/eigenen Grenzen, iConsole (auch zerteilt), LifeSpan. E2E: Profil des
  simulierten F37, Seite „Laufband wählen & einstellen“, „kein Gras auf dem Weg“.

## Vierter Merge (26.09.2026)

- **Handsensoren am Laufband**: Puls aus den Griffen – FTMS (Feld im Datenstrom oder eigener Pulsdienst des
  Bands) und FitShow (Byte 13 der Statusmeldung, am F37 noch zu bestätigen: Testplan E). Ein Pulsgurt hat
  immer Vorrang; im HUD „(Hand)“.
- **Weitere Geräte, nur lesend**: KingSmith WalkingPad (ältere Modelle, eigenes Protokoll) und Laufsensoren am
  Schuh (RSC: Stryd, Polar Stride …) – damit geht auch ein Band ohne Bluetooth. Die App sendet ihnen nichts
  außer der Statusabfrage; Steuerbefehle gibt es weiter nur für FitShow und FTMS.
- **FTMS vollständig gelesen** (alle Felder bis zum Puls), Pulsgurt: „kein Hautkontakt – Gurt anfeuchten“.
- Selbsttest „Laufband-Protokolle“ (13 Prüfungen gesamt), `-diag` schreibt alle 5 s die Rohdaten des Bands.
  Mac, iPad, Android: 105/105.

## Dritter Merge (26.09.2026)

- **Leistung schwacher Tablets** (Grafik „minimal“, gemessen im Emulator mit dem Profil des Fire HD 10):
  Dreiecke je Bild ~400k → ~265k, Draw Calls ~290 → ~245, GPU-Zeit −35 %.
  - Bäume, Büsche, Steine und Figuren bekommen beim Import automatische Detailstufen (Unity Mesh LOD,
    `Editor/MeshLods.cs`) – vorher wurde jede Kiefer auch in 200 m Entfernung voll gezeichnet. Wirkt auf
    allen Stufen, am stärksten bei „minimal“.
  - „minimal“: 5 statt 8 Mitläufer, 3 statt 6 Zuschauer, vereinfachte Gelände-Textur ab 50 m.
  - `-perf`: alle 10 s CPU-/GPU-Zeit, Draw Calls, Dreiecke und die schwersten Modelle ins Log.
- Am Fire HD 10 selbst noch nicht gemessen (dort vorher konstant 20 FPS). Mac, iPad, Android: 105/105.

## Zweiter Merge (26.09.2026)

- **Zuschauer** tragen nie deine Figur (wie die Mitläufer).
- **Bluetooth nach Review** (iPad, Android, Mac-Bridge): Wiederverbinden nach Bluetooth aus/an, neue
  Suche, wenn ein anderes Gerät gewünscht ist, Android-Suche alle 5 min neu (sonst nach 30 min stumm),
  Hinweis „Standort ist aus“ auf Android 9–11 (Fire OS 8), FTMS-Steuerpunkt mit Indikationen, das Band
  wird nie als Pulsgurt genommen, veraltete Bandbefehle (> 1 s) werden verworfen. Weiterhin sendet
  die App von sich aus nichts ans Band außer der FitShow-Statusabfrage.
- **Wach bleiben am Mac** über eine Energie-Zusicherung (IOKit) statt `caffeinate`.
- **Tablet-Bedienung**: Zurück-Taste (Android) wirkt wie „Zurück“ im Menü und als Pause im Lauf;
  Ohne-Laufband-Tempo startet bei 8 statt 12,6 km/h; „Ordner öffnen“ und „QR-Bild lesen“ nur am Mac;
  Dateien heißen auf dem Tablet „Austausch“ statt „Downloads“; Geist per Antippen aus; Profil- und
  Geräte-Seite ohne Überlappungen.

## Merge davor (26.09.2026)

- **App-Icon** für macOS, iPad und Android (nach der Vorlage, `Tools/make-icon.py`).
- **Kein Damm mehr**: der Weg liegt auf der Höhe des Geländes links und rechts (Rest 1–3 m); Ursache
  war der GPU-Höhenweg in MapMagic, der den Höhenmaßstab verlor.
- **Bluetooth auf iPad und Android** (natives Plugin, gleiche Nachrichten und Sicherheitsschicht wie
  die Mac-Bridge); schwache Tablets (unter 4 GB, z. B. Fire HD 10) mit Grafik „minimal“.
- **Mitläufer im eigenen Tempo**: das Feld richtet sich nach deinem Schnitt, Gummiband, der Rivale
  bleibt gleichauf; im Workout ziehen sie zum Zieltempo.
- **Ansagen am Tablet** über die Systemstimme (iPad: AVSpeechSynthesizer, Android: TextToSpeech).
- **Geist durchscheinend** und bläulich; **kein Mitläufer trägt deine Figur**.
- **Kein Ruhezustand**, solange die App läuft (Mac: `caffeinate`, Tablets: Bildschirm bleibt an).
- Gesamt-Durchlauf: 104 Prüfungen am Mac, 105 im iPad-Simulator und Android-Emulator (mit Ansage).

## Stand beim ersten Merge

58 Commits seit `4878676` (Linear-Farbraum). `main` hat seitdem nichts Neues – der Merge ist ein
Fast-Forward ohne Konflikte. Die große Dateizahl kommt fast nur von Asset-Paketen: die alte
Cartoon-Welt und UMA sind entfernt, MapMagic 2 und die Rocketbox-Figuren sind neu.

## Welt und Strecken

- Fotorealistische Landschaft mit MapMagic 2: echtes Gelände, Relief, Bodentexturen je Vegetation,
  Seen und Bäche, vier Jahreszeiten, Wetter (klar, bewölkt, Regen, Schnee) und Umgebungsgeräusche.
- Realistische Figuren: Mitläufer, Rivale, Zuschauer; der eigene Läufer frei wählbar.
- Streckengenerator (Rezept + Seed, deterministisch): Länge, Rundkurs oder A → B, Anstiege,
  Welligkeit, max. Steigung, Vegetation, Wasser, Tageszeit, Dunst. Neun Vorlagen, darunter neu
  Seerunde, Bergpass, Wellen und Winterwald.
- Werkstatt: Bäume, Zuschauer, Seen und Bäche entlang der Strecke setzen.
- Strecken teilen ohne Server: Code, Datei, QR-Code – und QR-Bilder wieder einlesen.
- Quick Run: jedes Mal eine neue Landschaft, als Strecke speicherbar; Geist-Läufer auf gespeicherten
  Strecken (abschaltbar).

## Läufer, Training, Statistik

- Mehrere Läufer mit eigenem Home, Logbuch (jeder Lauf mit Messpunkten), Profil, Wochenzielen.
- Workouts (Intervalle, Hügel, Tempo, nach Puls …) auf jeder Strecke, eigener Editor;
  Trainingspläne über mehrere Wochen.
- Pulsgurt je Läufer (fremder Gurt wird nicht aufgezeichnet), Zonen im HUD und im Logbuch.
- Statistik, Bestwerte, 24 Erfolge, Verlauf über 12 Wochen, Familien-Rangliste und -Challenge.
- TCX-Export für Strava/Garmin, Datensicherung und Wiederherstellung.
- Ansagen, große Laufband-Anzeige (H) mit Workout-Streifen, Grafikstufe hoch/mittel/niedrig.

## Laufband (Sportstech F37)

- Bluetooth über eine eigene Swift-Bridge (FitShow und FTMS), Band und Gurt werden gemerkt.
- Lauf folgt dem Band (Start, Pause bei Stopp, Sicherheitsclip, Verbindungsabbruch).
- Steigung folgt der Strecke oder dem Workout – alles über eine Sicherheitsschicht: nur bei laufendem
  Band und laufendem Lauf, 0–12 % in 1-%-Schritten, nach Handverstellung 30 s Zurückhaltung, das
  Band startet nie von selbst, nach dem Lauf nur die selbst gesetzte Steigung zurück.
- Tempo vom Workout nur, wenn in den Einstellungen freigegeben (0,5 km/h je Schritt, höchstens 16 km/h).
- Testplan für das echte Band: `docs/Laufband-Testplan.md`.

## Leistung

- Gelände nach etwa 3,5 s bereit (anfangs ~17 s), danach praktisch ohne Ruckler: Erosion ohne
  Speicherverbrauch (bitgleich), Bridge-Start im Hintergrund, Kachel-Cache pro Bild, Bäume und
  Ersatzkacheln mit Zeitbudget, kooperatives Stoppen der Generator-Threads.
- Lokale Änderungen an MapMagic sind mit „Jogging“ kommentiert (Liste im README).

## Prüfungen

- `Tools/check.sh`: 12 Selbsttests (Generator, Strecken teilen, Logbuch, Session, Bandsicherheit,
  Band-Simulator, Workouts, Pläne, Puls, Statistik, Datensicherung, Erosion bitgleich) plus QR-Codes
  über macOS Vision.
- `Tools/e2e.sh`: Gesamt-Durchlauf der gebauten App mit simuliertem Band und Puls in einem
  Wegwerf-Datenordner, heute 104 Prüfungen, etwa 3 Minuten (mit Hänger-Wächter).
- Code-Review (Bandsicherheit, Daten, MapMagic) vom 25.09.2026: alle Befunde umgesetzt, u. a. kein
  Befehl aus veralteten Statusmeldungen, Zwischenspeicher während des Laufs, sichere Wiederherstellung,
  geprüfte Strecken-Ids.

## Noch offen

- Hardware-Test am echten F37 mit Pulsgurt (Testplan) – am Mac und am Tablet.
- Tippen im iPad-Simulator: die Tipps des Simulator-Werkzeugs kommen in der App nicht an (Android
  per `adb` geht); am echten iPad oder mit der Maus im Simulator-Fenster prüfen.
- Die „Hänger“ des Gesamt-Durchlaufs direkt nach dem Start kamen von einem gesperrten Mac (die App
  bekommt dann keine Bilder); `Tools/e2e.sh` meldet das jetzt gleich.
