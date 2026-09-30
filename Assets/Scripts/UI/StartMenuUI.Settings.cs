using UnityEngine;
using UnityEngine.UI;
using Jogging.Core;
using Jogging.Locomotion.Treadmill;

namespace Jogging.UI
{
    /// <summary>
    /// Einstellungen — for the whole app, reached from the main dialog ("Wer läuft?"), not from a
    /// runner: treadmill (live status, connect / disconnect, connect on start, who controls, choose &amp;
    /// set up), heart rate sensor (status, connect, connect on start), announcements, sounds, display,
    /// graphics, backup. Saved in <see cref="AppSettings"/>. What belongs to a runner — their max heart
    /// rate, their own strap ("Anderer Gurt") — is in their profile.
    /// </summary>
    public partial class StartMenuUI
    {
        private void ShowTreadmillSettings()
        {
            ShowSettingsPage();
        }

        private void ShowSettingsPage()
        {
            const float w = 900f;
            var p = NewPage(w, 1070f);
            var st = AppSettings.Current;
            var bridge = FindFirstObjectByType<MacBleBridgeTransport>();
            var belt = FindFirstObjectByType<TreadmillLocomotionSource>();

            UiControls.Label(p, "Einstellungen", 40, 50f, -16f, 500f, 56f, UiTheme.TextPrimary, bold: true);
            UiControls.Button(p, Jogging.Core.Loc.T("Datensicherung"), () => ShowBackup(), w - 310f, -28f, 260f, 50f);
            UiControls.Label(p, "LAUFBAND", 17, 50f, -80f, 300f, 22f, UiTheme.TextMuted, bold: true);

            var box = UiTheme.Panel(p, UiTheme.PanelSoft);
            UiControls.Place(box.GetComponent<RectTransform>(), 50f, -106f, w - 100f, 110f);
            var dot = UiTheme.Panel(box.transform, UiTheme.Danger).GetComponent<Image>();
            UiControls.Place(dot.rectTransform, 22f, -26f, 16f, 16f);
            var line1 = UiControls.Label(box.transform, "", 26, 54f, -10f, w - 180f, 44f, UiTheme.TextPrimary, bold: true);
            var line2 = UiControls.Label(box.transform, "", 20, 54f, -56f, w - 180f, 36f, UiTheme.TextMuted);

            Text connectLabel = UiControls.Button(p, "", () =>
            {
                if (bridge == null) return;
                if (bridge.Wanted) bridge.Disconnect(); else bridge.Connect();
            }, 50f, -232f, 300f, 56f, UiTheme.Accent).GetComponentInChildren<Text>();
            // Pick a treadmill from a list, and its limits
            UiControls.Button(p, Jogging.Core.Loc.T("Laufband wählen & einstellen"), () => ShowTreadmillSetup(), 370f, -232f, 380f, 56f);

            UiLive.Attach(box, () =>
            {
                bool connected = bridge != null && bridge.IsConnected;
                bool data = belt != null && belt.IsActive;
                dot.color = data ? UiTheme.Success : connected ? UiTheme.Accent : UiTheme.Danger;
                if (bridge == null) { line1.text = Jogging.Core.Loc.T("Kein Laufband-Anschluss"); line2.text = Jogging.Core.Loc.T("Bluetooth-Bridge gibt es nur auf macOS"); }
                else if (data)
                {
                    line1.text = Jogging.Core.Loc.F("Verbunden · {0}{1}", (bridge.TreadmillDeviceName != "" ? bridge.TreadmillDeviceName + " · " : ""), bridge.Protocol);
                    line2.text = Jogging.Core.Loc.F("Band {0} · Steigung {1:0} % · {2}", Jogging.Core.Units.FmtSpeed(belt.SpeedMps * 3.6f), belt.InclinePercent, BeltText(belt.State));
                }
                else if (!Core.Platform.HasBleBridge && !NativeBle.Available && bridge.Simulator == null) { line1.text = Jogging.Core.Loc.T("Auf diesem Gerät noch nicht verfügbar"); line2.text = Jogging.Core.Loc.T("Laufband und Pulsgurt verbinden sich vorerst über die Mac-App"); }
                else if (bridge.Wanted) { line1.text = connected ? Jogging.Core.Loc.T("Verbunden, warte auf Daten …") : Jogging.Core.Loc.T("Suche Laufband …"); line2.text = bridge.StatusText; }
                else { line1.text = Jogging.Core.Loc.T("Getrennt"); line2.text = Core.Platform.IsMobile ? Jogging.Core.Loc.T("Ohne Laufband stellst du das Tempo mit − / +") : Jogging.Core.Loc.T("Ohne Laufband läufst du mit ↑ / ↓"); }
                connectLabel.text = bridge != null && bridge.Wanted ? Jogging.Core.Loc.T("Trennen") : Jogging.Core.Loc.T("Verbinden");
            });

            UiControls.Toggle(p, "Beim Start verbinden", "An", "Aus", st.treadmillAutoConnect,
                v => { st.treadmillAutoConnect = v; AppSettings.Save(); }, 50f, -320f, 560f);
            UiControls.Toggle(p, "Steuerung", "Strecke stellt die Steigung", "Band – du steuerst", st.beltIncline,
                v => { st.beltIncline = v; AppSettings.Save(); TreadmillConnectUI.Apply(); }, 50f, -386f, 560f);
            UiControls.Toggle(p, "Tempo (Workouts)", "Workout stellt das Tempo", "nur als Hinweis", st.beltSpeed,
                v => { st.beltSpeed = v; AppSettings.Save(); TreadmillConnectUI.Apply(); }, 50f, -452f, 560f);
            var tp = AppSettings.Treadmill(bridge != null && bridge.TreadmillDeviceId != "" ? bridge.TreadmillDeviceId : st.treadmillId);
            string incRange = tp.decline ? $"−{tp.maxDecline:0}–{tp.maxIncline:0} %" : $"0–{tp.maxIncline:0} %";
            var hint = UiControls.Label(p, Jogging.Core.Loc.F("Steigung: nur bei laufendem Band und Lauf, {0}, 1 % pro Schritt. Tempo (nur wenn eingeschaltet, nur in Workouts): {2} pro Schritt, höchstens {1}.\n", incRange, Jogging.Core.Units.FmtSpeed(tp.maxSpeed, "0.#"), Jogging.Core.Units.FmtSpeed(Jogging.Core.Units.SpeedToKmh(0.5f))) +
                                Jogging.Core.Loc.T("Verstellst du am Band, hält sich die App 30 s zurück. Das Band startet nie von selbst; Stopp und Clip haben immer Vorrang."),
                17, 50f, -506f, w - 100f, 76f, UiTheme.TextMuted);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;

            HeartRateSection(p, w, bridge, -610f);
            UiControls.Toggle(p, "Ansagen", "An", "Aus", st.announcements,
                v => { st.announcements = v; AppSettings.Save(); }, 50f, -870f, 400f);
            UiControls.Toggle(p, "Geräusche", "An", "Aus", st.ambience,
                v => { st.ambience = v; AppSettings.Save(); }, 470f, -870f, 380f);
            UiControls.Toggle(p, "Anzeige", Core.Platform.IsMobile ? "Groß – vom Laufband lesbar" : "Groß – vom Laufband lesbar (H)", "Normal", st.bigHud,
                v => { st.bigHud = v; AppSettings.Save(); }, 50f, -926f, 560f);
            Text gfx = null;
            gfx = UiControls.Button(p, GraphicsCaption(Jogging.World.GraphicsQuality.Level), () =>
            {
                string g0 = Jogging.World.GraphicsQuality.Level;
                st.graphics = g0 == "high" ? "medium" : g0 == "medium" ? "low" : g0 == "low" ? "minimal" : "high";
                st.graphicsChosen = true;
                AppSettings.Save();
                Jogging.World.GraphicsQuality.ApplyNow();
                gfx.text = GraphicsCaption(st.graphics);
            }, 630f, -926f, w - 680f, 52f).GetComponentInChildren<Text>();
            // Language: German / English (preselected from the operating system). Switching reloads the scene
            // so every text — menus, HUD, announcements — is in the new language, then reopens this page.
            UiControls.Button(p, Jogging.Core.Loc.En ? "Language: English  →  Deutsch" : "Sprache: Deutsch  →  English", () =>
            {
                st.language = Jogging.Core.Loc.En ? "de" : "en";
                AppSettings.Save();
                Jogging.Core.Loc.Init();
                reopenSettings = true;
                Jogging.World.SceneReload.Now();
            }, 50f, -990f, 320f, 56f, null, 19);
            // Units: metric / imperial (preselected from the system region); inside everything stays km/h and metres
            UiControls.Button(p, Jogging.Core.Units.Imperial ? "Einheiten: mph  →  km/h" : "Einheiten: km/h  →  mph", () =>
            {
                st.units = Jogging.Core.Units.Imperial ? "metric" : "imperial";
                AppSettings.Save();
                Jogging.Core.Units.Init();
                reopenSettings = true;
                Jogging.World.SceneReload.Now();
            }, 385f, -990f, 300f, 56f);
            UiControls.Button(p, Jogging.Core.Loc.T("Zurück"), () => ShowEntryAlways(), w - 200f, -990f, 150f, 56f);
        }

