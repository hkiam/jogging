using System.Collections.Generic;
using UnityEngine;
using Jogging.Route;

namespace Jogging.World
{
    /// <summary>
    /// Wild animals along the route – rare, as in a real forest, and each where it belongs:
    /// <list type="bullet">
    /// <item><b>Red deer</b> (stag) graze in small groups on open land by the forest edge, 70–180 m away –
    /// more often at dawn and dusk. They only stand and graze (the model has no run).</item>
    /// <item><b>Hares</b> sit on meadows and fields, 18–45 m from the trail; when you come within ~25 m they
    /// sit up and dash away across the field.</item>
    /// <item><b>Wild rabbits</b> sit at the forest edge close to the trail and hop off slowly when you pass.</item>
    /// <item>At most one <b>fox</b> per run, mostly in the morning, evening or at night: it crosses the trail
    /// some way ahead, stops for a moment and trots on.</item>
    /// </list>
    /// Planned from the route's seed like the wayside (<see cref="Wayside"/>), built around the runner. Weak
    /// tablets (graphics "minimal") get about half. Models: Resources/Animals (Editor/AnimalAssets).
    /// </summary>
    public class Animals : MonoBehaviour
    {
        public enum Kind { Deer, Hare, Rabbit, Fox }

        private struct Spot { public Kind kind; public float s, side, lateral; public int count, seed; }

        private class Beast
        {
            public Kind kind; public Transform t; public Animation anim;
            public Vector3 dir; public float speed, state, until; public int phase; // phase: 0 calm, 1 alert, 2 fleeing/moving
            public string seenKey;
        }

        private static readonly List<Spot> plan = new List<Spot>();
        private readonly Dictionary<int, List<Beast>> built = new Dictionary<int, List<Beast>>();
        private readonly Dictionary<int, float> retryAt = new Dictionary<int, float>();
        private readonly Dictionary<Kind, GameObject> prefabs = new Dictionary<Kind, GameObject>();
        private System.Random rnd = new System.Random(5);
        private const float Ahead = 420f, Behind = 90f;

        public static int Count(Kind k) { int n = 0; foreach (var p in plan) if (p.kind == k) n++; return n; }

        /// <summary>The animals standing in the world right now (test pictures).</summary>
        public IEnumerable<(Kind kind, Transform t)> Standing()
        {
            foreach (var l in built.Values) foreach (var b in l) if (b.t != null && b.t.gameObject.activeInHierarchy) yield return (b.kind, b.t);
        }

