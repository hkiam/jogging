using UnityEngine;

namespace Jogging.Locomotion.Treadmill
{
    /// <summary>What the belt itself reports (FitShow status byte; FTMS: derived from speed).</summary>
    public enum BeltState { Unknown, Stopped, Countdown, Running, Paused, Safety, Error }

    /// <summary>
    /// An <see cref="ILocomotionSource"/> fed by a treadmill (FitShow, FTMS, WalkingPad) or a foot pod
    /// over BLE. Converts the
    /// treadmill's speed (km/h) into m/s and exposes its incline. Data-driven: values update on
    /// each FTMS notification. Considered active while connected and receiving fresh data.
    /// </summary>
    public class TreadmillLocomotionSource : MonoBehaviour, ILocomotionSource
    {
        [Tooltip("Component implementing ITreadmillTransport (simulator or native BLE).")]
        [SerializeField] private MonoBehaviour transportBehaviour;

        [Tooltip("Seconds without data before the treadmill is considered inactive.")]
        [SerializeField] private float dataTimeout = 2.5f;

        private ITreadmillTransport transport;
        private float lastDataTime = -999f;
        private float nextRaw;
        private static readonly bool diag = Jogging.Core.Args.Has("-diag");

        public float SpeedMps { get; private set; }

        /// <summary>Belt state as reported by the treadmill (drives "run follows belt").</summary>
        public BeltState State { get; private set; } = BeltState.Unknown;
        public float InclinePercent { get; private set; }

        public bool IsActive =>
            transport != null && transport.IsConnected && (Time.unscaledTime - lastDataTime) <= dataTimeout;

        private void Awake()
        {
            transport = transportBehaviour as ITreadmillTransport;
        }

        private void OnEnable()
        {
            if (transport != null) transport.TreadmillDataReceived += OnData;
        }

        private void OnDisable()
        {
            if (transport != null) transport.TreadmillDataReceived -= OnData;
        }

        private void OnData(byte[] bytes)
        {
            // FitShow, FTMS, WalkingPad or a foot pod (RSC): TreadmillFrames knows them all
            // -diag: the raw frame every 5 s (e.g. to find where a treadmill puts the hand-grip pulse)
            // … and every frame that isn't a status report (answers to commands and queries) in full
            bool status = bytes.Length > 1 && bytes[0] == 0x02 && bytes[1] == FitShowParser.CmdSysStatus;
            if (diag && !status && transport.Protocol == "FitShow")
                Debug.Log($"[Band-Antwort] {System.BitConverter.ToString(bytes).Replace('-', ' ')}");
            if (diag && Time.unscaledTime > nextRaw)
            {
                nextRaw = Time.unscaledTime + 5f;
                Debug.Log($"[Band-Rohdaten] {transport.Protocol}: {System.BitConverter.ToString(bytes).Replace('-', ' ')}");
            }
            if (!TreadmillFrames.TryDecode(transport.Protocol, bytes, out var d, out var state)) return;
            reportedState = state;
            ReportedKmh = d.SpeedKmh;
            if (d.HasIncline) InclinePercent = d.InclinePercent;
            lastDataTime = Time.unscaledTime; // real time: a timescale must not make the belt look lost
            if (d.HasDistance) BeltDistance(d.TotalDistanceMeters);
            if (rampKmhPerS <= 0f) { SpeedMps = ReportedKmh / 3.6f; State = state; } // belt reports its real speed
        }

        /// <summary>The speed the belt reports. FitShow belts (F37) report their target: the belt gets
        /// there slowly — <see cref="SpeedMps"/> follows at the belt's rate.</summary>
        public float ReportedKmh { get; private set; }
        private BeltState reportedState = BeltState.Unknown;
        private float rampKmhPerS;
        private bool mayCoast;

        public void Tick(float deltaTime)
        {
            // Rate per treadmill (profile; FitShow default 1 km/h per s, FTMS etc. report real speed: off)
            var br = transport as MacBleBridgeTransport;
            var prof = Core.AppSettings.Treadmill(br != null ? br.TreadmillDeviceId : "");
            rampKmhPerS = prof.rampKmhPerS > 0f ? prof.rampKmhPerS : prof.rampKmhPerS < 0f ? 0f
                        : (transport != null && transport.Protocol == "FitShow" ? 1f : 0f);
            if (rampKmhPerS <= 0f) return;
            // Safety clip or error: the belt brakes hard — the run stops at once (no coasting, no "Weiter geht's").
            if (reportedState == BeltState.Safety || reportedState == BeltState.Error) { SpeedMps = 0f; mayCoast = false; }
            else if (reportedState == BeltState.Running) mayCoast = true;
            else if (reportedState == BeltState.Countdown) mayCoast = false;
            SpeedMps = Mathf.MoveTowards(SpeedMps, ReportedKmh / 3.6f, rampKmhPerS / 3.6f * deltaTime);
            // A normal stop: the belt says "stopped" while it still runs down — the run coasts along, the pause
            // comes when it stands. Only straight from running (not after the clip, not before a start).
            bool coasting = mayCoast && SpeedMps > 0.15f && (reportedState == BeltState.Stopped || reportedState == BeltState.Paused);
            if (!coasting && reportedState != BeltState.Running) mayCoast = false;
            State = coasting ? BeltState.Running : reportedState;
        }

        // -diag: the belt's own distance counter against the app's, every 5 s — to calibrate the rate.
        private float beltDist0 = -1f, appDist0, nextDistLog;
        private void BeltDistance(float beltM)
        {
            if (!diag) return;
            var stats = RunStatsRef;
            if (stats == null) return;
            if (beltDist0 < 0f || beltM < beltDist0) { beltDist0 = beltM; appDist0 = stats.DistanceMeters; nextDistLog = Time.unscaledTime + 5f; return; }
            if (Time.unscaledTime < nextDistLog) return;
            nextDistLog = Time.unscaledTime + 5f;
            Debug.Log($"[Band-Weg] Band zählt {beltM - beltDist0:0} m, App {stats.DistanceMeters - appDist0:0} m · gemeldet {ReportedKmh:0.0} km/h, App {SpeedMps * 3.6f:0.0} km/h (Anlaufen {rampKmhPerS:0.0} km/h/s)");
        }
        private Core.RunStats runStats;
        private Core.RunStats RunStatsRef => runStats != null ? runStats : (runStats = FindFirstObjectByType<Core.RunStats>());
    }
}
