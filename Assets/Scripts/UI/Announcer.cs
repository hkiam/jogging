using UnityEngine;
using Jogging.Core;
using Jogging.Training;

namespace Jogging.UI
{
    /// <summary>
    /// Announcements during the run, so nobody has to watch the screen on the treadmill: start and
    /// pause, every kilometre (with its split time), each workout segment, a short beep 5 s before a
    /// timed segment ends, the end of the run. Speech uses the system voice in the app's language (macOS "say" Anna / Samantha, on iPad/Android <see cref="NativeSpeech"/>);
    /// beeps are generated tones. Off in the settings (Geräte → Ansagen).
    /// Added by <see cref="RunSessionUI"/> at runtime.
    /// </summary>
    public class Announcer : MonoBehaviour
    {
        public static Announcer Current { get; private set; }

        private RunStats stats;
        private SessionState last = SessionState.Preparing;
        private int lastKm;
        private float lastKmTime;
        private int beepedFor = -1;
        private AudioSource audioSource;
        private AudioClip beep, beepHigh;

        public static bool Enabled => AppSettings.Current.announcements;

        private void Awake() { Current = this; CoachHints = 0; }
        private void OnDestroy() { if (Current == this) Current = null; Stop(); }

        private void Start()
        {
            stats = FindFirstObjectByType<RunStats>();
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            beep = Tone(880f, 0.12f);
            beepHigh = Tone(1320f, 0.35f);
        }

        /// <summary>Speak (replaces what is being said). No-op when switched off.</summary>
        public void Say(string text)
        {
            if (!Enabled || string.IsNullOrEmpty(text)) return;
            Debug.Log("[Jogging] Ansage: " + text);
            if (Platform.SpeechViaBridge) // Windows: the bridge speaks (Windows speech)
            {
                var bridge = FindFirstObjectByType<Jogging.Locomotion.Treadmill.MacBleBridgeTransport>();
                if (bridge != null) bridge.Speak(text, Loc.En ? "en" : "de");
                return;
            }
            if (!Platform.HasMacBridge) { NativeSpeech.Say(text); return; } // iPad / Android: the system voice
            // Replaces what is being said; in the background (don't wait for the speech).
            if (Shell.Run("/usr/bin/killall say 2>/dev/null; /usr/bin/say -v " + (Loc.En ? "Samantha -r 180 " : "Anna -r 185 ") + Shell.Quote(text) + " >/dev/null 2>&1 &") != 0)
                Debug.LogWarning("[Jogging] Ansage nicht möglich");
        }

        public void Beep(bool high = false)
        {
            if (!Enabled || audioSource == null) return;
            audioSource.PlayOneShot(high ? beepHigh : beep, 0.6f);
        }

        private void Stop()
        {
            if (!Enabled) return;
            if (Platform.SpeechViaBridge) FindFirstObjectByType<Jogging.Locomotion.Treadmill.MacBleBridgeTransport>()?.StopSpeaking();
            else if (Platform.HasMacBridge) Shell.Run("/usr/bin/killall say 2>/dev/null");
            else NativeSpeech.Stop();
        }

        private readonly PulseCoach coach = new PulseCoach();
        /// <summary>Hints the pulse coach gave in this run (tests).</summary>
        public static int CoachHints { get; private set; }

        private void Update()
        {
            var sm = RunSessionUI.Session;
            if (sm == null || stats == null) return;

            if (sm.State != last)
            {
                if (sm.State == SessionState.Running && last == SessionState.Countdown && WorkoutRuntime.Current == null) Say(Jogging.Core.Loc.T("Los geht's!"));
                else if (sm.State == SessionState.Running && last == SessionState.Paused) Say(Jogging.Core.Loc.T("Weiter geht's."));
                else if (sm.State == SessionState.Paused) Say(sm.Reason == PauseReason.ConnectionLost ? Jogging.Core.Loc.T("Verbindung zum Laufband verloren.") : Jogging.Core.Loc.T("Pause."));
                last = sm.State;
            }
            if (sm.State != SessionState.Running) return;

            // Every kilometre (or mile) with its split
            int km = Mathf.FloorToInt(Jogging.Core.Units.Dist(stats.DistanceMeters));
            if (km > lastKm)
            {
                Say(Speech.Split(km, stats.ElapsedSeconds - lastKmTime, stats.ElapsedSeconds));
                lastKm = km; lastKmTime = stats.ElapsedSeconds;
            }

            // 5 s before a timed workout segment ends: beeps (the next segment is announced when it starts)
            var r = WorkoutRuntime.Runner;
            var seg = r?.Current;

            // pulse coach: the segment's target zone, or the runner's own one in a free run (profile)
            var me = Jogging.Profile.ProfileService.Instance != null ? Jogging.Profile.ProfileService.Instance.Profile : null;
            int coachSetting = me != null ? me.pulseCoach : 0;
            int target = coachSetting < 0 ? 0 : seg != null ? seg.hrZone : WorkoutRuntime.Current == null ? coachSetting : 0;
            var hrm = HeartRateMonitor.Current;
            int zone = hrm != null ? hrm.Zone : 0;
            var hint = coach.Step(stats.ElapsedSeconds, zone, target);
            if (hint != PulseCoach.Hint.None)
            {
                string text = PulseCoach.Text(hint, zone, target);
                CoachHints++;
                RaceMessages.Post(text); // on screen too (also with the announcements off)
                Say(text);
            }
            if (seg != null && !seg.ByDistance && r.Remaining <= 5f && r.Remaining > 0f)
            {
                int second = Mathf.CeilToInt(r.Remaining);
                if (beepedFor != r.Index * 10 + second && second <= 3) { beepedFor = r.Index * 10 + second; Beep(); }
            }
        }

        /// <summary>A workout segment begins.</summary>
        public void Segment(WorkoutSegment s, bool first)
        {
            if (!first) Beep(high: true);
            Say(Speech.Segment(s));
        }

        private static AudioClip Tone(float hz, float seconds)
        {
            int rate = 44100, n = (int)(rate * seconds);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float env = Mathf.Min(1f, i / 400f) * Mathf.Min(1f, (n - i) / 1500f);
                data[i] = Mathf.Sin(2f * Mathf.PI * hz * i / rate) * 0.5f * env;
            }
            var clip = AudioClip.Create("beep" + hz, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
