// JoggingBleBridge — macOS helper that talks CoreBluetooth to the treadmill and relays the raw
// frames to Unity over UDP (localhost). Unity itself has no BLE on macOS; a signed app bundle
// with NSBluetoothAlwaysUsageDescription is the only thing macOS grants Bluetooth to.
//
// Protocols: FitShow (Sportstech F37: service FFF0, FFF1 notify / FFF2 write, polled) and
// FTMS (0x1826: 2ACD notify / 2AD9 control point). A second, independent slot connects a heart
// rate sensor (standard Heart Rate Service 0x180D, measurement 2A37 notify) when Unity asks for it.
//
// UDP  bridge -> Unity  127.0.0.1:47520   'D'+frame | 'S'+"1"/"0" (connected) | 'L'+text
//                                        'P'+"FitShow"/"FTMS" (protocol)
//                                        'R'+HR measurement | 'T'+"1"/"0" (HR connected) | 'M'+HR text
//                                        'N'+"tm|<id>|<name>" / "hr|<id>|<name>" (which device is connected)
//      Unity -> bridge  127.0.0.1:47521   'W'+bytes (write control) | 'C'[+id] (scan) | 'X' (disconnect) | 'Q' (quit)
//                                        'H'[+id] (find heart rate sensor) | 'Y' (disconnect it)
// With an id (CBPeripheral.identifier) only that device is connected; without, the first one found.
//
// Second use, no Bluetooth: `JoggingBleBridge --qr <image>` prints the text of every QR code in the
// image (Vision) and exits — 0 found, 1 none. The Jogging app reads shared routes this way.
import Foundation
import CoreBluetooth
import Network
import Vision
import AppKit

if CommandLine.arguments.count >= 3 && CommandLine.arguments[1] == "--qr" {
    let path = CommandLine.arguments[2]
    guard let img = NSImage(contentsOfFile: path),
          let cg = img.cgImage(forProposedRect: nil, context: nil, hints: nil) else { exit(2) }
    let req = VNDetectBarcodesRequest()
    req.symbologies = [.qr]
    try? VNImageRequestHandler(cgImage: cg).perform([req])
    let texts = (req.results ?? []).compactMap { $0.payloadStringValue }
    for t in texts { print(t) }
    exit(texts.isEmpty ? 1 : 0)
}

let toUnityPort: NWEndpoint.Port = 47520
let fromUnityPort: NWEndpoint.Port = 47521

let FTMS = CBUUID(string: "1826"), FTMS_DATA = CBUUID(string: "2ACD"), FTMS_CP = CBUUID(string: "2AD9")
let FS_SERVICE = CBUUID(string: "FFF0"), FS_NOTIFY = CBUUID(string: "FFF1"), FS_WRITE = CBUUID(string: "FFF2")
let HR_SERVICE = CBUUID(string: "180D"), HR_MEASURE = CBUUID(string: "2A37")
// KingSmith WalkingPad (older models; read only: only the stats query is sent) and foot pods (RSC, read only)
let WP_SERVICE = CBUUID(string: "FE00"), WP_NOTIFY = CBUUID(string: "FE01"), WP_WRITE = CBUUID(string: "FE02")
let RSC_SERVICE = CBUUID(string: "1814"), RSC_MEASURE = CBUUID(string: "2A53")
let walkingPadAskStats: [UInt8] = [0xF7, 0xA2, 0x00, 0x00, 0xA2, 0xFD]
// FTMS ranges (read once), iConsole+ (Changyow: FFF0 or Microchip UART) and LifeSpan (FFF0), both read only
let FTMS_SPEED_RANGE = CBUUID(string: "2AD4"), FTMS_INCL_RANGE = CBUUID(string: "2AD5")
let IC_UART = CBUUID(string: "49535343-FE7D-4AE5-8FA9-9FAFD205E455"), IC_WRITE = CBUUID(string: "49535343-8841-43F4-A8D4-ECBE34729BB3"), IC_NOTIFY = CBUUID(string: "49535343-1E4D-4BD9-BA61-23C647249616")
let PAFERS = CBUUID(string: "72D70001-501F-46F7-95F9-23846EE1ABA3")
let lifeSpanOps: [UInt8] = [0x82, 0x91, 0x85] // speed, state, distance — read-only queries

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

let fitShowStatusPoll: [UInt8] = [0x02, 0x51, 0x51, 0x03]

final class Bridge: NSObject, CBCentralManagerDelegate, CBPeripheralDelegate {
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
    private var wanted = true   // false after 'X' until the next 'C'
    private var hr: CBPeripheral?          // heart rate sensor (own slot, independent of the treadmill)
    private var hrConnected = false
    private var hrWanted = false           // true after 'H' until 'Y'
    private var treadmillId: String? = nil // only this treadmill (nil = any)
    private var hrId: String? = nil        // only this heart rate sensor (nil = any)
    private let out = NWConnection(host: "127.0.0.1", port: toUnityPort, using: .udp)

