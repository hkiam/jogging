namespace Jogging.Training
{
    /// <summary>
    /// The workout of the current run across the scene reload that starts it (like RouteRuntime):
    /// the menu sets <see cref="Selected"/>, the run's workout HUD starts it and publishes
    /// <see cref="Current"/> / <see cref="Runner"/> for the logbook and the finish screen.
    /// </summary>
    public static class WorkoutRuntime
    {
        /// <summary>Workout for the next run (null = free run).</summary>
        public static WorkoutDoc Selected;

        /// <summary>
        /// Set by the menu when the run really begins in this scene (reset on every scene start):
        /// only then is <see cref="Selected"/> taken — not in the frame between "start" and the
        /// reload onto another route.
        /// </summary>
        public static bool Armed;

        /// <summary>Workout of the running scene, null without one.</summary>
        public static WorkoutDoc Current { get; private set; }
        public static WorkoutRunner Runner { get; private set; }

        public static bool Completed => Runner != null && Runner.Done;

        public static void Begin(WorkoutDoc doc) { Current = doc; Runner = doc != null ? new WorkoutRunner(doc) : null; }

        public static void Clear() { Current = null; Runner = null; }
    }
}
