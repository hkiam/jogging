using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// Keeps the runner standing on real terrain (real-terrain mode of <see cref="TrackManager"/>):
    /// each frame the runner's height is set to the ground under it plus the capsule offset.
    /// Snaps the first time terrain appears, then follows smoothly.
    /// </summary>
    public class TerrainFollower : MonoBehaviour
    {
        [Tooltip("Runner pivot height above the ground (capsule centre = 1 m).")]
        [SerializeField] private float heightOffset = 1f;
        [SerializeField] private float smoothing = 14f;

        public bool OnGround { get; private set; }

        private void LateUpdate()
        {
            Vector3 p = transform.position;
            if (!TerrainGround.TryHeight(p.x, p.z, out float h)) return;
            float y = h + heightOffset;
            p.y = OnGround ? Mathf.Lerp(p.y, y, 1f - Mathf.Exp(-smoothing * Time.deltaTime)) : y;
            OnGround = true;
            transform.position = p;
        }
    }
}
