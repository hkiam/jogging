using UnityEngine;
using UnityEngine.UI;
using Jogging.Core;
using Jogging.Locomotion.Treadmill;

namespace Jogging.UI
{
    /// <summary>
    /// Geräte → Laufband wählen &amp; einstellen: the connected treadmill with the range it reports, the
    /// runner's caps for it (incline, decline — only if the belt can —, workout speed), kept per
    /// treadmill in <see cref="TreadmillProfile"/>; at the top "Dein Laufband" live with connect /
    /// disconnect; and a device search (started when the page opens) listing the own treadmill first —
    /// a connected one doesn't advertise — and every other treadmill and foot pod nearby with what the
    /// app can do with it (supported ones can be connected, others are only shown).
    /// </summary>
    public partial class StartMenuUI
    {
        private static string KindText(string kind)
        {
            switch (kind)
            {
                case "FitShow": return "FitShow";
                case "FTMS": return Jogging.Core.Loc.T("FTMS (Standard)");
                case "WalkingPad": return Jogging.Core.Loc.T("WalkingPad · nur lesen");
                case "RSC": return Jogging.Core.Loc.T("Laufsensor · nur Tempo");
                case "iConsole": return Jogging.Core.Loc.T("iConsole+ · nur lesen, ungetestet");
                case "LifeSpan": return Jogging.Core.Loc.T("LifeSpan · nur lesen, ungetestet");
                case "eHealth": case "SmartTreadmill": case "Pafers": return kind + Jogging.Core.Loc.T(" · nicht unterstützt");
                default: return kind;
            }
        }

        private static bool Pickable(string kind) => kind == "FitShow" || kind == "FTMS" || kind == "WalkingPad" || kind == "RSC" || kind == "iConsole" || kind == "LifeSpan";

