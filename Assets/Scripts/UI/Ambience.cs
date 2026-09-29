using UnityEngine;
using Jogging.Core;
using Jogging.World;

namespace Jogging.UI
{
    /// <summary>
    /// Ambient sound, synthesized live (no sound files): wind with gusts, footsteps in the running rhythm
    /// that sound like the surface underfoot (<see cref="TrailSurface"/>: gravel crunches, asphalt taps,
    /// earth thuds, needles crackle, grass swishes, snow squeaks), birdsong by the time of day (dawn chorus,
    /// quiet at night), crickets on summer evenings and nights, now and then a tawny owl in the night forest,
    /// rain, and a burbling stream when the trail crosses one. Off in Einstellungen → Geräusche.
    /// Added by <see cref="RunSessionUI"/> at runtime; the audio is computed in OnAudioFilterRead
    /// (audio thread) from values the main thread publishes each frame.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class Ambience : MonoBehaviour
    {
        // published by the main thread, read by the audio thread
        private volatile float windVol, rainVol, birdVol, stepVol, streamVol, stepsPerSecond, master;
        // footstep character (surface mix under the runner) and the night voices
        private volatile float stGravel, stAsphalt, stEarth, stForest, stMeadow, stSnow, cricketVol, owlVol;

        private int sampleRate;
        private System.Random rnd = new System.Random(4711);
        private float brown, brown2, pink1, pink2, pink3, hp, lastWhite;
        private double gustPhase, stepPhase;
        // bird: one chirp sequence at a time
        private int birdNotesLeft; private float birdNoteT, birdNoteLen, birdFreq, birdFreqEnd, birdPause = 2f, birdPhase;
        // footstep envelope
        private float stepEnv, stepTone, stepSwish;
        // crickets: pulse trains of a ~4.5 kHz chirp; owl: a hooting call now and then
        private double cricketPhase, cricketCarrier; private float owlPause = 12f, owlT = -1f, owlPhase;
        private readonly float[] surf = new float[TrailSurface.Count];

        private TrackManager track;
        private double energy; private long samples; private float nextLog = 10f; // -diag: loudness in the log

        public static bool Enabled => AppSettings.Current.ambience;

        private void Start()
        {
            track = FindFirstObjectByType<TrackManager>();
            sampleRate = AudioSettings.outputSampleRate;
            var src = GetComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f;
            src.clip = AudioClip.Create("ambience", sampleRate, 1, sampleRate, false); // silent carrier for the filter
            src.Play();
        }

        private void Update()
        {
            var p = RouteRuntime.Current != null ? RouteRuntime.Current.@params : null;
            string wx = p != null ? p.weather : "clear", season = p != null ? p.season : "summer";
            var sm = RunSessionUI.Session;
            bool menu = StartMenuUI.IsOpen;
            float speed = track != null ? track.SpeedMps : 0f;

            master = Enabled ? (menu ? 0.45f : 1f) : 0f;
            windVol = wx == "clear" ? 0.035f : wx == "cloudy" ? 0.07f : 0.06f;
            rainVol = wx == "rain" ? 0.16f : 0f;
            birdVol = wx == "rain" || wx == "snow" || season == "winter" ? 0f : wx == "cloudy" ? 0.025f : 0.045f;
            // time of day: dawn chorus, calmer midday, quiet at night; crickets and the owl after dark
            var sky = Sky.Instance;
            float sunEl = sky != null ? sky.SunElevation : 30f, hours = sky != null ? sky.Hours : 12f;
            float dawn = hours < 12f ? 1f - Mathf.Abs(Mathf.Clamp(sunEl, -4f, 20f) - 6f) / 14f : 0f;
            birdVol *= Mathf.Clamp01(Mathf.InverseLerp(-5f, 2f, sunEl)) * (0.75f + 0.8f * Mathf.Max(0f, dawn));
            bool warm = season == "summer" || season == "spring" && hours > 19f || season == "autumn" && sunEl > 10f;
            cricketVol = warm && wx != "rain" ? 0.018f * Mathf.Clamp01(Mathf.InverseLerp(10f, -2f, sunEl)) : 0f;
            owlVol = wx != "rain" && sunEl < -8f ? 0.05f : 0f;
            // Footsteps only while really moving (not the treadmill's own noise: the virtual gravel).
            bool moving = sm != null && sm.Moves && speed > 0.6f;
            stepVol = moving ? (season == "winter" || wx == "snow" ? 0.05f : 0.08f) : 0f;
            stepsPerSecond = Mathf.Clamp(2.3f + speed * 0.12f, 2.3f, 3.3f);
            var tp = TrailPath.Active;
            TrailSurface.Weights(tp != null ? tp.RunnerS : 0f, surf);
            stAsphalt = surf[0]; stGravel = surf[1]; stEarth = surf[2]; stForest = surf[3]; stMeadow = surf[4];
            stSnow = season == "winter" || wx == "snow" ? 1f : 0f;

            // Stream: the closer the trail is to a stream crossing, the louder.
            float sv = 0f;
            var path = TrailPath.Active;
            if (path != null && Lakes.Streams != null)
                foreach (var st in Lakes.Streams)
                {
                    float d = Mathf.Abs(path.RunnerS - st.s);
                    if (path.Loop) d = Mathf.Min(d, path.Length - d);
                    sv = Mathf.Max(sv, Mathf.Clamp01(1f - d / 80f));
                }
            streamVol = sv * sv * 0.14f;

            if (Time.unscaledTime > nextLog && System.Array.IndexOf(Jogging.Core.Args.All, "-diag") >= 0)
            {
                nextLog = Time.unscaledTime + 10f;
                double rms = samples > 0 ? System.Math.Sqrt(energy / samples) : 0;
                Debug.Log($"[Ambience] RMS {rms:0.000} (Wind {windVol:0.00}, Regen {rainVol:0.00}, Vögel {birdVol:0.00}, Grillen {cricketVol:0.000}, Kauz {owlVol:0.00}, Schritte {stepVol:0.00} auf {TrailSurface.At(TrailPath.Active != null ? TrailPath.Active.RunnerS : 0f)}, Bach {streamVol:0.00})");
                energy = 0; samples = 0;
            }
        }

        private float White() => (float)(rnd.NextDouble() * 2.0 - 1.0);

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float m = master;
            if (m <= 0f || sampleRate == 0) { System.Array.Clear(data, 0, data.Length); return; }
            float dt = 1f / sampleRate;
            float wv = windVol, rv = rainVol, bv = birdVol, sv = stepVol, strv = streamVol, sps = stepsPerSecond;

            for (int i = 0; i < data.Length; i += channels)
            {
                float w = White();

                // wind: brown noise with slow gusts
                brown = Mathf.Clamp(brown + w * 0.02f, -1f, 1f) * 0.9995f;
                gustPhase += dt * 0.13;
                float gust = 0.55f + 0.45f * Mathf.Sin((float)(gustPhase * 2 * System.Math.PI)) * Mathf.Sin((float)(gustPhase * 0.37 * 2 * System.Math.PI));
                float s = brown * 1.6f * wv * gust;

                // rain: bright hiss (white minus a little low end) with random droplet ticks
                hp = w - lastWhite * 0.6f; lastWhite = w;
                if (rv > 0f) s += (hp * 0.5f + (rnd.NextDouble() < 0.0008 ? White() * 3f : 0f)) * rv;

                // stream: pink-ish noise, amplitude flutter = burbling
                pink1 = 0.99765f * pink1 + w * 0.0990460f;
                pink2 = 0.96300f * pink2 + w * 0.2965164f;
                pink3 = 0.57000f * pink3 + w * 1.0526913f;
                float pink = (pink1 + pink2 + pink3 + w * 0.1848f) * 0.2f;
                brown2 = brown2 * 0.998f + White() * 0.03f;
                if (strv > 0f) s += pink * strv * (0.7f + 0.6f * Mathf.Abs(brown2));

                // footsteps every 1/sps seconds, sounding like the surface underfoot
                if (sv > 0f)
                {
                    stepPhase += dt * sps;
                    if (stepPhase >= 1.0) { stepPhase -= 1.0; stepEnv = 1f; stepTone = 0f; stepSwish = 1f; }
                }
                if (stepEnv > 0.001f || stepSwish > 0.001f)
                {
                    stepTone += dt;
                    float gG = stGravel, gA = stAsphalt, gE = stEarth, gF = stForest, gM = stMeadow, snow = stSnow;
                    // body: a low thump — harder and higher on asphalt, soft and low on earth, grass and snow
                    float tf = 65f + 40f * gA - 10f * (gE + gM), td = 45f + 45f * gA - 15f * (gE + gM + snow);
                    float thump = Mathf.Sin(stepTone * 2f * Mathf.PI * tf) * Mathf.Exp(-stepTone * td) * (0.9f - 0.4f * gM - 0.3f * snow);
                    // gravel and needles: grains of crunch; asphalt: a short bright click of the sole
                    float grains = rnd.NextDouble() < 0.02 * (gG + 0.5f * gF + 0.8f * snow) ? White() * 2.2f : 0f;
                    float crunch = (hp * 0.8f * (gG + 0.35f * gF) + grains) * stepEnv;
                    float click = stepTone < 0.004f ? hp * 1.2f * gA : 0f;
                    // earth: a muffled thud (low-passed); meadow: a longer swish through the grass
                    float thud = brown2 * 3f * gE * stepEnv;
                    float swish = pink * 1.4f * gM * stepSwish * Mathf.Sin(Mathf.Min(1f, stepTone * 10f) * Mathf.PI);
                    // snow: a compressed, squeaky crunch
                    float squeak = snow > 0f ? Mathf.Sin(stepTone * 2f * Mathf.PI * (900f + 400f * Mathf.Sin(stepTone * 60f))) * pink * 2f * stepEnv * snow : 0f;
                    s += (crunch * (1f - snow * 0.6f) + thump + click + thud + swish + squeak) * sv;
                    stepEnv *= 0.9975f - 0.001f * gA;  // ~40 ms (shorter on asphalt)
                    stepSwish *= 0.9988f;              // ~90 ms
                }

                // crickets: 3 pulses of a 4.5 kHz tone at 30 Hz, about twice a second (a couple of them)
                if (cricketVol > 0f)
                {
                    cricketPhase += dt * 1.9; cricketCarrier += dt * 4500.0;
                    double cp = cricketPhase - System.Math.Floor(cricketPhase);
                    float pulse = cp < 0.3 ? Mathf.Max(0f, Mathf.Sin((float)(cp / 0.1 * System.Math.PI))) : 0f;
                    s += Mathf.Sin((float)(cricketCarrier * 2 * System.Math.PI)) * pulse * cricketVol;
                }

                // tawny owl: "huu … hu-hu-huuu" now and then
                if (owlVol > 0f)
                {
                    if (owlT < 0f) { owlPause -= dt; if (owlPause <= 0f) { owlT = 0f; owlPause = 25f + (float)rnd.NextDouble() * 40f; } }
                    else
                    {
                        owlT += dt;
                        float a = 0f, f = 430f;
                        if (owlT < 0.9f) a = Mathf.Sin(owlT / 0.9f * Mathf.PI);
                        else if (owlT > 3.2f && owlT < 3.45f) a = Mathf.Sin((owlT - 3.2f) / 0.25f * Mathf.PI) * 0.7f;
                        else if (owlT > 3.6f && owlT < 4.6f) { a = Mathf.Sin((owlT - 3.6f) / 1.0f * Mathf.PI); f = 450f + 25f * Mathf.Sin(owlT * 40f); }
                        else if (owlT > 4.8f) owlT = -1f;
                        owlPhase += f * dt;
                        s += Mathf.Sin(owlPhase * 2f * Mathf.PI) * a * a * owlVol;
                    }
                }

                // birds: sequences of 3–6 short whistles sweeping in pitch, then a pause
                if (bv > 0f)
                {
                    if (birdNotesLeft <= 0)
                    {
                        birdPause -= dt;
                        if (birdPause <= 0f)
                        {
                            birdNotesLeft = 3 + rnd.Next(4);
                            birdNoteT = 0f; birdNoteLen = 0.06f + (float)rnd.NextDouble() * 0.08f;
                            birdFreq = 2600f + (float)rnd.NextDouble() * 2200f; birdFreqEnd = birdFreq * (0.8f + (float)rnd.NextDouble() * 0.5f);
                        }
                    }
                    else
                    {
                        birdNoteT += dt;
                        float f01 = birdNoteT / birdNoteLen;
                        if (f01 <= 1f)
                        {
                            float f = Mathf.Lerp(birdFreq, birdFreqEnd, f01);
                            birdPhase += f * dt;
                            float env = Mathf.Sin(f01 * Mathf.PI);
                            s += Mathf.Sin(birdPhase * 2f * Mathf.PI) * env * env * bv;
                        }
                        else if (birdNoteT > birdNoteLen + 0.05f)
                        {
                            birdNotesLeft--;
                            birdNoteT = 0f;
                            float step = (float)rnd.NextDouble() * 600f - 300f;
                            birdFreq = Mathf.Clamp(birdFreq + step, 2200f, 5200f); birdFreqEnd = birdFreq * (0.8f + (float)rnd.NextDouble() * 0.5f);
                            if (birdNotesLeft <= 0) birdPause = 2f + (float)rnd.NextDouble() * 6f;
                        }
                    }
                }

                s = Mathf.Clamp(s * m, -1f, 1f);
                energy += s * s; samples++;
                for (int c = 0; c < channels; c++) data[i + c] = s;
            }
        }
    }
}
