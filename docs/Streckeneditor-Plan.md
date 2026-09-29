# Streckengenerator & eigene Strecken — Planung

Stand: 24.09.2026 · Status: **Phase 0–4 umgesetzt (Teilen ohne Server); Server-Community bei Bedarf**

## 1. Zielbild

Ich erzeuge mir **schöne, vielfältige Laufstrecken aus Parametern**. Eine Karte ist nicht nötig. Beispiel:

> *„8 km, Rundkurs, zwei längere Anstiege mit etwa 6 %, sonst wellig, Mischwald mit Lichtungen,
> ein See, Abendsonne.“*

Daraus entsteht eine laufbare Landschaft. Ich laufe sie auf dem Laufband, passe sie an und speichere sie unter einem Namen. Später kann ich sie genauso wieder laufen. Irgendwann sollen andere eigene Strecken anlegen, teilen und Zeiten vergleichen können. Zuerst ist das aber nur für mich.

**Entschieden (24.09.2026):**
- keine Kartenanbindung nötig,
- Distanz und Höhenprofil sind maßgeblich, die Umgebung darf frei gestaltet sein,
- Community offen, erstmal nur für mich,
- Android erst, wenn alles funktional vollständig ist,
- die Cartoon-Welt fliegt raus.

## 2. Leitentscheidungen

### 2.1 Das Rezept speichern, nicht die Welt

Eine Strecke ist ein kleines, versioniertes JSON-Dokument, die `.jogroute`-Datei. Sie enthält die **Parameter und den Seed**, dazu eigene Änderungen. Die Welt wird daraus **deterministisch** neu erzeugt: gleiches Dokument, gleiche Strecke. Die Vorteile:

- Die Datei hat nur wenige KB und ist dadurch teilbar und versionierbar.
- Eine bessere Grafik in späteren Versionen wertet alte Strecken automatisch auf.
- Handy und Mac erzeugen dieselbe Strecke in ihrer jeweiligen Detailstufe.

Voraussetzung ist, dass jede Zufallsentscheidung aus Seed, Streckenposition und Kategorie abgeleitet wird. Sie darf nie von Zeit oder Ladereihenfolge abhängen. Ein Test vergleicht zwei Erzeugungen.

Zusätzlich speichert das Dokument das **erzeugte Höhenprofil** selbst. Für Ranglisten und Statistiken ist das die verbindliche Größe. Wenn eine spätere Generatorversion die Landschaft anders streut, bleibt die Strecke im Kern gleich: gleiche Länge, gleiches Profil.

### 2.2 Erst das Profil, dann die Landschaft darum

Die Reihenfolge ist umgekehrt zu heute:

1. **Höhenprofil** aus den Parametern: Länge, Anzahl, Höhe und Steilheit der Anstiege, Welligkeit. Es ist sofort laufbar: höchstens ±10–12 %, sanfte Übergänge, passend zum Band.
2. **Wegverlauf** aus den Parametern: Kurvigkeit, Rundkurs oder Strecke.
3. **Gelände** prozedural um Weg und Profil herum. Eine großräumige Grundhöhe folgt dem Profil, Hügel und Täler kommen aus Rauschen. Ein Wegbett wird eingeformt wie heute.
4. **Umgebung** entlang der Strecke: Abschnitte mit eigener Vegetation (Wald, Lichtung, Wiese, Heide), Wasser, Felsen, Zuschauer, Objekte.

Heute leitet `TrailShaper` das Profil aus dem Gelände ab. Künftig gibt das Profil die Richtung vor und das Gelände passt sich an. Der vorhandene Code für Wegbett, Böschungen, Streuung und Figuren bleibt gleich.

### 2.3 Änderungen hängen an der Streckenposition

Eigene Anpassungen sind Regeln entlang der Streckenlänge s, zum Beispiel:
- „Wald dichter km 1,2–1,8 links“
- „See rechts bei km 3“
- „Zuschauergruppe bei km 5“

So bleiben sie gültig, wenn ich Parameter nachjustiere.

## 3. Parameter (v1)

