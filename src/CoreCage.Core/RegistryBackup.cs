using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace CoreCage.Core
{
    /// <summary>
    /// Snapshots registry values before CoreCage mutates them, and restores them on demand —
    /// the per-tweak "undo" that complements the whole-system <see cref="RestorePoint"/>. Snapshots
    /// are stored as JSON under %LOCALAPPDATA%\CoreCage\Backups\&lt;label&gt;.json. Restoring a value
    /// that didn't exist at snapshot time deletes it (returning the key to its original state).
    /// All operations are best-effort and never throw.
    /// </summary>
    public static class RegistryBackup
    {
        private static readonly string BackupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoreCage", "Backups");

        /// <summary>The directory snapshots live in. Exposed so callers (the Big Red Button) restore
        /// from the SAME place Snapshot writes to — a prior path mismatch made snapshot-restore a no-op.</summary>
        public static string BackupDirectory => BackupDir;

        public sealed class Entry
        {
            public string Hive   { get; set; } = "";   // "HKLM" | "HKCU" | "HKCR" | "HKU" | "HKCC"
            public string SubKey { get; set; } = "";
            public string Name   { get; set; } = "";    // "" = the key's default value
            public bool   Existed { get; set; }
            public string? Kind  { get; set; }          // RegistryValueKind name
            public object? Value { get; set; }
        }

        /// <summary>Reads each target's current value/kind and writes a JSON snapshot under <paramref name="label"/>.</summary>
        public static void Snapshot(string label, IEnumerable<(string hive, string subKey, string name)> targets)
        {
            try
            {
                Directory.CreateDirectory(BackupDir);
                var entries = new List<Entry>();
                foreach (var (hive, subKey, name) in targets)
                {
                    var e = new Entry { Hive = hive, SubKey = subKey, Name = name };
                    try
                    {
                        using RegistryKey baseKey = OpenBase(hive);
                        using RegistryKey? key = baseKey.OpenSubKey(subKey, writable: false);
                        object? val = key?.GetValue(name, null);
                        if (key != null && val != null)
                        {
                            e.Existed = true;
                            e.Kind    = key.GetValueKind(name).ToString();
                            e.Value   = val;
                        }
                    }
                    catch { /* unreadable target → record as not-existing so restore deletes it */ }
                    entries.Add(e);
                }

                File.WriteAllText(SnapshotPath(label),
                    JsonConvert.SerializeObject(entries, Formatting.Indented));
                Logger.Log($"Registry snapshot saved: {label} ({entries.Count} values)");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Registry snapshot '{label}' failed", ex);
            }
        }

        /// <summary>Restores every value recorded in the named snapshot. Returns true if applied.</summary>
        public static bool Restore(string label)
        {
            string path = SnapshotPath(label);
            if (!File.Exists(path)) { Logger.Log($"No registry snapshot '{label}' to restore"); return false; }

            try
            {
                var entries = JsonConvert.DeserializeObject<List<Entry>>(File.ReadAllText(path))
                              ?? new List<Entry>();
                int restored = 0;
                foreach (Entry e in entries)
                {
                    try
                    {
                        using RegistryKey baseKey = OpenBase(e.Hive);
                        if (e.Existed)
                        {
                            using RegistryKey key = baseKey.CreateSubKey(e.SubKey, writable: true)!;
                            key.SetValue(e.Name, Coerce(e.Value, e.Kind), ParseKind(e.Kind));
                            restored++;
                        }
                        else
                        {
                            using RegistryKey? key = baseKey.OpenSubKey(e.SubKey, writable: true);
                            if (key?.GetValue(e.Name, null) != null) { key.DeleteValue(e.Name, throwOnMissingValue: false); restored++; }
                        }
                    }
                    catch (Exception inner) { Logger.LogError($"Restore of {e.Hive}\\{e.SubKey}\\{e.Name} failed", inner); }
                }
                Logger.Log($"Registry snapshot '{label}' restored ({restored}/{entries.Count} values)");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Registry restore '{label}' failed", ex);
                return false;
            }
        }

        public static bool HasSnapshot(string label) => File.Exists(SnapshotPath(label));

        /// <summary>Restores every snapshot whose label starts with <paramref name="prefix"/> (e.g. "rigopt-").
        /// Reads from <see cref="BackupDirectory"/> — the same dir Snapshot writes to. Returns the count
        /// of snapshots successfully restored. Best-effort; never throws.</summary>
        public static int RestoreAllWithPrefix(string prefix)
        {
            int restored = 0;
            try
            {
                if (!Directory.Exists(BackupDir)) return 0;
                foreach (string file in Directory.GetFiles(BackupDir, prefix + "*.json"))
                {
                    string label = Path.GetFileNameWithoutExtension(file);
                    try { if (Restore(label)) restored++; } catch { /* one bad snapshot must not abort the rest */ }
                }
            }
            catch (Exception ex) { Logger.LogError($"RestoreAllWithPrefix('{prefix}') failed", ex); }
            return restored;
        }

        private static string SnapshotPath(string label) =>
            Path.Combine(BackupDir, SanitizeFileName(label) + ".json");

        private static RegistryKey OpenBase(string hive) => hive.ToUpperInvariant() switch
        {
            "HKLM" => Registry.LocalMachine,
            "HKCU" => Registry.CurrentUser,
            "HKCR" => Registry.ClassesRoot,
            "HKU"  => Registry.Users,
            "HKCC" => Registry.CurrentConfig,
            _      => throw new ArgumentException($"Unknown hive '{hive}'"),
        };

        private static RegistryValueKind ParseKind(string? kind) =>
            Enum.TryParse(kind, out RegistryValueKind k) ? k : RegistryValueKind.String;

        // Newtonsoft deserializes JSON numbers to long and arrays to JArray; coerce back to the
        // concrete CLR type the registry kind expects.
        private static object Coerce(object? value, string? kind)
        {
            RegistryValueKind k = ParseKind(kind);
            try
            {
                return k switch
                {
                    RegistryValueKind.DWord       => Convert.ToInt32(value),
                    RegistryValueKind.QWord       => Convert.ToInt64(value),
                    RegistryValueKind.MultiString => ToStringArray(value),
                    RegistryValueKind.Binary      => ToByteArray(value),
                    _                             => value?.ToString() ?? "",
                };
            }
            catch { return value?.ToString() ?? ""; }
        }

        private static string[] ToStringArray(object? value)
        {
            if (value is Newtonsoft.Json.Linq.JArray arr) return arr.ToObject<string[]>() ?? Array.Empty<string>();
            if (value is string[] s) return s;
            return Array.Empty<string>();
        }

        private static byte[] ToByteArray(object? value)
        {
            if (value is Newtonsoft.Json.Linq.JArray arr) return arr.ToObject<byte[]>() ?? Array.Empty<byte>();
            if (value is byte[] b) return b;
            return Array.Empty<byte>();
        }

        private static string SanitizeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
