# Testplan: erster Lauf mit F37 und Pulsgurt

> **Stand 26.09.2026 (erster Test am F37, ohne Pulsgurt):** A1, A3, B1–B7 (B3 nach Korrektur der Bridge) und
> E1 bestanden; A2/B8/C/D (Pulsgurt, Tempo-Workout) noch offen.
> Gefunden und behoben: Befehle wurden vom F37 verworfen (Bridge schrieb „mit Antwort“ und fragte gleich
> weiter ab), der F37 nimmt die Steigung nur in 2-%-Schritten, er meldet das Zieltempo statt des echten
> Tempos (Anlaufen/Bremsen jetzt nachgebildet), eine alte Bridge lief weiter, der Steigungsbereich kam nicht
> immer an. Nächster Lauf mit `-diag`: die Zeilen `[Band-Weg]` zeigen, ob 1 km/h pro s zum F37 passt.

Dauer: etwa 15 Minuten. Alles, was die App ans Band schickt, steht mit Uhrzeit im Log
(`~/Library/Logs/DefaultCompany/Jogging/Player.log`). Nach dem Test in Claude einfach **„prüf das Log“**
sagen – dann wird jeder Punkt unten gegen das Log geprüft.

Vorher: F37 einschalten (nicht im Standby), Sicherheitsclip stecken, Pulsgurt anfeuchten und anlegen.
Die App starten.

## A. Verbinden (Start, 1 min)

- [ ] **A1** Nach dem Start oben rechts: grüner Punkt, „Band … km/h“. (Log: `Gerät verbunden: Laufband „FS-2ED3BC“`, `Laufband gemerkt`)
- [ ] **A2** Einstellungen (auf „Wer läuft?“) → Pulsgurt zeigt „Puls … · Zone …“ und den Namen des Gurts. (Log: `Pulsgurt „…“ gehört jetzt …`)
- [ ] **A3** App beenden und neu starten: Band und Gurt verbinden sich wieder von selbst.

## B. Quick Run (5 min)

- [ ] **B1** Quick Run → „Starte das Laufband“ → am Band Start. Der Lauf beginnt nach dem Countdown des Bands.
- [ ] **B2** Bei 6–8 km/h laufen. Das Tempo stellst **du** ein – die App ändert es nicht. (Log: jede Zeile `→ Band: … Tempo x→x km/h` mit gleichem Wert)
- [ ] **B3** Kommt ein Anstieg (Steigung im HUD > 1 %): das Band neigt sich in 1-%-Schritten mit. (Log: `→ Band: Steigung 0→1 %`, `1→2 %` …)
- [ ] **B4** Am Band die Steigung **von Hand** um 2–3 % erhöhen. Die App hält sich 30 s zurück. (Log: `Steigung von Hand verstellt … hält sich 30 s zurück`, ~30 s später `Zurückhalten vorbei`)
- [ ] **B5** Am Band **Stopp**: Pause „Band gestoppt“. Wieder **Start**: der Lauf geht weiter.
- [ ] **B6** Sicherheitsclip kurz ziehen: Pause „Sicherheitsschlüssel fehlt“. Clip stecken, Start, weiter.
- [ ] **B7** Auf einem Anstieg (Steigung am Band ≥ 2 %): **Esc → Beenden & speichern**, weitergehen. Die Steigung fährt in 1-%-Schritten auf 0 % zurück, das Tempo bleibt. (HUD: „Steigung zurück → 0 %“; Log: `→ Band: Steigung 3→2 %` … `1→0 %`)
- [ ] **B8** Ziel-Screen zeigt Ø Puls. **Fertig** → Home: „Letzter Lauf“ mit Puls.

## C. Workout mit Tempo vom Band (optional, 5 min)

Nur wenn du das willst – standardmäßig bleibt das Tempo bei dir.

- [ ] **C1** Einstellungen → „Tempo (Workouts)“ auf **„Workout stellt das Tempo“**.
- [ ] **C2** Workouts → **Anpassen** bei „Intervalle 30 min“: Aufwärmen auf 1:00, ersten Schnell-Abschnitt auf 1:00, speichern, **Starten**.
- [ ] **C3** Beim Wechsel auf „Schnell“ steigt das Tempo in 0,5-km/h-Schritten. Höchstens 16 km/h. (Log: `Tempo 7.0→7.5`, …)
- [ ] **C4** Am Band Tempo **−** drücken: die App hält sich 30 s zurück. (Log: `Tempo von Hand verstellt`)
- [ ] **C4b** **H** drücken: oben über den großen Werten der Workout-Streifen – Restzeit aus 2–3 m lesbar? Die letzten 5 s orange.
- [ ] **C5** Workout beenden, danach „Tempo (Workouts)“ wieder auf **„nur als Hinweis“**.

## D. Zwei Läufer mit zwei Gurten (optional)

- [ ] **D1** Zweiten Läufer anlegen, dessen Gurt anlegen: in **seinem Profil** → Pulsgurt → **Anderer Gurt**. Der Gurt wird ihm zugeordnet.
- [ ] **D2** Läufer wechseln: die App verbindet jeweils den eigenen Gurt; ein fremder Gurt wird nicht aufgezeichnet („Gurt gehört …“).

## E. Handsensoren (1 min, ohne Pulsgurt)

- [ ] **E1** Pulsgurt ablegen (oder unter Einstellungen → Pulsgurt **Trennen**), Quick Run, beide Hände an die Griffsensoren.
      Nach einigen Sekunden oben rechts „Puls … (Hand)“? (App dafür mit `-diag` starten: dann steht alle 5 s eine Zeile `[Band-Rohdaten]` im Log – daran sehe ich,
      an welcher Stelle der F37 den Handpuls meldet; die App liest ihn im Moment aus Byte 13 der Statusmeldung.)
- [ ] **E2** Mit angelegtem Gurt: es zählt der Gurt, nicht die Griffe (kein „(Hand)“).

## Wenn etwas anders ist

- Das Band ändert **das Tempo**, obwohl C1 aus ist → sofort Stopp am Band, Punkt notieren.
- Das Band **startet** von selbst → Stopp, Clip ziehen, Punkt notieren. (Darf nie passieren – im Simulator-Test ausgeschlossen.)
- Steigung folgt nicht → Einstellungen: „Steuerung: Strecke stellt die Steigung“? Nach Handverstellung 30 s gewartet?