| Gruppe | Parameter | Wirkung |
|---|---|---|
| Strecke | Länge (km); Rundkurs ja/nein; Kurvigkeit (gerade … verschlungen); Seed | Wegverlauf |
| Profil | Anzahl Anstiege; Höhe je Anstieg (Hm) oder Gesamt-Höhenmeter; max. Steigung; Welligkeit dazwischen; Start/Ende flach | Höhenprofil |
| Landschaft | Relief der Umgebung (sanft … bergig); Wasser (keins, Bach, See); Felsen (wenig … viel) | Gelände um den Weg |
| Vegetation | Grundtyp (Mischwald, Nadelwald, Birkenhain, Wiese/Heide); Dichte; Anteil Lichtungen; Unterholz; Abschnittswechsel alle x km | Bäume, Büsche, Gras |
| Stimmung | Tageszeit; Wetter/Dunst; Jahreszeit (später) | Licht, Nebel, Farben |
| Leben | Anzahl Mitläufer; Rivale an/aus mit Tempo; Zuschauer (wenig … viel) | Figuren |

Das Profil wird vor dem Erzeugen in einer **Vorschau** gezeigt: Kurve, Höhenmeter, maximale Steigung, geschätzte Dauer bei Wunschtempo. So sieht man sofort, ob es passt.

## 4. Datenmodell (Entwurf)

```jsonc
{
  "schemaVersion": 1,
  "id": "rt_01J…",                 // ULID, global eindeutig (teilbar)
  "revision": 2,
  "forkOf": null,
  "meta": { "name": "Abendrunde Seeblick", "author": "…", "created": "…", "updated": "…",
            "description": "", "tags": ["wellig", "see"], "license": "CC-BY-4.0" },
  "generator": { "version": "1.0", "seed": 424242 },
  "params": {
    "lengthKm": 8.0, "loop": true, "curviness": 0.5,
    "climbs": [ { "atKm": 2.0, "heightM": 35, "maxGrade": 0.06 }, { "atKm": 5.5, "heightM": 25, "maxGrade": 0.07 } ],
    "rolling": 0.4, "maxGrade": 0.10, "flatStartEndM": 200,
    "relief": 0.5, "water": "lake", "rocks": 0.3,
    "vegetation": { "base": "mixed", "density": 0.7, "clearings": 0.3, "undergrowth": 0.6, "sectionKm": 1.5 },
    "mood": { "timeOfDay": 18.5, "haze": 0.3 },
    "life": { "runners": 8, "rival": { "on": true, "paceKmh": 11 }, "spectators": 0.5 }
  },
  "profile": { "stepM": 2, "heightsM": [ … ] },     // Ergebnis, verbindlich für Statistik/Ranglisten
  "edits": [
    { "type": "density", "fromM": 1200, "toM": 1800, "side": "left", "factor": 1.6 },
    { "type": "spectators", "atM": 5000, "count": 12 }
  ]
}
```

Speicherort: `Application.persistentDataPath/routes/*.jogroute`. Import und Export laufen per Datei. Optional kommt später ein GPX-Export des Profils.

## 5. Bedienung

In der **App**, im Startmenü unter „Strecken“:
- **Neue Strecke:** Parameter wählen mit Presets („Flach & schnell“, „Hügelig“, „Bergauf-Training“, „Waldrunde“), Seed würfeln, Profil-Vorschau ansehen, „Erzeugen & laufen“.
- **Meine Strecken:** Liste mit Name, km, Höhenmetern und Bestzeit. Aktionen: laufen, bearbeiten (Parameter ändern ergibt eine neue Revision), duplizieren, umbenennen, löschen, exportieren.
- **Anpassen (später):** In der Strecke eine Werkstatt, in der ich Abschnitte verdichte oder lichte und Objekte oder Zuschauer setze. Die Änderungen landen als Edits im Dokument.

Einen eigenen Editor außerhalb der App braucht es nicht.

## 6. Technische Punkte und Risiken

