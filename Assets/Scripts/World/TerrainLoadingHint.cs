using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// Covers the screen with "Landschaft wird erzeugt…" until the ground (and trail bed) under the
    /// runner is final (MapMagic builds the tiles on start), so the wait looks intentional instead
    /// of showing coarse preview terrain.
    /// </summary>
    public class TerrainLoadingHint : MonoBehaviour
    {
        [SerializeField] private Transform runner;
        private bool ready;
        private GUIStyle style;

        private void Awake() => Den.Tools.TileDiag.PrewarmAllowed = false; // a new scene: the start comes first

        private void Update()
        {
            if (!ready && runner != null && TerrainGround.TryHeight(runner.position.x, runner.position.z, out _)
                && (TrailPath.Active == null || TrailShaper.Ready(TrailPath.Active.RunnerS - 5f, TrailPath.Active.RunnerS + 60f)))
            {
                ready = true;
                Den.Tools.TileDiag.PrewarmAllowed = true; // now MapMagic may build spare tiles in quiet frames
                Debug.Log($"[Jogging] Gelände bereit nach {Time.realtimeSinceStartup:0.0} s");
            }
        }

        private void OnGUI()
        {
            if (ready || Jogging.UI.StartMenuUI.IsOpen) return; // the menu has its own backdrop
            if (style == null)
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 26, fontStyle = FontStyle.Bold };
            // Cover the world while it's being built (coarse preview terrain, trail not bedded yet).
            var old = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.10f, 1f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
            string dots = new string('.', 1 + (int)(Time.realtimeSinceStartup * 2f) % 3);
            var r = new Rect(0, Screen.height * 0.5f - 20, Screen.width, 40);
            style.normal.textColor = new Color(0, 0, 0, 0.6f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), "Landschaft wird erzeugt" + dots, style);
            style.normal.textColor = Color.white;
            GUI.Label(r, "Landschaft wird erzeugt" + dots, style);
        }
    }
}
