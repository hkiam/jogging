// JoggingBleBridge (Windows) — the Windows counterpart of Tools/MacBleBridge/main.swift: talks Bluetooth LE to
// the treadmill and a heart-rate strap (WinRT Windows.Devices.Bluetooth) and relays the raw frames to the
// Jogging app over UDP (localhost), with the same messages. Unity's Windows player has no Bluetooth LE.
// In addition it speaks the app's announcements (Windows speech), since Windows has no "say".
//
// UDP  bridge -> Unity  127.0.0.1:47520   'D'+frame | 'S'+"1"/"0" (connected) | 'L'+text | 'P'+protocol
//                                        'R'+HR measurement | 'G'+treadmill hand-grip pulse | 'T'+"1"/"0" (HR connected)
//                                        'M'+HR text | 'N'+"tm|<id>|<name>" / "hr|<id>|<name>" | 'F'+"kind|id|name|rssi"
//                                        'E'+[1|2]+range (FTMS speed/incline range)
//      Unity -> bridge  127.0.0.1:47521   'W'+bytes (write control) | 'C'[+id] (connect) | 'F' (list devices 20 s)
//                                        'X' (disconnect) | 'H'[+id] (heart rate) | 'Y' (disconnect it) | 'K' (heartbeat)
//                                        'Q' (quit) | 'V'+"de|text" / "en|text" (speak) | 'Z' (stop speaking)
// Device ids are the Bluetooth address as 12 hex digits.
//
// Untested on a real Windows PC so far (written and built on a Mac) – see docs/Entwicklung.md.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Jogging.WinBleBridge
{
    /// <summary>Runs everything on one thread, like the Swift bridge's main queue: no locks needed.</summary>
    internal sealed class Loop : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback, object)> queue = new BlockingCollection<(SendOrPostCallback, object)>();
        public override void Post(SendOrPostCallback d, object state) => queue.Add((d, state));
        public void Run() { SetSynchronizationContext(this); foreach (var (d, s) in queue.GetConsumingEnumerable()) d(s); }
        public void Do(Action a) => Post(_ => a(), null);
    }

    internal static class Uuids
    {
        public static Guid Short(ushort id) => BluetoothUuidHelper.FromShortId(id);
        public static readonly Guid FTMS = Short(0x1826), FTMS_DATA = Short(0x2ACD), FTMS_CP = Short(0x2AD9);
        public static readonly Guid FTMS_SPEED_RANGE = Short(0x2AD4), FTMS_INCL_RANGE = Short(0x2AD5);
        public static readonly Guid FS_SERVICE = Short(0xFFF0), FS_NOTIFY = Short(0xFFF1), FS_WRITE = Short(0xFFF2);
        public static readonly Guid HR_SERVICE = Short(0x180D), HR_MEASURE = Short(0x2A37);
        public static readonly Guid WP_SERVICE = Short(0xFE00), WP_NOTIFY = Short(0xFE01), WP_WRITE = Short(0xFE02);
        public static readonly Guid RSC_SERVICE = Short(0x1814), RSC_MEASURE = Short(0x2A53);
        public static readonly Guid IC_UART = new Guid("49535343-FE7D-4AE5-8FA9-9FAFD205E455");
        public static readonly Guid IC_WRITE = new Guid("49535343-8841-43F4-A8D4-ECBE34729BB3");
        public static readonly Guid IC_NOTIFY = new Guid("49535343-1E4D-4BD9-BA61-23C647249616");
        public static readonly Guid PAFERS = new Guid("72D70001-501F-46F7-95F9-23846EE1ABA3");
    }

    internal sealed class Bridge
    {
        private const int ToUnityPort = 47520, FromUnityPort = 47521;
        private static readonly byte[] FitShowStatusPoll = { 0x02, 0x51, 0x51, 0x03 };
        private static readonly byte[] WalkingPadAskStats = { 0xF7, 0xA2, 0x00, 0x00, 0xA2, 0xFD };
        private static readonly byte[] LifeSpanOps = { 0x82, 0x91, 0x85 };
        private static readonly HashSet<string> SupportedTreadmills = new HashSet<string> { "FitShow", "FTMS", "WalkingPad", "iConsole", "LifeSpan" };

        private readonly Loop loop;
        private readonly UdpClient output = new UdpClient();
        private readonly IPEndPoint unity = new IPEndPoint(IPAddress.Loopback, ToUnityPort);
        private UdpClient input;
        private readonly BluetoothLEAdvertisementWatcher watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };

        // advertisement data per device (names come in the scan response, separately)
        private readonly Dictionary<ulong, string> names = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, HashSet<Guid>> services = new Dictionary<ulong, HashSet<Guid>>();

        private BluetoothLEDevice peripheral, hr;
        private ulong peripheralAddr, hrAddr;
        private GattCharacteristic writeChar;
        private readonly List<GattCharacteristic> subscribed = new List<GattCharacteristic>();
        private readonly List<GattDeviceService> openServices = new List<GattDeviceService>();
        private string proto = "", takenKind = "";
        private bool connected, hrConnected, wanted = true, hrWanted, scanning;
        private string treadmillId, hrId;
        private ulong rscCandidate;
        private DateTime scanSince = DateTime.Now, lastCommand = DateTime.MinValue, discoverUntil = DateTime.MinValue, lastFromApp = DateTime.Now;
        private readonly HashSet<ulong> listed = new HashSet<ulong>();
        private byte icAddr = 0x01, lsAsked;
        private int lsOp;
        private Timer pollTimer;
        private bool connecting, hrConnecting;

        // speech
        private readonly MediaPlayer player = new MediaPlayer();
        private SpeechSynthesizer synth;

        public Bridge(Loop loop) { this.loop = loop; }

        public void Start()
        {
            try { input = new UdpClient(new IPEndPoint(IPAddress.Loopback, FromUnityPort)); }
            catch (SocketException) { Environment.Exit(0); } // another bridge runs already
            new Thread(ReceiveLoop) { IsBackground = true, Name = "udp" }.Start();
            watcher.Received += (w, a) => loop.Do(() => OnAdvertisement(a));
            watcher.Stopped += (w, a) => loop.Do(() => { scanning = false; if (a.Error != BluetoothError.Success) Log("ERROR Bluetooth: " + a.Error); });
            Every(2000, SendState);
            // The app is gone (closed hard, crashed): without its heartbeat for 60 s let go of the devices and quit.
            Every(10000, () =>
            {
                if ((DateTime.Now - lastFromApp).TotalSeconds <= 60) return;
                Log("no message from the app for 60 s – releasing the treadmill and quitting");
                Quit();
            });
            // A newer bridge was installed next to the app: step aside, the app starts the new one.
            string exe = Environment.ProcessPath;
            DateTime built = exe != null && File.Exists(exe) ? File.GetLastWriteTimeUtc(exe) : DateTime.MinValue;
            Every(10000, () =>
            {
                if (exe == null || !File.Exists(exe) || File.GetLastWriteTimeUtc(exe) == built) return;
                Log("new bridge version installed – quitting");
                Quit();
            });
            StartScan();
        }

        private void Every(int ms, Action a) => new Timer(_ => loop.Do(a), null, ms, ms);
        private void After(int ms, Action a) { Timer t = null; t = new Timer(_ => { loop.Do(a); t.Dispose(); }, null, ms, Timeout.Infinite); }

        // ---- UDP ---------------------------------------------------------------------------
        private void Send(char type, byte[] payload)
        {
            var d = new byte[payload.Length + 1]; d[0] = (byte)type; Buffer.BlockCopy(payload, 0, d, 1, payload.Length);
            try { output.Send(d, d.Length, unity); } catch { }
        }
        private void Send(char type, string s) => Send(type, Encoding.UTF8.GetBytes(s));
        private void Log(string s) { Console.WriteLine(s); Send('L', s); }
        private void HrLog(string s) { Console.WriteLine("HR " + s); Send('M', s); }
        private void SendState() { Send('S', connected ? "1" : "0"); Send('T', hrConnected ? "1" : "0"); }

        private void ReceiveLoop()
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                byte[] d;
                try { d = input.Receive(ref from); } catch { return; }
                if (d.Length == 0) continue;
                char cmd = (char)d[0];
                var payload = d.Skip(1).ToArray();
                loop.Do(() => Handle(cmd, payload));
            }
        }

        private static string IdOf(ulong addr) => addr.ToString("X12");
        private static string IdFrom(byte[] payload) { var s = Encoding.UTF8.GetString(payload).Trim(); return s.Length == 0 ? null : s; }

        private void Handle(char cmd, byte[] payload)
        {
            lastFromApp = DateTime.Now;
            switch (cmd)
            {
                case 'K': break; // the app's heartbeat
                case 'W': // a command from the app (through its safety layer)
                    if (peripheral != null && writeChar != null)
                    {
                        var opt = writeChar.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse) ? GattWriteOption.WriteWithoutResponse : GattWriteOption.WriteWithResponse;
                        _ = writeChar.WriteValueAsync(payload.AsBuffer(), opt);
                        lastCommand = DateTime.Now;
                        Log("→ " + string.Join(" ", payload.Select(b => b.ToString("X2"))));
                    }
                    break;
                case 'C':
                {
                    string id = IdFrom(payload);
                    if (peripheral != null && id != null && IdOf(peripheralAddr) != id) DropTreadmill(); // another one wanted
                    treadmillId = id;
                    wanted = true;
                    if (!connected && peripheral == null && !connecting) StartScan();
                    else if (connected && peripheral != null && (id == null || IdOf(peripheralAddr) == id))
                    { Send('P', proto); SendDevice("tm", peripheralAddr); SendState(); }
                    break;
                }
                case 'F': // device list: report every treadmill, sensor and strap nearby for 20 s
                    discoverUntil = DateTime.Now.AddSeconds(20); listed.Clear();
                    StartScan();
                    After(20500, StopScanIfDone);
                    break;
                case 'X': wanted = false; DropTreadmill(); StopScanIfDone(); break;
                case 'H':
                {
                    string id = IdFrom(payload);
                    if (hr != null && id != null && IdOf(hrAddr) != id) DropHr();
                    hrId = id;
                    hrWanted = true;
                    if (hr == null && !hrConnecting) StartScan();
                    else if (hrConnected && (id == null || IdOf(hrAddr) == id)) { SendDevice("hr", hrAddr); SendState(); }
                    break;
                }
                case 'Y': hrWanted = false; DropHr(); StopScanIfDone(); break;
                case 'Q': Quit(); break;
                case 'V': Speak(Encoding.UTF8.GetString(payload)); break;
                case 'Z': try { player.Pause(); } catch { } break;
            }
        }

        private void Quit()
        {
            DropTreadmill(); DropHr();
            After(500, () => Environment.Exit(0));
        }

        private void SendDevice(string kind, ulong addr) => Send('N', $"{kind}|{IdOf(addr)}|{(names.TryGetValue(addr, out var n) ? n : "")}");

        // ---- scanning ------------------------------------------------------------------------
        private bool NeedTreadmill => wanted && peripheral == null && !connecting;
        private bool NeedHr => hrWanted && hr == null && !hrConnecting;
        private bool Discovering => DateTime.Now < discoverUntil;

        private void StartScan()
        {
            if (!(NeedTreadmill || NeedHr || Discovering)) return;
            if (NeedTreadmill) Log("scanning…");
            if (NeedHr) HrLog("suche Pulsgurt …");
            scanSince = DateTime.Now;
            if (scanning) return;
            try { watcher.Start(); scanning = true; }
            catch (Exception e) { Log("ERROR Bluetooth not available: " + e.Message); }
        }

        private void StopScanIfDone()
        {
            if (NeedTreadmill || NeedHr || Discovering || !scanning) return;
            try { watcher.Stop(); } catch { }
            scanning = false;
        }

        /// <summary>What a device is, from its advertised name and services (same rules as the Mac bridge).</summary>
        private static string DeviceKind(string name, ICollection<Guid> uuids)
        {
            string n = (name ?? "").ToUpperInvariant();
            if (n.StartsWith("LIFESPAN")) return "LifeSpan";
            if (n.StartsWith("EW-TM")) return "eHealth";
            if (n.StartsWith("RZ_TREADMIL")) return "SmartTreadmill";
            if (n.StartsWith("PAFERS") || uuids.Contains(Uuids.PAFERS)) return "Pafers";
            if (uuids.Contains(Uuids.FTMS)) return "FTMS";
            string[] ic = { "I-CONSOLE", "ICONSOLE", "I-RUNNING", "TOORX", "V-RUN", "K80_", "DKN RUN", "REEBOK", "ADIDAS" };
            if (ic.Any(p => n.StartsWith(p)) || uuids.Contains(Uuids.IC_UART) || (n.StartsWith("TREADMILL") && n.Length > 9 && n.Substring(9).All(char.IsDigit))) return "iConsole";
            if (uuids.Contains(Uuids.FS_SERVICE) || n.StartsWith("FS-")) return "FitShow";
            if (uuids.Contains(Uuids.WP_SERVICE) || n.StartsWith("WALKINGPAD") || n.StartsWith("KS-")) return "WalkingPad";
            if (uuids.Contains(Uuids.RSC_SERVICE)) return "RSC";
            if (uuids.Contains(Uuids.HR_SERVICE)) return "HR";
            return null;
        }

        private void OnAdvertisement(BluetoothLEAdvertisementReceivedEventArgs a)
        {
            ulong addr = a.BluetoothAddress;
            if (!services.TryGetValue(addr, out var set)) services[addr] = set = new HashSet<Guid>();
            foreach (var u in a.Advertisement.ServiceUuids) set.Add(u);
            if (!string.IsNullOrEmpty(a.Advertisement.LocalName)) names[addr] = a.Advertisement.LocalName;
            string name = names.TryGetValue(addr, out var nm) ? nm : "";
            string kind = DeviceKind(name, set);
            string id = IdOf(addr);
            if (Discovering && kind != null && listed.Add(addr)) Send('F', $"{kind}|{id}|{name}|{a.RawSignalStrengthInDBm}");
            bool looksTm = kind != null && kind != "HR" && kind != "RSC";
            if (kind != null && looksTm && !SupportedTreadmills.Contains(kind)) return; // listed, but never connected
            if (NeedHr && addr != peripheralAddr && set.Contains(Uuids.HR_SERVICE) && !looksTm && (hrId == null || id == hrId))
            {
                HrLog("gefunden: " + (name.Length == 0 ? "Pulsgurt" : name));
                _ = ConnectHr(addr);
                return;
            }
            bool chosen = treadmillId != null && id == treadmillId;
            // a foot pod only when chosen, or when no treadmill has shown up after 10 s of searching
            if (NeedTreadmill && addr != hrAddr && !looksTm && set.Contains(Uuids.RSC_SERVICE) && (treadmillId == null || chosen))
            {
                if (chosen || (DateTime.Now - scanSince).TotalSeconds > 10) { _ = ConnectTreadmill(addr, name, a.RawSignalStrengthInDBm); return; }
                if (rscCandidate == 0)
                {
                    rscCandidate = addr;
                    After(10000, () => { if (NeedTreadmill && rscCandidate != 0) _ = ConnectTreadmill(rscCandidate, names.TryGetValue(rscCandidate, out var rn) ? rn : "Laufsensor", 0); });
                }
                return;
            }
            if (!NeedTreadmill || addr == hrAddr || !looksTm || !(treadmillId == null || chosen)) return;
            takenKind = kind ?? "";
            _ = ConnectTreadmill(addr, name, a.RawSignalStrengthInDBm);
        }

        // ---- the treadmill -------------------------------------------------------------------
        private async Task ConnectTreadmill(ulong addr, string name, short rssi)
        {
            rscCandidate = 0;
            connecting = true;
            StopScanIfDone();
            Log($"found {name} rssi={rssi}");
            try
            {
                var dev = await BluetoothLEDevice.FromBluetoothAddressAsync(addr);
                if (dev == null) throw new Exception("not reachable");
                peripheral = dev; peripheralAddr = addr;
                dev.ConnectionStatusChanged += (d, _) => loop.Do(() =>
                {
                    if (d == peripheral && d.ConnectionStatus == BluetoothConnectionStatus.Disconnected && connected) { Log("disconnected"); ResetTreadmill(); }
                });
                Log("connected " + name);
                var all = await dev.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                if (all.Status != GattCommunicationStatus.Success) throw new Exception("services: " + all.Status);
                var svcs = all.Services.ToList();
                openServices.AddRange(svcs);
                Log("services: " + string.Join(", ", svcs.Select(s => s.Uuid.ToString("D").ToUpperInvariant())));
                GattDeviceService Svc(Guid u) => svcs.FirstOrDefault(s => s.Uuid == u);

                GattDeviceService main; Guid[] want;
                if (takenKind == "iConsole" && (Svc(Uuids.IC_UART) ?? Svc(Uuids.FS_SERVICE)) is GattDeviceService ic)
                {
                    main = ic; proto = "iConsole"; want = new[] { Uuids.FS_NOTIFY, Uuids.FS_WRITE, Uuids.IC_NOTIFY, Uuids.IC_WRITE };
                    string n = (name ?? "").ToUpperInvariant(); // console address per model
                    icAddr = n.StartsWith("REEBOK") ? (byte)0x32 : n.StartsWith("ADIDAS") ? (byte)0x5B : n.StartsWith("TREADMILL") ? (byte)0x2E : (byte)0x01;
                }
                else if (takenKind == "LifeSpan" && Svc(Uuids.FS_SERVICE) is GattDeviceService ls) { main = ls; proto = "LifeSpan"; want = new[] { Uuids.FS_NOTIFY, Uuids.FS_WRITE }; }
                else if (Svc(Uuids.FS_SERVICE) is GattDeviceService fs) { main = fs; proto = "FitShow"; want = new[] { Uuids.FS_NOTIFY, Uuids.FS_WRITE }; }
                else if (Svc(Uuids.FTMS) is GattDeviceService ft) { main = ft; proto = "FTMS"; want = new[] { Uuids.FTMS_DATA, Uuids.FTMS_CP, Uuids.FTMS_SPEED_RANGE, Uuids.FTMS_INCL_RANGE }; }
                else if (Svc(Uuids.WP_SERVICE) is GattDeviceService wp) { main = wp; proto = "WalkingPad"; want = new[] { Uuids.WP_NOTIFY, Uuids.WP_WRITE }; }
                else if (Svc(Uuids.RSC_SERVICE) is GattDeviceService rs) { main = rs; proto = "RSC"; want = new[] { Uuids.RSC_MEASURE }; }
                else { Log("no FitShow/FTMS/WalkingPad/RSC service"); DropTreadmill(); ResetTreadmill(); return; }

                var chars = await main.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                foreach (var ch in chars.Characteristics.Where(c => want.Contains(c.Uuid)))
                {
                    if (ch.Uuid == Uuids.FS_NOTIFY || ch.Uuid == Uuids.FTMS_DATA || ch.Uuid == Uuids.WP_NOTIFY || ch.Uuid == Uuids.RSC_MEASURE || ch.Uuid == Uuids.IC_NOTIFY || ch.Uuid == Uuids.FTMS_CP)
                        await Subscribe(ch, dev);
                    if (ch.Uuid == Uuids.FS_WRITE || ch.Uuid == Uuids.FTMS_CP || ch.Uuid == Uuids.WP_WRITE || ch.Uuid == Uuids.IC_WRITE) writeChar = ch;
                    if (ch.Uuid == Uuids.FTMS_SPEED_RANGE || ch.Uuid == Uuids.FTMS_INCL_RANGE) // the belt's limits
                    {
                        var r = await ch.ReadValueAsync(BluetoothCacheMode.Uncached);
                        if (r.Status == GattCommunicationStatus.Success)
                            Send('E', new[] { (byte)(ch.Uuid == Uuids.FTMS_SPEED_RANGE ? 1 : 2) }.Concat(r.Value.ToArray()).ToArray());
                    }
                }
                // hand grips: many treadmills offer their pulse as a heart rate service of their own → 'G'
                if (Svc(Uuids.HR_SERVICE) is GattDeviceService hs)
                {
                    var hc = await hs.GetCharacteristicsForUuidAsync(Uuids.HR_MEASURE, BluetoothCacheMode.Uncached);
                    foreach (var ch in hc.Characteristics) await Subscribe(ch, dev);
                }

                connected = true; connecting = false;
                Log($"ready ({proto})");
                Send('P', proto); SendDevice("tm", addr); SendState();
                StartPolling();
            }
            catch (Exception e)
            {
                Log("connect failed: " + e.Message);
                DropTreadmill(); ResetTreadmill();
            }
        }

        private async Task Subscribe(GattCharacteristic ch, BluetoothLEDevice dev)
        {
            var props = ch.CharacteristicProperties;
            var mode = props.HasFlag(GattCharacteristicProperties.Notify) ? GattClientCharacteristicConfigurationDescriptorValue.Notify
                     : props.HasFlag(GattCharacteristicProperties.Indicate) ? GattClientCharacteristicConfigurationDescriptorValue.Indicate
                     : GattClientCharacteristicConfigurationDescriptorValue.None;
            if (mode == GattClientCharacteristicConfigurationDescriptorValue.None) return;
            ch.ValueChanged += (c, a) => { var d = a.CharacteristicValue.ToArray(); loop.Do(() => OnValue(c, d, dev)); };
            await ch.WriteClientCharacteristicConfigurationDescriptorAsync(mode);
            subscribed.Add(ch);
        }

        private void OnValue(GattCharacteristic ch, byte[] d, BluetoothLEDevice dev)
        {
            if (ch.Uuid == Uuids.FTMS_CP) { Log("FTMS CP " + string.Join(" ", d.Select(b => b.ToString("x2")))); return; } // an answer, not a data frame
            if (ch.Uuid == Uuids.HR_MEASURE) { Send(dev == hr ? 'R' : 'G', d); return; }
            if (dev != peripheral) return;
            if (proto == "LifeSpan") { Send('D', new[] { lsAsked }.Concat(d).ToArray()); return; } // the answer carries no op: prefix it
            Send('D', d);
        }

        private void StartPolling()
        {
            var w = writeChar;
            if (w == null) return;
            var opt = w.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse;
            if (proto == "iConsole" || proto == "LifeSpan")
            {
                if (proto == "iConsole") _ = w.WriteValueAsync(new byte[] { 0xF0, 0xA0, 0x01, 0x01, 0x92 }.AsBuffer(), opt); // hello (read only)
                pollTimer = new Timer(_ => loop.Do(() =>
                {
                    if (writeChar != w) return;
                    if (proto == "iConsole") // status query F0 A2 addr D3 + sum
                    {
                        byte[] q = { 0xF0, 0xA2, icAddr, 0xD3 };
                        _ = w.WriteValueAsync(q.Concat(new[] { (byte)(q.Sum(b => b) & 0xFF) }).ToArray().AsBuffer(), opt);
                    }
                    else // LifeSpan: one read-only query per tick (speed, state, distance)
                    {
                        lsAsked = LifeSpanOps[lsOp % LifeSpanOps.Length]; lsOp++;
                        _ = w.WriteValueAsync(new byte[] { 0xA1, lsAsked, 0, 0, 0 }.AsBuffer(), opt);
                    }
                }), null, 350, 350);
            }
            if (proto == "FitShow" || proto == "WalkingPad")
            {
                var poll = proto == "FitShow" ? FitShowStatusPoll : WalkingPadAskStats;
                pollTimer = new Timer(_ => loop.Do(() =>
                {
                    if (writeChar != w || (DateTime.Now - lastCommand).TotalSeconds < 1.2) return; // a command is on its way
                    _ = w.WriteValueAsync(poll.AsBuffer(), opt);
                }), null, 1000, 1000);
            }
        }

        private void DropTreadmill()
        {
            pollTimer?.Dispose(); pollTimer = null;
            foreach (var ch in subscribed.ToList()) if (ch.Service.Device == peripheral) subscribed.Remove(ch);
            foreach (var s in openServices.ToList()) if (s.Device == peripheral) { s.Dispose(); openServices.Remove(s); }
            peripheral?.Dispose(); // Windows drops the connection when nothing holds the device any more
        }

        private void ResetTreadmill()
        {
            peripheral = null; peripheralAddr = 0; writeChar = null; connected = false; connecting = false; proto = ""; rscCandidate = 0;
            SendState();
            After(2000, StartScan);
        }

        // ---- the heart-rate strap ------------------------------------------------------------
        private async Task ConnectHr(ulong addr)
        {
            hrConnecting = true;
            StopScanIfDone();
            try
            {
                var dev = await BluetoothLEDevice.FromBluetoothAddressAsync(addr);
                if (dev == null) throw new Exception("not reachable");
                hr = dev; hrAddr = addr;
                dev.ConnectionStatusChanged += (d, _) => loop.Do(() =>
                {
                    if (d == hr && d.ConnectionStatus == BluetoothConnectionStatus.Disconnected && hrConnected) { HrLog("getrennt"); ResetHr(); }
                });
                HrLog("verbunden " + (names.TryGetValue(addr, out var n) ? n : ""));
                var s = await dev.GetGattServicesForUuidAsync(Uuids.HR_SERVICE, BluetoothCacheMode.Uncached);
                var svc = s.Services.FirstOrDefault();
                if (svc == null) { HrLog("kein Puls-Dienst"); DropHr(); ResetHr(); return; }
                openServices.Add(svc);
                var cs = await svc.GetCharacteristicsForUuidAsync(Uuids.HR_MEASURE, BluetoothCacheMode.Uncached);
                foreach (var ch in cs.Characteristics) await Subscribe(ch, dev);
                hrConnected = true; hrConnecting = false;
                HrLog("bereit");
                SendDevice("hr", addr); SendState();
            }
            catch (Exception e)
            {
                HrLog("Verbindung fehlgeschlagen: " + e.Message);
                DropHr(); ResetHr();
            }
        }

        private void DropHr()
        {
            foreach (var s in openServices.ToList()) if (s.Device == hr) { s.Dispose(); openServices.Remove(s); }
            hr?.Dispose();
        }

        private void ResetHr()
        {
            hr = null; hrAddr = 0; hrConnected = false; hrConnecting = false;
            SendState();
            After(2000, StartScan);
        }

        // ---- speech --------------------------------------------------------------------------
        private async void Speak(string msg)
        {
            int bar = msg.IndexOf('|');
            string lang = bar > 0 ? msg.Substring(0, bar) : "de", text = bar > 0 ? msg.Substring(bar + 1) : msg;
            try
            {
                synth ??= new SpeechSynthesizer();
                var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith(lang, StringComparison.OrdinalIgnoreCase));
                if (voice != null) synth.Voice = voice;
                var stream = await synth.SynthesizeTextToStreamAsync(text);
                player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
                player.Play();
            }
            catch (Exception e) { Log("speech failed: " + e.Message); }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            var loop = new Loop();
            var bridge = new Bridge(loop);
            loop.Do(bridge.Start);
            loop.Run();
        }
    }
}