        private void ShowTreadmillSetup()
        {
            const float w = 1200f;
            var p = NewPage(w, 940f);
            var st = AppSettings.Current;
            var bridge = FindFirstObjectByType<MacBleBridgeTransport>();
            var belt = FindFirstObjectByType<TreadmillLocomotionSource>();
            string id = st.treadmillId != "" ? st.treadmillId : bridge != null ? bridge.TreadmillDeviceId : "";
            var prof = AppSettings.Treadmill(id);

            UiControls.Label(p, "Laufband wählen & einstellen", 36, 50f, -20f, w - 100f, 56f, UiTheme.TextPrimary, bold: true);

            // --- "Dein Laufband": the remembered treadmill, live, with connect / disconnect right here
            var card = UiTheme.Panel(p, UiTheme.PanelSoft);
            UiControls.Place(card.GetComponent<RectTransform>(), 50f, -84f, w - 100f, 104f);
            var dot = UiTheme.Panel(card.transform, UiTheme.Danger).GetComponent<Image>();
            UiControls.Place(dot.rectTransform, 22f, -24f, 16f, 16f);
            var cl1 = UiControls.Label(card.transform, "", 24, 54f, -10f, w - 420f, 36f, UiTheme.TextPrimary, bold: true);
            var cl2 = UiControls.Label(card.transform, "", 18, 54f, -52f, w - 420f, 40f, UiTheme.TextMuted);
            var cBtn = UiControls.Button(card.transform, "", () =>
            {
                if (bridge == null) return;
                if (bridge.Wanted) bridge.Disconnect(); else bridge.Connect(st.treadmillId);
            }, w - 100f - 250f, -26f, 220f, 52f, UiTheme.Accent).GetComponentInChildren<Text>();
            UiLive.Attach(card, () =>
            {
                string name = prof.name != "" ? prof.name : st.treadmillName != "" ? st.treadmillName : "Noch kein Laufband gewählt";
                bool conn = bridge != null && bridge.IsConnected, data = belt != null && belt.IsActive;
                dot.color = data ? UiTheme.Success : conn ? UiTheme.Accent : UiTheme.Danger;
                cl1.text = Jogging.Core.Loc.F("Dein Laufband: {0}", Jogging.Core.Loc.T(name)) + (bridge != null && bridge.Protocol != "" && conn ? " · " + MacBleBridgeTransport.ProtocolName(bridge.Protocol) : prof.protocol != "" ? " · " + prof.protocol : "");
                cl2.text = bridge == null ? Jogging.Core.Loc.T("Kein Bluetooth-Anschluss")
                         : data ? Jogging.Core.Loc.F("verbunden · {0}", prof.inclineKnown || prof.speedKnown ? Jogging.Core.Loc.F("das Band meldet: {0}", prof.RangeText()) : Jogging.Core.Loc.T("liefert Daten"))
                         : bridge.Wanted ? Jogging.Core.Loc.T(conn ? "verbunden, warte auf Daten …" : "verbinde … (Band eingeschaltet, nicht im Standby?)")
                         : "getrennt";
                cBtn.text = bridge != null && bridge.Wanted ? Jogging.Core.Loc.T("Trennen") : Jogging.Core.Loc.T("Verbinden");
            });

            // --- the runner's caps for this treadmill
            float y = -216f;
            if (!string.IsNullOrEmpty(id))
            {
                UiControls.Label(p, "DEINE GRENZEN FÜR DIESES BAND", 16, 50f, y, 500f, 24f, UiTheme.TextMuted, bold: true);
                y -= 36f;
                float incHi = prof.inclineKnown && prof.inclineMax > prof.inclineMin ? prof.inclineMax : 40f;
                UiControls.Stepper(p, Jogging.Core.Loc.T("Steigung bis"), () => $"{prof.maxIncline:0} %",
                    () => { prof.maxIncline = Mathf.Max(0f, prof.maxIncline - 1f); AppSettings.Save(); },
                    () => { prof.maxIncline = Mathf.Min(incHi, prof.maxIncline + 1f); AppSettings.Save(); }, 50f, y, 560f);
                y -= 60f;
                if (prof.CanDecline)
                {
                    UiControls.Toggle(p, "Gefälle", "bergab erlaubt", "aus – nur bergauf", prof.decline,
                        v => { prof.decline = v; AppSettings.Save(); ShowTreadmillSetup(); }, 50f, y, 560f);
                    y -= 60f;
                    if (prof.decline)
                    {
                        float decHi = Mathf.Min(-prof.inclineMin, 10f);
                        UiControls.Stepper(p, Jogging.Core.Loc.T("Gefälle bis"), () => $"−{prof.maxDecline:0} %",
                            () => { prof.maxDecline = Mathf.Max(1f, prof.maxDecline - 1f); AppSettings.Save(); },
                            () => { prof.maxDecline = Mathf.Min(decHi, prof.maxDecline + 1f); AppSettings.Save(); }, 50f, y, 560f);
                        y -= 60f;
                    }
                }
                UiControls.Toggle(p, "Steigungsschritt", "2 % (z. B. F37)", "1 %", prof.inclineStep >= 2f,
                    v => { prof.inclineStep = v ? 2f : 1f; AppSettings.Save(); }, 50f, y, 560f);
                y -= 60f;
                string RampText() => prof.rampKmhPerS < 0f ? Jogging.Core.Loc.T("aus (Band meldet echtes Tempo)") : (Jogging.Core.Units.FmtSpeed(prof.rampKmhPerS > 0f ? prof.rampKmhPerS : prof.protocol == "FitShow" ? 1f : 0f) + "/s") + (prof.rampKmhPerS == 0f ? " (auto)" : "");
                UiControls.Stepper(p, "Anlaufen/Bremsen", RampText,
                    () => { float v = prof.rampKmhPerS > 0f ? prof.rampKmhPerS : 1f; prof.rampKmhPerS = v <= 0.3f ? -1f : Mathf.Max(0.3f, v - 0.2f); AppSettings.Save(); },
                    () => { float v = prof.rampKmhPerS > 0f ? prof.rampKmhPerS : prof.rampKmhPerS < 0f ? 0.1f : 1f; prof.rampKmhPerS = Mathf.Min(5f, v + 0.2f); AppSettings.Save(); }, 50f, y, 560f);
                y -= 60f;
                float spHi = prof.speedKnown && prof.speedMax > 1f ? prof.speedMax : 25f;
                UiControls.Stepper(p, Jogging.Core.Loc.T("Tempo bis (Workouts)"), () => Jogging.Core.Units.FmtSpeed(prof.maxSpeed, "0.#"),
                    () => { prof.maxSpeed = Mathf.Max(4f, prof.maxSpeed - Jogging.Core.Units.SpeedToKmh(0.5f)); AppSettings.Save(); },
                    () => { prof.maxSpeed = Mathf.Min(spHi, prof.maxSpeed + Jogging.Core.Units.SpeedToKmh(0.5f)); AppSettings.Save(); }, 50f, y, 560f);
                y -= 52f;
                var note = UiControls.Label(p, "Die App bleibt immer auch im Bereich, den das Band selbst meldet. Das Tempo stellt sie nur in Workouts " +
                                               "und nur, wenn „Tempo (Workouts)“ an ist. Gefälle nur, wenn das Band es kann und du es erlaubst.",
                                            16, 50f, y, 620f, 60f, UiTheme.TextMuted);
                note.horizontalOverflow = HorizontalWrapMode.Wrap;
            }

            // --- another treadmill: the search runs as soon as the page opens
            float lx = 700f, lw = w - lx - 50f;
            UiControls.Label(p, "ANDERES LAUFBAND", 16, lx, -216f, 300f, 24f, UiTheme.TextMuted, bold: true);
            var status = UiControls.Label(p, "", 17, lx, -244f, lw - 170f, 28f, UiTheme.TextMuted);
            UiControls.Button(p, Jogging.Core.Loc.T("Neu suchen"), () => { if (bridge != null) { bridge.StartDeviceSearch(); searchStarted = Time.unscaledTime; } }, lx + lw - 160f, -212f, 160f, 44f);
            if (bridge != null && (searchStarted <= 0f || Time.unscaledTime - searchStarted > 20f)) { bridge.StartDeviceSearch(); searchStarted = Time.unscaledTime; }
            var listRoot = new GameObject("Found", typeof(RectTransform)).transform;
            listRoot.SetParent(p, false);
            var lrt = (RectTransform)listRoot;
            lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0f, 1f);
            lrt.anchoredPosition = Vector2.zero; lrt.sizeDelta = new Vector2(w, 900f);
            string shown = null;
            UiLive.Attach(listRoot.gameObject, () =>
            {
                if (bridge == null) { status.text = Jogging.Core.Loc.T("Kein Bluetooth-Anschluss"); return; }
                bool searching = Time.unscaledTime - searchStarted < 20f;
                // own treadmill first (a connected treadmill doesn't advertise, so the search can't see it), then the others
                var rows = new System.Collections.Generic.List<MacBleBridgeTransport.FoundDevice>();
                if (st.treadmillId != "") rows.Add(new MacBleBridgeTransport.FoundDevice { kind = prof.protocol != "" ? prof.protocol : "FitShow", id = st.treadmillId, name = prof.name != "" ? prof.name : st.treadmillName });
                foreach (var f in bridge.Found) if (f.kind != "HR" && f.id != st.treadmillId) rows.Add(f);
                int others = rows.Count - (st.treadmillId != "" ? 1 : 0);
                status.text = searching ? Jogging.Core.Loc.F("suche … ({0} weitere gefunden)", others) : others == 0 ? Jogging.Core.Loc.T("keine weiteren Laufbänder in der Nähe") : Jogging.Core.Loc.F("{0} weitere gefunden", others);
                string sig = searching + "|" + bridge.Wanted + bridge.IsConnected + bridge.TreadmillWantedId + "|" + string.Join(",", rows.ConvertAll(r => r.id));
                if (sig == shown) return;
                shown = sig;
                for (int i = listRoot.childCount - 1; i >= 0; i--) Destroy(listRoot.GetChild(i).gameObject);
                float ry = -282f;
                foreach (var dev in rows)
                {
                    if (ry < -800f) break;
                    var d = dev;
                    bool mine = d.id == st.treadmillId;
                    bool connecting = mine && bridge.Wanted && !bridge.IsConnected;
                    UiControls.Label(listRoot, (d.name != "" ? d.name : Jogging.Core.Loc.T("(ohne Name)")) + (mine ? Jogging.Core.Loc.T("  · dein Laufband") : ""), 20, lx, ry, lw - 170f, 30f, UiTheme.TextPrimary, bold: mine);
                    UiControls.Label(listRoot, KindText(d.kind), 16, lx, ry - 28f, lw - 170f, 24f, Pickable(d.kind) ? UiTheme.TextMuted : UiTheme.Danger);
                    if (mine && bridge.IsConnected) UiControls.Label(listRoot, "✓ verbunden", 18, lx + lw - 160f, ry - 4f, 160f, 44f, UiTheme.Success, TextAnchor.MiddleCenter, bold: true);
                    else if (connecting) UiControls.Label(listRoot, "verbinde …", 18, lx + lw - 160f, ry - 4f, 160f, 44f, UiTheme.TextMuted, TextAnchor.MiddleCenter);
                    else if (Pickable(d.kind))
                        UiControls.Button(listRoot, "Verbinden", () =>
                        {
                            // this one becomes "your treadmill": remembered, connected; the page stays open
                            st.treadmillId = d.id; st.treadmillName = d.name; AppSettings.Save();
                            var tp = AppSettings.Treadmill(d.id); if (d.name != "") tp.name = d.name; if (tp.protocol == "") tp.protocol = d.kind; AppSettings.Save();
                            bridge.Disconnect(); bridge.Connect(d.id);
                            ShowTreadmillSetup();
                        }, lx + lw - 160f, ry - 4f, 160f, 44f, UiTheme.Accent);
                    ry -= 64f;
                }
                if (rows.Count <= 1)
                {
                    var hint = UiControls.Label(listRoot, "Ein anderes Laufband einschalten (nicht im Standby) – es erscheint hier. Ein Laufband, das gerade mit einem anderen Gerät verbunden ist, ist nicht zu sehen.",
                                                16, lx, ry - 8f, lw, 70f, UiTheme.TextMuted);
                    hint.horizontalOverflow = HorizontalWrapMode.Wrap;
                }
            });

