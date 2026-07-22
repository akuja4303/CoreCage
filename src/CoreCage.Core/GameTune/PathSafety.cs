using System;
using System.Collections.Generic;
using System.IO;

namespace CoreCage.Core.GameTune
{
    /// <summary>Defense-in-depth path checks for GameTune writes. A config write is only ever
    /// allowed to a fully-resolved path that sits under one of the profile's declared safe roots
    /// and is NOT under a known game-install marker (anti-cheat-protected territory).</summary>
    public static class PathSafety
    {
        private static readonly string[] InstallMarkers =
            { @"\steamapps\", @"\Epic Games\", @"\Program Files\", @"\Program Files (x86)\" };

        public static string Expand(string pathWithEnv) =>
            Environment.ExpandEnvironmentVariables(pathWithEnv ?? "");

        public static bool IsSafe(string resolvedPath, IReadOnlyList<string> safeRoots)
        {
            if (string.IsNullOrWhiteSpace(resolvedPath) || safeRoots == null) return false;
            string full;
            try { full = Path.GetFullPath(resolvedPath); }
            catch { return false; }

            foreach (var marker in InstallMarkers)
                if (full.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;

            foreach (var root in safeRoots)
            {
                var r = Path.GetFullPath(Expand(root)).TrimEnd('\\');
                if (full.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
