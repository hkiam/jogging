# Lauf-Plattform: Läufer, Logbuch, Training — Planung

Stand: 24.09.2026 · Status: **Phase 1–6 umgesetzt**; offen sind nur noch die Tests mit echter Hardware (siehe jeweilige Stand-Abschnitte)

## 1. Zielbild

Aus dem Laufband-Prototyp wird eine kleine Lauf-Plattform für die Familie. Die Welt bleibt das Erlebnis. Darum herum entstehen:

- **Läufer:** jede Person hat ein eigenes Profil mit Figur, Statistik, Erfolgen und Bestzeiten.
- **Logbuch:** jeder Lauf wird gespeichert, mit Messpunkten.
- **Training:** Workouts beschreiben, *wie* ich laufe. Sie lassen sich mit jeder Strecke kombinieren, die festlegt, *wo* ich laufe.
- **Steuerung:** Es ist klar geregelt, ob das Band die Welt steuert oder die App das Band. Die Sicherheit geht immer vor.

Der Kern bleibt kurz: **App starten → Läufer wählen → Quick Run → laufen.**

Ideengeber war ein extern erstelltes Konzept (angelehnt an MyHomeFIT, Zwift, Kinomap und iFIT). Es kannte den Code nicht, deshalb wurde es gegen den Ist-Stand geprüft (Abschnitt 2).

## 2. Bewertung des Konzepts

| Idee | Urteil | Begründung |
|---|---|---|
| Profil statt Avatar, Familien-Auswahl, Wochen-Krone | übernommen | Figur bleibt Eigenschaft des Profils (`figureModel`) |
| Quick Run als Hauptaktion | übernommen | das bisherige „Freie Laufen“ |
| Ziel-Screen → zurück zum Läufer-Home | übernommen | „Fertig“ statt „Schließen“ |
| Lauf-Historie mit Messpunkten | übernommen, **JSON statt SQLite** | 1 Hz × 1 h = 3 600 Punkte; ein Index je Läufer hält Menüs schnell; kein natives Plugin |
| Route ≠ Workout, Seed speichern | **schon vorhanden** | `.jogroute` = Rezept + Seed, Werkstatt, Teilen, Bestzeiten ([Streckeneditor-Plan](Streckeneditor-Plan.md)) |
| Session-Zustandsautomat | übernommen (Phase 2) | ersetzt `RunStats.Paused`, `track.Hold/IsFinished` und die Logik in `RunFollowsBelt` |
| Geräte in den Einstellungen, automatisch wieder verbinden | übernommen (Phase 2) | im HUD nur noch der Status |
| ControlStrategy + Safety-Schicht | übernommen, **an FitShow angepasst** | FitShow setzt die Steigung nur *zusammen* mit dem Tempo. Die Regeln aus `FitShowInclineCoupling` werden die Safety-Schicht und nicht neu erfunden |
| App steuert das Tempo (Workout, adaptiv nach Puls) | **angepasst** | erst Tempo-*Hinweise* im HUD; echte Tempo-Steuerung nur opt-in, mit Rampen; das Band wird nie gestartet |
| Datengetriebene Workouts | übernommen (Phase 4) | `.jogworkout`-JSON analog zu `.jogroute` |
| Herzfrequenz als eigener Sensor | übernommen (Phase 5) | BLE-HR über die vorhandene Swift-Bridge |
| Eigene Splash-Szene | verworfen | Die Welt hinter dem Menü ist der Splash: Sie wird schon gebaut, während man den Läufer wählt (seit Phase 2) |
| Szenen Bootstrap/MainUI/RunningWorld | verworfen | Das Terrain wird pro Strecke beim Laden geformt; eine Szene mit Reload funktioniert |
| Avatar-Editor (Haut, Haare, Kleidung) | verworfen | Rocketbox-Figuren sind fest; die Auswahl der Figur reicht |
| Schichtenarchitektur, EventBus | angepasst | reine C#-Klassen für Statistik, Zustände und Regelung; typisierte C#-Events, kein Framework |
| Accounts, Cloud, Multiplayer | verworfen | lokal und offline |

## 3. Datenmodell

Alles liegt unter `Application.persistentDataPath` (macOS: `~/Library/Application Support/DefaultCompany/Jogging`):