        private static bool reopenSettings; // after a language switch the scene reloads: back to this page

        private static string GraphicsCaption(string g) =>
            Jogging.Core.Loc.T("Grafik: ") + Jogging.Core.Loc.T(g == "minimal" ? "minimal" : g == "low" ? "niedrig" : g == "medium" ? "mittel" : "hoch");

        private void HeartRateSection(Transform p, float w, MacBleBridgeTransport bridge, float y)
        {
            var st = AppSettings.Current;
            UiControls.Label(p, "PULSGURT", 17, 50f, y, 300f, 26f, UiTheme.TextMuted, bold: true);
            var box = UiTheme.Panel(p, UiTheme.PanelSoft);
            UiControls.Place(box.GetComponent<RectTransform>(), 50f, y - 32f, w - 100f, 76f);
            var dot = UiTheme.Panel(box.transform, UiTheme.Danger).GetComponent<Image>();
            UiControls.Place(dot.rectTransform, 22f, -30f, 16f, 16f);
            var line = UiControls.Label(box.transform, "", 24, 54f, -8f, w - 180f, 36f, UiTheme.TextPrimary, bold: true);
            var sub = UiControls.Label(box.transform, "", 18, 54f, -42f, w - 180f, 28f, UiTheme.TextMuted);

            Text btn = UiControls.Button(p, "", () =>
            {
                if (bridge == null) return;
                if (bridge.HeartRateWanted) bridge.DisconnectHeartRate(); else bridge.ConnectHeartRate();
            }, 50f, y - 124f, 300f, 52f, UiTheme.Accent).GetComponentInChildren<Text>();

            float hrPageOpened = Time.unscaledTime;
            UiLive.Attach(box, () =>
            {
                var hrm = Training.HeartRateMonitor.Current;
                bool data = hrm != null && hrm.HasData;
                dot.color = data ? Training.HeartRateMonitor.ZoneColor(hrm.Zone) : bridge != null && bridge.HeartRateConnected ? UiTheme.Accent : UiTheme.Danger;
                var owner = Jogging.Profile.ProfileService.Instance?.StrapOwner(bridge != null ? bridge.HeartRateDeviceId : "");
                string strap = bridge != null && bridge.HeartRateDeviceName != "" ? bridge.HeartRateDeviceName : "Pulsgurt";
                if (hrm != null && hrm.ForeignStrap)
                { line.text = Jogging.Core.Loc.F("{0} gehört {1}", strap, owner.playerName); sub.text = Jogging.Core.Loc.T("Sein Puls wird nicht aufgezeichnet – eigenen Gurt anlegen, im eigenen Profil „Anderer Gurt“"); }
                else if (data) { line.text = Jogging.Core.Loc.F("Puls {0} · Zone {1} {2}", hrm.Bpm, hrm.Zone, Training.HeartRateZones.Name(hrm.Zone)); sub.text = hrm.Source == "Pulsgurt" ? $"{strap}" + (owner != null ? Jogging.Core.Loc.F(" · Gurt von {0}", owner.playerName) : "") : Jogging.Core.Loc.T(hrm.Source); }
                else if (bridge != null && bridge.HeartRateWanted && !bridge.HeartRateConnected && Time.unscaledTime - hrPageOpened > 20f)
                { line.text = Jogging.Core.Loc.T("Kein Pulsgurt in der Nähe"); sub.text = Jogging.Core.Loc.T("Ohne Gurt: im Lauf die Handsensoren am Band anfassen – oder Brustgurt / Uhr („Herzfrequenz senden“)"); }
                else if (bridge != null && bridge.HeartRateWanted) { line.text = bridge.HeartRateConnected ? Jogging.Core.Loc.T("Verbunden, warte auf Puls …") : Jogging.Core.Loc.T("Suche Pulsgurt …"); sub.text = Jogging.Core.Loc.T("Brustgurt anlegen oder Uhr auf „Herzfrequenz senden“ stellen"); }
                else { line.text = Jogging.Core.Loc.T("Getrennt"); sub.text = Jogging.Core.Loc.T("Ohne Pulsgurt läuft alles wie gewohnt"); }
                btn.text = bridge != null && bridge.HeartRateWanted ? Jogging.Core.Loc.T("Trennen") : Jogging.Core.Loc.T("Verbinden");
            });

            UiControls.Toggle(p, "Beim Start verbinden", "An", "Aus", st.heartRateAutoConnect,
                v => { st.heartRateAutoConnect = v; AppSettings.Save(); }, 370f, y - 128f, 330f);
            UiControls.Label(p, "Welcher Gurt zu wem gehört und der maximale Puls stehen im Profil des Läufers.",
                18, 50f, y - 200f, w - 100f, 44f, UiTheme.TextMuted);
        }

        /// <summary>The runner's own strap: forget it and pair the next free one found (profile).</summary>
        private static void PairOtherStrap(MacBleBridgeTransport bridge)
        {
            var ps0 = Jogging.Profile.ProfileService.Instance;
            if (ps0 != null && ps0.HasRunners) { ps0.Profile.hrDeviceId = ""; ps0.Profile.hrDeviceName = ""; ps0.SaveActive(); }
            if (bridge != null) { bridge.DisconnectHeartRate(); bridge.ConnectHeartRate(""); }
        }

        private static string BeltText(BeltState s)
        {
            switch (s)
            {
                case BeltState.Running: return Jogging.Core.Loc.T("läuft");
                case BeltState.Countdown: return Jogging.Core.Loc.T("startet");
                case BeltState.Paused: return Jogging.Core.Loc.T("Pause");
                case BeltState.Safety: return Jogging.Core.Loc.T("Sicherheitsclip fehlt");
                case BeltState.Error: return Jogging.Core.Loc.T("Fehler");
                default: return Jogging.Core.Loc.T("steht");
            }
        }
    }
}
