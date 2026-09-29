using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Jogging.Route;
using Jogging.Training;
using Jogging.World;

namespace Jogging.UI
{
    /// <summary>
    /// Workouts page: pick a route (where) and a workout (how) — every workout runs on every route.
    /// Built-ins plus own <c>.jogworkout</c> files; each row shows its segments as a small profile
    /// (width = time, height = target speed, colour = kind).
    /// </summary>
    public partial class StartMenuUI
    {
        private static string workoutRoute = "preset:frei"; // remembered for the session

        private void ShowWorkouts()
        {
            const float w = 1320f;
            var p = NewPage(w, 900f);
            UiControls.Label(p, "Workouts", 34, 40f, -24f, 600f, 48f, UiTheme.TextPrimary, bold: true);

            // Where: presets + saved routes.
            var values = new List<string> { "preset:frei", "preset:wald", "preset:huegelig", "preset:flach", "preset:see", "preset:pass", "preset:wellen", "preset:winterwald", "preset:mondnacht" };
            string P(string name, float km) => Jogging.Core.Loc.T(name) + " · " + Jogging.Core.Units.FmtDist(km * 1000f, "0.#");
            var labels = new List<string> { Jogging.Core.Loc.T("Freies Laufen (endlos)"), P("Waldrunde", 10f), P("Hügelrunde", 8f), P("Flach & schnell", 5f),
                                            P("Seerunde", 7f), P("Bergpass", 8f), P("Wellen", 6f), P("Winterwald", 6f), P("Mondnacht", 6f) };
            float openLengthM = 0f; // selected route is an open course of this length (0 = loop/endless: laps)
            foreach (var r in RouteStore.LoadAll())
            {
                if ("id:" + r.id == workoutRoute && !r.@params.loop && !r.@params.endless) openLengthM = r.@params.lengthKm * 1000f;
                values.Add("id:" + r.id);
                labels.Add($"{Short(r.meta.name, 22)} · {(r.@params.endless ? Jogging.Core.Loc.T("endlos") : Jogging.Core.Units.FmtDist(r.@params.lengthKm * 1000f, "0.#"))}");
            }
            if (!values.Contains(workoutRoute)) workoutRoute = values[0];
            Cycle(p, Jogging.Core.Loc.T("Strecke"), values.ToArray(), labels.ToArray(), workoutRoute, v => { workoutRoute = v; ShowWorkouts(); }, 700f, -28f, 580f); // redraw: the length hints depend on the route

            var all = WorkoutStore.LoadAll();
            const int perPage = 7;
            int pages = Mathf.Max(1, Mathf.CeilToInt(all.Count / (float)perPage));
            workoutPage = Mathf.Clamp(workoutPage, 0, pages - 1);
            float y = -96f;
            for (int k = workoutPage * perPage; k < Mathf.Min(all.Count, (workoutPage + 1) * perPage); k++)
            {
                var wd = all[k];
                var row = UiTheme.Panel(p, UiTheme.PanelSoft);
                UiControls.Place(row.GetComponent<RectTransform>(), 40f, y, 1240f, 88f);
                UiControls.Label(row.transform, wd.name, 24, 20f, -8f, 500f, 36f, UiTheme.TextPrimary, bold: true);
                bool tooShort = openLengthM > 0f && wd.EstimatedMeters > openLengthM;
                UiControls.Label(row.transform, tooShort
                        ? Jogging.Core.Loc.F("~{0:0.0} km · Strecke nur {1:0.0} km – endet am Ziel", wd.EstimatedMeters / 1000f, openLengthM / 1000f)
                        : Jogging.Core.Loc.F("~{0:0} min · {1} Abschnitte", wd.Minutes, wd.segments.Count) + (wd.description != "" ? " · " + Jogging.Core.Loc.T(wd.description) : ""),
                    17, 20f, -48f, 520f, 30f, tooShort ? new Color(1f, 0.78f, 0.35f) : UiTheme.TextMuted);
                Profile(row.transform, wd, 560f, -14f, 330f, 60f);
                var doc = wd;
                UiControls.Button(row.transform, wd.BuiltIn ? Jogging.Core.Loc.T("Anpassen") : Jogging.Core.Loc.T("Bearbeiten"), () => EditWorkout(doc), 910f, -18f, 150f, 52f, null, 20);
                UiControls.Button(row.transform, Jogging.Core.Loc.T("▶  Starten"), () => StartWorkout(doc), 1070f, -18f, 150f, 52f, UiTheme.Success, 20);
                y -= 100f;
            }

            if (pages > 1)
            {
                UiControls.Button(p, "◀", () => { workoutPage--; ShowWorkouts(); }, 40f, -790f, 70f, 56f);
                UiControls.Label(p, $"{workoutPage + 1} / {pages}", 20, 116f, -790f, 100f, 56f, UiTheme.TextMuted, TextAnchor.MiddleCenter);
                UiControls.Button(p, "▶", () => { workoutPage++; ShowWorkouts(); }, 222f, -790f, 70f, 56f);
            }
            UiControls.Button(p, Jogging.Core.Loc.T("+  Neues Workout"), () => EditWorkout(null), 320f, -790f, 280f, 56f, UiTheme.Accent);
            UiControls.Button(p, Jogging.Core.Loc.T("Pläne"), () => ShowPlans(), 616f, -790f, 128f, 56f);
            if (!Jogging.Core.Platform.IsMobile)
            UiControls.Button(p, Jogging.Core.Loc.T("Ordner öffnen"), () => Application.OpenURL("file://" + WorkoutStore.Folder), 760f, -790f, 220f, 56f);
            UiControls.Button(p, Jogging.Core.Loc.T("Zurück"), () => ShowHome(), 1000f, -790f, 280f, 56f);
        }

        private static int workoutPage;

        private void StartWorkout(WorkoutDoc doc)
        {
            RouteDoc route = null;
            if (workoutRoute.StartsWith("id:"))
            {
                string id = workoutRoute.Substring(3);
                foreach (var r in RouteStore.LoadAll()) if (r.id == id) { route = r; break; }
            }
            if (route == null)
            {
                string preset = workoutRoute.StartsWith("preset:") ? workoutRoute.Substring(7) : "frei";
                var cur = RouteRuntime.Current;
                bool fresh = RoutePresets.IsUnsavedFreeRun(cur) && (RunSessionUI.Session == null || !RunSessionUI.Session.Started);
                route = preset != "frei" ? RoutePresets.Create(preset, 7) : fresh ? cur : RoutePresets.NewFreeRun();
            }
            WorkoutRuntime.Selected = doc;
            Run(route);
        }

        // Segment blocks: width ∝ time, height ∝ target speed (hill workouts: incline), colour by kind.
        private static void Profile(Transform parent, WorkoutDoc doc, float x, float y, float w, float h)
        {
            float total = 0f, maxKmh = 1f;
            bool byIncline = false; // hill workouts: all blocks by incline
            foreach (var s in doc.segments) { total += s.EstimatedSeconds; maxKmh = Mathf.Max(maxKmh, s.speedKmh); byIncline |= s.setIncline; }
            if (total <= 0f) return;
            float cx = x;
            foreach (var s in doc.segments)
            {
                float bw = Mathf.Max(2f, w * s.EstimatedSeconds / total - 2f);
                float f = byIncline ? 0.2f + 0.8f * Mathf.Clamp01(s.inclinePercent / 8f)
                        : s.speedKmh > 0f ? 0.25f + 0.75f * s.speedKmh / maxKmh
                        : s.hrZone > 0 ? 0.2f * s.hrZone : 0.5f;
                var b = new GameObject("seg", typeof(Image));
                b.transform.SetParent(parent, false);
                b.GetComponent<Image>().color = WorkoutSegment.KindColor(s.kind);
                var rt = b.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(cx, y - h * (1f - f));
                rt.sizeDelta = new Vector2(bw, h * f);
                cx += bw + 2f;
            }
        }
    }
}
