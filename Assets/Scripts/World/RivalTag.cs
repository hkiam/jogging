using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// Understated rival marker for the realistic world: a small "RIVALE" label above the head
    /// that always faces the camera and fades out when the rival is right next to you.
    /// </summary>
    public class RivalTag : MonoBehaviour
    {
        private TextMesh text, shadow;
        private Color baseColor = new Color(1f, 0.82f, 0.25f);

        public static RivalTag Create(Transform figureRoot, float height = 2.25f)
        {
            var go = new GameObject("RivalTag");
            go.transform.SetParent(figureRoot, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            var tag = go.AddComponent<RivalTag>();
            tag.shadow = MakeText(go.transform, new Vector3(0.012f, -0.012f, 0.01f), new Color(0f, 0f, 0f, 0.6f));
            tag.text = MakeText(go.transform, Vector3.zero, tag.baseColor);
            return tag;
        }

        /// <summary>Other label and colour (e.g. the ghost runner); it may stay visible up close.</summary>
        public RivalTag SetText(string label, Color color, bool fadeNear = true)
        {
            text.text = shadow.text = label;
            baseColor = color;
            this.fadeNear = fadeNear;
            return this;
        }

        private bool fadeNear = true;

        private static TextMesh MakeText(Transform parent, Vector3 offset, Color c)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            var t = go.AddComponent<TextMesh>();
            t.text = Jogging.Core.Loc.T("▼ RIVALE");
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.GetComponent<MeshRenderer>().sharedMaterial = t.font.material;
            t.fontSize = 64;
            t.characterSize = 0.035f;
            t.anchor = TextAnchor.LowerCenter;
            t.alignment = TextAlignment.Center;
            t.fontStyle = FontStyle.Bold;
            t.color = c;
            return t;
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);
            // Fade when close (you can see who it is) — visible from ~6 m on.
            float d = Vector3.Distance(cam.transform.position, transform.position);
            float a = fadeNear ? Mathf.Clamp01((d - 5f) / 4f) : 0.85f;
            text.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
            shadow.color = new Color(0f, 0f, 0f, 0.6f * a);
        }
    }
}
