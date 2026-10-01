using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Jogging.Profile;
using Jogging.Route;
using Jogging.World;

namespace Jogging.UI
{
    /// <summary>
    /// Start screen and route menu (the run waits while it is open; the world keeps building behind it):
    ///   • Runners (who runs?), new runner, runner home with Quick Run — see StartMenuUI.Runners.cs.
    ///   • Editor: name, templates, parameters (length, loop, curviness, climbs, rolling, max grade),
    ///     variant (seed) and a live profile preview with key figures; save / save &amp; run.
    ///   • My routes: saved <c>.jogroute</c> files with best time; run, edit, copy, delete.
    /// Running a route other than the scene's current one sets <see cref="RouteRuntime.Selected"/>
    /// and reloads the scene (the terrain is shaped for the route from the start); the menu then
    /// doesn't show again. "-autostart" on the command line skips the menu (tests).
    /// </summary>
    public partial class StartMenuUI : MonoBehaviour
    {
        [SerializeField] private TrackManager track;
        [SerializeField] private bool showOnStart = true;

        private GameObject root, page;
        private static bool skipOnce; // set before reloading the scene for a newly chosen route

        /// <summary>Skip the menu on the next scene load (e.g. "run again" on the finish screen).</summary>
        public static void SkipNextOpen() => skipOnce = true;

        /// <summary>True while the menu is shown (the run session holds the run).</summary>
        public static bool IsOpen { get; private set; }

        private const float PlanPaceKmh = 10f; // for the duration estimate

        private void Start()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            BuildRoot();
            Training.WorkoutRuntime.Armed = false; // a new scene: armed only once the run begins here
            bool auto = System.Array.IndexOf(Jogging.Core.Args.All, "-autostart") >= 0;
            if (skipOnce || auto) { skipOnce = false; Begin(); }
            else if (showOnStart) { Open(); if (reopenSettings) { reopenSettings = false; ShowSettingsPage(); } else ShowEntry(); }
        }

        private void Open()
        {
            root.SetActive(true);
            IsOpen = true;
            // Time keeps running: the world builds behind the menu (the splash); the run session holds
            // the runner, the crowd and the clock until the run starts.
        }

        private void Begin()
        {
            root.SetActive(false);
            IsOpen = false;
            Time.timeScale = 1f; // the run session counts down / waits for the belt
            Training.WorkoutRuntime.Armed = true; // a selected workout starts with this run
        }

        /// <summary>Open a saved route in the workshop (reloads the scene with it).</summary>
        private void OpenWorkshop(RouteDoc doc)
        {
            RouteRuntime.Selected = doc;
            WorkshopUI.OpenOnLoad = true;
            skipOnce = true;
            Time.timeScale = 1f;
            IsOpen = false;
            Jogging.World.SceneReload.Now(); // frees the old landscape first
        }

        /// <summary>Run this route: right away if it is the scene's route, else reload with it.</summary>
        private void Run(RouteDoc doc)
        {
            var cur = RouteRuntime.Current;
            bool same = cur != null && (string.IsNullOrEmpty(doc.id)
                ? string.IsNullOrEmpty(cur.id) && cur.meta.name == doc.meta.name && cur.generator.seed == doc.generator.seed
                  && JsonUtility.ToJson(cur.@params) == JsonUtility.ToJson(doc.@params)
                : cur.id == doc.id && cur.revision == doc.revision);
            if (same) { Begin(); return; }
            RouteRuntime.Selected = doc;
            skipOnce = true;
            Time.timeScale = 1f;
            IsOpen = false;
            Jogging.World.SceneReload.Now(); // frees the old landscape first
        }

        // ------------------------------------------------------------------ chrome