1. **Die „immer vorwärts in +Z“-Annahme** in `TrailPath`, `TrailShaper` und der Streuung muss weg. Rundkurse drehen um 360°. Nötig sind ein räumlicher Index über die Streckenpunkte und eine Kachel-Fertig-Prüfung ohne die Z-Annahme.
   - Kreuzungen vermeiden wir im Generator von vornherein, zum Beispiel mit monoton drehender Richtung beim Rundkurs. Echte Kreuzungen sind damit kein v1-Thema.
2. **Rundkurs schließen:** Ende und Anfang müssen in Lage, Richtung und Höhe zusammenpassen. Das Profil gleicht dafür die Gesamt-Höhenmeter auf null aus, die Linie wird auf den Startpunkt geschlossen.
3. **Gelände um ein vorgegebenes Profil:** Die Grundhöhe muss großräumig dem Profil folgen, sonst gibt es Riesendämme. Das geht als weicher Versatz pro Kachel, der nur von der Weltposition abhängt und damit über Kachelgrenzen stetig ist.
   - **Zu prüfen in Phase 0:** ob das mit MapMagic sauber geht, oder ob ein eigener schlanker Terrain-Generator besser ist. Der eigene Generator hätte weitere Vorteile: Determinismus unter voller Kontrolle, schnellerer Start, keine Plattformprobleme mit dem nativen MapMagic-Plugin.
4. **Vielfalt:** Das braucht mehr Vegetationssätze und Gelände-Texturen als heute, dazu Wasser (See, Bach) und Lichtungen. Assets müssen frei und lizenzklar sein.
5. **Performance:** Die Streckenlänge ist egal, weil gestreamt wird. Kritisch sind Vegetationsdichte und Startzeit. Aktuell dauert der Start etwa 14 s, Ziel sind unter 8 s.
6. **Handy:** Detailstufen werden aus demselben Dokument abgeleitet, aber erst, wenn alles funktional vollständig ist.

## 7. Phasen mit Abnahmekriterien

| Phase | Inhalt | Fertig, wenn … |
|---|---|---|
| **0 – Aufräumen & Fundament** | Cartoon-Welt entfernen, PhotoRun wird die App. Figuren nur noch Rocketbox, mit einfacher Figurauswahl statt UMA. `.jogroute` v1 mit Laden und Speichern. Profil-Generator aus Parametern. Weg-Generator (auch Rundkurs). `TrailShaper` folgt dem vorgegebenen Profil. „+Z“-Annahme entfernt. Grundhöhe aus dem Profil. | eine per Parametern erzeugte 5-km-Runde ist fehlerfrei laufbar, schließt sauber, das Profil stimmt mit der Vorgabe überein (Höhenmeter ± 5 %), und die zweite Erzeugung ist identisch |
| **1 – Strecken in der App** | Menü „Strecken“ mit Neu, Presets, Profil-Vorschau, Meine Strecken und Bestzeiten. Ziel bei Streckenende, Ergebnis-Screen. | ich lege in 1 Minute eine Strecke an, laufe sie, sie ist nach Neustart wieder da und hat meine Bestzeit |
| **2 – Vielfalt** | Vegetations-Abschnitte, Lichtungen, Wasser, Felsgruppen, Tageszeit und Dunst, mehr Pflanzen und Texturen | fünf Presets sehen deutlich unterschiedlich aus |
| **3 – Anpassen** | Werkstatt in der App mit Edits entlang der Strecke | Änderungen überleben Speichern und Laden sowie eine Parameter-Änderung |
| **4 – Teilen** | Export und Import von `.jogroute`, später Server (Nakama) mit Stöbern, Bewerten und Ranglisten je Strecke | eine Datei von einem Rechner läuft auf einem anderen identisch |
| optional | GPX-Import als weitere Profil- und Längenquelle (echte Runde nachlaufen) | – |

## 8. Phase 0: Stand (24.09.2026)

