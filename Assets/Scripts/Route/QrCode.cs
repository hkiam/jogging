using System;
using System.Collections.Generic;
using System.Text;

namespace Jogging.Route
{
    /// <summary>
    /// Minimal QR code encoder (byte mode, versions 1–40, error correction L or M), after the
    /// structure of Project Nayuki's reference implementation (MIT). Plain C#, verified by decoding
    /// the output with the macOS barcode detector (Tools/qrcheck.swift, BackupCheck).
    /// </summary>
    public sealed class QrCode
    {
        public enum Ecc { Low = 0, Medium = 1 }
        private static readonly int[] FormatBits = { 1, 0 }; // L, M

        public readonly int Version, Size;
        private readonly bool[,] modules;     // [y, x] true = dark
        private readonly bool[,] isFunction;
        private readonly Ecc ecl;

        public bool this[int x, int y] => x >= 0 && y >= 0 && x < Size && y < Size && modules[y, x];

        // ---- tables (index 0 unused) ----
        private static readonly int[,] EccPerBlock =
        {
            { -1,  7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
            { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 },
        };
        private static readonly int[,] NumBlocks =
        {
            { -1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4,  4,  4,  4,  4,  6,  6,  6,  6,  7,  8,  8,  9,  9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 },
            { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5,  5,  8,  9,  9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 },
        };

        public static QrCode EncodeText(string text, Ecc ecl = Ecc.Low) => EncodeBytes(Encoding.UTF8.GetBytes(text ?? ""), ecl);

        public static QrCode EncodeBytes(byte[] data, Ecc ecl = Ecc.Low)
        {
            int version = 1, dataBits = 0;
            for (; ; version++)
            {
                if (version > 40) throw new ArgumentException("Zu viele Daten für einen QR-Code");
                int cc = version < 10 ? 8 : 16;
                dataBits = 4 + cc + data.Length * 8;
                if (data.Length < (1 << cc) && dataBits <= NumDataCodewords(version, ecl) * 8) break;
            }
            var bits = new List<bool>();
            void Append(int val, int len) { for (int i = len - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0); }
            Append(0x4, 4);
            Append(data.Length, version < 10 ? 8 : 16);
            foreach (var b in data) Append(b, 8);
            int capacity = NumDataCodewords(version, ecl) * 8;
            Append(0, Math.Min(4, capacity - bits.Count));
            Append(0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) Append(pad, 8);
            var codewords = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));
            return new QrCode(version, ecl, codewords);
        }

        private QrCode(int version, Ecc ecl, byte[] dataCodewords)
        {
            Version = version; this.ecl = ecl; Size = version * 4 + 17;
            modules = new bool[Size, Size]; isFunction = new bool[Size, Size];
            DrawFunctionPatterns();
            DrawCodewords(AddEccAndInterleave(dataCodewords));
            int best = 0; long bestPenalty = long.MaxValue;
            for (int m = 0; m < 8; m++)
            {
                ApplyMask(m); DrawFormatBits(m);
                long p = Penalty();
                if (p < bestPenalty) { bestPenalty = p; best = m; }
                ApplyMask(m); // undo (XOR)
            }
            ApplyMask(best); DrawFormatBits(best);
        }

        // ---- function patterns ----
        private void SetF(int x, int y, bool dark) { modules[y, x] = dark; isFunction[y, x] = true; }

        private void DrawFunctionPatterns()
        {
            for (int i = 0; i < Size; i++) { SetF(6, i, i % 2 == 0); SetF(i, 6, i % 2 == 0); }
            DrawFinder(3, 3); DrawFinder(Size - 4, 3); DrawFinder(3, Size - 4);
            var pos = AlignmentPositions();
            int n = pos.Length;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (!(i == 0 && j == 0 || i == 0 && j == n - 1 || i == n - 1 && j == 0)) DrawAlignment(pos[i], pos[j]);
            DrawFormatBits(0);
            DrawVersion();
        }

