using System.Collections.Generic;
using UnityEngine;
using Jogging.Profile;
using Jogging.Route;
using Jogging.Core;

namespace Jogging.World
{
    /// <summary>
    /// The playful side during a run (Profile/Game has the rules): times every climb segment of the route
    /// ("Bergwertung") – announced at its foot with the family record, at its top with your time against it –
    /// and notes the run's mood for the album when it starts (dawn, night, full moon, snow …).
    /// Lives next to the route (RouteRuntime), one per run.
    /// </summary>
    public class RunGame : MonoBehaviour
    {
        /// <summary>Best time of this run per climb (seconds; 0 = not run through) – into the logbook.</summary>
        public static readonly List<float> ClimbTimes = new List<float>();
        /// <summary>What happened on the climbs ("Anstieg 1: 2:35 – neuer Familienrekord") – finish screen.</summary>
        public static readonly List<string> ClimbResults = new List<string>();

        private List<ClimbSegments.Climb> climbs = new List<ClimbSegments.Climb>();
        private readonly Dictionary<int, float> enteredAt = new Dictionary<int, float>();
        private Dictionary<string, List<SessionSummary>> family;
        private Dictionary<string, string> names;
        private RunStats stats;
        private float lastPos = -1f;
        private bool moodsNoted;

        private void Awake() { ClimbTimes.Clear(); ClimbResults.Clear(); }

        private void Start()
        {
            stats = FindFirstObjectByType<RunStats>();
            var doc = RouteRuntime.Current;
            climbs = ClimbSegments.Of(doc);
            for (int i = 0; i < climbs.Count; i++) ClimbTimes.Add(0f);
            // the family's logbook as it was before this run (records to beat)
            family = new Dictionary<string, List<SessionSummary>>();
            names = new Dictionary<string, string>();
            var ps = ProfileService.Instance;
            if (ps != null) foreach (var r in ps.Runners) { family[r.id] = ps.SummariesOf(r.id); names[r.id] = r.playerName; }
            if (climbs.Count > 0) Debug.Log($"[Spiel] {climbs.Count} Bergwertungen: " + string.Join(", ", climbs.ConvertAll(c => $"{c.startM:0}–{c.endM:0} m ↑{c.gainM:0}")));
        }

        private void Update()
        {
            var sm = Jogging.UI.RunSessionUI.Session;
            var path = TrailPath.Active;
            if (sm == null || path == null || stats == null) return;
            if (!sm.Started) { lastPos = -1f; return; }
            if (!moodsNoted) { moodsNoted = true; NoteMoods(); }
            if (climbs.Count == 0) return;

            float L = path.Length, s = path.RunnerS;
            float pos = path.Loop && L > 0f ? Mathf.Repeat(s, L) : s;
            if (lastPos < 0f) { lastPos = pos; return; }
            float d = pos - lastPos;
            if (path.Loop && d < -L * 0.5f) d += L;            // over the start line of a loop
            if (d <= 0f || d > 60f) { lastPos = pos; return; }  // standing, or a jump (tests)
            float a = lastPos, b = lastPos + d;
            bool Crossed(float at) => (at > a && at <= b) || (path.Loop && at + L > a && at + L <= b);
            for (int i = 0; i < climbs.Count; i++)
            {
                var c = climbs[i];
                if (Crossed(c.startM)) { enteredAt[i] = stats.ElapsedSeconds; AtFoot(i, c); }
                else if (Crossed(c.endM) && enteredAt.TryGetValue(i, out float t0)) { enteredAt.Remove(i); AtTop(i, stats.ElapsedSeconds - t0); }
            }
            lastPos = pos;
        }

        private void NoteMoods()
        {
            var doc = RouteRuntime.Current;
            if (doc == null) return;
            double moon = doc.@params.moonAge >= 0f ? doc.@params.moonAge : Sky.MoonAge(System.DateTime.UtcNow);
            foreach (var m in Game.Moods(doc.@params, moon)) Wayside.NoteSight(m);
        }