        // ------------------------------------------------------------------ plan
        public static void MakePlan(RouteDoc doc, TrailPath path)
        {
            plan.Clear();
            if (doc == null || path == null) return;
            var p = doc.@params;
            var r = new System.Random(doc.generator.seed * 17 + 4242);
            float R() => (float)r.NextDouble();
            float end = p.endless ? Mathf.Min(path.Length, 30000f) : path.Length;
            bool Forest(float s) => Vegetation.At(s).trees > 0.55f;
            bool Open(float s) => Vegetation.At(s).trees < 0.3f;
            // dawn and dusk: game is out; the middle of the day less so
            float h = p.timeOfDay;
            float activity = h < 9.5f || h > 17.5f ? 1f : h < 11f || h > 16f ? 0.7f : 0.45f;
            if (p.weather == "rain") activity *= 0.6f;
            float share = GraphicsQuality.Level == "minimal" ? 0.5f : 1f;
            float lastDeer = -1e9f, lastHare = -1e9f, lastRabbit = -1e9f;
            for (float s = 200f; s < end - 60f; s += 25f)
            {
                bool open = Open(s), edge = open && (Forest(s - 80f) || Forest(s + 80f));
                // deer: open land by the forest, a group every ~2 km of such land
                if (edge && s - lastDeer > 700f && R() < 0.035f * activity * share)
                {
                    plan.Add(new Spot { kind = Kind.Deer, s = s, side = R() < 0.5f ? -1f : 1f, lateral = 70f + R() * 110f, count = 1 + (R() < 0.6f ? 1 : 0) + (R() < 0.25f ? 1 : 0), seed = r.Next() });
                    lastDeer = s;
                }
                // hares: meadows and fields
                else if (open && s - lastHare > 350f && R() < 0.05f * share)
                {
                    plan.Add(new Spot { kind = Kind.Hare, s = s, side = R() < 0.5f ? -1f : 1f, lateral = 18f + R() * 27f, count = 1, seed = r.Next() });
                    lastHare = s;
                }
                // wild rabbits: forest edge and clearings, close to the trail
                else if ((edge || Forest(s) && Vegetation.At(s).trees < 0.8f) && s - lastRabbit > 500f && R() < 0.03f * share)
                {
                    plan.Add(new Spot { kind = Kind.Rabbit, s = s, side = R() < 0.5f ? -1f : 1f, lateral = 6f + R() * 10f, count = 1 + (R() < 0.3f ? 1 : 0), seed = r.Next() });
                    lastRabbit = s;
                }
            }
            // one fox at most: mostly morning, evening or night
            if (end > 1500f && R() < (h < 9.5f || h > 17.5f ? 0.8f : 0.3f))
                for (int t = 0; t < 10; t++)
                {
                    float s = 400f + R() * (end - 800f);
                    if (Lakes.Inside(path.Point(s), 15f)) continue;
                    plan.Add(new Spot { kind = Kind.Fox, s = s, side = R() < 0.5f ? -1f : 1f, lateral = 22f, count = 1, seed = r.Next() });
                    break;
                }
            if (Jogging.Core.Args.Has("-animaltest")) // tests: one of each right at the start
            {
                plan.Clear();
                plan.Add(new Spot { kind = Kind.Hare, s = 30f, side = 1f, lateral = 7f, count = 1, seed = 1 });
                plan.Add(new Spot { kind = Kind.Rabbit, s = 26f, side = -1f, lateral = 5f, count = 1, seed = 2 });
                plan.Add(new Spot { kind = Kind.Deer, s = 60f, side = 1f, lateral = 16f, count = 2, seed = 3 });
                plan.Add(new Spot { kind = Kind.Fox, s = 45f, side = -1f, lateral = 9f, count = 1, seed = 4 });
            }
            plan.Sort((a, b) => a.s.CompareTo(b.s));
            Debug.Log($"[Tiere] Plan: {Count(Kind.Deer)} Hirschgruppen, {Count(Kind.Hare)} Hasen, {Count(Kind.Rabbit)} Kaninchen, {Count(Kind.Fox)} Fuchs");
        }

        // ------------------------------------------------------------------ build around the runner
        private GameObject Prefab(Kind k)
        {
            if (prefabs.TryGetValue(k, out var g)) return g;
            g = Resources.Load<GameObject>("Animals/" + (k == Kind.Deer ? "deer" : k == Kind.Hare ? "hare" : k == Kind.Rabbit ? "rabbit" : "fox"));
            if (g == null) Debug.LogWarning("[Tiere] Modell fehlt: " + k);
            return prefabs[k] = g;
        }

        private void Update()
        {
            var path = TrailPath.Active;
            if (path == null || RouteRuntime.Current == null) return;
            float z = path.RunnerS;
            float L = path.Loop ? path.Length : 0f;
            float Dist(float s) { float d = s - z; if (L > 0f) d = Mathf.Repeat(d + L * 0.5f, L) - L * 0.5f; return d; }
            int budget = 1;
            for (int i = 0; i < plan.Count; i++)
            {
                float d = Dist(plan[i].s);
                bool want = d > -Behind && d < Ahead;
                if (!want)
                {
                    if (built.TryGetValue(i, out var list)) { foreach (var b in list) if (b.t != null) Destroy(b.t.gameObject); built.Remove(i); }
                    continue;
                }
                if (built.ContainsKey(i) || budget <= 0 || (retryAt.TryGetValue(i, out float t) && Time.unscaledTime < t)) continue;
                var made = Build(plan[i], path);
                if (made != null) { built[i] = made; budget--; retryAt.Remove(i); }
                else retryAt[i] = Time.unscaledTime + 1f;
            }
            var cam = Camera.main;
            Vector3 runner = cam != null ? cam.transform.position : path.Point(z);
            foreach (var list in built.Values) foreach (var b in list) if (b.t != null) Behave(b, runner, path);
        }

