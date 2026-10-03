using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Jogging.Profile;
using Jogging.Training;
using Jogging.World;

namespace Jogging.UI
{
    /// <summary>
    /// The active runner's profile: name, figure (previewed on the player behind the menu), birth year
    /// and weight (→ max heart rate estimate and calories), max heart rate, weekly goals — and deleting
    /// the runner with the whole logbook (confirmed by a second click).
    /// </summary>
    public partial class StartMenuUI
    {
        private void ShowProfile()
        {
            const float w = 1000f;
            var p = NewPage(w, 1040f);
            SetDim(RunnerDim);
            var ps = ProfileService.Instance;
            if (ps == null || !ps.HasRunners) { ShowEntry(); return; }
            var me = ps.Profile;
            int year = DateTime.Now.Year;
            UiControls.Label(p, "Profil", 40, 50f, -24f, 600f, 60f, UiTheme.TextPrimary, bold: true);

            // Name
            UiControls.Label(p, "Name", 20, 50f, -104f, 260f, 44f, UiTheme.TextMuted);
            var name = UiControls.TextField(p, me.playerName, 310f, -104f, 380f);

            // Figure
            var player = FindFirstObjectByType<RealPlayerFigure>();
            var choices = player != null ? player.Choices : new GameObject[0];
            int fig = 0;
            for (int i = 0; i < choices.Length; i++) if (choices[i] != null && choices[i].name == me.figureModel) fig = i;
            string figure = me.figureModel;
            Func<string> figText = () => choices.Length == 0 ? "–" : $"{FigureKind(choices[fig].name)} {fig + 1} / {choices.Length}";
            Action<int> figStep = d =>
            {
                if (choices.Length == 0) return;
                fig = (fig + d + choices.Length) % choices.Length;
                figure = choices[fig].name;
                player.Show(figure); // live preview
            };
            UiControls.Stepper(p, Jogging.Core.Loc.T("Figur"), figText, () => figStep(-1), () => figStep(+1), 50f, -164f, 640f);

            // Body (for the heart rate estimate and calories)
            int birth = me.birthYear; float weight = me.weightKg; int maxHr = me.maxHeartRate;
            Text hrVal = null; Func<string> hrText = null; // the max heart rate estimate follows the birth year
            UiControls.Stepper(p, Jogging.Core.Loc.T("Geburtsjahr"), () => birth > 0 ? Jogging.Core.Loc.F("{0}  ({1} J.)", birth, year - birth) : "nicht angegeben",
                () => { birth = birth <= 0 ? year - 30 : Mathf.Max(1920, birth - 1); if (hrVal != null) hrVal.text = hrText(); },
                () => { birth = birth <= 0 ? year - 30 : birth + 1 > year - 5 ? 0 : birth + 1; if (hrVal != null) hrVal.text = hrText(); }, 50f, -234f, 640f);
            UiControls.Stepper(p, Jogging.Core.Loc.T("Gewicht"), () => weight > 0f ? Jogging.Core.Units.FmtWeight(weight) : Jogging.Core.Loc.T("nicht angegeben"),
                () => weight = weight <= 0f ? 70f : weight - Jogging.Core.Units.WeightToKg(1f) < 20f ? 0f : weight - Jogging.Core.Units.WeightToKg(1f),
                () => weight = weight <= 0f ? 70f : Mathf.Min(200f, weight + Jogging.Core.Units.WeightToKg(1f)), 50f, -294f, 640f);
            hrText = () =>
            {
                if (maxHr > 0) return Jogging.Core.Loc.F("{0} (eingestellt)", maxHr);
                int est = HeartRateZones.FromAge(birth, year);
                return est > 0 ? Jogging.Core.Loc.F("{0} (aus dem Alter)", est) : Jogging.Core.Loc.F("{0} (Standard)", HeartRateZones.DefaultMax);
            };
            int Current() => HeartRateZones.ForRunner(maxHr, birth, year);
            hrVal = UiControls.Stepper(p, "Max. Puls", hrText,
                () => maxHr = Mathf.Clamp(Current() - 1, 120, 230),
                () => maxHr = Mathf.Clamp(Current() + 1, 120, 230), 50f, -354f, 640f);
            UiControls.Button(p, Jogging.Core.Loc.T("Aus dem Alter"), () => { maxHr = 0; hrVal.text = hrText(); }, 720f, -352f, 230f, 42f);
            UiControls.Label(p, "Gewicht → Kalorien im Logbuch. Geburtsjahr → max. Puls nach Tanaka (208 − 0,7 × Alter), wenn keiner eingestellt ist.",
                16, 50f, -404f, w - 100f, 28f, UiTheme.TextMuted);

            // Weekly goals
            int gRuns = me.weekGoalRuns > 0 ? me.weekGoalRuns : Challenges.DefaultRuns;
            float gKm = me.weekGoalKm > 0f ? me.weekGoalKm : Challenges.DefaultKm;
            int gMin = me.weekGoalMinutes > 0 ? me.weekGoalMinutes : Challenges.DefaultMinutes;
            UiControls.Label(p, "WOCHENZIELE", 16, 50f, -448f, 300f, 24f, UiTheme.TextMuted, bold: true);
            UiControls.Stepper(p, Jogging.Core.Loc.T("Läufe pro Woche"), () => $"{gRuns}", () => gRuns = Mathf.Max(1, gRuns - 1), () => gRuns = Mathf.Min(14, gRuns + 1), 50f, -480f, 640f);
            UiControls.Stepper(p, Jogging.Core.Loc.T(Jogging.Core.Units.Imperial ? "Meilen pro Woche" : "Kilometer pro Woche"), () => Jogging.Core.Units.FmtDist(gKm * 1000f, "0"),
                () => gKm = Mathf.Max(Jogging.Core.Units.RoundKm(1f, 1f), Jogging.Core.Units.RoundKm(gKm, 1f) - Jogging.Core.Units.RoundKm(gKm > 20f ? 5f : 1f, 1f)),
                () => gKm = Mathf.Min(300f, Jogging.Core.Units.RoundKm(gKm, 1f) + Jogging.Core.Units.RoundKm(gKm >= 20f ? 5f : 1f, 1f)), 50f, -540f, 640f);
            UiControls.Stepper(p, Jogging.Core.Loc.T("Minuten pro Woche"), () => $"{gMin} min", () => gMin = Mathf.Max(10, gMin - 10), () => gMin = Mathf.Min(1200, gMin + 10), 50f, -600f, 640f);

            bool ghostOn = !me.ghostOff;
            UiControls.Toggle(p, Jogging.Core.Loc.T("Geist-Läufer"), Jogging.Core.Loc.T("An – dein bester Lauf läuft mit"), Jogging.Core.Loc.T("Aus"), ghostOn, v => ghostOn = v, 50f, -662f, 640f);

            // Pulse coach: off, only in workouts (their segment zones), or a target zone for free runs too
            int coach = Mathf.Clamp(me.pulseCoach, -1, 5);
            string CoachText() => coach < 0 ? Jogging.Core.Loc.T("aus") : coach == 0 ? Jogging.Core.Loc.T("nur in Workouts") : Jogging.Core.Loc.F("immer · Ziel Zone {0}", coach);
            UiControls.Stepper(p, Jogging.Core.Loc.T("Pulscoach"), CoachText, () => coach = Mathf.Max(-1, coach - 1), () => coach = Mathf.Min(5, coach + 1), 50f, -718f, 640f);
            // Incline by heart rate: the app moves the incline to keep that zone (when it sets the incline)
            bool pulseIncline = me.pulseIncline;
            UiControls.Toggle(p, Jogging.Core.Loc.T("Steigung nach Puls"), Jogging.Core.Loc.T("An"), Jogging.Core.Loc.T("Aus"), pulseIncline, v => pulseIncline = v, 50f, -774f, 640f);
            UiControls.Label(p, "hält dich in der Zielzone: die App stellt die Steigung nach (wenn sie die Steigung stellt)", 16, 720f, -772f, w - 770f, 48f, UiTheme.TextMuted).horizontalOverflow = HorizontalWrapMode.Wrap;
            UiControls.Label(p, "sagt Bescheid, wenn der Puls länger aus der Zielzone ist", 16, 720f, -716f, w - 770f, 44f, UiTheme.TextMuted).horizontalOverflow = HorizontalWrapMode.Wrap;

            // Companion dog (only breeds whose model is in this build; World/DogCompanion)
            var breeds = new List<(string id, string name)> { ("", "kein Hund") };
            foreach (var b in Jogging.World.DogCompanion.Breeds) if (Jogging.World.DogCompanion.Available(b.id)) breeds.Add(b);
            string dog = breeds.Exists(b => b.id == me.dog) ? me.dog : "";
            InputField dogName = null;
            if (breeds.Count > 1)
            {
                Cycle(p, "Begleithund", breeds.ConvertAll(b => b.id).ToArray(), breeds.ConvertAll(b => b.name).ToArray(), dog, v => dog = v, 50f, -830f, 640f);
                dogName = UiControls.TextField(p, string.IsNullOrWhiteSpace(me.dogName) ? Jogging.Core.Loc.T("Bello") : me.dogName, 720f, -830f, w - 770f);
            }

            // The runner's own heart rate strap (paired on first connect; "Anderer Gurt" takes the next free one)
            UiControls.Label(p, "Pulsgurt", 20, 50f, -886f, 210f, 44f, UiTheme.TextMuted);
            UiControls.Label(p, string.IsNullOrEmpty(me.hrDeviceName) && string.IsNullOrEmpty(me.hrDeviceId) ? "noch keiner – der erste, der sich verbindet"
                               : (string.IsNullOrEmpty(me.hrDeviceName) ? "Pulsgurt" : me.hrDeviceName), 20, 260f, -886f, 400f, 44f, UiTheme.TextPrimary);
            UiControls.Button(p, Jogging.Core.Loc.T("Anderer Gurt"), () => { PairOtherStrap(FindFirstObjectByType<Jogging.Locomotion.Treadmill.MacBleBridgeTransport>()); ShowProfile(); },
                                 w - 290f, -884f, 240f, 44f, null, 19);

            UiControls.Button(p, "Speichern", () =>
            {
                bool figChanged = figure != me.figureModel;
                me.playerName = string.IsNullOrWhiteSpace(name.text) ? me.playerName : name.text.Trim();
                me.figureModel = figure ?? "";
                me.birthYear = birth; me.weightKg = weight; me.maxHeartRate = maxHr;
                me.weekGoalRuns = gRuns == Challenges.DefaultRuns ? 0 : gRuns;
                me.weekGoalKm = Mathf.Approximately(gKm, Challenges.DefaultKm) ? 0f : gKm;
                me.weekGoalMinutes = gMin == Challenges.DefaultMinutes ? 0 : gMin;
                me.ghostOff = !ghostOn;
                me.pulseCoach = coach;
                me.pulseIncline = pulseIncline;
                me.dog = dog;
                if (dogName != null) me.dogName = string.IsNullOrWhiteSpace(dogName.text) ? "" : dogName.text.Trim();
                ps.SaveActive(figChanged);
                Jogging.World.DogCompanion.ForActiveRunner(); // on, off, other name: right away
                ShowHome();
            }, 50f, -950f, 300f, 60f, UiTheme.Success, 24);
            UiControls.Button(p, Jogging.Core.Loc.T("Abbrechen"), () => { if (player != null) player.Show(me.figureModel); ShowHome(); }, 370f, -950f, 260f, 60f);

            // Delete (second click confirms)
            Text del = null;
            bool armed = false;
            del = UiControls.Button(p, "Läufer löschen", () =>
            {
                if (!armed) { armed = true; del.text = Jogging.Core.Loc.F("Wirklich {0} mit allen Läufen löschen?", me.playerName); return; }
                ps.DeleteRunner(me.id);
                ShowEntry();
            }, 650f, -954f, w - 700f, 52f, UiTheme.Danger, 16).GetComponentInChildren<Text>();
        }
    }
}