    override init() {
        super.init()
        out.start(queue: .main)
        central = CBCentralManager(delegate: self, queue: .main)
        startCommandListener()
        Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in self?.sendState() }
        // The app is gone (closed hard, crashed): without its heartbeat for 60 s the bridge lets go of the
        // treadmill and quits — a connected treadmill doesn't advertise, so no other device could find it.
        Timer.scheduledTimer(withTimeInterval: 10, repeats: true) { [weak self] _ in
            guard let self = self, Date().timeIntervalSince(self.lastFromApp) > 60 else { return }
            self.log("keine Nachricht von der App seit 60 s – gebe das Laufband frei und beende mich")
            if let p = self.peripheral { self.central.cancelPeripheralConnection(p) }
            if let h = self.hr { self.central.cancelPeripheralConnection(h) }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { exit(0) }
        }
        // A newer bridge was installed (a new app build): step aside, the app starts the new one.
        // Otherwise an old bridge kept running for days and spoke yesterday's protocol.
        let exe = Bundle.main.executablePath ?? CommandLine.arguments[0]
        let built = (try? FileManager.default.attributesOfItem(atPath: exe)[.modificationDate]) as? Date
        Timer.scheduledTimer(withTimeInterval: 10, repeats: true) { [weak self] _ in
            let now = (try? FileManager.default.attributesOfItem(atPath: exe)[.modificationDate]) as? Date
            guard let a = built, let b = now, a != b else { return }
            self?.log("neue Bridge-Version installiert – beende mich")
            if let p = self?.peripheral { self?.central.cancelPeripheralConnection(p) }
            if let h = self?.hr { self?.central.cancelPeripheralConnection(h) }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { exit(0) }
        }
    }

    // ---- UDP ---------------------------------------------------------------
    private func send(_ type: Character, _ payload: Data) {
        var d = Data([UInt8(type.asciiValue!)]); d.append(payload)
        out.send(content: d, completion: .contentProcessed { _ in })
    }
    private func log(_ s: String) { print(s); send("L", s.data(using: .utf8)!) }
    private func hrLog(_ s: String) { print("HR " + s); send("M", s.data(using: .utf8)!) }
    private func sendState() {
        send("S", (connected ? "1" : "0").data(using: .utf8)!)
        send("T", (hrConnected ? "1" : "0").data(using: .utf8)!)
    }

    private func startCommandListener() {
        guard let listener = try? NWListener(using: .udp, on: fromUnityPort) else { log("UDP listen failed"); return }
        listener.newConnectionHandler = { [weak self] c in c.start(queue: .main); self?.receive(c) }
        listener.start(queue: .main)
    }
    private func receive(_ c: NWConnection) {
        c.receiveMessage { [weak self] data, _, _, err in
            if let d = data, let first = d.first { self?.handle(Character(UnicodeScalar(first)), d.dropFirst()) }
            if err == nil { self?.receive(c) }
        }
    }
    private var lastFromApp = Date()

    private func handle(_ cmd: Character, _ payload: Data) {
        lastFromApp = Date()
        switch cmd {
        case "K": break // the app's heartbeat (every 5 s)
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
            if let p = peripheral, id != nil, p.identifier.uuidString != id { central.cancelPeripheralConnection(p) } // other one wanted
            treadmillId = id
            let newNeed = !wanted; wanted = true
            if !connected && peripheral == nil { startScan(fresh: newNeed) }
            // Already connected (the game changed scenes and asks again): announce protocol and device
            // again, the new scene doesn't know them yet.
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
            if let h = hr, id != nil, h.identifier.uuidString != id { central.cancelPeripheralConnection(h) } // other runner's strap
            hrId = id
            let newHr = !hrWanted; hrWanted = true
            if hr == nil { startScan(fresh: newHr) }
            else if hrConnected, let h = hr, id == nil || h.identifier.uuidString == id { sendDevice("hr", h); sendState() }
        case "Y": hrWanted = false; if let h = hr { central.cancelPeripheralConnection(h) }; stopScanIfDone()
        case "Q": if let p = peripheral { central.cancelPeripheralConnection(p) }; if let h = hr { central.cancelPeripheralConnection(h) }; exit(0)
        default: break
        }
    }

    private func idFrom(_ payload: Data) -> String? {
        let s = String(data: payload, encoding: .utf8)?.trimmingCharacters(in: .whitespaces) ?? ""
        return s.isEmpty ? nil : s
    }

    private func sendDevice(_ kind: String, _ p: CBPeripheral) {
        send("N", "\(kind)|\(p.identifier.uuidString)|\(p.name ?? "")".data(using: .utf8)!)
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
        if c.state != .poweredOn { // Bluetooth off / reset: no disconnect callbacks come
            pollTimer?.invalidate(); pollTimer = nil
            peripheral = nil; writeChar = nil; connected = false; fitShow = false; proto = ""; rscCandidate = nil
            hr = nil; hrConnected = false
            sendState()
        }
        switch c.state {
        case .poweredOn: startScan()
        case .unauthorized: log("ERROR Bluetooth not authorized")
        case .poweredOff: log("ERROR Bluetooth off")
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
        if let k = kind, looksTm, !supportedTreadmills.contains(k) { return } // listed, but never connected // never the strap
        if needHr, p != peripheral, uuids.contains(HR_SERVICE), !looksTm, hrId == nil || p.identifier.uuidString == hrId {
            hrLog("gefunden: \(name.isEmpty ? "Pulsgurt" : name)")
            hr = p
            p.delegate = self
            c.connect(p, options: nil)
            stopScanIfDone()
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
        peripheral = p
        p.delegate = self
        central.connect(p, options: nil)
        stopScanIfDone()
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
        // Prefer FitShow when present (F37); otherwise standard FTMS.
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
            for ch in s.characteristics ?? [] where ch.uuid == HR_MEASURE { p.setNotifyValue(true, for: ch) }
            hrConnected = true
            hrLog("bereit")
            sendDevice("hr", p)
            sendState()
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
        sendDevice("tm", p)
        sendState()
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

let bridge = Bridge()
RunLoop.main.run()
