// JoggingHealth — runs into Apple Health on the iPad (AppleHealth.cs, DllImport "__Internal").
// Writes one indoor running workout per run: duration, distance, active energy, the heart rate and the
// elevation gain. Only writes, never reads. A run carries a sync identifier (runner + start), so sending
// it again replaces the entry in Health instead of adding a second one.
import Foundation
import HealthKit

final class JoggingHealth {
    static let shared = JoggingHealth()
    let store = HKHealthStore()
    var state: Int32 = 0          // last save: 0 idle/pending, 1 saved, -1 failed
    var lastError = ""

    private var shareTypes: Set<HKSampleType> {
        [HKObjectType.workoutType(),
         HKQuantityType.quantityType(forIdentifier: .distanceWalkingRunning)!,
         HKQuantityType.quantityType(forIdentifier: .activeEnergyBurned)!,
         HKQuantityType.quantityType(forIdentifier: .heartRate)!]
    }

    func authorize() {
        store.requestAuthorization(toShare: shareTypes, read: nil) { _, error in
            if let e = error { self.lastError = e.localizedDescription }
        }
    }

    /// 0 not asked yet, 1 allowed, 2 not allowed (for workouts)
    func status() -> Int32 {
        switch store.authorizationStatus(for: HKObjectType.workoutType()) {
        case .sharingAuthorized: return 1
        case .sharingDenied: return 2
        default: return 0
        }
    }

    struct Run: Decodable {
        let id: String
        let start: Double          // Unix seconds (UTC)
        let seconds: Double
        let distanceM: Double
        let kcal: Double
        let gainM: Double
        let t: [Double]            // per second: run time, distance (m), heart rate (0 = none)
        let dist: [Double]
        let hr: [Int]
    }

    func save(_ json: String) {
        state = 0
        guard let data = json.data(using: .utf8), let run = try? JSONDecoder().decode(Run.self, from: data) else {
            fail("Lauf nicht lesbar"); return
        }
        let start = Date(timeIntervalSince1970: run.start)
        let end = start.addingTimeInterval(max(1, run.seconds))
        let cfg = HKWorkoutConfiguration()
        cfg.activityType = .running
        cfg.locationType = .indoor
        let builder = HKWorkoutBuilder(healthStore: store, configuration: cfg, device: .local())

        var samples: [HKSample] = []
        let distType = HKQuantityType.quantityType(forIdentifier: .distanceWalkingRunning)!
        let hrType = HKQuantityType.quantityType(forIdentifier: .heartRate)!
        let bpm = HKUnit.count().unitDivided(by: .minute())
        // distance in 10-second pieces (Health shows the pace curve from them)
        var lastT = 0.0, lastD = 0.0
        for i in 0..<run.t.count {
            let t = run.t[i], d = run.dist[i]
            if t - lastT >= 10 || i == run.t.count - 1, d > lastD, t > lastT {
                samples.append(HKCumulativeQuantitySample(type: distType, quantity: HKQuantity(unit: .meter(), doubleValue: d - lastD),
                                                          start: start.addingTimeInterval(lastT), end: start.addingTimeInterval(t)))
                lastT = t; lastD = d
            }
        }
        // heart rate every 5 s
        for i in stride(from: 0, to: run.t.count, by: 5) where run.hr[i] > 0 {
            let at = start.addingTimeInterval(run.t[i])
            samples.append(HKQuantitySample(type: hrType, quantity: HKQuantity(unit: bpm, doubleValue: Double(run.hr[i])), start: at, end: at))
        }
        if run.kcal > 0 {
            samples.append(HKCumulativeQuantitySample(type: HKQuantityType.quantityType(forIdentifier: .activeEnergyBurned)!,
                                                      quantity: HKQuantity(unit: .kilocalorie(), doubleValue: run.kcal), start: start, end: end))
        }
        var meta: [String: Any] = [HKMetadataKeyIndoorWorkout: true,
                                   HKMetadataKeySyncIdentifier: run.id,
                                   HKMetadataKeySyncVersion: 1]
        if run.gainM > 0 { meta[HKMetadataKeyElevationAscended] = HKQuantity(unit: .meter(), doubleValue: run.gainM) }

        builder.beginCollection(withStart: start) { ok, error in
            guard ok else { self.fail(error?.localizedDescription ?? "beginCollection"); return }
            builder.addMetadata(meta) { _, _ in
                let go = {
                    builder.endCollection(withEnd: end) { ok, error in
                        guard ok else { self.fail(error?.localizedDescription ?? "endCollection"); return }
                        builder.finishWorkout { workout, error in
                            if workout != nil { self.state = 1 } else { self.fail(error?.localizedDescription ?? "finishWorkout") }
                        }
                    }
                }
                if samples.isEmpty { go() }
                else { builder.add(samples) { ok, error in if !ok { self.lastError = error?.localizedDescription ?? "" }; go() } }
            }
        }
    }

    private func fail(_ msg: String) { lastError = msg; state = -1 }
}

// ---- C entry points for Unity -----
@_cdecl("jog_health_available")
public func jog_health_available() -> Bool { HKHealthStore.isHealthDataAvailable() }

@_cdecl("jog_health_authorize")
public func jog_health_authorize() { DispatchQueue.main.async { JoggingHealth.shared.authorize() } }

@_cdecl("jog_health_status")
public func jog_health_status() -> Int32 { JoggingHealth.shared.status() }

@_cdecl("jog_health_save")
public func jog_health_save(_ json: UnsafePointer<CChar>) {
    let s = String(cString: json)
    JoggingHealth.shared.state = 0
    DispatchQueue.main.async { JoggingHealth.shared.save(s) }
}

@_cdecl("jog_health_state")
public func jog_health_state() -> Int32 { JoggingHealth.shared.state }

// returned as a malloc'd copy: Unity frees it after reading
@_cdecl("jog_health_error")
public func jog_health_error() -> UnsafeMutablePointer<CChar>? { strdup(JoggingHealth.shared.lastError) }
