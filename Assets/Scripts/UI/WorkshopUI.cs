using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Jogging.CameraRig;
using Jogging.Route;
using Jogging.World;

namespace Jogging.UI
{
    /// <summary>
    /// Workshop for a saved route: the run is held, the camera looks down on the trail from above
    /// and behind, and a position slider (or ←/→, A/D) moves along the course. At the current spot
    /// the adjustments ("edits") are placed: denser / sparser forest or a clearing over a chosen
    /// range and side, a spectator group, a lake, a stream across the trail. Tree and spectator edits
    /// show immediately; lakes and streams have to be dug into the terrain, which happens on the next
    /// scene load ("Speichern" reloads the workshop when water changed). Edits are anchored to the arc length, so they survive parameter
    /// changes of the route.
    /// </summary>
    public class WorkshopUI : MonoBehaviour
    {
        /// <summary>Open the workshop on the next scene load (set by the menu before reloading).</summary>
        public static bool OpenOnLoad;
        /// <summary>Position to resume at after a reload (lakes changed).</summary>
        public static float ResumeAt;

        /// <summary>True in a scene load that opens the workshop (valid from Awake on).</summary>
        public static bool Active { get; private set; }

        private void Awake()
        {
            // Tests: "-workshop" (with -routefile/-preset, -starts m) opens the workshop directly.
            var a = Jogging.Core.Args.All;
            if (System.Array.IndexOf(a, "-workshop") >= 0 && Time.frameCount < 5)
            {
                OpenOnLoad = true;
                int i = System.Array.IndexOf(a, "-starts");
                if (i >= 0 && i + 1 < a.Length) float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ResumeAt);
            }
            Active = OpenOnLoad;
        }

        private TrackManager track;
        private RouteDoc doc;
        private float s, range = 200f;
        private string side = "both";
        private Slider posSlider;
        private Text posText, info, countText;
        private bool lakesChanged, dirty;
        private float scrubVel;

        private static readonly string[] Sides = { "both", "left", "right" };
        private static readonly string[] SideLabels = { "beidseitig", "links", "rechts" };

        private void Start()
        {
            if (!OpenOnLoad) { enabled = false; return; }
            OpenOnLoad = false;
            doc = RouteRuntime.Current;
            track = FindFirstObjectByType<TrackManager>();
            if (doc == null || track == null) { enabled = false; return; }

            track.Hold = true;
            // Figures that live relative to the runner would slide along while scrubbing.
            foreach (var n in new[] { "AiRunners", "Spectators", "RaceHud" }) { var g = GameObject.Find(n); if (g != null) g.SetActive(false); }
            var cam = Camera.main != null ? Camera.main.GetComponent<FollowCamera>() : null;
            if (cam != null) { cam.Offset = new Vector3(0f, 16f, -30f); cam.LookHeight = 0f; }

            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Build();
            MoveTo(Mathf.Clamp(ResumeAt, 0f, track.RouteLength > 0 ? track.RouteLength : TrailPath.Active.Length));
            ResumeAt = 0f;
        }

        private float Length => TrailPath.Active != null ? TrailPath.Active.Length : 1000f;

        private void MoveTo(float v)
        {
            s = Mathf.Clamp(v, 0f, Length);
            track.StartAt(s);
            if (posSlider != null) posSlider.SetValueWithoutNotify(s);
            RefreshInfo();
        }

        private void Update()
        {
            // Hold ←/→ (or A/D) to move along the course; accelerates while held.
            bool typing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
                          && EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null;
            float axis = typing ? 0f : Input.GetAxisRaw("Horizontal");
            if (Mathf.Abs(axis) > 0.1f)
            {
                scrubVel = Mathf.Min(scrubVel + Time.unscaledDeltaTime * 120f, 250f);
                MoveTo(s + axis * scrubVel * Time.unscaledDeltaTime);
            }
            else scrubVel = 20f;
        }

        // ------------------------------------------------------------------ UI

