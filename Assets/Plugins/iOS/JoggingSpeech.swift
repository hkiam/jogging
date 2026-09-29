// JoggingSpeech — the run's announcements on the iPad (Announcer.cs → NativeSpeech.cs).
// AVSpeechSynthesizer with a German voice; a new announcement replaces the one being spoken.
// Mixes with other audio (music keeps playing, it is only ducked while speaking).
import Foundation
import AVFoundation

final class JoggingSpeech {
    static let shared = JoggingSpeech()
    private let synth = AVSpeechSynthesizer()


    private init() {
        try? AVAudioSession.sharedInstance().setCategory(.playback, mode: .spokenAudio, options: [.mixWithOthers, .duckOthers])
    }

    func say(_ text: String, _ language: String) {
        if synth.isSpeaking { synth.stopSpeaking(at: .immediate) }
        let u = AVSpeechUtterance(string: text)
        u.voice = AVSpeechSynthesisVoice(language: language) // de-DE or en-GB (the app's language)
        u.rate = AVSpeechUtteranceDefaultSpeechRate * 1.05
        synth.speak(u)
    }

    func stop() { synth.stopSpeaking(at: .immediate) }
}

// ---- C entry points for Unity (NativeSpeech.cs, DllImport "__Internal") -----
@_cdecl("jog_say")
public func jog_say(_ text: UnsafePointer<CChar>, _ language: UnsafePointer<CChar>) {
    let s = String(cString: text), l = String(cString: language)
    DispatchQueue.main.async { JoggingSpeech.shared.say(s, l) }
}

@_cdecl("jog_say_stop")
public func jog_say_stop() {
    DispatchQueue.main.async { JoggingSpeech.shared.stop() }
}
