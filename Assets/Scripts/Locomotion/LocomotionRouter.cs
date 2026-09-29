using UnityEngine;

namespace Jogging.Locomotion
{
    /// <summary>
    /// Routes the active locomotion to the rest of the game: the treadmill when it is connected
    /// and sending data, otherwise the keyboard. The game references this single
    /// <see cref="ILocomotionSource"/>, so switching input needs no changes elsewhere.
    /// </summary>
    public class LocomotionRouter : MonoBehaviour, ILocomotionSource
    {
        [Tooltip("Fallback input (KeyboardLocomotionSource).")]
        [SerializeField] private MonoBehaviour keyboardBehaviour;
        [Tooltip("Treadmill input (TreadmillLocomotionSource). Used when active.")]
        [SerializeField] private MonoBehaviour treadmillBehaviour;

        private ILocomotionSource keyboard;
        private ILocomotionSource treadmill;

        private ILocomotionSource Active =>
            (treadmill != null && treadmill.IsActive) ? treadmill : keyboard;

        /// <summary>True when the treadmill is currently driving movement.</summary>
        public bool UsingTreadmill => treadmill != null && treadmill.IsActive;

        public float SpeedMps => Active?.SpeedMps ?? 0f;
        public float InclinePercent => Active?.InclinePercent ?? 0f;
        public bool IsActive => true;

        private void Awake()
        {
            keyboard = keyboardBehaviour as ILocomotionSource;
            treadmill = treadmillBehaviour as ILocomotionSource;
        }

        public void Tick(float deltaTime)
        {
            keyboard?.Tick(deltaTime);
            treadmill?.Tick(deltaTime);
        }
    }
}