        private static bool Ground(Vector3 p, out float h) => TerrainGround.TryHeight(p.x, p.z, out h, 200);

        private List<Beast> Build(Spot sp, TrailPath path)
        {
            var prefab = Prefab(sp.kind);
            if (prefab == null) return new List<Beast>();
            var r = new System.Random(sp.seed);
            float R() => (float)r.NextDouble();
            var list = new List<Beast>();
            Vector3 centre = path.Offset(sp.s, sp.side * sp.lateral);
            if (!Ground(centre, out _)) return null;
            for (int k = 0; k < sp.count; k++)
            {
                Vector3 p = centre + new Vector3((R() - 0.5f) * 14f, 0f, (R() - 0.5f) * 14f) * (sp.count > 1 ? 1f : 0f);
                if (Lakes.Inside(p, 1f) || Wayside.Blocks(p, 0.5f) || !Ground(p, out float h)) continue;
                path.Project(p, sp.s, 300f, out float lat);
                if (Mathf.Abs(lat) < 4f) continue; // not on the trail
                var go = Instantiate(prefab, transform);
                float yaw = sp.kind == Kind.Fox ? 0f : R() * 360f;
                go.transform.SetPositionAndRotation(new Vector3(p.x, h, p.z), Quaternion.Euler(0f, yaw, 0f));
                float size = 0.92f + R() * 0.16f;
                go.transform.localScale = Vector3.one * size;
                var b = new Beast { kind = sp.kind, t = go.transform, anim = go.GetComponentInChildren<Animation>(), seenKey = SightKey(sp.kind) };
                if (sp.kind == Kind.Fox)
                {
                    // the fox waits beside the trail, out of sight, and crosses when the runner comes near
                    Vector3 across = -path.Right(sp.s) * sp.side;
                    b.dir = new Vector3(across.x, 0f, across.z).normalized;
                    go.transform.rotation = Quaternion.LookRotation(b.dir);
                    b.phase = 0;
                    Play(b, "stand", "idle");
                }
                else Play(b, sp.kind == Kind.Hare ? new[] { "idle_eating", "idle_03", "idle_04", "idle" } : new[] { "idle", "eat", "stand" }, R());
                list.Add(b);
            }
            return list;
        }

        private static string SightKey(Kind k) => k == Kind.Deer ? "Hirsch" : k == Kind.Hare ? "Hase" : k == Kind.Rabbit ? "Kaninchen" : "Fuchs";

        // plays the first clip whose name contains one of the keys (a random one among several)
        private void Play(Beast b, string key, string fallback) => Play(b, new[] { key, fallback }, (float)rnd.NextDouble());

        private void Play(Beast b, string[] keys, float pick)
        {
            if (b.anim == null) return;
            foreach (var key in keys)
            {
                var matches = new List<string>();
                foreach (AnimationState st in b.anim)
                {
                    string n = st.name.ToLowerInvariant();
                    if (n.Contains(key) && !n.Contains("foot") && !n.Contains("leg") && !n.Contains("dead")) matches.Add(st.name);
                }
                if (matches.Count == 0) continue;
                string clip = matches[Mathf.Min(matches.Count - 1, (int)(pick * matches.Count))];
                var state = b.anim[clip];
                state.speed = 0.9f + pick * 0.2f;
                if (!b.anim.IsPlaying(clip)) { b.anim.CrossFade(clip, 0.25f); state.normalizedTime = pick; }
                return;
            }
        }