| Schritt | Stand |
|---|---|
| 1. Cartoon raus | ✅ UMA, Cartoon-Pakete, Segment-System, Toon-Shader, alte Szene entfernt; Figurauswahl aus Rocketbox |
| 2. Szenenaufbau | ✅ PhotoRun ist die einzige Szene; der Builder ist beliebig oft ausführbar |
| 3. `.jogroute` v1 | ✅ `RouteDoc` + `RouteStore` (JSON, ULID, Schema-Version); die UI zum Speichern kommt in Phase 1 |
| 4. Profil-Generator | ✅ `RouteGenerator.Profile`: Anstiege, Welligkeit, laufbar begrenzt (≤ 10 %, ≤ 0,2 %/m), Rundkurs schließt |
| 5. Weg-Generator | ✅ offene Strecke (immer vorwärts) und Rundkurs (sternförmig geschlossen), ohne Selbstkreuzung |
| 6. Gelände folgt dem Profil | ✅ MapMagic bleibt; `TrailShaper` hebt und senkt das Umland weich (Gauß-Feld) und formt das Wegbett |
| 7. „+Z“-Annahme | ✅ entfernt (räumliches Raster über die Streckenpunkte) |

Prüfungen: `Jogging → Strecken → Generator prüfen` (5 Presets × 3 Seeds). Geprüft werden Determinismus,
Länge, Schluss, Radius, Kreuzung, Steigungsgrenze und der Datei-Round-Trip. In der App getestet:
Hügelrunde, Bergauf-Training (Anstieg mit 9 %), Flach & schnell bis zum Ziel mit sauberem Rundkurs-Schluss;
die Starthöhe ist in zwei Läufen identisch.

Bekannte Grenzen:
- Der Start dauert etwa 12–15 s; bis dahin verdeckt ein Ladebildschirm die Welt.
- Das Formen einer Kachel kostet einmalig ein paar Millisekunden.
- Zuschauer können in Büschen stehen.
- Der Testschalter `-starts` funktioniert nur bis etwa 1 km Luftlinie vom Start.

## 8b. Phase 1: Stand (24.09.2026)

- **Startseite:** Freies Laufen, Neue Strecke, Meine Strecken (mit Anzahl), „Weiter: …“ für die zuletzt gewählte Strecke.
- **Editor:**
  - Name, Vorlagen (Flach, Hügelig, Bergauf, Wald).
  - Regler für Länge (1–21 km), Form (Rundkurs oder A → B), Kurvigkeit, Anzahl, Höhe und Steilheit der Anstiege, Welligkeit, max. Steigung; Variante (Seed) würfeln.
  - Live-Höhenprofil mit ↑/↓ Höhenmetern, max. Steigung und geschätzter Dauer.
  - Speichern, Speichern & laufen.
- **Meine Strecken:** Liste (7 pro Seite) mit km, Höhenmetern, Form und Bestzeit. Aktionen: Laufen, Bearbeiten, Kopie, Löschen (mit Rückfrage), Ordner öffnen.
- **Revisionen:** Wer Parameter einer gespeicherten Strecke ändert, erzeugt eine neue Revision mit eigenen Bestzeiten.
- **Bestzeiten** je Strecke und Revision im Profil. Der Zielbildschirm zeigt „Erste Bestzeit“, „Neue Bestzeit“ oder die bisherige Bestzeit. Gezählt wird nur, wenn die ganze Strecke gelaufen wurde.
- **Rundkurse:** Jeder Anstieg wird ein Hügel, der Abstieg liegt auf halbem Weg zum nächsten Anstieg, nie direkt am Start.
- W/S-Tasten ändern beim Tippen in Textfeldern nicht mehr das Tempo.

## 8c. Phase 2: Stand (24.09.2026)

- **Vegetationsabschnitte** (`Vegetation`):
  - Die Strecke ist in Abschnitte von 0,5–1,3 km geteilt: Mischwald, Nadelwald, Birkenhain, Lichtung oder Wiese.
  - Welche vorkommen, bestimmen der Grundtyp (Mischwald, Nadelwald, Birkenhain, Wiese & Heide), der Lichtungsanteil und der Seed.
  - Die Abschnitte gehen über 80 m ineinander über; die Dichte skaliert Bäume und Unterholz.
