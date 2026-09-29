using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Den.Tools;
using Den.Tools.Matrices;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Self-check: MapMagic's erosion without allocations (Den.Tools.Erosion) gives bit-identical
    /// heights to the original managed code (…Ref) — same terrain for every route. Also reports the
    /// speed-up. Part of Tools/check.sh.
    /// </summary>
    public static class ErosionCheck
    {
        public static void RunBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        // the allocation-free erosion is one of the app's changes inside MapMagic (docs/Setup.md); without it
        // there is nothing to compare
        private static readonly System.Reflection.FieldInfo useRefField = typeof(Erosion).GetField("UseReference", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        private static readonly System.Reflection.PropertyInfo useRefProp = typeof(Erosion).GetProperty("UseReference", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        private static void UseReference(bool on) { if (useRefField != null) useRefField.SetValue(null, on); else useRefProp?.SetValue(null, on); }

        public static bool Run()
        {
            if (useRefField == null && useRefProp == null) { Debug.Log("[ErosionCheck] übersprungen – MapMagic ohne die Anpassungen der App"); return true; }
            var fails = new List<string>();
            foreach (int size in new[] { 65, 161 })
                for (int seed = 1; seed <= 3; seed++)
                {
                    var a = Terrain(size, seed);
                    var b = new Matrix(a);
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    RunErosion(a, reference: true);
                    long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                    RunErosion(b, reference: false);
                    long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
                    int diff = 0;
                    for (int i = 0; i < a.count; i++)
                        if (System.BitConverter.SingleToInt32Bits(a.arr[i]) != System.BitConverter.SingleToInt32Bits(b.arr[i])) diff++;
                    if (diff > 0) fails.Add($"{size}² Seed {seed}: {diff} Punkte weichen ab");
                    if (seed == 1)
                        Debug.Log($"[ErosionCheck] {size}²: Original {(t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0} ms, neu {(t2 - t1) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0} ms");
                }
            UseReference(false);
            foreach (var f in fails) Debug.LogError("[ErosionCheck] FAIL " + f);
            return fails.Count == 0;
        }

        // The loop of MapMagic's Erosion200 (3 iterations, fluidity 3), with either implementation.
        private static void RunErosion(Matrix h, bool reference)
        {
            UseReference(reference);
            var order = new Matrix2D<int>(h.rect);
            var torrents = new Matrix(h.rect);
            var mudflow = new Matrix(h.rect);
            var sediment = new Matrix(h.rect);
            for (int i = 0; i < 3; i++)
            {
                Erosion.SetOrder(h, order);
                Erosion.MaskBorders(order);
                Erosion.CreateTorrents(h, order, torrents);
                Erosion.Erode(h, torrents, mudflow, order, 0.9f, 1f, 0.75f);
                Erosion.TransferSettleMudflow(h, mudflow, sediment, order, 3);
            }
        }

        // Hills with some noise, heights 0…1 like MapMagic's matrices.
        private static Matrix Terrain(int size, int seed)
        {
            var m = new Matrix(new CoordRect(0, 0, size, size));
            var rng = new System.Random(seed);
            float fx = 0.05f + (float)rng.NextDouble() * 0.1f, fz = 0.04f + (float)rng.NextDouble() * 0.1f;
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                    m.arr[z * size + x] = 0.5f + 0.25f * Mathf.Sin(x * fx) * Mathf.Cos(z * fz)
                                        + 0.1f * Mathf.PerlinNoise(x * 0.13f + seed, z * 0.11f) + 0.01f * (float)rng.NextDouble();
            return m;
        }
    }
}
