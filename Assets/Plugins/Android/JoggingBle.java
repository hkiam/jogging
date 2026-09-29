// JoggingBle — Bluetooth to the treadmill and the heart rate strap on Android tablets.
// The same logic and one-letter messages as the Mac's Swift bridge (Tools/MacBleBridge/main.swift),
// but instead of UDP the messages wait in a queue that Unity polls every frame (NativeBle.cs):
//   plugin → Unity  'D'+frame | 'S'"1"/"0" | 'P'"FitShow"/"FTMS" | 'N'"tm|id|name"/"hr|id|name"
//                   'R'+HR measurement | 'T'"1"/"0" | 'M'+HR text | 'L'+text
//   Unity → plugin  'W'+bytes | 'C'[+id] | 'X' | 'H'[+id] | 'Y' | 'Q'
// FitShow (Sportstech F37: FFF0, FFF1 notify / FFF2 write, polled once a second) or FTMS (1826).
// Android runs one GATT operation per device at a time: every descriptor/characteristic write goes
// through a small queue per connection. Nothing here decides what is sent to the belt: BeltSafety in C#.
package local.jogging.ble;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothGatt;
import android.bluetooth.BluetoothGattCallback;
import android.bluetooth.BluetoothGattCharacteristic;
import android.bluetooth.BluetoothGattDescriptor;
import android.bluetooth.BluetoothGattService;
import android.bluetooth.BluetoothManager;
import android.bluetooth.BluetoothProfile;
import android.bluetooth.le.BluetoothLeScanner;
import android.bluetooth.le.ScanCallback;
import android.bluetooth.le.ScanRecord;
import android.bluetooth.le.ScanResult;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.location.LocationManager;
import android.os.Handler;
import android.os.Looper;
import android.os.ParcelUuid;

import java.io.ByteArrayOutputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;

@SuppressLint("MissingPermission") // the permissions are asked for in NativeBle.cs before start()
public class JoggingBle {
    private static UUID u(String s) { return UUID.fromString("0000" + s + "-0000-1000-8000-00805f9b34fb"); }
    private static final UUID FTMS = u("1826"), FTMS_DATA = u("2acd"), FTMS_CP = u("2ad9");
    private static final UUID FS_SERVICE = u("fff0"), FS_NOTIFY = u("fff1"), FS_WRITE = u("fff2");
    private static final UUID HR_SERVICE = u("180d"), HR_MEASURE = u("2a37");
    private static final UUID CCCD = u("2902");
    // KingSmith WalkingPad (older models; read only: only the stats query is sent) and foot pods (RSC, read only)
    private static final UUID WP_SERVICE = u("fe00"), WP_NOTIFY = u("fe01"), WP_WRITE = u("fe02");
    private static final UUID RSC_SERVICE = u("1814"), RSC_MEASURE = u("2a53");
    // FTMS ranges (read once), iConsole+ (Changyow: FFF0 or Microchip UART) and LifeSpan (FFF0), both read only
    private static final UUID FTMS_SPEED_RANGE = u("2ad4"), FTMS_INCL_RANGE = u("2ad5");
    private static final UUID IC_UART = UUID.fromString("49535343-fe7d-4ae5-8fa9-9fafd205e455"),
            IC_WRITE = UUID.fromString("49535343-8841-43f4-a8d4-ecbe34729bb3"), IC_NOTIFY = UUID.fromString("49535343-1e4d-4bd9-ba61-23c647249616");
    private static final UUID PAFERS = UUID.fromString("72d70001-501f-46f7-95f9-23846ee1aba3");
    private static final byte[] LIFESPAN_OPS = { (byte) 0x82, (byte) 0x91, (byte) 0x85 }; // speed, state, distance — read-only queries
    private static final java.util.Set<String> SUPPORTED = new java.util.HashSet<>(java.util.Arrays.asList("FitShow", "FTMS", "WalkingPad", "iConsole", "LifeSpan"));