- **Felsen:** Der Anteil der Steine im Unterholz folgt dem Parameter `rocks`.
- **Seen** (`Lakes`):
  - Bei „See am Weg“ gibt es etwa einen See pro 6 km, 14–30 m neben dem Weg, mit dem Wasserspiegel knapp unter Weghöhe.
  - `TrailShaper` gräbt das Becken, senkt das Ufer sanft zum Wasser ab und zieht einen niedrigen Rand, damit kein Wasser über tieferem Land schwebt.
  - Bäume halten Abstand zum Wasser.
- **Stimmung** (`Mood`):
  - Die Tageszeit bestimmt Sonnenhöhe, Richtung (Ost → West), Farbe (warm bei tiefer Sonne) und Intensität; der Himmel folgt.
  - Der Dunst bestimmt Nebeldichte und Farbe.
- **Editor:** neuer Bereich „Landschaft & Stimmung“ mit Vegetation, Dichte, Lichtungen, Wasser, Tageszeit und Dunst.
- **Vorlagen** unterscheiden sich jetzt sichtbar:
  - Flach: Wiese, Morgen, See
  - Hügelig: Mischwald, Abend
  - Bergauf: Nadelwald, Morgendunst, viele Felsen
  - Wald: dichter Mischwald, Mittag, See
- **Nachgereicht:**
  - **Relief** (`TerrainLook`): Höhe des MapMagic-Geländes von 40 m (flach) bis 190 m (bergig); der Weg folgt weiter seinem eigenen Profil.
  - **Boden-Texturen und Grasfarbe je Vegetation:**
    - Nadelwald: dunkler Waldboden, dunkles Gras
    - Birkenhain: frisches Grün
    - Wiese: goldenes, trockenes Gras
  - **Bäche** (Wasser: „Bach“ oder „See & Bach“):
    - Ein Bach quert den Weg an Tiefpunkten des Profils, etwa alle 2,5 km, mindestens 1,2 km auseinander.
    - Er folgt dem Gelände: Das natürliche Gelände entlang des Bachs wird abgetastet. Der Bach fließt vom höheren zum tieferen Ende, liegt immer mindestens 0,7 m unter dem Boden und steigt nie an. So schneidet sich die Rinne immer ein und hält ihr Wasser.
    - Der Weg quert auf einem kleinen Damm mit Holzgeländer.
- **Jahreszeiten** (Parameter `season`, Editor „Jahreszeit“):

  | | Bäume | Boden | Licht, Wasser |
  |---|---|---|---|
  | Frühling | frisch hellgrün | grün | – |
  | Sommer | wie gehabt | wie gehabt | – |
  | Herbst | Birkenlaub fleckig gelb, orange, rot; Büsche rostbraun | trockener | wärmer, etwas dunstiger |
  | Winter | Birken kahl (nur Rinde und Zweige); Kiefern mit Raureif; Büsche braungrau | Schnee, Gras bereift | tiefe, kühle Sonne; Seen und Bäche zugefroren |

  - Die Varianten erzeugt der Builder (`SeasonBuilder`) aus dem Birken-Atlas: Die Rinde bleibt, nur die grünen Blattpixel werden umgefärbt oder ausgeblendet.
  - Zur Laufzeit wählt `SeasonAssets.Swap` die passende Variante.
  - Vorlagen: Flach = Frühling, Hügelrunde = Herbst, Bergauf = Winter.

## 8d. Phase 3: Stand (24.09.2026)

- **Anpassungen** (`RouteEdit` in `edits` der `.jogroute`), verankert an der Streckenlänge:
  - `trees`: Wald dichter (×1,8), lichter (×0,45) oder Lichtung (×0,05) auf einem Bereich, links, rechts oder beidseitig; mit 30 m weichem Übergang.
  - `spectators`: eine Gruppe von 12 jubelnden und klatschenden Zuschauern an einer Stelle.
  - `lake`: ein See neben dem Weg.
  - `stream`: ein Bach quer zum Weg. Die Werkstatt prüft vor dem Setzen, ob kein anderer Teil der Strecke zu nah ist.
