using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Jogging.Locomotion.Treadmill;
using Jogging.Training;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Self-check of the treadmill and sensor decoders: FTMS Treadmill Data with every optional field
    /// up to the heart rate (hand grips), FitShow with and without the grip pulse, KingSmith WalkingPad
    /// status frames, foot pods (RSC), iConsole+ frames (also in pieces), LifeSpan answers, the "no skin
    /// contact" flag of a strap.
    /// Menu: Jogging → Lauf → Laufband-Protokolle prüfen.
    /// </summary>
    public static class TreadmillProtocolsCheck
    {
        [MenuItem("Jogging/Lauf/Laufband-Protokolle prüfen")]
        public static void RunMenu() => EditorUtility.DisplayDialog("Jogging", Run() ? "Alle Prüfungen bestanden." : "Fehler – siehe Console.", "OK");

        public static bool Run()
        {
            var fails = new List<string>();
            void Ok(bool c, string what) { if (!c) fails.Add(what); }

            // FTMS: flags speed(0) + distance(2) + incline(3) + elevation(4) + energy(7) + heart rate(8)
            ushort flags = (1 << 2) | (1 << 3) | (1 << 4) | (1 << 7) | (1 << 8);
            var ftms = new List<byte> { (byte)(flags & 0xFF), (byte)(flags >> 8) };
            ftms.AddRange(new byte[] { 0xE8, 0x03 });             // 10.00 km/h
            ftms.AddRange(new byte[] { 0x10, 0x27, 0x00 });       // 10000 m
            ftms.AddRange(new byte[] { 0x32, 0x00, 0x00, 0x00 }); // 5.0 %, ramp angle
            ftms.AddRange(new byte[] { 0x05, 0x00, 0x00, 0x00 }); // elevation gain +/−
            ftms.AddRange(new byte[] { 0x64, 0x00, 0x00, 0x02, 0x08 }); // energy
            ftms.Add(142);                                         // heart rate
            Ok(FtmsTreadmillParser.TryParse(ftms.ToArray(), out var f) && Mathf.Abs(f.SpeedKmh - 10f) < 0.01f
               && f.HasIncline && Mathf.Abs(f.InclinePercent - 5f) < 0.01f && f.TotalDistanceMeters == 10000 && f.HeartRateBpm == 142,
               $"FTMS mit Puls: {f.SpeedKmh} km/h, {f.InclinePercent} %, {f.TotalDistanceMeters} m, Puls {f.HeartRateBpm}");
            Ok(FtmsTreadmillParser.TryParse(new byte[] { 0x00, 0x00, 0xE8, 0x03 }, out var f2) && f2.HeartRateBpm == 0, "FTMS ohne Puls");

            // FitShow: the F37's own frame (no hands on the grips) and one with a pulse at byte 13
            var fs = new byte[] { 0x02, 0x51, 0x03, 0x28, 0x02, 0x42, 0x00, 0x3B, 0x00, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x16, 0x03 };
            Ok(FitShowParser.TryParse(fs, out var s1, out _) && s1.HeartRateBpm == 0 && Mathf.Abs(s1.SpeedKmh - 4f) < 0.01f, "FitShow F37 ohne Hand");
            var fsHr = (byte[])fs.Clone(); fsHr[13] = 128; Checksum(fsHr);
            Ok(FitShowParser.TryParse(fsHr, out var s2, out _) && s2.HeartRateBpm == 128, $"FitShow Handpuls: {s2.HeartRateBpm}");

            // WalkingPad: running at 3.5 km/h, 125 s, 1.23 km (123 × 10 m), 1500 steps
            var wp = new byte[] { 0xF8, 0xA2, 0x01, 0x23, 0x01, 0x00, 0x00, 0x7D, 0x00, 0x00, 0x7B, 0x00, 0x05, 0xDC, 0x69, 0x00, 0x00, 0x00, 0xFD };
            WpChecksum(wp);
            Ok(TreadmillFrames.TryDecode("WalkingPad", wp, out var w, out var ws) && ws == BeltState.Running
               && Mathf.Abs(w.SpeedKmh - 3.5f) < 0.01f && w.TotalDistanceMeters == 1230, $"WalkingPad: {w.SpeedKmh} km/h, {w.TotalDistanceMeters} m, {ws}");
            var wpBad = (byte[])wp.Clone(); wpBad[3] = 0x30;
            Ok(!WalkingPadParser.IsFrame(wpBad), "WalkingPad mit falscher Prüfsumme akzeptiert");
            Ok(!FitShowParser.IsFitShowFrame(wp) && !WalkingPadParser.IsFrame(fs), "FitShow und WalkingPad verwechselt");

            // Foot pod: 3.0 m/s (768/256), cadence 170, total distance 5000.0 m
            var rsc = new byte[] { 0x02, 0x00, 0x03, 170, 0x50, 0xC3, 0x00, 0x00 };
            Ok(TreadmillFrames.TryDecode("RSC", rsc, out var r, out var rs) && Mathf.Abs(r.SpeedKmh - 10.8f) < 0.01f
               && r.HasDistance && Mathf.Abs(r.TotalDistanceMeters - 5000f) < 0.1f && rs == BeltState.Running,
               $"Laufsensor: {r.SpeedKmh} km/h, {r.TotalDistanceMeters} m");

            // iConsole: 10.5 km/h, 4 %, 1.23 km, pulse 135 — every byte stored +1, split into two notifications
            var ic = new byte[19];
            ic[0] = 0xF0; ic[1] = 0xB2; ic[2] = 0x01; ic[3] = 0x01;
            ic[4] = 30 + 1; ic[5] = 12 + 1; ic[6] = 1 + 1; ic[7] = 23 + 1; ic[8] = 1; ic[9] = 1;
            ic[10] = 1 + 1; ic[11] = 35 + 1; ic[12] = 1 + 1; ic[13] = 5 + 1; ic[14] = 4 + 1; ic[15] = 1; ic[16] = 1; ic[17] = 1;
            int sum = 0; for (int i = 0; i < 18; i++) sum += ic[i]; ic[18] = (byte)(sum & 0xFF);
            var asm = new IConsoleAssembler();
            var got = new List<byte[]>();
            got.AddRange(asm.Add(new byte[] { 0x00, 0x13 }));                       // noise before a frame
            got.AddRange(asm.Add(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Take(ic, 8))));
            got.AddRange(asm.Add(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Skip(ic, 8))));
            TreadmillData icd = default; BeltState ics = BeltState.Unknown;
            Ok(got.Count == 1 && TreadmillFrames.TryDecode("iConsole", got[0], out icd, out ics) && Mathf.Abs(icd.SpeedKmh - 10.5f) < 0.01f
               && Mathf.Abs(icd.InclinePercent - 4f) < 0.01f && icd.TotalDistanceMeters == 1230 && icd.HeartRateBpm == 135 && ics == BeltState.Running,
               $"iConsole: {got.Count} Rahmen, {icd.SpeedKmh} km/h, {icd.InclinePercent} %, {icd.TotalDistanceMeters} m, Puls {icd.HeartRateBpm}");

            // LifeSpan: state running, then speed 2.50 mph (op in front, as the plugin sends it)
            Ok(TreadmillFrames.TryDecode("LifeSpan", new byte[] { 0x91, 0xA1, 0xAA, 0x03, 0x00, 0x00, 0x00 }, out _, out var lss) && lss == BeltState.Running, "LifeSpan Zustand");
            Ok(TreadmillFrames.TryDecode("LifeSpan", new byte[] { 0x82, 0xA1, 0xAA, 0x02, 50, 0x00, 0x00 }, out var lsd, out _) && Mathf.Abs(lsd.SpeedKmh - 4.02f) < 0.01f,
               $"LifeSpan Tempo: {lsd.SpeedKmh} km/h");
            Ok(!TreadmillFrames.TryDecode("LifeSpan", new byte[] { 0x82, 0xA1, 0xFF, 0x00, 0x00, 0x00, 0x00 }, out _, out _), "LifeSpan: unbekannte Anfrage akzeptiert");

            // Strap without skin contact
            Ok(HeartRateParser.NoContact(new byte[] { 0x04, 90 }) && !HeartRateParser.NoContact(new byte[] { 0x06, 90 }), "kein Hautkontakt");

            foreach (var x in fails) Debug.LogError("[Jogging] Laufband-Protokolle: " + x);
            if (fails.Count == 0) Debug.Log("[Jogging] Laufband-Protokolle: alles in Ordnung");
            return fails.Count == 0;
        }

        private static void Checksum(byte[] d) { byte x = 0; for (int i = 1; i < d.Length - 2; i++) x ^= d[i]; d[d.Length - 2] = x; }
        private static void WpChecksum(byte[] d) { int s = 0; for (int i = 1; i < d.Length - 2; i++) s += d[i]; d[d.Length - 2] = (byte)(s & 0xFF); }
    }
}