    /** What a device is, from its name and services; FFF0 devices that are not FitShow are told apart by name. */
    static String deviceKind(String name, List<ParcelUuid> uuids) {
        String n = name.toUpperCase(java.util.Locale.ROOT);
        if (n.startsWith("LIFESPAN")) return "LifeSpan";
        if (n.startsWith("EW-TM")) return "eHealth";
        if (n.startsWith("RZ_TREADMIL")) return "SmartTreadmill";
        if (n.startsWith("PAFERS") || has(uuids, PAFERS)) return "Pafers";
        if (has(uuids, FTMS)) return "FTMS";
        for (String p : new String[] { "I-CONSOLE", "ICONSOLE", "I-RUNNING", "TOORX", "V-RUN", "K80_", "DKN RUN", "REEBOK", "ADIDAS" })
            if (n.startsWith(p)) return "iConsole";
        if (has(uuids, IC_UART) || n.matches("TREADMILL\\d+")) return "iConsole";
        if (has(uuids, FS_SERVICE) || n.startsWith("FS-")) return "FitShow";
        if (has(uuids, WP_SERVICE) || n.startsWith("WALKINGPAD") || n.startsWith("KS-")) return "WalkingPad";
        if (has(uuids, RSC_SERVICE)) return "RSC";
        if (has(uuids, HR_SERVICE)) return "HR";
        return null;
    }

    private static final byte[] WALKINGPAD_ASK_STATS = { (byte) 0xF7, (byte) 0xA2, 0x00, 0x00, (byte) 0xA2, (byte) 0xFD };
    private static final byte[] FITSHOW_STATUS_POLL = { 0x02, 0x51, 0x51, 0x03 };
    // Android 13+ reports notifications through the new callback with the value; the old one is for older devices
    private static final boolean OLD_API = android.os.Build.VERSION.SDK_INT < 33;

    private static JoggingBle instance;

    private final Context context;
    private final Handler main = new Handler(Looper.getMainLooper());
    private final BluetoothAdapter adapter;
    private final ArrayList<byte[]> outbox = new ArrayList<>();

    // treadmill
    private BluetoothGatt tm;
    private BluetoothGattCharacteristic writeChar;
    private boolean fitShow, connected, wanted;
    private String proto = ""; // "FitShow" | "FTMS" | "WalkingPad" | "RSC"
    private BluetoothDevice rscCandidate; // a foot pod: taken only if no treadmill shows up
    private String takenKind = "";
    private long lastCommandMs;         // last command from the app (the poll pauses briefly after it)      // deviceKind of the treadmill being connected
    private long discoverUntil;         // device list: report everything seen until then
    private final java.util.HashSet<String> listed = new java.util.HashSet<>();
    private byte icAddr = 0x01;         // iConsole console address (per model)
    private int lsOp; private byte lsAsked;
    private String treadmillId;
    private final Ops tmOps = new Ops();
    // heart rate strap (own slot)
    private BluetoothGatt hr;
    private boolean hrConnected, hrWanted;
    private String hrId;
    private final Ops hrOps = new Ops();

    private boolean scanning;
    private long scanStarted;
    private static final long SCAN_RESTART_MS = 5 * 60 * 1000; // Android 7+ mutes unfiltered scans after 30 min

    public static synchronized JoggingBle start(Activity activity) {
        if (instance == null) instance = new JoggingBle(activity.getApplicationContext());
        return instance;
    }

    private JoggingBle(Context ctx) {
        context = ctx;
        BluetoothManager m = (BluetoothManager) ctx.getSystemService(Context.BLUETOOTH_SERVICE);
        adapter = m != null ? m.getAdapter() : null;
        if (adapter == null) log("ERROR kein Bluetooth auf diesem Gerät");
        else if (!adapter.isEnabled()) log("ERROR Bluetooth aus");
        main.postDelayed(stateTick, 2000);
        // Bluetooth switched off and on again: the scanner and the connections die without a callback
        ctx.registerReceiver(new BroadcastReceiver() {
            @Override public void onReceive(Context c, Intent i) {
                int st = i.getIntExtra(BluetoothAdapter.EXTRA_STATE, BluetoothAdapter.ERROR);
                main.post(() -> {
                    if (st == BluetoothAdapter.STATE_OFF) bluetoothOff();
                    else if (st == BluetoothAdapter.STATE_ON) { log("Bluetooth an"); startScan(); }
                });
            }
        }, new IntentFilter(BluetoothAdapter.ACTION_STATE_CHANGED));
    }

    private void bluetoothOff() {
        log("ERROR Bluetooth aus");
        scanning = false;
        main.removeCallbacks(poll);
        if (tm != null) tm.close();
        if (hr != null) hr.close();
        tm = null; writeChar = null; connected = false; fitShow = false; tmOps.clear();
        hr = null; hrConnected = false; hrOps.clear();
        sendState();
    }