```
profiles/<runnerId>.json        ProfileData: id, created, playerName, figureModel,
                                Summen (Läufe, Distanz, Zeit, Höhenmeter, bester Lauf),
                                Erfolge, Bestzeiten je Strecke/Revision
sessions/<runnerId>/index.json  Zusammenfassungen aller Läufe (für Menüs und Statistik)
sessions/<runnerId>/<start>.json SessionRecord: summary + samples[] (t, distM, kmh, incline)
settings.json                   zuletzt aktiver Läufer
routes/*.jogroute               Strecken (unverändert, für alle Läufer gemeinsam)
profile.json.migrated           altes Einzelprofil, beim ersten Start übernommen
```

Ein `SessionSummary` enthält: Start und Ende (UTC), Strecke (Id, Revision, Name), Distanz, Zeit, Höhenmeter, Ø- und Max-Tempo, ob die Strecke vollständig gelaufen wurde, und die Quelle (`belt` oder `keys`). Läufe unter 1 m oder 30 s werden nicht gespeichert.

## 4. Phasen mit Abnahmekriterien

| Phase | Inhalt | Abnahme |
|---|---|---|
| **1 Läufer & Logbuch** | mehrere Läufer, Übernahme des alten Profils, Logbuch mit Messpunkten, Seiten „Wer läuft?“, „Neuer Läufer“ und Läufer-Home mit Quick Run, Ziel-Screen → Home | Läufer anlegen und wechseln; Lauf erscheint unter „Letzter Lauf“ und „Diese Woche“; Datei mit Messpunkten vorhanden |
| 2 Session & Geräte | Zustandsautomat (Vorbereitung, Countdown, Läuft, Pause, Ende, Abbruch, Verbindung weg); Pause-Dialog mit Weiter/Beenden/Verwerfen; Laufband in den Einstellungen, automatisch wieder verbinden; Welt hinter dem Menü sichtbar | ein Lauf lässt sich über das Band *und* per Tastatur pausieren und beenden; nach einem Neustart verbindet sich das Band ohne Klick |
| 3 Steuerungshoheit | `ControlMode`: Band / Strecke (Steigung) / Workout. `FitShowInclineCoupling` und FTMS-`TreadmillController` laufen hinter einer Safety-Schicht (Grenzen, Rampen, Stopp hat Vorrang, manuelles Übersteuern) | Tests der Safety-Regeln (Batch-Check); Band und Welt verhalten sich wie bisher |
| 4 Workouts | Segmente (Zeit/Distanz, Ziel-Tempo/-Steigung, Aufwärmen/Auslaufen), mit jeder Strecke kombinierbar; Tempo zuerst als Hinweis | „30 min Intervalle“ auf zwei verschiedenen Strecken; Anzeige „Nächstes Segment“ |
| 5 Herzfrequenz | BLE-Herzfrequenz über die Bridge, Zonen im HUD und im Logbuch; später eine adaptive Regelung (langsam, mit Hysterese, nur opt-in) | Puls wird aufgezeichnet; Quick Run funktioniert auch ohne Sensor |
| 6 Statistik & Spiel | Verlaufsseite, Familien-Rangliste, mehr Erfolge, Wochen-Challenges | „Diese Woche 16,8 / 20 km“ |

## 5. Phase 1: Stand (24.09.2026)

