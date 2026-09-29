using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Jogging.Locomotion.Treadmill
{
    /// <summary>
    /// Real-treadmill transport for macOS (Editor + standalone). Unity has no Bluetooth on macOS,
    /// so a small signed helper app (Tools/MacBleBridge, installed to ~/Applications) does the
    /// CoreBluetooth work and relays the raw frames over UDP on localhost.
    ///
    /// UDP bridge → Unity :47520  'D'+frame | 'S'+"1"/"0" | 'L'+log | 'P'+protocol
    ///                            'R'+HR measurement | 'T'+"1"/"0" (HR connected) | 'M'+HR log
    ///                            'N'+"tm|id|name" / "hr|id|name" (connected device)
    ///     Unity → bridge :47521  'W'+bytes | 'C'[+id] connect | 'X' disconnect | 'Q' quit
    ///                            'H'[+id] find heart rate sensor | 'Y' disconnect it
    /// With an id only that device is connected (the family's treadmill, the runner's own strap).
    /// The heart rate sensor is a second, independent device of the same bridge.
    ///
    /// Test without hardware: start with <c>-beltsim</c> — a <see cref="FitShowEmulator"/> replaces the
    /// bridge (polled once a second like the real one); keys: B start/stop, ↑/↓ speed, PageUp/PageDown
    /// incline, S safety clip, D connection lost/back. Everything above the transport runs unchanged.
    ///
    /// Safety: belt control writes are OFF by default — the app only READS speed/incline until
    /// <see cref="allowBeltControl"/> is enabled, so the belt can never start on its own.
    /// </summary>
    public class MacBleBridgeTransport : MonoBehaviour, ITreadmillTransport
    {
        private const int ToUnityPort = 47520;
        private const int ToBridgePort = 47521;

        [SerializeField] private bool connectOnStart = true;
        [Tooltip("Path of the bridge app (built by Tools/MacBleBridge/build.sh).")]
        [SerializeField] private string bridgeApp = "~/Applications/JoggingBleScan.app";
        [Tooltip("Let the game write control commands (speed/incline/start) to the belt. Keep OFF " +
                 "unless the protocol's control commands are verified for your treadmill.")]
        [SerializeField] private bool allowBeltControl = false;
        [Tooltip("Bridge counts as gone when it sends no state for this long.")]
        [SerializeField] private float bridgeTimeout = 5f;

        public event Action<byte[]> TreadmillDataReceived;
        public event Action<bool> ConnectionChanged;

        /// <summary>Raw Heart Rate Measurement (0x2A37) notification bytes.</summary>
        public event Action<byte[]> HeartRateReceived;
        /// <summary>Heart Rate Measurement from the treadmill's own heart rate service (hand grips).</summary>
        public event Action<byte[]> GripHeartRateReceived;
        /// <summary>FTMS range characteristic: kind 1 = speed (2AD4), 2 = incline (2AD5), then its bytes.</summary>
        public event Action<byte, byte[]> FtmsRangeReceived;

        /// <summary>A device seen by the device search: its kind (FitShow, FTMS, WalkingPad, iConsole, LifeSpan,
        /// RSC, HR — or an unsupported eHealth, SmartTreadmill, Pafers), id and name.</summary>
        public struct FoundDevice { public string kind, id, name; public int rssi; }
        public readonly System.Collections.Generic.List<FoundDevice> Found = new System.Collections.Generic.List<FoundDevice>();

        /// <summary>List every treadmill, sensor and strap nearby for 20 s (connects nothing).</summary>
        public void StartDeviceSearch()
        {
            Found.Clear();
            if (Simulator != null) { Found.Add(new FoundDevice { kind = "FitShow", id = SimId, name = "F37 (Simulator)" }); return; }
            SendRaw(new[] { (byte)'F' });
        }

        private const string SimId = "SIM-F37";

        private readonly IConsoleAssembler icAssembler = new IConsoleAssembler();

        /// <summary>True while a heart rate sensor is connected via the bridge.</summary>
        public bool HeartRateConnected => hrWanted && hrBridgeConnected && Time.unscaledTime - lastState < bridgeTimeout;
        public bool HeartRateWanted => hrWanted;
        public string HeartRateStatus { get; private set; } = "Getrennt";

        private bool hrWanted, hrBridgeConnected;
        private float hrAskTime = -999f, tmAskTime = -999f, relaunchTime, heartbeatTime;
        private string treadmillWantedId = "", hrWantedId = "";

        /// <summary>Raised with (kind "tm"/"hr", device id, name) when the bridge has connected a device.</summary>
        public event Action<string, string, string> DeviceConnected;

        public string TreadmillDeviceId { get; private set; } = "";
        public string TreadmillDeviceName { get; private set; } = "";
        public string HeartRateDeviceId { get; private set; } = "";
        public string HeartRateDeviceName { get; private set; } = "";

        public bool IsConnected => wanted && bridgeConnected && Time.unscaledTime - lastState < bridgeTimeout;

        /// <summary>Human-readable state for the UI ("Suche Laufband…", last bridge log…).</summary>
        public string StatusText { get; private set; } = "Getrennt";
        /// <summary>"FitShow" or "FTMS" once the bridge has identified the device.</summary>
        public string Protocol { get; private set; } = "";

        /// <summary>How the protocol is called in texts; a foot pod is a running sensor, not a treadmill protocol.</summary>
        public static string ProtocolName(string p) => p == "RSC" ? Jogging.Core.Loc.T("Laufsensor, nur Tempo") : p == "WalkingPad" ? Jogging.Core.Loc.T("WalkingPad, nur lesen") : p;

        private UdpClient socket;
        private Thread receiver;
        private volatile bool running;
        private readonly ConcurrentQueue<byte[]> inbox = new ConcurrentQueue<byte[]>();
        private bool wanted, bridgeConnected, lastReported, warnedControl;
        private float lastState = -999f;

        /// <summary>The simulated F37 (-beltsim), null with the real bridge.</summary>
        public FitShowEmulator Simulator { get; private set; }
        private float simPoll;
        private bool simDropped;

        /// <summary>
        /// Tests (-e2e): no bridge is launched, nothing is sent or received — real devices can never
        /// take part. Straps are simulated with <see cref="SimulateStrap"/>.
        /// </summary>
        public static bool Offline { get; set; }

        /// <summary>Tests: a strap connects, exactly as the bridge would report it (T1, N hr|id|name).</summary>
        public void SimulateStrap(string id, string name)
        {
            hrWanted = true;
            inbox.Enqueue(Message('T', "1"));
            inbox.Enqueue(Message('N', $"hr|{id}|{name}"));
        }

        /// <summary>Tests: one heart rate measurement from the simulated strap (flags 0, 8-bit bpm).</summary>
        public void SimulateBeat(int bpm) => inbox.Enqueue(new byte[] { (byte)'R', 0, (byte)Math.Clamp(bpm, 0, 255) });

        private static byte[] Message(char kind, string text)
        {
            var b = Encoding.UTF8.GetBytes(text);
            var m = new byte[b.Length + 1]; m[0] = (byte)kind; Buffer.BlockCopy(b, 0, m, 1, b.Length);
            return m;
        }

        /// <summary>Simulator: the BLE link drops (true) or comes back (false) — tests and the D key.</summary>
        public bool SimulatedDrop { get => simDropped; set => simDropped = value; }
        private int simViolations;

        /// <summary>Simulator violations over all scenes of this app run (the simulator is new in every scene) — tests.</summary>
        public static int SimViolationsTotal { get; private set; }

        private void CountSimViolations()
        {
            if (Simulator == null || Simulator.Violations <= simViolations) return;
            SimViolationsTotal += Simulator.Violations - simViolations;
            simViolations = Simulator.Violations;
            Debug.LogError($"[BeltSim] VERSTOSS: Steuerbefehl bei nicht laufendem Band (der echte F37 wäre gestartet) – {simViolations}×");
        }

        private void Awake()
        {
            if (Array.IndexOf(Jogging.Core.Args.All, "-beltsim") >= 0)
            {
                Simulator = new FitShowEmulator();
                Debug.Log("[Jogging] Band-Simulator aktiv (-beltsim): B Start/Stopp · ↑↓ Tempo · Bild↑/↓ Steigung · S Clip · D Verbindung");
            }
        }

        private void Start()
        {
            if (connectOnStart) Connect(treadmillWantedId);
            if (connectHeartRateOnStart) ConnectHeartRate(hrWantedId);
        }

        /// <summary>Find the heart rate sensor when the scene starts (from the app settings; set before Start).</summary>
        public bool ConnectHeartRateOnStart { get => connectHeartRateOnStart; set => connectHeartRateOnStart = value; }

        /// <summary>Devices to connect at the start (set before Start): the family's treadmill, the runner's strap.</summary>
        /// <summary>The strap asked for (empty = the first one found).</summary>
        public string HeartRateWantedId => hrWantedId;
        /// <summary>The treadmill asked for (empty = the first one found).</summary>
        public string TreadmillWantedId => treadmillWantedId;

        public void SetPreferred(string treadmillId, string heartRateId) { treadmillWantedId = treadmillId ?? ""; hrWantedId = heartRateId ?? ""; }
        private bool connectHeartRateOnStart;

        public void Connect() => Connect(treadmillWantedId);

        /// <summary>Connect the treadmill with this id (empty = the first one found).</summary>
        public void Connect(string deviceId)
        {
            wanted = true;
            treadmillWantedId = deviceId ?? "";
            if (Simulator != null) { StatusText = Jogging.Core.Loc.T("Simulator F37"); Protocol = "FitShow"; TreadmillDeviceId = SimId; TreadmillDeviceName = "F37 (Simulator)"; return; }
            if (!Core.Platform.HasMacBridge && !NativeBle.Available) { StatusText = Jogging.Core.Loc.T("Laufband auf diesem Gerät nicht verfügbar"); return; }
            StatusText = Jogging.Core.Loc.T("Starte Bluetooth-Bridge…");
            OpenSocket();
            LaunchBridge();
            SendWithId('C', treadmillWantedId); tmAskTime = Time.unscaledTime;
        }

        public void Disconnect()
        {
            wanted = false;
            Send((byte)'X');
            StatusText = Jogging.Core.Loc.T("Getrennt");
        }

        /// <summary>Find and connect a heart rate sensor (chest strap, watch in HR broadcast mode).</summary>
        public void ConnectHeartRate() => ConnectHeartRate(hrWantedId);

        /// <summary>Connect the heart rate sensor with this id (empty = the first one found).</summary>
        public void ConnectHeartRate(string deviceId)
        {
            hrWanted = true;
            hrWantedId = deviceId ?? "";
            if (!Core.Platform.HasMacBridge && !NativeBle.Available) { HeartRateStatus = Jogging.Core.Loc.T("Pulsgurt auf diesem Gerät nicht verfügbar"); return; }
            if (HeartRateDeviceId != "" && hrWantedId != "" && HeartRateDeviceId != hrWantedId) { HeartRateDeviceId = ""; HeartRateDeviceName = ""; }
            HeartRateStatus = Jogging.Core.Loc.T("Suche Pulsgurt …");
            OpenSocket();
            LaunchBridge();
            SendWithId('H', hrWantedId); hrAskTime = Time.unscaledTime;
        }

        private void SendWithId(char cmd, string id)
        {
            var b = Encoding.UTF8.GetBytes(id ?? "");
            var msg = new byte[b.Length + 1];
            msg[0] = (byte)cmd;
            Buffer.BlockCopy(b, 0, msg, 1, b.Length);
            SendRaw(msg);
        }

        public void DisconnectHeartRate()
        {
            hrWanted = false;
            Send((byte)'Y');
            HeartRateStatus = Jogging.Core.Loc.T("Getrennt");
        }

        /// <summary>Connect when the scene starts (from the app settings; set before Start).</summary>
        public bool ConnectOnStart { get => connectOnStart; set => connectOnStart = value; }

        /// <summary>True while connecting or connected (the runner wants the treadmill).</summary>
        public bool Wanted => wanted;

        /// <summary>Gate for anything that can move the belt. Off by default.</summary>
        public bool AllowBeltControl { get => allowBeltControl; set => allowBeltControl = value; }

        /// <summary>
        /// Read-only FitShow queries (SYS_INFO / SYS_STATUS) — they never move the belt, so they are
        /// allowed even while belt control is disabled. Anything else is rejected.
        /// </summary>
        public void SendQuery(byte[] frame)
        {
            if (!FitShowParser.IsFitShowFrame(frame)) return;
            if (frame[1] != FitShowParser.CmdSysInfo && frame[1] != FitShowParser.CmdSysStatus) return;
            if (Simulator != null) { SimReply(Simulator.Handle(frame)); return; }
            var msg = new byte[frame.Length + 1];
            msg[0] = (byte)'W';
            Buffer.BlockCopy(frame, 0, msg, 1, frame.Length);
            SendRaw(msg);
        }

        public void WriteControlPoint(byte[] data)
        {
            if (!allowBeltControl)
            {
                if (!warnedControl) { Debug.Log("[Jogging] Bandsteuerung deaktiviert (allowBeltControl=false) — nur Lesen."); warnedControl = true; }
                return;
            }
            // Only commands in the protocol the bridge identified: FTMS bytes are garbage for FitShow
            // and vice versa, and nothing goes out before the protocol is known.
            bool fitShow = FitShowParser.IsFitShowFrame(data);
            if (Protocol == "FitShow" ? !fitShow : (Protocol != "FTMS" || fitShow)) return;
            if (Simulator != null) { if (!simDropped) SimReply(Simulator.Handle(data)); return; }
            var msg = new byte[data.Length + 1];
            msg[0] = (byte)'W';
            Buffer.BlockCopy(data, 0, msg, 1, data.Length);
            SendRaw(msg);
        }

        // ------------------------------------------------------------------ bridge process
        private void LaunchBridge()
        {
            if (Offline) return;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            string path = bridgeApp.StartsWith("~")
                ? Environment.GetFolderPath(Environment.SpecialFolder.Personal) + bridgeApp.Substring(1)
                : bridgeApp;
            if (!System.IO.Directory.Exists(path) && !InstallEmbeddedBridge(path))
            {
                StatusText = Jogging.Core.Loc.T("Bridge fehlt – Tools/MacBleBridge/build.sh ausführen");
                Debug.LogWarning("[Jogging] BLE-Bridge nicht gefunden: " + path);
                return;
            }
            try
            {
                // -g: don't steal focus. 'open' is a no-op if the bridge already runs. In the background
                // (" &"): waiting for LaunchServices took up to 4 s on every scene start, before the first
                // frame; the connect request is repeated every 3 s until the bridge answers.
                Core.Shell.Run("/usr/bin/open -g " + Core.Shell.Quote(path) + " >/dev/null 2>&1 &"); // (Process.Start doesn't work in IL2CPP players)
            }
            catch (Exception e) { Debug.LogWarning("[Jogging] Bridge-Start fehlgeschlagen: " + e.Message); }
#endif
        }

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        // The standalone macOS build ships the bridge in Jogging.app/Contents/Resources. macOS only
        // lets apps from a real Applications folder get Bluetooth permission, so copy it to
        // ~/Applications on first run (the user then allows it once in Privacy → Bluetooth).
        private bool InstallEmbeddedBridge(string target)
        {
            string embedded = System.IO.Path.Combine(Application.dataPath, "Resources", "JoggingBleScan.app");
            if (!System.IO.Directory.Exists(embedded)) return false;
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                Core.Shell.Run("/bin/cp -R " + Core.Shell.Quote(embedded) + " " + Core.Shell.Quote(target));
                bool ok = System.IO.Directory.Exists(target);
                if (ok)
                {
                    StatusText = Jogging.Core.Loc.T("Bridge installiert – Bluetooth erlauben (Datenschutz → Bluetooth)");
                    Debug.Log("[Jogging] BLE-Bridge nach " + target + " installiert.");
                }
                return ok;
            }
            catch (Exception e) { Debug.LogWarning("[Jogging] Bridge-Installation fehlgeschlagen: " + e.Message); return false; }
        }
#else
        private bool InstallEmbeddedBridge(string target) => false;
#endif

        // ------------------------------------------------------------------ UDP
        private void OpenSocket()
        {
            if (Offline) return;
            if (!Core.Platform.HasMacBridge) { NativeBle.Start(); return; } // iPad/Android: the native plugin instead of UDP
            if (socket != null) return;
            try
            {
                socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, ToUnityPort));
                running = true;
                receiver = new Thread(ReceiveLoop) { IsBackground = true, Name = "BleBridgeRx" };
                receiver.Start();
            }
            catch (Exception e)
            {
                StatusText = Jogging.Core.Loc.T("UDP-Port belegt");
                Debug.LogError($"[Jogging] UDP {ToUnityPort} nicht nutzbar: {e.Message}");
                socket = null;
            }
        }

        private void ReceiveLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try { inbox.Enqueue(socket.Receive(ref any)); }
                catch (SocketException) { if (!running) break; }
                catch (ObjectDisposedException) { break; }
            }
        }

        private void Send(byte cmd) => SendRaw(new[] { cmd });

        private void SendRaw(byte[] msg)
        {
            if (Offline) return;
            if (!Core.Platform.HasMacBridge) { NativeBle.Send(msg); return; }
            try
            {
                using var tx = new UdpClient();
                tx.Send(msg, msg.Length, new IPEndPoint(IPAddress.Loopback, ToBridgePort));
            }
            catch (Exception e) { Debug.LogWarning("[Jogging] Bridge-Send: " + e.Message); }
        }

        // ------------------------------------------------------------------ main thread
        private void SimReply(byte[] reply)
        {
            if (reply != null && wanted && !simDropped) TreadmillDataReceived?.Invoke(reply);
        }

        // The simulated belt: keys = the belt's buttons; polled once a second like the bridge does.
        private void UpdateSimulator()
        {
            var sim = Simulator;
            if (Input.GetKeyDown(KeyCode.B)) sim.PressStartStop();
            if (Input.GetKeyDown(KeyCode.UpArrow)) sim.PressSpeed(+0.5f);
            if (Input.GetKeyDown(KeyCode.DownArrow)) sim.PressSpeed(-0.5f);
            if (Input.GetKeyDown(KeyCode.PageUp)) sim.PressIncline(+1);
            if (Input.GetKeyDown(KeyCode.PageDown)) sim.PressIncline(-1);
            if (Input.GetKeyDown(KeyCode.S)) sim.ToggleSafetyClip();
            if (Input.GetKeyDown(KeyCode.D)) { simDropped = !simDropped; Debug.Log($"[BeltSim] Verbindung {(simDropped ? "weg" : "wieder da")}"); }
            sim.Tick(Time.unscaledDeltaTime);
            if (!simDropped) { lastState = Time.unscaledTime; bridgeConnected = true; }
            simPoll -= Time.unscaledDeltaTime;
            if (simPoll <= 0f) { simPoll = 1f; SimReply(sim.Handle(FitShowParser.StatusPoll)); }
            while (sim.Log.Count > 0) { Debug.Log("[BeltSim] ← " + sim.Log[0]); sim.Log.RemoveAt(0); }
            CountSimViolations();
        }

        private void Update()
        {
            if (Simulator != null && wanted) UpdateSimulator();
            byte[] newestStatus = null;
            if (!Core.Platform.HasMacBridge && !Offline) NativeBle.Poll(m => inbox.Enqueue(m)); // iPad/Android plugin
            while (inbox.TryDequeue(out var m))
            {
                if (m.Length == 0) continue;
                var payload = new byte[m.Length - 1];
                Buffer.BlockCopy(m, 1, payload, 0, payload.Length);
                // With the simulated belt the real bridge only serves the heart rate sensor: its
                // treadmill messages ("not connected", frames, protocol) must not override the simulator.
                char kind = (char)m[0];
                if (Simulator != null && (kind == 'S' || kind == 'D' || kind == 'P' || kind == 'L'
                    || (kind == 'N' && payload.Length > 1 && payload[0] == (byte)'t'))) { if (kind == 'S') lastState = Time.unscaledTime; continue; }
                switch ((char)m[0])
                {
                    case 'D':
                        // Status frames: only the newest per frame goes on (see below). After a main-thread
                        // stall the queue holds several; an old RUNNING one must not be judged "now" — the
                        // belt may have stopped since, and FitShow's TARGET_OR_RUN would start it again.
                        if (Protocol == "iConsole") { foreach (var f in icAssembler.Add(payload)) newestStatus = f; break; } // arrives in pieces
                        if (IsStatusFrame(payload)) newestStatus = payload;
                        else if (wanted) TreadmillDataReceived?.Invoke(payload);
                        break;
                    case 'S':
                        lastState = Time.unscaledTime;
                        bridgeConnected = payload.Length > 0 && payload[0] == (byte)'1';
                        if (wanted && !bridgeConnected && StatusText.StartsWith(Jogging.Core.Loc.T("Verbunden"))) StatusText = Jogging.Core.Loc.T("Suche Laufband…");
                        break;
                    case 'P': Protocol = Encoding.UTF8.GetString(payload); break;
                    case 'R': if (hrWanted) HeartRateReceived?.Invoke(payload); break;
                    case 'G': GripHeartRateReceived?.Invoke(payload); break;
                    case 'F': // device list (StartDeviceSearch): "kind|id|name|rssi"
                        {
                            var fp = Encoding.UTF8.GetString(payload).Split('|');
                            if (fp.Length >= 3 && !Found.Exists(x => x.id == fp[1]))
                                Found.Add(new FoundDevice { kind = fp[0], id = fp[1], name = fp[2], rssi = fp.Length > 3 && int.TryParse(fp[3], out int rv) ? rv : 0 });
                        }
                        break;
                    case 'E': if (payload.Length > 1) { var rest = new byte[payload.Length - 1]; Buffer.BlockCopy(payload, 1, rest, 0, rest.Length); FtmsRangeReceived?.Invoke(payload[0], rest); } break;
                    case 'T': hrBridgeConnected = payload.Length > 0 && payload[0] == (byte)'1'; break;
                    case 'N':
                        var parts = Encoding.UTF8.GetString(payload).Split('|');
                        if (parts.Length >= 3)
                        {
                            if (parts[0] == "hr") { HeartRateDeviceId = parts[1]; HeartRateDeviceName = parts[2]; }
                            else { TreadmillDeviceId = parts[1]; TreadmillDeviceName = parts[2]; }
                            Debug.Log($"[Jogging] Gerät verbunden: {(parts[0] == "hr" ? "Pulsgurt" : "Laufband")} „{parts[2]}“ ({parts[1]})");
                            DeviceConnected?.Invoke(parts[0], parts[1], parts[2]);
                        }
                        break;
                    case 'M':
                        var hl = Encoding.UTF8.GetString(payload);
                        Debug.Log("[BLE-Bridge] Puls: " + hl);
                        if (hrWanted) HeartRateStatus = hl;
                        break;
                    case 'L':
                        var line = Encoding.UTF8.GetString(payload);
                        Debug.Log("[BLE-Bridge] " + line);
                        if (wanted) StatusText = line;
                        break;
                }
            }
            if (newestStatus != null && wanted) TreadmillDataReceived?.Invoke(newestStatus);

            // Heartbeat to the Mac bridge: without it for 60 s it lets go of the treadmill and quits (app gone)
            if (Core.Platform.HasMacBridge && Simulator == null && !Offline && (wanted || hrWanted) && Time.unscaledTime - heartbeatTime > 5f)
            { heartbeatTime = Time.unscaledTime; SendRaw(new[] { (byte)'K' }); }

            // No word from the bridge for 6 s although a device is wanted (it quit — e.g. a newer version was
            // installed — or crashed): start it again ('open' does nothing if it runs).
            if ((wanted || hrWanted) && Simulator == null && !Offline && Core.Platform.HasMacBridge
                && Time.unscaledTime - lastState > 6f && Time.unscaledTime - relaunchTime > 10f)
            { relaunchTime = Time.unscaledTime; LaunchBridge(); }

            // The bridge may not listen yet right after its start (or restarted): ask again while searching.
            if (hrWanted && !HeartRateConnected && Time.unscaledTime - hrAskTime > 3f) { SendWithId('H', hrWantedId); hrAskTime = Time.unscaledTime; }
            // … and also while connected but protocol or device unknown: after an app restart the bridge is
            // still connected and answers the first request with protocol + device — if that answer was
            // missed, nothing else would ever tell the app it is a FitShow belt.
            if (wanted && (!IsConnected || Protocol == "" || TreadmillDeviceId == "") && Time.unscaledTime - tmAskTime > 3f)
            { SendWithId('C', treadmillWantedId); tmAskTime = Time.unscaledTime; }

            bool now = IsConnected;
            if (now != lastReported)
            {
                lastReported = now;
                if (now) StatusText = Simulator != null ? Jogging.Core.Loc.T("Simulator F37 (FitShow)") : Jogging.Core.Loc.F("Verbunden ({0})", ProtocolName(Protocol));
                ConnectionChanged?.Invoke(now);
            }
        }

        // A frame that describes the belt's current state: FitShow SYS_STATUS, or any FTMS treadmill data.
        private static bool IsStatusFrame(byte[] d) =>
            !FitShowParser.IsFitShowFrame(d) || (d.Length > 1 && d[1] == FitShowParser.CmdSysStatus);

        private void OnApplicationQuit()
        {
            if (Simulator == null) Send((byte)'Q'); // stop the helper with the game
        }

        private void OnDestroy()
        {
            CountSimViolations(); // the last frames of this scene still count
            if (Simulator != null) { if (Simulator.Violations > 0) Debug.LogError($"[BeltSim] {Simulator.Violations} Steuerbefehl(e) bei nicht laufendem Band – der echte F37 wäre gestartet!"); return; }
            // A scene change must not stop the helper: that dropped the Bluetooth link to belt and strap on
            // every run start (and raced with the next scene's launch). It quits with the game only.
            running = false;
            try { socket?.Close(); } catch { }
            socket = null;
        }
    }
}