            UiControls.Button(p, Jogging.Core.Loc.T("Zurück"), () => ShowTreadmillSettings(), w - 250f, -860f, 200f, 56f);
        }

        private static float searchStarted;

        /// <summary>Open a page (tests): "main", "settings", "profile".</summary>
        public static void OpenPageForTest(string page)
        {
            var m = FindFirstObjectByType<StartMenuUI>(); if (m == null) return;
            if (page == "editor") m.ShowEditor(null); else if (page == "main") m.ShowEntryAlways(); else if (page == "settings") m.ShowSettingsPage(); else if (page == "profile") m.ShowProfile();
            else if (page == "export") m.ShowExport(); else if (page == "import") m.ShowImport(null); else if (page == "stats") m.ShowStats();
            else if (page == "gpxeditor") // the first GPX file in Downloads, opened in the editor
            {
                var c = Jogging.Route.RouteShare.FindFiles().Find(x => x.doc != null && Jogging.Route.GpxImport.IsGpx(x.doc.@params));
                if (c != null) m.ShowEditor(c.doc);
            }
        }

        /// <summary>Open the page (tests).</summary>
        public static void OpenTreadmillSetup() { var m = FindFirstObjectByType<StartMenuUI>(); if (m != null) m.ShowTreadmillSetup(); }
    }
}
