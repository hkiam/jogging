using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Jogging.Core;
using Jogging.Profile;

namespace Jogging.UI
{
    /// <summary>
    /// Data backup: save everything (runners, logbook, routes, workouts, settings) as one ZIP in
    /// Downloads, and restore one of the backups found there (or in the app's own backup folder).
    /// Restoring asks for a second click and first saves the current state automatically.
    /// </summary>
    public partial class StartMenuUI
    {
        private static string DownloadsDir
        {
            get
            {
                string d = Jogging.Core.DataPaths.Downloads;
                return Directory.Exists(d) ? d : Jogging.Core.DataPaths.Root;
            }
        }

        private static string SafetyDir => Path.Combine(Jogging.Core.DataPaths.Root, Jogging.Core.Loc.T("Sicherungen"));

        private void ShowBackup(string note = null)
        {
            const float w = 1100f;
            var p = NewPage(w, 820f);
            UiControls.Label(p, "Datensicherung", 36, 50f, -24f, 600f, 56f, UiTheme.TextPrimary, bold: true);
            var info = UiControls.Label(p, "Alle Läufer, Läufe, Strecken, Workouts und Einstellungen in einer Datei – z. B. für einen neuen Rechner.",
                18, 50f, -84f, w - 100f, 30f, UiTheme.TextMuted);
            info.horizontalOverflow = HorizontalWrapMode.Wrap;

            UiControls.Button(p, Jogging.Core.Loc.F("Jetzt sichern (in „{0}“)", Jogging.Core.DataPaths.ExchangeName), () =>
            {
                try
                {
                    string path = DataBackup.Create(Jogging.Core.DataPaths.Root, DownloadsDir);
                    Debug.Log("[Jogging] Datensicherung: " + path);
                    ShowBackup(Jogging.Core.Loc.F("Gesichert: {0}", Path.GetFileName(path)));
                }
                catch (Exception e) { ShowBackup(Jogging.Core.Loc.F("Sichern fehlgeschlagen: {0}", Jogging.Core.Loc.T(e.Message))); }
            }, 50f, -130f, 460f, 60f, UiTheme.Success, 22);
            if (note != null) UiControls.Label(p, note, 18, 530f, -140f, w - 580f, 40f, UiTheme.TextPrimary);

            UiControls.Label(p, "WIEDERHERSTELLEN", 16, 50f, -220f, 400f, 24f, UiTheme.TextMuted, bold: true);
            var found = DataBackup.Find(DownloadsDir, SafetyDir);
            if (found.Count == 0) UiControls.Label(p, Jogging.Core.Loc.F("Keine Sicherung gefunden (in „{0}“).", Jogging.Core.DataPaths.ExchangeName), 19, 50f, -252f, 800f, 30f, UiTheme.TextMuted);
            float y = -252f;
            for (int i = 0; i < found.Count && i < 6; i++)
            {
                string path = found[i];
                var what = DataBackup.Inspect(path);
                var row = UiTheme.Panel(p, UiTheme.PanelSoft);
                UiControls.Place(row.GetComponent<RectTransform>(), 50f, y, w - 100f, 70f);
                bool safety = path.StartsWith(SafetyDir);
                UiControls.Label(row.transform, File.GetLastWriteTime(path).ToString("g", Jogging.Core.Loc.Culture) + (safety ? Jogging.Core.Loc.T("  ·  automatisch vor dem letzten Wiederherstellen") : ""),
                    20, 18f, -6f, 700f, 30f, UiTheme.TextPrimary, bold: true);
                UiControls.Label(row.transform, what.HasValue ? Jogging.Core.Loc.F("{0} Läufer · {1} · {2}", what.Value.runners, Runs(what.Value.runs), Path.GetFileName(path)) : Jogging.Core.Loc.T("keine gültige Sicherung"),
                    16, 18f, -38f, 700f, 26f, UiTheme.TextMuted);
                if (!what.HasValue) { y -= 78f; continue; }
                Text lbl = null; bool armed = false;
                lbl = UiControls.Button(row.transform, "Wiederherstellen", () =>
                {
                    if (!armed) { armed = true; lbl.text = Jogging.Core.Loc.T("Sicher? Ersetzt alle Daten"); return; }
                    try
                    {
                        DataBackup.Restore(path, Jogging.Core.DataPaths.Root, SafetyDir);
                        Debug.Log("[Jogging] Daten wiederhergestellt aus " + path);
                        AppSettings.Reload();
                        ProfileService.ForgetChoice();
                        Jogging.World.SceneReload.Now(); // frees the old landscape first // everything reads the data anew
                    }
                    catch (Exception e) { ShowBackup(Jogging.Core.Loc.F("Wiederherstellen fehlgeschlagen: {0}", Jogging.Core.Loc.T(e.Message))); }
                }, w - 440f, -12f, 320f, 46f, UiTheme.Danger, 18).GetComponentInChildren<Text>();
                y -= 78f;
            }
            UiControls.Label(p, "Vor jedem Wiederherstellen wird der aktuelle Stand automatisch gesichert – es lässt sich also immer rückgängig machen.",
                16, 50f, -730f, w - 600f, 56f, UiTheme.TextMuted).horizontalOverflow = HorizontalWrapMode.Wrap;
            if (!Platform.IsMobile) // tablets have no file browser to open
                UiControls.Button(p, Jogging.Core.Loc.T("Ordner öffnen"), () => Application.OpenURL("file://" + DownloadsDir), w - 520f, -730f, 220f, 56f);
            UiControls.Button(p, Jogging.Core.Loc.T("Zurück"), () => ShowTreadmillSettings(), w - 250f, -730f, 200f, 56f);
        }
    }
}
