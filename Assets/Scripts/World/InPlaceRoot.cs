using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// Keeps a Rocketbox figure running on the spot: its run clips move the Bip01 root forward
    /// ~2–3.5 m per cycle and snap back on loop (visible as jumping back). After the Animator has
    /// written the pose, reset the root's forward offset; the game moves the figure itself.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class InPlaceRoot : MonoBehaviour
    {
        [SerializeField] private Transform root; // Bip01

        public void Init(Transform bip01) => root = bip01;

        private void LateUpdate()
        {
            if (root == null) return;
            var p = root.localPosition;
            p.z = 0f;
            root.localPosition = p;
        }
    }
}
