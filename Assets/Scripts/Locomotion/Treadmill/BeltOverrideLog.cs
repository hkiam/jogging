using System;
using UnityEngine;

namespace Jogging.Locomotion.Treadmill
{
    /// <summary>
    /// Logs when the safety layer notices a manual change at the belt (the app backs off) and when it
    /// takes over again — with the wall-clock time, so the back-off can be checked against the belt
    /// commands in the log (those carry the time too, see <see cref="Stamp"/>).
    /// </summary>
    public class BeltOverrideLog
    {
        private bool incline, speed;
        private float inclineSince, speedSince;

        public static string Stamp => DateTime.Now.ToString("HH:mm:ss");

        /// <summary>Call after every <see cref="BeltSafety.OnReading"/>.</summary>
        public void After(BeltSafety s, BeltReading r, float now)
        {
            bool i = s.InclineOverridden(now), v = s.SpeedOverridden(now);
            if (i && !incline)
            {
                inclineSince = now;
                Debug.Log($"[Jogging] {Stamp} Band: Steigung von Hand verstellt ({r.InclinePercent:0} %) – App hält sich {s.Limits.OverridePause:0} s zurück");
            }
            else if (!i && incline)
                Debug.Log($"[Jogging] {Stamp} Band: Zurückhalten vorbei nach {now - inclineSince:0} s – Steigung wieder von der App ({r.InclinePercent:0} %)");
            if (v && !speed)
            {
                speedSince = now;
                Debug.Log($"[Jogging] {Stamp} Band: Tempo von Hand verstellt ({r.SpeedKmh:0.0} km/h) – App hält sich {s.Limits.OverridePause:0} s zurück");
            }
            else if (!v && speed)
                Debug.Log($"[Jogging] {Stamp} Band: Zurückhalten vorbei nach {now - speedSince:0} s – Tempo wieder von der App");
            incline = i; speed = v;
        }
    }
}
