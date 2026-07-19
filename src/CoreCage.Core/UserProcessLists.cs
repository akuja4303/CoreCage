using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoreCage.Core
{
    /// <summary>User-curated per-mode process lists. Pure helpers (normalize/dedupe/migrate/IsListedIn)
    /// are unit-tested; an in-memory cache (set from settings.json on load) backs the runtime IsListed
    /// used by the boost/throttle/kill paths.</summary>
    public static class UserProcessLists
    {
        private static List<string> _gaming = new List<string>();

        /// <summary>Bare, lowercased process name with no directory and no ".exe" — matches Process.ProcessName.</summary>
        public static string Normalize(string nameOrPath)
        {
            if (string.IsNullOrWhiteSpace(nameOrPath)) return "";
            string name = nameOrPath.Trim();
            try { name = Path.GetFileName(name); } catch { /* keep as-is on invalid path chars */ }
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name.Trim().ToLowerInvariant();
        }

        /// <summary>Normalizes and adds to the list if non-blank and not already present (case-insensitive).</summary>
        public static void AddNormalized(List<string> list, string nameOrPath)
        {
            string n = Normalize(nameOrPath);
            if (n.Length == 0) return;
            if (!list.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase)))
                list.Add(n);
        }

        /// <summary>True if processName (e.g. Process.ProcessName) matches an entry, case-insensitive.</summary>
        public static bool IsListedIn(IEnumerable<string> list, string processName)
        {
            string n = Normalize(processName);
            return n.Length > 0 && list.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>One-time migration: if gaming is empty and a legacy whitelist exists, seed gaming from it.
        /// Returns true if it changed anything (caller should persist).</summary>
        public static bool Migrate(List<string> legacyWhitelist, List<string> gaming)
        {
            if (gaming.Count > 0 || legacyWhitelist == null || legacyWhitelist.Count == 0) return false;
            foreach (var w in legacyWhitelist) AddNormalized(gaming, w);
            return true;
        }

        // Runtime cache (set by MainWindow after loading settings.json)
        public static void SetLists(List<string> gaming)
        {
            _gaming = gaming ?? new List<string>();
        }

        public static List<string> GetList(string mode) =>
            string.Equals(mode, "gaming", StringComparison.OrdinalIgnoreCase) ? _gaming :
            new List<string>();

        /// <summary>Runtime check used by boost/throttle/kill: is processName in the cached list for mode?</summary>
        public static bool IsListed(string mode, string processName) => IsListedIn(GetList(mode), processName);
    }
}