    private final Runnable stateTick = new Runnable() {
        @Override public void run() {
            sendState();
            // a long search: start it afresh now and then (else it goes quiet after 30 min)
            if (scanning && System.currentTimeMillis() - scanStarted > SCAN_RESTART_MS) restartScan();
            main.postDelayed(this, 2000);
        }
    };

    // ---- queue to Unity --------------------------------------------------------------------
    private void send(char type, byte[] payload) {
        byte[] m = new byte[payload.length + 1];
        m[0] = (byte) type;
        System.arraycopy(payload, 0, m, 1, payload.length);
        synchronized (outbox) { outbox.add(m); if (outbox.size() > 2000) outbox.remove(0); }
    }
    private void send(char type, String s) { send(type, s.getBytes(StandardCharsets.UTF_8)); }
    private void log(String s) { send('L', s); }
    private void hrLog(String s) { send('M', s); }
    private void sendState() { send('S', connected ? "1" : "0"); send('T', hrConnected ? "1" : "0"); }
    private void sendDevice(String kind, BluetoothDevice d) {
        String name = d.getName() != null ? d.getName() : "";
        send('N', kind + "|" + d.getAddress() + "|" + name);
    }

    /** All queued messages as [len lo, len hi, bytes]… (Unity, once per frame). */
    public byte[] poll() {
        ByteArrayOutputStream b = new ByteArrayOutputStream();
        synchronized (outbox) {
            for (byte[] m : outbox) { b.write(m.length & 0xFF); b.write((m.length >> 8) & 0xFF); b.write(m, 0, m.length); }
            outbox.clear();
        }
        return b.toByteArray();
    }

    // ---- commands from Unity ---------------------------------------------------------------
    public void send(final byte[] msg) {
        if (msg == null || msg.length == 0) return;
        main.post(() -> handle(msg));
    }

    private void handle(byte[] msg) {
        byte[] payload = new byte[msg.length - 1];
        System.arraycopy(msg, 1, payload, 0, payload.length);
        String id = new String(payload, StandardCharsets.UTF_8).trim();
        switch ((char) msg[0]) {
            case 'W': // a command from the app (through its safety layer); the status poll waits a moment after it
                if (tm != null && writeChar != null) { write(tm, tmOps, writeChar, payload); lastCommandMs = System.currentTimeMillis(); }
                break;
            case 'C':
                if (tm != null && !id.isEmpty() && !tm.getDevice().getAddress().equals(id)) dropTm(); // other one wanted
                treadmillId = id.isEmpty() ? null : id;
                boolean newNeed = !wanted; wanted = true;
                if (!connected && tm == null) { if (newNeed) restartScan(); else startScan(); }
                else if (connected && tm != null && (treadmillId == null || tm.getDevice().getAddress().equals(treadmillId))) {
                    send('P', proto); sendDevice("tm", tm.getDevice()); sendState();
                }
                break;
            case 'F': // device list: report every treadmill, sensor and strap nearby for 20 s (connects nothing)
                discoverUntil = System.currentTimeMillis() + 20000; listed.clear();
                restartScan();
                main.postDelayed(this::stopScanIfDone, 20500);
                break;
            case 'X':
                wanted = false; if (tm != null) dropTm(); stopScanIfDone();
                break;
            case 'H':
                if (hr != null && !id.isEmpty() && !hr.getDevice().getAddress().equals(id)) dropHr(); // other runner's strap
                hrId = id.isEmpty() ? null : id;
                boolean newHr = !hrWanted; hrWanted = true;
                if (hr == null) { if (newHr) restartScan(); else startScan(); }
                else if (hrConnected && (hrId == null || hr.getDevice().getAddress().equals(hrId))) { sendDevice("hr", hr.getDevice()); sendState(); }
                break;
            case 'Y':
                hrWanted = false; if (hr != null) dropHr(); stopScanIfDone();
                break;
            case 'Q':
                wanted = false; hrWanted = false;
                if (tm != null) dropTm();
                if (hr != null) dropHr();
                stopScanIfDone();
                break;
        }
    }

    // Connected: disconnect (the callback closes and resets). Still connecting: close and reset now.
    private void dropTm() {
        if (connected) { tm.disconnect(); return; }
        BluetoothGatt g = tm; tm = null; g.close(); resetTm();
    }
    private void dropHr() {
        if (hrConnected) { hr.disconnect(); return; }
        BluetoothGatt g = hr; hr = null; g.close(); resetHr();
    }