        private void Build()
        {
            var canvasGo = new GameObject("Workshop Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 28;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = UiTheme.Panel(canvasGo.transform);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0f, 0f); prt.anchorMax = new Vector2(1f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.anchoredPosition = new Vector2(0f, 20f);
            prt.sizeDelta = new Vector2(-40f, 270f);
            var p = panel.transform;

            UiControls.Label(p, Jogging.Core.Loc.F("Werkstatt: {0}", Jogging.Core.Loc.T(doc.meta.name)), 26, 30f, -14f, 900f, 40f, UiTheme.TextPrimary, bold: true);
            countText = UiControls.Label(p, "", 20, 0f, -14f, -30f, 40f, UiTheme.TextMuted, TextAnchor.MiddleRight);

            // Position
            UiControls.Label(p, "Position", 20, 30f, -62f, 120f, 40f, UiTheme.TextMuted);
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.transform.SetParent(p, false);
            UiControls.Place(go.GetComponent<RectTransform>(), 160f, -74f, 1100f, 18f);
            posSlider = go.GetComponent<Slider>();
            StyleSlider(posSlider);
            posSlider.minValue = 0f; posSlider.maxValue = Length;
            posSlider.onValueChanged.AddListener(MoveTo);
            posText = UiControls.Label(p, "", 22, 1275f, -62f, 230f, 40f, UiTheme.TextPrimary, TextAnchor.MiddleLeft, bold: true);
            UiControls.Button(p, "◀ 100 m", () => MoveTo(s - 100f), 1510f, -60f, 160f, 44f, null, 20);
            UiControls.Button(p, "100 m ▶", () => MoveTo(s + 100f), 1680f, -60f, 160f, 44f, null, 20);

            // Range + side + actions
            UiControls.SliderRow(p, Jogging.Core.Loc.T("Bereich"), 50f, 800f, range, true, v => Jogging.Core.Units.FmtShort(v), v => range = v, 30f, -118f, 640f);
            int si = 0;
            Button sideBtn = null;
            sideBtn = UiControls.Button(p, Jogging.Core.Loc.F("Seite: {0}", Jogging.Core.Loc.T(SideLabels[si])), () =>
            {
                si = (si + 1) % Sides.Length; side = Sides[si];
                sideBtn.GetComponentInChildren<Text>().text = Jogging.Core.Loc.F("Seite: {0}", Jogging.Core.Loc.T(SideLabels[si]));
            }, 690f, -116f, 230f, 44f, null, 20);

            float bx = 940f, bw = 148f;
            UiControls.Button(p, Jogging.Core.Loc.T("Wald dichter"), () => AddTrees(1.8f), bx, -116f, bw, 44f, null, 19); bx += bw + 8f;
            UiControls.Button(p, Jogging.Core.Loc.T("Wald lichter"), () => AddTrees(0.45f), bx, -116f, bw, 44f, null, 19); bx += bw + 8f;
            UiControls.Button(p, Jogging.Core.Loc.T("Lichtung"), () => AddTrees(0.05f), bx, -116f, bw, 44f, null, 19); bx += bw + 8f;
            UiControls.Button(p, "Zuschauer", AddSpectators, bx, -116f, bw, 44f, null, 19); bx += bw + 8f;
            UiControls.Button(p, "See", AddLake, bx, -116f, bw, 44f, null, 19); bx += bw + 8f;
            UiControls.Button(p, "Bach", AddStream, bx, -116f, bw, 44f, null, 19);

            // Edits here + actions
            info = UiControls.Label(p, "", 19, 30f, -176f, 590f, 70f, UiTheme.TextMuted, TextAnchor.UpperLeft);
            info.horizontalOverflow = HorizontalWrapMode.Wrap;
            // wayside objects of your own: pick one, set it 20 m ahead on the chosen side
            Button objBtn = null;
            objBtn = UiControls.Button(p, "◀  " + Jogging.Core.Loc.T(ObjectNames[obj]) + "  ▶", () =>
            {
                obj = (obj + 1) % ObjectKinds.Length;
                objBtn.GetComponentInChildren<Text>().text = "◀  " + Jogging.Core.Loc.T(ObjectNames[obj]) + "  ▶";
            }, 630f, -176f, 250f, 50f, null, 19);
            UiControls.Button(p, "Setzen", AddObject, 888f, -176f, 140f, 50f, null, 20);
            UiControls.Button(p, "Hier entfernen", RemoveHere, 1040f, -176f, 200f, 50f, UiTheme.Danger, 20);
            UiControls.Button(p, "Zurück", Back, 1270f, -176f, 160f, 50f, null, 20);
            UiControls.Button(p, "Speichern", Save, 1440f, -176f, 170f, 50f, UiTheme.Accent, 20);
            UiControls.Button(p, "▶ Speichern & laufen", SaveAndRun, 1620f, -176f, 240f, 50f, UiTheme.Success, 20);
        }

        private static void StyleSlider(Slider sl)
        {
            var bg = sl.transform.Find("Background")?.GetComponent<Image>();
            if (bg != null) { bg.sprite = UiTheme.Rounded(); bg.type = Image.Type.Sliced; bg.color = UiTheme.ButtonBg; }
            if (sl.fillRect != null) { var f = sl.fillRect.GetComponent<Image>(); f.sprite = UiTheme.Rounded(); f.type = Image.Type.Sliced; f.color = UiTheme.Accent; }
            if (sl.handleRect != null) { sl.handleRect.GetComponent<Image>().sprite = UiTheme.Rounded(); sl.handleRect.sizeDelta = new Vector2(24f, 0f); }
        }

        // ------------------------------------------------------------------ edits

        private void AddTrees(float factor)
        {
            doc.edits.Add(new RouteEdit { type = "trees", fromM = Mathf.Max(0f, s - range * 0.5f), toM = Mathf.Min(Length, s + range * 0.5f), side = side, factor = factor });
            Changed();
            TerrainTreeScatter.RebuildAll();
        }

        private void AddSpectators()
        {
            doc.edits.Add(new RouteEdit { type = "spectators", atM = s + 25f, side = side, count = 12 });
            Changed();
            SpectatorGroups.EditsChanged();
        }

        private static readonly string[] ObjectKinds = { "Bench", "Signpost", "HuntingStand", "LogPile", "WaysideCross", "Shelter", "Chapel", "HayBales", "InfoBoard", "FieldBarn" };
        private static readonly string[] ObjectNames = { "Bank", "Wegweiser", "Hochsitz", "Holzpolter", "Wegkreuz", "Schutzhütte", "Kapelle", "Heuballen", "Infotafel", "Feldscheune" };
        private int obj;

        private void AddObject()
        {
            doc.edits.Add(new RouteEdit { type = "object", what = ObjectKinds[obj], atM = Mathf.Clamp(s + 20f, 0f, Length), side = side == "left" ? "left" : "right" });
            Changed();
            Wayside.Replan();
            RaceMessages.Post(Jogging.Core.Loc.F("{0} gesetzt", Jogging.Core.Loc.T(ObjectNames[obj])));
        }

        private void AddLake()
        {
            doc.edits.Add(new RouteEdit { type = "lake", atM = s + 60f, side = side == "left" ? "left" : "right" });
            lakesChanged = true;
            Changed();
            RaceMessages.Post(Jogging.Core.Loc.T("See gesetzt – er wird beim Speichern ins Gelände eingegraben"));
        }

        private void AddStream()
        {
            float at = Mathf.Clamp(s + 30f, 0f, Length);
            if (!Lakes.CanPlaceStream(TrailPath.Active, at))
            {
                RaceMessages.Post(Jogging.Core.Loc.T("Hier passt kein Bach – ein anderer Teil der Strecke ist zu nah"));
                return;
            }
            doc.edits.Add(new RouteEdit { type = "stream", atM = at });
            lakesChanged = true;
            Changed();
            RaceMessages.Post(Jogging.Core.Loc.T("Bach gesetzt – er wird beim Speichern ins Gelände eingegraben"));
        }

        private void RemoveHere()
        {
            bool trees = false, fans = false, objects = false;
            doc.edits.RemoveAll(e =>
            {
                bool hit = Covers(e, s);
                if (hit) { trees |= e.type == "trees"; fans |= e.type == "spectators"; objects |= e.type == "object"; lakesChanged |= e.type == "lake" || e.type == "stream"; }
                return hit;
            });
            Changed();
            if (trees) TerrainTreeScatter.RebuildAll();
            if (fans) SpectatorGroups.EditsChanged();
            if (objects) Wayside.Replan();
        }

        private static bool Covers(RouteEdit e, float at) =>
            e.type == "trees" ? at >= e.fromM - 10f && at <= e.toM + 10f : Mathf.Abs(e.atM - at) < 90f;

        private void Changed() { dirty = true; RefreshInfo(); }

        private void RefreshInfo()
        {
            if (posText != null) posText.text = Jogging.Core.Units.Dist(s).ToString("0.00", Jogging.Core.Loc.Culture) + " / " + Jogging.Core.Units.FmtDist(Length, "0.00");
            if (countText != null) countText.text = (doc.edits.Count == 1 ? Jogging.Core.Loc.T("1 Anpassung") : Jogging.Core.Loc.F("{0} Anpassungen", doc.edits.Count)) + (dirty ? Jogging.Core.Loc.T(" · ungespeichert") : "");
            if (info == null) return;
            var here = new List<string>();
            foreach (var e in doc.edits)
                if (Covers(e, s)) here.Add(Describe(e));
            info.text = here.Count > 0 ? Jogging.Core.Loc.T("Hier: ") + string.Join(" · ", here)
                                       : Jogging.Core.Loc.T("Hier keine Anpassung. Bereich und Seite wählen, dann eine Aktion.  (←/→ bewegt entlang der Strecke)");
        }

        private static string Describe(RouteEdit e)
        {
            string sd = e.side == "left" ? " links" : e.side == "right" ? " rechts" : "";
            switch (e.type)
            {
                case "trees":
                    string what = e.factor < 0.1f ? "Lichtung" : e.factor < 1f ? "Wald lichter" : "Wald dichter";
                    return Jogging.Core.Loc.T(what) + " " + Jogging.Core.Units.Dist(e.fromM).ToString("0.00", Jogging.Core.Loc.Culture) + "–" + Jogging.Core.Units.FmtDist(e.toM, "0.00") + Jogging.Core.Loc.T(sd);
                case "spectators": return Jogging.Core.Loc.F("Zuschauer bei {0}{1}", Jogging.Core.Units.FmtDist(e.atM, "0.00"), Jogging.Core.Loc.T(sd));
                case "lake": return Jogging.Core.Loc.F("See bei {0}{1}", Jogging.Core.Units.FmtDist(e.atM, "0.00"), Jogging.Core.Loc.T(sd));
                case "stream": return Jogging.Core.Loc.F("Bach bei {0}", Jogging.Core.Units.FmtDist(e.atM, "0.00"));
                case "object":
                    int oi = System.Array.IndexOf(ObjectKinds, e.what);
                    return Jogging.Core.Loc.F("{0} bei {1}{2}", Jogging.Core.Loc.T(oi >= 0 ? ObjectNames[oi] : e.what), Jogging.Core.Units.FmtDist(e.atM, "0.00"), Jogging.Core.Loc.T(sd));
            }
            return e.type;
        }

        private void Save()
        {
            RouteStore.Save(doc);
            dirty = false;
            Debug.Log($"[Jogging] Werkstatt gespeichert: {doc.meta.name}, {doc.edits.Count} Anpassungen");
            if (lakesChanged)
            {
                // The terrain has to be shaped again for the lake: reload the workshop at this spot.
                lakesChanged = false;
                Reload(doc, workshop: true);
                return;
            }
            RefreshInfo();
        }

        private void SaveAndRun()
        {
            RouteStore.Save(doc);
            Reload(doc, workshop: false);
        }

        private void Back()
        {
            // Discard unsaved changes: back to the menu with the saved version.
            var saved = (RouteStore.IsValidId(doc.id) ? RouteStore.Load(RouteStore.PathFor(doc)) : null) ?? doc; // (unsaved route: no file)
            RouteRuntime.Selected = saved;
            Time.timeScale = 1f;
            Jogging.World.SceneReload.Now(); // frees the old landscape first
        }

        private void Reload(RouteDoc d, bool workshop)
        {
            RouteRuntime.Selected = d;
            OpenOnLoad = workshop;
            ResumeAt = workshop ? s : 0f;
            StartMenuUI.SkipNextOpen();
            Time.timeScale = 1f;
            Jogging.World.SceneReload.Now(); // frees the old landscape first
        }
    }
}
