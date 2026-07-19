using System;
using System.Collections.Generic;

namespace CoreCage.Core.Profiles
{
    /// <summary>Pure foreground-exe → profile matching. No I/O — unit-testable.</summary>
    public static class ProfileMatcher
    {
        /// <summary>Returns the first profile whose ExeName matches the foreground exe, or null.</summary>
        public static GameProfile? Match(string foregroundExe, IEnumerable<GameProfile> profiles)
        {
            if (string.IsNullOrWhiteSpace(foregroundExe) || profiles == null) return null;
            string norm = Normalize(foregroundExe);
            foreach (GameProfile p in profiles)
            {
                if (string.IsNullOrWhiteSpace(p.ExeName)) continue;
                if (Normalize(p.ExeName) == norm) return p;
            }
            return null;
        }

        /// <summary>Lowercases, strips any directory and a trailing ".exe" so "C:\X\Game.exe" == "game".</summary>
        public static string Normalize(string exe)
        {
            if (string.IsNullOrWhiteSpace(exe)) return "";
            exe = exe.Trim();
            int slash = exe.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0) exe = exe.Substring(slash + 1);
            if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe = exe.Substring(0, exe.Length - 4);
            return exe.ToLowerInvariant();
        }
    }
}