        private void BuildRoot()
        {
            var canvasGo = new GameObject("Start Menu Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            root = canvasGo;

            var dim = UiTheme.Panel(canvasGo.transform, new Color(0.05f, 0.07f, 0.10f, 0.92f));
            dim.GetComponent<Image>().sprite = null;
            dimImage = dim.GetComponent<Image>();
            UiTheme.Stretch(dim.GetComponent<RectTransform>());
            root.SetActive(false);
        }

        private Image dimImage;

        /// <summary>Runner pages show the world (and the runner's figure) through a lighter dim.</summary>
        private void SetDim(float alpha)
        {
            if (dimImage != null) dimImage.color = new Color(0.05f, 0.07f, 0.10f, alpha);
        }

        // Esc on the Mac, the back key on Android: the page's own "Zurück" (or "Abbrechen"), if it has one.
        private void Update()
        {
            if (!IsOpen || page == null || !Input.GetKeyDown(KeyCode.Escape)) return;
            var sel = UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            if (sel != null && sel.GetComponent<InputField>() is InputField f && f.isFocused) return; // Esc leaves the text field first
            Button back = null;
            foreach (var b in page.GetComponentsInChildren<Button>())
            {
                var t = b.GetComponentInChildren<Text>();
                if (t == null || !b.interactable) continue;
                if (t.text == Jogging.Core.Loc.T("Zurück")) { back = b; break; }
                if (t.text == Jogging.Core.Loc.T("Abbrechen") && back == null) back = b;
            }
            back?.onClick.Invoke();
        }

        private Transform NewPage(float w, float h)
        {
            SetDim(0.92f);
            if (page != null) Destroy(page);
            page = UiTheme.Panel(root.transform);
            var rt = page.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(w, h);
            return page.transform;
        }

        // ------------------------------------------------------------------ editor

        private RouteDoc edit;          // working copy
        private string editOriginal;    // params JSON when opened (edits bump the revision)
        private bool editSaved;         // editing a saved route (vs. a new one)
        private int climbCount; private float climbHeight, climbGrade;
        private ProfileChart chart;
        private Text figures;
        private InputField nameField;

        private void ShowEditor(RouteDoc source)
        {
            if (source == null)
            {
                edit = RoutePresets.Create("huegelig", NewSeed());
                edit.meta.name = Jogging.Core.Loc.T("Neue Strecke");
                editSaved = false;
            }
            else
            {
                edit = RouteStore.FromJson(RouteStore.ToJson(source)); // copy
                editSaved = !string.IsNullOrEmpty(edit.id);
            }
            editOriginal = JsonUtility.ToJson(edit.@params) + edit.generator.seed;
            ReadClimbs();
            BuildEditor();
        }

        private void ReadClimbs()
        {
            var c = edit.@params.climbs;
            climbCount = c.Count;
            climbHeight = c.Count > 0 ? Mathf.Abs(c[0].heightM) : 30f;
            climbGrade = c.Count > 0 ? c[0].grade : 0.06f;
        }

        private void BuildEditor()
        {
            var p = NewPage(1560f, 1010f);
            var prm = edit.@params;
            UiControls.Label(p, editSaved ? "Strecke bearbeiten" : "Neue Strecke", 34, 40f, -24f, 700f, 48f, UiTheme.TextPrimary, bold: true);

            // Left column: name, templates, parameters.
            float x = 40f, w = 720f, y = -96f;
            UiControls.Label(p, "Name", 20, x, y, 210f, 44f, UiTheme.TextMuted);
            nameField = UiControls.TextField(p, edit.meta.name, x + 215f, y, w - 215f);
            y -= 64f;

            UiControls.Label(p, "Vorlage", 20, x, y, 210f, 44f, UiTheme.TextMuted);
            string[] ids = { "flach", "huegelig", "bergauf", "wald", "see", "pass", "wellen", "winterwald", "mondnacht" };
            string[] labels = { "Flach", "Hügelig", "Bergauf", "Wald", "Seerunde", "Bergpass", "Wellen", "Winterwald", "Mondnacht" };
            float bw = (w - 215f - 30f) / 4f;
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                UiControls.Button(p, labels[i], () => ApplyTemplate(id), x + 215f + (i % 4) * (bw + 10f), y - (i / 4) * 52f, bw, 44f, null, 20);
            }
            y -= 52f * ((ids.Length + 3) / 4) + 20f; // rows of templates

            bool gpx = GpxImport.IsGpx(prm);
            if (gpx) // a real route: length, form and profile come from the GPX file
            {
                var fixedNote = UiControls.Label(p, Jogging.Core.Loc.F("Aus einer GPX-Datei: Länge, Höhenprofil und Form ({0}) stehen fest. Landschaft und Stimmung kannst du frei wählen.",
                    Jogging.Core.Loc.T(prm.loop ? "Rundkurs" : "Strecke (A → B)")), 20, x, y, w, 120f, UiTheme.TextMuted, TextAnchor.UpperLeft);
                fixedNote.horizontalOverflow = HorizontalWrapMode.Wrap;
                y -= 110f;
            }
            else
            {
            UiControls.SliderRow(p, Jogging.Core.Loc.T("Länge"), 1f, 21f, prm.lengthKm, false, v => Jogging.Core.Units.FmtDist(Jogging.Core.Units.RoundKm(v, 0.5f) * 1000f), v => { prm.lengthKm = Jogging.Core.Units.RoundKm(v, 0.5f); Changed(); }, x, y, w); y -= 52f;
            UiControls.Toggle(p, Jogging.Core.Loc.T("Form"), Jogging.Core.Loc.T("Rundkurs"), Jogging.Core.Loc.T("Strecke (A → B)"), prm.loop, v => { prm.loop = v; Changed(); }, x, y, w); y -= 58f;
            UiControls.SliderRow(p, Jogging.Core.Loc.T("Kurvigkeit"), 0f, 1f, prm.curviness, false, v => Word(v, "gerade", "sanft", "kurvig", "verschlungen"), v => { prm.curviness = v; Changed(); }, x, y, w); y -= 62f;

            UiControls.SliderRow(p, Jogging.Core.Loc.T("Anstiege"), 0f, 5f, climbCount, true, v => v < 0.5f ? "keine" : $"{v:0}", v => { climbCount = Mathf.RoundToInt(v); Changed(); }, x, y, w); y -= 52f;
            UiControls.SliderRow(p, Jogging.Core.Loc.T("Höhe je Anstieg"), 10f, 100f, climbHeight, true, v => Jogging.Core.Units.FmtElev(v), v => { climbHeight = v; Changed(); }, x, y, w); y -= 52f;
            UiControls.SliderRow(p, Jogging.Core.Loc.T("Steilheit"), 3f, 10f, climbGrade * 100f, true, v => $"{v:0} %", v => { climbGrade = v / 100f; Changed(); }, x, y, w); y -= 62f;

            UiControls.SliderRow(p, Jogging.Core.Loc.T("Welligkeit"), 0f, 1f, prm.rolling, false, v => Word(v, "flach", "leicht", "wellig", Jogging.Core.Loc.T("sehr wellig")), v => { prm.rolling = v; Changed(); }, x, y, w); y -= 52f;
            UiControls.SliderRow(p, Jogging.Core.Loc.T("Max. Steigung"), 5f, 12f, prm.maxGrade * 100f, true, v => $"{v:0} %", v => { prm.maxGrade = v / 100f; Changed(); }, x, y, w); y -= 62f;
            }

            UiControls.Label(p, "Variante", 20, x, y, 210f, 44f, UiTheme.TextMuted);
            var seedLabel = UiControls.Label(p, $"#{edit.generator.seed}", 22, x + 215f, y, 200f, 44f, UiTheme.TextPrimary, bold: true);
            UiControls.Button(p, Jogging.Core.Loc.T("Neu würfeln"), () => { edit.generator.seed = NewSeed(); seedLabel.text = $"#{edit.generator.seed}"; Changed(); }, x + 420f, y, w - 420f, 44f, null, 20);
            y -= 62f;
            Cycle(p, "Jahreszeit", new[] { "spring", "summer", "autumn", "winter" }, new[] { "Frühling", "Sommer", "Herbst", "Winter" },
                prm.season, v => prm.season = v, x, y, w);
            y -= 52f;
            Cycle(p, "Wetter", new[] { "clear", "cloudy", "rain", "snow" }, new[] { "klar", "bewölkt", "Regen", "Schnee" },
                prm.weather, v => prm.weather = v, x, y, w);
            y -= 52f;
            // Night sky: far from any town the Milky Way, near one only the bright stars and an orange glow
            UiControls.SliderRow(p, Jogging.Core.Loc.T("Nachthimmel"), 0f, 1f, prm.lightPollution, false, v => Word(v, "sehr dunkel", "dunkel", "Ortsnähe", "Stadtnähe"), v => prm.lightPollution = v, x, y, w);

            // Right column: profile preview + figures.
            float rx = 800f, rw = 720f;
            UiControls.Label(p, "Höhenprofil", 20, rx, -96f, rw, 32f, UiTheme.TextMuted);
            var frame = UiTheme.Panel(p, new Color(0.07f, 0.09f, 0.12f, 1f));
            UiControls.Place(frame.GetComponent<RectTransform>(), rx, -132f, rw, 250f);
            var cg = new GameObject("Chart", typeof(RectTransform), typeof(CanvasRenderer), typeof(ProfileChart));
            cg.transform.SetParent(frame.transform, false);
            UiTheme.Stretch(cg.GetComponent<RectTransform>(), 14f);
            chart = cg.GetComponent<ProfileChart>();
            chart.color = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.30f);
            chart.raycastTarget = false;
            figures = UiControls.Label(p, "", 22, rx, -396f, rw, 100f, UiTheme.TextPrimary, TextAnchor.UpperLeft);