    // ---- scanning --------------------------------------------------------------------------
    private boolean needTreadmill() { return wanted && tm == null; }
    private boolean needHr() { return hrWanted && hr == null; }
    private boolean discovering() { return System.currentTimeMillis() < discoverUntil; }

    private void startScan() {
        if (adapter == null || scanning || !(needTreadmill() || needHr() || discovering())) return;
        if (!adapter.isEnabled()) { log("ERROR Bluetooth aus"); return; }
        if (!locationOk()) { log("ERROR Standort ist aus – Android braucht ihn für die Bluetooth-Suche"); return; } // Unity asks again every 3 s
        BluetoothLeScanner s = adapter.getBluetoothLeScanner();
        if (s == null) return;
        if (needTreadmill()) log("scanning…");
        if (needHr()) hrLog("suche Pulsgurt …");
        try { s.startScan(scanCallback); scanning = true; scanStarted = System.currentTimeMillis(); }
        catch (SecurityException e) { log("ERROR Bluetooth nicht erlaubt"); }
    }

    // A new search (new device wanted, or a long one): results already seen are reported again.
    private void restartScan() {
        if (scanning) {
            BluetoothLeScanner s = adapter != null ? adapter.getBluetoothLeScanner() : null;
            try { if (s != null) s.stopScan(scanCallback); } catch (SecurityException ignored) { }
            scanning = false;
        }
        startScan();
    }

    // Android 11 and older find nothing while the system location is off (even with the permission).
    private boolean locationOk() {
        if (android.os.Build.VERSION.SDK_INT >= 31 || android.os.Build.VERSION.SDK_INT < 28) return true;
        LocationManager lm = (LocationManager) context.getSystemService(Context.LOCATION_SERVICE);
        return lm == null || lm.isLocationEnabled();
    }

    private void stopScanIfDone() {
        if (!scanning || needTreadmill() || needHr() || discovering()) return;
        BluetoothLeScanner s = adapter != null ? adapter.getBluetoothLeScanner() : null;
        try { if (s != null) s.stopScan(scanCallback); } catch (SecurityException ignored) { }
        scanning = false;
    }

    private final ScanCallback scanCallback = new ScanCallback() {
        @Override public void onScanResult(int type, final ScanResult r) { main.post(() -> discovered(r)); }
        @Override public void onScanFailed(int code) { main.post(() -> { scanning = false; log("scan failed " + code); }); }
    };

    private void discovered(ScanResult r) {
        BluetoothDevice d = r.getDevice();
        ScanRecord rec = r.getScanRecord();
        String name = rec != null && rec.getDeviceName() != null ? rec.getDeviceName() : (d.getName() != null ? d.getName() : "");
        List<ParcelUuid> uuids = rec != null ? rec.getServiceUuids() : null;
        String kind = deviceKind(name, uuids);
        if (discovering() && kind != null && listed.add(d.getAddress())) send('F', kind + "|" + d.getAddress() + "|" + name + "|" + r.getRssi());
        boolean hasHr = has(uuids, HR_SERVICE), hasTm = kind != null && !kind.equals("HR") && !kind.equals("RSC");
        if (hasTm && !SUPPORTED.contains(kind)) return; // listed, but never connected
        String addr = d.getAddress();
        boolean isTm = tm != null && tm.getDevice().getAddress().equals(addr);
        boolean isHr = hr != null && hr.getDevice().getAddress().equals(addr);

        if (needHr() && !isTm && hasHr && !hasTm && (hrId == null || addr.equals(hrId))) {
            hrLog("gefunden: " + (name.isEmpty() ? "Pulsgurt" : name));
            hr = d.connectGatt(context, false, hrCallback, BluetoothDevice.TRANSPORT_LE);
            stopScanIfDone();
            return;
        }
        boolean chosen = treadmillId != null && addr.equals(treadmillId);
        // A foot pod only when chosen, or when no treadmill has shown up after 10 s of searching
        if (needTreadmill() && !isHr && !hasTm && has(uuids, RSC_SERVICE) && (treadmillId == null || chosen)) {
            if (chosen || System.currentTimeMillis() - scanStarted > 10000) { take(d, name, r.getRssi()); return; }
            if (rscCandidate == null) {
                rscCandidate = d;
                main.postDelayed(() -> { if (needTreadmill() && rscCandidate != null) take(rscCandidate, "Laufsensor", 0); }, 10000);
            }
            return;
        }
        if (!needTreadmill() || isHr || !hasTm || (treadmillId != null && !chosen)) return;
        takenKind = kind;
        take(d, name, r.getRssi());
    }

