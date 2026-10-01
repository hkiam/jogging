using UnityEngine;
using UnityEngine.UI;
using Jogging.Profile;

namespace Jogging.UI
{
    /// <summary>
    /// Export page (Statistik → Export): every run of the active runner as TCX into Downloads/Jogging-Export,
    /// optionally automatically after each run, and how to get the files into Strava, Garmin Connect & co.
    /// </summary>
    public partial class StartMenuUI
    {
        private void ShowExport(string note = null)
        {
            const float w = 1100f;
            var p = NewPage(w, 760f);
            var ps = ProfileService.Instance;
            if (ps == null) { ShowHome(); return; }
            var st = Jogging.Core.AppSettings.Current;
            string folder = ExportFolderName();

            UiControls.Label(p, Jogging.Core.Loc.F("Export · {0}", ps.Profile.playerName), 34, 40f, -24f, 900f, 48f, UiTheme.TextPrimary, bold: true);
            var info = UiControls.Label(p, Jogging.Core.Loc.F("Läufe als TCX-Datei – mit Zeit, Strecke, Tempo, Puls und Höhenmetern – in „{0}“.", folder),
                20, 40f, -84f, w - 80f, 60f, UiTheme.TextMuted);
            info.horizontalOverflow = HorizontalWrapMode.Wrap;

            UiControls.Toggle(p, "Nach jedem Lauf", "automatisch exportieren", "nur von Hand", st.autoExport,
                v => { st.autoExport = v; Jogging.Core.AppSettings.Save(); }, 40f, -160f, 700f);

            int count = ps.SummariesOf(ps.Profile.id).Count;
            UiControls.Button(p, Jogging.Core.Loc.F("Alle {0} Läufe exportieren", count), () =>
            {
                try
                {
                    var (written, total) = ps.ExportAll(ps.Profile);
                    ShowExport(written == 0 && total > 0 ? Jogging.Core.Loc.T("Alle Läufe waren schon exportiert.")
                                                         : Jogging.Core.Loc.F("{0} Läufe exportiert.", written));
                }
                catch (System.Exception e) { Debug.LogWarning($"[Jogging] Export: {e.Message}"); ShowExport(Jogging.Core.Loc.T("Export fehlgeschlagen")); }
            }, 40f, -232f, 420f, 56f, UiTheme.Accent);
            if (!Jogging.Core.Platform.IsMobile)
                UiControls.Button(p, Jogging.Core.Loc.T("Ordner öffnen"), () =>
                {
                    System.IO.Directory.CreateDirectory(ProfileService.ExportFolder);
                    Application.OpenURL("file://" + ProfileService.ExportFolder);
                }, 480f, -232f, 240f, 56f);
            if (note != null) UiControls.Label(p, note, 20, 740f, -240f, w - 780f, 40f, UiTheme.Success);

            UiControls.Label(p, "SO KOMMEN DIE LÄUFE HINÜBER", 16, 40f, -330f, 600f, 24f, UiTheme.TextMuted, bold: true);
            var how = UiControls.Label(p,
                Jogging.Core.Loc.T("Strava: strava.com → ＋ → Datei hochladen → TCX wählen; als Sportart „Virtueller Lauf“ oder „Laufband“.") + "\n" +
                Jogging.Core.Loc.T("Garmin Connect: connect.garmin.com → Importieren → Daten → TCX-Datei.") + "\n" +
                Jogging.Core.Loc.T("Runalyze, Intervals.icu, TrainingPeaks & Co. lesen TCX ebenfalls.") + "\n" +
                Jogging.Core.Loc.T("Auf dem Laufband gibt es kein GPS – die Läufe erscheinen als Indoor-Läufe ohne Karte."),
                19, 40f, -362f, w - 80f, 200f, UiTheme.TextPrimary, TextAnchor.UpperLeft);
            how.horizontalOverflow = HorizontalWrapMode.Wrap;

            UiControls.Button(p, Jogging.Core.Loc.T("Zurück"), () => ShowStats(), w - 240f, -680f, 200f, 56f);
        }

        private static string ExportFolderName() =>
            (Jogging.Core.Platform.IsMobile ? Jogging.Core.DataPaths.ExchangeName : "Downloads") + " › Jogging-Export";
    }
}
