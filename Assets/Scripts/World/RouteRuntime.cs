using UnityEngine;
using Jogging.Route;

namespace Jogging.World
{
    /// <summary>
    /// Sets up the course for this scene load: takes the selected route (set by the menu before it
    /// reloads the scene), else a route from the command line (-routefile path | -preset name
    /// [-seed n]; tests: -starts metres), else a free run; generates line + profile and hands them to the trail
    /// (<see cref="TrailPath"/>), the terrain shaping (<see cref="TrailShaper"/>) and the lap logic
    /// (<see cref="TrackManager"/>). Runs before everything else so the trail exists on Awake.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class RouteRuntime : MonoBehaviour
    {
        /// <summary>Route to run on the next scene load (the menu sets it, then reloads).</summary>
        public static RouteDoc Selected;

        /// <summary>The route of the running scene.</summary>
        public static RouteDoc Current { get; private set; }

        [SerializeField] private TrailPath path;
        [SerializeField] private TrailShaper shaper;
        [SerializeField] private TrackManager track;

        private void Awake()
        {
            var doc = Selected ?? FromCommandLine() ?? RoutePresets.NewFreeRun();
            if (doc.profile == null || doc.profile.heightsM == null || doc.profile.heightsM.Length < 2
                || doc.generator.version != RouteGenerator.Version)
                RouteGenerator.Generate(doc);
            Current = doc;
            var cl = Jogging.Core.Args.All; // tests: -weather clear|cloudy|rain|snow, -season …, -timeofday 21.5, -lightpollution 0.1
            for (int i = 0; i < cl.Length - 1; i++)
            {
                if (cl[i] == "-weather") doc.@params.weather = cl[i + 1];
                if (cl[i] == "-season") doc.@params.season = cl[i + 1];
                if (cl[i] == "-timeofday" && float.TryParse(cl[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float tod)) doc.@params.timeOfDay = tod;
                if (cl[i] == "-lightpollution" && float.TryParse(cl[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float lp)) doc.@params.lightPollution = lp;
            }

            var p = doc.@params;
            bool loop = p.loop && !p.endless;
            if (path == null) path = FindFirstObjectByType<TrailPath>();
            if (shaper == null) shaper = FindFirstObjectByType<TrailShaper>();
            if (track == null) track = FindFirstObjectByType<TrackManager>();

            path.SetRoute(RouteGenerator.Path(p, doc.generator.seed), loop);
            Lakes.Plan(doc, path, doc.profile.heightsM, doc.profile.stepM); // before shaping: the shaper digs them
            bool wayside = !Jogging.Core.Args.Has("-nowayside"); // tests: -nowayside for A/B frame-time comparisons
            if (wayside) Wayside.MakePlan(doc, path); else Wayside.ClearPlan(); // before the trees: they keep off side paths and planned objects
            TrailSurface.Plan(doc, path, Wayside.Plan); // asphalt, gravel, earth, forest floor, meadow path
            if (wayside && FindFirstObjectByType<Wayside>() == null) new GameObject("Wegrand").AddComponent<Wayside>();
            if (wayside) Animals.MakePlan(doc, path); // deer, hares, rabbits, a fox – after the wayside (they keep off it)
            if (wayside && FindFirstObjectByType<Animals>() == null) new GameObject("Tiere").AddComponent<Animals>();
            if (FindFirstObjectByType<RunGame>() == null) new GameObject("Spiel").AddComponent<RunGame>(); // climb segments, album moods
            if (shaper != null) shaper.SetRoute(doc.profile.heightsM, doc.profile.stepM, loop, path.Length);
            Sky.Begin(p, doc.generator.seed);
            var look = FindFirstObjectByType<TerrainLook>();
            if (look != null) look.Apply(p); // before MapMagic starts generating (its OnEnable runs after this Awake)
            if (track != null)
            {
                var rd = ScriptableObject.CreateInstance<RouteData>();
                rd.name = doc.meta.name;
                rd.lengthMeters = p.endless ? 0f : path.Length; // 0 = endless (no finish)
                track.SetRoute(rd);
            }
            var args = Jogging.Core.Args.All;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-starts" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s0) && track != null)
                    track.StartAt(s0); // tests: start somewhere along the route
            Debug.Log($"[Jogging] Strecke „{doc.meta.name}“: {path.Length / 1000f:0.0} km {(loop ? "Rundkurs" : p.endless ? "endlos" : "Strecke")}, " +
                      $"↑{doc.profile.ascentM:0} m, max {doc.profile.maxGradePercent:0.0} %, Seed {doc.generator.seed}");
        }

        // MapMagic doesn't stop its generator threads when a scene ends (its quit hook is editor-only).
        // Queued tiles of the old scene are dropped; running ones stop on their stop tokens (each
        // TerrainTile stops its tasks in OnDestroy) — no Thread.Abort, which under IL2CPP could kill a
        // thread mid-allocation and crash the GC later. The threads are background threads (no hang on quit).
        private void OnDestroy() => MapMagicExt.ClearQueue();

        private static RouteDoc FromCommandLine()
        {
            var a = Jogging.Core.Args.All;
            string preset = null; int seed = 7;
            for (int i = 0; i < a.Length - 1; i++)
            {
                if (a[i] == "-routefile") { var d = RouteStore.Load(a[i + 1]); if (d != null) return d; }
                if (a[i] == "-preset") preset = a[i + 1];
                if (a[i] == "-seed") int.TryParse(a[i + 1], out seed);
            }
            return preset != null ? RoutePresets.Create(preset, seed) : null;
        }
    }
}
