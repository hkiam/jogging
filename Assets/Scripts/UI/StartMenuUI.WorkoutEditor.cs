using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Jogging.Training;

namespace Jogging.UI
{
    /// <summary>
    /// Workout editor: name and segments (kind, time or distance, target speed, own incline or the
    /// route's, heart rate zone), reorder / duplicate / remove, and a live profile of the whole workout.
    /// Edits a copy; "Speichern" writes a .jogworkout (a built-in is saved as an own copy).
    /// </summary>
    public partial class StartMenuUI
    {
        private static readonly string[] Kinds = { "warmup", "run", "walk", "fast", "recovery", "climb", "cooldown" };
        private const int RowsPerPage = 8;

        private WorkoutDoc editing;
        private int editPage;

        private void EditWorkout(WorkoutDoc source)
        {
            editing = source != null ? source.Clone() : new WorkoutDoc { name = "Mein Workout" };
            if (source != null && source.BuiltIn) { editing.id = ""; editing.path = null; editing.name = Jogging.Core.Loc.T(source.name) + Jogging.Core.Loc.T(" (eigene)"); }
            if (editing.segments.Count == 0)
            {
                editing.segments.Add(new WorkoutSegment { kind = "warmup", durationS = 300f, speedKmh = 7f });
                editing.segments.Add(new WorkoutSegment { kind = "run", durationS = 1200f, speedKmh = 9f });
                editing.segments.Add(new WorkoutSegment { kind = "cooldown", durationS = 300f, speedKmh = 6.5f });
            }
            editPage = 0;
            ShowWorkoutEditor();
        }

