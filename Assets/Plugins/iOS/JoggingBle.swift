// JoggingBle — Bluetooth to the treadmill and the heart rate strap on the iPad.
// The same logic and one-letter messages as the Mac's Swift bridge (Tools/MacBleBridge/main.swift),
// but instead of UDP the messages wait in a queue that Unity polls every frame (NativeBle.cs):
//   plugin → Unity  'D'+frame | 'S'"1"/"0" | 'P'"FitShow"/"FTMS" | 'N'"tm|id|name"/"hr|id|name"
//                   'R'+HR measurement | 'T'"1"/"0" | 'M'+HR text | 'L'+text
//   Unity → plugin  'W'+bytes | 'C'[+id] | 'X' | 'H'[+id] | 'Y' | 'Q'
// FitShow (Sportstech F37: FFF0, FFF1 notify / FFF2 write, polled once a second) or FTMS (1826).
// Nothing here decides what is sent to the belt: that is BeltSafety in C#.
import Foundation
import CoreBluetooth

private let FTMS = CBUUID(string: "1826"), FTMS_DATA = CBUUID(string: "2ACD"), FTMS_CP = CBUUID(string: "2AD9")
private let FS_SERVICE = CBUUID(string: "FFF0"), FS_NOTIFY = CBUUID(string: "FFF1"), FS_WRITE = CBUUID(string: "FFF2")
private let HR_SERVICE = CBUUID(string: "180D"), HR_MEASURE = CBUUID(string: "2A37")
// KingSmith WalkingPad (older models; read only: only the stats query is sent) and foot pods (RSC, read only)
private let WP_SERVICE = CBUUID(string: "FE00"), WP_NOTIFY = CBUUID(string: "FE01"), WP_WRITE = CBUUID(string: "FE02")
private let RSC_SERVICE = CBUUID(string: "1814"), RSC_MEASURE = CBUUID(string: "2A53")
private let walkingPadAskStats: [UInt8] = [0xF7, 0xA2, 0x00, 0x00, 0xA2, 0xFD]
// FTMS ranges (read once), iConsole+ (Changyow: FFF0 or Microchip UART) and LifeSpan (FFF0), both read only
private let FTMS_SPEED_RANGE = CBUUID(string: "2AD4"), FTMS_INCL_RANGE = CBUUID(string: "2AD5")
private let IC_UART = CBUUID(string: "49535343-FE7D-4AE5-8FA9-9FAFD205E455"), IC_WRITE = CBUUID(string: "49535343-8841-43F4-A8D4-ECBE34729BB3"), IC_NOTIFY = CBUUID(string: "49535343-1E4D-4BD9-BA61-23C647249616")
private let PAFERS = CBUUID(string: "72D70001-501F-46F7-95F9-23846EE1ABA3")
private let lifeSpanOps: [UInt8] = [0x82, 0x91, 0x85] // speed, state, distance — read-only queries

/// What a device is, from its advertised name and services (for the device list and auto-connect).
/// Devices on FFF0 that are not FitShow are told apart by name. Unsupported kinds are listed, never connected.
func deviceKind(_ name: String, _ uuids: [CBUUID]) -> String? {
    let n = name.uppercased()
    if n.hasPrefix("LIFESPAN") { return "LifeSpan" }
    if n.hasPrefix("EW-TM") { return "eHealth" }
    if n.hasPrefix("RZ_TREADMIL") { return "SmartTreadmill" }
    if n.hasPrefix("PAFERS") || uuids.contains(PAFERS) { return "Pafers" }
    if uuids.contains(FTMS) { return "FTMS" }
    let ic = ["I-CONSOLE", "ICONSOLE", "I-RUNNING", "TOORX", "V-RUN", "K80_", "DKN RUN", "REEBOK", "ADIDAS"]
    if ic.contains(where: { n.hasPrefix($0) }) || uuids.contains(IC_UART) || (n.hasPrefix("TREADMILL") && n.dropFirst(9).allSatisfy({ $0.isNumber }) && n.count > 9) { return "iConsole" }
    if uuids.contains(FS_SERVICE) || n.hasPrefix("FS-") { return "FitShow" }
    if uuids.contains(WP_SERVICE) || n.hasPrefix("WALKINGPAD") || n.hasPrefix("KS-") { return "WalkingPad" }
    if uuids.contains(RSC_SERVICE) { return "RSC" }
    if uuids.contains(HR_SERVICE) { return "HR" }
    return nil
}
let supportedTreadmills: Set<String> = ["FitShow", "FTMS", "WalkingPad", "iConsole", "LifeSpan"]

