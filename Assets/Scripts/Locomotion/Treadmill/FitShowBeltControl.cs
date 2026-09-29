using UnityEngine;
using Jogging.UI;
using Jogging.World;

namespace Jogging.Locomotion.Treadmill
{
    /// <summary>
    /// FitShow adapter (Sportstech F37) of the control authority: reads every status frame, asks
    /// <see cref="BeltControl"/> for targets and lets only what <see cref="BeltSafety"/> allows reach
    /// the belt. FitShow has no incline-only command: incline is set together with speed via
    /// SYS_CONTROL/TARGET_OR_RUN, which could also start a stopped belt — the safety layer only sends
    /// while the belt reports RUNNING, and unless speed control is allowed the belt's own, freshly
    /// reported speed byte is echoed (speed never changes). Commands go out right after a fresh
    /// status frame. Also requires <see cref="MacBleBridgeTransport.AllowBeltControl"/>.
    /// K toggles the route incline coupling (saved).
    /// </summary>
    public class FitShowBeltControl : MonoBehaviour
    {
        [SerializeField] private MacBleBridgeTransport transport;
        [SerializeField] private TrackManager track;
        [SerializeField] private KeyCode toggleKey = KeyCode.K;
        [Tooltip("Seconds between belt commands (safety layer).")]
        [SerializeField] private float sendInterval = 3f;

        private readonly BeltSafety safety = new BeltSafety(new BeltLimits(), requireRange: true);
        private readonly BeltOverrideLog overrideLog = new BeltOverrideLog();
        private bool rangeKnown;
        private float queryTime = -999f;

        /// <summary>What the control is doing ("Steigung → 4 %", "pausiert (manuell verstellt)", …).</summary>
        public string StateText { get; private set; } = "";

        public BeltSafety Safety => safety;

        private void Awake() => safety.Limits.SendInterval = sendInterval;

        private void OnEnable()
        {
            if (transport == null) return;
            transport.TreadmillDataReceived += OnData;
            transport.ConnectionChanged += OnConnection;
        }

        private void OnDisable()
        {
            if (transport == null) return;
            transport.TreadmillDataReceived -= OnData;
            transport.ConnectionChanged -= OnConnection;
        }

        private void OnConnection(bool connected)
        {
            safety.Reset();
            if (!connected) return;
            // The range this treadmill reported last time (same device): the F37 doesn't always answer
            // the incline query — without a range the app would not follow the route.
            var prof = Core.AppSettings.Treadmill((transport as MacBleBridgeTransport)?.TreadmillDeviceId);
            if (prof.inclineKnown) { safety.SetInclineRange(prof.inclineMin, prof.inclineMax); rangeKnown = true; }
            if (prof.speedKnown) safety.SetSpeedRange(prof.speedMin, prof.speedMax);
            QueryRanges();
        }

        // Both queries, the second a moment after the first: sent back to back the F37 answered only one.
        private void QueryRanges()
        {
            queryTime = Time.unscaledTime;
            transport.SendQuery(FitShowParser.QuerySpeedRange());   // read-only
            inclineQueryAt = Time.unscaledTime + 0.6f;
        }
        private float inclineQueryAt = -1f;

        private void Update()
        {
            if (inclineQueryAt > 0f && Time.unscaledTime >= inclineQueryAt && transport != null && transport.IsConnected)
            { inclineQueryAt = -1f; transport.SendQuery(FitShowParser.QueryInclineRange()); } // read-only
            if (Input.GetKeyDown(toggleKey) && !StartMenuUI.IsOpen)
            {
                var st = Core.AppSettings.Current;
                st.beltIncline = !st.beltIncline;
                Core.AppSettings.Save();
                TreadmillConnectUI.Apply();
                Debug.Log($"[Jogging] Steuerung: {BeltControl.Describe(BeltControl.Mode)}");
            }
            // Range not known (e.g. after a scene reload the belt didn't answer): the one this treadmill
            // reported before; otherwise ask again (read-only).
            if (transport != null && transport.IsConnected && !rangeKnown)
            {
                var prof = Core.AppSettings.Treadmill((transport as MacBleBridgeTransport)?.TreadmillDeviceId);
                if (prof.inclineKnown) { safety.SetInclineRange(prof.inclineMin, prof.inclineMax); rangeKnown = true; }
                else if (Time.unscaledTime - queryTime > 3f) QueryRanges();
            }
        }

        private int unanswered, lastSentInc = int.MinValue, lastSentFrom;
        private float followPauseUntil;

