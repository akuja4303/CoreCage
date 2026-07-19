using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using ValveKeyValue;

namespace CoreCage.Core.Profiles
{
    /// <summary>An installed Steam game discovered from its app manifest.</summary>
    public class InstalledGame
    {
        public string AppId { get; set; } = "";
        public string Name { get; set; } = "";
        public string InstallDir { get; set; } = ""; // full path under steamapps\common when resolved
    }

    /// <summary>
    /// Enumerates installed Steam games by parsing <c>libraryfolders.vdf</c> + <c>appmanifest_*.acf</c>
    /// with ValveKeyValue. The parse methods take a <see cref="Stream"/> so they're unit-testable; the
    /// orchestration (registry lookup + file scan) is best-effort and never throws.
    /// </summary>
    public static class SteamGames
    {
        /// <summary>Locates the Steam install directory from the registry, or null.</summary>
        public static string? FindSteamPath()
        {
            try
            {
                using RegistryKey? k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (k?.GetValue("SteamPath") is string p && p.Length > 0) return p.Replace('/', '\\');
            }
            catch { }
            try
            {
                using RegistryKey? k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                if (k?.GetValue("InstallPath") is string p && p.Length > 0) return p;
            }
            catch { }
            return null;
        }

        /// <summary>Parses libraryfolders.vdf, yielding each library's root path.</summary>
        public static IReadOnlyList<string> ParseLibraryFolders(Stream vdf)
        {
            var paths = new List<string>();
            KVObject root = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(vdf);
            foreach (KVObject lib in root.Children)
            {
                string? path = Child(lib, "path");
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }
            return paths;
        }

        /// <summary>Parses one appmanifest_*.acf into an <see cref="InstalledGame"/> (InstallDir is the raw folder name).</summary>
        public static InstalledGame? ParseAppManifest(Stream acf)
        {
            KVObject root = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(acf);
            string? appid = Child(root, "appid");
            string? name = Child(root, "name");
            string? installdir = Child(root, "installdir");
            if (appid == null && name == null) return null;
            return new InstalledGame { AppId = appid ?? "", Name = name ?? "", InstallDir = installdir ?? "" };
        }

        /// <summary>Discovers all installed games across every Steam library. Best-effort.</summary>
        public static IReadOnlyList<InstalledGame> GetInstalledGames()
        {
            var games = new List<InstalledGame>();
            string? steam = FindSteamPath();
            if (steam == null) return games;

            var libraries = new List<string> { steam };
            string libVdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libVdf))
            {
                try { using FileStream fs = File.OpenRead(libVdf); libraries.AddRange(ParseLibraryFolders(fs)); }
                catch (Exception ex) { Logger.LogError("Parsing libraryfolders.vdf failed", ex); }
            }

            foreach (string lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string steamapps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(steamapps)) continue;
                foreach (string acf in Directory.GetFiles(steamapps, "appmanifest_*.acf"))
                {
                    try
                    {
                        using FileStream fs = File.OpenRead(acf);
                        InstalledGame? g = ParseAppManifest(fs);
                        if (g != null)
                        {
                            if (g.InstallDir.Length > 0)
                                g.InstallDir = Path.Combine(steamapps, "common", g.InstallDir);
                            games.Add(g);
                        }
                    }
                    catch { }
                }
            }
            Logger.Log($"Steam scan: {games.Count} installed games across {libraries.Count} libraries");
            return games;
        }

        private static string? Child(KVObject o, string name) =>
            o.Children?.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
              ?.Value?.ToString();
    }
}
