// Decodes every PNG in a folder with Apple's Vision barcode detector and compares with <name>.txt.
import Foundation
import Vision
import AppKit
let dir = CommandLine.arguments[1]
var fails = 0
for f in try! FileManager.default.contentsOfDirectory(atPath: dir).sorted() where f.hasSuffix(".png") {
    let name = String(f.dropLast(4))
    let expected = (try? String(contentsOfFile: "\(dir)/\(name).txt", encoding: .utf8)) ?? ""
    guard let img = NSImage(contentsOfFile: "\(dir)/\(f)"), let cg = img.cgImage(forProposedRect: nil, context: nil, hints: nil) else { print("FAIL \(name): Bild nicht lesbar"); fails += 1; continue }
    let req = VNDetectBarcodesRequest()
    req.symbologies = [.qr]
    try! VNImageRequestHandler(cgImage: cg).perform([req])
    let got = (req.results ?? []).compactMap { $0.payloadStringValue }.first
    if got == expected { print("OK   \(name) (\(expected.count) Zeichen)") }
    else { print("FAIL \(name): \(got == nil ? "nicht erkannt" : "falscher Inhalt (\(got!.prefix(40))…)")"); fails += 1 }
}
exit(fails == 0 ? 0 : 1)
