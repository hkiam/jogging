using System;
using System.IO;

namespace Jogging.Core
{
    /// <summary>
    /// Crash-safe files: <see cref="Write"/> writes a temp file and swaps it in atomically, keeping the
    /// previous version as <c>.bak</c>; <see cref="Read"/> falls back to that backup when the file is
    /// missing or doesn't parse. A crash mid-write can therefore never cost more than the last change.
    /// Plain .NET (tested by SafeFileCheck).
    /// </summary>
    public static class SafeFile
    {
        public static string BackupOf(string path) => path + ".bak";

        public static void Write(string path, string text)
        {
            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            {
                var bytes = new System.Text.UTF8Encoding(false).GetBytes(text);
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true); // on disk before it replaces the old version (no empty file after a power cut)
            }
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, BackupOf(path)); return; }
                catch (PlatformNotSupportedException) { } // fall through: copy backup, then move
                catch (IOException) { }
                File.Copy(path, BackupOf(path), true);
                File.Delete(path);
            }
            File.Move(tmp, path);
        }

        /// <summary>Delete a file together with its backup and a leftover temp file.</summary>
        public static void Delete(string path)
        {
            foreach (var p in new[] { path, BackupOf(path), path + ".tmp" })
                if (File.Exists(p)) File.Delete(p);
        }

        /// <summary>
        /// A backup without its file is an orphan (the file was deleted) — unless a temp file is left
        /// too: then a write was interrupted between removing the old file and moving the new one in.
        /// </summary>
        public static bool InterruptedWrite(string path) => !File.Exists(path) && File.Exists(BackupOf(path)) && File.Exists(path + ".tmp");

        /// <summary>
        /// Parse the file, or its backup if the file is missing/corrupt. <paramref name="parse"/> returns
        /// null (or throws) for unusable content. Returns null if neither works.
        /// </summary>
        public static T Read<T>(string path, Func<string, T> parse) where T : class
        {
            foreach (var p in new[] { path, BackupOf(path) })
            {
                try
                {
                    if (!File.Exists(p)) continue;
                    var v = parse(File.ReadAllText(p));
                    if (v != null) return v;
                }
                catch { /* try the next one */ }
            }
            return null;
        }
    }
}
