using System;

namespace Jogging.Training
{
    /// <summary>
    /// Plays a <see cref="WorkoutDoc"/>: advanced with running time and distance (only while the run
    /// runs), it knows the current and the next segment, what is left of it, and when the workout is
    /// done. Plain C#, tested by WorkoutCheck.
    /// </summary>
    public class WorkoutRunner
    {
        public WorkoutDoc Doc { get; }
        public int Index { get; private set; }
        public bool Done { get; private set; }
        public float SegmentSeconds { get; private set; }
        public float SegmentMeters { get; private set; }
        public float TotalSeconds { get; private set; }

        /// <summary>A segment begins (also the first one, on <see cref="Start"/>).</summary>
        public event Action<WorkoutSegment> SegmentStarted;
        public event Action Completed;

        public WorkoutRunner(WorkoutDoc doc) => Doc = doc;

        public int Count => Doc.segments.Count;
        public WorkoutSegment Current => Done ? null : Doc.segments[Index];
        public WorkoutSegment Next => !Done && Index + 1 < Count ? Doc.segments[Index + 1] : null;

        /// <summary>Seconds (time segment) or metres (distance segment) left in the current segment.</summary>
        public float Remaining
        {
            get
            {
                var s = Current;
                if (s == null) return 0f;
                return s.ByDistance ? Math.Max(0f, s.distanceM - SegmentMeters) : Math.Max(0f, s.durationS - SegmentSeconds);
            }
        }

        /// <summary>0…1 through the current segment.</summary>
        public float SegmentProgress
        {
            get
            {
                var s = Current;
                if (s == null) return 1f;
                float p = s.ByDistance ? SegmentMeters / s.distanceM : SegmentSeconds / s.durationS;
                return p < 0f ? 0f : p > 1f ? 1f : p;
            }
        }

        public void Start()
        {
            if (Count == 0) { Done = true; Completed?.Invoke(); return; }
            SegmentStarted?.Invoke(Current);
        }

        /// <summary>Running time and distance since the last call (call only while the run runs).</summary>
        public void Advance(float seconds, float meters)
        {
            if (Done) return;
            TotalSeconds += seconds;
            SegmentSeconds += seconds;
            SegmentMeters += meters;
            // A segment ends; the overshoot carries over into the next one.
            while (!Done)
            {
                var s = Current;
                float over;
                if (s.ByDistance) { if (SegmentMeters < s.distanceM) break; over = SegmentMeters - s.distanceM; SegmentMeters = over; SegmentSeconds = 0f; }
                else { if (SegmentSeconds < s.durationS) break; over = SegmentSeconds - s.durationS; SegmentSeconds = over; SegmentMeters = 0f; }
                if (Index + 1 >= Count) { Done = true; Completed?.Invoke(); break; }
                Index++;
                SegmentStarted?.Invoke(Current);
            }
        }
    }
}