private let fitShowStatusPoll: [UInt8] = [0x02, 0x51, 0x51, 0x03]

final class JoggingBle: NSObject, CBCentralManagerDelegate, CBPeripheralDelegate {
    static var shared: JoggingBle?

    private var central: CBCentralManager!
    private var peripheral: CBPeripheral?
    private var writeChar: CBCharacteristic?
    private var fitShow = false
    private var proto = "" // "FitShow" | "FTMS" | "WalkingPad" | "RSC"
    private var rscCandidate: CBPeripheral?   // a foot pod: taken only if no treadmill shows up
    private var scanSince = Date()
    private var lastCommand = Date.distantPast // last command from the app (the poll pauses briefly after it)
    private var takenKind = ""                // deviceKind of the treadmill being connected
    private var discoverUntil = Date.distantPast // device list: report everything seen until then
    private var listed = Set<String>()
    private var icAddr: UInt8 = 0x01          // iConsole console address (per model)
    private var lsOp = 0                      // LifeSpan: next query
    private var lsAsked: UInt8 = 0
    private var connected = false
    private var pollTimer: Timer?
    private var wanted = false
    private var hr: CBPeripheral?
    private var hrConnected = false
    private var hrWanted = false
    private var treadmillId: String? = nil
    private var hrId: String? = nil

    private let lock = NSLock()
    private var outbox = [Data]()

