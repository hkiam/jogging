using UnityEngine;
using Jogging.Locomotion;
using Jogging.Profile;

namespace Jogging.World
{
    /// <summary>
    /// Player figure as a realistic Rocketbox avatar. The runner transform sits 1 m above the
    /// ground (capsule centre), so the figure's feet go 1 m below it; the run blend follows the
    /// player's speed. Which figure is used comes from the profile (chosen in the customization
    /// panel) and can be swapped at runtime.
    /// </summary>
    public class RealPlayerFigure : MonoBehaviour
    {
        [SerializeField] private GameObject model;              // default figure
        [SerializeField] private GameObject[] choices = new GameObject[0];
        [SerializeField] private RuntimeAnimatorController controller;        // male run blend
        [SerializeField] private RuntimeAnimatorController femaleController;  // female run blend
        [SerializeField] private MonoBehaviour locomotionSourceBehaviour; // ILocomotionSource
        [SerializeField] private float footOffset = -1f;

        private Animator anim;
        private ILocomotionSource loco;
        private TrackManager track;
        private static readonly int SpeedId = Animator.StringToHash("Speed");

        /// <summary>Figures the player can choose from (for the customization panel).</summary>
        public GameObject[] Choices => choices;
        public RuntimeAnimatorController MaleController => controller;
        public RuntimeAnimatorController FemaleController => femaleController != null ? femaleController : controller;

        /// <summary>The model asset of the current figure (for the ghost runner).</summary>
        public GameObject CurrentModel
        {
            get
            {
                string n = CurrentName;
                foreach (var c in choices) if (c != null && c.name == n) return c;
                return model;
            }
        }
        /// <summary>The figure the active runner has chosen (also before it is shown).</summary>
        public string ChosenName
        {
            get
            {
                var ps = ProfileService.Instance;
                string n = ps != null ? ps.Profile.figureModel : null;
                if (!string.IsNullOrEmpty(n)) foreach (var c in choices) if (c != null && c.name == n) return n;
                return model != null ? model.name : "";
            }
        }
        public string CurrentName => anim != null ? anim.gameObject.name : (model != null ? model.name : "");

        private void Start()
        {
            loco = locomotionSourceBehaviour as ILocomotionSource;
            track = FindFirstObjectByType<TrackManager>();
            var ps = ProfileService.Instance;
            Show(ps != null ? ps.Profile.figureModel : null);
            if (ps != null) ps.FigureChanged += Show;
        }

        private void OnDestroy()
        {
            if (ProfileService.Instance != null) ProfileService.Instance.FigureChanged -= Show;
        }

        /// <summary>Show the figure with this model name (unknown/empty → default).</summary>
        public void Show(string modelName)
        {
            GameObject m = model;
            if (!string.IsNullOrEmpty(modelName))
                foreach (var c in choices) if (c != null && c.name == modelName) { m = c; break; }
            if (m == null) return;
            if (anim != null) { if (anim.gameObject.name == m.name) return; Destroy(anim.gameObject); }
            anim = RealFigure.Spawn(transform, m, controller, femaleController != null ? femaleController : controller);
            anim.transform.localPosition = new Vector3(0f, footOffset, 0f);
            anim.transform.localRotation = Quaternion.identity;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        private void Update()
        {
            // Real progress along the trail (standing while the run waits for the terrain).
            float v = track != null ? track.SpeedMps : (loco != null ? loco.SpeedMps : 0f);
            RealFigure.Drive(anim, v); // walks, runs — and walks slowly at 1 km/h instead of gliding
        }
    }
}
