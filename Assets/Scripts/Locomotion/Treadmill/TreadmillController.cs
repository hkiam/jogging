using UnityEngine;
using Jogging.UI;
using Jogging.World;

namespace Jogging.Locomotion.Treadmill
{
    /// <summary>
    /// FTMS adapter of the control authority (standard treadmills and the simulator): reads the FTMS
    /// Treadmill Data, asks <see cref="BeltControl"/> for targets and sends only what
    /// <see cref="BeltSafety"/> allows as FTMS Control Point commands (incline; speed only if the
    /// safety layer allows speed). Takes control (Request Control) only right before the first
    /// command, never starts the belt, and stays silent on non-FTMS treadmills (FitShow has its own
    /// adapter, <see cref="FitShowBeltControl"/>).
    /// </summary>
    public class TreadmillController : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour transportBehaviour; // ITreadmillTransport
        [SerializeField] private TrackManager track;

        private ITreadmillTransport transport;
        private readonly BeltSafety safety = new BeltSafety(new BeltLimits { InclineResolution = 0.5f }, requireRange: false);
        private bool controlRequested;
        private readonly BeltOverrideLog overrideLog = new BeltOverrideLog();

        public string StateText { get; private set; } = "";

        private void Awake() => transport = transportBehaviour as ITreadmillTransport;

        private void OnEnable()
        {
            if (transport == null) return;
            transport.ConnectionChanged += OnConnectionChanged;
            transport.TreadmillDataReceived += OnData;
            if (transport is MacBleBridgeTransport b) b.FtmsRangeReceived += OnRange;
        }

        // FTMS Supported Speed Range (2AD4: min, max, step in 0.01 km/h) and Inclination Range
        // (2AD5: min, max in 0.1 %, signed — e.g. −5 … 40 % on an incline trainer), read once on connect.
        private void OnRange(byte kind, byte[] d)
        {
            if (d == null || d.Length < 4 || !(transport is MacBleBridgeTransport b)) return;
            bool incline = kind == 2;
            float min = incline ? (short)(d[0] | (d[1] << 8)) * 0.1f : (d[0] | (d[1] << 8)) * 0.01f;
            float max = incline ? (short)(d[2] | (d[3] << 8)) * 0.1f : (d[2] | (d[3] << 8)) * 0.01f;
            if (incline) safety.SetInclineRange(min, max); else safety.SetSpeedRange(min, max);
            BeltControl.ReportRange(b.TreadmillDeviceId, b.TreadmillDeviceName, "FTMS", incline, min, max);
            Debug.Log($"[Jogging] FTMS {(incline ? "Steigungsbereich" : "Tempobereich")}: {min:0.#}…{max:0.#} {(incline ? "%" : "km/h")}");
        }

        private void OnDisable()
        {
            if (transport == null) return;
            transport.ConnectionChanged -= OnConnectionChanged;
            transport.TreadmillDataReceived -= OnData;
            if (transport is MacBleBridgeTransport b) b.FtmsRangeReceived -= OnRange;
        }

        private void OnConnectionChanged(bool connected) { safety.Reset(); controlRequested = false; }

        private void OnData(byte[] d)
        {
            if (FitShowParser.IsFitShowFrame(d)) return;                                  // not an FTMS belt
            if (transport is MacBleBridgeTransport b && b.Protocol != "FTMS") return;     // bridge: only an identified FTMS belt
            if (!FtmsTreadmillParser.TryParse(d, out var data)) return;

            float now = Time.unscaledTime;
            var reading = new BeltReading { Running = data.SpeedKmh > 0.05f, SpeedKmh = data.SpeedKmh, InclinePercent = data.HasIncline ? data.InclinePercent : 0f };
            safety.Limits.AllowSpeed = BeltControl.SpeedAllowed && BeltControl.Mode == ControlMode.Workout;
            BeltControl.ApplyProfile(safety.Limits, (transport as MacBleBridgeTransport)?.TreadmillDeviceId); // this treadmill's caps
            safety.OnReading(reading, now);
            overrideLog.After(safety, reading, now);
            if (!data.HasIncline) return; // can't follow an incline we can't see
            var session = RunSessionUI.Session;
            bool over = BeltControl.RunIsOver(session);
            float? ti = null, ts = null; // a finished run always gets its level-out (a completed workout sets no targets)
            if (!over && !BeltControl.Targets(track != null ? track.CurrentInclinePercent : 0f, out ti, out ts)) { StateText = ""; return; }

            bool sessionRunning = session != null && session.Moves;
            // No run in progress (finished, discarded, menu): only bring the incline back down.
            BeltCommand cmd;
            bool sent = over ? safety.TryLevelOut(now, BeltControl.AppIncline, out cmd)
                             : safety.TryCommand(now, sessionRunning, ti, ts, out cmd);
            StateText = safety.State;
            if (!sent) return;
            BeltControl.Sent(cmd);

            if (!controlRequested) { transport.WriteControlPoint(FtmsControl.RequestControl()); controlRequested = true; }
            transport.WriteControlPoint(FtmsControl.SetTargetInclination(cmd.InclinePercent));
            if (cmd.ChangesSpeed) transport.WriteControlPoint(FtmsControl.SetTargetSpeed(cmd.SpeedKmh));
            Debug.Log($"[Jogging] {BeltOverrideLog.Stamp} → Band (FTMS): Steigung {reading.InclinePercent:0.0}→{cmd.InclinePercent:0.0} %, Tempo {reading.SpeedKmh:0.0}→{cmd.SpeedKmh:0.0} km/h ({BeltControl.Describe(BeltControl.Mode)})");
        }
    }
}