            // Landscape & mood.
            UiControls.Label(p, "Landschaft & Stimmung", 20, rx, -500f, rw, 32f, UiTheme.TextMuted, bold: true);
            float ly = -538f;
            Cycle(p, "Vegetation", new[] { "mixed", "conifer", "birch", "meadow" },
                new[] { Jogging.Core.Loc.T("Mischwald"), Jogging.Core.Loc.T("Nadelwald"), Jogging.Core.Loc.T("Birkenhain"), Jogging.Core.Loc.T("Wiese & Heide") }, prm.vegetation, v => prm.vegetation = v, rx, ly, rw); ly -= 46f;
            UiControls.SliderRow(p, Jogging.Core.Loc.T("Dichte"), 0f, 1f, prm.density, false, v => Word(v, "licht", "locker", "dicht", Jogging.Core.Loc.T("sehr dicht")), v => prm.density = v, rx, ly, rw); ly -= 46f;
            UiControls.SliderRow(p, Jogging.Core.Loc.T("Lichtungen"), 0f, 1f, prm.clearings, false, v => Word(v, "kaum", "einige", "viele", Jogging.Core.Loc.T("sehr viele")), v => prm.clearings = v, rx, ly, rw); ly -= 46f;
            Cycle(p, "Wasser", new[] { "none", "lake", "stream", "both" }, new[] { "kein Wasser", "See am Weg", "Bach", "See & Bach" },
                prm.water, v => prm.water = v, rx, ly, rw); ly -= 46f;
            UiControls.SliderRow(p, Jogging.Core.Loc.T("Relief"), 0f, 1f, prm.relief, false, v => Word(v, "flach", "sanft", Jogging.Core.Loc.T("hügelig"), "bergig"), v => prm.relief = v, rx, ly, rw); ly -= 46f;
            UiControls.SliderRow(p, Jogging.Core.Loc.T("Tageszeit"), 5f, 23.5f, prm.timeOfDay, false, v => Jogging.Core.Loc.F("{0}:{1:00} Uhr", Mathf.FloorToInt(v), Mathf.RoundToInt((v % 1f) * 60f) % 60), v => prm.timeOfDay = Mathf.Round(v * 4f) / 4f, rx, ly, rw); ly -= 46f;
            UiControls.SliderRow(p, Jogging.Core.Loc.T("Dunst"), 0f, 1f, prm.haze, false, v => Word(v, Jogging.Core.Loc.T("klar"), "leicht", "dunstig", "neblig"), v => prm.haze = v, rx, ly, rw); ly -= 46f;
            Cycle(p, "Zuschauer", new[] { "none", "few", "some", "many" }, new[] { "keine", "wenige", "einige", "viele" },
                prm.spectators, v => prm.spectators = v, rx, ly, rw);