- **Werkstatt:** in „Meine Strecken“ → Werkstatt.
  - Der Lauf ruht, die Kamera schaut von schräg oben auf den Weg.
  - Man fährt mit dem Positionsregler, mit ◀/▶ 100 m oder mit ←/→ bzw. A/D entlang der Strecke.
  - Bereich und Seite wählen, dann eine Aktion. „Hier entfernen“ löscht die Anpassungen an der Stelle.
  - Wald und Zuschauer wirken sofort. Seen und Bäche gräbt „Speichern“ per Neuladen an derselben Stelle ins Gelände.
  - „Speichern & laufen“, „Zurück“ verwirft Ungespeichertes.
- Anpassungen überstehen Speichern und Laden sowie Parameter-Änderungen; das prüft die Selbstprüfung mit.
- Die Laufzeit zählt in der Werkstatt nicht.

## 8e. Phase 4: Stand (24.09.2026) — Teilen ohne Server

- **Teilen-Code** (`RouteShare.ToCode/FromCode`):
  - Aufbau: `JOG1:` + base64url(gzip(JSON)) mit Parametern, Seed, Anpassungen, Metadaten und einem Prüfwert des Höhenprofils, etwa 700–900 Zeichen.
  - Die App kopiert ihn in die Zwischenablage; verschicken geht z. B. per Messenger.
  - Der Empfänger erzeugt das Profil neu und vergleicht den Prüfwert. Bei einer abweichenden App-Version erscheint eine Warnung.
- **Datei:** „Als Datei exportieren“ legt `<Name>.jogroute` in Downloads ab. „Importieren“ listet `.jogroute`-Dateien aus Downloads und vom Schreibtisch.
- **Import:**
  - behält ID und Revision, damit dieselbe Strecke überall dieselbe ist und Bestzeiten je Revision gelten;
  - Stand je Strecke: „neu“, „neuere Version“ oder „schon vorhanden“ (wird dann nicht erneut übernommen).
- „Meine Strecken“ → „…“: Teilen-Code kopieren, Datei exportieren, Kopie anlegen, Löschen. Die Autorin (Läufername) wird beim Speichern eingetragen.
- Die Selbstprüfung testet den Code-Round-Trip: gleiche ID, gleiche Anpassungen, identisches Profil.
- **Server-Community (später, bei Bedarf):** Nakama (selbst gehostet) mit Konten, Hochladen, Stöbern, Bewerten, Ranglisten je Strecke und Revision, Moderation und Datenschutz (siehe Abschnitte 7–8). Format, IDs und Revisionen sind dafür vorbereitet.

## 8a. Phase 0: ursprüngliche Reihenfolge

1. **Aufräumen (Cartoon raus):** Anhand einer Abhängigkeitsanalyse der PhotoRun-Szene entfernen, was nur die Cartoon-Welt nutzt:
   - Szenen, Segment-System, Toon-Shader und Themes,
   - nicht mehr genutzte Asset-Pakete,
   - UMA nach Ersatz durch Rocketbox-Auswahl.

   Build und Lauf müssen danach unverändert funktionieren.
2. **Szenenaufbau vereinfachen:** Der PhotoRun-Builder baut die Szene heute aus einer Kopie der Cartoon-Szene. Künftig baut er sie aus einer schlanken Basis.
3. **`.jogroute` v1:** Datenklassen, JSON lesen und schreiben, Versionsfeld, Speicherordner.
4. **Profil-Generator:** Parameter plus Seed ergeben Höhen je 2 m, laufbar begrenzt. Unit-Test auf Determinismus und Höhenmeter.
5. **Weg-Generator:** Parameter plus Seed ergeben Linie (Strecke oder Rundkurs, ohne Selbstkreuzung). Test auf Schluss und Länge.
6. **Gelände folgt dem Profil:** Grundhöhe plus Wegbett. Entscheidung MapMagic oder eigener Generator nach einem kleinen Prototyp.
7. **„+Z“-Annahme entfernen:** Räumlicher Index in `TrailPath`, Kachel-Fertig-Prüfung ohne Z.