    override init() {
        super.init()
        central = CBCentralManager(delegate: self, queue: .main)
        Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in self?.sendState() }
    }

    // ---- queue to Unity ----------------------------------------------------
    private func send(_ type: Character, _ payload: Data) {
        var d = Data([UInt8(type.asciiValue!)]); d.append(payload)
        lock.lock(); outbox.append(d); if outbox.count > 2000 { outbox.removeFirst(outbox.count - 2000) }; lock.unlock()
    }
    private func log(_ s: String) { send("L", s.data(using: .utf8)!) }
    private func hrLog(_ s: String) { send("M", s.data(using: .utf8)!) }
    private func sendState() {
        send("S", (connected ? "1" : "0").data(using: .utf8)!)
        send("T", (hrConnected ? "1" : "0").data(using: .utf8)!)
    }
    private func sendDevice(_ kind: String, _ p: CBPeripheral) {
        send("N", "\(kind)|\(p.identifier.uuidString)|\(p.name ?? "")".data(using: .utf8)!)
    }

    /// Batch [len lo, len hi, bytes]… into buf; returns the number of bytes written.
    func poll(_ buf: UnsafeMutablePointer<UInt8>, _ capacity: Int) -> Int {
        lock.lock(); defer { lock.unlock() }
        var n = 0, taken = 0
        for m in outbox {
            if n + 2 + m.count > capacity { break }
            buf[n] = UInt8(m.count & 0xFF); buf[n + 1] = UInt8((m.count >> 8) & 0xFF)
            m.copyBytes(to: buf.advanced(by: n + 2), count: m.count)
            n += 2 + m.count; taken += 1
        }
        outbox.removeFirst(taken)
        return n
    }

    // ---- commands from Unity ------------------------------------------------
    func handle(_ msg: Data) {
        guard let first = msg.first else { return }
        let payload = msg.dropFirst()
        switch Character(UnicodeScalar(first)) {
        case "W": // a command from the app (through its safety layer): without response where possible, as FitShow apps
                  // do; the status poll waits a moment so it doesn't cut in before the belt has taken the command
            if let p = peripheral, let w = writeChar {
                let type: CBCharacteristicWriteType = w.properties.contains(.writeWithoutResponse) ? .withoutResponse : .withResponse
                p.writeValue(Data(payload), for: w, type: type)
                lastCommand = Date()
                log("→ " + payload.map { String(format: "%02X", $0) }.joined(separator: " "))
            }
        case "C":
            let id = idFrom(payload)
            if let p = peripheral, id != nil, p.identifier.uuidString != id { central.cancelPeripheralConnection(p) }
            treadmillId = id
            let newNeed = !wanted; wanted = true
            if !connected && peripheral == nil { startScan(fresh: newNeed) }
            else if connected, let p = peripheral, id == nil || p.identifier.uuidString == id {
                send("P", proto.data(using: .utf8)!); sendDevice("tm", p); sendState()
            }
        case "F": // device list: report every treadmill, sensor and strap nearby for 20 s (connects nothing)
            discoverUntil = Date().addingTimeInterval(20); listed.removeAll()
            startScan(fresh: true)
            DispatchQueue.main.asyncAfter(deadline: .now() + 20.5) { [weak self] in self?.stopScanIfDone() }
        case "X": wanted = false; if let p = peripheral { central.cancelPeripheralConnection(p) }; stopScanIfDone()
        case "H":
            let id = idFrom(payload)
            if let h = hr, id != nil, h.identifier.uuidString != id { central.cancelPeripheralConnection(h) }
            hrId = id
            let newHr = !hrWanted; hrWanted = true
            if hr == nil { startScan(fresh: newHr) }
            else if hrConnected, let h = hr, id == nil || h.identifier.uuidString == id { sendDevice("hr", h); sendState() }
        case "Y": hrWanted = false; if let h = hr { central.cancelPeripheralConnection(h) }; stopScanIfDone()
        case "Q":
            wanted = false; hrWanted = false
            if let p = peripheral { central.cancelPeripheralConnection(p) }
            if let h = hr { central.cancelPeripheralConnection(h) }
            stopScanIfDone()
        default: break
        }
    }

    private func idFrom(_ payload: Data) -> String? {
        let s = String(data: payload, encoding: .utf8)?.trimmingCharacters(in: .whitespaces) ?? ""
        return s.isEmpty ? nil : s
    }

    // ---- BLE ---------------------------------------------------------------
    private var needTreadmill: Bool { wanted && peripheral == nil }
    private var needHr: Bool { hrWanted && hr == nil }
    private var discovering: Bool { Date() < discoverUntil }

    /// fresh: a new device is wanted — scan anew, since a running scan reports each device only once.
    private func startScan(fresh: Bool = false) {
        guard central.state == .poweredOn, needTreadmill || needHr || discovering else { return }
        if central.isScanning { if !fresh { return }; central.stopScan() }
        if needTreadmill { log("scanning…") }
        if needHr { hrLog("suche Pulsgurt …") }
        scanSince = Date()
        central.scanForPeripherals(withServices: nil, options: nil)
    }

    private func stopScanIfDone() { if !needTreadmill && !needHr && !discovering { central.stopScan() } }

    func centralManagerDidUpdateState(_ c: CBCentralManager) {
        if c.state != .poweredOn { dropAll() } // Bluetooth off / reset: no disconnect callbacks come
        switch c.state {
        case .poweredOn: startScan()
        case .unauthorized: log("ERROR Bluetooth nicht erlaubt (Einstellungen → Jogging → Bluetooth)")
        case .poweredOff: log("ERROR Bluetooth aus")
        case .unsupported: log("ERROR kein Bluetooth auf diesem Gerät")
        default: break
        }
    }

    func centralManager(_ c: CBCentralManager, didDiscover p: CBPeripheral,
                        advertisementData ad: [String: Any], rssi: NSNumber) {
        let name = (ad[CBAdvertisementDataLocalNameKey] as? String) ?? p.name ?? ""
        let uuids = (ad[CBAdvertisementDataServiceUUIDsKey] as? [CBUUID]) ?? []
        let kind = deviceKind(name, uuids)
        if discovering, let k = kind, !listed.contains(p.identifier.uuidString) {
            listed.insert(p.identifier.uuidString)
            send("F", "\(k)|\(p.identifier.uuidString)|\(name)|\(rssi)".data(using: .utf8)!)
        }
        let looksTm = kind != nil && kind != "HR" && kind != "RSC"
        if let k = kind, looksTm, !supportedTreadmills.contains(k) { return } // listed, but never connected
        if needHr, p != peripheral, uuids.contains(HR_SERVICE), !looksTm, hrId == nil || p.identifier.uuidString == hrId {
            hrLog("gefunden: \(name.isEmpty ? "Pulsgurt" : name)")
            hr = p; p.delegate = self; c.connect(p, options: nil); watchConnect(p); stopScanIfDone()
            return
        }
        let chosen = treadmillId != nil && p.identifier.uuidString == treadmillId
        // A foot pod only when chosen, or when no treadmill has shown up after 10 s of searching
        if needTreadmill, p != hr, !looksTm, uuids.contains(RSC_SERVICE), treadmillId == nil || chosen {
            if chosen || Date().timeIntervalSince(scanSince) > 10 { take(p, name, rssi: rssi); return }
            if rscCandidate == nil {
                rscCandidate = p
                DispatchQueue.main.asyncAfter(deadline: .now() + 10) { [weak self] in
                    guard let self = self, self.needTreadmill, let c = self.rscCandidate else { return }
                    self.take(c, c.name ?? "Laufsensor", rssi: 0)
                }
            }
            return
        }
        guard needTreadmill, p != hr, looksTm, treadmillId == nil || chosen else { return }
        takenKind = kind ?? ""
        take(p, name, rssi: rssi)
    }

    private func take(_ p: CBPeripheral, _ name: String, rssi: NSNumber) {
        rscCandidate = nil
        log("found \(name) rssi=\(rssi)")
        peripheral = p; p.delegate = self; central.connect(p, options: nil); watchConnect(p); stopScanIfDone()
    }

    func centralManager(_ c: CBCentralManager, didConnect p: CBPeripheral) {
        if p == hr { hrLog("verbunden \(p.name ?? "")"); p.discoverServices([HR_SERVICE]); return }
        log("connected \(p.name ?? "?")")
        p.discoverServices([FS_SERVICE, FTMS, WP_SERVICE, RSC_SERVICE, HR_SERVICE, IC_UART])
    }

    func centralManager(_ c: CBCentralManager, didFailToConnect p: CBPeripheral, error: Error?) {
        if p == hr { hrLog("Verbindung fehlgeschlagen"); resetHr(); return }
        log("connect failed: \(error?.localizedDescription ?? "")"); reset()
    }

    func centralManager(_ c: CBCentralManager, didDisconnectPeripheral p: CBPeripheral, error: Error?) {
        if p == hr { hrLog("getrennt"); resetHr(); return }
        log("disconnected"); reset()
    }

    private func dropAll() {
        pollTimer?.invalidate(); pollTimer = nil
        peripheral = nil; writeChar = nil; connected = false; fitShow = false; proto = ""; rscCandidate = nil
        hr = nil; hrConnected = false
        sendState()
    }

    // A connection attempt that never completes (the device went away) blocks its slot: give up after 10 s.
    private func watchConnect(_ p: CBPeripheral) {
        Timer.scheduledTimer(withTimeInterval: 10, repeats: false) { [weak self] _ in
            guard let self = self, p.state != .connected else { return }
            if p == self.peripheral || p == self.hr { self.central.cancelPeripheralConnection(p) } // → didFailToConnect / reset
        }
    }

    private func resetHr() {
        hr = nil; hrConnected = false
        sendState()
        DispatchQueue.main.asyncAfter(deadline: .now() + 2) { [weak self] in self?.startScan() }
    }

    private func reset() {
        pollTimer?.invalidate(); pollTimer = nil
        peripheral = nil; writeChar = nil; connected = false; fitShow = false; proto = ""; rscCandidate = nil
        sendState()
        DispatchQueue.main.asyncAfter(deadline: .now() + 2) { [weak self] in self?.startScan() }
    }

    func peripheral(_ p: CBPeripheral, didDiscoverServices error: Error?) {
        let services = p.services ?? []
        if p != hr { log("services: " + services.map { $0.uuid.uuidString }.joined(separator: ", ")) } // which protocols the belt offers
        if p == hr {
            if let s = services.first(where: { $0.uuid == HR_SERVICE }) { p.discoverCharacteristics([HR_MEASURE], for: s) }
            else { hrLog("kein Puls-Dienst"); central.cancelPeripheralConnection(p) }
            return
        }
        if takenKind == "iConsole", let ic = services.first(where: { $0.uuid == IC_UART || $0.uuid == FS_SERVICE }) {
            fitShow = false; proto = "iConsole"; p.discoverCharacteristics([FS_NOTIFY, FS_WRITE, IC_NOTIFY, IC_WRITE], for: ic)
            let n = (p.name ?? "").uppercased() // console address per model (qdomyos-zwift): Reebok 0x32, Adidas 0x5B, FlowFitness 0x2E
            icAddr = n.hasPrefix("REEBOK") ? 0x32 : n.hasPrefix("ADIDAS") ? 0x5B : n.hasPrefix("TREADMILL") ? 0x2E : 0x01
        }
        else if takenKind == "LifeSpan", let ls = services.first(where: { $0.uuid == FS_SERVICE }) {
            fitShow = false; proto = "LifeSpan"; p.discoverCharacteristics([FS_NOTIFY, FS_WRITE], for: ls)
        }
        else if let fs = services.first(where: { $0.uuid == FS_SERVICE }) { fitShow = true; proto = "FitShow"; p.discoverCharacteristics([FS_NOTIFY, FS_WRITE], for: fs) }
        else if let ft = services.first(where: { $0.uuid == FTMS }) { fitShow = false; proto = "FTMS"; p.discoverCharacteristics([FTMS_DATA, FTMS_CP, FTMS_SPEED_RANGE, FTMS_INCL_RANGE], for: ft) }
        else if let wp = services.first(where: { $0.uuid == WP_SERVICE }) { fitShow = false; proto = "WalkingPad"; p.discoverCharacteristics([WP_NOTIFY, WP_WRITE], for: wp) }
        else if let rs = services.first(where: { $0.uuid == RSC_SERVICE }) { fitShow = false; proto = "RSC"; p.discoverCharacteristics([RSC_MEASURE], for: rs) }
        else { log("no FitShow/FTMS/WalkingPad/RSC service"); central.cancelPeripheralConnection(p); return }
        // hand grips: many treadmills offer their pulse as a heart rate service of their own
        if let h = services.first(where: { $0.uuid == HR_SERVICE }) { p.discoverCharacteristics([HR_MEASURE], for: h) }
    }

    func peripheral(_ p: CBPeripheral, didDiscoverCharacteristicsFor s: CBService, error: Error?) {
        if p == hr {
            guard let ch = s.characteristics?.first(where: { $0.uuid == HR_MEASURE }) else {
                hrLog("kein Puls-Messwert"); central.cancelPeripheralConnection(p); return
            }
            p.setNotifyValue(true, for: ch)
            hrConnected = true
            hrLog("bereit"); sendDevice("hr", p); sendState()
            return
        }
        if s.uuid == HR_SERVICE { // the treadmill's own pulse (hand grips) → 'G'
            for ch in s.characteristics ?? [] where ch.uuid == HR_MEASURE { p.setNotifyValue(true, for: ch) }
            return
        }
        for ch in s.characteristics ?? [] {
            if ch.uuid == FS_NOTIFY || ch.uuid == FTMS_DATA || ch.uuid == WP_NOTIFY || ch.uuid == RSC_MEASURE || ch.uuid == IC_NOTIFY { p.setNotifyValue(true, for: ch) }
            if ch.uuid == FS_WRITE || ch.uuid == FTMS_CP || ch.uuid == WP_WRITE || ch.uuid == IC_WRITE { writeChar = ch }
            if ch.uuid == FTMS_SPEED_RANGE || ch.uuid == FTMS_INCL_RANGE { p.readValue(for: ch) } // the belt's limits
            if ch.uuid == FTMS_CP { p.setNotifyValue(true, for: ch) } // FTMS: indications before any write
        }
        connected = true
        log("ready (\(proto))")
        send("P", proto.data(using: .utf8)!)
        sendDevice("tm", p); sendState()
        if (proto == "iConsole" || proto == "LifeSpan"), let w = writeChar {
            let type: CBCharacteristicWriteType = w.properties.contains(.write) ? .withResponse : .withoutResponse
            if proto == "iConsole" { p.writeValue(Data([0xF0, 0xA0, 0x01, 0x01, 0x92]), for: w, type: type) } // hello (read only)
            pollTimer = Timer.scheduledTimer(withTimeInterval: 0.35, repeats: true) { [weak self] _ in
                guard let self = self else { return }
                if self.proto == "iConsole" { // status query F0 A2 addr D3 + sum
                    let q: [UInt8] = [0xF0, 0xA2, self.icAddr, 0xD3]
                    p.writeValue(Data(q + [UInt8(q.reduce(0) { ($0 + Int($1)) } & 0xFF)]), for: w, type: type)
                } else { // LifeSpan: one read-only query per tick (speed, state, distance)
                    self.lsAsked = lifeSpanOps[self.lsOp % lifeSpanOps.count]; self.lsOp += 1
                    p.writeValue(Data([0xA1, self.lsAsked, 0x00, 0x00, 0x00]), for: w, type: type)
                }
            }
        }
        if (proto == "FitShow" || proto == "WalkingPad"), let w = writeChar {
            let poll = Data(proto == "FitShow" ? fitShowStatusPoll : walkingPadAskStats)
            let type: CBCharacteristicWriteType = w.properties.contains(.write) ? .withResponse : .withoutResponse
            pollTimer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
                if let me = self, Date().timeIntervalSince(me.lastCommand) < 1.2 { return } // a command is on its way
                p.writeValue(poll, for: w, type: type)
            }
        }
    }

    func peripheral(_ p: CBPeripheral, didUpdateValueFor ch: CBCharacteristic, error: Error?) {
        guard let d = ch.value else { return }
        if ch.uuid == FTMS_CP { log("FTMS CP " + d.map { String(format: "%02x", $0) }.joined(separator: " ")); return } // an answer, not a data frame
        if ch.uuid == HR_MEASURE { send(p == hr ? "R" : "G", d); return }
        if ch.uuid == FTMS_SPEED_RANGE { send("E", Data([1]) + d); return }
        if ch.uuid == FTMS_INCL_RANGE { send("E", Data([2]) + d); return }
        if proto == "LifeSpan" { send("D", Data([lsAsked]) + d); return } // the answer carries no op: prefix it
        send("D", d)
    }
}

// ---- C entry points for Unity (NativeBle.cs, DllImport "__Internal") --------
@_cdecl("jog_ble_start")
public func jog_ble_start() {
    DispatchQueue.main.async { if JoggingBle.shared == nil { JoggingBle.shared = JoggingBle() } }
}

@_cdecl("jog_ble_send")
public func jog_ble_send(_ data: UnsafePointer<UInt8>, _ length: Int32) {
    let msg = Data(bytes: data, count: Int(length))
    DispatchQueue.main.async { JoggingBle.shared?.handle(msg) }
}

@_cdecl("jog_ble_poll")
public func jog_ble_poll(_ buffer: UnsafeMutablePointer<UInt8>, _ capacity: Int32) -> Int32 {
    guard let b = JoggingBle.shared else { return 0 }
    return Int32(b.poll(buffer, Int(capacity)))
}