            // Actions.
            UiControls.Button(p, Jogging.Core.Loc.T("Zurück"), () => ShowHome(), rx, -926f, 200f, 56f);
            UiControls.Button(p, Jogging.Core.Loc.T("Speichern"), () => { Save(); ShowList(0); }, rx + 220f, -926f, 220f, 56f, UiTheme.Accent);
            UiControls.Button(p, Jogging.Core.Loc.T("▶  Speichern & laufen"), () => { Save(); Run(edit); }, rx + 460f, -926f, 260f, 56f, UiTheme.Success);

            Changed();
        }

        private void ApplyTemplate(string id)
        {
            var t = RoutePresets.Create(id, edit.generator.seed);
            string name = nameField != null ? nameField.text : edit.meta.name;
            var old = edit.@params;
            if (GpxImport.IsGpx(old)) // a GPX route keeps its course: the template only sets landscape and mood
            {
                var q = t.@params;
                q.source = old.source; q.gpxStepM = old.gpxStepM; q.gpxHeightsDm = old.gpxHeightsDm;
                q.lengthKm = old.lengthKm; q.loop = old.loop; q.endless = false; q.curviness = old.curviness;
                q.climbs = new List<RouteClimb>(); q.rolling = 0f; q.maxGrade = old.maxGrade; q.relief = old.relief;
            }
            edit.@params = t.@params;
            edit.meta.name = string.IsNullOrWhiteSpace(name) || name == "Neue Strecke" || name == Jogging.Core.Loc.T("Neue Strecke") ? t.meta.name : name;
            ReadClimbs();
            BuildEditor();
        }

        // Rebuild climbs from the sliders (evenly spread), regenerate, refresh the preview.
        private void Changed()
        {
            var prm = edit.@params;
            prm.endless = false;
            prm.climbs = new List<RouteClimb>();
            if (GpxImport.IsGpx(prm)) climbCount = 0; // its own profile
            for (int i = 0; i < climbCount; i++)
                prm.climbs.Add(new RouteClimb { atKm = prm.lengthKm * (i + 1f) / (climbCount + 1f), heightM = climbHeight, grade = climbGrade });
            RouteGenerator.Generate(edit);
            if (chart != null) chart.SetData(edit.profile.heightsM);
            if (figures != null)
            {
                var pr = edit.profile;
                int minutes = Mathf.RoundToInt(prm.lengthKm / PlanPaceKmh * 60f);
                figures.text =
                    Jogging.Core.Units.FmtDist(prm.lengthKm * 1000f) + "  ·  " + Jogging.Core.Loc.T(prm.loop ? "Rundkurs" : "Strecke") + "\n" +
                    Jogging.Core.Loc.F("↑ {0}   ↓ {1}   ·   max. {2:0.0} %\n", Jogging.Core.Units.FmtElev(pr.ascentM), Jogging.Core.Units.FmtElev(pr.descentM), pr.maxGradePercent) +
                    Jogging.Core.Loc.F("≈ {0}:{1:00} h bei {2}", minutes / 60, minutes % 60, Jogging.Core.Units.FmtSpeed(PlanPaceKmh, "0"));
            }
        }

        private void Save()
        {
            edit.meta.name = string.IsNullOrWhiteSpace(nameField.text) ? Jogging.Core.Loc.T("Meine Strecke") : nameField.text.Trim();
            if (string.IsNullOrEmpty(edit.meta.author) && ProfileService.Instance != null) edit.meta.author = ProfileService.Instance.Profile.playerName;
            if (editSaved && JsonUtility.ToJson(edit.@params) + edit.generator.seed != editOriginal)
                edit.revision = Mathf.Min(edit.revision + 1, RouteDoc.MaxRevision); // different course → its own best times
            RouteStore.Save(edit);
            editSaved = true;
            editOriginal = JsonUtility.ToJson(edit.@params) + edit.generator.seed;
            Debug.Log($"[Jogging] Strecke gespeichert: {edit.meta.name} (Rev. {edit.revision}) → {RouteStore.PathFor(edit)}");
        }

        // ------------------------------------------------------------------ my routes

        private string confirmDelete;

        private void ShowList(int pageIndex)
        {
            var p = NewPage(1320f, 880f);
            UiControls.Label(p, "Meine Strecken", 34, 40f, -24f, 700f, 48f, UiTheme.TextPrimary, bold: true);
            var all = RouteStore.LoadAll();
            const int perPage = 7;
            int pages = Mathf.Max(1, Mathf.CeilToInt(all.Count / (float)perPage));
            pageIndex = Mathf.Clamp(pageIndex, 0, pages - 1);

            if (all.Count == 0)
                UiControls.Label(p, "Noch keine Strecken gespeichert – leg unter „Neue Strecke“ eine an.", 22, 40f, -120f, 1240f, 40f, UiTheme.TextMuted);

            float y = -96f;
            for (int k = pageIndex * perPage; k < Mathf.Min(all.Count, (pageIndex + 1) * perPage); k++)
            {
                var d = all[k];
                var row = UiTheme.Panel(p, UiTheme.PanelSoft);
                UiControls.Place(row.GetComponent<RectTransform>(), 40f, y, 1240f, 86f);
                var rec = ProfileService.Instance != null ? ProfileService.Instance.RecordFor(d.id, d.revision) : null;
                string best = rec != null ? Jogging.Core.Loc.F("Bestzeit {0}  ({1}×)", Clock(rec.bestSeconds), rec.runs) : Jogging.Core.Loc.T("noch nicht gelaufen");
                UiControls.Label(row.transform, d.meta.name, 24, 20f, -8f, 520f, 36f, UiTheme.TextPrimary, bold: true);
                int edits = d.edits != null ? d.edits.Count : 0;
                UiControls.Label(row.transform, $"{Jogging.Core.Units.FmtDist(d.@params.lengthKm * 1000f)} · ↑{Jogging.Core.Units.FmtElev(d.profile.ascentM)} · {Jogging.Core.Loc.T(d.@params.loop ? "Rundkurs" : "Strecke")} · {best}" +
                    (edits > 0 ? (edits == 1 ? Jogging.Core.Loc.T(" · 1 Anpassung") : Jogging.Core.Loc.F(" · {0} Anpassungen", edits)) : ""), 18, 20f, -46f, 540f, 30f, UiTheme.TextMuted);
                var doc = d;
                UiControls.Button(row.transform, Jogging.Core.Loc.T("▶ Laufen"), () => Run(doc), 600f, -18f, 150f, 50f, UiTheme.Success, 20);
                UiControls.Button(row.transform, Jogging.Core.Loc.T("Werkstatt"), () => OpenWorkshop(doc), 760f, -18f, 150f, 50f, UiTheme.Accent, 20);
                UiControls.Button(row.transform, Jogging.Core.Loc.T("Bearbeiten"), () => ShowEditor(doc), 920f, -18f, 150f, 50f, null, 20);
                UiControls.Button(row.transform, "…", () => ShowActions(doc, pageIndex), 1080f, -18f, 140f, 50f, null, 22);
                y -= 98f;
            }

            if (pages > 1)
            {
                UiControls.Button(p, "◀", () => ShowList(pageIndex - 1), 40f, -792f, 70f, 56f);
                UiControls.Label(p, $"{pageIndex + 1} / {pages}", 22, 120f, -792f, 120f, 56f, UiTheme.TextMuted, TextAnchor.MiddleCenter);
                UiControls.Button(p, "▶", () => ShowList(pageIndex + 1), 250f, -792f, 70f, 56f);
            }
            UiControls.Button(p, Jogging.Core.Loc.T("Importieren"), () => ShowImport(null), 480f, -792f, 200f, 56f);
            if (!Jogging.Core.Platform.IsMobile)
            UiControls.Button(p, Jogging.Core.Loc.T("Ordner öffnen"), () => Application.OpenURL("file://" + RouteStore.Folder), 700f, -792f, 220f, 56f);
            UiControls.Button(p, Jogging.Core.Loc.T("＋ Neue Strecke"), () => ShowEditor(null), 940f, -792f, 200f, 56f, UiTheme.Accent);
            UiControls.Button(p, Jogging.Core.Loc.T("Zurück"), () => ShowHome(), 1160f, -792f, 120f, 56f);
        }

        // ------------------------------------------------------------------ route actions / sharing

        private void ShowActions(RouteDoc d, int pageIndex, string note = null)
        {
            var p = NewPage(1240f, 640f);
            lastActionsPage = pageIndex;
            ShareQr(p, d, 800f, -24f);
            UiControls.Label(p, d.meta.name, 32, 40f, -24f, 680f, 48f, UiTheme.TextPrimary, bold: true);
            UiControls.Label(p, Jogging.Core.Loc.F("{0} · ↑{1} · Rev. {2}", Jogging.Core.Units.FmtDist(d.@params.lengthKm * 1000f), Jogging.Core.Units.FmtElev(d.profile.ascentM), d.revision) +
                (string.IsNullOrEmpty(d.meta.author) ? "" : Jogging.Core.Loc.F(" · von {0}", d.meta.author)), 20, 40f, -72f, 680f, 32f, UiTheme.TextMuted);
            float y = -130f;
            UiControls.Button(p, "Teilen-Code kopieren", () =>
            {
                string code = RouteShare.ToCode(d);
                GUIUtility.systemCopyBuffer = code;
                ShowActions(d, pageIndex, Jogging.Core.Loc.F("Code ({0} Zeichen) ist in der Zwischenablage – z. B. per Messenger verschicken.", code.Length));
            }, 40f, y, 680f, 58f, UiTheme.Accent); y -= 72f;
            UiControls.Button(p, Jogging.Core.Loc.F("Als Datei exportieren ({0})", Jogging.Core.DataPaths.ExchangeName), () =>
            {
                string path = RouteShare.ExportFile(d);
                ShowActions(d, pageIndex, Jogging.Core.Loc.F("Gespeichert: {0} in {1}", System.IO.Path.GetFileName(path), Jogging.Core.DataPaths.ExchangeName));
            }, 40f, y, 680f, 58f); y -= 72f;
            UiControls.Button(p, Jogging.Core.Loc.T("Kopie anlegen"), () => { Duplicate(d, pageIndex); }, 40f, y, 680f, 58f); y -= 72f;
            bool asking = confirmDelete == d.id;
            UiControls.Button(p, asking ? "Wirklich löschen?" : "Löschen", () =>
            {
                if (confirmDelete == d.id) { RouteStore.Delete(d); confirmDelete = null; ShowList(pageIndex); }
                else { confirmDelete = d.id; ShowActions(d, pageIndex); }
            }, 40f, y, 680f, 58f, asking ? UiTheme.Danger : (Color?)null); y -= 84f;
            if (note != null) UiControls.Label(p, note, 19, 40f, y, 680f, 60f, UiTheme.Success, TextAnchor.UpperLeft);
            UiControls.Button(p, Jogging.Core.Loc.T("Zurück"), () => { confirmDelete = null; ShowList(pageIndex); }, 540f, -566f, 180f, 56f);
        }

        // The share code as a QR code: scan it with a phone camera and send the text on (messenger, mail),
        // then paste it on the other computer under "Importieren". Also as a PNG in Downloads.
        private void ShareQr(Transform p, RouteDoc d, float x, float y)
        {
            string code = RouteShare.ToCode(d);
            QrCode qr;
            try { qr = QrCode.EncodeText(code, QrCode.Ecc.Low); }
            catch (System.ArgumentException) { UiControls.Label(p, "Zu groß für einen QR-Code – bitte den Code kopieren.", 18, x, y - 60f, 400f, 60f, UiTheme.TextMuted); return; }
            var frame = UiTheme.Panel(p, Color.white);
            UiControls.Place(frame.GetComponent<RectTransform>(), x, y, 400f, 400f);
            var img = new GameObject("QR", typeof(RawImage));
            img.transform.SetParent(frame.transform, false);
            var tex = QrTexture.Create(qr, 4);
            img.GetComponent<RawImage>().texture = tex;
            UiControls.Place(img.GetComponent<RectTransform>(), 8f, -8f, 384f, 384f);
            UiControls.Label(p, "Mit der Handy-Kamera scannen, den Text weiterschicken und am anderen Rechner unter „Importieren“ einfügen.",
                16, x, y - 410f, 400f, 60f, UiTheme.TextMuted).horizontalOverflow = HorizontalWrapMode.Wrap;
            UiControls.Button(p, Jogging.Core.Loc.F("QR als Bild ({0})", Jogging.Core.DataPaths.ExchangeName), () =>
            {
                string dir = Jogging.Core.DataPaths.Downloads;
                if (!System.IO.Directory.Exists(dir)) dir = Jogging.Core.DataPaths.Root;
                string safe = new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(d.meta.name, char.IsLetterOrDigit)));
                string path = System.IO.Path.Combine(dir, $"Strecke-{safe}.png");
                System.IO.File.WriteAllBytes(path, QrTexture.Create(qr, 8).EncodeToPNG());
                ShowActions(d, lastActionsPage, Jogging.Core.Loc.F("QR-Code gespeichert: {0} in {1}", System.IO.Path.GetFileName(path), Jogging.Core.DataPaths.ExchangeName));
            }, x, y - 480f, 400f, 52f);
        }

        private int lastActionsPage;

        private RouteDoc pasted;
        private bool pastedMatch;
        private QrReader.Search qrSearch; // running in the background

        private void ShowImport(string note)
        {
            if (qrSearch != null && qrSearch.Done)
            {
                pasted = qrSearch.Doc; pastedMatch = qrSearch.Match; note = qrSearch.Info;
                qrSearch = null;
            }
            var p = NewPage(1320f, 880f);
            UiControls.Label(p, "Strecke importieren", 34, 40f, -24f, 900f, 48f, UiTheme.TextPrimary, bold: true);

            // From a share code in the clipboard.
            UiControls.Label(p, "Teilen-Code", 22, 40f, -92f, 400f, 36f, UiTheme.TextMuted, bold: true);
            UiControls.Button(p, "Code aus Zwischenablage einfügen", () =>
            {
                pasted = RouteShare.FromCode(GUIUtility.systemCopyBuffer, out pastedMatch, out string err);
                ShowImport(err);
            }, 40f, -132f, 520f, 54f, UiTheme.Accent);
            if (Jogging.Core.Platform.HasMacBridge) // reading QR images needs the Mac (Vision in the bridge)
            UiControls.Button(p, "QR-Bild lesen (Downloads, Schreibtisch)", () =>
            {
                if (qrSearch != null) return; // already searching
                pasted = null;
                qrSearch = QrReader.Start(RouteStore.LoadAll());
                ShowImport(null);
            }, 40f, -196f, 520f, 54f);
            if (qrSearch != null)
            {
                note = "Suche QR-Code …";
                int built = Time.frameCount;
                UiLive.Attach(p.gameObject, () =>
                {
                    if (Time.frameCount == built || qrSearch == null || !qrSearch.Done) return;
                    ShowImport(null); // takes the result
                });
            }
            if (pasted != null)
            {
                var local = RouteStore.LoadAll();
                string state = RouteShare.StateOf(pasted, local, out bool ok);
                UiControls.Label(p, $"{pasted.meta.name} · {Jogging.Core.Units.FmtDist(pasted.@params.lengthKm * 1000f)} · ↑{Jogging.Core.Units.FmtElev(pasted.profile.ascentM)} · {state}" +
                    (pastedMatch ? "" : Jogging.Core.Loc.T("  (Achtung: Höhenprofil weicht ab – andere App-Version)")), 20, 580f, -140f, 700f, 40f,
                    pastedMatch ? UiTheme.TextPrimary : UiTheme.Danger);
                if (ok)
                    UiControls.Button(p, Jogging.Core.Loc.T("Übernehmen"), () => { RouteShare.Import(pasted); var n = pasted.meta.name; pasted = null; ShowImport(Jogging.Core.Loc.F("„{0}“ übernommen.", n)); }, 1100f, -196f, 180f, 50f, UiTheme.Success, 20);
            }

            // From files in Downloads / on the Desktop.
            UiControls.Label(p, (Jogging.Core.Platform.IsMobile ? ".jogroute- und GPX-Dateien in „Austausch“" : ".jogroute- und GPX-Dateien in Downloads und auf dem Schreibtisch"), 22, 40f, -262f, 900f, 36f, UiTheme.TextMuted, bold: true);
            var files = RouteShare.FindFiles();
            if (files.Count == 0) UiControls.Label(p, "Keine gefunden.", 20, 40f, -302f, 800f, 36f, UiTheme.TextMuted);
            float y = -302f;
            for (int i = 0; i < Mathf.Min(files.Count, 6); i++)
            {
                var c = files[i];
                var row = UiTheme.Panel(p, UiTheme.PanelSoft);
                UiControls.Place(row.GetComponent<RectTransform>(), 40f, y, 1240f, 64f);
                if (c.doc == null) // a GPX file that can't be used: say why
                {
                    UiControls.Label(row.transform, $"{System.IO.Path.GetFileName(c.file)} · {c.error}", 20, 20f, -12f, 1180f, 40f, UiTheme.Danger);
                    y -= 74f;
                    continue;
                }
                UiControls.Label(row.transform, $"{c.doc.meta.name} · {Jogging.Core.Units.FmtDist(c.doc.@params.lengthKm * 1000f)} · ↑{Jogging.Core.Units.FmtElev(c.doc.profile.ascentM)} · {c.status}",
                    20, 20f, -12f, 920f, 40f, c.importable ? UiTheme.TextPrimary : UiTheme.TextMuted);
                if (c.importable)
                {
                    var cand = c;
                    UiControls.Button(row.transform, Jogging.Core.Loc.T("Übernehmen"), () => { RouteShare.Import(cand.doc); ShowImport(Jogging.Core.Loc.F("„{0}“ übernommen.", cand.doc.meta.name)); }, 1060f, -8f, 160f, 48f, UiTheme.Success, 20);
                }
                y -= 74f;
            }

            if (note != null) UiControls.Label(p, note, 22, 40f, -760f, 900f, 40f, UiTheme.Success);
            UiControls.Button(p, Jogging.Core.Loc.T("Zurück"), () => { pasted = null; qrSearch = null; ShowList(0); }, 1160f, -792f, 120f, 56f);
        }

        private void Duplicate(RouteDoc d, int pageIndex)
        {
            var c = RouteStore.FromJson(RouteStore.ToJson(d));
            c.id = ""; c.revision = 1; c.meta.created = ""; c.meta.name = d.meta.name + Jogging.Core.Loc.T(" (Kopie)");
            RouteStore.Save(c);
            ShowList(pageIndex);
        }

        // ------------------------------------------------------------------ helpers

        private static int NewSeed() => new System.Random(System.Guid.NewGuid().GetHashCode()).Next(1, 99999);

        private static string Clock(float seconds)
        {
            int s = Mathf.RoundToInt(seconds), h = s / 3600, m = s / 60 % 60;
            return h > 0 ? $"{h}:{m:00}:{s % 60:00}" : $"{m}:{s % 60:00}";
        }

        // Caption + a button that cycles through options.
        private static void Cycle(Transform p, string caption, string[] values, string[] labels, string current,
            System.Action<string> onChange, float x, float y, float w)
        {
            UiControls.Label(p, caption, 20, x, y, 210f, 40f, UiTheme.TextMuted);
            int i = Mathf.Max(0, System.Array.IndexOf(values, current));
            Button b = null;
            b = UiControls.Button(p, "◀  " + Jogging.Core.Loc.T(labels[i]) + "  ▶", () =>
            {
                i = (i + 1) % values.Length;
                b.GetComponentInChildren<Text>().text = "◀  " + Jogging.Core.Loc.T(labels[i]) + "  ▶";
                onChange(values[i]);
            }, x + 215f, y + 2f, w - 215f, 42f, null, 20);
        }

        private static string Word(float v, string a, string b, string c, string d) => v < 0.2f ? a : v < 0.45f ? b : v < 0.75f ? c : d;
    }
}