        private Game.ClimbRecord? Record(int i)
        {
            var doc = RouteRuntime.Current;
            return doc != null ? Game.ClimbBest(family, doc.id, doc.revision, i) : null;
        }

        private string Who(string runnerId) => names.TryGetValue(runnerId, out var n) ? n : "?";

        private void AtFoot(int i, ClimbSegments.Climb c)
        {
            var rec = Record(i);
            var doc = RouteRuntime.Current;
            bool saved = doc != null && !string.IsNullOrEmpty(doc.id);
            string text = Jogging.Core.Loc.F("Bergwertung: {0} · {1}", ClimbSegments.Name(i), ClimbSegments.Describe(c));
            string said = Jogging.Core.Loc.F("Bergwertung. {0} Höhenmeter auf {1}.", Mathf.RoundToInt(c.gainM), Jogging.Training.Speech.Km(c.LengthM));
            if (rec != null)
            {
                text += Jogging.Core.Loc.F(" · Rekord {0} ({1})", Clock(rec.Value.seconds), Who(rec.Value.runnerId));
                said += " " + Jogging.Core.Loc.F("Rekord {0} von {1}.", Jogging.Training.Speech.Duration(rec.Value.seconds), Who(rec.Value.runnerId));
            }
            else if (saved) said += " " + Jogging.Core.Loc.T("Noch kein Rekord – setz die erste Zeit.");
            Jogging.UI.RaceMessages.Post("⛰  " + text);
            Jogging.UI.Announcer.Current?.Say(said);
        }

        private void AtTop(int i, float seconds)
        {
            if (seconds <= 0f) return;
            if (ClimbTimes[i] <= 0f || seconds < ClimbTimes[i]) ClimbTimes[i] = seconds;
            var rec = Record(i);
            var me = ProfileService.Instance != null ? ProfileService.Instance.Profile.id : "";
            string text, said;
            var doc = RouteRuntime.Current;
            if (doc == null || string.IsNullOrEmpty(doc.id))
            {
                text = Jogging.Core.Loc.F("{0}: {1}", ClimbSegments.Name(i), Clock(seconds));
                said = Jogging.Core.Loc.F("Oben. {0}.", Jogging.Training.Speech.Duration(seconds));
            }
            else if (rec == null || seconds < rec.Value.seconds)
            {
                bool mine = rec != null && rec.Value.runnerId == me;
                text = Jogging.Core.Loc.F("{0}: {1} – {2}", ClimbSegments.Name(i), Clock(seconds), Jogging.Core.Loc.T(mine ? "neue Bestzeit 👑" : "neuer Familienrekord 👑"));
                said = Jogging.Core.Loc.F("{0}! {1}.", Jogging.Core.Loc.T(mine ? "Neue Bestzeit am Berg" : "Neuer Familienrekord am Berg"), Jogging.Training.Speech.Duration(seconds));
            }
            else
            {
                float behind = seconds - rec.Value.seconds;
                text = Jogging.Core.Loc.F("{0}: {1} – {2:0} s hinter {3}", ClimbSegments.Name(i), Clock(seconds), behind, Who(rec.Value.runnerId));
                said = Jogging.Core.Loc.F("Oben. {0}, {1} Sekunden hinter {2}.", Jogging.Training.Speech.Duration(seconds), Mathf.RoundToInt(behind), Who(rec.Value.runnerId));
            }
            ClimbResults.RemoveAll(x => x.StartsWith(ClimbSegments.Name(i) + ":"));
            ClimbResults.Add(text);
            Jogging.UI.RaceMessages.Post("⛰  " + text);
            Jogging.UI.Announcer.Current?.Say(said);
        }

        public static string Clock(float seconds)
        {
            int t = Mathf.RoundToInt(seconds);
            return t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60}:{t % 60:00}";
        }
    }
}
