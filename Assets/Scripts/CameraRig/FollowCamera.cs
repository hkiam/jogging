using UnityEngine;

namespace Jogging.CameraRig
{
    /// <summary>
    /// Lightweight chase camera positioned behind and above the runner, matching the
    /// reference screenshot's over-the-shoulder view. This is a dependency-free fallback;
    /// for production, prefer Cinemachine (a virtual camera with a Transposer + a little
    /// damping gives the same result with less code). Kept here so the MVP scene runs with
    /// zero extra packages.
    /// </summary>
    public class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 2.6f, -4.5f);
        [SerializeField] private float lookHeight = 1.4f;
        [Tooltip("Higher = snappier follow. 0 = locked to target.")]
        [SerializeField] private float positionDamping = 6f;

        /// <summary>Offset from the target in its local frame (e.g. higher/further back in the workshop).</summary>
        public Vector3 Offset { get => offset; set => offset = value; }
        public float LookHeight { get => lookHeight; set => lookHeight = value; }

        private void LateUpdate()
        {
            if (target == null) return;

            Vector3 desired = target.position + target.TransformVector(offset);
            transform.position = positionDamping <= 0f
                ? desired
                : Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-positionDamping * Time.unscaledDeltaTime)); // also behind the (time-frozen) menu

            transform.LookAt(target.position + Vector3.up * lookHeight);
        }
    }
}
