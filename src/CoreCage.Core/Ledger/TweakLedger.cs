using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CoreCage.Core.Ledger
{
    /// <summary>
    /// Persists the Tweak Ledger to JSON at %LOCALAPPDATA%\CoreCage\ledger.json (default; the path is
    /// constructor-injectable so no test ever touches the real file — mirrors GamingMode's
    /// mode-state.json posture, including its no-throw error handling: a missing file is an empty
    /// ledger, a corrupted file is an empty ledger, neither is ever an exception).
    ///
    /// The ledger tracks *current state per tweak*, not a full history log: <see cref="Record"/>
    /// replaces any existing entry with the same <see cref="LedgerEntry.TweakId"/> rather than
    /// appending a duplicate. That's what lets GamingMode.ApplyAsync record an un-benchmarked row
    /// ("not yet benchmarked") and a later "Prove it" run replace it in place with the measured
    /// numbers, instead of leaving the UI to disambiguate two rows for the same tweak.
    /// </summary>
    public sealed class TweakLedger
    {
        private readonly string _path;
        private readonly List<LedgerEntry> _entries;

        public TweakLedger(string path) : this(path, new List<LedgerEntry>())
        {
        }

        private TweakLedger(string path, List<LedgerEntry> entries)
        {
            _path = path;
            _entries = entries;
        }

        /// <summary>Current ledger rows, most-recently-recorded order.</summary>
        public IReadOnlyList<LedgerEntry> Entries => _entries;

        /// <summary>Adds a new row, or replaces the existing row for the same TweakId.</summary>
        public void Record(LedgerEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            int idx = _entries.FindIndex(e => string.Equals(e.TweakId, entry.TweakId, StringComparison.Ordinal));
            if (idx >= 0) _entries[idx] = entry;
            else _entries.Add(entry);
        }

        /// <summary>Flips Active to false for the row with this TweakId. No-op (never throws) if the
        /// TweakId isn't in the ledger.</summary>
        public void Deactivate(string tweakId)
        {
            int idx = _entries.FindIndex(e => string.Equals(e.TweakId, tweakId, StringComparison.Ordinal));
            if (idx < 0) return;
            _entries[idx] = _entries[idx] with { Active = false };
        }

        /// <summary>Writes the current entries to the injected path. Never throws — a failed save is
        /// logged, not fatal to whatever mode-apply/revert/benchmark flow triggered it. Atomic: writes
        /// to a "<path>.tmp" sibling first, then <see cref="File.Move(string, string, bool)"/>s it into
        /// place, so a crash mid-write never leaves a half-written ledger.json for Load to silently
        /// discard (Load treats a corrupted file the same as an empty one).</summary>
        public void Save()
        {
            string tmpPath = _path + ".tmp";
            try
            {
                string? dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(tmpPath, JsonConvert.SerializeObject(_entries, Formatting.Indented));
                File.Move(tmpPath, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                Logger.LogError("TweakLedger.Save failed", ex);
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* best-effort cleanup */ }
            }
        }

        /// <summary>Loads the ledger from <paramref name="path"/>. A missing file or a corrupted one
        /// both come back as an empty ledger (matching GamingMode.LoadState) — never an exception.</summary>
        public static TweakLedger Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new TweakLedger(path);
                var entries = JsonConvert.DeserializeObject<List<LedgerEntry>>(File.ReadAllText(path));
                return new TweakLedger(path, entries ?? new List<LedgerEntry>());
            }
            catch (Exception ex)
            {
                Logger.LogError("TweakLedger.Load failed", ex);
                return new TweakLedger(path);
            }
        }

        /// <summary>Default production path: %LOCALAPPDATA%\CoreCage\ledger.json.</summary>
        public static string DefaultPath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoreCage", "ledger.json");
    }
}