        private void DrawFinder(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int d = Math.Max(Math.Abs(dx), Math.Abs(dy)), xx = x + dx, yy = y + dy;
                    if (xx >= 0 && xx < Size && yy >= 0 && yy < Size) SetF(xx, yy, d != 2 && d != 4);
                }
        }

        private void DrawAlignment(int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++) SetF(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }

        private int[] AlignmentPositions()
        {
            if (Version == 1) return new int[0];
            int n = Version / 7 + 2;
            int step = (Version * 8 + n * 3 + 5) / (n * 4 - 4) * 2;
            var r = new int[n];
            r[0] = 6;
            for (int i = n - 1, p = Size - 7; i >= 1; i--, p -= step) r[i] = p;
            return r;
        }

        private static bool Bit(int x, int i) => ((x >> i) & 1) != 0;

        private void DrawFormatBits(int mask)
        {
            int data = FormatBits[(int)ecl] << 3 | mask, rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;
            for (int i = 0; i <= 5; i++) SetF(8, i, Bit(bits, i));
            SetF(8, 7, Bit(bits, 6)); SetF(8, 8, Bit(bits, 7)); SetF(7, 8, Bit(bits, 8));
            for (int i = 9; i < 15; i++) SetF(14 - i, 8, Bit(bits, i));
            for (int i = 0; i < 8; i++) SetF(Size - 1 - i, 8, Bit(bits, i));
            for (int i = 8; i < 15; i++) SetF(8, Size - 15 + i, Bit(bits, i));
            SetF(8, Size - 8, true);
        }

        private void DrawVersion()
        {
            if (Version < 7) return;
            int rem = Version;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = Version << 12 | rem;
            for (int i = 0; i < 18; i++)
            {
                bool b = Bit(bits, i);
                int a = Size - 11 + i % 3, c = i / 3;
                SetF(a, c, b); SetF(c, a, b);
            }
        }

        // ---- data ----
        private static int NumRawDataModules(int ver)
        {
            int r = (16 * ver + 128) * ver + 64;
            if (ver >= 2)
            {
                int n = ver / 7 + 2;
                r -= (25 * n - 10) * n - 55;
                if (ver >= 7) r -= 36;
            }
            return r;
        }

        private static int NumDataCodewords(int ver, Ecc e) =>
            NumRawDataModules(ver) / 8 - EccPerBlock[(int)e, ver] * NumBlocks[(int)e, ver];

        private byte[] AddEccAndInterleave(byte[] data)
        {
            int numBlocks = NumBlocks[(int)ecl, Version], eccLen = EccPerBlock[(int)ecl, Version];
            int raw = NumRawDataModules(Version) / 8;
            int numShort = numBlocks - raw % numBlocks, shortLen = raw / numBlocks;
            var div = RsDivisor(eccLen);
            var blocks = new List<byte[]>();
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                int len = shortLen - eccLen + (i < numShort ? 0 : 1);
                var dat = new byte[len];
                Array.Copy(data, k, dat, 0, len);
                k += len;
                var ecc = RsRemainder(dat, div);
                var block = new byte[shortLen + 1];
                Array.Copy(dat, 0, block, 0, len);
                // short blocks: one padding byte before the ECC (skipped when interleaving)
                Array.Copy(ecc, 0, block, shortLen + 1 - eccLen, eccLen);
                blocks.Add(block);
            }
            var result = new List<byte>(raw);
            for (int i = 0; i < shortLen + 1; i++)
                for (int j = 0; j < blocks.Count; j++)
                    if (i != shortLen - eccLen || j >= numShort) result.Add(blocks[j][i]);
            return result.ToArray();
        }

        private static byte[] RsDivisor(int degree)
        {
            var r = new byte[degree];
            r[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    r[j] = (byte)Mul(r[j], root);
                    if (j + 1 < degree) r[j] ^= r[j + 1];
                }
                root = Mul(root, 0x02);
            }
            return r;
        }

        private static byte[] RsRemainder(byte[] data, byte[] div)
        {
            var r = new byte[div.Length];
            foreach (var b in data)
            {
                int factor = b ^ r[0];
                Array.Copy(r, 1, r, 0, r.Length - 1);
                r[r.Length - 1] = 0;
                for (int i = 0; i < r.Length; i++) r[i] ^= (byte)Mul(div[i], factor);
            }
            return r;
        }

        private static int Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--) { z = (z << 1) ^ ((z >> 7) * 0x11D); z ^= ((y >> i) & 1) * x; }
            return z & 0xFF;
        }

        private void DrawCodewords(byte[] data)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < Size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vert : vert;
                        if (!isFunction[y, x] && i < data.Length * 8)
                        {
                            modules[y, x] = Bit(data[i >> 3], 7 - (i & 7));
                            i++;
                        }
                    }
            }
        }

        private void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    bool inv;
                    switch (mask)
                    {
                        case 0: inv = (x + y) % 2 == 0; break;
                        case 1: inv = y % 2 == 0; break;
                        case 2: inv = x % 3 == 0; break;
                        case 3: inv = (x + y) % 3 == 0; break;
                        case 4: inv = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: inv = x * y % 2 + x * y % 3 == 0; break;
                        case 6: inv = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: inv = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (inv && !isFunction[y, x]) modules[y, x] = !modules[y, x];
                }
        }

        // Simplified penalty (runs, 2×2 blocks, balance) — enough to pick a well-scannable mask.
        private long Penalty()
        {
            long p = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < Size; a++)
                {
                    int run = 1;
                    for (int b = 1; b < Size; b++)
                    {
                        bool cur = pass == 0 ? modules[a, b] : modules[b, a], prev = pass == 0 ? modules[a, b - 1] : modules[b - 1, a];
                        if (cur == prev) { run++; if (run == 5) p += 3; else if (run > 5) p++; }
                        else run = 1;
                    }
                }
            int dark = 0;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (modules[y, x]) dark++;
                    if (x < Size - 1 && y < Size - 1)
                    {
                        bool c = modules[y, x];
                        if (c == modules[y, x + 1] && c == modules[y + 1, x] && c == modules[y + 1, x + 1]) p += 3;
                    }
                }
            int total = Size * Size;
            p += (Math.Abs(dark * 20 - total * 10) + total - 1) / total * 10 - 10;
            return p;
        }
    }
}