**Umgesetzt:**
- `Profile/LocalProfileStore.cs`: ein JSON je Läufer. Das alte `profile.json` wird beim ersten Start zum ersten Läufer (die alte Datei bleibt als `.migrated` erhalten).
- `Profile/ProfileService.cs`: `Runners`, `Select`, `Create`, `ActiveChanged`. `Profile` ist immer der aktive Läufer, alle bisherigen Aufrufer bleiben gleich. Die Auswahl überlebt einen Scene-Reload (statisch) und einen Neustart (`settings.json`).
- `Profile/SessionLog.cs`: `SessionRecord`/`SessionSummary`/`SessionStore`, mit Index je Läufer; ein fehlender Index wird neu aufgebaut.
- `Profile/SessionRecorder.cs`: 1 Messpunkt pro Sekunde Laufzeit; speichert beim Ziel, beim Reload und beim Beenden der App.
- `Profile/RunnerStats.cs` (reines C#): Woche (ab Montag), Gesamt, letzter Lauf, Serie, Wochen-Champion, Pace.
- `UI/StartMenuUI.Runners.cs`: „Wer läuft?“ (Karten, „Nr. 1 diese Woche“ hervorgehoben), „Neuer Läufer“ (Name, Figur als Live-Vorschau), Läufer-Home (Quick Run, Strecken, Diese Woche, Letzter Lauf, Gesamt, Läufer wechseln). Mit nur einem Läufer geht es direkt ins Home.
- `UI/FinishController.cs`: Pace und neue Erfolge; „Fertig“ → Läufer-Home, „Nochmal laufen“.
- `UI/RunFollowsBelt.cs`: „■ Lauf beenden (Esc)“, damit auch ein Lauf per Tastatur ein Ende hat.
- `UI/HudController.cs`: Der Name im HUD folgt dem Läuferwechsel.
- `Editor/RunnerStatsCheck.cs`: Batch-Check (Übernahme, zwei Läufer, Logbuch mit Round-Trip und Index-Neuaufbau, Statistik). Headless: `-executeMethod Jogging.EditorTools.RunnerStatsCheck.RunBatch` → bestanden.

**Behoben (Gelände hing):** Alle Builds, auch HEAD `393d53f`, blieben bei „Landschaft wird erzeugt…“ stehen. Ursache war `TrailShaper.SampleRaw`. Es las je Durchlauf höchstens 4000 unbekannte Wegpunkte, und zwar vom *Ende* der Liste her; Punkte ohne Kachel darunter verbrauchten das Budget ebenfalls. Das freie Laufen hat 42 km, also rund 21 000 Punkte. Die Punkte am Start wurden deshalb nie gelesen, `TryBase` lief nie, und keine Kachel wurde geformt. Jetzt fragt jede gestreamte Kachel über den räumlichen Hash nur die Punkte ab, die auf ihr liegen.

**Im Build geprüft (24.09.2026):**
- Das alte Profil wurde übernommen (36 Läufe, 23 km).
- „Wer läuft?“ mit zwei Läufern; „Nr. 1 diese Woche“ steht beim richtigen Läufer.
- Anlegen eines Läufers, Wechsel, der Name im HUD folgt.
- Quick Run durch die fertige Welt, Ende mit Esc, Ziel-Screen „Lauf beendet“ mit Pace, „Fertig“ → Läufer-Home mit „Letzter Lauf … heute“ und „Diese Woche“.
- Die Logbuch-Datei hat 1 Messpunkt pro Sekunde; Start und Ende passen zur Laufzeit.
- Ein Lauf wird auch beim Beenden der App gespeichert.
- `RunnerStatsCheck` und `RouteGeneratorCheck` bestehen.

**Noch nicht mit dem Laufband geprüft:** Quelle `belt` im Logbuch, Pause und Ende über das Band.

## 6. Phase 2: Stand (24.09.2026)

**Umgesetzt:**
- `Core/SessionStateMachine.cs` (reines C#): Vorbereitung → Countdown → Läuft ⇄ Pause → Beendet/Verworfen, mit Pausengrund (Knopf, Band gestoppt, Sicherheitsclip, Bandfehler, Verbindung weg).
  - Ein Lauf per Tastatur zählt 3-2-1 herunter; ein Lauf am Band wartet auf das Band und dessen eigenen Countdown.
  - Sobald ein Band Daten liefert, ist es ein Bandlauf und bleibt einer. Reißt die Verbindung ab, pausiert der Lauf, statt still auf die Tastatur zu wechseln. Kommen wieder Daten, geht er von selbst weiter.
  - Steht das Band 120 s, endet der Lauf und wird gespeichert (wie bisher).
- `UI/RunSessionUI.cs` (ersetzt `RunFollowsBelt`; gleiche `.meta`, die Szene bleibt verdrahtet): füttert und wendet die State Machine an. Nur ein laufender Lauf bewegt Läufer, Mitläufer (`TrackManager.Frozen`) und die Uhr. Overlay für „Starte das Laufband“, 3-2-1 und jede Pause mit **Weiter / Beenden & speichern / Verwerfen** (Verwerfen mit zweitem Klick bestätigen). HUD-Knopf „Ⅱ Pause (Esc)“; Esc im Pausendialog heißt „weiter“.
- `SessionRecorder.Discard()`: Ein verworfener Lauf wird weder beim Reload noch beim Beenden gespeichert.
- `Core/AppSettings.cs` (`settings.json`): letzter Läufer, „Beim Start verbinden“, „Steigung ans Band“. Die Voreinstellungen entsprechen dem bisherigen Verhalten (beides an).
- `UI/StartMenuUI.Settings.cs`: Einstellungsseite „Laufband“ im Läufer-Home: Live-Status (Protokoll, Tempo, Steigung, Bandzustand), Verbinden/Trennen, die beiden Schalter mit einem Hinweis zu den Sicherheitsregeln.
- `UI/TreadmillConnectUI.cs`: im HUD nur noch Status (Punkt und eine Zeile); wendet die Einstellungen an, bevor sich die Bridge verbindet.
- Die Welt hinter dem Menü: Das Menü hält die Zeit nicht mehr an, die Session hält den Lauf. MapMagic baut die Landschaft, während man wählt (bereit nach etwa 13 s). `FollowCamera` folgt mit unskalierter Zeit.
- `Editor/SessionCheck.cs`: Batch-Check aller Übergänge ohne Hardware, darunter Band-Countdown, Stopp, Clip, Verbindungsverlust, Auto-Ende und Start-Abbruch. Headless: `-executeMethod Jogging.EditorTools.SessionCheck.RunBatch` → bestanden.

**Im Build geprüft:**
- Welt hinter dem Home sichtbar.
- Einstellungsseite: Schalter werden gespeichert, Trennen/Verbinden funktioniert.
- Quick Run mit 3-2-1; Pause per Esc hält Uhr und Welt an; „Weiter“.
- „Verwerfen“ (zweimal geklickt): nichts gespeichert, zurück im Home.
- „Beenden & speichern“ → „Lauf beendet“ → „Fertig“ → das Home zeigt den neuen Lauf.

**Noch nicht mit dem Laufband geprüft:** Warten auf den Bandstart, Pause bei Bandstopp oder Verbindungsverlust, automatisches Verbinden beim Start (die State Machine ist dafür per `SessionCheck` getestet).

## 7. Phase 3: Stand (24.09.2026)

**Umgesetzt:**
- `Locomotion/Treadmill/BeltSafety.cs` (reines C#): die **eine** Stelle, die entscheidet, ob die App das Band bewegen darf. Alle Adapter und Strategien laufen hindurch. Die Regeln entsprechen denen, die am F37 geprüft wurden:
  - nur bei laufendem Band (Tempo > 0) **und** laufendem Lauf; das Band wird nie gestartet;
  - Steigung 0–12 %, begrenzt auf den Bereich des Bands, höchstens 1 % pro Befehl, in Band-Auflösung (FitShow: ganze Prozent);
  - höchstens ein Befehl alle 3 s (am F37 2 s, vom Szenenbauer gesetzt); nach jeder Tempoänderung, auch nach dem Anlaufen, 3 s Ruhe;
  - verstellt der Läufer einen Wert am Band, hält sich die App bei diesem Wert 30 s zurück. Der eigene Befehl, der zurückkommt, zählt nicht als Verstellung;
  - Tempo nur mit ausdrücklicher Freigabe (`AllowSpeed`, für Workouts), in Rampen (0,5 km/h pro Befehl), höchstens 16 km/h; sonst wird das Tempo des Bands unverändert zurückgeschickt.
- `BeltControl.cs`: `ControlMode` Band (die App schreibt nie) / Strecke (Steigung aus der Strecke, Tempo bleibt beim Läufer) / Workout (Ziele vom Workout-Motor, Phase 4).
- `FitShowBeltControl.cs` (ersetzt `FitShowInclineCoupling`; gleiche `.meta`) und `TreadmillController.cs` (FTMS): beide sind jetzt Adapter vor der Safety-Schicht. FTMS fordert „Request Control“ erst direkt vor dem ersten Befehl an und nur bei einem erkannten FTMS-Band.
- **Behobene Lücke:** `MacBleBridgeTransport.WriteControlPoint` hat bisher Befehle durchgelassen, solange das Protokoll noch unbekannt war. So hätten FTMS-Bytes (z. B. „Request Control“ direkt beim Verbinden) den F37 erreichen können. Jetzt geht nur noch das erkannte Protokoll hinaus, vorher gar nichts.
- Einstellungen: „Steuerung: Strecke stellt die Steigung / Band – du steuerst“, mit einem Hinweis zu den Regeln. Taste K schaltet um (wird gespeichert). Das HUD zeigt, wer steuert oder was die Steuerung gerade tut („Steigung → 4 %“, „pausiert (manuell verstellt)“).
- `Editor/BeltSafetyCheck.cs`: 25 Fälle, z. B. stehendes Band, pausierter Lauf, unbekannter Bereich, Anlaufen, Schritte, Sendeabstand, Grenzen, Gefälle, Rundung, Band ohne Steigung, manuelle Verstellung und eigenes Echo, Tempo ohne und mit Freigabe, Stopp mitten im Lauf und die Modi. Headless: `-executeMethod Jogging.EditorTools.BeltSafetyCheck.RunBatch` → bestanden.

**Im Build geprüft:** Einstellung umschalten und speichern, HUD, Lauf mit Tastatur unverändert; alle vier Batch-Checks bestehen.

**Noch nicht mit dem Laufband geprüft:** Steigung folgt der Strecke am F37, Zurückhalten nach manueller Verstellung am Band.

## 8. Phase 4: Stand (24.09.2026)

**Umgesetzt:**
- `Training/WorkoutDoc.cs`: Ein Workout sagt, **wie** ich laufe; die Strecke sagt, **wo**. Es besteht aus Abschnitten:
  - nach Zeit **oder** Distanz;
  - Art: Aufwärmen, Laufen, Schnell, Erholung, Anstieg, Auslaufen;
  - Zieltempo (vorerst ein Hinweis) und optional eine eigene Steigung.
- Eingebaut sind: Intervalle 30 min, Pyramide 23 min, Hügel 25 min (3 · 5 · 7 % am Band), Tempodauerlauf 25 min, 5 km locker (nach Distanz).
- Eigene Workouts: `.jogworkout`-Dateien (JSON, gleiche Form) in `workouts/` des Datenordners. Ungültige Dateien werden übersprungen.
- `Training/WorkoutRunner.cs` (reines C#): läuft nur mit, solange der Lauf läuft. Der Überschuss am Abschnittsende zählt zum nächsten Abschnitt. Der Runner kennt den aktuellen und den nächsten Abschnitt und den Rest; das Ende meldet er genau einmal.
- `UI/WorkoutHud.cs` (von `RunSessionUI` zur Laufzeit angelegt):
  - Panel mit Workout und Abschnitt n/N, Zieltempo, Restzeit bzw. Restdistanz, Fortschritt in der Farbe der Abschnittsart, „Nächstes: …“ und dem Hinweis „↑ schneller / ↓ langsamer / Tempo passt“ (±0,5 km/h);
  - eine Meldung bei jedem Abschnittswechsel;
  - am Ende „Workout geschafft!“, und der Lauf wird beendet und gespeichert.
- Band: Steht die Steuerung auf „Strecke“, übernimmt das Workout (`ControlMode.Workout`). Ein Abschnitt mit eigener Steigung stellt diese ein, sonst gilt die Steigung der Strecke. Alles läuft durch `BeltSafety`, das Tempo bleibt beim Läufer.
- Menü:
  - „Workouts“ im Läufer-Home;
  - eine Seite mit Streckenwahl (Freies Laufen, Waldrunde, Hügelrunde, Flach, eigene Strecken) – jedes Workout auf jeder Strecke;
  - pro Workout ein kleines Profil der Abschnitte (Breite = Zeit, Höhe = Tempo bzw. bei Hügeln Steigung).
- Logbuch: `workoutId`, `workoutName`, `workoutCompleted`. Ziel-Screen: „Workout geschafft!“ / „Workout beendet“ mit „vollständig“ oder „n von N Abschnitten“; „Nochmal laufen“ wiederholt das Workout. Das Home zeigt das Workout beim letzten Lauf.
- `Editor/WorkoutCheck.cs`: Dauern der eingebauten Workouts, Zeit- und Distanz-Abschnitte mit Übertrag, Ende genau einmal, JSON-Round-Trip und Validierung → bestanden.
- **Beim Test gefunden und behoben:** Führte der Start auf eine andere Strecke (mit Reload), hat die alte Szene das Workout im letzten Frame vor dem Reload übernommen, und nach dem Reload fehlte es. Jetzt wird es erst übernommen, wenn das Menü den Lauf in dieser Szene wirklich startet (`WorkoutRuntime.Armed`).

- **Ebenfalls gefunden und behoben – App hing beim Beenden:**
  - MapMagic stoppt seine Generator-Threads nicht, wenn eine Szene endet; der Hook dafür ist nur im Editor aktiv (`#if UNITY_EDITOR`). Nach Reloads (Streckenwechsel, „Fertig“, „Verwerfen“) rechneten die Threads für die alte Szene weiter und hielten beim Beenden Mono fest (16 Threads statt höchstens 3).
  - Jetzt bricht `RouteRuntime.OnDestroy` am Szenenende `ThreadManager.Abort()` auf.
  - Eine Zeile im mitgelieferten MapMagic-Code (`ThreadManager.cs`, als „Jogging:“ markiert): Worker-Threads sind Background-Threads.
  - Ergebnis: Die App beendet sich nach zwei Reloads in etwa 1 s.

**Im Build geprüft:**
- „5 km locker“ auf Freies Laufen (Panel, Hinweis „langsamer“).
- Ein eigenes Test-Workout (3 × 15 s, als Datei) auf der Waldrunde: Abschnittswechsel, „schneller“, Ende nach 45 s, „Workout geschafft! … vollständig“, gespeichert mit 45 Messpunkten, Home zeigt das Workout.

**Offen:** Tempo ans Band (die Safety-Schicht kann es schon, braucht aber eine eigene Freigabe und einen Test am F37); Workout-Editor im Menü; Hügel-Workout am F37 prüfen.

## 9. Phase 5: Stand (24.09.2026)

**Umgesetzt:**
- **Bridge** (`Tools/MacBleBridge/main.swift`, Version 3.0): Ein zweiter, unabhängiger Platz verbindet einen Pulsgurt (Standard Heart Rate Service 0x180D, Messwert 2A37), zum Beispiel einen Brustgurt oder eine Uhr, die die Herzfrequenz sendet. Neue UDP-Nachrichten: `H`/`Y` (suchen/trennen) sowie `R`/`T`/`M` (Messwert, verbunden, Status). Das Laufband bleibt davon unberührt.
- `MacBleBridgeTransport`: `ConnectHeartRate`, `DisconnectHeartRate`, `HeartRateReceived`. Solange noch kein Gurt verbunden ist, fragt die App alle 3 s erneut, weil die Bridge direkt nach dem Start eventuell noch nicht zuhört.
- `Training/HeartRate.cs` (reines C#):
  - Parser für 8/16 Bit; „kein Hautkontakt“ wird verworfen.
  - Fünf Zonen als Anteil am maximalen Puls (< 60 · 60–70 · 70–80 · 80–90 · ≥ 90 %).
  - Statistik: Durchschnitt, Maximum, Zeit pro Zone.
- `Training/HeartRateMonitor.cs`: liefert den Puls aus dem Gurt. Zum Testen ohne Hardware gibt es die Kommandozeile `-hrsim`: Der simulierte Puls folgt Tempo und Steigung mit etwa 20 s Verzögerung. Im HUD erscheinen „Puls · Zone“ in der Zonenfarbe, aber **nur, wenn ein Puls da ist**; ohne Gurt sieht das HUD aus wie bisher.
- Logbuch: Puls je Messpunkt, Durchschnitt, Maximum, Sekunden pro Zone. Ziel-Screen: „Ø Puls · max“. Home: „Ø … bpm“ beim letzten Lauf.
- Workouts: Abschnitte können eine Zielzone haben (`hrZone`). Der Hinweis lautet dann „Puls zu niedrig / zu hoch / Zone passt“; die Zielzone hat Vorrang vor dem Tempo. Neu eingebaut: „Zone 2 · 30 min“.
- Menü: „Laufband“ heißt jetzt **„Geräte“**, mit Abschnitten für Laufband und Pulsgurt (Status, Verbinden, „Beim Start verbinden“). Der maximale Puls wird pro Läufer eingestellt (Standard 190).
- `Editor/HeartRateCheck.cs`: Parser (8/16 Bit, Kontakt, RR, ungültige Werte), Zonengrenzen, Statistik, Zone-2-Workout → bestanden.

**Bewusst noch nicht:** eine adaptive Regelung von Tempo oder Steigung nach Puls. Sie braucht Tests mit echtem Gurt am F37. Die Safety-Schicht aus Phase 3 ist dafür bereits die Voraussetzung.

**Im Build geprüft:**
- Geräte-Seite mit Pulsgurt-Abschnitt.
- Lauf mit `-hrsim`: HUD „Puls 165 · Zone 4 Schwelle“, Ziel-Screen „Ø Puls 134 · max 166“, im Logbuch Puls je Sekunde und Zeit pro Zone.
- Home „Ø 134 bpm“.
- Ohne Gurt bleibt das HUD unverändert.
- Die App beendet sich sauber.

**Noch nicht mit Hardware geprüft:** echter Pulsgurt. Die neue Bridge 3.0 wurde nach `~/Applications` installiert; eventuell fragt macOS noch einmal nach der Bluetooth-Erlaubnis.

## 10. Phase 6: Stand (24.09.2026)

**Umgesetzt:**
- **Erfolge** (`Profile/AchievementCatalog.cs`), weiter datengetrieben: 23 statt 6. Die Bedingungen sehen jetzt auch das Logbuch (`AchievementContext`): Anzahl Läufe, Gesamtstrecke bis 500 km, 5/10 km und 30/60 min am Stück, 1000 Höhenmeter, Serien von 3 und 7 Tagen, Workouts, der erste Lauf am Band, 20 min in Zone 2 und eine eigene Strecke komplett. Die Ids der alten Erfolge bleiben gleich. Geprüft wird nach jedem Lauf, wenn das Logbuch schon gespeichert ist, und beim Öffnen der Statistik (nachträglich).
- **Wochenziele** (`Profile/Challenges.cs`), ohne Server: 3 Läufe, 15 km und 90 min pro Woche (Montag bis Sonntag). Im Home steht kompakt „Wochenziele n / 3“ mit drei Balken, in der Statistik ausführlich.
- `RunnerStats`:
  - schnellste 5 km innerhalb eines Laufs aus den 1-Hz-Messpunkten, im Logbuch als `best5kS` gespeichert;
  - Zeiträume Heute, Woche, Monat, Jahr, Gesamt;
  - Familien-Rangliste der Woche.
- **Statistik-Seite** („Statistik & Erfolge“ im Home):
  - links die Zeiträume, Bestwerte (längster Lauf, längste Zeit, schnellste 5 km, Ø Tempo, Serie) und die Wochenziele;
  - rechts „Familie diese Woche“ (ab zwei Läufern) und das Logbuch, seitenweise.
- **Lauf-Detail** (Klick auf einen Lauf im Logbuch): Kennzahlen, ein Diagramm von Tempo, Puls und Steigung über die Zeit (`UI/LineChart.cs`) und die Zeit pro Pulszone.
- **Erfolge-Seite**: alle 23 in Gruppen; freigeschaltete in Gold, die anderen mit dem Hinweis, wie man sie bekommt.
- `Editor/StatsCheck.cs`: schnellste 5 km (2 km locker, 5 km schnell, 1 km locker → 25:00), Zeiträume, Rangliste, Wochenziele, Erfolge aus dem Logbuch (genau einmal, keine doppelten Ids, Prüfung auch ohne Logbuch) → bestanden.

**Im Build geprüft:**
- Home mit den Wochenzielen.
- Statistik mit Zeiträumen, Bestwerten, Wochenzielen und Logbuch.
- Lauf-Detail mit Diagramm und Pulszonen.
- Erfolge-Seite: drei wurden nachträglich aus dem Logbuch freigeschaltet, insgesamt 9 von 23.
- Alle sieben Batch-Checks bestehen.

**Bewusst klein gehalten** (wie im Konzept): keine Online-Ranglisten, keine Freunde, keine Abos. Die Familien-Rangliste ist rein lokal.

## 11. Review-Punkte (25.09.2026)

Aus dem Review (fachlich/technisch), umgesetzt und committet:

| # | Punkt | Stand |
|---|---|---|
| 2 | Workouts laufen auf Rundkursen in weitere Runden („Runde 2 · …“); Hinweis bei zu kurzer offener Strecke | im Build geprüft |
| 3 | Absturzsicheres Speichern (`SafeFile`: Temp-Datei, Tausch, `.bak`); Logbuch-Index wird aus den Lauf-Dateien neu aufgebaut | Batch-Check |
| 4 | Steigung nach dem Lauf schrittweise zurück auf 0 % – nur was die App gesetzt hat, nie das Tempo | Batch-Check; am F37 noch zu prüfen |
| 5 | Steuerungsmodus bei jedem Szenenstart aus den Einstellungen | am F37 geprüft (Steigung folgt der Strecke) |
| – | Protokollzeile mit Uhrzeit für manuelle Verstellung und Ende des Zurückhaltens | eingebaut; beim nächsten Lauf im Log prüfen |
| 6 | Quick Run: jedes Mal eine neue Landschaft, startet sofort (sie wird hinter dem Home vorbereitet); „Strecke speichern“ auf dem Ziel-Screen | im Build geprüft |
| 7 | Profil-Seite: Name, Figur, Läufer löschen; einzelne Läufe löschen | im Build geprüft |
| 8 | Laufband wird gemerkt, Pulsgurt pro Läufer gekoppelt; fremder Gurt wird ignoriert (Bridge 3.1) | gebaut; F37 war beim Test im Standby → **offen** |
| 9 | Workout-Editor (Anpassen, Bearbeiten, Neu, Löschen) | im Build geprüft |
| 10 | Geburtsjahr/Gewicht → max. Puls (Tanaka) und Kalorien (ACSM) | Batch-Check, Seite im Build |
| 11 | Tempo ans Band in Workouts – **nur wenn eingeschaltet** (Standard aus), über `BeltSafety` | Batch-Check; am F37 **offen** |
| 12 | Export als TCX (Strava, Garmin) in „Downloads“ | Batch-Check |
| 13 | Build erzeugt keine neuen Asset-GUIDs mehr (vorher ~34 Dateien pro Build, jetzt nur die Szene) | zweimal gebaut, verglichen |
| 14 | `Tools/check.sh`: alle 7 Selbsttests in einem Lauf (≈7 s) | läuft |
| 15 | Wochenziele pro Läufer | Batch-Check |
| 16 | Tastatur-Tempo höchstens 20 km/h; `-starts` zählt den Startversatz nicht mehr als Distanz | Code |
| 17 | Layout-Helfer | nur `UiControls.Stepper` (in Profil, Editor genutzt); kein Umbau der alten Seiten |

**Erledigt (25.09.):** Der Szenenbauer verwendet seine Objekte wieder (`Root()`: zurückgesetzt statt neu angelegt) – ein Build ändert jetzt **keine** Datei mehr. Dazu ein F37-Simulator (`-beltsim`, `FitShowEmulator`, `BeltEmulatorCheck`), mit dem die Band-Punkte 4, 5 und 11 im geschlossenen Regelkreis getestet sind.

**Noch offen:** Tests am echten F37: Punkt 8 (Laufband gemerkt, Gurt gekoppelt), Punkt 11 (Tempo in einem Workout, nur eingeschaltet), Punkt 4 (Steigung zurück nach „Beenden & speichern“ am Anstieg).

## 12. Ohne Laufband weiterentwickelt (25.09.2026)

| Thema | Stand |
|---|---|
| F37-Simulator (`-beltsim`) | Band-Tests im geschlossenen Regelkreis (`BeltEmulatorCheck`); dabei gefunden: Tempo-Rampe des Bands wurde als Verstellung gewertet |
| Szene ohne Build-Änderungen | Szenenbauer verwendet Objekte wieder – ein Build ändert keine Datei |
| Verlauf | 12-Wochen-Balken (km, Zeit, Läufe) pro Läufer, Wochenziel als Linie |
| Trainingspläne | 3 Pläne über 4–6 Wochen, Fortschritt pro Läufer, nächste Einheit im Home; dabei gefunden: Workouts liefen mit Bildzeit statt Laufuhr |
| Geist-Läufer | bester (sonst letzter) Lauf auf gespeicherten Strecken als Figur, Abstand in Sekunden; pro Läufer abschaltbar (Profil, Taste G) |
| Ansagen | Start, Pause, Kilometer, Abschnitte, Piepen vor dem Wechsel, Zusammenfassung; macOS-Stimme; abschaltbar |
| Datensicherung | ZIP in „Downloads“, Wiederherstellen mit automatischer Sicherung davor |
| Aufräumen | README neu, [Bedienungsanleitung](Bedienungsanleitung.md), alter HUD-Knopf „Läufer anpassen“ entfernt (steckt im Profil) |

**Bewusst nicht gemacht:** die alten Menüseiten (Streckeneditor, Meine Strecken, Werkstatt) auf den
Layout-Helfer umstellen – sie funktionieren, der Umbau brächte nur Risiko. Neue Seiten nutzen `UiControls`.

Selbsttests: `Tools/check.sh`, 10 Prüfungen.

## 13. Start, Anzeige, Wetter, Familie (25.09.2026)

| Thema | Stand |
|---|---|
| Schnellerer Start | Gemessen: Drafts nach 2 s, erste Detail-Kachel nach 12,6 s (MapMagic-Erosion als C#, das native Plugin gibt es nur für x86_64). Build jetzt mit **IL2CPP**: Gelände nach ~9 s statt ~17 s. `Process.Start` geht unter IL2CPP nicht → `Core.Shell` (libc `system`) für Bridge-Start und Ansagen. Builds dauern ~3,5 min. |
| Laufband-Ansicht | große Zahlen unten (Zeit, km, km/h + Pace, Steigung, Puls); Geräte → Anzeige oder Taste H |
| Wetter | klar / bewölkt / Regen / Schnee pro Strecke (Editor; Quick Run zufällig aus dem Seed); Licht, Dunst, Regen-/Schnee-Partikel |
| Geräusche | live erzeugt: Wind, Schritte im Laufrhythmus, Vögel, Regen, Bach; Geräte → Geräusche |
| Familien-Challenge | gemeinsames Wochenziel (Standard 10 km pro Läufer, in der Statistik einstellbar); Balken mit den Anteilen auf „Wer läuft?“, kurz im Home |
