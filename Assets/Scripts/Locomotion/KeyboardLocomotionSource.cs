using UnityEngine;

namespace Jogging.Locomotion
{
    /// <summary>
    /// MVP locomotion: speed is driven by keyboard (or a UI slider that sets
    /// <see cref="TargetSpeedMps"/>). Stands in for a treadmill until Stufe 5.
    /// Attach to any GameObject in the scene, or create via `new` and Tick() it yourself.
    /// </summary>
    public class KeyboardLocomotionSource : MonoBehaviour, ILocomotionSource
    {
        [Header("Speed (m/s)")]
        [Tooltip("Speed the runner eases toward. ~2.5 = brisk walk, ~4 = jog, ~6 = run.")]
        [SerializeField] private float targetSpeedMps = 8f / 3.6f; // 8 km/h, like a Quick Run on the belt
        [SerializeField] private float minSpeedMps = 0f;
        [SerializeField] private float maxSpeedMps = 8f;
        [Tooltip("How fast current speed catches up to target (m/s per second).")]
        [SerializeField] private float acceleration = 4f;

        [Header("Keyboard control")]
        [Tooltip("Up/Down arrows (or W/S) change target speed by this much per second.")]
        [SerializeField] private float keyboardStepPerSecond = 2f;

        private float currentSpeed;

        /// <summary>Never faster than a treadmill (F37: 20 km/h), whatever the scene says — keeps test runs realistic.</summary>
        private const float CapMps = 20f / 3.6f;
        private float MaxMps => Mathf.Min(maxSpeedMps, CapMps);

        public float SpeedMps => currentSpeed;
        public float InclinePercent { get; set; } // set by the route/track later
        public bool IsActive => true;

        /// <summary>Public so a UI slider or debug tool can drive the target directly.</summary>
        public float TargetSpeedMps
        {
            get => targetSpeedMps;
            set => targetSpeedMps = Mathf.Clamp(value, minSpeedMps, MaxMps);
        }

        // Tests: -pace <km/h> sets the starting pace (e.g. -pace 8).
        private void Awake()
        {
            var a = Jogging.Core.Args.All;
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-pace" && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float kmh))
                    TargetSpeedMps = kmh / 3.6f;
        }

        public void Tick(float deltaTime)
        {
            // Keyboard nudges the target speed (Input Manager axis "Vertical").
            float axis = Typing() ? 0f : Input.GetAxisRaw("Vertical");
            if (!Mathf.Approximately(axis, 0f))
            {
                TargetSpeedMps += axis * keyboardStepPerSecond * deltaTime;
            }

            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeedMps, acceleration * deltaTime);
        }

        // W/S are letters too: don't change the speed while a text field (e.g. a route name) has focus.
        private static bool Typing()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            return go != null && go.GetComponent<UnityEngine.UI.InputField>() is { isFocused: true };
        }

        // Convenience so a MonoBehaviour-only setup works without a central driver.
        private void Update()
        {
            // Guard: if a central GameDirector already calls Tick(), skip self-ticking
            // to avoid double stepping. Controlled by AutoTick.
            if (AutoTick) Tick(Time.deltaTime);
        }

        [Tooltip("Leave ON for a standalone test scene. Turn OFF if a GameDirector calls Tick().")]
        public bool AutoTick = true;
    }
}