        private void Behave(Beast b, Vector3 runner, TrailPath path)
        {
            float dt = Time.deltaTime;
            Vector3 pos = b.t.position;
            float dist = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(runner.x, runner.z));
            if (b.seenKey != null && dist < 120f) { Wayside.NoteSight(b.seenKey); b.seenKey = null; }
            Vector3 away = pos - runner; away.y = 0f; away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            switch (b.kind)
            {
                case Kind.Deer: // grazes; now and then turns a little
                    if (Time.time > b.until) { b.until = Time.time + 6f + (float)rnd.NextDouble() * 10f; b.t.rotation *= Quaternion.Euler(0f, ((float)rnd.NextDouble() - 0.5f) * 50f, 0f); }
                    return;
                case Kind.Hare:
                    if (b.phase == 0 && dist < 25f) { b.phase = 1; b.until = Time.time + 0.6f; Play(b, new[] { "alerted", "static_pose", "idle" }, 0f); }
                    else if (b.phase == 1 && Time.time > b.until)
                    {
                        b.phase = 2; b.speed = 9f + (float)rnd.NextDouble() * 3f;
                        b.dir = Quaternion.Euler(0f, ((float)rnd.NextDouble() - 0.5f) * 60f, 0f) * away;
                        Play(b, new[] { "sprint_fwd_01", "sprint", "run", "walk" }, 0f);
                    }
                    else if (b.phase == 2) Move(b, dt, 60f);
                    return;
                case Kind.Rabbit:
                    if (b.phase == 0 && dist < 12f) { b.phase = 2; b.speed = 0.9f; b.dir = away; b.until = Time.time + 3f + (float)rnd.NextDouble() * 2f; Play(b, new[] { "walk" }, 0f); }
                    else if (b.phase == 2) { Move(b, dt, 0f); if (Time.time > b.until) { b.phase = 3; Play(b, new[] { "idle" }, 0.5f); } }
                    return;
                case Kind.Fox:
                    // crosses when the runner is 35–70 m away: trots to the trail, stops, trots on
                    if (b.phase == 0 && dist < 70f && dist > 30f) { b.phase = 2; b.speed = 2.6f; b.until = Time.time + 5.5f; Play(b, new[] { "walkfast", "walk" }, 0f); }
                    else if (b.phase == 2)
                    {
                        Move(b, dt, 0f);
                        path.Project(b.t.position, path.RunnerS, 300f, out float lat);
                        if (Mathf.Abs(lat) < 2f && b.state == 0f) { b.state = 1f; b.phase = 3; b.until = Time.time + 1.6f; Play(b, new[] { "stand" }, 0f); } // looks at you
                    }
                    else if (b.phase == 3 && Time.time > b.until) { b.phase = 4; Play(b, new[] { "walkfast", "walk" }, 0f); }
                    else if (b.phase == 4) Move(b, dt, 0f);
                    return;
            }
        }

        // moves along the ground in its direction; turns out of the way of the trail, gone when far
        private void Move(Beast b, float dt, float goneAt)
        {
            var p = b.t.position + b.dir * b.speed * dt;
            if (Lakes.Inside(p, 0.5f)) { b.dir = Quaternion.Euler(0f, 90f, 0f) * b.dir; return; }
            if (Ground(p, out float h)) p.y = Mathf.Lerp(b.t.position.y, h, 0.5f);
            b.t.position = p;
            b.t.rotation = Quaternion.Slerp(b.t.rotation, Quaternion.LookRotation(b.dir), dt * 6f);
            var cam = Camera.main;
            if (goneAt > 0f && cam != null && Vector3.Distance(cam.transform.position, p) > goneAt && Vector3.Dot(p - cam.transform.position, cam.transform.forward) < 0.3f * Vector3.Distance(cam.transform.position, p))
                b.t.gameObject.SetActive(false); // off to the side and out of view: gone
        }
    }
}