        private void OnData(byte[] d)
        {
            if (FitShowParser.TryParseRange(d, out var kind, out var max, out var min))
            {
                var br = transport as MacBleBridgeTransport;
                if (br != null) BeltControl.ReportRange(br.TreadmillDeviceId, br.TreadmillDeviceName, "FitShow", kind == FitShowParser.InfoIncline,
                                                        kind == FitShowParser.InfoIncline ? min : min / 10f, kind == FitShowParser.InfoIncline ? max : max / 10f);
                if (kind == FitShowParser.InfoIncline)
                {
                    safety.SetInclineRange(min, max); rangeKnown = true;
                    Debug.Log($"[Jogging] F37 Steigungsbereich: {min}…{max} %" + (max <= min ? " (nicht unterstützt)" : ""));
                }
                else
                {
                    safety.SetSpeedRange(min / 10f, max / 10f);
                    Debug.Log($"[Jogging] F37 Tempobereich: {min / 10f:0.0}…{max / 10f:0.0} km/h");
                }
                return;
            }
            if (!FitShowParser.IsFitShowFrame(d) || d[1] != FitShowParser.CmdSysStatus) return;

            float now = Time.unscaledTime;
            bool running = FitShowParser.TryGetRunningSpeedByte(d, out var speedByte, out var incline);
            var reading = new BeltReading { Running = running, SpeedKmh = speedByte / 10f, InclinePercent = incline };
            safety.Limits.AllowSpeed = BeltControl.SpeedAllowed && BeltControl.Mode == ControlMode.Workout;
            BeltControl.ApplyProfile(safety.Limits, (transport as MacBleBridgeTransport)?.TreadmillDeviceId); // this treadmill's caps
            safety.OnReading(reading, now);
            overrideLog.After(safety, reading, now);

            var session = RunSessionUI.Session;
            bool over = BeltControl.RunIsOver(session);
            // A finished run always gets its level-out, even when nobody sets targets any more (a
            // completed workout returns none).
            float? ti = null, ts = null;
            if (!over && !BeltControl.Targets(track != null ? track.CurrentInclinePercent : 0f, out ti, out ts)) { StateText = ""; return; }
            if (!transport.AllowBeltControl) { StateText = "Bandsteuerung gesperrt"; return; }

            bool sessionRunning = session != null && session.Moves;
            if (now < followPauseUntil) { StateText = "Band folgt der Steigung nicht – Pause"; return; }
            // No run in progress (finished, discarded, menu): only bring the incline back down.
            BeltCommand cmd;
            bool sent = over ? safety.TryLevelOut(now, BeltControl.AppIncline, out cmd)
                             : safety.TryCommand(now, sessionRunning, ti, ts, out cmd);
            StateText = safety.State;
            if (!sent) return;
            BeltControl.Sent(cmd);

            // Echo the belt's exact speed byte unless the safety layer lets the speed change.
            byte spd = cmd.ChangesSpeed ? (byte)Mathf.Clamp(Mathf.RoundToInt(cmd.SpeedKmh * 10f), 1, 255) : speedByte;
            sbyte inc = (sbyte)Mathf.RoundToInt(cmd.InclinePercent);
            // The same incline again and the belt hasn't moved since: it ignores the step (the F37 takes
            // only even values). Third time: learn 2-% steps for this treadmill; still nothing: pause 30 s.
            if (inc == lastSentInc && incline == lastSentFrom && inc != incline) unanswered++; else unanswered = 0;
            lastSentInc = inc; lastSentFrom = incline;
            if (unanswered >= 2)
            {
                unanswered = 0; lastSentInc = int.MinValue;
                var br2 = transport as MacBleBridgeTransport;
                var prof = Core.AppSettings.Treadmill(br2?.TreadmillDeviceId);
                if (prof.inclineStep < 2f && br2 != null && br2.TreadmillDeviceId != "")
                {
                    prof.inclineStep = 2f; Core.AppSettings.Save();
                    Debug.Log($"[Jogging] {BeltOverrideLog.Stamp} Band nimmt die Steigung nur in 2-%-Schritten – für „{prof.name}“ gemerkt");
                }
                else
                {
                    followPauseUntil = now + 30f;
                    Debug.Log($"[Jogging] {BeltOverrideLog.Stamp} Band folgt der Steigung nicht ({incline} % statt {inc} %) – App pausiert 30 s");
                }
                return;
            }
            transport.WriteControlPoint(FitShowParser.SetSpeedAndIncline(spd, inc));
            Debug.Log($"[Jogging] {BeltOverrideLog.Stamp} → Band: Steigung {incline}→{inc} %, Tempo {speedByte / 10f:0.0}→{spd / 10f:0.0} km/h ({(over ? "Lauf beendet – Steigung zurück" : BeltControl.Describe(BeltControl.Mode))})");
        }
    }
}