    private void take(BluetoothDevice d, String name, int rssi) {
        rscCandidate = null;
        log("found " + name + " rssi=" + rssi);
        tm = d.connectGatt(context, false, tmCallback, BluetoothDevice.TRANSPORT_LE);
        stopScanIfDone();
    }

    private static boolean has(List<ParcelUuid> list, UUID id) {
        if (list == null) return false;
        for (ParcelUuid p : list) if (p.getUuid().equals(id)) return true;
        return false;
    }

    // ---- treadmill -------------------------------------------------------------------------
    private final BluetoothGattCallback tmCallback = new BluetoothGattCallback() {
        @Override public void onConnectionStateChange(final BluetoothGatt g, int status, final int state) {
            main.post(() -> {
                if (state == BluetoothProfile.STATE_CONNECTED) { log("connected " + g.getDevice().getName()); g.discoverServices(); }
                else if (state == BluetoothProfile.STATE_DISCONNECTED) { log("disconnected"); g.close(); if (g == tm) resetTm(); }
            });
        }
        @Override public void onServicesDiscovered(final BluetoothGatt g, int status) { main.post(() -> { if (g == tm) tmServices(g); }); }
        @Override public void onDescriptorWrite(final BluetoothGatt g, BluetoothGattDescriptor d, int status) { main.post(() -> { if (g == tm) tmOps.done(); }); }
        @Override public void onCharacteristicWrite(final BluetoothGatt g, BluetoothGattCharacteristic c, int status) { main.post(() -> { if (g == tm) tmOps.done(); }); }
        @SuppressWarnings("deprecation")
        @Override public void onCharacteristicRead(BluetoothGatt g, BluetoothGattCharacteristic c, int status) { if (OLD_API) rangeRead(g, c, c.getValue()); }
        @Override public void onCharacteristicRead(BluetoothGatt g, BluetoothGattCharacteristic c, byte[] value, int status) { rangeRead(g, c, value); }
        @SuppressWarnings("deprecation")
        @Override public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c) { if (OLD_API) tmValue(c, c.getValue().clone()); }
        @Override public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c, byte[] value) { tmValue(c, value.clone()); }
    };

    // FTMS range characteristics → 'E' kind(1 speed, 2 incline) + bytes
    private void rangeRead(BluetoothGatt g, BluetoothGattCharacteristic c, byte[] v) {
        if (v != null) {
            byte kind = FTMS_SPEED_RANGE.equals(c.getUuid()) ? (byte) 1 : FTMS_INCL_RANGE.equals(c.getUuid()) ? (byte) 2 : 0;
            if (kind != 0) { byte[] m = new byte[v.length + 1]; m[0] = kind; System.arraycopy(v, 0, m, 1, v.length); send('E', m); }
        }
        main.post(() -> { if (g == tm) tmOps.done(); });
    }

    // Treadmill data → 'D'; FTMS control point answers are not data frames, only logged.
    private void tmValue(BluetoothGattCharacteristic c, byte[] v) {
        if (HR_MEASURE.equals(c.getUuid())) { send('G', v); return; } // the treadmill's own pulse (hand grips)
        if (proto.equals("LifeSpan")) { byte[] m = new byte[v.length + 1]; m[0] = lsAsked; System.arraycopy(v, 0, m, 1, v.length); send('D', m); return; } // the answer carries no op
        if (FTMS_CP.equals(c.getUuid())) { StringBuilder h = new StringBuilder("FTMS CP"); for (byte b : v) h.append(String.format(" %02x", b)); log(h.toString()); }
        else send('D', v);
    }

    private void tmServices(BluetoothGatt g) {
        StringBuilder svl = new StringBuilder("services:"); for (BluetoothGattService x : g.getServices()) svl.append(' ').append(x.getUuid().toString().substring(4, 8)); log(svl.toString());
        BluetoothGattService fs = g.getService(FS_SERVICE), ft = g.getService(FTMS), wp = g.getService(WP_SERVICE), rs = g.getService(RSC_SERVICE), ic = g.getService(IC_UART);
        BluetoothGattCharacteristic notify = null;
        if (takenKind.equals("iConsole") && (ic != null || fs != null)) {
            fitShow = false; proto = "iConsole";
            BluetoothGattService sv = ic != null ? ic : fs;
            notify = sv.getCharacteristic(ic != null ? IC_NOTIFY : FS_NOTIFY); writeChar = sv.getCharacteristic(ic != null ? IC_WRITE : FS_WRITE);
            String n = g.getDevice().getName() != null ? g.getDevice().getName().toUpperCase(java.util.Locale.ROOT) : ""; // address per model (qdomyos-zwift)
            icAddr = (byte) (n.startsWith("REEBOK") ? 0x32 : n.startsWith("ADIDAS") ? 0x5B : n.startsWith("TREADMILL") ? 0x2E : 0x01);
        }
        else if (takenKind.equals("LifeSpan") && fs != null) { fitShow = false; proto = "LifeSpan"; notify = fs.getCharacteristic(FS_NOTIFY); writeChar = fs.getCharacteristic(FS_WRITE); }
        else if (fs != null) { fitShow = true; proto = "FitShow"; notify = fs.getCharacteristic(FS_NOTIFY); writeChar = fs.getCharacteristic(FS_WRITE); }
        else if (ft != null) { fitShow = false; proto = "FTMS"; notify = ft.getCharacteristic(FTMS_DATA); writeChar = ft.getCharacteristic(FTMS_CP); }
        else if (wp != null) { fitShow = false; proto = "WalkingPad"; notify = wp.getCharacteristic(WP_NOTIFY); writeChar = wp.getCharacteristic(WP_WRITE); }
        else if (rs != null) { fitShow = false; proto = "RSC"; notify = rs.getCharacteristic(RSC_MEASURE); writeChar = null; }
        else { log("no FitShow/FTMS/WalkingPad/RSC service"); g.disconnect(); return; }
        if (notify != null) enableNotify(g, tmOps, notify);
        // hand grips: many treadmills offer their pulse as a heart rate service of their own → 'G'
        BluetoothGattService hs = g.getService(HR_SERVICE);
        BluetoothGattCharacteristic hc = hs != null ? hs.getCharacteristic(HR_MEASURE) : null;
        if (hc != null) enableNotify(g, tmOps, hc);
        if (proto.equals("FTMS") && writeChar != null) enableNotify(g, tmOps, writeChar); // FTMS: the control point needs indications before any write
        if (proto.equals("FTMS")) for (UUID ru : new UUID[] { FTMS_SPEED_RANGE, FTMS_INCL_RANGE }) { // the belt's limits, read once
            final BluetoothGattCharacteristic rc = ft.getCharacteristic(ru);
            if (rc != null) tmOps.add(() -> { if (!g.readCharacteristic(rc)) tmOps.done(); });
        }
        tmOps.add(() -> {                    // after the notification is on: ready
            connected = true;
            log("ready (" + proto + ")");
            send('P', proto);
            sendDevice("tm", g.getDevice());
            sendState();
            if (proto.equals("iConsole")) write(g, tmOps, writeChar, new byte[] { (byte) 0xF0, (byte) 0xA0, 0x01, 0x01, (byte) 0x92 }); // hello (read only)
            if (!proto.equals("FTMS") && !proto.equals("RSC")) main.postDelayed(poll, 1000);
            tmOps.done();
        });
    }

    private final Runnable poll = new Runnable() {
        @Override public void run() {
            if (!connected || tm == null || writeChar == null || proto.equals("FTMS") || proto.equals("RSC")) return;
            if (tmOps.idle() && System.currentTimeMillis() - lastCommandMs > 1200) { // a command waiting / just sent goes first
                byte[] q;
                if (proto.equals("FitShow")) q = FITSHOW_STATUS_POLL;
                else if (proto.equals("WalkingPad")) q = WALKINGPAD_ASK_STATS;
                else if (proto.equals("iConsole")) { q = new byte[] { (byte) 0xF0, (byte) 0xA2, icAddr, (byte) 0xD3, 0 }; q[4] = (byte) ((0xF0 + 0xA2 + (icAddr & 0xFF) + 0xD3) & 0xFF); }
                else { lsAsked = LIFESPAN_OPS[lsOp++ % LIFESPAN_OPS.length]; q = new byte[] { (byte) 0xA1, lsAsked, 0, 0, 0 }; }
                write(tm, tmOps, writeChar, q);
            }
            main.postDelayed(this, proto.equals("FitShow") || proto.equals("WalkingPad") ? 1000 : 350);
        }
    };

    private void resetTm() {
        main.removeCallbacks(poll);
        tm = null; writeChar = null; connected = false; fitShow = false; proto = ""; rscCandidate = null; tmOps.clear();
        sendState();
        main.postDelayed(this::startScan, 2000);
    }

    // ---- heart rate strap ------------------------------------------------------------------
    private final BluetoothGattCallback hrCallback = new BluetoothGattCallback() {
        @Override public void onConnectionStateChange(final BluetoothGatt g, int status, final int state) {
            main.post(() -> {
                if (state == BluetoothProfile.STATE_CONNECTED) { hrLog("verbunden " + g.getDevice().getName()); g.discoverServices(); }
                else if (state == BluetoothProfile.STATE_DISCONNECTED) { hrLog("getrennt"); g.close(); if (g == hr) resetHr(); }
            });
        }
        @Override public void onServicesDiscovered(final BluetoothGatt g, int status) {
            main.post(() -> {
                if (g != hr) return;
                BluetoothGattService s = g.getService(HR_SERVICE);
                BluetoothGattCharacteristic c = s != null ? s.getCharacteristic(HR_MEASURE) : null;
                if (c == null) { hrLog("kein Puls-Dienst"); g.disconnect(); return; }
                enableNotify(g, hrOps, c);
                hrOps.add(() -> { hrConnected = true; hrLog("bereit"); sendDevice("hr", g.getDevice()); sendState(); hrOps.done(); });
            });
        }
        @Override public void onDescriptorWrite(final BluetoothGatt g, BluetoothGattDescriptor d, int status) { main.post(() -> { if (g == hr) hrOps.done(); }); }
        @SuppressWarnings("deprecation")
        @Override public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c) { if (OLD_API) send('R', c.getValue().clone()); }
        @Override public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c, byte[] value) { send('R', value.clone()); }
    };

    private void resetHr() {
        hr = null; hrConnected = false; hrOps.clear();
        sendState();
        main.postDelayed(this::startScan, 2000);
    }

    // ---- GATT operations, one at a time per connection -------------------------------------
    @SuppressWarnings("deprecation")
    private void enableNotify(final BluetoothGatt g, Ops ops, final BluetoothGattCharacteristic c) {
        ops.add(() -> {
            g.setCharacteristicNotification(c, true);
            BluetoothGattDescriptor d = c.getDescriptor(CCCD);
            if (d == null) { ops.done(); return; }
            boolean indicate = (c.getProperties() & BluetoothGattCharacteristic.PROPERTY_INDICATE) != 0
                            && (c.getProperties() & BluetoothGattCharacteristic.PROPERTY_NOTIFY) == 0;
            d.setValue(indicate ? BluetoothGattDescriptor.ENABLE_INDICATION_VALUE : BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE);
            if (!g.writeDescriptor(d)) ops.done();
        });
    }

    @SuppressWarnings("deprecation")
    private void write(final BluetoothGatt g, Ops ops, final BluetoothGattCharacteristic c, final byte[] value) {
        final long queued = System.currentTimeMillis();
        ops.add(() -> {
            // a command that waited over a second is stale: the safety layer decides afresh with new data
            if (System.currentTimeMillis() - queued > 1000) { ops.done(); return; }
            // without response where the belt offers it (as FitShow apps do), else with response
            boolean noAck = (c.getProperties() & BluetoothGattCharacteristic.PROPERTY_WRITE_NO_RESPONSE) != 0;
            c.setWriteType(noAck ? BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE : BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT);
            c.setValue(value);
            if (!g.writeCharacteristic(c)) ops.done();
        });
    }

    /** One GATT operation at a time; the next starts when the previous one reports back (or after 3 s). */
    private final class Ops {
        private final ArrayDeque<Runnable> queue = new ArrayDeque<>();
        private boolean busy;
        private final Runnable timeout = this::done;

        void add(Runnable op) { if (queue.size() < 50) queue.add(op); next(); }
        boolean idle() { return !busy && queue.isEmpty(); }
        void clear() { queue.clear(); busy = false; main.removeCallbacks(timeout); }
        void done() { main.removeCallbacks(timeout); busy = false; next(); }
        private void next() {
            if (busy || queue.isEmpty()) return;
            busy = true;
            main.postDelayed(timeout, 3000);
            queue.poll().run();
        }
    }
}