        private void ShowWorkoutEditor()
        {
            const float w = 1320f;
            var p = NewPage(w, 900f);
            var d = editing;
            UiControls.Label(p, "Workout", 34, 40f, -24f, 300f, 48f, UiTheme.TextPrimary, bold: true);
            var name = UiControls.TextField(p, d.name, 240f, -26f, 520f);
            name.onValueChanged.AddListener(v => d.name = v);
            var sum = UiControls.Label(p, "", 18, 780f, -24f, 500f, 48f, UiTheme.TextMuted, TextAnchor.MiddleRight);
            Transform chart = null;
            Action refresh = () =>
            {
                if (chart != null)
                {
                    for (int i = chart.childCount - 1; i >= 0; i--) Destroy(chart.GetChild(i).gameObject);
                    Profile(chart, d, 16f, -8f, 1208f, 60f);
                }
                sum.text = Jogging.Core.Loc.F("~{0:0} min · ~{1} · {2} Abschnitte", d.Minutes, Jogging.Core.Units.FmtDist(d.EstimatedMeters), d.segments.Count);
            };

            // Column headers
            float y = -92f;
            string[] heads = { "ART", "DAUER / STRECKE", "ZIELTEMPO", "STEIGUNG", "PULSZONE", "" };
            float[] xs = { 40f, 250f, 560f, 790f, 990f, 1160f };
            for (int i = 0; i < heads.Length; i++) UiControls.Label(p, heads[i], 15, xs[i], y, 220f, 22f, UiTheme.TextMuted, bold: true);
            y -= 28f;

            int pages = Mathf.Max(1, Mathf.CeilToInt(d.segments.Count / (float)RowsPerPage));
            editPage = Mathf.Clamp(editPage, 0, pages - 1);
            for (int k = editPage * RowsPerPage; k < Mathf.Min(d.segments.Count, (editPage + 1) * RowsPerPage); k++)
            {
                var s = d.segments[k];
                int idx = k;
                var row = UiTheme.Panel(p, UiTheme.PanelSoft);
                UiControls.Place(row.GetComponent<RectTransform>(), 40f, y, 1240f, 60f);
                row.GetComponent<Image>().color = Color.Lerp(UiTheme.PanelSoft, WorkoutSegment.KindColor(s.kind), 0.18f);
                var t = row.transform;

                // Kind (click = next)
                Button kb = null;
                kb = UiControls.Button(t, WorkoutSegment.KindName(s.kind), () =>
                {
                    s.kind = Kinds[(Array.IndexOf(Kinds, s.kind) + 1) % Kinds.Length];
                    kb.GetComponentInChildren<Text>().text = WorkoutSegment.KindName(s.kind);
                    row.GetComponent<Image>().color = Color.Lerp(UiTheme.PanelSoft, WorkoutSegment.KindColor(s.kind), 0.18f);
                    refresh();
                }, 8f, -8f, 190f, 44f, null, 19);

                // Time or distance: the unit button switches, the stepper changes the amount.
                Text amount = null;
                Func<string> amountText = () => s.ByDistance ? Jogging.Core.Units.FmtDistAuto(s.distanceM) : $"{Mathf.RoundToInt(s.durationS) / 60}:{Mathf.RoundToInt(s.durationS) % 60:00} min";
                amount = UiControls.Stepper(t, "", amountText,
                    () => { StepAmount(s, -1); refresh(); }, () => { StepAmount(s, +1); refresh(); }, 208f, -8f, 228f, 0f);
                Button ub = null;
                ub = UiControls.Button(t, s.ByDistance ? "km" : "min", () =>
                {
                    if (s.ByDistance) { s.durationS = Mathf.Max(60f, Mathf.Round(s.EstimatedSeconds / 60f) * 60f); s.distanceM = 0f; }
                    else { s.distanceM = Mathf.Max(100f, Mathf.Round(s.EstimatedSeconds * (s.speedKmh > 0f ? s.speedKmh : 8f) / 3.6f / 100f) * 100f); s.durationS = 0f; }
                    ub.GetComponentInChildren<Text>().text = s.ByDistance ? "km" : "min";
                    amount.text = amountText(); refresh();
                }, 442f, -8f, 66f, 44f, null, 17);

                // Target speed (0 = free)
                UiControls.Stepper(t, "", () => s.speedKmh > 0f ? Jogging.Core.Units.FmtSpeed(s.speedKmh) : "frei",
                    () => { s.speedKmh = s.speedKmh <= 5f ? 0f : s.speedKmh - Jogging.Core.Units.SpeedToKmh(0.5f); refresh(); },
                    () => { s.speedKmh = s.speedKmh <= 0f ? Jogging.Core.Units.SpeedToKmh(Jogging.Core.Units.Imperial ? 4f : 6f) : Mathf.Min(20f, s.speedKmh + Jogging.Core.Units.SpeedToKmh(0.5f)); refresh(); }, 520f, -8f, 230f, 0f);

                // Incline: the route's, or its own 0–12 %
                UiControls.Stepper(t, "", () => s.setIncline ? $"{s.inclinePercent:0} %" : "Strecke",
                    () => { if (!s.setIncline) return; if (s.inclinePercent <= 0f) s.setIncline = false; else s.inclinePercent -= 1f; },
                    () => { if (!s.setIncline) { s.setIncline = true; s.inclinePercent = 0f; } else s.inclinePercent = Mathf.Min(12f, s.inclinePercent + 1f); },
                    750f, -8f, 190f, 0f);

                // Heart rate zone (0 = none)
                UiControls.Stepper(t, "", () => s.hrZone > 0 ? Jogging.Core.Loc.F("Zone {0}", s.hrZone) : "–",
                    () => s.hrZone = Mathf.Max(0, s.hrZone - 1), () => s.hrZone = Mathf.Min(5, s.hrZone + 1), 950f, -8f, 170f, 0f);

                // Up / duplicate / remove
                UiControls.Button(t, "↑", () => { if (idx > 0) { (d.segments[idx - 1], d.segments[idx]) = (d.segments[idx], d.segments[idx - 1]); ShowWorkoutEditor(); } }, 1128f, -8f, 34f, 44f, null, 18);
                UiControls.Button(t, "2×", () => { d.segments.Insert(idx + 1, JsonUtility.FromJson<WorkoutSegment>(JsonUtility.ToJson(s))); ShowWorkoutEditor(); }, 1164f, -8f, 36f, 44f, null, 15);
                UiControls.Button(t, "X", () => { if (d.segments.Count > 1) { d.segments.RemoveAt(idx); ShowWorkoutEditor(); } }, 1202f, -8f, 32f, 44f, UiTheme.Danger, 16);
                y -= 66f;
            }

            if (pages > 1)
            {
                UiControls.Button(p, "◀", () => { editPage--; ShowWorkoutEditor(); }, 40f, y - 4f, 60f, 44f);
                UiControls.Label(p, $"{editPage + 1} / {pages}", 18, 104f, y - 4f, 90f, 44f, UiTheme.TextMuted, TextAnchor.MiddleCenter);
                UiControls.Button(p, "▶", () => { editPage++; ShowWorkoutEditor(); }, 198f, y - 4f, 60f, 44f);
            }
            UiControls.Button(p, "+ Abschnitt", () =>
            {
                var last = d.segments[d.segments.Count - 1];
                d.segments.Add(new WorkoutSegment { kind = "run", durationS = 300f, speedKmh = last.speedKmh > 0f ? last.speedKmh : 8f });
                editPage = int.MaxValue; // jump to the last page
                ShowWorkoutEditor();
            }, 280f, y - 4f, 220f, 44f, UiTheme.Accent);

            // Profile of the whole workout
            var box = UiTheme.Panel(p, UiTheme.PanelSoft);
            UiControls.Place(box.GetComponent<RectTransform>(), 40f, -720f, 1240f, 76f);
            chart = box.transform;

            UiControls.Button(p, "Speichern", () =>
            {
                if (string.IsNullOrWhiteSpace(d.name)) d.name = "Mein Workout";
                WorkoutStore.Save(d);
                Debug.Log($"[Jogging] Workout gespeichert: „{d.name}“ → {d.path}");
                ShowWorkouts();
            }, 40f, -816f, 260f, 56f, UiTheme.Success, 22);
            UiControls.Button(p, Jogging.Core.Loc.T("Abbrechen"), () => ShowWorkouts(), 316f, -816f, 220f, 56f);
            if (!string.IsNullOrEmpty(d.path))
            {
                Text del = null; bool armed = false;
                var orig = d;
                del = UiControls.Button(p, "Workout löschen", () =>
                {
                    if (!armed) { armed = true; del.text = Jogging.Core.Loc.T("Wirklich löschen?"); return; }
                    WorkoutStore.Delete(orig);
                    ShowWorkouts();
                }, 1000f, -816f, 280f, 56f, UiTheme.Danger, 20).GetComponentInChildren<Text>();
            }
            refresh();
        }

        // Time in 30 s steps (1 min above 10 min), distance in 100 m steps (500 m above 2 km).
        private static void StepAmount(WorkoutSegment s, int dir)
        {
            if (s.ByDistance)
            {
                float step = s.distanceM >= 2000f ? 500f : 100f;
                s.distanceM = Mathf.Clamp(s.distanceM + dir * step, 100f, 42200f);
            }
            else
            {
                float step = s.durationS >= 600f ? 60f : 30f;
                s.durationS = Mathf.Clamp(s.durationS + dir * step, 30f, 7200f);
            }
        }
    }
}
