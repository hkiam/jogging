// JoggingSpeech — the run's announcements on Android tablets (Announcer.cs → NativeSpeech.cs).
// The system's TextToSpeech in German; a new announcement replaces the one being spoken (QUEUE_FLUSH).
// What is said before the engine is ready waits (only the latest one).
package local.jogging.speech;

import android.content.Context;
import android.speech.tts.TextToSpeech;

import java.util.Locale;

public class JoggingSpeech {
    private static JoggingSpeech instance;
    private TextToSpeech tts; // not final: the init listener reads it
    private volatile boolean ready;
    private String pending;
    private String error;

    public static synchronized JoggingSpeech start(Context context) {
        if (instance == null) instance = new JoggingSpeech(context.getApplicationContext());
        return instance;
    }

    private JoggingSpeech(Context context) {
        tts = new TextToSpeech(context, status -> {
            synchronized (this) {
                if (status != TextToSpeech.SUCCESS) { error = "keine Sprachausgabe (TTS " + status + ")"; return; }
                setLanguage(wanted);
                tts.setSpeechRate(1.05f);
                ready = true;
                if (pending != null) { speak(pending); pending = null; }
            }
        });
    }

    public synchronized void say(String text, String language) {
        Locale want = "en".equals(language) ? Locale.UK : Locale.GERMANY;
        if (ready && !want.equals(current)) { setLanguage(want); }
        wanted = want;
        if (!ready) { pending = text; return; }
        speak(text);
    }

    private Locale wanted = Locale.GERMANY, current;

    private void setLanguage(Locale l) {
        int r = tts.setLanguage(l);
        if (r == TextToSpeech.LANG_MISSING_DATA || r == TextToSpeech.LANG_NOT_SUPPORTED)
            r = tts.setLanguage(l == Locale.UK ? Locale.ENGLISH : Locale.GERMAN);
        if (r == TextToSpeech.LANG_MISSING_DATA || r == TextToSpeech.LANG_NOT_SUPPORTED)
            error = l == Locale.UK ? "keine englische Stimme installiert" : "keine deutsche Stimme installiert";
        current = l;
    }

    private void speak(String text) { tts.speak(text, TextToSpeech.QUEUE_FLUSH, null, "jogging"); }

    public void stop() { if (ready) tts.stop(); }

    /// Null while all is well; otherwise why nothing can be heard (logged once by Unity).
    public synchronized String error() { String e = error; error = null; return e; }
}
